using System;
using InkWash.Combat;
using UnityEngine;
using UnityEngine.AI;

namespace InkWash.Enemies
{
    public enum EnemyState
    {
        Spawning,   // 出场（可无动画）
        Idle,       // 未发现玩家
        Chase,      // 追击 / 走位
        Attack,     // 前摇 → 判定 → 收招
        HitStun,    // 受击硬直
        Dead,
    }

    /// <summary>
    /// 敌人基类：NavMeshAgent 移动 + Animator 动画 + 一个显式状态机 + <see cref="IDamageable"/>。
    ///
    /// 设计取舍（三条，都是为了让验收可断言）：
    /// 1) **不用 Animator 的转移条件来驱动行为**，而是用显式状态机（枚举 + 计时器）：
    ///    动画只是"跟着状态播"，判断全在 C# 里。这样"敌人第几秒进入攻击、第几秒出现判定"
    ///    是可以直接读出来的数字，而不是"看动画像不像在打"。
    ///    本项目已经吃过"同一个阈值被动画状态机和 C# 两处管"的亏，所以时间口径只留一份。
    /// 2) **判定时刻用配置时间（windup）而非动画事件**：动画事件要在动画窗口里手工打点，
    ///    换片段即失效；配置时间可以随片段长度重算，且帧率无关。
    /// 3) **击退走 agent.Move**，不加速度/不碰刚体：NavMeshAgent 自己就能在导航网格上位移，
    ///    加 Rigidbody 会引入穿插与重力副作用（玩家是 CharacterController，用的是同一套理由）。
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    public abstract class EnemyBase : MonoBehaviour, IDamageable
    {
        [Header("身份")]
        public string enemyName = "墨徒";

        [Header("生存")]
        public float maxHealth = 40f;

        [Header("移动")]
        public float walkSpeed = 1.5f;
        public float chaseSpeed = 3.4f;
        public float acceleration = 14f;
        public float turnSpeedDeg = 720f;

        [Header("感知")]
        public float sightRange = 16f;
        public float loseSightRange = 24f;
        [Range(10f, 360f)] public float sightAngleDeg = 200f;
        [Tooltip("需要视线无遮挡才算发现（被柱子挡住不会隔墙发现你）")]
        public bool requireLineOfSight = true;
        public Vector3 eyeOffset = new Vector3(0f, 1.4f, 0f);

        [Header("攻击")]
        [Tooltip("进入这个距离就停下开打")]
        public float attackRange = 2.0f;
        public float attackCooldown = 1.6f;
        [Tooltip("前摇：从攻击开始到判定出现")]
        public float attackWindup = 0.32f;
        [Tooltip("判定窗口时长")]
        public float attackActive = 0.16f;
        [Tooltip("收招")]
        public float attackRecover = 0.45f;

        [Header("动画")]
        public Animator animator;
        public float speedDamp = 0.12f;

        [Header("出场")]
        public float spawnDelay = 0.25f;

        [Header("死亡")]
        [Tooltip("死亡后停留多久销毁（0 = 不销毁）")]
        public float destroyAfterDeath = 2.4f;

        [Header("诊断（只读）")]
        [SerializeField] protected EnemyState _state = EnemyState.Spawning;
        [SerializeField] protected float _stateTime;
        [SerializeField] protected float _health;
        [SerializeField] protected float _distanceToPlayer;
        [SerializeField] protected int _attackCount;
        [SerializeField] protected int _damageTakenCount;

        // ---- 全局统计（波次系统用）----
        public static int AliveCount;
        public static int TotalKills;
        public static int TotalSpawned;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { AliveCount = 0; TotalKills = 0; TotalSpawned = 0; }

        // ---- 只读接口 ----
        public EnemyState State => _state;
        public float StateTime => _stateTime;
        public float Health => _health;
        public float HealthRatio => maxHealth > 0f ? Mathf.Clamp01(_health / maxHealth) : 0f;
        public float DistanceToPlayer => _distanceToPlayer;
        public int AttackCount => _attackCount;
        public int DamageTakenCount => _damageTakenCount;
        public bool IsAlive => _health > 0f && _state != EnemyState.Dead;

        public Faction Faction => Faction.Enemy;
        public Transform Transform => transform;

        // ---- IDamageable ----
        public virtual bool TakeDamage(DamageInfo info)
        {
            if (!IsAlive) return false;

            // 伤害倍率在**扣血之前**由子类决定 —— 弹反要减伤就必须在这一步，
            // 放到 OnDamaged（扣完血之后）已经来不及了。
            float mult = DamageMultiplier(info);
            _health = Mathf.Max(0f, _health - Mathf.Max(0f, info.amount) * mult);
            _damageTakenCount++;
            OnDamaged(info);

            if (_health <= 0f) { Die(); return true; }

            // 硬直（弹反会传更长的 hitStun，见 EnemyElite）
            float stun = (info.hitStun > 0f ? info.hitStun : 0.3f) * StunMultiplier(info);
            EnterHitStun(stun, info);
            HitStop.Request(info.hitStop, 0.06f);
            return true;
        }

        /// <summary>子类可改写（精英的弹反窗口在这里判定）。</summary>
        protected virtual void OnDamaged(DamageInfo info) { }

        /// <summary>伤害倍率。<see cref="EnemyElite"/> 用它在弹反窗口内减伤。</summary>
        protected virtual float DamageMultiplier(DamageInfo info) => 1f;

        /// <summary>硬直倍率（弹反后可以给更长的硬直）。</summary>
        protected virtual float StunMultiplier(DamageInfo info) => 1f;

        /// <summary>决定这次攻击用第一套还是第二套动作（精英的冲锋用它）。</summary>
        protected virtual void ChooseAttack() { _useSecondAttack = false; }

        /// <summary>攻击过程中的位移（精英冲锋用它）。默认不动。</summary>
        protected virtual void AttackMovement(float t, float hitAt) { }

        // ---- 内部 ----
        protected NavMeshAgent _agent;
        protected Animator _anim;
        protected Collider _collider;
        protected Hitbox _hitbox;

        protected Vector3 _knockVelocity;
        protected float _knockTimer;
        protected float _cooldownTimer;
        protected float _repathTimer;
        protected float _speedForAnim;
        protected bool _deadHandled;
        protected bool _useSecondAttack;

        protected static readonly int HashSpeed = Animator.StringToHash("Speed");
        protected static readonly int HashAttack = Animator.StringToHash("Attack");
        protected static readonly int HashAttack2 = Animator.StringToHash("Attack2");
        protected static readonly int HashHit = Animator.StringToHash("Hit");
        protected static readonly int HashDead = Animator.StringToHash("Dead");

        protected virtual void Awake()
        {
            _agent = GetComponent<NavMeshAgent>();
            _anim = animator != null ? animator : GetComponentInChildren<Animator>();
            _collider = GetComponent<Collider>();
            _hitbox = GetComponentInChildren<Hitbox>(true);
            _health = maxHealth;
        }

        protected virtual void OnEnable()
        {
            AliveCount++;
            TotalSpawned++;
        }

        protected virtual void OnDisable()
        {
            // 只在"还活着就被移除"时减一次；Die() 里已经减过的不重复减
            if (!_deadHandled) AliveCount = Mathf.Max(0, AliveCount - 1);
        }

        protected virtual void Start()
        {
            if (_agent != null)
            {
                _agent.speed = walkSpeed;
                _agent.acceleration = acceleration;
                _agent.updateRotation = false;   // 转向自己管，否则 NavMeshAgent 会跟手感打架
                _agent.stoppingDistance = 0f;
            }
            if (_hitbox != null) _hitbox.ownerFaction = Faction.Enemy;
            // 距离必须在 Start 就算一次：否则在"生成的那一帧"被读到的是 0，
            // 报告里会出现「距玩家 0.000 m」这种一眼看去像 bug 的假数字
            // （诊断字段一旦会撒谎，就会浪费一整轮排查）。
            UpdateDistance();
            Enter(EnemyState.Spawning);
        }

        protected virtual void Update()
        {
            _stateTime += Time.deltaTime;
            if (_cooldownTimer > 0f) _cooldownTimer -= Time.deltaTime;
            if (_repathTimer > 0f) _repathTimer -= Time.deltaTime;

            UpdateDistance();
            TickKnockback();

            switch (_state)
            {
                case EnemyState.Spawning: TickSpawning(); break;
                case EnemyState.Idle: TickIdle(); break;
                case EnemyState.Chase: TickChase(); break;
                case EnemyState.Attack: TickAttack(); break;
                case EnemyState.HitStun: TickHitStun(); break;
                case EnemyState.Dead: TickDead(); break;
            }

            DriveAnimation();
        }

        // ---------------- 状态切换 ----------------
        protected void Enter(EnemyState s)
        {
            _state = s;
            _stateTime = 0f;
            OnEnterState(s);
        }

        protected virtual void OnEnterState(EnemyState s)
        {
            switch (s)
            {
                case EnemyState.Idle:
                    SetAgentSpeed(0f);
                    break;
                case EnemyState.Chase:
                    ResumeAgent();
                    SetAgentSpeed(chaseSpeed);
                    break;
                case EnemyState.Attack:
                    SetAgentSpeed(0f);
                    StopAgent();
                    FacePlayerInstant();
                    _attackCount++;
                    ChooseAttack();
                    // 冷却从"攻击开始"起算，覆盖整段动作 + 额外冷却 —— 否则会动作没播完又开下一次
                    _cooldownTimer = attackWindup + attackActive + attackRecover + attackCooldown;
                    {
                        int trig = (_useSecondAttack && HasParam(HashAttack2)) ? HashAttack2 : HashAttack;
                        if (_anim != null && HasParam(trig)) _anim.SetTrigger(trig);
                    }
                    break;
                case EnemyState.HitStun:
                    SetAgentSpeed(0f);
                    StopAgent();
                    if (_anim != null && HasParam(HashHit)) _anim.SetTrigger(HashHit);
                    break;
            }
        }

        // ---------------- 各状态 ----------------
        protected virtual void TickSpawning()
        {
            StopAgent();
            if (_stateTime >= spawnDelay) Enter(EnemyState.Idle);
        }

        protected virtual void TickIdle()
        {
            StopAgent();
            if (PlayerRef.Exists && CanSeePlayer()) Enter(EnemyState.Chase);
        }

        protected virtual void TickChase()
        {
            if (!PlayerRef.Exists) { Enter(EnemyState.Idle); return; }
            float dist = _distanceToPlayer;

            if (dist > loseSightRange) { Enter(EnemyState.Idle); return; }

            if (dist <= attackRange && _cooldownTimer <= 0f && CanSeePlayer())
            {
                Enter(EnemyState.Attack);
                return;
            }

            float want = PreferredRange;
            if (dist > want && CanSeePlayer())
            {
                // 追：交给 NavMeshAgent 走，朝向跟着"它实际想去的方向"（不然会像漂移）
                ResumeAgent();
                SetAgentSpeed(chaseSpeed);
                if (_repathTimer <= 0f)
                {
                    _repathTimer = 0.15f;
                    _agent.SetDestination(PlayerRef.Position);
                }
                FacePlayerTowardAgent(Time.deltaTime);
            }
            else
            {
                // 已经站到位（近战贴脸 / 远程拉够距离）：停下，只转向玩家
                StopAgent();
                if (_agent != null && _agent.enabled && _agent.hasPath) _agent.ResetPath();
                FacePlayer(Time.deltaTime);
            }
        }

        protected virtual void TickAttack()
        {
            StopAgent();
            FacePlayer(Time.deltaTime);

            float t = _stateTime;
            float hitAt = attackWindup;
            float endAt = attackWindup + attackActive + attackRecover;

            AttackMovement(t, hitAt);

            // 判定只在 t 跨过 windup 的那一帧触发一次
            float prev = _stateTime - Time.deltaTime;
            if (prev < hitAt && t >= hitAt) PerformHit();

            if (t >= endAt) Enter(EnemyState.Chase);
        }

        protected virtual void TickHitStun()
        {
            StopAgent();
            if (_stateTime >= _hitStunDuration) Enter(EnemyState.Chase);
        }

        protected virtual void TickDead()
        {
            StopAgent();
            if (destroyAfterDeath > 0f && _stateTime >= destroyAfterDeath) Destroy(gameObject);
        }

        /// <summary>攻击判定出现的瞬间。近战开 Hitbox 窗口，远程在这里生成墨弹。</summary>
        protected virtual void PerformHit()
        {
            if (_hitbox != null) _hitbox.Activate(Mathf.Max(0.06f, attackActive));
        }

        private float _hitStunDuration = 0.3f;

        protected virtual void EnterHitStun(float duration, DamageInfo info)
        {
            _hitStunDuration = duration;
            Enter(EnemyState.HitStun);

            Vector3 dir = info.hitDirection;
            dir.y = 0f;
            if (dir.sqrMagnitude > 1e-6f) dir.Normalize();
            _knockVelocity = dir * Mathf.Max(0f, info.knockback);
            _knockTimer = Mathf.Min(0.35f, duration);
        }

        protected virtual void Die()
        {
            if (_deadHandled) return;
            _deadHandled = true;
            AliveCount = Mathf.Max(0, AliveCount - 1);
            TotalKills++;
            _health = 0f;
            _knockVelocity = Vector3.zero;
            _knockTimer = 0f;
            if (_hitbox != null) _hitbox.Deactivate();
            if (_collider != null) _collider.enabled = false;
            if (_agent != null && _agent.enabled) { _agent.ResetPath(); _agent.enabled = false; }
            if (_anim != null && HasParam(HashDead)) _anim.SetTrigger(HashDead);
            Enter(EnemyState.Dead);
            OnDied();
            FireDied();
        }

        protected virtual void OnDied() { }

        /// <summary>死亡事件（波次系统订阅它来统计剩余敌人）。
        /// 用事件而不是"每帧去扫场景"，是因为扫场景需要 `FindObjectsOfType` —— 那是 O(场景) 的开销，
        /// 且延迟一帧才准。事件是即时的、零扫描的。</summary>
        public event Action<EnemyBase> DiedEvent;

        private void FireDied()
        {
            var e = DiedEvent;
            if (e == null) return;
            // 逐个 try：一个订阅者抛异常不该让后面的收不到（本项目踩过这个坑）
            foreach (var d in e.GetInvocationList())
            {
                try { ((Action<EnemyBase>)d).Invoke(this); }
                catch (Exception ex) { Debug.LogError("[EnemyBase] 死亡订阅者抛异常（已隔离）：" + ex); }
            }
            DiedEvent = null;
        }

        // ---------------- 工具 ----------------
        /// <summary>它想跟玩家保持的距离。近战 = 贴到攻击距离，远程 = 拉开。</summary>
        protected virtual float PreferredRange => attackRange * 0.8f;

        protected bool CanSeePlayer()
        {
            if (!PlayerRef.Exists) return false;
            Vector3 eye = transform.position + eyeOffset;
            Vector3 target = PlayerRef.Position + Vector3.up * 1.0f;
            Vector3 to = target - eye;
            float dist = to.magnitude;
            if (dist > sightRange) return false;

            Vector3 flat = new Vector3(to.x, 0f, to.z);
            if (flat.sqrMagnitude > 1e-4f)
            {
                float ang = Vector3.Angle(transform.forward, flat);
                if (ang > sightAngleDeg * 0.5f) return false;
            }

            if (!requireLineOfSight) return true;

            RaycastHit hit;
            if (Physics.Linecast(eye, target, out hit, ~0, QueryTriggerInteraction.Ignore))
            {
                var t = hit.collider.transform;
                return t == transform || t.IsChildOf(transform) || t == PlayerRef.Instance || t.IsChildOf(PlayerRef.Instance);
            }
            return true;
        }

        protected void SetAgentSpeed(float v)
        {
            if (_agent != null && _agent.enabled) _agent.speed = v;
        }

        protected void StopAgent()
        {
            if (_agent != null && _agent.enabled && _agent.isOnNavMesh)
            {
                _agent.isStopped = true;
                _agent.velocity = Vector3.zero;
            }
            // 注意：不要在这里把 _speedForAnim 直接清零 —— 那会让 DriveAnimation 的插值失效
            // （每帧被外部重置，Lerp 永远是起点）。交给 DriveAnimation 自己收敛。
        }

        protected void ResumeAgent()
        {
            if (_agent != null && _agent.enabled && _agent.isOnNavMesh) _agent.isStopped = false;
        }

        protected void FacePlayer(float dt)
        {
            if (!PlayerRef.Exists) return;
            Vector3 d = PlayerRef.Position - transform.position;
            d.y = 0f;
            if (d.sqrMagnitude < 1e-6f) return;
            var want = Quaternion.LookRotation(d.normalized, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, want, turnSpeedDeg * dt);
        }

        protected void FacePlayerTowardAgent(float dt)
        {
            if (_agent == null || !_agent.hasPath) return;
            Vector3 d = _agent.desiredVelocity;
            d.y = 0f;
            if (d.sqrMagnitude < 1e-4f) { FacePlayer(dt); return; }
            var want = Quaternion.LookRotation(d.normalized, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, want, turnSpeedDeg * dt);
        }

        protected void FacePlayerInstant()
        {
            if (!PlayerRef.Exists) return;
            Vector3 d = PlayerRef.Position - transform.position;
            d.y = 0f;
            if (d.sqrMagnitude < 1e-6f) return;
            transform.rotation = Quaternion.LookRotation(d.normalized, Vector3.up);
        }

        protected void TickKnockback()
        {
            if (_knockTimer <= 0f) return;
            _knockTimer -= Time.deltaTime;
            if (_agent != null && _agent.enabled && _agent.isOnNavMesh && _knockVelocity.sqrMagnitude > 1e-6f)
                _agent.Move(_knockVelocity * Time.deltaTime);
            _knockVelocity = Vector3.Lerp(_knockVelocity, Vector3.zero, Time.deltaTime * 8f);
        }

        protected void UpdateDistance()
        {
            _distanceToPlayer = PlayerRef.Exists
                ? Vector3.Distance(transform.position, PlayerRef.Position)
                : float.PositiveInfinity;
        }

        protected void DriveAnimation()
        {
            if (_anim == null) return;

            float speed = 0f;
            if (_agent != null && _agent.enabled && _agent.isOnNavMesh && !_agent.isStopped)
                speed = new Vector3(_agent.velocity.x, 0f, _agent.velocity.z).magnitude;
            else if (_state == EnemyState.HitStun || _state == EnemyState.Attack)
                speed = 0f;

            _speedForAnim = Mathf.Lerp(_speedForAnim, speed, speedDamp <= 0f ? 1f : Time.deltaTime / speedDamp);
            if (HasParam(HashSpeed)) _anim.SetFloat(HashSpeed, _speedForAnim);
        }

        protected bool HasParam(int hash)
        {
            if (_anim == null || _anim.runtimeAnimatorController == null) return false;
            foreach (var p in _anim.parameters)
                if (p.nameHash == hash) return true;
            return false;
        }

        /// <summary>把敌人放回导航网格上（生成点/被击退出界时用）。</summary>
        public bool SnapToNavMesh(float maxDist = 4f)
        {
            NavMeshHit hit;
            if (NavMesh.SamplePosition(transform.position, out hit, maxDist, NavMesh.AllAreas))
            {
                transform.position = hit.position;
                return true;
            }
            return false;
        }
    }
}
