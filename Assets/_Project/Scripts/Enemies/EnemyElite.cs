using InkWash.Combat;
using UnityEngine;

namespace InkWash.Enemies
{
    /// <summary>
    /// 墨魇（精英）：一套 1-2 的重击 —— 第一次原地范围横扫，第二次带**冲锋**砸击。
    /// 它比小怪多一件东西：**可弹反窗口**。
    ///
    /// 弹反的实现刻意做得很小：前摇期间受击 ⇒ 减伤 + 超长硬直。
    /// 为什么不做"反弹伤害/反弹动画"：那是全新的战斗子系统（要判定朝向、要反击动作、要无敌帧联动），
    /// 在 Demo 范围内收益远小于成本。而"减伤 + 长硬直"已经完整表达了机制内核
    /// —— 玩家的正确决策（在前摇时打它）能换来一个明确的、可被验收测量的收益窗口
    /// （<see cref="ParriedCount"/> 与 <see cref="StunMultiplier"/> 都是可断言的数字）。
    /// </summary>
    public class EnemyElite : EnemyBase
    {
        [Header("墨魇·个性")]
        [Tooltip("冲锋速度")]
        public float chargeSpeed = 8.5f;
        [Tooltip("冲锋最大时长（防止冲出场地）")]
        public float chargeMaxTime = 0.55f;

        [Header("弹反")]
        [Tooltip("弹反窗口 = 攻击前摇的前 N 秒")]
        public float parryWindow = 0.4f;
        [Tooltip("被弹反时的减伤倍率")]
        [Range(0.05f, 1f)] public float parryDamageScale = 0.35f;
        [Tooltip("被弹反时的硬直倍率（这是玩家的收益）")]
        public float parryStunScale = 3.2f;

        [Header("诊断（只读）")]
        [SerializeField] private int _parriedCount;
        [SerializeField] private int _chargeCount;
        [SerializeField] private bool _parryWindowOpen;

        public int ParriedCount => _parriedCount;
        public int ChargeCount => _chargeCount;
        public bool ParryWindowOpen => _parryWindowOpen;

        private bool _charging;

        /// <summary>弹反窗口：攻击前摇的前 <see cref="parryWindow"/> 秒内。</summary>
        private void UpdateParryWindow()
        {
            _parryWindowOpen = _state == EnemyState.Attack
                               && !_useSecondAttack
                               && _stateTime <= parryWindow;
        }

        protected override void Update()
        {
            base.Update();
            UpdateParryWindow();
        }

        protected override void ChooseAttack()
        {
            // 1 → 横扫（可弹反）；2 → 冲锋砸击。交替出招，节奏可预期、也因此可被玩家学会
            _useSecondAttack = ((_attackCount % 2) == 0);
            _charging = _useSecondAttack;
            if (_charging) _chargeCount++;
        }

        protected override void AttackMovement(float t, float hitAt)
        {
            if (!_charging) return;
            // 冲锋：从前摇开始就往前压，判定结束后减速
            float end = attackWindup + attackActive;
            if (t > end || t > chargeMaxTime + attackWindup) return;

            Vector3 step = transform.forward * (chargeSpeed * Time.deltaTime);
            if (_agent != null && _agent.enabled && _agent.isOnNavMesh) _agent.Move(step);
            else transform.position += step;
        }

        protected override float DamageMultiplier(DamageInfo info)
        {
            if (_parryWindowOpen && info.sourceFaction == Faction.Player)
            {
                _parriedCount++;
                return parryDamageScale;
            }
            return 1f;
        }

        protected override float StunMultiplier(DamageInfo info)
        {
            if (_parryWindowOpen && info.sourceFaction == Faction.Player) return parryStunScale;
            return 1f;
        }

        protected override void OnDied()
        {
            // 精英死了要能一眼看出来（不做 UI，先用日志 + 计数，验收里断言）
            Debug.Log("[EnemyElite] 墨魇被击破：被弹反 " + _parriedCount + " 次，冲锋 " + _chargeCount + " 次");
        }
    }
}
