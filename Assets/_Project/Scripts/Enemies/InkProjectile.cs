using InkWash.Combat;
using UnityEngine;

namespace InkWash.Enemies
{
    /// <summary>
    /// 墨弹：远程敌人的投掷物。
    ///
    /// 关键实现细节：**必须做扫掠检测，不能只测当前点**。
    /// 12 m/s 的弹丸在 30 fps 下单帧位移 0.4 m，若只在当前位置做球形检测，
    /// 会直接穿过 0.3 m 厚的目标（隧道效应）—— 表现是"明明瞄得很准却打不中"，
    /// 而且这种漏判是**概率性**的，极难复现。这里用「上一帧位置 → 这一帧位置」的胶囊扫掠。
    /// </summary>
    public class InkProjectile : MonoBehaviour
    {
        public Faction ownerFaction = Faction.Enemy;
        public GameObject owner;

        public float speed = 12f;
        public float damage = 10f;
        public float knockback = 3.5f;
        public float hitStun = 0.3f;
        public float hitStop = 0.04f;
        public float lifetime = 5f;
        public float radius = 0.33f;
        public LayerMask blockMask = ~0;

        [Header("诊断（只读）")]
        [SerializeField] private int _bouncesOffWalls;
        public int WallsHit => _bouncesOffWalls;

        private Vector3 _dir = Vector3.forward;
        private float _life;
        private readonly Collider[] _buf = new Collider[16];

        public void Launch(Vector3 direction)
        {
            _dir = direction.sqrMagnitude > 1e-6f ? direction.normalized : transform.forward;
            transform.rotation = Quaternion.LookRotation(_dir, Vector3.up);
        }

        private void Update()
        {
            float step = speed * Time.deltaTime;
            Vector3 prev = transform.position;
            Vector3 next = prev + _dir * step;

            int n = Physics.OverlapCapsuleNonAlloc(prev, next, radius, _buf, blockMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var col = _buf[i];
                if (col == null) continue;
                if (owner != null && (col.transform == owner.transform || col.transform.IsChildOf(owner.transform))) continue;

                var target = col.GetComponentInParent<IDamageable>();
                if (target != null && target.IsAlive && target.Faction != ownerFaction)
                {
                    Vector3 point = col.ClosestPoint(next);
                    var info = new DamageInfo
                    {
                        amount = damage,
                        hitPoint = point,
                        hitDirection = _dir,
                        knockback = knockback,
                        hitStun = hitStun,
                        hitStop = hitStop,
                        sourceFaction = ownerFaction,
                        source = owner,
                    };
                    target.TakeDamage(info);
                    Destroy(gameObject);
                    return;
                }

                // 撞到非目标（墙/柱子/地面）：消失，不然会飞穿墙
                _bouncesOffWalls++;
                Destroy(gameObject);
                return;
            }

            transform.position = next;
            _life += Time.deltaTime;
            if (_life >= lifetime) Destroy(gameObject);
        }
    }
}
