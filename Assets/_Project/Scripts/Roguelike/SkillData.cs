using UnityEngine;

namespace InkWash.Roguelike
{
    /// <summary>稀有度。只影响**权重倍数**与显示颜色，不直接改数值 —— 数值由 <see cref="SkillData.valuePerStack"/> 决定。
    /// 分开的道理：稀有度是"抽到的概率"，强度是"抽到后有多强"，把两者揉在同一个字段里，
    /// 后面想把某个技能调强就会发现"顺带也变容易抽到了"。</summary>
    public enum SkillRarity
    {
        Common = 0,
        Rare = 1,
        Epic = 2,
    }

    /// <summary>
    /// 一条技能的数据。刻意做成 ScriptableObject 而不是代码里的常量表：
    /// 论文里要展示"技能池可配置"，而且新增技能不应该需要改一行代码、重编译一次。
    ///
    /// 设计上的一条硬规矩：**数值只有一种表达方式** —— `stat` + `valuePerStack`。
    /// 不做"这个技能比较特殊所以单独写一段逻辑"（那种技能一旦有第二个，验收就没法统一断言）。
    /// 三重叠加的复杂度留给后续版本，本 Demo 的范围是"数值可叠加成长"。
    /// </summary>
    [CreateAssetMenu(menuName = "InkWash/技能", fileName = "Skill_")]
    public class SkillData : ScriptableObject
    {
        [Header("身份")]
        [Tooltip("稳定标识。**不要用 displayName 当键** —— 改名就断档（本项目在 PlayerRef 上栽过一次）")]
        public string id = "skill_new";

        public string displayName = "新技能";

        [TextArea(2, 4)]
        [Tooltip("描述模板。{v} 会被替换成「每层数值」，{t} 会替换成「当前层数下的总数值」")]
        public string description = "伤害提升 {v}%";

        [Header("抽取")]
        public SkillRarity rarity = SkillRarity.Common;
        [Tooltip("权重。同稀有度内相对比较；稀有度会再乘一个倍数")]
        public float weight = 1f;
        [Tooltip("最多叠几层。到上限后不再进候选池")]
        public int maxStacks = 5;

        [Header("效果（唯一的数值出口）")]
        public StatKind stat = StatKind.DamageBonus;
        [Tooltip("每层投递多少。百分数类属性直接写 0.20 表示 +20%")]
        public float valuePerStack = 0.2f;
        [Tooltip("描述里按百分比显示（勾上则 0.2 显示为 20%）")]
        public bool showAsPercent = true;

        [Header("流派标签（诊断/后续加权用）")]
        public string[] tags = new string[0];

        /// <summary>稀有度的权重倍数。Epic 更稀有 —— 倍数在池子里统一乘，不散落到各个资产上。</summary>
        public float RarityWeightFactor
        {
            get
            {
                switch (rarity)
                {
                    case SkillRarity.Epic: return 0.28f;
                    case SkillRarity.Rare: return 0.65f;
                    default: return 1f;
                }
            }
        }

        /// <summary>把 0.2 显示成 "20%"、把 3 显示成 "3"。</summary>
        public string FormatValue(float v)
        {
            return showAsPercent ? (v * 100f).ToString("0.#") + "%" : v.ToString("0.#");
        }

        /// <summary>
        /// 生成带层数上下文的描述。**候选卡上必须显示"再叠一层会变成多少"**，
        /// 否则玩家看到"伤害 +20%"会以为是又加 20%，而实际是 20%→40%，是两种完全不同的决策。
        /// </summary>
        public string BuildDescription(int currentStacks)
        {
            float per = valuePerStack;
            float total = valuePerStack * (currentStacks + 1);
            return description
                .Replace("{v}", FormatValue(per))
                .Replace("{t}", FormatValue(total))
                .Replace("{n}", (currentStacks + 1).ToString());
        }

        public string RarityName
        {
            get
            {
                switch (rarity)
                {
                    case SkillRarity.Epic: return "史诗";
                    case SkillRarity.Rare: return "稀有";
                    default: return "普通";
                }
            }
        }

        public Color RarityColor
        {
            get
            {
                switch (rarity)
                {
                    case SkillRarity.Epic: return new Color(0.85f, 0.55f, 0.20f);  // 赭石
                    case SkillRarity.Rare: return new Color(0.36f, 0.55f, 0.62f);  // 石青
                    default: return new Color(0.42f, 0.42f, 0.40f);                // 淡墨
                }
            }
        }
    }
}
