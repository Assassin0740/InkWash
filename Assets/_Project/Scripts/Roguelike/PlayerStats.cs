using System;
using System.Collections.Generic;
using UnityEngine;

namespace InkWash.Roguelike
{
    /// <summary>
    /// 可叠加的属性种类。**每一种都必须有一个真实的消费点**——否则技能描述上写着"伤害 +20%"，
    /// 而代码里根本没人读，验收时量出来"数值没变"会让人以为是抽取/叠加坏了，
    /// 实际是这个属性从头到尾没接线。加新枚举值时必须同时把消费点写进 <see cref="PlayerStats"/> 的注释表。
    /// </summary>
    public enum StatKind
    {
        /// <summary>伤害 +%（消费点：PlayerSwordHitbox.OnHitMoment）</summary>
        DamageBonus = 0,
        /// <summary>移速 +%（消费点：PlayerController 的速度钳位）</summary>
        MoveSpeedBonus = 1,
        /// <summary>最大生命 +值（消费点：PlayerHealth.maxHealth）</summary>
        MaxHealthBonus = 2,
        /// <summary>暴击率 +绝对值 0..1（消费点：PlayerSwordHitbox.OnHitMoment）</summary>
        CritChance = 3,
        /// <summary>暴击伤害 +绝对倍率，如 0.5 = 由 150% 提到 200%（消费点同上）</summary>
        CritDamage = 4,
        /// <summary>攻速 +%（消费点：PlayerController 的三段挥砍时长）</summary>
        AttackSpeedBonus = 5,
        /// <summary>吸血 0..1（消费点：PlayerSwordHitbox 命中后回血）</summary>
        Lifesteal = 6,
        /// <summary>冲刺冷却 -%（消费点：PlayerController.dashCooldown）</summary>
        DashCooldownCut = 7,
        /// <summary>受击无敌 +秒（消费点：PlayerHealth.invincibleAfterHit）</summary>
        InvincibleBonus = 8,
        /// <summary>击退 +%（消费点：PlayerSwordHitbox.OnHitMoment）</summary>
        KnockbackBonus = 9,
        /// <summary>击杀回血 每杀 +点（消费点：LevelSystem 的击杀回调）</summary>
        KillHeal = 10,
    }

    /// <summary>
    /// 玩家属性池：所有"越打越强"的来源都往这里投递**加法修饰器**，消费方只读这里的聚合结果。
    ///
    /// 为什么是**加法累加**而不是**连乘**（这是刻意选的，不是偷懒）：
    ///   1. 连乘顺序敏感且会爆炸 —— 三个"伤害 +20%"连乘是 1.73 倍，四个就是 2.07 倍，
    ///      数值曲线在第四个技能处直接失控；加法累加则永远是 1 + 0.2n，策划算得出来、验收也量得出来。
    ///   2. 连乘的**可验证性差**：同样两个技能，先加哪个会得到不同结果，验收就得规定顺序；
    ///      加法与顺序无关，断言可以写成"装上这两个技能后伤害倍率应当 = 1.40"。
    ///   3. 本项目所有加成都来自**同一类来源**（技能层数），本来就没有"乘区"要区分。
    ///
    /// 与既有代码的关系：`PlayerController`/`PlayerHealth`/`PlayerSwordHitbox` 里原本的
    /// `walkSpeed`/`runSpeed`/`maxHealth`/`damage[]` 全部**保留为基值**，
    /// 倍率为 1（没装任何技能）时行为与 Sprint 4 之前**逐位一致** —— 这一点由验收脚本断言。
    /// </summary>
    [DisallowMultipleComponent]
    public class PlayerStats : MonoBehaviour
    {
        /// <summary>一条修饰器记录。保留 source 是为了能回答"这 40% 伤害是谁给的"。</summary>
        public struct Modifier
        {
            public StatKind stat;
            public float value;
            public string source;
        }

