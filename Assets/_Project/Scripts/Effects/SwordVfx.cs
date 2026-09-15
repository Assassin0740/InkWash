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

        [Tooltip("飞白强度：0 = 平滑边缘，越大笔触越干。\n" +
                 "★ 它同时会**吃掉 alpha** —— 0.35 时整笔的实心浓淡被干笔噪声拉薄一大截，\n" +
                 "  墨色变浅发灰。要保留「干」的形态又不过分削弱浓度，取 0.22。")]
        [Range(0f, 1f)] public float flyingWhite = 0.22f;

        [Header("刀锋拖尾")]
        [Tooltip("拖尾锚点相对**手骨骼**的局部偏移。\n" +
                 "⚠ 方向**不是手骨的 +Y**。握上真实武器后剑身沿手骨局部 **+Z**\n" +
                 "（拳头隧道与手指方向正交，换算到手骨空间后剑轴 = +Z、掌法向 = −X）。\n" +
                 "锚点必须**贴着剑轴**：偏离轴心的话，手骨一挥锚点就绕着一个大半径转圈，\n" +
                 "轨迹会自交成一团黑色多面体（用户看到的那团「刀光」就是这个）。\n" +
                 "自检：锚点到剑轴的垂距应为 0.0000 m。\n" +
                 "本值对应剑身约 16% 处（护手上方），改武器模型要重新按剑轴投影。")]
        public Vector3 bladeLocalOffset = new Vector3(-0.0055f, 0.0623f, 0.30f);

        [Tooltip("拖尾残留时间：越长笔触拖得越久。" +
                 "本值 0.34 略长于一段挥砍的出光窗口（0.34s），让收笔时尾巴还在，读起来才是一「笔」。")]
        public float trailTime = 0.34f;
        [Tooltip("最宽处宽度。⚠ 单位是**手骨骼所在的模型容器空间**，不是世界米！\n" +
                 "容器 Visual.localScale = 2.213，所以本值 0.118 → 世界宽 0.261 m。\n" +
                 "★ 这里踩过一次：本字段曾写成 0.26（看着像「世界 26 厘米」），\n" +
                 "  实际世界宽被放大成 0.26 × 2.213 = **0.575 m** —— 半米多宽的墨带，\n" +
                 "  正是「一道笔触」退化成「一坨墨块」的原因。改模型换了 Visual.localScale 必须同除。")]
        public float trailStartWidth = 0.118f;

        [Tooltip("两端宽度。同上，模型容器空间：0.015 → 世界 0.033 m（收笔的尖）。")]
        public float trailEndWidth = 0.015f;

        [Header("水墨配色")]
        [Tooltip("墨色（拖尾与弧光共用）")]
        public Color inkColor = new Color(0.06f, 0.06f, 0.08f, 0.92f);

        [Tooltip("拖尾末端的淡出色（alpha 归零，靠宽度曲线收笔）")]
        public Color inkFadeColor = new Color(0.06f, 0.06f, 0.08f, 0f);

        [Header("弧光")]
        public bool enableArc = true;
        [Tooltip("外半径（米）。角色身高 1.78，半径超过 2 就会扫到地上和画外")]
        public float arcRadius = 1.30f;
        [Tooltip("弧带宽度（外半径 − 内半径）。\n" +
                 "★ 别再放大：0.70 时外缘硬边 + 半米多宽的拖尾一起糊上来，\n" +
                 "  整个屏幕右下角是一块灰斑，用户看到的「积木感」有它一份。")]
        public float arcThickness = 0.46f;
        [Tooltip("扫过的角度。弧面建在**竖直平面**里，从右上（+58°）往左下扫")]
        public float arcSweepDeg = 128f;

        [Tooltip("弧光存活时长。短促才有「甩出去」的速度感；\n" +
                 "0.3 秒会让一笔墨在屏幕上驻留成一块滞后的色斑。")]
        public float arcLifetime = 0.24f;

        [Tooltip("弧光相对角色正面的滚转偏角。\n" +
                 "★ 这个角与 BuildArcMesh 里的 angStartDeg 是**叠加**的，两者相加才是屏幕上看到的走向。\n" +
                 "  -28 时叠加后弧心压到 -65°，整笔落到了胯下偏右，不像「过顶斜劈」。")]
        public float arcTiltDeg = -10f;

        [Tooltip("弧光生成点相对角色原点的高度（弧心大致对着胸口）")]
        public float arcHeight = 1.25f;

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
        // 解法：按**刀刃线速度**门控 —— 抬刀阶段速度不够就不发射，等真正抡起来才出光。
        //
        // ★ 阈值不能定高：实测本套攻击动画的刀刃线速度是「起手瞬间 9~11 m/s → 立刻掉到 1 m/s 上下」，
        //   阈值取 3.0 / 2.6 时整段挥砍只有 1 帧满足条件，轨迹攒不到顶点。
        //   现在取 1.0，作用是**只挡完全静止**，真正决定窗口长度的是上面 kEmitDuration。
        // ★ 阈值本轮又下调了一次（1.0 → 0.15）。逐帧实测刀刃锚点的线速度曲线是：
        //   起手瞬间 6.6 m/s，**随后长期落在 0.1~0.6 m/s**（挥砍主要是绕腕/肩的**转动**，
        //   锚点平动很小）。取 1.0 时发射窗口 0.34s 里只有前 3 帧满足条件，轨迹只攒下 5 个顶点 ——
        //   屏幕上根本看不出有笔触。所以门控只保留"确实完全静止"这一个用途，
        //   真正决定出光长短的是 kEmitDuration 那个窗口。
        [Tooltip("刀刃线速度超过本值才开始拖尾（米/秒）。**只用来挡「刀完全静止」**，\n" +
                 "千万不要调高 —— 挥砍的刀刃平动速度实测长期只有 0.1~0.6 m/s，\n" +
                 "调高到 1.0 会让整段挥砍只剩 3 帧出光。")]
        public float trailMinSpeed = 0.15f;

        [Tooltip("挥砍开始后的强制静默期（秒），跳过起手准备动作")]
        public float trailStartDelay = 0.04f;

        [Tooltip("已经在发射时，速度低于本值就停止（比进入阈值低，形成迟滞）")]
        public float trailHoldMinSpeed = 0.05f;

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

        /// <summary>运行时克隆出来的正式武器实例；没配 weaponPrefab 时为 null。</summary>
        /// <remarks>给 <see cref="InkWash.Player.CombatStance"/> 这类组件用 —— 它负责把武器在
        /// 「右手 ↔ 背部」之间搬运，直接复用本实例，避免再克隆一份出来两把剑。</remarks>
        public GameObject WeaponInstance => _weapon;

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
            // 顶点间距要小。这里的教训是**一路降下来的**：
            //   0.02 → 整段只有 2~5 个顶点，轨迹是"一根短粗的棒"；
            //   0.012 → 5~6 个（本轮实测），仍然看不出笔触；
            //   0.004 → 低速段也能连续出点，才真的是一道墨。
            // 根因：刀刃锚点是"绕着腕/肩转"，**平动速度**远小于肉眼以为的挥剑速度。
            _trail.minVertexDistance = 0.004f;
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

            // ★ 清空必须发生在**一段挥砍开始时**，不能放在"检测到该发射"的那一帧。
            //   后者是一笔连续墨的粉碎机：刀刃速度在阈值附近抖动时，emitting 会反复关→开，
            //   每开一次就 Clear 一次，实测整段挥砍只攒下 **2~5 个顶点** ——
            //   配上 0.5 m 的宽度就成了"一坨黑块"，而不是一道笔触。
            _trail.Clear();

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
            // ★ 弧光以**角色自身**为中心，不往前推。
            //   原来写成 `+ transform.forward * 0.45f`，而在第三人称里相机就在角色背后 ——
            //   "角色前方"正是**画面深处**，于是弧光整个跑到角色背后被身体挡掉大半，
            //   屏幕上只剩左右两侧露出一点边（用户看到的就是"墙上多了块灰影"）。
            //   以角色为中心之后，弧面横跨角色两侧，无论朝哪打都能看到完整的一笔。
            var go = new GameObject("SlashArc");            go.transform.position = transform.position
                + Vector3.up * arcHeight;

            // 斜劈：绕角色正面轴滚转，制造"从右上到左下"的走势；偶数段反向，连击看起来有交替
            float roll = arcTiltDeg * (step % 2 == 0 ? -1f : 1f);
            go.transform.rotation = transform.rotation * Quaternion.Euler(0f, 0f, roll);

            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = _arcMesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _inkMat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;

            float scale = step >= 3 ? 1.15f : 1f;
            go.transform.localScale = Vector3.one * scale;

            // 放大曲线：**接近满尺寸出现，再略微外扩**。
            // 原来从 0.55 涨到 1.25 —— 一笔墨要花小半条命才长到该有的大小，
            // 前半程是"一小片灰"，看着像没出光；而挥砍本身只有 0.2 秒。
            var anim = go.AddComponent<SlashArcFade>();
            anim.Init(arcLifetime, 0.84f * scale, 1.06f * scale);

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
        /// 生成一段弧形扇面（**五条环：内缘 / 内侧 / 中脊 / 外侧 / 外缘**）。顶点色负责两件事：
        ///   rgb 保持 1（墨色交给材质），alpha 沿「跨笔宽度」做软边、沿扫掠方向做收笔（两端 0）。
        ///
        /// ★ 环数为什么是**五**（两条 → 三条 → 五条，两次都是被画面逼出来的）：
        ///   · 两条环：顶点色只能表达"内淡外浓"，**外缘仍是满 alpha 的几何直边**，
        ///     屏幕上一块剪影式硬边色块 —— 用户说的"不像水墨、像积木"。
        ///   · 三条环：加一条中脊，跨笔两侧能归零了，形状对了；但"满浓"只落在**一条线**上，
        ///     屏幕上是一道**细灰线**，不是毛笔的横截面（修完剖分后看画面才发现）。
        ///   · 五条环：浓淡剖面 0.06 / 0.70 / 1.0 / 0.70 / 0.06 ⇒ 中间是一条**有宽度**的浓墨。
        ///   三次调整的共同教训：**跨笔浓淡是"形状"问题，环数不够就是表达不出来**，
        ///   再怎么调材质 alpha 都改不掉。
        ///
        /// ★ 弧面必须建在**竖直平面（XY）**里，不能建在水平面（XZ）里。
        ///   原来写成 `dir = (sin(ang), 0, cos(ang))` —— 顶点全在 y=0 平面上，
        ///   而第三人称相机俯角只有十几度、基本是平视，**水平面片几乎正对视线被"压扁成一条线"**，
        ///   屏幕上只剩一片比地面略深的色斑。用户反馈"没有挥墨的那种效果感觉"，
        ///   一半原因在这儿（另一半是拖尾被速度门控卡成了碎块，见 UpdateTrailGate）。
        ///   改成 XY 平面后，弧面正对角色前方，而相机正好在角色背后 —— 一抬眼就是一整笔。
        ///
        /// 角度约定：从**右上（+40°）扫到左下（+40° − sweepDeg）**，
        /// 对应"举刀过顶 → 斜劈到身前"的走势，与连击的 tilt 滚转叠加后每段方向略有不同。
        /// </summary>
        private static Mesh BuildArcMesh(float radius, float thickness, float sweepDeg, int segments)
        {
            var mesh = new Mesh { name = "SlashArcMesh" };
            int n = Mathf.Max(4, segments);
            float inner = Mathf.Max(radius - thickness, radius * 0.05f);

            const float angStartDeg = 58f;                       // 右上
            float angEndDeg = angStartDeg - sweepDeg;            // 左下

            // ★ 五条环（原为三条）。浓淡剖面见下面的 kU / kA。
            const int Rings = 5;
            // 跨笔位置（0 = 内缘，1 = 外缘）与其浓淡。中间两档都给足，让浓墨有厚度。
            float[] kU = new float[] { 0f, 0.26f, 0.5f, 0.74f, 1f };
            float[] kA = new float[] { 0.10f, 0.82f, 1f, 0.82f, 0.10f };

            var verts = new Vector3[n * Rings];
            var colors = new Color[n * Rings];
            // 每条环带 1 个四边形 = 2 个三角形 = 6 个索引；五条环 ⇒ 4 条环带 ⇒ 每段 24 个索引
            const int TrisPerSeg = (Rings - 1) * 6;
            var tris = new int[(n - 1) * TrisPerSeg];

            float rMid0 = (radius + inner) * 0.5f;
            float half0 = (radius - inner) * 0.5f;

            for (int i = 0; i < n; i++)
            {
                float t = i / (float)(n - 1);
                float ang = Mathf.Lerp(angStartDeg, angEndDeg, t) * Mathf.Deg2Rad;

                // 两端收笔：弧的起止处变细、也变淡。
                // 下限从 0.22 收到 0.10 —— 毛笔的收笔是**尖**的，留 0.22 会留下一个小方头。
                float taper = Mathf.Sin(t * Mathf.PI);

                // ★ 粗细与浓淡**必须用两条曲线**（本轮改）：
                //   原来两者共用同一个 taper，于是整笔只有正中一小段是满浓度，其余都发灰 ——
                //   对比度拉伸后看得很清楚：形状对了，但读起来像一片淡影而不是一笔墨。
                //   真实的一笔是：**粗细**很快收成尖（sin 保持），但**浓度**在中段一直压得住，
                //   直到接近末端才抬笔。所以浓度用 sin^0.6 压平中段。
                float inkTaper = Mathf.Pow(taper, 0.6f);

                float half = half0 * (0.10f + 0.90f * taper);
                float rIn = rMid0 - half;
                float rOut = rMid0 + half;

                Vector3 dir = new Vector3(Mathf.Cos(ang), Mathf.Sin(ang), 0f);   // ← XY 平面
                for (int r = 0; r < Rings; r++)
                {
                    verts[i * Rings + r] = dir * Mathf.Lerp(rIn, rOut, kU[r]);
                    colors[i * Rings + r] = new Color(1f, 1f, 1f, kA[r] * inkTaper);
                }

                if (i < n - 1)
                {
                    int o = i * TrisPerSeg;
                    int a0 = i * Rings, a1 = (i + 1) * Rings;
                    for (int r = 0; r < Rings - 1; r++)
                    {
                        // 环带 r 的四边形 =（环 r 的第 i 点、环 r+1 的第 i 点、
                        //                   环 r+1 的第 i+1 点、环 r 的第 i+1 点）
                        int p0 = a0 + r, p1 = a0 + r + 1, q0 = a1 + r, q1 = a1 + r + 1;

                        // ★ 必须沿**真正的对角线 p0→q1** 剖分。
                        //   原实现拿 p0→q0（**同一环上相邻两点的连线**）当对角线 —— 那是环带的一条
                        //   **边界边**，两个三角形的第三个顶点都落在它的同一侧，于是它们是一对
                        //   **重叠的蝴蝶结**，四边形里留出一个**三角形空洞**。
                        //   屏幕上的表现是一把**等距梳子**：齿数 = 段数（28 段 → 27 齿，200 段 → 199 齿）。
                        //
                        //   这个 bug 从「两条环」时代就存在，加到三条环时被原样照抄。它不影响
                        //   「有没有刀光」，只把一笔墨变成锯齿，因此被先后误判成：shader 的 clip
                        //   阈值线、高频拉丝噪声、背面剔除（把绕序全部统一后锯齿不变，即可证伪）。
                        //   最终靠**导出运行时网格 + 离线按重心坐标光栅化**复现出同一把梳子才锁定。
                        //   现在四条环带共用同一条规则，不再有「内带外带各写一段」的分叉。
                        int b = o + r * 6;
                        tris[b + 0] = p0; tris[b + 1] = p1; tris[b + 2] = q1;
                        tris[b + 3] = p0; tris[b + 4] = q1; tris[b + 5] = q0;
                    }
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
                    // 直接从基准 alpha 算，不做逐帧累乘 —— 累乘会随帧率漂移。
                    //
                    // ★ 指数从 1.6 降到 1.0（线性），并把最后 20% 单独收干。
                    //   1.6 的曲线在寿命一半时就只剩 33% 浓度 —— 逐帧连拍里弧光大部分帧
                    //   淡到几乎看不见，读起来像"闪了一下就没了"而不是"一笔墨留在纸上"。
                    //   墨痕的正确节奏是：前半程维持浓度，最后快速收干。
                    float a = _baseAlpha * Mathf.Pow(1f - k, 1.0f);
                    if (k > 0.8f) a *= Mathf.Clamp01((1f - k) / 0.2f);
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
