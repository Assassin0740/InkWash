using UnityEngine;
using InkWash.Player;
using InkWash.CameraRig;

namespace InkWash.Effects
{
    /// <summary>
    /// 刀光特效（问题 4「攻击没有特效」）。
    ///
    /// 全程序化生成，不依赖任何贴图素材 —— 理由：
    ///   1. 本项目走水墨风格，笔触本来就更适合程序化生成，且可写进论文当作实现点；
    ///   2. 避免再去下载 / 授权第三方的拖尾贴图。
    ///
    /// 三样东西构成一次挥砍的视觉：
    ///   a) **刀锋拖尾**：挂在右手骨骼上的 TrailRenderer。用「前宽后窄」的宽度曲线，
    ///      轨迹自然收成毛笔的飞白笔触，而不是一根等宽塑料条。
    ///   b) **弧光**：程序化生成的扇形网格，挥砍瞬间在身前铺开，短促放大 + 淡出。
    ///   c) **震屏**：命中时刻调用相机震动，把"打到了"这件事从视觉传到体感。
    ///
    /// 拖尾默认关闭，只在挥砍窗口内发射，否则角色待机时也会拖出一条线。
    ///
    /// 武器：`weaponPrefab` 有值就挂真实模型（F3「古代宝剑」），否则退回程序化占位剑。
    /// 真实模型的挂点对齐**全部烘在预制体里**（Assets/_Project/Prefabs/Weapons/W_Sword.prefab），
    /// 本组件只负责 Instantiate 到右手骨下 —— 刻意不在代码里再存一份挂点偏移/缩放。
    ///
    /// 材质用自研的 `InkWash/InkSlash`（见 Assets/_Project/Shaders/InkSlash.shader）：
    /// URP 内置 Unlit **不读顶点色**，会导致拖尾的墨色渐隐与弧光的收笔全部失效，
    /// 所以必须换成读 COLOR 语义的着色器。
    /// </summary>
    [DisallowMultipleComponent]
    public class SwordVfx : MonoBehaviour
    {
        [Header("绑定")]
        [Tooltip("玩家控制器，留空自动找")]
        public PlayerController player;

        [Tooltip("刀锋拖尾的挂点（右手骨骼），留空自动找 hand_r")]
        public Transform bladeAnchor;

        [Header("材质")]
        [Tooltip("留空则 Shader.Find(\"InkWash/InkSlash\")。显式赋值能让打包时把这个 Shader 收进去")]
        public Shader inkShader;

        [Tooltip("飞白强度：0 = 平滑边缘，越大笔触越干")]
        [Range(0f, 1f)] public float flyingWhite = 0.35f;

        [Header("刀锋拖尾")]
        [Tooltip("刀锋相对手骨骼的局部偏移（骨骼沿自身 +Y 延伸，所以剑尖在 +Y 方向）")]
        public Vector3 bladeLocalOffset = new Vector3(0f, 0.62f, 0f);

        public float trailTime = 0.16f;
        public float trailStartWidth = 0.34f;
        public float trailEndWidth = 0.02f;

        [Header("水墨配色")]
        [Tooltip("墨色（拖尾与弧光共用）")]
        public Color inkColor = new Color(0.06f, 0.06f, 0.08f, 0.92f);

        [Tooltip("拖尾末端的淡出色（alpha 归零，靠宽度曲线收笔）")]
        public Color inkFadeColor = new Color(0.06f, 0.06f, 0.08f, 0f);

        [Header("弧光")]
        public bool enableArc = true;
        public float arcRadius = 1.45f;
        public float arcThickness = 0.85f;
        public float arcSweepDeg = 130f;
        public float arcLifetime = 0.26f;

        [Tooltip("弧光相对角色正面的俯仰偏角（正数=从右上劈到左下）")]
        public float arcTiltDeg = -28f;

        [Tooltip("弧光生成点相对角色原点的高度")]
        public float arcHeight = 1.15f;

        [Header("占位刀身（仅在 weaponPrefab 留空时使用）")]
        [Tooltip("角色手上没有武器模型时，挥砍动作会读不出来（看着像空手摆姿势）。\n" +
                 "打开后挂一把程序化生成的三维直剑兜底。\n" +
                 "⚠ 一旦 weaponPrefab 有值，本项**自动失效** —— 真实模型优先。")]
        public bool placeholderBlade = true;