        [Header("基值（不装任何技能时的数值 = 角色底子）")]
        [Tooltip("暴击基础倍率。150% 是动作游戏的常见起点")]
        public float baseCritMultiplier = 1.5f;

        [Header("上限（防止叠加把数值曲线拉爆）")]
        public float maxDamageBonus = 3f;        // 伤害最多 ×4
        public float maxMoveSpeedBonus = 1f;     // 移速最多 ×2
        public float maxAttackSpeedBonus = 1.5f; // 攻速最多 ×2.5
        public float minDashCooldownScale = 0.35f;

        [Header("随机（暴击 roll）")]
        [Tooltip("固定种子 → 序列可复现。验收脚本靠它把「暴击率 30%」变成可断言的数字")]
        public int randomSeed = 20260915;

        [Header("诊断（只读）")]
        [SerializeField] private int _appliedCount;
        [SerializeField] private int _critRolls;
        [SerializeField] private int _critSuccesses;
        [SerializeField] private float _lastRawDamage;
        [SerializeField] private float _lastFinalDamage;

        /// <summary>投递过修饰器就抛一次（UI 刷新用）。</summary>
        public event Action Changed;

        private readonly List<Modifier> _mods = new List<Modifier>();
        private readonly float[] _sum = new float[11];
        private System.Random _rng;

        // ---- 只读诊断 ----
        public int AppliedCount => _appliedCount;
        public int ModifierCount => _mods.Count;
        public int CritRolls => _critRolls;
        public int CritSuccesses => _critSuccesses;
        /// <summary>上一刀「未暴击时的原始伤害」与「最终伤害」，验收用它算实测倍率。</summary>
        public float LastRawDamage => _lastRawDamage;
        public float LastFinalDamage => _lastFinalDamage;
        public IReadOnlyList<Modifier> Modifiers => _mods;

        // ---- 聚合结果（消费方只读这些）----
        public float DamageMultiplier => 1f + Mathf.Min(Sum(StatKind.DamageBonus), maxDamageBonus);
        public float MoveSpeedMultiplier => 1f + Mathf.Min(Sum(StatKind.MoveSpeedBonus), maxMoveSpeedBonus);
        public float MaxHealthBonus => Sum(StatKind.MaxHealthBonus);
        public float CritChance => Mathf.Clamp01(Sum(StatKind.CritChance));
        public float CritMultiplier => baseCritMultiplier + Sum(StatKind.CritDamage);
        /// <summary>攻速倍率：>1 表示更快。挥砍时长要**除以**它。</summary>
        public float AttackSpeedMultiplier => 1f + Mathf.Min(Sum(StatKind.AttackSpeedBonus), maxAttackSpeedBonus);
        public float Lifesteal => Mathf.Clamp01(Sum(StatKind.Lifesteal));
        public float DashCooldownScale =>
            Mathf.Clamp(1f - Sum(StatKind.DashCooldownCut), minDashCooldownScale, 1f);
        public float InvincibleBonus => Mathf.Max(0f, Sum(StatKind.InvincibleBonus));
        public float KnockbackMultiplier => 1f + Mathf.Max(0f, Sum(StatKind.KnockbackBonus));
        public float KillHealPerKill => Mathf.Max(0f, Sum(StatKind.KillHeal));

        private void Awake()
        {
            _rng = new System.Random(randomSeed);
        }

        /// <summary>投递一条加成。<paramref name="source"/> 只用于诊断展示。</summary>
        public void Add(StatKind stat, float value, string source = null)
        {
            if (Mathf.Abs(value) < 1e-6f) return;
            int i = (int)stat;
            if (i < 0 || i >= _sum.Length) return;

            _sum[i] += value;
            _mods.Add(new Modifier { stat = stat, value = value, source = source });
            _appliedCount++;

            // ★ 表现层（面板刷新）可能抛异常，绝不能带崩"技能生效"这条玩法链路
            //   —— 本项目在 SwingStarted 多播上栽过一次，约定见 Docs/美术风格规范.md
            if (Changed != null)
            {
                var handlers = Changed.GetInvocationList();
                for (int h = 0; h < handlers.Length; h++)
                {
                    try { ((Action)handlers[h])(); }
                    catch (Exception e) { Debug.LogError("[PlayerStats] Changed 订阅者抛异常（已隔离）：" + e); }
                }
            }
        }

