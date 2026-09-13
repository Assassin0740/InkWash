using UnityEngine;

namespace InkWash.Player
{
    /// <summary>
    /// 玩家角色控制器（Sprint 1）。
    ///
    /// 设计要点：
    /// 1. 相机相对移动 —— 输入以相机朝向为基准解算世界方向，保证"按 W 就朝屏幕上方走"，
    ///    这是第三人称动作游戏的手感基线。
    /// 2. 加减速分离 —— 起步与刹车用不同速率，制造重量感；直接设速度会让移动"发飘"。
    /// 3. 冲刺内建无敌帧窗口 —— S2 的闪避 i-frame 直接复用这里的 <see cref="IsInvincible"/>，
    ///    避免两套判定打架。
    /// 4. 输入读取集中在 <see cref="ReadInput"/> —— 目前用传统 Input Manager，
    ///    将来换 Input System 只需改这一个方法，不动运动逻辑。
    ///
    /// 注意：本阶段无跳跃，<see cref="ApplyGravity"/> 只负责把角色压在地面上。
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    [DisallowMultipleComponent]
    public class PlayerController : MonoBehaviour
    {
        [Header("移动")]
        [Tooltip("常规移动速度（米/秒）")]
        public float walkSpeed = 5.5f;

        [Tooltip("按住疾跑键时的速度")]
        public float runSpeed = 9f;

        [Tooltip("起步加速度：越大越跟手")]
        public float acceleration = 26f;

        [Tooltip("刹车减速度：略大于加速度，停步更干脆")]
        public float deceleration = 34f;

        [Tooltip("转向角速度（度/秒）。设为 0 表示瞬间转向")]
        public float turnSpeedDeg = 900f;

        [Header("冲刺 / 闪避")]
        public float dashSpeed = 18f;

        [Tooltip("冲刺持续时间")]
        public float dashDuration = 0.22f;

        [Tooltip("冲刺冷却")]
        public float dashCooldown = 0.7f;

        [Tooltip("冲刺起始的无敌窗口时长，供 S2 的闪避 i-frame 使用")]
        public float dashInvincibleWindow = 0.16f;

        [Header("重力")]
        public float gravity = -24f;

        [Tooltip("落地时施加的下压速度，避免走下坡时反复离地")]
        public float groundedStick = -3f;

        [Header("引用")]
        [Tooltip("相机变换，留空则自动取 Camera.main")]
        public Transform cameraTransform;

        [Tooltip("动画驱动组件，留空则自动在子节点里找")]
        public Animator animator;

        // ---------------- 对外只读状态（S2 战斗系统 / S3 敌人 AI 会读取） ----------------

        /// <summary>
        /// 水平面**实际**速度向量（y 恒为 0）。
        /// 注意与 <see cref="DesiredSpeed"/> 的区别：顶住墙体时角色实际速度为 0，
        /// 但期望速度仍是 8~9 m/s。动画与 HUD 必须用实际速度，否则会"贴着墙跑步"。
        /// </summary>
        public Vector3 PlanarVelocity => new Vector3(_actualVelocity.x, 0f, _actualVelocity.z);

        /// <summary>当前水平**实际**速率（m/s）。</summary>
        public float CurrentSpeed => PlanarVelocity.magnitude;

        /// <summary>当前水平**期望**速率（m/s）—— 运动积分的结果，未考虑碰撞阻挡。</summary>
        public float DesiredSpeed => new Vector2(_velocity.x, _velocity.z).magnitude;

        /// <summary>是否处于冲刺中。</summary>
        public bool IsDashing => _dashTimer > 0f;

        /// <summary>是否处于无敌帧窗口内（冲刺起始阶段）。</summary>
        public bool IsInvincible => _dashTimer > dashDuration - dashInvincibleWindow;

        /// <summary>本帧的世界空间移动方向（未归一化时为输入方向）。</summary>
        public Vector3 MoveDirection { get; private set; }

        /// <summary>冲刺是否已冷却完毕。</summary>
        public bool CanDash => _dashCooldownTimer <= 0f && !IsDashing;

        /// <summary>是否贴地。</summary>
        public bool IsGrounded => _cc != null && _cc.isGrounded;

        // ---------------- 内部状态 ----------------

        private CharacterController _cc;
        private Vector3 _velocity;          // 世界空间"期望"速度（运动积分的产物）
        private Vector3 _actualVelocity;    // 世界空间"实际"速度（由位移反推，含碰撞阻挡）
        private Vector2 _input;             // x = 左右, y = 前后
        private bool _runHeld;

