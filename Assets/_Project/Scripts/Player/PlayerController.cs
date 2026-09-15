using System;
using UnityEngine;
using InkWash.CameraRig;
using InkWash.Roguelike;

namespace InkWash.Player
{
    /// <summary>玩家当前所处的行为阶段。三者互斥，同一时刻只有一个生效。</summary>
    public enum ActionPhase
    {
        /// <summary>常规移动（含待机、走、跑）。</summary>
        Locomotion,
        /// <summary>冲刺 / 闪避，含无敌帧。</summary>
        Dash,
        /// <summary>三段连击中的任意一段（含其后摇）。</summary>
        Attack,
    }

    /// <summary>
    /// 玩家角色控制器（Sprint 1 移动 + Sprint 2 战斗内核）。
    ///
    /// 设计要点：
    /// 1. **相机相对移动** —— 输入以相机朝向为基准解算世界方向，保证"按 W 就朝屏幕上方走"。
    /// 2. **加减速分离** —— 起步与刹车用不同速率，制造重量感。
    /// 3. **实际速度 / 期望速度分离** —— 前者由真实位移反推（含碰撞阻挡），后者是运动积分的产物。
    ///    动画与 HUD 用实际速度，否则角色顶着墙还会播跑步。
    /// 4. **步伐同步** —— 动画播放速度按「实际速度 / 参考速度」推算并写入 MotionSpeed，
    ///    使步频与位移匹配。固定播放速度必然滑步，这是走/跑动画的通用解法。
    /// 5. **后摇取消窗口** —— 攻击后摇不是"硬直等它播完"，而是从某个归一化时刻起开放取消：
    ///    可接下一段连击、可冲刺、可移动。
    /// 6. **打击位移而非滑行** —— 挥砍时前进的位移由代码按曲线施加，与挥砍时长对齐，
    ///    不再是"动画结束了人还在滑"。
    ///
    /// 状态机归属：**行为阶段由本脚本裁定，动画只负责表现**。脚本把阶段翻译成 Animator 参数，
    /// 并回读动画状态的归一化时间来判断取消窗口 —— 时间口径只有一份，不会两边写死而漂移。
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    [DisallowMultipleComponent]
    public class PlayerController : MonoBehaviour
    {
        [Header("移动")]
        [Tooltip("【慢走档】按住 Shift 时使用。对应动画 Walk（Feng 自带 Walk 的循环副本 Feng_Walk_Loop）。\n" +
                 "取 1.25 → 1.25/0.91 = 1.37× → 步频 2.06 步/秒（124 步/分，快走偏上但可控）。\n" +
                 "★ 曾经取 1.4（1.54×）→ 步频 2.31 步/秒 = 竞走级，观感是「小碎步快走」。\n" +
                 "★ 也试过换 KI Walk01_Forward（原生 1.80，取 1.4 只要 0.78×，步频更漂亮）——\n" +
                 "  但实测它的最低点跨度 0.327m（Feng 只有 0.166），侧视图里腿抬得像跨步/跑步，\n" +
                 "  不像走路，已回退。选片段不能只看步频，要看「脚位波动」。\n" +
                 "⚠ 本档是**修饰键档**（默认档是 runSpeed）。走档原生只有 0.91 m/s，\n" +
                 "  物理速度上限约 1.5 m/s，承担不了常用移动 —— 详见 ReadInput 里的说明。\n" +
                 "上限由验收的步频断言把关。")]
        public float walkSpeed = 1.25f;

        [Tooltip("【默认档】不按修饰键时的移动速度，也是按住 Shift 之外的常态速度。\n" +
                 "对应动画 Run（Kevin Iglesias Run01_Forward）。\n" +
                 "Run01_Forward 原生 4.12 m/s（脚踝法实测），取 4.0 → 0.97× → 步频 3.23 步/秒，落在真人跑步区间。\n" +
                 "★ 它才是默认档：走档（walkSpeed）原生只有 0.91 m/s，撑不起「平时移动」，\n" +
                 "  按 Shift 慢走、不按就跑 —— 见 ReadInput 里的完整理由。\n" +
                 "默认值必须与预制体上的序列化值一致（4.0）。曾经默认 5.6、预制体 4.2，\n" +
                 "两边不一致很容易让人误判\"实际生效的是哪个\"。")]
        public float runSpeed = 4.0f;

        [Tooltip("走路 -> 跑步 的切换阈值（按期望速度）。动画状态机用它切 Walk/Run 状态")]
        public float runAnimEnterSpeed = 3.0f;

        [Tooltip("跑步 -> 走路 的切换阈值。必须小于上面的进入阈值，形成迟滞，否则在阈值附近会来回抖")]
        public float runAnimExitSpeed = 2.5f;

        [Tooltip("起步加速度：越大越跟手")]
        public float acceleration = 26f;

        [Tooltip("刹车减速度：略大于加速度，停步更干脆")]
        public float deceleration = 34f;

        [Tooltip("转向角速度（度/秒）。攻击中会按 attackTurnScale 打折")]
        public float turnSpeedDeg = 900f;

        [Header("步伐同步（消除脚滑）")]
        // 参考速度是**片段自身的属性**，而且必须与**当前骨架**一起量 —— 换片段要重标，
        // 换骨架（腿长比例变了）同样要重标。本轮就栽在这：换成 Feng 骨架后，连 KayKit 的
        // Walking_A 都不是 0.69 了（腿变长 → 重定向把步伐一起放大），实测 1.32，
        // 旧值让播放倍率虚高到 1.45 → 腿倒腾得比真人快 50%。
        //
        // 「播放倍率落在 1.0~1.8 是健康的」这个旧结论是错的：倍率只说明片段被拉伸多少，
        // 真正的观感指标是**步频**（步/循环 ÷ 片段时长 × 倍率）。腿看起来自然与否看的是这个数。
        //
        // 标定用 Tools/cs/q_refspeed2.cs：逐脚取**脚踝骨骼**，只在「脚踝进入该脚最低点 +4cm」的
        // 着地帧量它的水平后移速度。**不要用网格最低点** —— 真人比例模型走路时脚掌会
        // 「脚跟→脚尖」滚动，最低点在着地相内部平移了约一个脚掌长（0.25m），速度会虚高一倍
        // （v1 用网格最低点把 Feng Walk 量成 2.25 m/s，实际 0.91）。
        [Tooltip("走路片段（Feng 自带 Walk）自身的地面速度参考值（米/秒）。\n" +
                 "Tools/cs/q_refspeed2.cs 脚踝法实测 0.91（步频 1.50、单步 0.61m）。")]
        public float walkRefSpeed = 0.91f;