        public float Sum(StatKind stat)
        {
            int i = (int)stat;
            return (i >= 0 && i < _sum.Length) ? _sum[i] : 0f;
        }

        public int StacksOf(StatKind stat)
        {
            int n = 0;
            for (int i = 0; i < _mods.Count; i++) if (_mods[i].stat == stat) n++;
            return n;
        }

        /// <summary>清空所有加成（重开一局 / 验收复位用）。</summary>
        public void ResetAll()
        {
            _mods.Clear();
            Array.Clear(_sum, 0, _sum.Length);
            _appliedCount = 0;
            _critRolls = 0;
            _critSuccesses = 0;
            _lastRawDamage = 0f;
            _lastFinalDamage = 0f;
            _rng = new System.Random(randomSeed);
        }

        /// <summary>固定随机序列（验收想让"暴击必中/必不中"时改它）。</summary>
        public void SetSeed(int seed)
        {
            randomSeed = seed;
            _rng = new System.Random(seed);
        }

        /// <summary>把下次必定暴击 / 必定不暴击（验收断言暴击倍数时用，避免概率把结论搅成"有时候通过"）。</summary>
        public void ForceNextCrit(bool crit) { _forceCrit = crit ? 1 : -1; }
        private int _forceCrit;

        /// <summary>
        /// 结算一次伤害：先按倍率放大，再判暴击，最后把两个数字都记进诊断。
        ///
        /// 为什么把 roll 放在这里而不是让 <see cref="PlayerSwordHitbox"/> 自己 `Random.value`：
        /// 验收要断言"暴击率 30% 时，200 次命中大约 60 次暴击"。随机数散在各处就没法固定种子，
        /// 断言会变成"有时候通过"，那是尺子的问题、不是玩法的问题。
        /// </summary>
        public float RollDamage(float baseDamage, out bool crit)
        {
            if (_rng == null) _rng = new System.Random(randomSeed);

            float scaled = Mathf.Max(0f, baseDamage) * DamageMultiplier;
            _lastRawDamage = scaled;

            crit = false;
            if (_forceCrit != 0)
            {
                crit = _forceCrit > 0;
                _forceCrit = 0;
            }
            else if (CritChance > 0f)
            {
                crit = _rng.NextDouble() < CritChance;
            }

            _critRolls++;
            float final = crit ? scaled * CritMultiplier : scaled;
            if (crit) _critSuccesses++;
            _lastFinalDamage = final;
            return final;
        }

        public void ResetDiagnostics()
        {
            _critRolls = 0;
            _critSuccesses = 0;
            _lastRawDamage = 0f;
            _lastFinalDamage = 0f;
        }

        /// <summary>一行式摘要，验收报告里直接打这行。</summary>
        public string Describe()
        {
            return "伤害×" + DamageMultiplier.ToString("F2")
                 + "  移速×" + MoveSpeedMultiplier.ToString("F2")
                 + "  生命+" + MaxHealthBonus.ToString("F0")
                 + "  暴击 " + (CritChance * 100f).ToString("F0") + "%/" + CritMultiplier.ToString("F2")
                 + "  攻速×" + AttackSpeedMultiplier.ToString("F2")
                 + "  吸血 " + (Lifesteal * 100f).ToString("F0") + "%"
                 + "  冲刺CD×" + DashCooldownScale.ToString("F2")
                 + "  无敌+" + InvincibleBonus.ToString("F2") + "s";
        }
    }
}
