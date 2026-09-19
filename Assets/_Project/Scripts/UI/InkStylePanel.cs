using System.Collections.Generic;
using UnityEngine;
using InkWash.Rendering;

namespace InkWash.UI
{
    /// <summary>
    /// 水墨风格参数面板（运行时 IMGUI）。按 <b>F1</b> 开关，<b>1~4</b> 直接切阶段。
    ///
    /// 为什么要在运行时做面板，而不是让人去 Inspector 里调：
    /// 水墨的"味道"是**一眼定生死**的 —— 同一个参数静态截图里看着合适，
    /// 动起来可能完全不对（例如飞白太强会让画面一直在"闪"）。
    /// 放在运行时就能边跑边调、立刻看到动态效果。
    ///
    /// 五个阶段（论文对照图的顺序，每步只加一层）：
    ///   1 白模（原 PBR） → 2 水墨量化光照 → 3 ＋飞白墨线 → 4 ＋宣纸底纹 → 5 ＋墨晕扩散
    ///
    /// 为什么墨晕是**追加的第 5 层**而不是插进前四层：§9.14 的四阶段对照图
    /// （①白模 → ④宣纸）已经是论文里"逐层叠加"的核心证据，改动它的含义会让那组图与报告对不上。
    /// 墨晕接在后面，前四个阶段的画面**逐像素不变**（验收会重跑 S4 的逐层像素差来复核）。
    ///
    /// ★ 本面板**不写任何资产**：
    ///   * 材质全走 `new Material(...)` 的运行时实例（写 sharedMaterial 会把改动持久化）；
    ///   * 两个 RendererFeature 的参数写进 <see cref="InkStyleRegistry"/> 的运行时覆盖，
    ///     不碰渲染器资产（否则"跑一次 Play 就把工程改了"，而且没人知道是谁改的）。
    /// </summary>
    public class InkStylePanel : MonoBehaviour
    {
        [Header("调参用的水墨材质（通常是主角那份）")]
        public Material inkMaterial;

        [Header("显示")]
        public bool visible = false;    // 第三十八轮：调试面板默认隐藏（F1 唤出），玩家画面不被占
        public KeyCode toggleKey = KeyCode.F1;

        [Header("状态（只读）")]
        [SerializeField, Range(0, 4)] private int _stage = 4;   // 第三十八轮：出厂五层全开（含墨晕）

        // ---------------- 换材质：自注册表 ----------------
        private class Entry
        {
            public InkMaterialSwap swap;
            public Material[] runtime;      // 水墨材质的运行时实例；null = 还没建
            public Family family;           // 它属于哪一族（按源材质分）
        }

        /// <summary>
        /// 「一族材质」= 同一个源材质派生出来的全部运行时实例（主角一份、敌人一份…）。
        /// 参数面板按族编辑：改主角的值只落回主角那一族，不越界去改敌人。
        /// </summary>
        private class Family
        {
            public Material source;         // 建这些实例时用的源材质（判族依据）
            public Material edit;           // 面板读写的那一份运行时副本
            public readonly List<Entry> members = new List<Entry>();
        }

        private static readonly List<Family> Families = new List<Family>();

        private static readonly List<Entry> Entries = new List<Entry>();
        private static int s_stage = 4;

        private Material _edit;
        private int _editFamily = -1;       // 当前编辑的是第几族（-1 = 没得编）
        private bool _dirty;
        private float _fps, _fpsAccum;
        private int _fpsFrames;
        private Vector2 _scroll;
        private GUIStyle _title, _rowStyle;

        public int Stage => _stage;
        public static int CurrentStage => s_stage;
        /// <summary>换材质登记了几家（验收脚本用：应当 ≥ 主角 1 家）。</summary>
        public static int RegisteredCount => Entries.Count;
        /// <summary>参数族数（一个源材质一族；验收脚本用：应当 ≥ 2，主角与敌人各一族）。</summary>
        public static int FamilyCount => Families.Count;