        [Tooltip("跑步片段（Kevin Iglesias Run01_Forward）自身的地面速度参考值（米/秒）。\n" +
                 "脚踝法实测 4.12（步频 3.33、单步 1.24m）。取这个值 → 倍率 4.0/4.12 = 0.97×。")]
        public float runRefSpeed = 4.12f;

        [Tooltip("播放速度下限，防止慢走时腿部僵住")]
        public float motionSpeedMin = 0.6f;

        [Tooltip("播放速度上限，防止高速时腿部抽帧。\n" +
                 "取值必须 ≥ max(walkSpeed/walkRefSpeed, runSpeed/runRefSpeed)，否则会被截断而产生残余滑步。\n" +
                 "当前：走路 1.25/0.91 = 1.37，跑步 4.0/4.12 = 0.97，上限取 2.2 留足余量。")]
        public float motionSpeedMax = 2.2f;

        [Header("冲刺 / 闪避")]
        public float dashSpeed = 7.4f;

        [Tooltip("冲刺持续时间。必须与 Dash 动画（KayKit Dodge_Forward，0.40s）对齐：\n" +
                 "状态机里 Dash 状态的播放速度就是按 Dodge_Forward.length / 本值 算出来的，\n" +
                 "取 0.40 时倍率正好 1.0×（动作按原速播）。改这里要同步改 " +
                 "Tools/cs/s3_build_animator_kaykit.cs 的 DashDuration，验收会核对。\n" +
                 "位移 ≈ 7.4 × 0.40 × 0.85 ≈ 2.5m，仍满足「冲刺产生显著位移（> 2m）」的断言。")]
        public float dashDuration = 0.40f;

        public float dashCooldown = 0.55f;

        [Tooltip("冲刺起始的无敌窗口时长")]
        public float dashInvincibleWindow = 0.2f;

        [Header("三段连击")]
        [Tooltip("各段的前冲位移距离（米）—— 由代码施加，不是滑行")]
        public float[] comboLungeDistance = { 1.2f, 1.5f, 2.6f };

        [Tooltip("各段位移持续时长（秒）。随攻击提速同步按 1.25 缩：距离不变、加速度更干脆")]
        public float[] comboLungeDuration = { 0.176f, 0.192f, 0.336f };

        [Tooltip("各段挥砍动作总时长（秒），与 Player.controller 里的状态时长一致。\n" +
                 "Quaternius UAL2 的剑术片段是「斩击 + 收招」两段式，控制器里对应\n" +
                 "AtkN 与 AtkNRec 两个状态，所以这里的总时长 =\n" +
                 "主段 × exitTime / 主段speed + 收招段 × exitTime / 收招speed（speed 见 Player.controller）：\n" +
                 "A 0.433×0.95/1.25 + 0.967×0.85/1.70 = 0.813\n" +
                 "B 0.533×0.95/1.25 + 1.033×0.85/1.70 = 0.922\n" +
                 "C 2.000×0.72/1.25 = 1.152\n" +
                 "（收招段 speed 由 1.40 提到 1.70 —— 收招占一轮连击 60%+，是「廉价感」的直接来源）")]
        public float[] comboSwingDuration = { 0.813f, 0.922f, 1.152f };

        [Tooltip("各段的命中时刻（秒）—— 用于触发刀光与震屏。取值 = 剑尖世界速度峰值附近、略提前：\n" +
                 "A 0.279s / B 0.258s / C 0.656s（Tools/cs/q_atktiming.cs 标定）\n" +
                 "攻击提速后按「峰值时刻 ÷ 主段 speed」重算、沿用原有的绝对提前量 → 0.19 / 0.19 / 0.48")]
        public float[] comboHitTime = { 0.19f, 0.19f, 0.48f };

        [Tooltip("各段后摇的取消窗口起点（归一化时间），与动画状态机的 exitTime 对应")]
        public float[] comboRecCancelStart = { 0.3f, 0.3f, 0.45f };

        [Tooltip("连击输入的缓冲时间（秒）—— 提前按下也算数")]
        public float attackInputBuffer = 0.25f;

        [Tooltip("攻击中的转向速率折扣")]
        [Range(0f, 1f)] public float attackTurnScale = 0.25f;

        [Header("重力")]
        public float gravity = -24f;
        public float groundedStick = -3f;

        [Header("引用")]
        public Transform cameraTransform;
        public Animator animator;
        public ThirdPersonCamera cameraRig;

        [Header("属性加成（Roguelike，留空自动取同物体）")]
        [Tooltip("移速 / 攻速 / 冲刺冷却从这里读。为 null 时全部倍率 = 1，行为与 Sprint 4 逐位一致")]
        public PlayerStats stats;

        // ---------------- 对外只读状态（战斗 / AI / HUD 读取） ----------------

        /// <summary>水平面**实际**速度（y 恒 0）。</summary>
        public Vector3 PlanarVelocity => new Vector3(_actualVelocity.x, 0f, _actualVelocity.z);

        /// <summary>当前水平**实际**速率（m/s）。</summary>
        public float CurrentSpeed => PlanarVelocity.magnitude;

