using System.Collections.Generic;
using UnityEngine;

namespace InkWash.Combat
{
    /// <summary>
    /// 判定体：在「激活窗口」内每帧沿一条线段做胶囊扫描，打中对立阵营的 <see cref="IDamageable"/>。
    ///
    /// 为什么用**主动轮询**而不是 OnTriggerEnter 触发器：
    /// 1) 触发器要求「双方至少一方有 Rigidbody」，而本项目敌人是 NavMeshAgent + CapsuleCollider、
    ///    玩家是 CharacterController —— 加 Rigidbody 会引入物理穿插/重力副作用；
    /// 2) 触发器的事件时机不可控（进入瞬间 vs 离开瞬间），而**验收需要确定性**：
    ///    「第 N 帧激活、持续 T 秒、命中 M 次」必须是可复现的数字；
    /// 3) 挥砍要打一条**弧线**，触发器只能给点，轮询可以直接扫一条线段。
    ///
    /// 线段两端点在**本地空间**定义，所以判定体跟着手/武器一起动即可 —— 不需要把坐标算来算去。
    /// </summary>
    public class Hitbox : MonoBehaviour
    {
        [Header("归属")]
        public GameObject owner;
        public Faction ownerFaction = Faction.Enemy;

        [Header("判定形状（本地空间线段 + 半径；两端点相同即退化为球形）")]
        public Vector3 pointA = new Vector3(0f, 0f, 0.4f);
        public Vector3 pointB = new Vector3(0f, 0f, 1.2f);
        public float radius = 0.35f;
        public LayerMask targetMask = ~0;

        [Header("伤害参数")]
        public float damage = 12f;
        public float knockback = 4f;
        public float hitStun = 0.3f;
        public float hitStop = 0.06f;
        [Tooltip("一次激活窗口内同一目标只吃一次伤害（防止横扫在同一个敌人身上刷帧）")]
        public bool oncePerTargetInWindow = true;

        [Header("诊断（只读）")]
        [SerializeField] private int _activationCount;
        [SerializeField] private int _hitCount;

        public int ActivationCount => _activationCount;
        public int HitCount => _hitCount;
        public bool IsActive => _timer > 0f;
        public float RemainingTime => Mathf.Max(0f, _timer);

        private float _timer;
        private readonly Collider[] _buf = new Collider[48];
        private readonly HashSet<IDamageable> _alreadyHit = new HashSet<IDamageable>();

        public void ResetDiagnostics() { _activationCount = 0; _hitCount = 0; }

        /// <summary>开窗。多次调用取"更晚结束"的那次，避免连击互相截断窗口。</summary>
        public void Activate(float duration)
        {
            _timer = Mathf.Max(_timer, duration);
            _activationCount++;
            _alreadyHit.Clear();
        }

        public void Deactivate()
        {
            _timer = 0f;
            _alreadyHit.Clear();
        }

        private void Update()
        {
            if (_timer <= 0f) return;
            _timer -= Time.deltaTime;

            Vector3 a = transform.TransformPoint(pointA);
            Vector3 b = transform.TransformPoint(pointB);
            int n = Physics.OverlapCapsuleNonAlloc(a, b, radius, _buf, targetMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var col = _buf[i];
                if (col == null) continue;
                if (owner != null && (col.transform == owner.transform || col.transform.IsChildOf(owner.transform))) continue;

                var target = col.GetComponentInParent<IDamageable>();
                if (target == null || !target.IsAlive) continue;
                if (target.Faction == ownerFaction) continue;                // 不打自己人
                if (oncePerTargetInWindow && _alreadyHit.Contains(target)) continue;

                _alreadyHit.Add(target);
                Apply(target, col, (a + b) * 0.5f);
            }
        }

        private void Apply(IDamageable target, Collider col, Vector3 mid)
        {
            Vector3 point = mid;
            var cc = col as CharacterController;
            if (cc != null) point = cc.ClosestPoint(mid);
            else if (!col.isTrigger) point = col.ClosestPoint(mid);

            Vector3 dir = target.Transform != null ? target.Transform.position - mid : Vector3.zero;
            dir.y = 0f;
            if (dir.sqrMagnitude < 1e-6f) dir = transform.forward;
            dir.Normalize();

            var info = new DamageInfo
            {
                amount = damage,
                hitPoint = point,
                hitDirection = dir,
                knockback = knockback,
                hitStun = hitStun,
                hitStop = hitStop,
                sourceFaction = ownerFaction,
                source = owner,
            };

            if (target.TakeDamage(info)) _hitCount++;
        }
    }
}
