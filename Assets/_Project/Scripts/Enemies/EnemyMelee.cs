using UnityEngine;

namespace InkWash.Enemies
{
    /// <summary>
    /// 墨徒（近战）：一击即走的三段式 —— 发现你、贴上来、劈一刀、退开再劈。
    /// 行为几乎全部继承自 <see cref="EnemyBase"/>，只有"想站多近"和"劈多快"是它的个性。
    /// 差异化靠**参数**而不是靠**新代码分支**，是因为参数可以进表格做数值对比（论文要用）。
    /// </summary>
    public class EnemyMelee : EnemyBase
    {
        [Header("墨徒·个性")]
        [Tooltip("往前多压一点，避免刚好卡在攻击距离外沿反复起手")]
        public float pressFactor = 0.75f;
        [Tooltip("每次起手前摇的随机浮动（避免一群敌人同帧同步挥刀）")]
        public float windupJitter = 0.08f;

        protected override float PreferredRange => attackRange * pressFactor;

        protected override void ChooseAttack()
        {
            _useSecondAttack = false;
            // 同批生成的敌人错开节奏，观感上"各自为战"而不是"齐步走"
            if (windupJitter > 0f)
                attackWindup = Mathf.Max(0.08f, _baseWindup + Random.Range(-windupJitter, windupJitter));
        }

        private float _baseWindup = -1f;

        protected override void Awake()
        {
            base.Awake();
            _baseWindup = attackWindup;
        }
    }
}