        [Tooltip("剑身长度。⚠ 单位是**模型容器空间**（即 Visual.localScale 生效前的空间），\n" +
                 "换模型改了 Visual.localScale 必须同步按 1/缩放 折算，否则长短宽窄全错。")]
        public float bladeLength = 1.05f;

        [Tooltip("剑身宽度。同上，**模型容器空间**。\n" +
                 "⚠ 本项目换 Feng 时漏折算了这一项，世界宽被撑到 0.166 m（1 m 长的剑宽 16.6 cm）\n" +
                 "→ 剑看起来是块平板。折算是 0.075 / 2.213 = 0.03389。")]
        public float bladeWidth = 0.075f;

        [Header("正式武器")]
        [Tooltip("有正式武器模型就赋值。赋值后优先用真实模型，placeholderBlade 只在留空时兜底。\n" +
                 "预制体请遵循 socket 约定：**根节点保持 identity**（根原点 = 挂载点），\n" +
                 "对齐用的位移/旋转/缩放放在子节点里。这样本组件就不需要再存「挂点偏移 / 缩放」\n" +
                 "之类的序列化字段 —— 少一个字段就少一次「换模型漏折算」。")]
        public GameObject weaponPrefab;

        [Header("拖尾发射门控")]
        // 问题：挥砍事件一到就开拖尾，可这时候刀还在抬手准备、几乎没速度，
        // 于是"刀还没动，光先出来了"，看着像一条凭空出现又消失的黑线。
        // 解法：按**刀刃线速度**门控 —— 抬刀阶段速度不够就不发射，等真正抡起来才出光；
        // 收招速度掉下来就停，尾巴自然由 TrailRenderer.time 淡出。
        [Tooltip("刀刃线速度超过本值才开始拖尾（米/秒）")]
        public float trailMinSpeed = 3.0f;

        [Tooltip("挥砍开始后的强制静默期（秒），跳过起手准备动作")]
        public float trailStartDelay = 0.05f;

        [Tooltip("已经在发射时，速度低于本值就停止（比进入阈值低，形成迟滞）")]
        public float trailHoldMinSpeed = 1.6f;

        [Header("震屏")]
        public bool enableShake = true;
        public float shakeAmplitude = 0.075f;
        public float shakeDuration = 0.13f;

        // ---------------- 对外只读探针（自动化验收用，勿删） ----------------
        // 验收必须能"量化"特效是否真的出现了，而不是靠人眼截图。
        // 这些都是只读计数 / 状态，不参与任何表现逻辑。

        /// <summary>已发生的挥砍次数（SwingStarted 触发计数）。</summary>
        public int SwingCount { get; private set; }

        /// <summary>已生成的弧光次数。</summary>
        public int ArcSpawnCount { get; private set; }

        /// <summary>已触发的震屏次数（HitMoment 触发计数）。</summary>
        public int ShakeTriggerCount { get; private set; }

        /// <summary>当前场景里存活的弧光实例数（弧光生命期 0.26s，可据此判断"此刻画面有刀光"）。</summary>
        public static int ActiveArcCount { get; private set; }

        /// <summary>拖尾当前是否在发射。</summary>
        public bool IsTrailEmitting => _trail != null && _trail.emitting;

        /// <summary>拖尾当前的顶点数 —— 大于 0 才说明屏幕上真的有那道笔触，而不只是"开关打开了"。</summary>
        public int TrailPositionCount => _trail != null ? _trail.positionCount : 0;

        /// <summary>手上是否真的有武器：正式模型或程序化占位剑，二者其一。验收判据。</summary>
        public bool HasWeapon => _weapon != null || (_bladeMesh != null && bladeAnchor != null);

        /// <summary>当前挂的是哪把武器（验收报告直接打印，免得看不出是真实模型还是占位剑）。</summary>
        public string WeaponName => _weapon != null
            ? _weapon.name
            : (_bladeMesh != null ? "PlaceholderBlade(程序化占位)" : "无");

        /// <summary>右手骨骼是否解析成功（拖尾挂点）。</summary>
        public bool HasBladeAnchor => bladeAnchor != null;

