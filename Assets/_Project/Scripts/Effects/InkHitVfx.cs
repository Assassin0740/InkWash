using System.Collections.Generic;
using UnityEngine;

namespace InkWash.Effects
{
    /// <summary>
    /// 水墨命中特效：**溅墨**（命中瞬间朝击退方向甩出的墨点）与**墨花**（落地时地面绽开的一圈墨）。
    ///
    /// 论文 · E5。两个都是纯程序化生成：没有贴图、没有 ParticleSystem 资产，
    /// 池里的每个面片用 <c>InkSplash.shader</c> 按不同 seed 生成不同形状的墨点。
    ///
    /// ★ 为什么做成"场景单例 + 静态入口"，而不是让每个敌人自带一个特效组件：
    ///   受击方是**运行时生成**的敌人（还有玩家、以后可能有的场景物件），
    ///   若特效挂在各自身上，"池"就会变成每只怪一份 —— 三波怪刷完就是几十份池子，
    ///   每份还各自持有材质实例。集中成一个管理器之后，全场共用一个池、一份材质，
    ///   且**受击方不需要认识特效的实现**，只调一个静态方法（见 <see cref="Spawn"/>）。
    ///
    /// ★ 表现层不得带崩玩法链路（本项目在 SwingStarted 多播上栽过）：
    ///   <see cref="Spawn"/> 不抛异常、不阻塞、池满时静默丢弃并计数，
    ///   池子/材质拿不到时只警告一次。
    ///
    /// 池是**惰性创建**的（首次真正需要时才建），不在 Awake 里建 ——
    /// 否则编辑期被 AddComponent 时就会往场景里塞 32 个对象，把场景文件搞脏。
    /// </summary>
    [DisallowMultipleComponent]
    public class InkHitVfx : MonoBehaviour
    {
        public static InkHitVfx Instance { get; private set; }

        [Header("池")]
        [Tooltip("同时存在的墨点上限。满了就丢弃新的（并计数），不扩容 —— 战斗最激烈时也不该产生 GC")]
        public int poolSize = 40;

        [Header("溅墨")]
        public float life = 0.55f;
        [Tooltip("每次命中甩出几个墨点")]
        public int dotsPerHit = 5;
        [Tooltip("墨点世界尺寸范围（米）")]
        public Vector2 splashSize = new Vector2(0.10f, 0.26f);
        public float flySpeed = 2.6f;
        [Tooltip("墨点受的重力（负 = 往下掉，像真墨滴）")]
        public float gravity = -7f;
        public float drag = 4.2f;

        [Header("墨花（落地）")]
        public float bloomLife = 0.5f;
        [Tooltip("墨花最终半径（米）")]
        public float bloomRadius = 0.9f;
        public int bloomDots = 3;

        [Header("风格")]
        public Color inkColor = new Color(0.055f, 0.062f, 0.082f, 1f);
        [Range(0f, 1f)] public float irregular = 0.45f;
        [Range(0f, 1f)] public float dryEdge = 0.30f;
        [Tooltip("整体不透明度上限。太实会像贴纸，0.8 左右更像宣纸上的墨")]
        [Range(0f, 1f)] public float opacity = 0.85f;

        [Header("诊断（只读）")]
        [SerializeField] private int _spawnCount;
        [SerializeField] private int _groundSpawnCount;
        [SerializeField] private int _activeCount;
        [SerializeField] private int _droppedCount;
        [SerializeField] private int _peakActive;

        public int SpawnCount => _spawnCount;
        public int GroundSpawnCount => _groundSpawnCount;
        public int ActiveCount => _activeCount;
        public int DroppedCount => _droppedCount;
        public int PeakActive => _peakActive;
        public bool PoolReady => _dots != null;

        // ---------------- 内部 ----------------
        private class Dot
        {
            public Transform tr;
            public MeshRenderer mr;
            public MaterialPropertyBlock mpb;
            public Vector3 vel;
            public float born;
            public float size;
            public float spinDeg;
            public float lifespan;
            public bool billboard;      // true = 朝相机；false = 平铺地面
            public bool active;
        }

        private static readonly int IdAlpha = Shader.PropertyToID("_Alpha");
        private static readonly int IdSeed = Shader.PropertyToID("_Seed");
        private static readonly int IdColor = Shader.PropertyToID("_Color");
        private static readonly int IdIrregular = Shader.PropertyToID("_Irregular");
        private static readonly int IdDryEdge = Shader.PropertyToID("_DryEdge");

        private readonly List<Dot> _dots = new List<Dot>();
        private Material _material;
        private Mesh _quad;
        private Camera _cam;
        private bool _warned;
        private System.Random _rng;

        private void Awake()
        {
            Instance = this;
            _rng = new System.Random(20260915);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (_material != null) Destroy(_material);
        }

        // ==================================================================
        //  静态入口（受击方调这两个，不需要认识本类的实现）
        // ==================================================================