        /// <summary>当前在编第几族的名字（验收脚本用；空串 = 没得编）。</summary>
        public string EditingFamilyName
        {
            get { return _editFamily >= 0 && _editFamily < Families.Count ? FamilyName(Families[_editFamily]) : ""; }
        }

        // ---------------- Shader 属性 ID ----------------
        private static readonly int IdBands = Shader.PropertyToID("_Bands");
        private static readonly int IdSoftness = Shader.PropertyToID("_BandSoftness");
        // 墨分五色：三个锚点色（与 InkSurface / InkCharacter 的属性名保持一致）
        private static readonly int IdInkDark  = Shader.PropertyToID("_InkDark");
        private static readonly int IdInkMid   = Shader.PropertyToID("_InkMid");
        private static readonly int IdInkLight = Shader.PropertyToID("_InkLight");
        private static readonly int IdLadderSkew = Shader.PropertyToID("_LadderSkew");
        private static readonly int IdBandBias   = Shader.PropertyToID("_BandBias");
        private static readonly int IdInkDensity = Shader.PropertyToID("_InkDensity");
        private static readonly int IdBrushStrength = Shader.PropertyToID("_BrushStrength");
        private static readonly int IdBrushScale = Shader.PropertyToID("_BrushScale");
        private static readonly int IdBrushWorld = Shader.PropertyToID("_BrushUvFromWorld");
        private static readonly int IdRimStrength = Shader.PropertyToID("_RimStrength");
        private static readonly int IdRimPower = Shader.PropertyToID("_RimPower");
        private static readonly int IdSpecStrength = Shader.PropertyToID("_SpecStrength");
        private static readonly int IdSpecBands = Shader.PropertyToID("_SpecBands");

        private static readonly int[] FloatProps =
        {
            IdBands, IdSoftness, IdBandBias, IdLadderSkew, IdInkDensity,
            IdBrushStrength, IdBrushScale, IdBrushWorld,
            IdRimStrength, IdRimPower, IdSpecBands, IdSpecStrength,
        };
        private static readonly int[] ColorProps = { IdInkDark, IdInkMid, IdInkLight };

        // ==================================================================
        // 自注册接口
        // ==================================================================
        public static void Register(InkMaterialSwap swap)
        {
            if (swap == null) return;
            foreach (var e in Entries) if (e.swap == swap) return;
            var entry = new Entry { swap = swap };
            Entries.Add(entry);
            EnsureFamilyFor(entry);     // ★ 先按源材质判族，再进 ApplyToEntry
            ApplyToEntry(entry);        // 新刷出来的敌人立刻跟上当前阶段
        }

        public static void Unregister(InkMaterialSwap swap)
        {
            if (swap == null) return;
            for (int i = Entries.Count - 1; i >= 0; i--)
            {
                if (Entries[i].swap != swap) continue;
                DetachFromFamily(Entries[i]);
                DestroyRuntime(Entries[i]);
                Entries.RemoveAt(i);
            }
            LeaveEmptyFamilies();
        }

        /// <summary>活着的面板（Unregister 是静态的，但要改实例上的"正在编第几族"）。</summary>
        private static readonly List<InkStylePanel> Panels = new List<InkStylePanel>();

        // ==================================================================
        // 材质族：同源材质 → 同一族
        // ==================================================================
        /// <summary>取「用来建运行时实例的源材质（第 0 槽）」—— 判族的键。</summary>
        private static Material SourceOf(InkMaterialSwap swap)
        {
            if (swap == null || swap.inkMaterials == null || swap.inkMaterials.Length == 0) return null;
            return swap.inkMaterials[0];
        }

        private static Family FindFamily(Material source)
        {
            if (source == null) return null;
            foreach (var f in Families) if (f.source == source) return f;
            return null;
        }

        private static void EnsureFamilyFor(Entry e)
        {
            var src = SourceOf(e.swap);
            if (src == null) return;
            var fam = FindFamily(src);
            if (fam == null)
            {
                fam = new Family { source = src, edit = new Material(src) };
                fam.edit.hideFlags = HideFlags.HideAndDontSave;
                Families.Add(fam);
            }
            if (!fam.members.Contains(e)) fam.members.Add(e);
            e.family = fam;
        }

