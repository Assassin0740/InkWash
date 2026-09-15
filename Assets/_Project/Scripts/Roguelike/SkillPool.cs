using System;
using System.Collections.Generic;
using UnityEngine;

namespace InkWash.Roguelike
{
    /// <summary>
    /// 加权随机抽取池。一次抽 n 个**互不重复**的候选。
    ///
    /// 用了 A-Res 算法（Efraimidis &amp; Spirakis, 2006）：给每个候选生成 key = u^(1/w)
    /// （u 为 (0,1] 均匀随机），取 key 最大的 n 个。
    ///
    /// 为什么不用更直观的「按权重轮盘赌反复抽、命中重复的就重抽」：
    ///   轮盘赌在**候选数接近 n** 时会退化 —— 剩 1 个名额而只剩 2 个候选时，
    ///   每次重抽仍有约 1/2 概率撞上已抽中的，期望重抽次数发散（最坏情况无上界）。
    ///   三选一恰好经常走到"池子就剩 3 条"的情形。A-Res 一次 O(m) 遍历出结果，
    ///   且抽出的组合分布与轮盘赌一致，验收才敢对**分布**下断言（而不是只看"能抽出东西"）。
    ///
    /// 随机源外部注入（<see cref="System.Random"/>）：固定种子即可让整套抽取序列可复现，
    /// 这是"技能池权重"能被自动化断言的前提。
    /// </summary>
    public static class SkillPool
    {
        /// <summary>一次抽取的诊断记录，验收报告直接打这个。</summary>
        public class DrawResult
        {
            public List<SkillData> picked = new List<SkillData>();
            public int candidateCount;
            public float totalWeight;
            /// <summary>本次每个候选取到的 key（越大越可能被选中），排查"某个技能怎么都抽不到"时看它。</summary>
            public List<string> keys = new List<string>();
        }

        /// <summary>
        /// 抽 <paramref name="count"/> 个候选。会排除：已达 <c>maxStacks</c> 的技能、<paramref name="exclude"/> 里的技能。
        /// 候选不足时**返回尽可能多的**（不报错）—— 池子只够 2 条就给 2 条，这是玩法可接受的降级，
        /// 而抛异常会让一局游戏卡在奖励界面。
        /// </summary>
        public static DrawResult Draw(IList<SkillData> all, SkillInventory inventory,
                                      int count, System.Random rng, IList<SkillData> exclude = null)
        {
            var res = new DrawResult();
            if (all == null || count <= 0) return res;
            if (rng == null) rng = new System.Random();

            // ---- 1) 建候选表并算权重 ----
            var cand = new List<SkillData>(all.Count);
            var w = new List<float>(all.Count);
            for (int i = 0; i < all.Count; i++)
            {
                var s = all[i];
                if (s == null) continue;
                if (inventory != null && inventory.StacksOf(s) >= Mathf.Max(1, s.maxStacks)) continue;
                if (exclude != null && exclude.Contains(s)) continue;
                cand.Add(s);
                w.Add(Mathf.Max(0.0001f, s.weight) * s.RarityWeightFactor);
            }
            res.candidateCount = cand.Count;
            for (int i = 0; i < w.Count; i++) res.totalWeight += w[i];
            if (cand.Count == 0) return res;

            // ---- 2) A-Res：key = u^(1/w)，取最大的 count 个 ----
            int n = Mathf.Min(count, cand.Count);
            var keys = new double[cand.Count];
            for (int i = 0; i < cand.Count; i++)
            {
                double u = rng.NextDouble();
                if (u <= 0.0) u = double.Epsilon;          // log(0) 保护；NextDouble 理论上是 [0,1)
                keys[i] = Math.Pow(u, 1.0 / w[i]);
            }

            var idx = new int[cand.Count];
            for (int i = 0; i < idx.Length; i++) idx[i] = i;
            Array.Sort(idx, (a, b) => keys[b].CompareTo(keys[a]));   // 降序

            for (int i = 0; i < n; i++)
            {
                res.picked.Add(cand[idx[i]]);
                res.keys.Add(cand[idx[i]].displayName + "=" + keys[idx[i]].ToString("F4"));
            }
            return res;
        }

        /// <summary>
        /// 只用同一稀有度的技能做**分布自检**：抽 N 次，统计每个技能被选中的次数。
        /// 验收用它验证"权重真的起作用了"（而不是只在代码里写了个 weight 字段）。
        /// </summary>
        public static Dictionary<string, int> SampleDistribution(IList<SkillData> all, int rounds, int pickPerRound, int seed)
        {
            var tally = new Dictionary<string, int>();
            if (all == null) return tally;
            var rng = new System.Random(seed);
            for (int r = 0; r < rounds; r++)
            {
                var res = Draw(all, null, pickPerRound, rng);
                for (int i = 0; i < res.picked.Count; i++)
                {
                    var s = res.picked[i];
                    int c;
                    tally.TryGetValue(s.id, out c);
                    tally[s.id] = c + 1;
                }
            }
            return tally;
        }
    }
}
