using System;
using InkWash.Combat;
using UnityEngine;

namespace InkWash.Player
{
    /// <summary>
    /// 玩家生命与受击。刻意**独立于 <see cref="PlayerController"/>** —— 控制器已经有 760 行，
    /// 把生命值塞进去会让"移动手感"和"生存数值"这两件毫不相干的事互相牵扯。
    ///
    /// 一个重要的可验证点：<see cref="PlayerController.IsInvincible"/>（冲刺无敌帧）在这里被真正消费 ——
    /// 无敌期间受击返回 false 且**一点血都不掉**。验收里能直接断言"闪避穿过攻击不掉血"。
    /// </summary>
    public class PlayerHealth : MonoBehaviour, IDamageable
    {
        public PlayerController controller;

        [Header("数值")]
        public float maxHealth = 120f;
        [Tooltip("受击后的无敌时长（防止被多个敌人同帧连续咬死）")]
        public float invincibleAfterHit = 0.65f;

        [Header("诊断（只读）")]
        [SerializeField] private float _health;
        [SerializeField] private int _damageTakenCount;
        [SerializeField] private int _damageBlockedByIFrameCount;
        [SerializeField] private float _lastDamageTime = -999f;

        public float Health => _health;
        public float HealthRatio => maxHealth > 0f ? Mathf.Clamp01(_health / maxHealth) : 0f;
        public int DamageTakenCount => _damageTakenCount;
        public int DamageBlockedByIFrameCount => _damageBlockedByIFrameCount;
        public float LastDamageTime => _lastDamageTime;
        public bool IsInvincibleNow => controller != null && controller.IsInvincible;

        /// <summary>受击时抛出（表现层订阅：屏幕墨染、音效）。默认没有订阅者。</summary>
        public event Action<DamageInfo> Damaged;
        /// <summary>死亡时抛出一次。</summary>
        public event Action Died;

        // ---- IDamageable ----
        public bool IsAlive => _health > 0f;
        public Faction Faction => Faction.Player;
        public Transform Transform => transform;

        private float _iFrameTimer;

        private void Awake()
        {
            if (controller == null) controller = GetComponent<PlayerController>();
            _health = maxHealth;
            // 敌人靠这个找玩家（不走 Find("Player") —— 改名即静默失效，本项目栽过两次）
            PlayerRef.Register(transform);
        }

        public void ResetDiagnostics()
        {
            _damageTakenCount = 0;
            _damageBlockedByIFrameCount = 0;
            _lastDamageTime = -999f;
        }

        public void ResetHealth()
        {
            _health = maxHealth;
            _iFrameTimer = 0f;
        }

        /// <summary>
        /// 只清受击无敌计时（不动血量、不动诊断计数）。
        /// 验收脚本需要"连续两次受击"这类断言时，不能让第一次留下的 0.65 s 无敌
        /// 把第二次吃掉 —— 那测的就不是无敌帧逻辑，而是"脚本等够没等够"。
        /// </summary>
        public void ClearInvincibility()
        {
            _iFrameTimer = 0f;
        }

        private void Update()
        {
            if (_iFrameTimer > 0f) _iFrameTimer -= Time.deltaTime;
        }

        public bool TakeDamage(DamageInfo info)
        {
            if (!IsAlive) return false;

            // ① 冲刺无敌帧：完全免疫（这是"闪避有意义"的实现点）
            if (controller != null && controller.IsInvincible)
            {
                _damageBlockedByIFrameCount++;
                return false;
            }
            // ② 受击后短暂无敌：避免被围攻时一次扣光
            if (_iFrameTimer > 0f)
            {
                _damageBlockedByIFrameCount++;
                return false;
            }

            _health = Mathf.Max(0f, _health - Mathf.Max(0f, info.amount));
            _damageTakenCount++;
            _lastDamageTime = Time.time;
            _iFrameTimer = invincibleAfterHit;

            // 顿帧只给一点点 —— 玩家挨打时顿太久会像卡帧
            HitStop.Request(Mathf.Min(info.hitStop, 0.03f));

            // ★ 表现层可能抛异常，绝不能让它带崩玩法链路（本项目踩过：一个订阅者抛异常，
            //   排在后面的全部收不到）。所以逐个 try 包起来。
            SafeInvoke(Damaged, info);

            if (_health <= 0f) SafeInvoke(Died);
            return true;
        }

        public void Heal(float amount)
        {
            if (amount <= 0f) return;
            _health = Mathf.Min(maxHealth, _health + amount);
        }

        private void SafeInvoke(Action<DamageInfo> evt, DamageInfo info)
        {
            if (evt == null) return;
            foreach (var d in evt.GetInvocationList())
            {
                try { ((Action<DamageInfo>)d).Invoke(info); }
                catch (Exception e) { Debug.LogError("[PlayerHealth] 受击表现层抛异常（已隔离）：" + e); }
            }
        }

        private void SafeInvoke(Action evt)
        {
            if (evt == null) return;
            foreach (var d in evt.GetInvocationList())
            {
                try { ((Action)d).Invoke(); }
                catch (Exception e) { Debug.LogError("[PlayerHealth] 死亡表现层抛异常（已隔离）：" + e); }
            }
        }
    }
}