        private static void DetachFromFamily(Entry e)
        {
            if (e.family == null) return;
            e.family.members.Remove(e);
            e.family = null;
        }

        /// <summary>某一族的实例全没了（敌人死光）就回收；空族留着只会白占面板行数。</summary>
        private static void LeaveEmptyFamilies()
        {
            for (int i = Families.Count - 1; i >= 0; i--)
            {
                if (Families[i].members.Count > 0) continue;
                var doomed = Families[i];
                if (doomed.edit != null) UnityEngine.Object.Destroy(doomed.edit);
                Families.RemoveAt(i);
                // 序号会左移：让每个面板重新对齐自己那一族
                foreach (var p in Panels) p.OnFamiliesChanged(i, doomed);
            }
        }

        private void OnFamiliesChanged(int removedIndex, Family removed)
        {
            // 序号会左移，两种情况要分：删的正好是我在编的 / 删的是我前面的
            if (removed != null && _edit == removed.edit)
            {
                _editFamily = -1;
                _edit = null;
            }
            else if (_editFamily > removedIndex) _editFamily--;

            if (_edit == null)
            {
                if (_editFamily < 0 && Families.Count > 0) EditFamily(0);
                else if (_editFamily >= 0 && _editFamily < Families.Count) _edit = Families[_editFamily].edit;
            }
        }

        /// <summary>切换正在编辑的族（面板上的按钮）。</summary>
        public void EditFamily(int index)
        {
            if (index < 0 || index >= Families.Count) return;
            if (_editFamily == index) return;
            _editFamily = index;
            _edit = Families[index].edit;
        }

        /// <summary>按源材质找族序号（找不到返回 -1）。</summary>
        public static int IndexOfFamily(Material source)
        {
            if (source == null) return -1;
            for (int i = 0; i < Families.Count; i++) if (Families[i].source == source) return i;
            return -1;
        }

        // ==================================================================
        // 生命周期
        // ==================================================================
        private void Awake()
        {
            // inkMaterial 只是"面板一开始指向哪一族"的入口；
            // 真正的参数副本由 EnsureFamilyFor 按源材质建，一族一份（主角一份、敌人一份）。
            ApplyStage(_stage);
            if (inkMaterial != null) EditFamily(IndexOfFamily(inkMaterial));
            if (_edit == null && Families.Count > 0) EditFamily(0);
        }

        private void OnEnable()
        {
            if (!Panels.Contains(this)) Panels.Add(this);
        }

        private void OnDisable()
        {
            Panels.Remove(this);
        }

        private void OnDestroy()
        {
            Panels.Remove(this);
            _edit = null;       // 不能 Destroy：edit 归 Family 所有，可能还有别的面板在用
        }

        private void Update()
        {
            // 惰性对齐：面板 Awake 时场上可能一个人都还没注册（Prefab 里的面板比角色先醒），
            // 所以这里每帧补一次 —— 有族了但还没选中，就选第一族。
            if (_edit == null && Families.Count > 0) EditFamily(0);

            _fpsAccum += Time.unscaledDeltaTime;
            _fpsFrames++;
            if (_fpsAccum >= 0.5f)
            {
                _fps = _fpsFrames / _fpsAccum;
                _fpsAccum = 0f;
                _fpsFrames = 0;
            }

            if (Input.GetKeyDown(toggleKey)) visible = !visible;
            if (Input.GetKeyDown(KeyCode.Alpha1)) ApplyStage(0);
            if (Input.GetKeyDown(KeyCode.Alpha2)) ApplyStage(1);
            if (Input.GetKeyDown(KeyCode.Alpha3)) ApplyStage(2);
            if (Input.GetKeyDown(KeyCode.Alpha4)) ApplyStage(3);
            if (Input.GetKeyDown(KeyCode.Alpha5)) ApplyStage(4);
        }

