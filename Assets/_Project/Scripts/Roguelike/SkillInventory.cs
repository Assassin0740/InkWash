using System;
using System.Collections.Generic;
using UnityEngine;

namespace InkWash.Roguelike
{
    /// <summary>
    /// 技能背包：记住"已经拿过哪些技能、各几层"，并把效果**投递**给 <see cref="PlayerStats"/>。
    ///
    /// 为什么要独立于 PlayerStats：两者回答的是不同问题 ——
    ///   PlayerStats 回答"我现在伤害倍率是多少"（聚合后的结果，消费方只读）；
    ///   SkillInventory 回答"这个倍率是怎么来的"（技能名 + 层数，UI 与论文截图要展示的就是它）。
    /// 把层数塞进 PlayerStats 会让"数值"和"成长史"互相污染 —— 比如重置数值时该不该清空技能列表？
    /// 分开之后这个问题的答案是显然的。
    ///
    /// 键用 `SkillData.id`（字符串）而不是对象引用：ScriptableObject 资产在重新导入后
    /// 对象引用可能变化，而 id 是写在资产里的稳定标识。
    /// </summary>
    [DisallowMultipleComponent]
    public class SkillInventory : MonoBehaviour
    {
        [Tooltip("数值出口。留空自动取同物体上的 PlayerStats")]
        public PlayerStats stats;

        [Header("诊断（只读）")]
        [SerializeField] private int _acquireCount;
        [SerializeField] private int _distinctCount;

        /// <summary>拿到一个技能时抛一次（参数：技能、拿完之后的层数）。UI / 音效订阅它。</summary>
        public event Action<SkillData, int> Acquired;

        private readonly List<SkillData> _owned = new List<SkillData>();
        private readonly Dictionary<string, int> _stacks = new Dictionary<string, int>();

        public int AcquireCount => _acquireCount;
        public int DistinctCount => _distinctCount;
        public IReadOnlyList<SkillData> Owned => _owned;

        private void Awake()
        {
            if (stats == null) stats = GetComponent<PlayerStats>();
        }

        public int StacksOf(SkillData s)
        {
            if (s == null) return 0;
            int n;
            return _stacks.TryGetValue(s.id, out n) ? n : 0;
        }

        /// <summary>
        /// 拿一个技能：层数 +1，并把**这一层**的数值投递给 PlayerStats。
        ///
        /// 投递的是 valuePerStack 而不是"总值"：PlayerStats 是加法累加池，
        /// 每次只投增量，层数语义自然成立；若投总值，第二次就会重复计入第一层。
        /// </summary>
        public void Acquire(SkillData s)
        {
            if (s == null) return;

            int n;
            _stacks.TryGetValue(s.id, out n);
            n++;
            _stacks[s.id] = n;
            _acquireCount++;

            if (n == 1) { _owned.Add(s); _distinctCount = _owned.Count; }

            if (stats != null) stats.Add(s.stat, s.valuePerStack, s.displayName);

            if (Acquired != null)
            {
                var handlers = Acquired.GetInvocationList();
                for (int i = 0; i < handlers.Length; i++)
                {
                    try { ((Action<SkillData, int>)handlers[i])(s, n); }
                    catch (Exception e) { Debug.LogError("[SkillInventory] Acquired 订阅者抛异常（已隔离）：" + e); }
                }
            }
        }

        /// <summary>把背包清空。**同时**清 PlayerStats —— 只清一边会留下"没有技能却有加成"的鬼状态。</summary>
        public void ResetAll()
        {
            _owned.Clear();
            _stacks.Clear();
            _acquireCount = 0;
            _distinctCount = 0;
            if (stats != null) stats.ResetAll();
        }

        /// <summary>一行式摘要，验收报告直接打。</summary>
        public string Describe()
        {
            if (_owned.Count == 0) return "（未获得任何技能）";
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < _owned.Count; i++)
            {
                if (i > 0) sb.Append("、");
                sb.Append(_owned[i].displayName).Append("×").Append(StacksOf(_owned[i]));
            }
            return sb.ToString();
        }
    }
}