        /// <summary>命中溅墨：在 <paramref name="point"/> 朝 <paramref name="dir"/> 甩出几个墨点。</summary>
        public static void Spawn(Vector3 point, Vector3 dir, float strength = 1f)
        {
            var it = Instance;
            if (it == null) return;
            it.SplashInternal(point, dir, strength);
        }

        /// <summary>落地墨花：在 <paramref name="groundPoint"/> 的地面绽开一圈墨。</summary>
        public static void SpawnGround(Vector3 groundPoint, float radiusScale = 1f)
        {
            var it = Instance;
            if (it == null) return;
            it.GroundInternal(groundPoint, radiusScale);
        }

        // ==================================================================
        //  实现
        // ==================================================================

        private void EnsurePool()
        {
            if (_dots.Count > 0) return;

            var sh = Shader.Find("InkWash/InkSplash");
            if (sh == null)
            {
                WarnOnce("[InkHitVfx] 找不到 shader: InkWash/InkSplash —— 溅墨不会出现。");
                return;
            }
            _material = new Material(sh);
            _material.hideFlags = HideFlags.HideAndDontSave;
            _material.SetColor(IdColor, inkColor);
            _material.SetFloat(IdIrregular, irregular);
            _material.SetFloat(IdDryEdge, dryEdge);

            _quad = BuildQuadMesh();

            for (int i = 0; i < Mathf.Max(1, poolSize); i++)
            {
                var go = new GameObject("InkDot" + i);
                go.transform.SetParent(transform, false);
                var mf = go.AddComponent<MeshFilter>();
                mf.sharedMesh = _quad;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = _material;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;

                var dot = new Dot
                {
                    tr = go.transform,
                    mr = mr,
                    mpb = new MaterialPropertyBlock(),
                    active = false,
                };
                go.SetActive(false);
                _dots.Add(dot);
            }
        }

        /// <summary>
        /// 程序化生成一张朝向 +Z 的单位四边形（枢轴在**中心**）。
        /// 不用 <c>CreatePrimitive</c>：它会顺带建一个 Collider，还得再删；
        /// 而且它给的 mesh 是共享资产，改一下会影响所有用到它的对象。
        /// </summary>
        private static Mesh BuildQuadMesh()
        {
            var m = new Mesh();
            m.name = "InkSplashQuad";
            m.hideFlags = HideFlags.HideAndDontSave;
            m.vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, 0f),
                new Vector3( 0.5f, -0.5f, 0f),
                new Vector3(-0.5f,  0.5f, 0f),
                new Vector3( 0.5f,  0.5f, 0f),
            };
            m.uv = new[]
            {
                new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(0f, 1f), new Vector2(1f, 1f),
            };
            // 绕序：从 +Z 看逆时针（(-.5,-.5) → (.5,-.5) → (.5,.5) → (-.5,.5)）
            m.triangles = new[] { 0, 1, 3, 0, 3, 2 };
            // ★ 必须重算法线，否则 URP 拿不到光照（本项目在程序化网格上栽过两次）
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        private float NextFloat() { return (float)_rng.NextDouble(); }
        private float Range(float a, float b) { return a + (b - a) * NextFloat(); }

        private Dot TakeDot()
        {
            for (int i = 0; i < _dots.Count; i++)
                if (!_dots[i].active) return _dots[i];
            return null;
        }

        private void SplashInternal(Vector3 point, Vector3 dir, float strength)
        {
            EnsurePool();
            if (_dots.Count == 0) return;

            if (dir.sqrMagnitude < 1e-6f) dir = Vector3.up;
            dir.Normalize();

            int n = Mathf.Max(1, Mathf.RoundToInt(dotsPerHit * Mathf.Clamp(strength, 0.4f, 2f)));

            for (int i = 0; i < n; i++)
            {
                var d = TakeDot();
                if (d == null) { _droppedCount++; continue; }

                // 锥形飞散：主方向 ± 约 35°
                Vector3 spread = (Randomness(dir) * 0.55f + dir * 0.85f).normalized;
                var go = d.tr.gameObject;
                go.SetActive(true);
                d.tr.position = point + Randomness(Vector3.up) * 0.04f;
                d.size = Range(splashSize.x, splashSize.y) * Mathf.Clamp(strength, 0.6f, 1.8f);
                d.tr.localScale = Vector3.one * d.size;
                d.vel = spread * flySpeed * Range(0.7f, 1.35f);
                d.spinDeg = Range(120f, 480f) * (NextFloat() < 0.5f ? -1f : 1f);
                d.born = Time.time;
                d.lifespan = life * Range(0.75f, 1.25f);
                d.billboard = true;
                d.active = true;

                d.mpb.Clear();
                d.mpb.SetFloat(IdAlpha, opacity);
                d.mpb.SetFloat(IdSeed, Range(0f, 30f));
                d.mr.SetPropertyBlock(d.mpb);

                _spawnCount++;
            }
        }