        /// <summary>切换阶段。每一阶段只在上一个之上**加一层**，对照图才说得清每层的贡献。</summary>
        public void ApplyStage(int stage)
        {
            _stage = Mathf.Clamp(stage, 0, 4);
            s_stage = _stage;

            foreach (var e in Entries) ApplyToEntry(e);
            // 走注册表的覆盖：不碰渲染器资产
            InkStyleRegistry.EdgeOn = _stage >= 2;
            InkStyleRegistry.PaperOn = _stage >= 3;
            InkStyleRegistry.BloomOn = _stage >= 4;
            // 阶段 ≥1 才需要读 Feature 参数；顺手把运行时覆盖建出来
            if (_stage >= 2) InkStyleRegistry.EnsureEdgeRuntime();
            if (_stage >= 3) InkStyleRegistry.EnsurePaperRuntime();
            if (_stage >= 4) InkStyleRegistry.EnsureBloomRuntime();

            if (_edit != null) Broadcast();
        }

        // ==================================================================
        // 换材质
        // ==================================================================
        private static void ApplyToEntry(Entry e)
        {
            if (e.swap == null || e.swap.target == null) return;

            if (s_stage == 0)
            {
                var lit = e.swap.litMaterials;
                if (lit != null && lit.Length > 0) e.swap.target.sharedMaterials = lit;
                return;
            }

            if (e.runtime == null) e.runtime = BuildRuntime(e.swap);
            if (e.runtime != null && e.runtime.Length > 0) e.swap.target.sharedMaterials = e.runtime;
        }

        private static Material[] BuildRuntime(InkMaterialSwap swap)
        {
            string problem;
            if (!swap.Validate(out problem))
                Debug.LogWarning("[InkStylePanel] " + swap.name + " 换材质配置有问题：" + problem);
            if (swap.inkMaterials == null || swap.inkMaterials.Length == 0) return null;

            var arr = new Material[swap.inkMaterials.Length];
            for (int i = 0; i < arr.Length; i++)
            {
                if (swap.inkMaterials[i] == null) continue;
                arr[i] = new Material(swap.inkMaterials[i]);
                arr[i].hideFlags = HideFlags.HideAndDontSave;
            }
            return arr;
        }

        private static void DestroyRuntime(Entry e)
        {
            if (e.runtime == null) return;
            foreach (var m in e.runtime) if (m != null) Destroy(m);
            e.runtime = null;
        }

        private void OnGUI()
        {
            if (!visible) return;
            if (_title == null)
            {
                _title = new GUIStyle(GUI.skin.label) { fontSize = 13, fontStyle = FontStyle.Bold, wordWrap = false };
                _rowStyle = new GUIStyle(GUI.skin.label) { wordWrap = false };
            }

            var rect = new Rect(12, 12, 356, Mathf.Min(800, Screen.height - 24));
            GUILayout.BeginArea(rect, GUI.skin.box);
            _scroll = GUILayout.BeginScrollView(_scroll);

            GUILayout.Label("墨刃 · 水墨风格参数（F1 隐藏 / 1-4 切阶段）", _title);
            GUILayout.Label(string.Format("FPS {0:F0}   阶段 {1}/5：{2}   换材质 {3} 家",
                _fps, _stage + 1, StageName(_stage), Entries.Count));

            GUILayout.Space(4);
            GUILayout.BeginHorizontal();
            if (GUILayout.Toggle(_stage == 0, "1 白模", GUI.skin.button)) ApplyStage(0);
            if (GUILayout.Toggle(_stage == 1, "2 量化", GUI.skin.button)) ApplyStage(1);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Toggle(_stage == 2, "3 +墨线", GUI.skin.button)) ApplyStage(2);
            if (GUILayout.Toggle(_stage == 3, "4 +宣纸", GUI.skin.button)) ApplyStage(3);
            if (GUILayout.Toggle(_stage == 4, "5 +墨晕", GUI.skin.button)) ApplyStage(4);
            GUILayout.EndHorizontal();

