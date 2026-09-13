using System;
using UnityEngine;
using InkWash.CameraRig;

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
        [Tooltip("走路速度（米/秒）。对应动画 Walk（UAL1 Walk_Loop）")]
        public float walkSpeed = 2.2f;

        [Tooltip("按住 Shift 时的跑步速度。对应动画 Run（Kevin Iglesias Run01_Forward）\n" +
                 "默认值必须与预制体上的序列化值一致（4.2）。曾经默认写 5.6、预制体是 4.2，\n" +
                 "两边不一致很容易让人误判\"实际生效的是哪个\"。")]
        public float runSpeed = 4.2f;

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
        // 上一版只有一段「抱物走」可当移动动画，走路和跑步都靠调它的播放速度硬撑
        // （跑步要 3.79 倍），所以脚步永远对不上位移。本版两段各配自己的片段与参考速度。
        //
        // 注意「播放倍率落在 1.0~1.8 是健康的」这个旧结论是错的：倍率只说明片段被拉伸多少，
        // 真正的观感指标是**步频**（2 步 / 片段时长 × 倍率）。1.8 倍摊到一段 0.93s 的慢跑上
        // 就是 3.9 步/秒，已经超过真人冲刺；腿看起来自然与否看的是这个数，
        // 验收里的「疾跑步频」断言现在就是这么算的。
        // 参考值用 Tools/cs/s2_probe_refspeed.cs 实测标定：锁住接触点，只取「竖直速度≈0」
        // （真正踩在地上）的帧，量它相对角色根节点的水平后移速度。
        // 该方法在 Walk_Loop 上做了交叉验证：算出 1.15 m/s、单步 0.77m，与真人走路一致。
        //
        // 注意：Walk_Loop 实测原生只有 1.15 m/s，而 walkSpeed = 2.2 m/s 本身就超出素材设计范围。
        // 这里**故意保留 1.55**：宁可让脚有一点滑步，也不让腿的周期被压得太短
        // （取 1.15 的话步频会变成 1.91×）。要根治只能降 walkSpeed，见 Docs 待办。
        [Tooltip("走路片段（Walk_Loop）自身的地面速度参考值（米/秒）。实测 1.15，这里按 1.55 保守取")]
        public float walkRefSpeed = 1.55f;

        [Tooltip("跑步片段（Kevin Iglesias Run01_Forward）自身的地面速度参考值（米/秒）。\n" +
                 "换片段后必须重量：Tools/cs/s2_probe_refspeed_ki.cs 实测 4.49（单步 1.35m、200 步/分）。\n" +
                 "取这个值 → 倍率 4.2/4.49 = 0.94×，脚不再打滑，步频 3.1 步/秒落在正常跑步区间。\n" +
                 "（旧值 3.70 是给 Quaternius Sprint_Loop 标的，套在新片段上会让脚后滑 ≈0.9 m/s。）")]
        public float runRefSpeed = 4.49f;

        [Tooltip("播放速度下限，防止慢走时腿部僵住")]
        public float motionSpeedMin = 0.6f;

        [Tooltip("播放速度上限，防止高速时腿部抽帧。\n" +
                 "取值必须 ≥ max(walkSpeed/walkRefSpeed, runSpeed/runRefSpeed)，否则会被截断而产生残余滑步。\n" +
                 "当前：走路 2.2/1.55 = 1.42，跑步 4.2/4.49 = 0.94，上限取 2.2 留足余量。")]
        public float motionSpeedMax = 2.2f;

        [Header("冲刺 / 闪避")]
        public float dashSpeed = 7.4f;

        [Tooltip("冲刺持续时间。必须与 Dash 动画（UAL1 Roll，1.47s）对齐：\n" +
                 "状态机里 Dash 状态的播放速度就是按 Roll.length / 本值 算出来的，\n" +
                 "改这里要同步改 Tools/cs/s2_build_animator.cs 的 DashDuration，验收会核对。")]
        public float dashDuration = 0.72f;

        public float dashCooldown = 0.55f;

        [Tooltip("冲刺起始的无敌窗口时长")]
        public float dashInvincibleWindow = 0.2f;

        [Header("三段连击")]
        [Tooltip("各段的前冲位移距离（米）—— 由代码施加，不是滑行")]
        public float[] comboLungeDistance = { 1.2f, 1.5f, 2.6f };

        [Tooltip("各段位移持续时长（秒）")]
        public float[] comboLungeDuration = { 0.22f, 0.24f, 0.42f };

        [Tooltip("各段挥砍动作总时长（秒），与 Player.controller 里的状态时长一致")]
        public float[] comboSwingDuration = { 0.43f, 0.53f, 1.43f };

        [Tooltip("各段的命中时刻（秒）—— 用于触发刀光与震屏")]
        public float[] comboHitTime = { 0.15f, 0.18f, 0.4f };

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

        private int _comboStep;
        private bool _attackQueued;
        private float _attackQueuedAt = -99f;
        private bool _hitFiredThisStep;
        /// <summary>本段攻击是否**确实**见到过攻击动画状态。见 TickAttack 末尾的说明。</summary>
        private bool _attackStateSeen;

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

            _runHeld = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

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

            float targetSpeed = dir.sqrMagnitude > 1e-4f ? (_runHeld ? runSpeed : walkSpeed) : 0f;
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

            // ---- 前冲位移：按速度曲线施加 ----
            // 曲线 v(t) = 2·D·(1 - t/T) / T 对 t 的积分恰好等于设定距离 D
            // （∫₀ᵀ v dt = D），所以这里必须**赋值**给 _velocity，而不是把 v·dt 累加进去。
            // 曾经写成 `_velocity += dir * (v * dt)`：那样 _velocity 只是慢慢涨到 ≈ D，
            // 真实位移变成 ∫_velocity dt（第二次积分），比设定值小一个量级 ——
            // 表现是"挥砍几乎不前进"。三段连击的位移学判定就是被这个吃掉的。
            float lungeDur = Mathf.Max(comboLungeDuration[idx], 1e-3f);
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
            if (!_hitFiredThisStep && _phaseTimer >= comboHitTime[idx])
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
            _dashCooldownTimer = dashCooldown + dashDuration;

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
                if (_comboStep >= 3) return false;
                if (IsCancelWindowOpen) { AdvanceCombo(); return true; }
                _attackQueued = true;
                _attackQueuedAt = Time.time;
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
            _phase = ActionPhase.Locomotion;
            Phase = ActionPhase.Locomotion;
            _phaseTimer = 0f;
            _comboStep = 0;
            _attackQueued = false;
            _attackStateSeen = false;
            IsCancelWindowOpen = false;
            _blendSpeed = 0f;   // 交给下一帧的 TickLocomotion 按输入重新决定
            BrakePlanar(Time.deltaTime, brakeRate * 25f);
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
            var st = animator.GetCurrentAnimatorStateInfo(0);
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