        private float _dashTimer;
        private float _dashCooldownTimer;
        private Vector3 _dashDir;

        // 输入注入：供自动化验收、演示录屏与过场动画使用。
        // 之所以必须有这个入口：本项目使用传统 Input Manager，
        // 自动化工具无法注入真实按键（键盘注入需要 Input System 包），
        // 因此把"输入来源"做成可替换的，运动逻辑本身保持单一实现。
        private bool _overrideActive;
        private Vector2 _overrideMove;
        private bool _overrideRun;
        private bool _overrideDashQueued;

        // 动画参数哈希：避免每帧字符串查找
        private static readonly int HashSpeed = Animator.StringToHash("Speed");
        private static readonly int HashMotionSpeed = Animator.StringToHash("MotionSpeed");
        private static readonly int HashGrounded = Animator.StringToHash("Grounded");
        private static readonly int HashDash = Animator.StringToHash("Dash");

        private void Awake()
        {
            _cc = GetComponent<CharacterController>();
            if (animator == null) animator = GetComponentInChildren<Animator>();
            if (cameraTransform == null && Camera.main != null)
                cameraTransform = Camera.main.transform;
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            Vector3 posBefore = transform.position;

            ReadInput();
            TickTimers(dt);

            if (IsDashing)
                TickDashMotion(dt);
            else
                TickLocomotion(dt);

            ApplyGravity(dt);
            ApplyRotation(dt);

            // 用真实位移反推实际速度 —— CharacterController.Move 被挡住时不会回写 _velocity，
            // 若不单独测量，动画就会在角色顶着墙时继续播放跑步。
            _actualVelocity = (transform.position - posBefore) / Mathf.Max(dt, 1e-5f);

            PushAnimatorState();
        }

        // ------------------------------------------------------------------
        // 输入
        // ------------------------------------------------------------------