            // ★ 材质族选择：主角 / 各敌人各自一族，改谁只动谁。
            //   没有这一行的话，"调主角的墨"会顺手把所有敌人也涂成主角的墨。
            if (Families.Count > 0)
            {
                Section("调节对象（材质族）");
                for (int i = 0; i < Families.Count; i++)
                    if (GUILayout.Toggle(_editFamily == i, FamilyName(Families[i]) + "（" + Families[i].members.Count + "）",
                                         GUI.skin.button))
                        EditFamily(i);
            }

            if (_edit == null)
                GUILayout.Label("（没有可调对象：场上还没有换过材质的角色）");

            var m = _edit;
            if (m != null && m.HasProperty(IdBands))
            {
                Section("量化光照 · 墨分五色");
                S(m, "墨阶数（少=大写意）", IdBands, 1, 8);
                S(m, "阶间柔度", IdSoftness, 0.001f, 0.4f);
                // ★ _BandBias 是本轮最重要的旋钮：它决定"这个物体用几号墨"。
                //   画面有没有主次、明度带宽能不能撑开，全靠它在各材质间的分配。
                S(m, "基础墨阶偏移（负=更浓）", IdBandBias, -0.8f, 0.6f);
                S(m, "中间两级位置", IdLadderSkew, 0.1f, 0.9f);
                S(m, "贴图信息量", IdInkDensity, 0, 1);
                C(m, "焦墨（最暗）", IdInkDark);
                C(m, "重墨（中间）", IdInkMid);
                C(m, "清墨（近纸白）", IdInkLight);
                S(m, "轮廓墨强度", IdRimStrength, 0, 2);
                S(m, "轮廓收束", IdRimPower, 0.5f, 12);
                S(m, "高光阶数", IdSpecBands, 1, 6);
                S(m, "高光强度", IdSpecStrength, 0, 2);

                Section("角色 · 飞白");
                S(m, "飞白强度（0=光滑卡通）", IdBrushStrength, 0, 1);
                S(m, "飞白尺度", IdBrushScale, 1, 90);
                S(m, "飞白按世界坐标", IdBrushWorld, 0, 1);
            }

            var edge = InkStyleRegistry.Edge;
            var es = InkStyleRegistry.EdgeSettings;
            if (es != null)
            {
                Section("飞白墨线（屏幕空间）");
                InkStyleRegistry.EdgeOn = GUILayout.Toggle(InkStyleRegistry.EdgeEnabled, " 启用墨线");
                C("墨线颜色", () => es.edgeColor, v => { es.edgeColor = v; _dirty = true; });
                Sl("深度灵敏度", 0.1f, 60f, () => es.depthSensitivity, v => { es.depthSensitivity = v; _dirty = true; });
                Sl("深度死区", 0f, 6f, () => es.depthBias, v => { es.depthBias = v; _dirty = true; });
                Sl("法线灵敏度", 0.1f, 4f, () => es.normalSensitivity, v => { es.normalSensitivity = v; _dirty = true; });
                Sl("法线死区", 0f, 1f, () => es.normalBias, v => { es.normalBias = v; _dirty = true; });
                Sl("线宽", 0.5f, 4f, () => es.lineThickness, v => { es.lineThickness = v; _dirty = true; });
                Sl("墨线强度", 0f, 3f, () => es.lineStrength, v => { es.lineStrength = v; _dirty = true; });
                Sl("飞白强度", 0f, 1f, () => es.dryBrushStrength, v => { es.dryBrushStrength = v; _dirty = true; });
                Sl("飞白尺度", 0f, 64f, () => es.dryBrushScale, v => { es.dryBrushScale = v; _dirty = true; });
                Sl("断笔阈值", 0f, 1f, () => es.dryBrushBias, v => { es.dryBrushBias = v; _dirty = true; });
                Sl("远处淡出(米)", 5f, 200f, () => es.distanceFade, v => { es.distanceFade = v; _dirty = true; });
            }
            else if (edge == null)
            {
                Section("飞白墨线");
                GUILayout.Label("（渲染器资产里没装配 InkEdgeFeature）");
            }

