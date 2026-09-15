using InkWash.Combat;
using UnityEngine;

namespace InkWash.Enemies
{
    /// <summary>
    /// 墨偶（远程）：保持距离 → 投墨弹 → 被贴近就后撤。
    ///
    /// 与近战最大的结构差别：它的"攻击距离"（能打到多远）和"想站的距离"（走位目标）
    /// 是**两个不同的量**。近战这两个量几乎重合，所以近战可以直接继承基类逻辑；
    /// 远程必须拆开 —— 否则要么它站在 2 m 外发法术（贴脸法师），
    /// 要么它以为要走到 9 m 才动手（永远不开火）。
    /// </summary>
    public class EnemyRanged : EnemyBase
    {
        [Header("墨偶·个性")]
        [Tooltip("想保持的距离（走位目标）")]
        public float keepDistance = 7.5f;
        [Tooltip("近于这个距离就开始后撤")]
        public float retreatDistance = 4.0f;
        [Tooltip("后撤时朝反方向走到多远")]
        public float retreatStep = 4.0f;

        [Header("投射物")]
        public GameObject projectilePrefab;
        public Transform muzzle;
        public float projectileDamage = 10f;
        public float projectileSpeed = 13f;
        [Tooltip("瞄准玩家的胸口而不是原点，否则弹道会偏低打在地面")]
        public float aimHeight = 1.0f;

        [Header("诊断（只读）")]
        [SerializeField] private int _projectilesSpawned;
        [SerializeField] private int _retreatCount;

        public int ProjectilesSpawned => _projectilesSpawned;
        public int RetreatCount => _retreatCount;

        protected override float PreferredRange => keepDistance;

        protected override void PerformHit()
        {
            if (projectilePrefab == null)
            {
                Debug.LogWarning("[EnemyRanged] 没配 projectilePrefab，改用判定体兜底");
                base.PerformHit();
                return;
            }

            Vector3 origin = muzzle != null ? muzzle.position : transform.position + Vector3.up * 1.2f;
            Vector3 aim = PlayerRef.Exists ? PlayerRef.Position + Vector3.up * aimHeight : origin + transform.forward * 8f;
            Vector3 dir = (aim - origin).normalized;

            var go = Instantiate(projectilePrefab, origin, Quaternion.LookRotation(dir, Vector3.up));
            go.name = "InkBolt";
            var proj = go.GetComponent<InkProjectile>();
            if (proj != null)
            {
                proj.owner = gameObject;
                proj.ownerFaction = Faction.Enemy;
                proj.damage = projectileDamage;
                proj.speed = projectileSpeed;
                proj.Launch(dir);
            }
            _projectilesSpawned++;
        }

        protected override void TickChase()
        {
            if (!PlayerRef.Exists) { Enter(EnemyState.Idle); return; }
            float dist = _distanceToPlayer;
            if (dist > loseSightRange) { Enter(EnemyState.Idle); return; }

            bool los = CanSeePlayer();

            // 太近：先跑开，别在原地被打
            if (dist < retreatDistance && los)
            {
                ResumeAgent();
                SetAgentSpeed(chaseSpeed * 0.95f);
                Vector3 away = transform.position + (transform.position - PlayerRef.Position).normalized * retreatStep;
                if (_repathTimer <= 0f)
                {
                    // 计数只能记「一次后撤决定」，不能记「帧数」。
                    // 放在 repath 分支里（每 0.22 s 才重新决策一次），否则 2 s 就能记出上百次，
                    // 报告里的"后撤 190 次"其实是 190 帧 —— 一个骗自己的数字。
                    _retreatCount++;
                    _repathTimer = 0.22f;
                    _agent.SetDestination(away);
                }
                FacePlayer(Time.deltaTime);
                return;
            }

            // 在射程内且站位合理：开火
            if (dist <= attackRange && _cooldownTimer <= 0f && los)
            {
                Enter(EnemyState.Attack);
                return;
            }

            // 太远：靠拢到 keepDistance
            if (dist > keepDistance && los)
            {
                ResumeAgent();
                SetAgentSpeed(chaseSpeed);
                if (_repathTimer <= 0f)
                {
                    _repathTimer = 0.18f;
                    _agent.SetDestination(PlayerRef.Position);
                }
                FacePlayerTowardAgent(Time.deltaTime);
            }
            else
            {
                StopAgent();
                if (_agent != null && _agent.enabled && _agent.hasPath) _agent.ResetPath();
                FacePlayer(Time.deltaTime);
            }
        }
    }
}