        /// <summary>当前水平**期望**速率（m/s）—— 运动积分的结果，未考虑碰撞阻挡。</summary>
        public float DesiredSpeed => new Vector2(_velocity.x, _velocity.z).magnitude;

        public bool IsGrounded => _cc != null && _cc.isGrounded;
        public bool IsDashing => Phase == ActionPhase.Dash;
        public bool IsInvincible => Phase == ActionPhase.Dash && _phaseTimer <= dashInvincibleWindow;
        public bool CanDash => Phase != ActionPhase.Dash && _dashCooldownTimer <= 0f;

        /// <summary>当前行为阶段。</summary>
        public ActionPhase Phase { get; private set; } = ActionPhase.Locomotion;

        /// <summary>当前连击段（1/2/3）。未在攻击时为 0。</summary>
        public int ComboStep => _comboStep;

        /// <summary>后摇取消窗口是否已打开（打开后可接下一段连击 / 冲刺 / 移动）。</summary>
        public bool IsCancelWindowOpen { get; private set; }

        /// <summary>本帧的世界空间移动方向。</summary>
        public Vector3 MoveDirection { get; private set; }

        /// <summary>
        /// 当前阶段的已持续时间（秒）。阶段切换时归零。
        /// 用于自动化验收与调试 HUD —— 攻击前冲曲线、取消窗口都挂在这个时间轴上。
        /// </summary>
        public float PhaseTime => _phaseTimer;

        /// <summary>当前世界空间「期望」速度（含 y）。攻击前冲的积分量就在这里，验收用它判断前冲是否真的施加了。</summary>
        public Vector3 DesiredVelocity => _velocity;

        /// <summary>本段攻击是否已确认进入过攻击动画状态（诊断用）。</summary>
        public bool AttackStateSeen => _attackStateSeen;

        /// <summary>
        /// 本帧步伐同步所用的片段参考速度（走路用的 walkRefSpeed 还是跑步用的 runRefSpeed）。
        /// 验收靠它把「实际速度 / MotionSpeed」反推回片段速度，核对是否等于所选的参考值。
        /// </summary>
        public float CurrentRefSpeed { get; private set; }

        /// <summary>当前是否应该跑（用于核对状态机的 Walk/Run 选择与代码判据一致）。</summary>
        public bool WantsRunAnimation => CurrentSpeed >= runAnimExitSpeed;

        // ---------------- 事件（供 VFX / 音频 / 震屏订阅） ----------------

        /// <summary>某一段挥砍开始。参数为连击段序号 1..3。</summary>
        public event Action<int> SwingStarted;

        /// <summary>某一段的命中时刻。参数为连击段序号 1..3。</summary>
        public event Action<int> HitMoment;

        /// <summary>冲刺 / 闪避开始。供音效订阅。</summary>
        public event Action DashStarted;

        // ---------------- 内部状态 ----------------

        private CharacterController _cc;
        private Vector3 _velocity;          // 世界空间"期望"速度（运动积分产物）
        private Vector3 _actualVelocity;    // 世界空间"实际"速度（由位移反推）
        private Vector2 _input;
        private bool _runHeld;

        private ActionPhase _phase = ActionPhase.Locomotion;
        private float _phaseTimer;
        private float _dashCooldownTimer;
        private Vector3 _actionDir;         // 冲刺 / 突击方向

        /// <summary>
        /// 本次攻击的攻速倍率（进入攻击阶段时从 <see cref="stats"/> **快照一次**）。
        ///
        /// 为什么快照而不是每次现读 stats：攻速同时影响三处 —— Animator 播放速率、
        /// 代码侧命中时刻 <c>comboHitTime</c>、前冲时长 <c>comboLungeDuration</c>。
        /// 若三处各自读一次 stats，而玩家在挥砍中途升级，就会出现"动画已提速、
        /// 命中时刻却还按旧速度算"的错位 —— 表现是那一刀明明挥出去却打空。
        /// 一次挥砍内锁死同一个倍率，三处保证一致。
        /// </summary>
        private float _atkSpeed = 1f;

        /// <summary>只读诊断：本次攻击的攻速倍率。</summary>
        public float CurrentAttackSpeed => _atkSpeed;
        /// <summary>只读诊断：当前移速倍率。</summary>
        public float CurrentMoveSpeedScale => stats != null ? stats.MoveSpeedMultiplier : 1f;

        private int _comboStep;
        private bool _attackQueued;
        private float _attackQueuedAt = -99f;
        private bool _hitFiredThisStep;
        /// <summary>本段攻击是否**确实**见到过攻击动画状态。见 TickAttack 末尾的说明。</summary>
        private bool _attackStateSeen;

        /// <summary>
        /// 「这一招收完就**重新起手**」的意图。与 <see cref="_attackQueued"/> 是两件事，
        /// 这是本轮（攻击收招期间再按攻击会失效）踩出来的区分：
        ///   · <see cref="_attackQueued"/> = 「我还想接**下一段**连击」，只在取消窗口
        ///     打开之前有意义，窗口一开就消费；有效期只有 <see cref="attackInputBuffer"/>(0.25s)。
        ///   · 本字段 = 「这一段已经接不上下一段了（后摇已过 / 第 3 段按设计不可取消），
        ///     我只想**再打一遍**」，从按下一直保留到阶段回到移动态再消费。
        /// 所以**不能**复用 _attackQueued：它的 0.25s 有效期撑不过
        /// 「第 3 段收招 + 后摇混合」（实测 0.5s 以上），复用会让玩家这一次按键静默消失。
        /// </summary>
        private bool _restartQueued;