            var ps = InkStyleRegistry.PaperSettings;
            if (ps != null)
            {
                Section("宣纸底纹");
                InkStyleRegistry.PaperOn = GUILayout.Toggle(InkStyleRegistry.PaperEnabled, " 启用宣纸");
                Sl("纸纹强度", 0f, 1f, () => ps.paperStrength, v => { ps.paperStrength = v; _dirty = true; });
                Sl("纸纹密度", 0.25f, 12f, () => ps.paperTiling, v => { ps.paperTiling = v; _dirty = true; });
                Sl("纸纹对比", 0.2f, 3f, () => ps.paperContrast, v => { ps.paperContrast = v; _dirty = true; });
                Sl("生宣颗粒", 0f, 1f, () => ps.grainStrength, v => { ps.grainStrength = v; _dirty = true; });
                Sl("底色着染", 0f, 1f, () => ps.tintStrength, v => { ps.tintStrength = v; _dirty = true; });
                Sl("边缘压暗", 0f, 1f, () => ps.vignette, v => { ps.vignette = v; _dirty = true; });
                Sl("墨色加深", 0f, 1f, () => ps.inkDeepen, v => { ps.inkDeepen = v; _dirty = true; });
            }

            var bs = InkStyleRegistry.BloomSettings;
            if (bs != null)
            {
                Section("墨晕扩散");
                InkStyleRegistry.BloomOn = GUILayout.Toggle(InkStyleRegistry.BloomEnabled, " 启用墨晕");
                Sl("暗度阈值（只晕更暗的部分）", 0f, 1f, () => bs.threshold, v => { bs.threshold = v; _dirty = true; });
                Sl("晕的强度", 0f, 2f, () => bs.strength, v => { bs.strength = v; _dirty = true; });
                Sl("近场半径(像素)", 0.5f, 24f, () => bs.radius1, v => { bs.radius1 = v; _dirty = true; });
                Sl("远场半径(像素)", 1f, 64f, () => bs.radius2, v => { bs.radius2 = v; _dirty = true; });
                Sl("远场权重", 0f, 1f, () => bs.farWeight, v => { bs.farWeight = v; _dirty = true; });
                Sl("纸纤维抖动", 0f, 0.6f, () => bs.jitter, v => { bs.jitter = v; _dirty = true; });
                Sl("墨色掺入", 0f, 1f, () => bs.tintAmount, v => { bs.tintAmount = v; _dirty = true; });
                Sl("整体墨雾", 0f, 1f, () => bs.veil, v => { bs.veil = v; _dirty = true; });
                C("晕的墨色", () => bs.inkColor, v => { bs.inkColor = v; _dirty = true; });
            }
            else if (InkStyleRegistry.Bloom == null)
            {
                Section("墨晕扩散");
                GUILayout.Label("（渲染器资产里没装配 InkBloomFeature）");
            }

            GUILayout.EndScrollView();
            GUILayout.EndArea();

            // 一次滑块拖动可能产生几十次变更，攒到帧末统一同步给所有换材质对象，
            // 而不是每次变更都遍历一遍（敌人多的时候那是 N×12 次 SetFloat）
            if (_dirty)
            {
                Broadcast();
                _dirty = false;
            }
        }

        public static string StageName(int s)
        {
            switch (s)
            {
                case 0: return "白模（原 PBR）";
                case 1: return "水墨量化光照";
                case 2: return "＋飞白墨线";
                case 3: return "＋宣纸底纹";
                default: return "＋墨晕扩散";
            }
        }

        /// <summary>族的显示名 = 源材质名去掉 M_/M_Ink_ 前缀，够辨认就行。</summary>
        private static string FamilyName(Family f)
        {
            if (f == null || f.source == null) return "（无名）";
            string n = f.source.name;
            if (n.StartsWith("M_Ink_")) n = n.Substring(6);
            else if (n.StartsWith("M_")) n = n.Substring(2);
            return n;
        }

