using UnityEngine;

namespace InkWash.Combat
{
    /// <summary>阵营。判定只看「阵营是否相同」，不看具体是谁。</summary>
    public enum Faction
    {
        Player = 0,
        Enemy = 1,
        Neutral = 2,
    }

    /// <summary>
    /// 一次伤害的完整描述。用 struct 是为了避免每次命中都产生 GC（战斗里每帧可能几十次）。
    /// 设计要点：把「伤害」和「受击表现」放在同一条数据里 —— 否则每个受击方都要自己去猜
    /// 该硬直多久、该被击退多远，最后必然各处不一致。
    /// </summary>
    public struct DamageInfo
    {
        public float amount;          // 伤害值
        public Vector3 hitPoint;      // 命中点（世界坐标，用于溅墨/音效定位）
        public Vector3 hitDirection;  // 击退方向（单位向量，指向受击者被推走的方向）
        public float knockback;       // 击退速度（m/s，0 = 不击退）
        public float hitStun;         // 硬直时长（s，0 = 不硬直）
        public float hitStop;         // 顿帧时长（s，0 = 不顿帧）
        public Faction sourceFaction; // 施加者的阵营
        public GameObject source;     // 施加者（可空）

        public static DamageInfo Simple(float amount, Faction from, GameObject src = null)
        {
            return new DamageInfo
            {
                amount = amount,
                sourceFaction = from,
                source = src,
                hitDirection = Vector3.zero,
            };
        }
    }
}
