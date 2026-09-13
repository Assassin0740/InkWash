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
        [Tooltip("常规移动速度（米/秒）")]
        public float walkSpeed = 4.5f;

        [Tooltip("按住疾跑键时的速度")]
        public float runSpeed = 7.2f;

        [Tooltip("起步加速度：越大越跟手")]
        public float acceleration = 26f;

        [Tooltip("刹车减速度：略大于加速度，停步更干脆")]
        public float deceleration = 34f;

        [Tooltip("转向角速度（度/秒）。攻击中会按 attackTurnScale 打折")]
        public float turnSpeedDeg = 900f;

        [Header("步伐同步（消除脚滑）")]
        // 资源约束（已实测）：UAL2 里唯一的循环行走片段是 Walk_Carry_Loop（2.0s），
        // 没有正经的 Walk / Run 循环。所以"消除脚滑"只能靠调播放速度，而不是换一段更快的动画。
        // 走路 4.5/1.9 = 2.37 倍，跑步 7.2/1.9 = 3.79 倍 —— 后者约合 0.53s 一个步态周期，
        // 观感是慢跑，不抽帧。等有了正式角色动画（含独立 Run）再换片段。
        [Tooltip("动画片段自身的「地面速度」参考值（米/秒）。播放速度 = 实际速度 / 本值")]
        public float footSyncReferenceSpeed = 1.9f;

        [Tooltip("播放速度下限，防止慢走时腿部僵住")]
        public float motionSpeedMin = 0.7f;

        [Tooltip("播放速度上限，防止高速时腿部抽帧。\n" +
                 "取值必须 ≥ runSpeed/footSyncReferenceSpeed，否则跑起来会被截断而产生残余滑步：\n" +
                 "跑步 7.2/1.9 = 3.79，所以上限取 4.2 才不会削弱步伐同步。")]
        public float motionSpeedMax = 4.2f;

        [Header("冲刺 / 闪避")]
        public float dashSpeed = 14f;

        [Tooltip("冲刺持续时间。必须与 Dash 动画时长对齐（否则会出现「挥着剑滑行」）")]
        public float dashDuration = 0.32f;

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
            if (IsCancelWindowOpen)
            {
                Vector3 dir = ResolveMoveDirection();
                if (dir.sqrMagnitude > 1e-4f)
                {
                    MoveDirection = dir;
                    float target = (_runHeld ? runSpeed : walkSpeed);
                    _velocity.x = Mathf.Lerp(_velocity.x, dir.x * target, 8f * dt);
                    _velocity.z = Mathf.Lerp(_velocity.z, dir.z * target, 8f * dt);
                }
            }

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

            // Speed 用「期望速度」而非实际速度：攻击中移动被锁定，期望速度自然趋零，
            // 不会因为打击位移把状态机误推到 Move。
            animator.SetFloat(HashSpeed, DesiredSpeed, 0.10f, Time.deltaTime);

            // 步伐同步：播放速度 = 实际速度 / 片段参考速度
            float motion = Mathf.Clamp(CurrentSpeed / Mathf.Max(footSyncReferenceSpeed, 0.01f),
                motionSpeedMin, motionSpeedMax);
            animator.SetFloat(HashMotionSpeed, motion, 0.10f, Time.deltaTime);

            animator.SetBool(HashGrounded, _cc.isGrounded);

            // 上半身覆盖层权重：只在移动/待机时生效（攻击、冲刺交给全身动画）
            float wantUpper = _phase == ActionPhase.Locomotion ? 1f : 0f;
            float cur = animator.GetLayerWeight(1);
            animator.SetLayerWeight(1, Mathf.MoveTowards(cur, wantUpper, 4f * Time.deltaTime));
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
                animator.SetLayerWeight(1, 1f);
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
