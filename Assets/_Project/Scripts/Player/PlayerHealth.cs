using System;
using InkWash.Combat;
using InkWash.Roguelike;
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

        [Header("属性加成（Roguelike，留空自动取同物体）")]
        [Tooltip("最大生命 / 受击无敌时长从这里读。为 null 时行为与 Sprint 4 逐位一致")]
        public PlayerStats stats;

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

        /// <summary>
        /// 有效最大生命 = 基值 + 属性加成。
        /// **所有读「上限」的地方都必须走这里**（初始化、治疗封顶、弃血比例），
        /// 否则会出现"技能加了 30 点血上限，但治疗只能回到 120"这种**半生效**状态 ——
        /// 不报错、不掉血、只是数值静默地对不上，是最难查的一类问题。
        /// </summary>
        public float EffectiveMaxHealth => maxHealth + (stats != null ? stats.MaxHealthBonus : 0f);

        /// <summary>有效受击无敌时长 = 基值 + 属性加成。</summary>
        public float EffectiveInvincibleAfterHit => invincibleAfterHit + (stats != null ? stats.InvincibleBonus : 0f);

        public float HealthRatio
        {
            get
            {
                float m = EffectiveMaxHealth;
                return m > 0f ? Mathf.Clamp01(_health / m) : 0f;
            }
        }
        public int DamageTakenCount => _damageTakenCount;
        public int DamageBlockedByIFrameCount => _damageBlockedByIFrameCount;
        public float LastDamageTime => _lastDamageTime;
        public bool IsInvincibleNow => controller != null && controller.IsInvincible;

        /// <summary>受击时抛出（表现层订阅：屏幕墨染、音效）。默认没有订阅者。</summary>
        public event Action<DamageInfo> Damaged;
        /// <summary>死亡时抛出一次。</summary>
        public event Action Died;
        /// <summary>
        /// 复位到满血时抛出（表现层的"把尸体扶起来"钩子）。
        /// 死亡倒地是表现层行为（RunPresentation 订阅 <see cref="Died"/> 做倒地），
        /// 那么复活/重开就必须有对应的还原信号 —— <see cref="RunManager.ResetRunContents"/>
        /// 与验收脚本的 <see cref="ResetHealth"/> 都走这里，一条通路全覆盖。
        /// </summary>
        public event Action Revived;

        // ---- IDamageable ----
        public bool IsAlive => _health > 0f;
        public Faction Faction => Faction.Player;
        public Transform Transform => transform;

        private float _iFrameTimer;

        private void Awake()
        {
            if (controller == null) controller = GetComponent<PlayerController>();
            if (stats == null) stats = GetComponent<PlayerStats>();
            if (stats == null) stats = GetComponentInParent<PlayerStats>();
            _health = EffectiveMaxHealth;
            _lastMax = _health;
            // 敌人靠这个找玩家（不走 Find("Player") —— 改名即静默失效，本项目栽过两次）
            PlayerRef.Register(transform);
        }

        /// <summary>血上限的上一帧快照，用来在加"最大生命"技能时同步补血。</summary>
        private float _lastMax;

        private void OnEnable()
        {
            if (stats != null) stats.Changed += OnStatsChanged;
        }

        private void OnDisable()
        {
            if (stats != null) stats.Changed -= OnStatsChanged;
        }

        /// <summary>
        /// 属性变了：**加血上限时把差额补进当前血量**。
        ///
        /// 为什么不补也能"跑"：那玩家的体验是"拿了个 +30 生命的技能，血条一点没动"，
        /// 必须回满血才能看到效果 —— 而 Roguelike 里通常不会立刻回满，
        /// 于是这个技能在整局里都是隐形的。补上之后，技能一到手血条就长一截，反馈是即时的。
        /// </summary>
        private void OnStatsChanged()
        {
            float now = EffectiveMaxHealth;
            float delta = now - _lastMax;
            _lastMax = now;
            if (delta > 0f) _health = Mathf.Min(now, _health + delta);
            else if (delta < 0f) _health = Mathf.Min(_health, now);   // 上限掉了也不能超
        }

        public void ResetDiagnostics()
        {
            _damageTakenCount = 0;
            _damageBlockedByIFrameCount = 0;
            _lastDamageTime = -999f;
        }

        public void ResetHealth()
        {
            bool wasDead = _health <= 0f;
            _health = EffectiveMaxHealth;
            _lastMax = _health;
            _iFrameTimer = 0f;
            // 从死亡状态复位 ⇒ 通知表现层还原倒地（内部逐订阅者隔离，防表现层异常带崩复位）
            if (wasDead) SafeInvoke(Revived);
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
            _iFrameTimer = EffectiveInvincibleAfterHit;

            // 顿帧只给一点点 —— 玩家挨打时顿太久会像卡帧
            HitStop.Request(Mathf.Min(info.hitStop, 0.03f));

            // 玩家挨打也要见墨。强度固定 1.0 而不是按比例 —— 玩家的 maxHealth 随技能成长，
            // 按比例会让"越强越看不见自己被打"，那是反直觉的反馈衰减。
            InkWash.Effects.InkHitVfx.Spawn(info.hitPoint, info.hitDirection, 1.0f);

            // ★ 表现层可能抛异常，绝不能让它带崩玩法链路（本项目踩过：一个订阅者抛异常，
            //   排在后面的全部收不到）。所以逐个 try 包起来。
            SafeInvoke(Damaged, info);

            if (_health <= 0f) SafeInvoke(Died);
            return true;
        }

        public void Heal(float amount)
        {
            if (amount <= 0f) return;
            _health = Mathf.Min(EffectiveMaxHealth, _health + amount);
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