        /// <summary>
        /// 交给动画混合树的「移动意图速度」（m/s）。**不等于** <see cref="DesiredSpeed"/>。
        ///
        /// 为什么要单独分出一个量（真实踩过的坑）：
        ///   状态机的 Idle→Walk(Speed>0.15) / Walk→Run(Speed>3.0) 这些转移是**按列表顺序**
        ///   求值的，而攻击的起手转移（Attack1 触发器）排在它们后面。原先直接把
        ///   `DesiredSpeed`（= `_velocity.magnitude`）灌进 `Speed` 参数，可攻击的**前冲**
        ///   本身就是往 `_velocity` 里赋值（峰值 ~11 m/s），于是：
        ///     发起攻击的那一帧 → 速度参数瞬间冲过 3.0 → 排在前面、条件成立的
        ///     Idle→Walk→Run 抢先成立 → 动画被拽去走路/跑步，攻击触发器一直没能落地；
        ///     再过 `kAttackEntryTimeout`(0.35s) → 逻辑判定"没进攻击态"直接退回移动 →
        ///     取消窗口永远打不开、连击推不到第 3 段。
        ///   所以这里按「意图」而不是「速度积分结果」来驱动混合树：
        ///     移动阶段 = 输入决定的目标速度；攻击 / 冲刺阶段 = 0（位移是动作自带的，不算移动）；
        ///     只有后摇取消窗口里玩家**真的**按了方向才把意图速度交回去（保留"后摇可移动取消"）。
        /// </summary>
        private float _blendSpeed;

        // 输入注入（自动化验收 / 演示录屏）
        private bool _overrideActive;
        private Vector2 _overrideMove;
        private bool _overrideRun;
        private bool _overrideDash;
        private bool _overrideAttack;

        private static readonly int HashSpeed = Animator.StringToHash("Speed");
        private static readonly int HashMotionSpeed = Animator.StringToHash("MotionSpeed");
        private static readonly int HashGrounded = Animator.StringToHash("Grounded");
        private static readonly int HashDash = Animator.StringToHash("Dash");
        private static readonly int HashAttack1 = Animator.StringToHash("Attack1");
        private static readonly int HashAttack2 = Animator.StringToHash("Attack2");
        private static readonly int HashAttack3 = Animator.StringToHash("Attack3");
        private static readonly int HashIdle = Animator.StringToHash("Idle");

        /// <summary>
        /// 发起攻击后，等待 Animator 真正切进攻击状态的兜底时限（秒）。
        /// 超时仍未进入就强制退回移动阶段，避免攻击触发器意外丢失时卡死在 Attack。
        /// </summary>
        private const float kAttackEntryTimeout = 0.35f;

        private void Awake()
        {
            _cc = GetComponent<CharacterController>();
            if (animator == null) animator = GetComponentInChildren<Animator>();
            if (cameraRig == null) cameraRig = FindObjectOfType<ThirdPersonCamera>();
            if (cameraTransform == null && cameraRig != null) cameraTransform = cameraRig.transform;
            if (cameraTransform == null && Camera.main != null) cameraTransform = Camera.main.transform;
            if (stats == null) stats = GetComponent<PlayerStats>();
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            Vector3 posBefore = transform.position;

            ReadInput();
            _phaseTimer += dt;
            if (_dashCooldownTimer > 0f) _dashCooldownTimer -= dt;

            switch (_phase)
            {
                case ActionPhase.Locomotion: TickLocomotion(dt); break;
                case ActionPhase.Dash: TickDash(dt); break;
                case ActionPhase.Attack: TickAttack(dt); break;
            }

            ApplyGravity(dt);
            ApplyRotation(dt);

            // 真实位移反推实际速度 —— CharacterController.Move 被挡住时不会回写 _velocity
            _actualVelocity = (transform.position - posBefore) / Mathf.Max(dt, 1e-5f);

            PushAnimatorState();
        }

        // ==================================================================
        // 输入
        // ==================================================================