        /// <summary>刀刃当前线速度（米/秒）。拖尾门控的输入量，也用于验收判定"出光时刀已经动了"。</summary>
        public float BladeSpeed { get; private set; }

        /// <summary>本段挥砍里，拖尾**第一次**真正开始发射时的刀刃速度。要求 ≥ trailMinSpeed。</summary>
        public float EmitStartBladeSpeed { get; private set; } = -1f;

        /// <summary>拖尾发射窗口被真正点亮的次数。</summary>
        public int TrailEmitStartCount { get; private set; }

        // ---------------- 内部 ----------------
        private TrailRenderer _trail;
        private Material _inkMat;
        private Material _bladeMat;
        private Mesh _arcMesh;
        private Mesh _bladeMesh;
        private GameObject _weapon;      // 运行时克隆出来的正式武器（weaponPrefab）
        private ThirdPersonCamera _cam;

        // 拖尾发射窗口（由 Update 逐帧按刀刃速度开关，不再用协程定时）
        private bool _emitWindowActive;
        private float _emitWindowStart;
        private float _emitWindowEnd;
        private bool _emissionStartedThisSwing;
        private Vector3 _lastBladePos;
        private bool _hasLastBladePos;

        // 各段挥砍的拖尾发射窗口时长（秒），与连击时长对应
        private static readonly float[] kEmitDuration = { 0.34f, 0.42f, 0.95f };

        /// <summary>
        /// 静态计数复位。关闭"域重载"时静态字段会跨 Play 会话残留，
        /// 不重置会让第二次进入 Play 的验收报告虚高。
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticProbes() { ActiveArcCount = 0; }

        private void Awake()
        {
            if (player == null) player = GetComponentInParent<PlayerController>();
            if (player == null) player = FindObjectOfType<PlayerController>();

            _inkMat = CreateInkMaterial();
            _arcMesh = BuildArcMesh(arcRadius, arcThickness, arcSweepDeg, 28);

            SetupTrail();
            BuildWeapon();

            if (player != null)
            {
                player.SwingStarted += OnSwingStarted;
                player.HitMoment += OnHitMoment;
            }
            else
            {
                Debug.LogWarning("[SwordVfx] 场景里找不到 PlayerController，刀光不会触发");
            }
        }

        private void OnDestroy()
        {
            if (player != null)
            {
                player.SwingStarted -= OnSwingStarted;
                player.HitMoment -= OnHitMoment;
            }
            if (_inkMat != null) Destroy(_inkMat);
            if (_bladeMat != null) Destroy(_bladeMat);
            if (_arcMesh != null) Destroy(_arcMesh);
            if (_bladeMesh != null) Destroy(_bladeMesh);
            if (_weapon != null) Destroy(_weapon);
        }

        // ------------------------------------------------------------------
        // 拖尾
        // ------------------------------------------------------------------

        /// <summary>
        /// 找右手骨骼。注意坑：SwordVfx 挂在 Player 根节点，而 Animator 在子节点 Visual 上，
        /// 所以 GetComponentInParent 找不到 —— 必须优先用 PlayerController 已经解析好的 animator，
        /// 再退化为向下查找。
        /// </summary>
        private Transform ResolveBladeAnchor()
        {
            if (bladeAnchor != null) return bladeAnchor;

            Animator anim = player != null ? player.animator : null;
            if (anim == null) anim = GetComponentInChildren<Animator>();
            if (anim == null) anim = GetComponentInParent<Animator>();

            if (anim != null && anim.isHuman)
                return anim.GetBoneTransform(HumanBodyBones.RightHand);

            return null;
        }