        private void GroundInternal(Vector3 groundPoint, float radiusScale)
        {
            EnsurePool();
            if (_dots.Count == 0) return;

            for (int i = 0; i < bloomDots; i++)
            {
                var d = TakeDot();
                if (d == null) { _droppedCount++; continue; }

                var go = d.tr.gameObject;
                go.SetActive(true);
                // 略微抬高，避免与地面 z-fighting（墨花在宣纸上是"渗进纸里"，不该闪烁）
                d.tr.position = groundPoint + new Vector3(Range(-0.18f, 0.18f), 0.012f + i * 0.004f, Range(-0.18f, 0.18f));
                d.tr.rotation = Quaternion.Euler(90f, Range(0f, 360f), 0f);   // 平铺：绕 X 转 90°
                d.size = bloomRadius * radiusScale * Range(0.55f, 1.0f);
                d.vel = Vector3.zero;
                d.spinDeg = 0f;
                d.born = Time.time;
                d.lifespan = bloomLife * Range(0.85f, 1.15f);
                d.billboard = false;
                d.active = true;

                d.mpb.Clear();
                d.mpb.SetFloat(IdAlpha, 0f);
                d.mpb.SetFloat(IdSeed, Range(0f, 30f));
                d.mr.SetPropertyBlock(d.mpb);

                _groundSpawnCount++;
            }
        }

        /// <summary>在与 <paramref name="basis"/> 垂直的平面内取一个随机单位向量。</summary>
        private Vector3 Randomness(Vector3 basis)
        {
            Vector3 a = Vector3.Cross(basis, Vector3.up);
            if (a.sqrMagnitude < 1e-4f) a = Vector3.Cross(basis, Vector3.right);
            a.Normalize();
            Vector3 b = Vector3.Cross(basis, a).normalized;
            float ang = Range(0f, Mathf.PI * 2f);
            return (a * Mathf.Cos(ang) + b * Mathf.Sin(ang)).normalized;
        }

        private void Update()
        {
            if (_dots.Count == 0) return;

            if (_cam == null) _cam = Camera.main;
            int active = 0;

            for (int i = 0; i < _dots.Count; i++)
            {
                var d = _dots[i];
                if (!d.active) continue;

                float t = (Time.time - d.born) / Mathf.Max(0.01f, d.lifespan);
                if (t >= 1f)
                {
                    d.active = false;
                    d.tr.gameObject.SetActive(false);
                    continue;
                }

                active++;

                if (d.billboard)
                {
                    // 溅墨：飞散 + 重力 + 阻尼，并始终面向相机
                    d.vel += Vector3.up * gravity * Time.deltaTime;
                    d.vel -= d.vel * Mathf.Clamp01(drag * Time.deltaTime);
                    d.tr.position += d.vel * Time.deltaTime;
                    if (_cam != null)
                        d.tr.rotation = Quaternion.LookRotation(d.tr.position - _cam.transform.position);
                    else
                        d.tr.rotation = Quaternion.identity;
                    if (d.spinDeg != 0f)
                        d.tr.localRotation *= Quaternion.Euler(0f, 0f, d.spinDeg * Time.deltaTime);
                }
                else
                {
                    // 墨花：从中心向外扩散（半径按 sqrt 推进，看起来像渗开而不是弹开）
                    float grow = Mathf.Sqrt(Mathf.Clamp01(t * 1.6f));
                    d.tr.localScale = Vector3.one * (d.size * (0.25f + 0.75f * grow));
                }

                // 不透明度：溅墨先快后慢地淡；墨花先淡出再消失（先"显影"再"褪"）
                float a;
                if (d.billboard)
                {
                    a = Mathf.Pow(1f - t, 0.75f);
                }
                else
                {
                    float appear = Mathf.Clamp01(t / 0.18f);
                    float fade = 1f - Mathf.Clamp01((t - 0.45f) / 0.55f);
                    a = appear * fade;
                }

                d.mpb.SetFloat(IdAlpha, opacity * a);
                d.mr.SetPropertyBlock(d.mpb);
            }

            _activeCount = active;
            if (active > _peakActive) _peakActive = active;
        }

        private void WarnOnce(string msg)
        {
            if (_warned) return;
            _warned = true;
            Debug.LogWarning(msg);
        }

        public void ResetDiagnostics()
        {
            _spawnCount = 0;
            _groundSpawnCount = 0;
            _droppedCount = 0;
            _peakActive = 0;
        }

        /// <summary>把所有墨点立刻收回池子（验收复位用）。</summary>
        public void ClearAll()
        {
            for (int i = 0; i < _dots.Count; i++)
            {
                _dots[i].active = false;
                if (_dots[i].tr != null) _dots[i].tr.gameObject.SetActive(false);
            }
            _activeCount = 0;
        }

        public string Describe()
        {
            return "溅墨 " + _spawnCount + " 次　墨花 " + _groundSpawnCount + " 次　"
                 + "当前活跃 " + _activeCount + "（峰值 " + _peakActive + "）　丢弃 " + _droppedCount;
        }
    }
}
