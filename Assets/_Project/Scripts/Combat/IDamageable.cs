using UnityEngine;

namespace InkWash.Combat
{
    /// <summary>
    /// 「可被伤害」的统一入口。玩家、敌人、将来的可破坏物都实现它。
    /// 为什么用接口而不是「给敌人脚本加个方法」：判定体（<see cref="Hitbox"/>）不该知道
    /// 对面到底是玩家还是敌人，否则每加一种受击者就要改一次判定代码。
    /// </summary>
    public interface IDamageable
    {
        bool IsAlive { get; }
        Faction Faction { get; }
        Transform Transform { get; }

        /// <summary>返回 true 表示这次伤害真的生效了（被无敌帧/已死亡挡掉时返回 false）。</summary>
        bool TakeDamage(DamageInfo info);
    }
}
