using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace InkWash.Effects
{
    /// <summary>
    /// 墨龙风暴特效：**青白细电弧**（沿脊骨链游走）+ **纯墨黑烟**（沿链 + 口部向前）。
    ///
    /// 论文 · E5 / 设计见 `Docs/墨龙特效设计.md`（状态→强度映射是那张表的唯一真相）。
    ///
    /// ══════════════════════════════════════════════════════════════════════
    ///  ★ 为什么是"场景单例 + 静态入口"，与 `InkHitVfx` 完全同构：
    ///    受击方（这里是墨龙）是运行时生成的，若特效挂在各自身上，
    ///    三波怪就是几十份池子 + 几十份材质实例。集中成一个管理器之后，
    ///    全场共用一个池、一份材质，且**玩法层不需要认识表现层**，只调一个静态入口。
    ///
    ///  ★ 惰性创建：`Ensure()` 只在**真的要画东西时**才被调用（见 `Drive`），
    ///    所以"龙在地面待机 / 没龙"时场景里不会多出一个空物体 —— 编辑期不脏场景文件。
    ///
    ///  ★ 表现层不得带崩玩法链路：所有静态入口不抛异常、不阻塞，
    ///    材质/Shader 拿不到时**只警告一次**，电弧/烟直接不画（而不是刷屏报错）。
    ///    入口带自愈：`if (!enabled) enabled = true;`（被禁用的 MonoBehaviour 不跑 Update，
    ///    这是本项目 `工程坑表` 二-5 记过的静默失效）。
    ///
    ///  ★ 不产生 GC：所有数组在**创建时一次性分配**，之后每帧只写内容（`Mesh.SetVertices` 等），
    ///    连顶点数都是创建时定死、永不改变。验收判据里有"稳态 0 B/帧"这一条。
    ///    为此电弧的"段数"是**每个池位固定**的（不是每条随机），这样拓扑与 UV 只需要建一次。
    /// ══════════════════════════════════════════════════════════════════════
    /// </summary>
    [DisallowMultipleComponent]
    public class DragonStormVfx : MonoBehaviour
    {
        /// <summary>驱动层每帧报过来的姿态。表现层把它翻译成"几条电弧 / 多浓的烟"。</summary>
        public enum Pose
        {
            Off = 0,        // 死亡 / 未起飞 ⇒ 全关
            Idle,           // 常态盘旋
            Tell,           // 俯冲预兆（蓄力）
            Dive,           // 俯冲（主战场）
            Strike,         // 扑咬 / 扫尾（峰值）
            Recover,        // 拉起
            Breath,         // 悬停吐息（口部向前喷烟）
            Roar            // 阶段演出的长啸
        }

        public static DragonStormVfx Instance { get; private set; }

        // ══════════════════════════════════════════════════════════════
        //  调参（全部放在这个**运行时创建**的组件上，不往龙的 prefab 里塞字段
        //  —— 那边每加一个 public 字段，`check_prefab_overrides.py` 就会报一条"未写"）
        // ══════════════════════════════════════════════════════════════

        [Header("电弧条数（设计定案，见 Docs/墨龙特效设计.md §三）")]
        [Tooltip("常态盘旋 2~3")]
        public float arcsIdle = 3f;
        public float arcsTell = 4f;
        [Tooltip("俯冲主战场 8")]
        public float arcsDive = 8f;
        [Tooltip("扑咬峰值 10")]
        public float arcsStrike = 10f;
        public float arcsRecover = 5f;
        public float arcsBreath = 3f;
        public float arcsRoar = 6f;
        [Tooltip("受击瞬时放电条数（0.25 s 后回落）")]
        public float arcsHit = 9f;
        [Tooltip("受击放电持续时间（秒）")]
        public float hitDuration = 0.25f;
        [Tooltip("条数变化的最小步进间隔（秒）——不做插值，条数是整数，太滑反而不像放电")]
        public float arcStepInterval = 0.08f;

        [Header("电弧形态")]
        [Tooltip("池容量。满了就丢弃（不扩容）")]
        public int arcPool = 14;
        [Tooltip("单条电弧每秒重新抖动的次数。★ 别调到帧率：60 Hz 抖动读起来是一团糊，20~25 才有形")]
        public float arcRefireHz = 22f;
        [Tooltip("单条电弧存活时间（秒）。设计定案 0.12~0.30")]
        public Vector2 arcLife = new Vector2(0.12f, 0.30f);
        [Tooltip("沿身体电弧的段数范围（每个池位创建时定死，保证 0 GC）")]
        public Vector2Int arcStations = new Vector2Int(7, 12);
        [Tooltip("沿身体电弧跨越的脊柱节数范围")]
        public Vector2Int arcSpanLinks = new Vector2Int(3, 8);
        [Tooltip("电弧带宽（米）")]
        public Vector2 arcWidth = new Vector2(0.10f, 0.26f);
        [Tooltip("抖动幅度（米）")]
        public Vector2 arcJitter = new Vector2(0.06f, 0.22f);
        [Tooltip("★ 弧带朝取景相机推出去的米数。必须大于龙身半径：锚点取的是脊柱骨节、本来在身体内部，不推出去会被龙自己的不透明皮深度剔除（ZTest LEqual）⇒ 侧视时一条电弧都看不见")]
        public float arcCamBias = 0.8f;
        [Tooltip("烟中电弧（B′）相对主体的亮度倍率")]
        [Range(0.1f, 1f)] public float smokeArcBrightness = 0.55f;

        [Header("电弧配色（材料级，改这里不用重编 shader）")]
        [Tooltip("芯色。★ 别给纯白：纸色背景近白，纯白芯在纸上完全看不见（实测）。给「带青的近白」——在墨黑身上是白热，在纸上仍是清楚的青线")]
        public Color arcCoreColor = new Color(0.78f, 0.96f, 1.0f, 1f);
        [Tooltip("辉光色（饱和青）。在墨黑与纸色两种底上都读得出")]
        public Color arcGlowColor = new Color(0.20f, 0.58f, 0.98f, 1f);
        [Tooltip("芯宽占弧带半宽的比例")]
        [Range(0.02f, 1f)] public float arcCoreWidth = 0.50f;
        [Tooltip("强度乘子。★ alpha 混合下 rgb 会被截到 1 —— 给 >1 只会把芯色洗成纯白，在纸上反而消失")]
        [Range(0.2f, 3f)] public float arcIntensity = 1.0f;
        [Tooltip("弧带深度测试：4 = LEqual（正常玩法用这个）、8 = Always。★ 只在诊断时改成 8 —— 用来分清「根本没画」与「被龙自己的皮挡住」，见 Docs/工程坑表.md 44")]
        [Range(4f, 8f)] public float arcZTest = 4f;

        [Header("黑烟")]
        [Tooltip("强度 1 时每秒发射的烟粒子数（会均摊到全部链节上）")]
        public float smokeRatePerSecond = 190f;
        public int smokeMaxParticles = 700;
        [Tooltip("单团烟的存活时间（秒）。设计定案 1.2~2.0")]
        public Vector2 smokeLife = new Vector2(1.2f, 2.0f);
        [Tooltip("单团烟的起始尺寸（米）")]
        public Vector2 smokeSize = new Vector2(0.55f, 1.25f);
        [Tooltip("烟向后上方拖尾的速度（米/秒）")]
        public float smokeTrailSpeed = 0.85f;
        [Tooltip("★ 纯墨黑（用户定案）。Linear 空间下必须按线性值给，见设计文档 §五")]
        public Color inkSmokeColor = new Color(0.020f, 0.021f, 0.025f, 0.86f);

        [Header("吐息（口部向前喷）")]
        [Tooltip("吐息时每秒从口部喷出的烟粒子数")]
        public float mouthRatePerSecond = 120f;
        public float mouthSpeed = 3.4f;
        [Tooltip("喷口扩散半角（度）")]
        public float mouthSpreadDeg = 16f;

        [Header("素材（留空则找 shader 的默认白图；见 TryAutoAssignTextures）")]
        public Texture2D arcTex;
        public Texture2D smokeTex;
        [Tooltip("烟贴图的网格格数：单张图填 (1,1)；6×6 动画图集填 (6,6)（见 Docs/工程坑表.md 42）")]
        public Vector2Int smokeSheetCells = new Vector2Int(1, 1);

        [Header("取景相机（决定弧带朝哪一侧摊平；留空则用 Camera.main）")]
        public Camera viewCamera;

        // ══════════════════════════════════════════════════════════════
        //  静态入口
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// **每帧一次**：驱动层（`EnemyDragon`）报告当前姿态。
        /// 表现层负责把姿态翻译成条数/浓度，并做**平滑逼近**（设计文档 §三的"不是每帧硬写"）。
        /// </summary>
        /// <param name="src">沿链取点来源。可为 null（此时只有口部烟能画）。</param>
        /// <param name="pose">当前姿态。</param>
        /// <param name="mouth01">吐息强度 0~1：&gt;0 时从口部向前喷烟。</param>
        public static void Drive(IDragonSpineSource src, Pose pose, float mouth01)
        {
            var it = Instance;
            if (it == null)
            {
                // ★ 惰性：只在**真的要画**的时候才建。全关时连对象都不建。
                if (pose == Pose.Off && mouth01 <= 0.01f) return;
                it = Ensure();
            }
            if (!it.enabled) it.enabled = true;      // 自愈（被禁用的 MonoBehaviour 不跑 Update）

            it._src = src;
            it._pose = pose;
            it._mouth01 = Mathf.Clamp01(mouth01);
            it._arcTarget = it.ArcsFor(pose);
            it._smokeTarget = it.SmokeFor(pose);
            if (Time.time < it._hitUntil)
            {
                it._arcTarget = Mathf.Max(it._arcTarget, it.arcsHit);
                it._smokeTarget = Mathf.Max(it._smokeTarget, 0.5f);
            }
        }

        /// <summary>受击瞬时放电（设计定案：8~10 条，0.25 s 后回落）。</summary>
        public static void Burst(float strength01)
        {
            var it = Instance;
            if (it == null) return;
            it._hitUntil = Time.time + Mathf.Max(0.05f, it.hitDuration);
            it._arcTarget = Mathf.Max(it._arcTarget, Mathf.Lerp(it.arcsRoar, it.arcsStrike, Mathf.Clamp01(strength01)));
            it._smokeTarget = Mathf.Max(it._smokeTarget, 0.45f);
        }

        /// <summary>全关（死亡 / 退出飞行）。烟让它自然散掉，不突兀收掉。</summary>
        public static void Shut()
        {
            var it = Instance;
            if (it == null) return;
            it._pose = Pose.Off;
            it._mouth01 = 0f;
            it._arcTarget = 0f;
            it._smokeTarget = 0f;
        }

        /// <summary>取得（必要时创建）场景单例。</summary>
        public static DragonStormVfx Ensure()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("DragonStormVfx(Runtime)");
            Instance = go.AddComponent<DragonStormVfx>();
            return Instance;
        }

        // ══════════════════════════════════════════════════════════════
        //  诊断（验收探针读这几个）
        // ══════════════════════════════════════════════════════════════

        public int ActiveArcCount => _activeArcs;
        public int ActiveSmokeArcCount => _activeSmokeArcs;
        public float ArcLevel => _arcShown;
        public float SmokeLevel => _smokeLevel;
        public int LivePuffCount { get { int n = 0; for (int i = 0; i < _puffs.Length; i++) if (_puffs[i].die > Time.time) n++; return n; } }
        /// <summary>本帧"最近的活跃电弧中点到最近活烟团"的距离（米）。判据 4 直接量这个。</summary>
        public float MinArcToPuffDistance => _minArcPuffDist;
        /// <summary>烟发射点覆盖过的链节位图（判据 3：俯冲段应 = 全部节数）。</summary>
        public ulong LinkEmitMask => _linkMask;
        public int LinkEmitCount { get { int c = 0; ulong m = _linkMask; while (m != 0UL) { c += (int)(m & 1UL); m >>= 1; } return c; } }
        public void ResetLinkMask() { _linkMask = 0UL; }

        /// <summary>累计"沿链喷"次数。<b>判据：吐息时应恒为 0</b>（用户定案「只有口部向前喷」）。</summary>
        public int SpineEmitTotal => _spineEmitTotal;
        /// <summary>累计"口部喷"次数。</summary>
        public int MouthEmitTotal => _mouthEmitTotal;

        // ══════════════════════════════════════════════════════════════
        //  内部状态
        // ══════════════════════════════════════════════════════════════

        private IDragonSpineSource _src;
        private Pose _pose = Pose.Off;
        private float _mouth01;

        private float _arcTarget, _arcShown, _arcStepTimer;
        private float _smokeTarget, _smokeLevel;
        private float _hitUntil = -99f;

        private int _activeArcs, _activeSmokeArcs;
        private float _minArcPuffDist = 999f;
        private ulong _linkMask;
        private int _linkCursor;
        private float _emitAcc, _mouthAcc;
        private int _spineEmitTotal, _mouthEmitTotal;

        private Camera _camCache;
        private float _camNextFind;
        private bool _warned;

        // ── 电弧池 ──
        private class Arc
        {
            public GameObject go;
            public Transform tr;
            public MeshFilter mf;
            public MeshRenderer mr;
            public Mesh mesh;
            public Vector3[] pts;       // 基础路径（世界），长度 stations —— **沿链弧每帧刷新**
            public float[] jit;         // 抖形偏移（米），长度 stations —— **只在 22 Hz 换**，位置每帧读
            public Vector3[] verts;     // 2 * stations
            public Vector3[] across;    // 每个站点的"跨带方向"（世界），长度 stations
            public Color[] cols;        // 2 * stations
            public Vector2[] uvs;       // 2 * stations（静态）
            public int[] tris;          // 静态
            public int stations;
            public float width;
            public float jitter;
            public float life;
            public float born;
            public float nextJitter;
            public float bright;
            public float seed;          // 断段图案的种子：**同一条命内恒定** ⇒ 电弧有自己的"性格"
            public int i0, i1;          // 沿链弧锚定的脊柱节区间
            public bool spanValid;      // true = 锚点每帧从脊骨重算；false = 锚点世界固定（烟中弧）
            public bool fromSmoke;
            public bool active;
        }
        private readonly List<Arc> _arcs = new List<Arc>();
        private Material _arcMat;
        private bool _arcMatTried;

        /// <summary>
        /// ★★ 弧带的包围盒，**每帧必须重写在 `SetVertices` 之后**。
        ///   踩坑：`CreateArc` 里写了一次 `mesh.bounds = 400 m 大盒`，但 `SetVertices` 会**重算**
        ///   并以它算出的结果覆盖掉 —— 于是那行代码等于没写。顶点全是世界坐标、物件在龙身上，
        ///   重算出来的盒本来也能用；**可一旦顶点里混进一个 NaN（见 `Rebuild` 里 `Clamp01` 的注释），
        ///   重算结果塌成 (0,0,0) ⇒ 整条弧带被视锥剔除**，而且剔除与否随相机而变，
        ///   症状看着像"深度/朝向不对"。这里用一个恒定的大盒把这条路彻底堵死。
        /// </summary>
        private static readonly Bounds ArcBounds = new Bounds(Vector3.zero, Vector3.one * 400f);

        // ── 烟 ──
        private ParticleSystem _ps;
        private ParticleSystemRenderer _psr;
        private Material _smokeMat;
        private bool _smokeTried;

        private struct Puff { public Vector3 pos; public float rad; public float die; }
        private readonly Puff[] _puffs = new Puff[24];
        /// <summary>判据 4 的补丁用：烟是否已经「可见且账本里有烟」，以及它的上升沿。</summary>
        private bool _smokeArmedPrev;
        private bool _forceSmokeRespawn;
        private int _puffHead;

        // ══════════════════════════════════════════════════════════════
        //  生命周期
        // ══════════════════════════════════════════════════════════════

        private void Awake()
        {
            Instance = this;
            TryAutoAssignTextures();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (_arcMat != null) Destroy(_arcMat);
            if (_smokeMat != null) Destroy(_smokeMat);
            for (int i = 0; i < _arcs.Count; i++)
                if (_arcs[i].mesh != null) Destroy(_arcs[i].mesh);
        }

        /// <summary>
        /// ★ 素材引用：优先用 Inspector 上显式赋的值；没赋就在**编辑器里按路径自动抓**。
        ///   为什么允许自动抓：这两个贴图属于商店素材（EULA 不可再分发）⇒ 被 .gitignore 排除，
        ///   不能塞进 `Resources/`（那会把素材带进版本库）。所以引用只能"运行时找"。
        ///   ⚠ 打包后 `AssetDatabase` 不存在 ⇒ 自动抓失效，那时必须由**预制体/场景显式赋值**。
        ///     这一点在文档里标为待办，不要当成已经解决。
        /// </summary>
        private void TryAutoAssignTextures()
        {
#if UNITY_EDITOR
            if (arcTex == null)
                arcTex = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(ARC_TEX_PATH);
            if (smokeTex == null)
            {
                // 首选：**单张**软烟团。实测 alpha 边缘 0 / 中心 239，形状干净，
                // 直接就是"一团烟"，不需要任何 UV 处理。
                smokeTex = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(SMOKE_TEX_PATH);
                if (smokeTex != null) smokeSheetCells = Vector2Int.one;
                else
                {
                    // 退路：ParticlePack 的烟 —— ★ 它是 **6×6 动画图集**，整张当单图采样会出现
                    // 规则点阵格线（第二十三轮实测）。所以必须配 smokeSheetCells = (6,6)，
                    // 交给粒子系统的 Texture Sheet Animation 逐粒子取一格。见 Docs/工程坑表.md 42。
                    smokeTex = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(SMOKE_TEX_FALLBACK_PATH);
                    if (smokeTex != null) smokeSheetCells = SMOKE_FALLBACK_CELLS;
                }
            }
#endif
        }

        // ★ 电弧贴图必须是**单张水平光条**。`Lightning.tif` 是 2×2 图集（四根完整闪电），
        //   当单张采样会在弧带上出现四根竖闪电 + 格线 —— 不能用。见 Docs/工程坑表.md 42。
        //   两者都是 RGB（无 alpha）⇒ shader 侧遮罩取 max(rgb, a)，不能只读 alpha。
        private const string ARC_TEX_PATH =
            "Assets/UnityTechnologies/ParticlePack/EffectExamples/Fire & Explosion Effects/Textures/LightningTrail.tif";
        // 首选：单张烟团
        private const string SMOKE_TEX_PATH =
            "Assets/WarFX Assets/WarFX/Desktop/Textures/Smoke/WFX_T_SmokeLoopAlpha.tga";
        // 退路：6×6 图集（必须配 SMOKE_FALLBACK_CELLS）
        private const string SMOKE_TEX_FALLBACK_PATH =
            "Assets/UnityTechnologies/ParticlePack/EffectExamples/Smoke & Steam Effects/Textures/SmokeLoop.tif";
        private static readonly Vector2Int SMOKE_FALLBACK_CELLS = new Vector2Int(6, 6);

        // ══════════════════════════════════════════════════════════════
        //  姿态 → 目标强度（设计文档 §三 那张表）
        // ══════════════════════════════════════════════════════════════

        private float ArcsFor(Pose p)
        {
            switch (p)
            {
                case Pose.Idle: return arcsIdle;
                case Pose.Tell: return arcsTell;
                case Pose.Dive: return arcsDive;
                case Pose.Strike: return arcsStrike;
                case Pose.Recover: return arcsRecover;
                case Pose.Breath: return arcsBreath;
                case Pose.Roar: return arcsRoar;
            }
            return 0f;
        }

        private float SmokeFor(Pose p)
        {
            switch (p)
            {
                case Pose.Idle: return 0f;
                case Pose.Tell: return 0.22f;      // 起（淡）
                case Pose.Dive: return 1.0f;       // 整链全喷
                case Pose.Strike: return 1.0f;     // 最浓
                case Pose.Recover: return 0.32f;   // 渐散
                // ★ 用户定案「吐息喷烟**只有口部向前**」⇒ 整链不喷，烟全部走 _mouth01 那条路。
                //   这里以前错写成 0.45，实测「口部向前烟占比」只有 7.9%（七成以上是链上喷的）。
                case Pose.Breath: return 0f;
                case Pose.Roar: return 0.30f;
            }
            return 0f;
        }

        // ══════════════════════════════════════════════════════════════
        //  每帧
        // ══════════════════════════════════════════════════════════════

        private void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) dt = 0.02f;

            // ★★ 龙已销毁的假空探测（第二十八轮实测）：`_src` 是接口引用，
            //   `!= null` 走**引用比较**，探测不到 Unity 的"托管还活着、原生已销毁"。
            //   龙销毁后 Drive 不再被调用，_arcTarget/_smokeTarget 停在旧值 ⇒ 电弧每帧重生，
            //   `SpineLerp → GetSpinePosition` 摸已销毁骨头的 `transform` ⇒ MissingReferenceException 刷屏。
            //   这里把假空归正为真 null，并立即按 Off 收场（烟自然散掉）。
            if (_src != null && (_src as UnityEngine.Object) == null)
            {
                _src = null;
                _pose = Pose.Off;
                _mouth01 = 0f;
                _arcTarget = 0f;
                _smokeTarget = 0f;
            }

            // ── 条数：整数、阶梯式变化（不是插值）──
            _arcStepTimer -= dt;
            if (_arcStepTimer <= 0f)
            {
                _arcStepTimer = Mathf.Max(1f / 240f, arcStepInterval);
                float d = _arcTarget - _arcShown;
                if (Mathf.Abs(d) >= 1f) _arcShown += Mathf.Sign(d);
                else if (Mathf.Abs(d) < 0.01f) _arcShown = _arcTarget;
            }
            // ── 烟的浓度：连续平滑 ──
            _smokeLevel = Mathf.MoveTowards(_smokeLevel, _smokeTarget, 2.2f * dt);
            // ★★ 判据 4 的补丁（第二十三轮实测）：`wantSmoke` 是**槽位重生那一刻**掷的骰子，
            //   而烟中电弧只挂在 `slot % 3 == 2` 这几个槽上 ⇒ 若它们在烟起来之前刚重生过，
            //   就得等自己 0.12~0.30 s 的寿命走完才轮得到重掷 ⇒ 俯冲起手那 ~0.3 s 里
            //   可能**一条烟中电弧都没有**。而判据 4 量的是「这一帧有没有一条电弧在烟里」，
            //   于是整段占比被起手几帧拖到 60~75%（实测双峰：距离中位 0.05 m 却只有 63% 达标）。
            //   这里在烟的**上升沿**把指定槽位标记为待重生，`UpdateArcs` 里立刻执行。
            bool smokeArmed = _smokeLevel > 0.08f && LivePuffCount > 0;
            _forceSmokeRespawn = smokeArmed && !_smokeArmedPrev;
            _smokeArmedPrev = smokeArmed;

            UpdateArcs(dt);
            UpdateSmoke(dt);

            // ★ 这里**不做**"全关就把粒子系统停掉"的优化：`Emit` 在暂停的粒子系统上不模拟，
            //   而 `EnsureSmoke()` 见 `_ps != null` 就直接返回 ⇒ 一旦暂停就再也播不起来（静默哑掉）。
            //   烟雾系统的待机开销可以忽略，不值得为它引入一个只在特定时序下才复现的 bug。
            //   电弧那边不需要这种优化：`SetActive(false)` 本来就把它整个从渲染里摘掉了。
        }

        // ────────────────────────────── 电弧 ──────────────────────────────

        private void UpdateArcs(float dt)
        {
            int want = Mathf.Clamp(Mathf.RoundToInt(_arcShown), 0, Mathf.Max(1, arcPool));
            if (want > 0 && !EnsureArcMaterial()) want = 0;

            _activeArcs = 0; _activeSmokeArcs = 0; _minArcPuffDist = 999f;
            Camera cam = ViewCam();

            for (int i = 0; i < _arcs.Count || i < want; i++)
            {
                if (i >= _arcs.Count)
                {
                    if (i >= want) break;
                    _arcs.Add(CreateArc(_arcs.Count));
                }

                Arc a = _arcs[i];
                if (i >= want)
                {
                    if (a.active) { a.active = false; a.go.SetActive(false); }
                    continue;
                }

                if (!a.active)
                {
                    Respawn(a, i);
                    Rebuild(a, cam, true);
                }
                // `_forceSmokeRespawn`：烟的上升沿那一帧，把指定的烟中电弧槽位立刻换锚点
                else if (Time.time - a.born > a.life || (_forceSmokeRespawn && i % 3 == 2))
                {
                    Respawn(a, i);       // 寿命到了：换锚点（设计定案 0.12~0.30 s 随机重生）
                    Rebuild(a, cam, true);
                }
                else
                {
                    // ★ **每帧**写网格：锚点必须跟着脊骨走（身体在动）。
                    //   抖形只在 22 Hz 那一次重生成 —— 见 Rebuild 的注释。
                    bool rj = Time.time >= a.nextJitter;
                    if (rj) a.nextJitter = Time.time + 1f / Mathf.Max(1f, arcRefireHz);
                    Rebuild(a, cam, rj);
                }

                a.active = true;
                _activeArcs++;
                if (a.fromSmoke) _activeSmokeArcs++;

                // 判据 4 的直接度量：活跃电弧中点到最近活烟团的距离
                // （对所有电弧都算 —— 判据原文是「存在与烟团距离 < 0.6 m 的电弧」，不限来源）
                {
                    Vector3 mid = a.pts[a.stations / 2];
                    float best = 999f;
                    for (int k = 0; k < _puffs.Length; k++)
                    {
                        if (_puffs[k].die <= Time.time) continue;
                        float d2 = (mid - _puffs[k].pos).sqrMagnitude;
                        if (d2 < best) best = d2;
                    }
                    if (best < 998001f) _minArcPuffDist = Mathf.Min(_minArcPuffDist, Mathf.Sqrt(best));
                }
            }
            _forceSmokeRespawn = false;   // 只在本帧生效，下一帧交回寿命判定
        }

        private Camera ViewCam()
        {
            // ★ 只判 null，**不能判 `isActiveAndEnabled`**：验收探针的相机是 `enabled = false`
            //   然后手动 `cam.Render()` 的（这样才不会抢 Game view 的渲染）。
            //   用「激活且启用」去筛会把探针相机筛掉、悄悄退到 `Camera.main`
            //   ⇒ 弧带照着另一台相机的方向摊平，从出图的那台看过去是一条线。
            //   这里只需要一个**方向参考**，禁用中的相机同样有合法的 transform。
            if (viewCamera != null) return viewCamera;
            if (_camCache != null) return _camCache;
            if (Time.time >= _camNextFind)
            {
                _camNextFind = Time.time + 0.5f;
                _camCache = Camera.main;
            }
            return _camCache;
        }

        private Material ArcMaterial()
        {
            Shader sh = Shader.Find("InkWash/InkArc");
            if (sh == null) { WarnOnce("[DragonStormVfx] 找不到 Shader: InkWash/InkArc —— 电弧不会出现。"); return null; }
            var m = new Material(sh) { name = "M_DragonArc(Runtime)" };
            if (arcTex != null && m.HasProperty("_MainTex")) m.SetTexture("_MainTex", arcTex);
            if (m.HasProperty("_CoreColor")) m.SetColor("_CoreColor", arcCoreColor);
            if (m.HasProperty("_GlowColor")) m.SetColor("_GlowColor", arcGlowColor);
            if (m.HasProperty("_CoreWidth")) m.SetFloat("_CoreWidth", arcCoreWidth);
            if (m.HasProperty("_Intensity")) m.SetFloat("_Intensity", arcIntensity);
            if (m.HasProperty("_ZTest")) m.SetFloat("_ZTest", arcZTest);
            m.renderQueue = (int)RenderQueue.Transparent + 30;
            return m;
        }

        private bool EnsureArcMaterial()
        {
            if (_arcMat != null) return true;
            if (_arcMatTried && _arcMat == null) return false;
            _arcMatTried = true;
            _arcMat = ArcMaterial();
            return _arcMat != null;
        }

        private Arc CreateArc(int slot)
        {
            var a = new Arc();
            a.go = new GameObject("DragonArc_" + slot);
            a.go.transform.SetParent(transform, false);
            a.tr = a.go.transform;
            a.mf = a.go.AddComponent<MeshFilter>();
            a.mr = a.go.AddComponent<MeshRenderer>();
            a.mr.sharedMaterial = _arcMat;
            a.mr.shadowCastingMode = ShadowCastingMode.Off;
            a.mr.receiveShadows = false;
            a.mr.lightProbeUsage = LightProbeUsage.Off;
            a.mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            a.mr.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;

            // ★ 段数**每池位固定**：拓扑/UV 只建一次，之后每帧只改顶点 ⇒ 0 GC
            a.stations = Mathf.Max(3, Random.Range(arcStations.x, arcStations.y + 1));
            int n = a.stations;
            a.pts = new Vector3[n];
            a.jit = new float[n];
            a.across = new Vector3[n];
            a.verts = new Vector3[2 * n];
            a.cols = new Color[2 * n];
            a.uvs = new Vector2[2 * n];
            a.tris = new int[(n - 1) * 6];
            for (int k = 0; k < n; k++)
            {
                float u = k / (float)(n - 1);
                a.uvs[2 * k] = new Vector2(u, 0f);
                a.uvs[2 * k + 1] = new Vector2(u, 1f);
            }
            for (int k = 0; k + 1 < n; k++)
            {
                int o = k * 6, v = k * 2;
                a.tris[o + 0] = v + 0; a.tris[o + 1] = v + 1; a.tris[o + 2] = v + 2;
                a.tris[o + 3] = v + 1; a.tris[o + 4] = v + 3; a.tris[o + 5] = v + 2;
            }

            a.mesh = new Mesh { name = "DragonArcMesh" };
            a.mesh.MarkDynamic();
            // 顶点是**世界坐标**、物件在龙身上 ⇒ 给一个足够大的局部包围盒，免得被视锥剔除掉。
            // （每帧顶点都在变，靠 RecalculateBounds 既费又可能闪；直接用一个大盒最稳。）
            // ⚠ 这一行**不够**：`Rebuild` 里的 `SetVertices` 会把它覆盖掉，那边每帧重写一次，见 ArcBounds。
            a.mesh.bounds = ArcBounds;
            a.mesh.SetVertices(a.verts);
            a.mesh.SetColors(a.cols);
            a.mesh.SetUVs(0, a.uvs);
            a.mesh.SetTriangles(a.tris, 0, false);
            a.mf.sharedMesh = a.mesh;
            a.go.SetActive(false);
            return a;
        }

        /// <summary>换一个锚点：沿身体的一段，或一个烟团内部。</summary>
        private void Respawn(Arc a, int slot)
        {
            a.width = Random.Range(arcWidth.x, arcWidth.y);
            a.jitter = Random.Range(arcJitter.x, arcJitter.y);
            a.life = Random.Range(arcLife.x, arcLife.y);
            a.born = Time.time;
            a.nextJitter = Time.time;
            a.bright = 1f;
            a.seed = Random.value * 10f;

            // ★ 每 3 条里有 1 条是"烟中电弧"（B′）—— 前提是烟够浓，否则读起来像凭空放电。
            //   ★★ 阈值 0.25 太钝了（第二十三轮实测）：判据 4 量的是「**所有**活跃电弧里
            //   到最近烟团距离的最小值」，也就是"这一帧只要有**一条**电弧在烟里就达标"。
            //   而 `_smokeLevel` 由 `MoveTowards(_, 2.2*dt)` 爬到 0.25 要 ~3.4 帧，
            //   俯冲段总共只有 ~18 帧 ⇒ 起手那几帧一条烟中电弧都没有，直接判不达标。
            //   实测形态是**双峰**的：距离中位 0.05 m（在烟里）却只有 63% 达标。
            //   ⇒ 门槛降到"烟刚可见"即可 —— 「凭空放电」由 `LivePuffCount > 0` 兜底，
            //   它保证账本里真的有一颗已经发射出去的烟（见 `PushPuff` 的注释）。
            bool wantSmoke = _smokeLevel > 0.08f && (slot % 3 == 2) && LivePuffCount > 0;
            a.fromSmoke = wantSmoke;
            if (wantSmoke)
            {
                a.bright = smokeArcBrightness;
                a.width *= 0.55f;
                a.jitter *= 0.5f;
            }

            int n = a.stations;
            if (wantSmoke)
            {
                // 烟团内部：随机方向的一小段
                Puff puff = NewestLivePuff();
                Vector3 dir = Random.onUnitSphere;
                float len = Mathf.Max(0.25f, puff.rad * 1.2f);
                Vector3 mid = puff.pos;
                for (int k = 0; k < n; k++)
                {
                    float u = k / (float)(n - 1);
                    a.pts[k] = mid + dir * ((u - 0.5f) * len);
                }
            }
            else
            {
                int cnt = _src != null ? _src.SpineCount : 0;
                a.spanValid = false;
                if (cnt <= 1)
                {
                    // 没有骨链（或只有 1 节）时退化成"在龙前方横着放电"，
                    // 总比不画强，而且不会因为 0 而除零。
                    Vector3 c = _src != null ? _src.GetMouthPosition() : transform.position;
                    Vector3 f = _src != null ? _src.GetMouthForward() : transform.forward;
                    for (int k = 0; k < n; k++)
                    {
                        float u = k / (float)(n - 1);
                        a.pts[k] = c + f * 0.4f + Vector3.Cross(f, Vector3.up).normalized * ((u - 0.5f) * 1.2f);
                    }
                }
                else
                {
                    int span = Random.Range(arcSpanLinks.x, arcSpanLinks.y + 1);
                    a.i0 = Random.Range(0, Mathf.Max(1, cnt - span));
                    a.i1 = Mathf.Min(cnt - 1, a.i0 + span);
                    a.spanValid = true;
                    for (int k = 0; k < n; k++)
                    {
                        float u = k / (float)(n - 1);
                        a.pts[k] = SpineLerp(a.i0, a.i1, u);
                    }
                }
            }

            a.go.SetActive(true);
            a.active = true;
        }

        /// <summary>脊柱上的连续取点（在离散骨节之间做线性插值，避免"阶梯状"路径）。</summary>
        private Vector3 SpineLerp(int i0, int i1, float u)
        {
            float t = Mathf.Lerp(i0, i1, u);
            int i = Mathf.FloorToInt(t);
            float f = t - i;
            Vector3 p0 = _src.GetSpinePosition(i);
            Vector3 p1 = _src.GetSpinePosition(i + 1 <= i1 ? i + 1 : i1);
            return Vector3.Lerp(p0, p1, f);
        }

        private Puff NewestLivePuff()
        {
            for (int k = 0; k < _puffs.Length; k++)
            {
                int idx = (_puffHead - 1 - k + _puffs.Length * 2) % _puffs.Length;
                if (_puffs[idx].die > Time.time) return _puffs[idx];
            }
            return new Puff { pos = transform.position, rad = 0.6f, die = Time.time + 1f };
        }

        /// <summary>
        /// 重新抖动形状并写进网格。
        /// ★ 两端**必须钉在锚点上**（不抖），中间按 sin 包络抖 —— 这就是"电弧两端搭在身体上、
        ///   中段炸开"的经典形状。若两端也抖，读起来是一团浮空的静电噪声。
        /// </summary>
        /// <summary>
        /// 写网格。
        /// ★★ 这里分成两件**频率不同**的事（第二十三轮的关键修正）：
        ///   · **位置**：沿链弧的锚点必须**每帧**从脊骨重算。之前只在 `Respawn` 时取一次，
        ///     锚点就冻在世界坐标里了 —— 巡航/俯冲时龙每 0.2 s 前进 1~2 m，电弧被**落在身后**，
        ///     侧视看就是"身上一条电弧都没有"（实测：盘旋条目青像素 0，而悬停慢速的吐息条目有 768）。
        ///   · **抖形**：`a.jit[]` 只在 22 Hz 换。抖动与跟随是两回事，混在一起就只能二选一：
        ///     要么电弧不跟身体，要么抖动被抬到帧率（60 Hz 抖 = 一团糊）。
        /// </summary>
        /// <param name="recalcJitter">true = 这一帧重新生成抖形（22 Hz 那一次）</param>
        private void Rebuild(Arc a, Camera cam, bool recalcJitter)
        {
            int n = a.stations;
            Vector3 camPos = cam != null ? cam.transform.position : a.pts[n / 2] + Vector3.forward * 10f;

            for (int k = 0; k < n; k++)
            {
                float u = k / (float)(n - 1);

                // ★ 锚点每帧跟随脊骨（只有沿链弧才有"脊骨"可跟；烟中弧锚在烟团、世界固定）
                if (a.spanValid && _src != null) a.pts[k] = SpineLerp(a.i0, a.i1, u);

                Vector3 tangent;
                if (k == 0) tangent = a.pts[1] - a.pts[0];
                else if (k == n - 1) tangent = a.pts[n - 1] - a.pts[n - 2];
                else tangent = a.pts[k + 1] - a.pts[k - 1];
                if (tangent.sqrMagnitude < 1e-8f) tangent = Vector3.forward;
                tangent.Normalize();

                Vector3 toCam = camPos - a.pts[k];
                if (toCam.sqrMagnitude < 1e-6f) toCam = Vector3.forward;
                toCam.Normalize();

                // 跨带方向 = 既垂直于切线、又垂直于视线 ⇒ 弧带正面朝相机
                Vector3 across = Vector3.Cross(tangent, toCam);
                if (across.sqrMagnitude < 1e-6f) across = Vector3.Cross(tangent, Vector3.up);
                across.Normalize();
                a.across[k] = across;

                // ★★ 这里必须 `Clamp01`：`u = k/(n-1)` 在**最后一节正好等于 1.0**，而 float 的
                //    `Mathf.Sin(Mathf.PI)` 是 **−8.7e-8（负数）**（float 的 π 略小于真 π）
                //    ⇒ `Mathf.Pow(负数, 0.55)` = **NaN**。
                //    后果远不止"最后一个顶点坏掉"：NaN 顶点会让 `SetVertices` 重算出的
                //    `Mesh.bounds` **塌成零尺寸** ⇒ **整条弧带被视锥剔除**。剔除发生在光栅化之前，
                //    所以 `_ZTest = Always` 救不了它（这正是归因 A/B 五档全 0 的原因）；
                //    而剔除与否取决于相机与龙原点的相对位置 ⇒ 表现为「3/4 看得见、侧视看不见」
                //    这种**随相机变化**的症状，看起来像深度/朝向问题，其实完全无关。
                //    定位过程见 `Tools/cs/drg_arc_diag.cs`（同帧多趟渲染，零颜色假设）。
                float env = Mathf.Pow(Mathf.Clamp01(Mathf.Sin(u * Mathf.PI)), 0.55f);   // 两端 0、中段 1
                if (recalcJitter)
                {
                    float jit = Random.Range(-1f, 1f) * env;
                    // 加一个低频分量，避免"纯白噪声"读起来像静电雪花
                    jit += 0.45f * Mathf.Sin((u * 3.1f + a.born * 7.3f) * Mathf.PI) * env;
                    a.jit[k] = jit;
                }

                // ★★ 朝相机推出去：锚点取的是**脊柱骨节**，本来就在身体内部，
                //    不推的话整条弧带被龙自己的不透明皮深度剔除（ZTest LEqual）
                //    ⇒ 侧视时电弧全部消失（第二十三轮实测踩到）。
                float bias = a.fromSmoke ? arcCamBias * 0.35f : arcCamBias;
                Vector3 center = a.pts[k] + across * (a.jit[k] * a.jitter) + toCam * bias;
                a.verts[2 * k]     = center - across * (a.width * 0.5f);
                a.verts[2 * k + 1] = center + across * (a.width * 0.5f);

                // 顶点色 alpha = 两端收尖 × 本条亮度（B′ 更暗）× **断段**
                // ★ 断段是"像不像电"的关键：只做平滑收尖 + 抖动，出来是一条**平滑的青带**（实测第一版就是
                //   "龙身上画了一条蓝线"）。真正的电弧是**一段亮一段灭**的。
                //   图案由 `a.seed` 定 ⇒ 同一条命内不变，抖动时不会闪成一团噪声。
                float taper = Mathf.Pow(Mathf.Clamp01(Mathf.Sin(u * Mathf.PI)), 0.45f);   // ★ Clamp01 不能去，见上面 env 的注释
                float seg = Mathf.PerlinNoise(a.seed * 7.1f + k * 0.85f, a.seed * 3.3f);
                float brk = seg < 0.34f ? 0.08f : 1f;
                Color c = new Color(1f, 1f, 1f, Mathf.Clamp01(taper * a.bright * brk));
                a.cols[2 * k] = c;
                a.cols[2 * k + 1] = c;
            }

            a.mesh.SetVertices(a.verts);
            a.mesh.SetColors(a.cols);
            // ★ 必须写在 SetVertices **之后**（它会重算并覆盖包围盒），见 ArcBounds 的注释。
            a.mesh.bounds = ArcBounds;
        }

        // ────────────────────────────── 黑烟 ──────────────────────────────

        private void UpdateSmoke(float dt)
        {
            // ★ 条件必须带 `|| _mouth01`：吐息时 _smokeLevel 是 0（整链不喷），
            //   只判 _smokeLevel 就永远不建粒子系统 ⇒ 口部烟静默消失。
            if (_smokeLevel > 0.01f || _mouth01 > 0.01f) EnsureSmoke();
            if (_ps == null) return;

            bool bodyOk = _src != null && _src.SpineCount > 0;

            // ── 沿链喷（俯冲主战场）──
            if (_smokeLevel > 0.01f && bodyOk)
            {
                _emitAcc += smokeRatePerSecond * _smokeLevel * dt;
                int guard = 0;
                while (_emitAcc >= 1f && guard++ < 64)
                {
                    _emitAcc -= 1f;
                    EmitSpineOne();
                }
            }
            else _emitAcc = 0f;

            // ── 口部向前喷（吐息）──
            if (_mouth01 > 0.01f)
            {
                _mouthAcc += mouthRatePerSecond * _mouth01 * dt;
                int guard = 0;
                while (_mouthAcc >= 1f && guard++ < 64)
                {
                    _mouthAcc -= 1f;
                    EmitMouthOne();
                }
            }
            else _mouthAcc = 0f;
        }

        /// <summary>
        /// 沿链喷一口烟。
        /// ★ 用**轮转游标**而不是随机取节：随机分布在 24 节上要 ~90 次发射才覆盖满
        ///   （coupon collector），而一次俯冲只发得出 ~70 次 ⇒ 判据 3「发射点覆盖 24 节」
        ///   会随机不达标。轮转是 O(节数) 就铺满，而且**看起来更均匀**（像整条龙在冒烟）。
        /// </summary>
        private void EmitSpineOne()
        {
            int cnt = _src.SpineCount;
            int i = _linkCursor % cnt;
            _linkCursor = (_linkCursor + 1) % cnt;
            if (i < 64) _linkMask |= (1UL << i);
            _spineEmitTotal++;

            Vector3 p = _src.GetSpinePosition(i) + Random.insideUnitSphere * 0.18f;
            Vector3 d = _src.GetSpineDirection(i);
            Vector3 vel = -d * smokeTrailSpeed + Vector3.up * (smokeTrailSpeed * 0.7f)
                        + Random.insideUnitSphere * 0.35f;

            var ep = new ParticleSystem.EmitParams();
            ep.position = p;
            ep.velocity = vel;
            ep.startSize = Random.Range(smokeSize.x, smokeSize.y);
            ep.startLifetime = Random.Range(smokeLife.x, smokeLife.y);
            ep.applyShapeToPosition = false;
            _ps.Emit(ep, 1);

            // ★★ 记账数组必须**每次发射都写**，不能抽稀。
            //   原来这里是 `if ((_linkCursor & 3) == 0) PushPuff(...)`，而 `_linkCursor` 在上一步已经自增过
            //   ⇒ 条件等价于「只在 i ≡ 3 (mod 4) 的节上记账」⇒ **24 节里只有 6 节**有账本，
            //   间距约 1.4 m；而视觉上的粒子有 400+ 颗铺满全身 ⇒ **账本和看得见的烟不是一回事**。
            //   代价是判据 4（「俯冲段存在与烟团距离 < 0.6 m 的电弧」）永远只能到 50~75%，
            //   而且「烟中电弧」(B′) 只会出现在那 6 个位置上。
            //   改动后：`_puffs` 是 24 槽环形缓冲 + 轮转游标 ⇒ 最新 24 条账目**恰好覆盖 24 节**，
            //   间距 = 一节长（~0.35 m）。写入是 struct 赋值，不分配（判据 7 仍是 0 B/帧）。
            PushPuff(p, 0.6f);
        }

        private void EmitMouthOne()
        {
            Vector3 o = _src != null ? _src.GetMouthPosition() : transform.position;
            Vector3 f = _src != null ? _src.GetMouthForward() : transform.forward;
            Vector3 dir = Quaternion.AngleAxis(Random.Range(-mouthSpreadDeg, mouthSpreadDeg), Vector3.up) *
                          Quaternion.AngleAxis(Random.Range(-mouthSpreadDeg, mouthSpreadDeg), Vector3.right) * f;

            var ep = new ParticleSystem.EmitParams();
            ep.position = o + dir * 0.25f;
            ep.velocity = dir * mouthSpeed + Random.insideUnitSphere * 0.4f;
            ep.startSize = Random.Range(smokeSize.x, smokeSize.y) * 1.1f;
            ep.startLifetime = Random.Range(smokeLife.x, smokeLife.y);
            ep.applyShapeToPosition = false;
            _ps.Emit(ep, 1);

            PushPuff(o + dir * 0.8f, 0.7f);
            _mouthEmitTotal++;
        }

        private void PushPuff(Vector3 p, float rad)
        {
            _puffs[_puffHead] = new Puff { pos = p, rad = rad, die = Time.time + smokeLife.y };
            _puffHead = (_puffHead + 1) % _puffs.Length;
        }

        private void EnsureSmoke()
        {
            if (_ps != null) return;
            if (_smokeMat == null)
            {
                if (_smokeTried) return;          // 已经失败过一次 ⇒ 不再重试（WarnOnce 已经把话说清楚）
                _smokeTried = true;
                _smokeMat = SmokeMaterial();
                if (_smokeMat == null) return;
            }

            var go = new GameObject("DragonStormSmoke");
            go.transform.SetParent(transform, false);
            _ps = go.AddComponent<ParticleSystem>();

            var main = _ps.main;
            main.loop = true;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = Mathf.Max(32, smokeMaxParticles);
            main.startLifetime = new ParticleSystem.MinMaxCurve(smokeLife.x, smokeLife.y);
            main.startSize = new ParticleSystem.MinMaxCurve(smokeSize.x, smokeSize.y);
            main.startSpeed = 0f;
            main.startColor = new ParticleSystem.MinMaxGradient(inkSmokeColor);
            main.gravityModifier = 0f;
            // ★ 单张烟团贴图不是径向对称的 ⇒ 不随机朝向的话，N 团烟就是同一张图叠出来的一串复制品
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);

            // ★ 全手动 Emit：不用 rateOverTime，因为发射点要**逐节轮转**，交给 rate 就失去控制了
            var em = _ps.emission; em.enabled = false;
            var shp = _ps.shape; shp.enabled = false;

            // ★★ 图集贴图必须走 Texture Sheet Animation 逐粒子取一格。
            //    不要指望材质去缩放 UV —— URP 粒子 shader 是否吃 `_BaseMap_ST` 依版本而异；
            //    而这个模块是**把 UV 直接写进顶点流**的，与 shader 无关，是唯一可靠的用法。
            var tsa = _ps.textureSheetAnimation;
            if (smokeSheetCells.x > 1 || smokeSheetCells.y > 1)
            {
                tsa.enabled = true;
                tsa.mode = ParticleSystemAnimationMode.Grid;
                tsa.numTilesX = Mathf.Max(1, smokeSheetCells.x);
                tsa.numTilesY = Mathf.Max(1, smokeSheetCells.y);
                tsa.animation = ParticleSystemAnimationType.WholeSheet;
                tsa.frameOverTime = new ParticleSystem.MinMaxCurve(0f);
                tsa.startFrame = new ParticleSystem.MinMaxCurve(0f, 1f);
                // ★ 不要设 `cycleMode` / `ParticleSystemAnimationCycles` —— 团结版 2022.3 里
                //   这两个名字不存在（编译报 CS1061 / CS0103）。反正 `frameOverTime` 是常量 0，
                //   「循环」与否在行为上完全一样，不设就没有这层版本依赖。
                tsa.timeMode = ParticleSystemAnimationTimeMode.Lifetime;
            }
            else tsa.enabled = false;

            var col = _ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(1f, 0.16f),
                    new GradientAlphaKey(0.82f, 0.55f),
                    new GradientAlphaKey(0f, 1f)
                });
            col.color = new ParticleSystem.MinMaxGradient(g);

            var sol = _ps.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, GrowthCurve());

            // 湍流：让烟"有机地"翻滚。这是廉价但极有效的一步 —— 没有它，烟是一串光滑的球。
            var noi = _ps.noise;
            noi.enabled = true;
            noi.strength = 0.55f;
            noi.frequency = 0.22f;
            noi.scrollSpeed = 0.35f;
            noi.damping = true;
            noi.octaveCount = 2;

            _psr = go.GetComponent<ParticleSystemRenderer>();
            _psr.renderMode = ParticleSystemRenderMode.Billboard;
            _psr.alignment = ParticleSystemRenderSpace.View;
            _psr.shadowCastingMode = ShadowCastingMode.Off;
            _psr.receiveShadows = false;
            _psr.lightProbeUsage = LightProbeUsage.Off;
            _psr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            _psr.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            _psr.sortingFudge = -2f;
            _psr.sharedMaterial = _smokeMat;

            _ps.Play();
        }

        private static AnimationCurve GrowthCurve()
        {
            var c = new AnimationCurve();
            c.AddKey(0f, 0.55f);
            c.AddKey(0.5f, 1.05f);
            c.AddKey(1f, 1.45f);
            return c;
        }

        /// <summary>
        /// 烟材质。
        /// ★ 用 URP 的粒子 Unlit（`EnergyExplosion.mat` 已验证它在 URP 下正常渲染），
        ///   而不是包里的 legacy 材质 —— 少一层"到底会不会变洋红"的不确定性。
        /// ★ 近黑的 `_BaseColor` 乘上灰白的烟贴图 ⇒ 纯墨黑。**不能只取贴图不染色**。
        /// </summary>
        private Material SmokeMaterial()
        {
            Shader sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (sh == null) sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (sh == null) sh = Shader.Find("Legacy Shaders/Particles/Alpha Blended");
            if (sh == null)
            {
                WarnOnce("[DragonStormVfx] 找不到可用的烟 Shader —— 黑烟不会出现。");
                return null;
            }

            var m = new Material(sh) { name = "M_DragonSmoke(Runtime)" };
            if (smokeTex != null)
            {
                if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", smokeTex);
                if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", smokeTex);
            }
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", inkSmokeColor);
            if (m.HasProperty("_Color")) m.SetColor("_Color", inkSmokeColor);

            if (m.HasProperty("_Surface"))
            {
                m.SetFloat("_Surface", 1f);                                   // Transparent
                m.SetFloat("_Blend", 0f);                                     // Alpha
                m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                m.SetFloat("_ZWrite", 0f);
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.DisableKeyword("_ALPHATEST_ON");
                m.renderQueue = (int)RenderQueue.Transparent;
            }
            return m;
        }

        private void WarnOnce(string msg)
        {
            if (_warned) return;
            _warned = true;
            Debug.LogWarning(msg);
        }
    }
}