        private void Section(string name)
        {
            GUILayout.Space(6);
            GUILayout.Label("— " + name + " —", _title);
        }

        // ---------------- 同步 ----------------
        /// <summary>
        /// 把 <c>_edit</c> 上的参数抄回**它自己那一族**的运行时实例。
        ///
        /// ★ 这里原来遍历 ALL entries，于是"调主角的墨阶"会把主角的值盖到每一个敌人头上
        ///   （主角 _BandBias +0.15 / _InkDensity 0.55 / 青墨 _InkDark 盖住了龙自己的
        ///    −0.42 / 0.25 / 赭墨）。龙因此变成"主角的黑剪纸"、颜色靠 _ChromaKeep 硬撑。
        ///   现在按族分发：改谁只动谁。
        /// </summary>
        private void Broadcast()
        {
            if (_edit == null) return;
            var fam = (_editFamily >= 0 && _editFamily < Families.Count) ? Families[_editFamily] : null;
            if (fam == null) return;
            foreach (var id in FloatProps) Sync(fam, id, true);
            foreach (var id in ColorProps) Sync(fam, id, false);
        }

        private void Sync(Family fam, int id, bool isFloat)
        {
            if (!fam.edit.HasProperty(id)) return;
            float f = isFloat ? fam.edit.GetFloat(id) : 0f;
            Color c = isFloat ? default : fam.edit.GetColor(id);
            foreach (var e in fam.members)          // ← 只遍历本族成员
            {
                if (e.runtime == null) continue;
                foreach (var mat in e.runtime)
                {
                    if (mat == null || !mat.HasProperty(id)) continue;
                    if (isFloat) mat.SetFloat(id, f); else mat.SetColor(id, c);
                }
            }
        }

        // ---------------- 滑块小工具 ----------------
        private void S(Material m, string label, int prop, float lo, float hi)
        {
            if (!m.HasProperty(prop)) return;
            float v = m.GetFloat(prop);
            float nv = Row(label, v, lo, hi);
            if (!Mathf.Approximately(v, nv)) { m.SetFloat(prop, nv); _dirty = true; }
        }

        private void C(Material m, string label, int prop)
        {
            if (!m.HasProperty(prop)) return;
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, _rowStyle, GUILayout.Width(150));
            var c = m.GetColor(prop);
            var nc = RgbRow(c);
            if (nc != c) { m.SetColor(prop, nc); _dirty = true; }
            GUILayout.EndHorizontal();
        }

        private static void Sl(string label, float lo, float hi, System.Func<float> get, System.Action<float> set)
        {
            float v = get();
            float nv = Row(label, v, lo, hi);
            if (!Mathf.Approximately(v, nv)) set(nv);
        }

        private void C(string label, System.Func<Color> get, System.Action<Color> set)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, _rowStyle, GUILayout.Width(150));
            var c = get();
            var nc = RgbRow(c);
            if (nc != c) set(nc);
            GUILayout.EndHorizontal();
        }

        private static float Row(string label, float v, float lo, float hi)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label + "  " + v.ToString("0.###"), RowStyle, GUILayout.Width(150));
            float nv = GUILayout.HorizontalSlider(v, lo, hi);
            GUILayout.EndHorizontal();
            return nv;
        }

        private static GUIStyle _rowStatic;
        private static GUIStyle RowStyle
        {
            get
            {
                if (_rowStatic == null) _rowStatic = new GUIStyle(GUI.skin.label) { wordWrap = false };
                return _rowStatic;
            }
        }

        private static Color RgbRow(Color c)
        {
            GUILayout.Label("R", RowStyle, GUILayout.Width(12));
            c.r = GUILayout.HorizontalSlider(c.r, 0f, 1f);
            GUILayout.Label("G", RowStyle, GUILayout.Width(12));
            c.g = GUILayout.HorizontalSlider(c.g, 0f, 1f);
            GUILayout.Label("B", RowStyle, GUILayout.Width(12));
            c.b = GUILayout.HorizontalSlider(c.b, 0f, 1f);
            return c;
        }
    }
}