        private void ReadInput()
        {
            if (_overrideActive)
            {
                _input = _overrideMove;
                if (_input.sqrMagnitude > 1f) _input.Normalize();
                // 注入路径**不做 Shift 反转**：runHeld 直接就是"用跑档 / 用走档"，
                // 与 SetInjectedMove 的参数一一对应（验收脚本按字面理解即可）。
                _runHeld = _overrideRun;
                if (_overrideDash) { _overrideDash = false; TryStartDash(); }
                if (_overrideAttack) { _overrideAttack = false; TryAttack(); }
                return;
            }

            float x = 0f, y = 0f;
            if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) x -= 1f;
            if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) x += 1f;
            if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) y -= 1f;
            if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) y += 1f;

            _input = new Vector2(x, y);
            if (_input.sqrMagnitude > 1f) _input.Normalize();

            // ★ Shift 的语义：**按住 = 慢走，不按 = 跑**。
            //
            // 为什么要把默认档从"走"换成"跑"（原先是不按 Shift 走、按 Shift 跑）：
            //   · Feng 的 Walk 片段原生只有 **0.91 m/s**（脚踝法实测）。要让它看着自然，
            //     播放倍率必须留在 1.0~1.6 ⇒ 物理速度上限约 1.5 m/s。
            //     在 44 m 见方的场地里，从中心走到外墙要 **15 秒** —— 用户实测反馈
            //     「平时走路有点太慢了」，指的就是这个。
            //   · 而 Run 片段原生 4.12 m/s，取 4.0 的播放倍率是 **0.97** —— 它本来就该是默认档。
            //   · 曾试着直接把 walkSpeed 提到 1.4（倍率 1.54），得到的是**竞走级的小碎步**
            //     （步频 2.31 步/秒），只好回退。**问题不在速度数值，在于走档压根撑不起常用移动。**
            //   · 现在两个档位都落在各自片段的舒适区：默认 4.0（倍率 0.97，步频 3.23）、
            //     按住 Shift 1.25（倍率 1.37，步频 2.06）。没有任何滑步或碎步。
            //
            // Shift = 慢走也符合多数玩家的肌肉记忆（潜行 / 精细走位）。
            bool slowWalkHeld = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            _runHeld = !slowWalkHeld;

            if (Input.GetKeyDown(KeyCode.Space) || Input.GetMouseButtonDown(1)) TryStartDash();
            if (Input.GetMouseButtonDown(0) || Input.GetKeyDown(KeyCode.J)) TryAttack();
        }

        // ==================================================================
        // 行为阶段
        // ==================================================================

        private void TickLocomotion(float dt)
        {
            Vector3 dir = ResolveMoveDirection();
            MoveDirection = dir;

            // 移速加成在这里消费 —— 乘在**目标速度**上，而不是乘在最终位移上。
            // 乘在位移上会把加速阶段一起放大（表现成"起步瞬间到顶"），
            // 也会让 Walk→Run 的切换点漂移（那是按速度绝对值判的）。
            float speedScale = stats != null ? stats.MoveSpeedMultiplier : 1f;
            float targetSpeed = dir.sqrMagnitude > 1e-4f
                ? (_runHeld ? runSpeed : walkSpeed) * speedScale
                : 0f;
            Vector3 targetVel = dir * targetSpeed;
            Vector3 planar = new Vector3(_velocity.x, 0f, _velocity.z);

            float rate = targetSpeed > planar.magnitude ? acceleration : deceleration;
            planar = Vector3.MoveTowards(planar, targetVel, rate * dt);

            _velocity.x = planar.x;
            _velocity.z = planar.z;

            // 移动阶段：混合树按"输入意图"跑 Idle/Walk/Run（见 _blendSpeed 的说明）
            _blendSpeed = targetSpeed;
        }

        private void TickDash(float dt)
        {
            MoveDirection = _actionDir;
            Vector3 planar = new Vector3(_velocity.x, 0f, _velocity.z);
            // 速度曲线：起步快速拉起、末段略收，避免"到点硬停"
            float nd = Mathf.Clamp01(_phaseTimer / Mathf.Max(dashDuration, 1e-4f));
            float speed = dashSpeed * Mathf.Lerp(1f, 0.55f, nd * nd);
            planar = Vector3.MoveTowards(planar, _actionDir * speed, 200f * dt);
            _velocity.x = planar.x;
            _velocity.z = planar.z;

            // 冲刺位移同样不是"移动意图"：不给它，否则冲刺前段会被混合树拽去跑步
            _blendSpeed = 0f;

            if (_phaseTimer >= dashDuration) EnterLocomotion(1.2f);
        }

        /// <summary>
        /// 攻击阶段：只负责「打击位移」与「取消窗口判定」。
        /// 动画推进与切换交给状态机（exitTime 即窗口），这里回读它的归一化时间做同步，
        /// 保证时间口径只有一份。
        /// </summary>
        private void TickAttack(float dt)
        {
            int idx = Mathf.Clamp(_comboStep - 1, 0, comboLungeDistance.Length - 1);

            // 攻击动画按攻速加速；但**取消窗口一开就恢复正常速度** —— 窗口内允许移动取消，
            // 那时若还保持加速，走路动画会被一起加速（脚步与位移脱节，看起来像滑步）。
            if (animator != null)
            {
                float want = (!IsCancelWindowOpen && _atkSpeed != 1f) ? _atkSpeed : 1f;
                if (!Mathf.Approximately(animator.speed, want)) animator.speed = want;
            }

            // ---- 前冲位移：按速度曲线施加 ----
            // 曲线 v(t) = 2·D·(1 - t/T) / T 对 t 的积分恰好等于设定距离 D
            // （∫₀ᵀ v dt = D），所以这里必须**赋值**给 _velocity，而不是把 v·dt 累加进去。
            // 曾经写成 `_velocity += dir * (v * dt)`：那样 _velocity 只是慢慢涨到 ≈ D，
            // 真实位移变成 ∫_velocity dt（第二次积分），比设定值小一个量级 ——
            // 表现是"挥砍几乎不前进"。三段连击的位移学判定就是被这个吃掉的。
            // 攻速缩短前冲时长。不除的话"动画加速了、滑行却没跟上"，看起来像脚下打滑。
            float lungeDur = Mathf.Max(comboLungeDuration[idx] / _atkSpeed, 1e-3f);
            if (_phaseTimer < lungeDur)
            {
                float nd = _phaseTimer / lungeDur;
                float v = 2f * comboLungeDistance[idx] * (1f - nd) / lungeDur;
                Vector3 dir = new Vector3(_actionDir.x, 0f, _actionDir.z);
                if (dir.sqrMagnitude > 1e-6f) dir.Normalize();
                _velocity.x = dir.x * v;
                _velocity.z = dir.z * v;
            }
            else BrakePlanar(dt, deceleration * 1.6f);

            // ---- 命中时刻：抛事件（刀光 / 震屏 / 顿帧） ----
            // ★ 必须除以攻速：comboHitTime 是**真实秒**，而动画已被 _atkSpeed 加速。
            //   不除的话，加速 1.5 倍时视觉上剑早已扫过目标、判定却还在等 ——
            //   表现是"刀挥过去了才掉血"，而且只在装了攻速技能后复现，很难查。
            if (!_hitFiredThisStep && _phaseTimer >= comboHitTime[idx] / _atkSpeed)
            {
                _hitFiredThisStep = true;
                if (HitMoment != null) HitMoment(_comboStep);
            }

            // ---- 取消窗口：读动画状态，而不是自己另算一套时间 ----
            IsCancelWindowOpen = EvaluateCancelWindow();

            // ---- 窗口内允许移动取消 ----
            // 意图速度默认给 0：攻击的位移是动作自带的，不该被混合树当成"我在走路"。
            // 只有在窗口内**真的按了方向**时才交回意图速度 —— 否则后摇里接移动会先慢半拍。
            float intent = 0f;
            if (IsCancelWindowOpen)
            {
                Vector3 dir = ResolveMoveDirection();
                if (dir.sqrMagnitude > 1e-4f)
                {
                    MoveDirection = dir;
                    float target = (_runHeld ? runSpeed : walkSpeed);
                    _velocity.x = Mathf.Lerp(_velocity.x, dir.x * target, 8f * dt);
                    _velocity.z = Mathf.Lerp(_velocity.z, dir.z * target, 8f * dt);
                    intent = target;
                }
            }
            _blendSpeed = intent;

            // ---- 攻击输入缓冲：过期作废 ----
            if (_attackQueued && Time.time - _attackQueuedAt > attackInputBuffer)
                _attackQueued = false;

            if (_attackQueued && IsCancelWindowOpen && _comboStep < 3)
            {
                _attackQueued = false;
                AdvanceCombo();
                return;
            }

            // 缓冲没能被消费、而状态机已经开始离开这一段 → 转成「重新起手」。
            // 顺序很讲究：必须排在上面那次 AdvanceCombo 之后 —— 反过来的话，
            // 后摇退出期间按钮会去"重新起手"而不是接第二段，连击会被砍掉一段。
            if (_attackQueued && IsLeavingAttack())
            {
                _attackQueued = false;
                _restartQueued = true;
            }

            // 动画若已自行回到待机/移动，说明这一段（含后摇）走完了。
            //
            // 关键：必须「**先见过**攻击状态」才允许退出。
            // 发起攻击的那一帧 Animator 还没完成过渡，GetCurrentAnimatorStateInfo 读到的
            // 仍是 Idle/Move；若直接判定"不在攻击态"，单击攻击会在发招的**同一帧**掉出
            // Attack —— 表现是挥砍不出前冲位移、命中事件不触发（没有刀光震屏）、
            // 取消窗口永远打不开。连打之所以看起来正常，只是后续按键时动画已经切过去了。
            if (IsInAttackState()) _attackStateSeen = true;
            else if (_attackStateSeen || _phaseTimer > kAttackEntryTimeout) EnterLocomotion(1.6f);
        }

        // ==================================================================
        // 动作发起
        // ==================================================================

        private bool TryStartDash()
        {
            if (!CanDash) return false;
            _phase = ActionPhase.Dash;
            Phase = ActionPhase.Dash;
            _phaseTimer = 0f;
            _comboStep = 0;
            _attackQueued = false;
            _attackStateSeen = false;
            IsCancelWindowOpen = false;
            // 冲刺冷却缩放在这里消费（只缩冷却，不缩冲刺本身的时长 ——
            // 缩短冲刺时长会连带缩短无敌帧，那是"变强"而不是"更灵活"，与技能描述不符）
            _dashCooldownTimer = dashCooldown * (stats != null ? stats.DashCooldownScale : 1f) + dashDuration;

            Vector3 dir = ResolveMoveDirection();
            _actionDir = dir.sqrMagnitude > 1e-4f ? dir : transform.forward;
            _actionDir.y = 0f;
            _actionDir.Normalize();

            if (animator != null) animator.SetTrigger(HashDash);
            if (DashStarted != null) DashStarted();
            return true;
        }

        /// <summary>
        /// 发起攻击：
        ///  - 移动/待机中 → 起手第 1 段
        ///  - 冲刺后段 → 也能接（冲刺取消）
        ///  - 攻击后摇且窗口已开 → 立刻接下一段
        ///  - 窗口未开 → 记入输入缓冲，窗口一开自动接
        /// </summary>
        private bool TryAttack()
        {
            if (_phase == ActionPhase.Attack)
            {
                // 1) 取消窗口开着且还有下一段 → 立刻接（连击的正常路径）
                if (_comboStep < 3 && IsCancelWindowOpen) { AdvanceCombo(); return true; }

                // 2) 还有下一段，只是窗口没开（挥砍前段按早了）→ 进输入缓冲，
                //    窗口一开由 TickAttack 自动接上。这是连打能推满三段的基础。
                if (_comboStep < 3 && !IsLeavingAttack())
                {
                    _attackQueued = true;
                    _attackQueuedAt = Time.time;
                    return true;
                }

                // 3) 接不了下一段了：第 3 段正收招（按设计不可取消），或本段后摇的状态机
                //    已经开始退出。**这时绝不能 return false 把玩家的输入丢掉** ——
                //    用户报的「攻击结束后立刻再攻击，招式不出来」就是这里。
                //    记成「收完重新起手」，EnterLocomotion 会立刻兑现。
                _restartQueued = true;
                return true;
            }

            if (_phase == ActionPhase.Dash && _phaseTimer < dashDuration * 0.55f) return false;
            if (animator == null) return false;

            _comboStep = 1;
            _phase = ActionPhase.Attack;
            Phase = ActionPhase.Attack;
            _phaseTimer = 0f;
            _hitFiredThisStep = false;
            _attackStateSeen = false;
            IsCancelWindowOpen = false;
            _actionDir = ResolveAttackDir();

            // 攻速快照（一次挥砍内锁死，见 _atkSpeed 的说明）
            _atkSpeed = stats != null ? Mathf.Max(0.2f, stats.AttackSpeedMultiplier) : 1f;

            animator.SetTrigger(HashAttack1);
            if (SwingStarted != null) SwingStarted(1);
            return true;
        }

        private void AdvanceCombo()
        {
            _comboStep++;
            _phaseTimer = 0f;
            _hitFiredThisStep = false;
            _attackStateSeen = false;
            IsCancelWindowOpen = false;
            _actionDir = ResolveAttackDir();

            if (animator != null) animator.SetTrigger(_comboStep == 2 ? HashAttack2 : HashAttack3);
            if (SwingStarted != null) SwingStarted(_comboStep);
        }

        private void EnterLocomotion(float brakeRate)
        {
            if (_phase == ActionPhase.Locomotion) return;
            bool restartWanted = _restartQueued;
            _phase = ActionPhase.Locomotion;
            Phase = ActionPhase.Locomotion;
            _phaseTimer = 0f;
            _comboStep = 0;
            _attackQueued = false;
            _attackStateSeen = false;
            _restartQueued = false;
            IsCancelWindowOpen = false;
            _blendSpeed = 0f;   // 交给下一帧的 TickLocomotion 按输入重新决定
            _atkSpeed = 1f;
            // 动画速率必须恢复 —— 它是**全局**的，忘了恢复会让之后的走路/待机一直加速
            if (animator != null)
            {
                animator.speed = 1f;
                // ★ 退出攻击时必须把三个攻击触发器一起清掉。
                //   理由（本轮实测到的**真实故障**）：Attk1Rec 往 Idle 混合的那条出边
                //   原本 interruptionSource = None，已经开始就无法被 Attk1Rec→Attk2 打断。
                //   逻辑那边判「取消窗口还开着」于是 AdvanceCombo → SetTrigger(Attack2)，
                //   而状态机压根消费不到这个触发器 —— 它就一直**挂着**。
                //   危害不在当下，而在**下一次**攻击：那一刀播到 Atk1Rec 的瞬间，
                //   残留的 Attack2 触发器突然生效，动画从半途直接跳进第二段。
                //   用户看到的就是「卡在中间一个奇怪的动作」。
                //   现在出边已改为可打断（见 a_fix_fsm.cs），这里是第二道保险：
                //   只要回到移动态，连击相关的触发器一律清零，绝不把状态带出去。
                animator.ResetTrigger(HashAttack1);
                animator.ResetTrigger(HashAttack2);
                animator.ResetTrigger(HashAttack3);
            }
            BrakePlanar(Time.deltaTime, brakeRate * 25f);

            // 收招期间按下的攻击在这里兑现：直接重新起手第 1 段。
            // 放在最后是因为它会再次把阶段切成 Attack，前面几步的复位必须先做完。
            if (restartWanted && animator != null) TryAttack();
        }

        private void BrakePlanar(float dt, float rate)
        {
            Vector3 planar = new Vector3(_velocity.x, 0f, _velocity.z);
            planar = Vector3.MoveTowards(planar, Vector3.zero, rate * dt);
            _velocity.x = planar.x;
            _velocity.z = planar.z;
        }

        // ==================================================================
        // 取消窗口：直接读动画状态，避免两套时间口径漂移
        // ==================================================================

        /// <summary>
        /// 判据必须是「**当前这一段自己的**后摇状态」，不能只看"是不是某个后摇"。
        /// 否则升段（AdvanceCombo）之后动画还没切过去，读到的仍是上一段的 Atk1Rec，
        /// 窗口就一直开着 —— 连打会让逻辑段位一路跑到动画前面（第 2/3 段的前冲在半途被截断），
        /// 而动画还停在上一段。这就是"两套时间口径漂移"。
        /// </summary>
        private bool EvaluateCancelWindow()
        {
            if (animator == null) return true;
            if (!_attackStateSeen) return false;   // 还没真正进入攻击动画，窗口无从谈起

            var st = animator.GetCurrentAnimatorStateInfo(0);
            float nt = st.normalizedTime % 1f;

            switch (_comboStep)
            {
                case 1: return st.IsName("Atk1Rec") && nt >= comboRecCancelStart[0];
                case 2: return st.IsName("Atk2Rec") && nt >= comboRecCancelStart[1];
                case 3: return st.IsName("Atk3") && nt >= comboRecCancelStart[2];
            }
            return false;
        }

        private bool IsInAttackState()
        {
            if (animator == null) return false;
            return IsAttackStateName(animator.GetCurrentAnimatorStateInfo(0));
        }

        /// <summary>
        /// 状态机**是否已经在离开这一段连击** —— 即当前处于一次过渡中，且过渡目标
        /// 不再是攻击状态（典型就是 Atk1Rec→Idle 的后摇退出混合）。
        ///
        /// 为什么必须单独判这个（本轮实测故障）：
        ///   `GetCurrentAnimatorStateInfo` 在一次过渡**完成之前**一直返回**源状态**，
        ///   所以「当前是 Atk1Rec」这句话在整个退出混合期间（0.16 归一化 ≈ 0.2s）都成立。
        ///   而取消窗口判据是 `Atk1Rec && normalizedTime >= 0.3`，于是窗口在退出混合期间
        ///   仍报"开"—— 逻辑以为能接第二段，实际上状态机已经在往外走了。
        ///   对照实验（Tools/reports/a_retrigger2.txt 变体C）：
        ///     第 103 帧按下 → 段位涨到 2，但动画 18 帧内一直是 Atk1Rec，最终掉回 Idle，
        ///     整段第二刀**完全没有出现**。用户原话：「没有立刻继续执行攻击动作」。
        /// </summary>
        private bool IsLeavingAttack()
        {
            if (animator == null || !animator.IsInTransition(0)) return false;
            return !IsAttackStateName(animator.GetNextAnimatorStateInfo(0));
        }

        private static bool IsAttackStateName(AnimatorStateInfo st)
        {
            return st.IsName("Atk1") || st.IsName("Atk1Rec")
                || st.IsName("Atk2") || st.IsName("Atk2Rec")
                || st.IsName("Atk3");
        }

        // ==================================================================
        // 方向解算
        // ==================================================================

        private Vector3 ResolveMoveDirection()
        {
            if (_input.sqrMagnitude < 1e-4f) return Vector3.zero;

            Vector3 fwd, right;
            if (cameraTransform != null)
            {
                fwd = cameraTransform.forward;
                right = cameraTransform.right;
                fwd.y = 0f; right.y = 0f;
                if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
                fwd.Normalize();
                right.Normalize();
            }
            else { fwd = Vector3.forward; right = Vector3.right; }

            return (fwd * _input.y + right * _input.x).normalized;
        }

        /// <summary>攻击朝向：优先朝输入方向，没有输入就朝角色正面。</summary>
        private Vector3 ResolveAttackDir()
        {
            Vector3 dir = ResolveMoveDirection();
            if (dir.sqrMagnitude < 1e-4f)
            {
                dir = transform.forward;
                dir.y = 0f;
            }
            return dir.normalized;
        }

        // ==================================================================
        // 物理与朝向
        // ==================================================================

        private void ApplyGravity(float dt)
        {
            if (_cc.isGrounded && _velocity.y < 0f) _velocity.y = groundedStick;
            else _velocity.y += gravity * dt;

            _cc.Move(_velocity * dt);
        }

        private void ApplyRotation(float dt)
        {
            Vector3 dir = MoveDirection;
            dir.y = 0f;
            if (dir.sqrMagnitude < 1e-4f) return;

            float speed = turnSpeedDeg;
            if (_phase == ActionPhase.Attack) speed *= attackTurnScale;

            Quaternion target = Quaternion.LookRotation(dir, Vector3.up);
            transform.rotation = speed <= 0f
                ? target
                : Quaternion.RotateTowards(transform.rotation, target, speed * dt);
        }

        // ==================================================================
        // 动画
        // ==================================================================

        private void PushAnimatorState()
        {
            if (animator == null) return;

            // Speed 用「移动意图」而不是实际速度：攻击/冲刺的位移是动作自带的，
            // 若把它当成移动速度，会瞬间冲过 Run 的门槛，把还没落地的攻击起手转移挤掉
            // （Idle→Walk→Run 在状态机列表里排在攻击转移前面）。详见 _blendSpeed 的说明。
            animator.SetFloat(HashSpeed, _blendSpeed, 0.10f, Time.deltaTime);

            // 步伐同步：播放速度 = 实际速度 / 本状态的片段参考速度。
            // 走路与跑步是两段不同的动画、各自的地面速度也不同，所以参考值要按当前状态取。
            // 迟滞下沿（runAnimExitSpeed）作为判据，与状态机的 Run->Walk 门槛一致，
            // 避免"参考速度用跑步的、状态却是走路"这种错配。
            bool runState = CurrentSpeed >= runAnimExitSpeed;
            float refSpeed = runState ? runRefSpeed : walkRefSpeed;
            CurrentRefSpeed = refSpeed;
            float motion = Mathf.Clamp(CurrentSpeed / Mathf.Max(refSpeed, 0.01f),
                motionSpeedMin, motionSpeedMax);
            animator.SetFloat(HashMotionSpeed, motion, 0.10f, Time.deltaTime);

            animator.SetBool(HashGrounded, _cc.isGrounded);

            // 不再有上半身遮罩层：走路换成正经走路动画后，遮罩层既没必要，
            // 又会把攻击动作的上半身锁死在待机姿势（问题 2「攻击第一下很怪」的根源）。
        }

        // ==================================================================
        // 输入注入（自动化验收 / 演示录屏 / 过场动画）
        // ==================================================================

        public void BeginInputOverride()
        {
            _overrideActive = true;
            _overrideMove = Vector2.zero;
            _overrideRun = false;
            _overrideDash = false;
            _overrideAttack = false;
        }

        public void EndInputOverride()
        {
            _overrideActive = false;
            _overrideMove = Vector2.zero;
            _overrideRun = false;
            _overrideDash = false;
            _overrideAttack = false;
        }

        public void SetInjectedMove(Vector2 move, bool runHeld) { _overrideMove = move; _overrideRun = runHeld; }
        public void RequestInjectedDash() { _overrideDash = true; }
        public void RequestInjectedAttack() { _overrideAttack = true; }
        public bool IsInputOverridden => _overrideActive;

        /// <summary>
        /// 把角色状态机强制复位到移动阶段并清空速度。
        /// 用于关卡重置 / 检查点复活 / 自动化验收（每个用例要从同一个干净状态起步，
        /// 否则上一段的攻击后摇会带进下一段，采样起点就不一致了）。
        /// **不重置位置** —— 位置由调用方负责。
        /// </summary>
        public void ResetToLocomotion()
        {
            EnterLocomotion(1.6f);
            _velocity = Vector3.zero;
            _actualVelocity = Vector3.zero;
            _attackQueued = false;
            _attackStateSeen = false;
            _hitFiredThisStep = false;
            _dashCooldownTimer = 0f;

            // 光复位脚本的阶段是不够的：Animator 自己的状态机（触发器 + 状态）还在演上一段攻击，
            // 会继续吐 Atk2Rec 之类的状态出来，让取消窗口判定读到"上一个动作的残留"。
            // 所以这里连动画状态一起复位。
            if (animator != null)
            {
                animator.ResetTrigger(HashDash);
                animator.ResetTrigger(HashAttack1);
                animator.ResetTrigger(HashAttack2);
                animator.ResetTrigger(HashAttack3);
                if (animator.HasState(0, HashIdle))
                {
                    animator.Play(HashIdle, 0, 0f);
                    animator.Update(0f);
                }
                // 注：S2.1 起 Animator 只有 layer 0（上半身遮罩层已删除，它会让攻击锁在抱臂待机），
                // 所以这里不再有 SetLayerWeight(1, 1f) —— 那会刷 "Invalid Layer Index '1'" 警告。
            }
        }

        // ==================================================================
        // 编辑器可视化
        // ==================================================================

        private void OnDrawGizmosSelected()
        {
            if (!Application.isPlaying) return;

            Gizmos.color = IsInvincible ? new Color(0.2f, 0.9f, 1f, 0.55f)
                        : _phase == ActionPhase.Dash ? new Color(1f, 0.85f, 0.2f, 0.35f)
                        : _phase == ActionPhase.Attack ? new Color(1f, 0.3f, 0.3f, 0.45f)
                        : new Color(1f, 1f, 1f, 0.12f);
            Gizmos.DrawWireSphere(transform.position + Vector3.up * 0.05f, _cc != null ? _cc.radius * 2.2f : 0.8f);

            Gizmos.color = new Color(0.3f, 1f, 0.4f, 0.8f);
            Gizmos.DrawRay(transform.position + Vector3.up * 0.1f, MoveDirection * 1.5f);
        }
    }
}
