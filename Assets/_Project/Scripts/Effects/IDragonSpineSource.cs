using UnityEngine;

namespace InkWash.Effects
{
    /// <summary>
    /// 墨龙风暴特效需要的**最小几何接口** —— 只给"沿链取点"，不给招式/状态/血量。
    ///
    /// ★ 为什么是接口，而不是让特效管理器直接持有 `EnemyDragon`：
    ///   · 依赖方向必须是 **玩法 → 表现**（见 `Docs/墨龙特效设计.md` §四）。
    ///     一旦特效层认识 `EnemyDragon`，以后给别的 Boss 复用就得改特效代码；
    ///   · 接口调用**不产生 GC**（class 实现接口只是引用转换，不装箱），
    ///     而 `Func&lt;int, Vector3&gt;` 这类委托写起来顺手、闭包一不留神就每帧分配 ——
    ///     验收判据里有"稳态 0 B/帧"这一条，用接口把这条不确定性直接关掉。
    ///
    /// ★ 方向约定**沿用 `EnemyDragon` 既有约定**：**i 小 = 尾/根侧，i 大 = 头侧**。
    ///   （`_spine[0]` 的祖先链上挂着尾尖 `drgon_0226`，见 EnemyDragon.cs 的 ResolveSpine 注释。）
    ///   这个约定**不能由实现方自行更改** —— 电弧"从头往尾游走"的方向感全靠它。
    /// </summary>
    public interface IDragonSpineSource
    {
        /// <summary>链节数。**0 是合法值**（骨链未就绪），特效层必须能忍。</summary>
        int SpineCount { get; }

        /// <summary>第 i 节的世界位置。越界自动夹取；链空时返回 <c>transform.position</c>。</summary>
        Vector3 GetSpinePosition(int i);

        /// <summary>第 i 节"沿链前进"的单位方向（相邻节点差分；末节沿用前一节）。</summary>
        Vector3 GetSpineDirection(int i);

        /// <summary>口部位置（喷息 / 喷烟的发射点）。</summary>
        Vector3 GetMouthPosition();

        /// <summary>口部朝向（喷息主方向）。</summary>
        Vector3 GetMouthForward();
    }
}