        private void SetupTrail()
        {
            bladeAnchor = ResolveBladeAnchor();
            if (bladeAnchor == null)
            {
                Debug.LogWarning("[SwordVfx] 找不到右手骨骼，刀光拖尾不可用");
                return;
            }
            if (_inkMat == null) return;

            var go = new GameObject("BladeTrail_Anchor");
            go.transform.SetParent(bladeAnchor, false);
            go.transform.localPosition = bladeLocalOffset;
            go.transform.localRotation = Quaternion.identity;

            _trail = go.AddComponent<TrailRenderer>();
            _trail.time = trailTime;
            _trail.minVertexDistance = 0.02f;
            _trail.autodestruct = false;
            _trail.emitting = false;
            _trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _trail.receiveShadows = false;
            _trail.material = _inkMat;
            _trail.numCapVertices = 4;

            // 前宽后窄 → 毛笔笔触的收笔感
            var width = new AnimationCurve();
            width.AddKey(0f, trailEndWidth);
            width.AddKey(0.12f, trailStartWidth);
            width.AddKey(1f, trailEndWidth);
            _trail.widthCurve = width;

            // 颜色键用白色，让材质自身的 _BaseColor 决定墨色 —— 否则两边相乘会二次变暗。
            // alpha 由 0 → 实 → 0，两端都收，轨迹才有淡入淡出的层次。
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(inkColor.a, 0.22f),
                        new GradientAlphaKey(0f, 1f) });
            _trail.colorGradient = grad;

