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
        public bool visible = true;
        public KeyCode toggleKey = KeyCode.F1;

        [Header("状态（只读）")]
        [SerializeField, Range(0, 4)] private int _stage = 3;

        // ---------------- 换材质：自注册表 ----------------
        private class Entry
        {
            public InkMaterialSwap swap;
            public Material[] runtime;      // 水墨材质的运行时实例；null = 还没建
        }

        private static readonly List<Entry> Entries = new List<Entry>();
        private static int s_stage = 3;

        private Material _edit;
        private bool _dirty;
        private float _fps, _fpsAccum;
        private int _fpsFrames;
        private Vector2 _scroll;
        private GUIStyle _title, _rowStyle;

        public int Stage => _stage;
        public static int CurrentStage => s_stage;
        /// <summary>换材质登记了几家（验收脚本用：应当 ≥ 主角 1 家）。</summary>
        public static int RegisteredCount => Entries.Count;

        // ---------------- Shader 属性 ID ----------------
        private static readonly int IdBands = Shader.PropertyToID("_Bands");
        private static readonly int IdSoftness = Shader.PropertyToID("_BandSoftness");
        private static readonly int IdInkColor = Shader.PropertyToID("_InkColor");
        private static readonly int IdPaperColor = Shader.PropertyToID("_PaperColor");
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
            IdBands, IdSoftness, IdInkDensity, IdBrushStrength, IdBrushScale, IdBrushWorld,
            IdRimStrength, IdRimPower, IdSpecBands, IdSpecStrength,
        };
        private static readonly int[] ColorProps = { IdInkColor, IdPaperColor };

        // ==================================================================
        // 自注册接口
        // ==================================================================
        public static void Register(InkMaterialSwap swap)
        {
            if (swap == null) return;
            foreach (var e in Entries) if (e.swap == swap) return;
            var entry = new Entry { swap = swap };
            Entries.Add(entry);
            ApplyToEntry(entry);        // 新刷出来的敌人立刻跟上当前阶段
        }

        public static void Unregister(InkMaterialSwap swap)
        {
            if (swap == null) return;
            for (int i = Entries.Count - 1; i >= 0; i--)
            {
                if (Entries[i].swap != swap) continue;
                DestroyRuntime(Entries[i]);
                Entries.RemoveAt(i);
            }
        }

        // ==================================================================
        // 生命周期
        // ==================================================================
        private void Awake()
        {
            if (inkMaterial != null)
            {
                _edit = new Material(inkMaterial);
                _edit.hideFlags = HideFlags.HideAndDontSave;
            }
            ApplyStage(_stage);
        }

        private void OnDestroy()
        {
            if (_edit != null) Destroy(_edit);
        }

        private void Update()
        {
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

            if (_edit == null)
                GUILayout.Label("（没有指定水墨材质，角色参数不可调）");

            var m = _edit;
            if (m != null && m.HasProperty(IdBands))
            {
                Section("角色 · 量化光照");
                S(m, "墨阶数（少=大写意）", IdBands, 1, 8);
                S(m, "阶间柔度", IdSoftness, 0.001f, 0.4f);
                S(m, "墨的浓度", IdInkDensity, 0, 1);
                C(m, "墨色（暗部）", IdInkColor);
                C(m, "纸色（受光）", IdPaperColor);
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

        private void Section(string name)
        {
            GUILayout.Space(6);
            GUILayout.Label("— " + name + " —", _title);
        }

        // ---------------- 同步 ----------------
        /// <summary>把 `_edit` 上的参数抄到所有运行时实例上（主角 + 全部敌人一起变）。</summary>
        private void Broadcast()
        {
            if (_edit == null) return;
            foreach (var id in FloatProps) Sync(id, true);
            foreach (var id in ColorProps) Sync(id, false);
        }

        private void Sync(int id, bool isFloat)
        {
            if (!_edit.HasProperty(id)) return;
            float f = isFloat ? _edit.GetFloat(id) : 0f;
            Color c = isFloat ? default : _edit.GetColor(id);
            foreach (var e in Entries)
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