        /// <summary>
        /// 读取输入。当前用传统 Input Manager（KeyCode 直读，不依赖 InputManager.asset 的轴配置，
        /// 换机器不会被改坏）。将来接入 Input System 时只需替换本方法体。
        /// 若已开启输入注入，则改用注入值 —— 运动逻辑单一路径，不做分支复制。
        /// </summary>
        private void ReadInput()
        {
            if (_overrideActive)
            {
                _input = _overrideMove;
                if (_input.sqrMagnitude > 1f) _input.Normalize();
                _runHeld = _overrideRun;
                if (_overrideDashQueued)
                {
                    _overrideDashQueued = false;
                    if (CanDash) StartDash();
                }
                return;
            }

            float x = 0f, y = 0f;
            if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) x -= 1f;
            if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) x += 1f;
            if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) y -= 1f;
            if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) y += 1f;

            _input = new Vector2(x, y);
            if (_input.sqrMagnitude > 1f) _input.Normalize();   // 斜向不该更快

            _runHeld = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

            if (Input.GetKeyDown(KeyCode.Space) && CanDash)
                StartDash();
        }

        // ------------------------------------------------------------------
        // 输入注入（自动化验收 / 演示录屏 / 过场动画）
        // ------------------------------------------------------------------

        /// <summary>开启输入注入，忽略真实键盘。</summary>
        public void BeginInputOverride()
        {
            _overrideActive = true;
            _overrideMove = Vector2.zero;
            _overrideRun = false;
            _overrideDashQueued = false;
        }

        /// <summary>结束输入注入，恢复读取真实键盘（同时清空运动输入，避免"粘键"）。</summary>
        public void EndInputOverride()
        {
            _overrideActive = false;
            _overrideMove = Vector2.zero;
            _overrideRun = false;
            _overrideDashQueued = false;
        }

        /// <summary>注入移动输入。<paramref name="move"/> 的 x 为左右、y 为前后。</summary>
        public void SetInjectedMove(Vector2 move, bool runHeld)
        {
            _overrideMove = move;
            _overrideRun = runHeld;
        }

        /// <summary>注入一次冲刺请求（下一帧生效）。</summary>
        public void RequestInjectedDash()
        {
            _overrideDashQueued = true;
        }

        /// <summary>当前是否处于输入注入状态。</summary>
        public bool IsInputOverridden => _overrideActive;

        // ------------------------------------------------------------------
        // 计时器
        // ------------------------------------------------------------------

        private void TickTimers(float dt)
        {
            if (_dashTimer > 0f) _dashTimer -= dt;
            if (_dashCooldownTimer > 0f) _dashCooldownTimer -= dt;
        }

        private void StartDash()
        {
            Vector3 dir = ResolveMoveDirection();
            // 无输入时朝角色正面冲，避免原地冲刺
            _dashDir = dir.sqrMagnitude > 0.001f ? dir : transform.forward;
            _dashDir.y = 0f;
            _dashDir.Normalize();

            _dashTimer = dashDuration;
            _dashCooldownTimer = dashCooldown + dashDuration;

            if (animator != null) animator.SetTrigger(HashDash);
        }

        // ------------------------------------------------------------------
        // 移动
        // ------------------------------------------------------------------

        /// <summary>把二维输入解算成相机相对的世界方向。相机未就绪时退化为世界坐标。</summary>
        private Vector3 ResolveMoveDirection()
        {
            if (_input.sqrMagnitude < 0.0001f) return Vector3.zero;

            Vector3 fwd, right;
            if (cameraTransform != null)
            {
                fwd = cameraTransform.forward;
                right = cameraTransform.right;
                fwd.y = 0f; right.y = 0f;
                // 相机接近垂直俯视时 forward 在水平面的投影会退化，此时退回世界坐标
                if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
                fwd.Normalize();
                right.Normalize();
            }
            else
            {
                fwd = Vector3.forward;
                right = Vector3.right;
            }

            return (fwd * _input.y + right * _input.x).normalized;
        }

        private void TickLocomotion(float dt)
        {
            Vector3 desiredDir = ResolveMoveDirection();
            MoveDirection = desiredDir;

            float targetSpeed = 0f;
            if (desiredDir.sqrMagnitude > 0.0001f)
                targetSpeed = _runHeld ? runSpeed : walkSpeed;

            Vector3 targetVel = desiredDir * targetSpeed;
            Vector3 planar = new Vector3(_velocity.x, 0f, _velocity.z);   // 内部用期望速度积分

            // 起步与刹车用不同速率
            float rate = (targetSpeed > planar.magnitude) ? acceleration : deceleration;
            planar = Vector3.MoveTowards(planar, targetVel, rate * dt);

            _velocity.x = planar.x;
            _velocity.z = planar.z;
        }

        private void TickDashMotion(float dt)
        {
            MoveDirection = _dashDir;
            Vector3 planar = new Vector3(_velocity.x, 0f, _velocity.z);
            planar = Vector3.MoveTowards(planar, _dashDir * dashSpeed, 120f * dt);
            _velocity.x = planar.x;
            _velocity.z = planar.z;
        }

        private void ApplyGravity(float dt)
        {
            if (_cc.isGrounded && _velocity.y < 0f)
                _velocity.y = groundedStick;
            else
                _velocity.y += gravity * dt;

            _cc.Move(_velocity * dt);
        }

        private void ApplyRotation(float dt)
        {
            Vector3 dir = MoveDirection;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f) return;

            Quaternion target = Quaternion.LookRotation(dir, Vector3.up);
            if (turnSpeedDeg <= 0f)
                transform.rotation = target;
            else
                transform.rotation = Quaternion.RotateTowards(transform.rotation, target, turnSpeedDeg * dt);
        }

        // ------------------------------------------------------------------
        // 动画
        // ------------------------------------------------------------------

        private void PushAnimatorState()
        {
            if (animator == null) return;

            float speed = CurrentSpeed;
            animator.SetFloat(HashSpeed, speed, 0.08f, Time.deltaTime);
            animator.SetFloat(HashMotionSpeed, _runHeld ? 1f : 0.6f, 0.08f, Time.deltaTime);
            animator.SetBool(HashGrounded, _cc.isGrounded);
        }

        // ------------------------------------------------------------------
        // 编辑器可视化
        // ------------------------------------------------------------------

        private void OnDrawGizmosSelected()
        {
            if (!Application.isPlaying) return;

            Gizmos.color = IsInvincible ? new Color(0.2f, 0.9f, 1f, 0.55f)
                          : IsDashing ? new Color(1f, 0.85f, 0.2f, 0.35f)
                          : new Color(1f, 1f, 1f, 0.12f);
            Gizmos.DrawWireSphere(transform.position + Vector3.up * 0.05f, _cc != null ? _cc.radius * 2.2f : 0.8f);

            Gizmos.color = new Color(0.3f, 1f, 0.4f, 0.8f);
            Gizmos.DrawRay(transform.position + Vector3.up * 0.1f, MoveDirection * 1.5f);
        }
    }
}
