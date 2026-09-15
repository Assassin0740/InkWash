using System;
using UnityEngine;
using InkWash.Enemies;

namespace InkWash.Roguelike
{
    /// <summary>
    /// 经验与升级：击杀敌人攒经验，满级即产出一张"三选一"门票。
    ///
    /// 一个刻意的设计：升级只**排队**（<see cref="PendingLevelUps"/>），不直接弹 UI。
    /// 原因是"谁来决定什么时候弹窗"必须只有一个地方 —— 若是 LevelSystem 自己弹，
    /// 就会出现"玩家正在被三只墨徒围攻时突然弹出选择界面"这种时序问题，
    /// 而且验收脚本没法控制弹窗时机。排队后由 <see cref="RunManager"/> 在安全的时刻消费。
    ///
    /// 击杀信号走 <see cref="EnemyBase.AnyDied"/>（静态事件）：敌人是**运行时生成**的，
    /// 逐个订阅需要在 WaveSpawner 的生成回调里挂钩子，而敌人自毁后钩子还得清理；
    /// 静态事件一次订阅管全场，代价只是要记得在 <c>SubsystemRegistration</c> 复位（已做）。
    /// </summary>
    [DisallowMultipleComponent]
    public class LevelSystem : MonoBehaviour
    {
        [Tooltip("读取经验加成 / 击杀回血等属性；留空自动取同物体")]
        public PlayerStats stats;

        [Header("经验曲线")]
        [Tooltip("升到 2 级需要多少经验")]
        public int baseXp = 20;
        [Tooltip("每级递增多少（线性曲线：够简单，验收能直接算）")]
        public float growthPerLevel = 14f;

        [Header("诊断（只读）")]
        [SerializeField] private int _level = 1;
        [SerializeField] private int _xp;
        [SerializeField] private int _xpToNext;
        [SerializeField] private int _pendingLevelUps;
        [SerializeField] private int _totalXpGained;
        [SerializeField] private int _killCount;
        [SerializeField] private int _lastXpGrant;

        /// <summary>升一级抛一次（参数：新等级）。</summary>
        public event Action<int> LeveledUp;

        public int Level => _level;
        public int Xp => _xp;
        public int XpToNext => _xpToNext;
        public int PendingLevelUps => _pendingLevelUps;
        public int TotalXpGained => _totalXpGained;
        public int KillCount => _killCount;
        public int LastXpGrant => _lastXpGrant;
        public float XpRatio => _xpToNext > 0 ? Mathf.Clamp01((float)_xp / _xpToNext) : 0f;

        private void Awake()
        {
            if (stats == null) stats = GetComponent<PlayerStats>();
            _xpToNext = CostFor(_level);
        }

        private void OnEnable() { EnemyBase.AnyDied += OnEnemyDied; }
        private void OnDisable() { EnemyBase.AnyDied -= OnEnemyDied; }

        /// <summary>从 level 升到 level+1 所需经验。线性曲线刻意选简单 —— 论文里要能一眼算出来。</summary>
        public int CostFor(int level) => Mathf.Max(1, Mathf.RoundToInt(baseXp + growthPerLevel * (level - 1)));

        private void OnEnemyDied(EnemyBase e)
        {
            _killCount++;
            int gain = e != null ? Mathf.Max(1, e.xpReward) : 1;
            GrantXp(gain);

            // 击杀回血（属性池里 KillHeal 的消费点）
            if (stats != null && stats.KillHealPerKill > 0f)
            {
                var hp = GetComponent<InkWash.Player.PlayerHealth>();
                if (hp != null) hp.Heal(stats.KillHealPerKill);
            }
        }

        public void GrantXp(int amount)
        {
            if (amount <= 0) return;
            _lastXpGrant = amount;
            _xp += amount;
            _totalXpGained += amount;

            // while 而不是 if：一次拿到大量经验（例如一刀清了 5 只）时要连升多级，
            // 且必须**真的一次只升一级地算**，不能"补一次差值"——否则经验曲线的斜率信息就丢了。
            while (_xp >= _xpToNext)
            {
                _xp -= _xpToNext;
                _level++;
                _xpToNext = CostFor(_level);
                _pendingLevelUps++;

                if (LeveledUp != null)
                {
                    var handlers = LeveledUp.GetInvocationList();
                    for (int i = 0; i < handlers.Length; i++)
                    {
                        try { ((Action<int>)handlers[i])(_level); }
                        catch (Exception e) { Debug.LogError("[LevelSystem] LeveledUp 订阅者抛异常（已隔离）：" + e); }
                    }
                }
            }
        }

        /// <summary>取走一张升级门票。返回 false = 当前没有待处理的升级。</summary>
        public bool ConsumePendingLevelUp()
        {
            if (_pendingLevelUps <= 0) return false;
            _pendingLevelUps--;
            return true;
        }

        public void ResetAll()
        {
            _level = 1;
            _xp = 0;
            _xpToNext = CostFor(1);
            _pendingLevelUps = 0;
            _totalXpGained = 0;
            _killCount = 0;
            _lastXpGrant = 0;
        }

        public string Describe()
        {
            return "Lv." + _level + "  经验 " + _xp + "/" + _xpToNext
                 + "  待处理升级 " + _pendingLevelUps
                 + "  累计经验 " + _totalXpGained + "  击杀 " + _killCount;
        }
    }
}