            _trail.widthMultiplier = 1f;
            _trail.textureMode = LineTextureMode.Stretch;
            _trail.alignment = LineAlignment.View;
            _trail.generateLightingData = false;
        }

        // ------------------------------------------------------------------
        // 占位刀身
        // ------------------------------------------------------------------

        /// <summary>
        /// 手上没有武器模型，挥砍动作会读不出来（看着像空手摆姿势）。
        /// 这里挂一把程序化生成的**三维**直剑做占位（菱形截面剑身 + 剑格，见 BuildBladeMesh）。
        /// 有正式武器模型后把 placeholderBlade 关掉，或直接把模型挂到 bladeAnchor（右手骨）下。
        /// </summary>
        /// <summary>
        /// 挂武器：有正式模型优先，否则退回程序化占位剑。
        ///
        /// ⚠ 用 <c>Instantiate(prefab, parent, false)</c>：第三个参数 false 表示**保留预制体自身的
        ///   local 变换**、不按世界变换去反算。socket 约定下预制体根是 identity 所以看起来无所谓，
        ///   但传 true 会拿角色当前的姿态去反算根节点，凭空引入一次无意义的补偿。
        /// </summary>
        private void BuildWeapon()
        {
            if (bladeAnchor == null) return;

            if (weaponPrefab != null)
            {
                _weapon = Instantiate(weaponPrefab, bladeAnchor, false);
                _weapon.name = weaponPrefab.name;
                return;
            }

            BuildPlaceholderBlade();
        }

        private void BuildPlaceholderBlade()
        {
            if (!placeholderBlade || bladeAnchor == null) return;

            _bladeMat = CreateBladeMaterial();
            if (_bladeMat == null) return;

            _bladeMesh = BuildBladeMesh(bladeLength, bladeWidth);

            var go = new GameObject("PlaceholderBlade");
            go.transform.SetParent(bladeAnchor, false);
            go.transform.localPosition = Vector3.zero;
            // 骨骼沿自身 +Y 延伸，剑身也沿 +Y。
            // 绕 Y 转 45°：剑身是"左右宽、前后薄"的菱形截面，转 45° 让刃面和剑脊都不正对镜头，
            // 从正面、侧面看都能读出厚度（不转的话正对镜头时只剩一条线）。
            go.transform.localRotation = Quaternion.Euler(0f, 45f, 0f);

            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = _bladeMesh;

            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _bladeMat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }

        /// <summary>
        /// 刀身必须用**不透明**材质：墨材质是透明混合且 ZWrite Off，
        /// 拿它画实体刀会因为不写深度而排序错乱（刀穿到身体前面/后面乱跳）。
        /// </summary>
        private static Material CreateBladeMaterial()
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Standard");
            if (shader == null) return null;

            var m = new Material(shader) { name = "M_PlaceholderBlade(Runtime)" };
            var dark = new Color(0.10f, 0.10f, 0.12f, 1f);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", dark);
            if (m.HasProperty("_Color")) m.SetColor("_Color", dark);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0.25f);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.55f);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", 0.55f);
            return m;
        }

        /// <summary>
        /// 程序化占位剑：**真三维**剑身（菱形截面，左右为刃、前后为脊）+ 剑格。
        /// 从任何角度看都有体积感，约 90 个三角形。
        ///
        /// ⚠ 三个踩过的坑，改这里前先读：
        ///
        /// 1. **必须有法线**（`RecalculateNormals()`）。最早那版只调了 `RecalculateBounds()`，
        ///    网格没有法线 → URP/Lit 拿不到光照方向 → 整个剑身永远是同一个颜色，
        ///    再好的三维形状也读不出来，看上去就是一块平板。这是"像平板"的**主因之一**。
        ///
        /// 2. **宽度必须跟着模型缩放走**。长度宽度都是**模型容器空间**的量，
        ///    换模型改了 `Visual.localScale` 就必须同步折算，漏一个比例就崩
        ///    （本项目换 Feng 时就漏了 `bladeWidth`：世界宽被撑到 0.166 m，
        ///    一把 1 m 长的剑宽 16.6 cm，直接成船桨 —— 这是"像平板"的**另一半原因**）。
        ///
        /// 3. 尺寸全部**由 length / width 推导**，不新增序列化字段 ——
        ///    序列化字段加了就得改预制体，多一个漏折算的机会。
        /// </summary>
        private static Mesh BuildBladeMesh(float length, float width)
        {
            length = Mathf.Max(length, 0.05f);
            width = Mathf.Max(width, 0.004f);

            // ---- 由剑身尺寸推导各部件（比例按"1 m 长的剑"手调过）----
            float halfW = width * 0.5f;                  // 剑身半宽（左右到刃）
            float ridge = halfW * 0.30f;                 // 剑脊半厚（前后）
            float guardH = length * 0.048f;              // 剑格半展（左右）
            // ⚠ 剑格的前后厚度必须绑**剑脊厚度**，不能绑剑身宽度。
            //   绑宽度会得到 0.088（局部）= 0.195 m 世界，剑格变成前后鼓 19.5 cm 的十字架。
            //   实测踩过：第一版写成 halfW * 2.6f，网格 Z 向尺寸直接 0.0881。
            float guardD = ridge * 1.6f;                 // 剑格前后厚度（薄）
            float guardT = length * 0.012f;              // 剑格沿 Y 的厚度
            float bladeFrom = guardT;                    // 剑身从剑格上沿起
            float bladeTo = guardT + length;

            var verts = new System.Collections.Generic.List<Vector3>();
            var tris = new System.Collections.Generic.List<int>();

            // ---- 剑格：矩形截面短管，两端封口 ----
            var guardSec = new[]
            {
                new Vector2(guardH, guardD), new Vector2(-guardH, guardD),
                new Vector2(-guardH, -guardD), new Vector2(guardH, -guardD),
            };
            AppendTube(verts, tris, guardSec,
                       new[] { -guardT, guardT }, new[] { 1f, 1f },
                       capStart: true, capEnd: true);

            // ---- 剑身：菱形截面，前 78% 等宽，之后线性收窄，最后聚成剑尖 ----
            // 截面顺序必须是 +X → +Z → -X → -Z，否则面片朝里（见 AppendTube 注释）
            var bladeSec = new[]
            {
                new Vector2(halfW, 0f), new Vector2(0f, ridge),
                new Vector2(-halfW, 0f), new Vector2(0f, -ridge),
            };
            const int segs = 12;
            const float tipStart = 0.78f;
            var ys = new float[segs + 1];
            var sc = new float[segs + 1];
            for (int i = 0; i <= segs; i++)
            {
                float t = i / (float)segs;
                ys[i] = Mathf.Lerp(bladeFrom, bladeTo, t);
                sc[i] = t <= tipStart
                    ? 1f
                    : Mathf.Max(0.10f, 1f - (t - tipStart) / (1f - tipStart));
            }
            int lastRing = AppendTube(verts, tris, bladeSec, ys, sc,
                                      capStart: true, capEnd: false);

            // 剑尖：把最后一环聚到一个顶点，收成真正的尖而不是齐头
            int apex = verts.Count;
            verts.Add(new Vector3(0f, bladeTo, 0f));
            for (int i = 0; i < bladeSec.Length; i++)
            {
                int j = (i + 1) % bladeSec.Length;
                tris.Add(lastRing + i); tris.Add(apex); tris.Add(lastRing + j);
            }

            var mesh = new Mesh { name = "PlaceholderBladeMesh" };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            // ⚠ 坑 1：没有法线 → 光照失效 → 三维形状读不出来。不能删。
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// 把一个横截面沿 +Y 拉伸成管，逐环可缩放（用来做收尖）。
        /// 返回**最后一环的起始顶点号**（调用方要拿它聚尖或封口）。
        ///
        /// ⚠ 横截面顶点必须按 <c>+X → +Z → -X → -Z</c> 的顺序给（俯视逆时针），
        ///   否则生成的面片法线朝里，模型会内外翻转、背面剔除后直接看不见。
        /// </summary>
        private static int AppendTube(System.Collections.Generic.List<Vector3> v,
                                      System.Collections.Generic.List<int> t,
                                      Vector2[] sec, float[] ys, float[] scales,
                                      bool capStart, bool capEnd)
        {
            int n = sec.Length;
            int rings = ys.Length;
            int baseIdx = v.Count;

            for (int r = 0; r < rings; r++)
            {
                float s = scales[r];
                float y = ys[r];
                for (int i = 0; i < n; i++)
                    v.Add(new Vector3(sec[i].x * s, y, sec[i].y * s));
            }

            // 侧壁：环 r 与 r+1 之间连一圈四边形
            for (int r = 0; r < rings - 1; r++)
            {
                int a = baseIdx + r * n;
                int b = baseIdx + (r + 1) * n;
                for (int i = 0; i < n; i++)
                {
                    int j = (i + 1) % n;
                    t.Add(a + i); t.Add(b + i); t.Add(b + j);
                    t.Add(a + i); t.Add(b + j); t.Add(a + j);
                }
            }

            if (capStart) AppendFan(v, t, baseIdx, n, ys[0], false);
            if (capEnd) AppendFan(v, t, baseIdx + (rings - 1) * n, n, ys[rings - 1], true);

            return baseIdx + (rings - 1) * n;
        }

        /// <summary>给一环截面封口：加一个中心顶点，扇形连到环上。up=true 朝 +Y，false 朝 -Y。</summary>
        private static void AppendFan(System.Collections.Generic.List<Vector3> v,
                                      System.Collections.Generic.List<int> t,
                                      int ringStart, int n, float y, bool up)
        {
            // 求该环的中心（按环上顶点均值，避免依赖外部再传一次参数）
            Vector3 c = Vector3.zero;
            for (int i = 0; i < n; i++) c += v[ringStart + i];
            c /= n;
            int center = v.Count;
            v.Add(new Vector3(c.x, y, c.z));

            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                if (up)
                {
                    t.Add(ringStart + i); t.Add(center); t.Add(ringStart + j);
                }
                else
                {
                    t.Add(ringStart + i); t.Add(ringStart + j); t.Add(center);
                }
            }
        }

        // ------------------------------------------------------------------
        // 事件
        // ------------------------------------------------------------------

        private void OnSwingStarted(int step)
        {
            SwingCount++;
            if (_trail == null) return;

            float dur = kEmitDuration[Mathf.Clamp(step - 1, 0, kEmitDuration.Length - 1)];
            _emitWindowActive = true;
            _emitWindowStart = Time.time + Mathf.Max(0f, trailStartDelay);
            _emitWindowEnd = Time.time + dur;
            _emissionStartedThisSwing = false;
            EmitStartBladeSpeed = -1f;

            if (enableArc && _arcMesh != null && _inkMat != null) SpawnArc(step);
        }

        private void OnHitMoment(int step)
        {
            if (!enableShake) return;
            if (_cam == null) _cam = FindObjectOfType<ThirdPersonCamera>();
            if (_cam != null)
            {
                _cam.Shake(shakeAmplitude * (step >= 3 ? 1.6f : 1f), shakeDuration);
                ShakeTriggerCount++;
            }
        }

        /// <summary>
        /// 逐帧测刀刃线速度 + 按速度门控拖尾开关。
        /// 用 Update 而不是协程定时，是因为门控判据依赖"这一帧刀动得多快"，
        /// 协程里的 WaitForSeconds 拿不到这个量，只能盲开盲关。
        /// </summary>
        private void Update()
        {
            UpdateBladeSpeed();
            UpdateTrailGate();
        }

        private void UpdateBladeSpeed()
        {
            if (_trail == null) { BladeSpeed = 0f; return; }

            Vector3 p = _trail.transform.position;
            float dt = Mathf.Max(Time.deltaTime, 1e-5f);
            BladeSpeed = _hasLastBladePos ? Vector3.Distance(p, _lastBladePos) / dt : 0f;
            _lastBladePos = p;
            _hasLastBladePos = true;
        }

        private void UpdateTrailGate()
        {
            if (!_emitWindowActive) return;

            if (_trail == null) { _emitWindowActive = false; return; }

            // 窗口结束：关掉发射，剩下的顶点交给 TrailRenderer.time 自然淡出
            if (Time.time > _emitWindowEnd)
            {
                _emitWindowActive = false;
                _trail.emitting = false;
                return;
            }

            // 起手静默期：抬手准备阶段一律不出光
            if (Time.time < _emitWindowStart)
            {
                _trail.emitting = false;
                return;
            }

            // 迟滞：进入要 trailMinSpeed，已在发射只需维持 trailHoldMinSpeed
            float need = _trail.emitting ? trailHoldMinSpeed : trailMinSpeed;
            bool want = BladeSpeed >= need;

            if (want && !_trail.emitting)
            {
                // 清掉窗口开始前残留的旧顶点，否则会从上一段的位置连一条线过来
                _trail.Clear();
                if (!_emissionStartedThisSwing)
                {
                    _emissionStartedThisSwing = true;
                    EmitStartBladeSpeed = BladeSpeed;
                    TrailEmitStartCount++;
                }
            }
            _trail.emitting = want;
        }

        // ------------------------------------------------------------------
        // 弧光
        // ------------------------------------------------------------------

        private void SpawnArc(int step)
        {
            var go = new GameObject("SlashArc");            go.transform.position = transform.position
                + Vector3.up * arcHeight
                + transform.forward * 0.45f;

            // 斜劈：绕角色正面轴滚转，制造"从右上到左下"的走势；偶数段反向，连击看起来有交替
            float roll = arcTiltDeg * (step % 2 == 0 ? -1f : 1f);
            go.transform.rotation = transform.rotation * Quaternion.Euler(0f, 0f, roll);

            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = _arcMesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _inkMat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;

            float scale = step >= 3 ? 1.35f : 1f;
            go.transform.localScale = Vector3.one * scale;

            var anim = go.AddComponent<SlashArcFade>();
            anim.Init(arcLifetime, 0.55f * scale, 1.25f * scale);

            ArcSpawnCount++;
            ActiveArcCount++;
        }

        // ------------------------------------------------------------------
        // 程序化资源
        // ------------------------------------------------------------------

        /// <summary>建"墨"材质。优先用自研 Shader，再退到 URP/Unlit。</summary>
        private Material CreateInkMaterial()
        {
            Shader shader = inkShader;
            bool custom = shader != null;
            if (shader == null) shader = Shader.Find("InkWash/InkSlash");
            if (shader != null) custom = true;
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            if (shader == null)
            {
                Debug.LogError("[SwordVfx] 找不到任何可用 Shader，刀光不可用");
                return null;
            }

            var m = new Material(shader) { name = "M_InkSlash(Runtime)" };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", inkColor);
            if (m.HasProperty("_Color")) m.SetColor("_Color", inkColor);
            if (m.HasProperty("_FlyingWhite")) m.SetFloat("_FlyingWhite", flyingWhite);

            // 自研 Shader 的 Pass 里已经写死透明混合；URP/Unlit 需要用属性切换，这里兜底。
            if (m.HasProperty("_Surface"))
            {
                m.SetFloat("_Surface", 1f);
                m.SetFloat("_Blend", 0f);
                m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                m.SetFloat("_ZWrite", 0f);
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.DisableKeyword("_ALPHATEST_ON");
                m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                m.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
            }
            else if (!custom)
            {
                m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            }

            return m;
        }

        /// <summary>
        /// 生成一段环形扇面（内圈到外圈）。顶点色负责两件事：
        ///   rgb 保持 1（墨色交给材质），alpha 在弧的两端与内外圈之间渐变，模拟收笔与晕开。
        /// </summary>
        private static Mesh BuildArcMesh(float radius, float thickness, float sweepDeg, int segments)
        {
            var mesh = new Mesh { name = "SlashArcMesh" };
            int n = Mathf.Max(4, segments);
            float inner = Mathf.Max(radius - thickness, radius * 0.05f);

            var verts = new Vector3[n * 2];
            var colors = new Color[n * 2];
            var tris = new int[(n - 1) * 6];

            for (int i = 0; i < n; i++)
            {
                float t = i / (float)(n - 1);
                float ang = Mathf.Lerp(-sweepDeg * 0.5f, sweepDeg * 0.5f, t) * Mathf.Deg2Rad;

                // 两端收笔：弧的起止处变细
                float taper = Mathf.Sin(t * Mathf.PI);
                float rOut = Mathf.Lerp(inner, radius, 0.25f + 0.75f * taper);
                float rIn = inner * (0.35f + 0.65f * taper);

                Vector3 dir = new Vector3(Mathf.Sin(ang), 0f, Mathf.Cos(ang));
                verts[i * 2 + 0] = dir * rIn;
                verts[i * 2 + 1] = dir * rOut;

                float aIn = 0.10f * taper;
                float aOut = 0.95f * taper;
                colors[i * 2 + 0] = new Color(1f, 1f, 1f, aIn);
                colors[i * 2 + 1] = new Color(1f, 1f, 1f, aOut);

                if (i < n - 1)
                {
                    int o = i * 6;
                    tris[o + 0] = i * 2 + 0;
                    tris[o + 1] = i * 2 + 1;
                    tris[o + 2] = i * 2 + 3;
                    tris[o + 3] = i * 2 + 0;
                    tris[o + 4] = i * 2 + 3;
                    tris[o + 5] = i * 2 + 2;
                }
            }

            mesh.vertices = verts;
            mesh.colors = colors;
            mesh.triangles = tris;
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>弧光生命周期：短促放大 + 淡出。</summary>
        private class SlashArcFade : MonoBehaviour
        {
            private float _life;
            private float _t;
            private float _from;
            private float _to;
            private Material _mat;
            private float _baseAlpha = 0.92f;
            private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
            private static readonly int ColorId = Shader.PropertyToID("_Color");

            public void Init(float life, float fromScale, float toScale)
            {
                _life = Mathf.Max(life, 0.01f);
                _from = fromScale;
                _to = toScale;

                var mr = GetComponent<MeshRenderer>();
                if (mr == null) return;

                _mat = mr.material;   // 取实例副本，逐个体调 alpha（不污染共享材质）
                if (_mat.HasProperty(BaseColorId)) _baseAlpha = _mat.GetColor(BaseColorId).a;
                else if (_mat.HasProperty(ColorId)) _baseAlpha = _mat.GetColor(ColorId).a;
            }

            private void Update()
            {
                _t += Time.deltaTime;
                float k = Mathf.Clamp01(_t / _life);

                // 放大用 ease-out，淡出略慢于放大，视觉上有"甩出去再消散"的层次
                transform.localScale = Vector3.one * Mathf.Lerp(_from, _to, 1f - Mathf.Pow(1f - k, 2.2f));

                if (_mat != null)
                {
                    // 直接从基准 alpha 算，不做逐帧累乘 —— 累乘会随帧率漂移
                    float a = _baseAlpha * Mathf.Pow(1f - k, 1.6f);
                    var c = _mat.HasProperty(BaseColorId) ? _mat.GetColor(BaseColorId) : _mat.GetColor(ColorId);
                    c.a = a;
                    if (_mat.HasProperty(BaseColorId)) _mat.SetColor(BaseColorId, c);
                    if (_mat.HasProperty(ColorId)) _mat.SetColor(ColorId, c);
                }

                if (_t >= _life) Destroy(gameObject);
            }

            private void OnDestroy()
            {
                // 探针计数必须成对，否则退出 Play 再进会残留（静态字段不随场景重置）
                if (ActiveArcCount > 0) ActiveArcCount--;
            }
        }
    }
}
