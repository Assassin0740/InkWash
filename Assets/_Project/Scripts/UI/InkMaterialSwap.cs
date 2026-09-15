using UnityEngine;

namespace InkWash.UI
{
    /// <summary>
    /// 让一个 <see cref="Renderer"/> 能在「原 PBR 材质」与「水墨材质」之间整组切换。
    ///
    /// 为什么做成**组件 + 自注册**，而不是让参数面板持有一张列表：
    /// 敌人是**运行时按波次生成**的 —— 面板在 Awake 时根本不知道后面会出现谁。
    /// 组件在自己 `OnEnable` 时登记上来，于是"第 2 波刷出来的墨徒"也自动跟着换材质，
    /// 不需要面板去轮询 `FindObjectsOfType`（那个每帧扫全场的做法在波次多时会明显掉帧）。
    ///
    /// 只在运行时登记：`OnEnable`/`OnDisable` 在**编辑态**也会被调用
    /// （例如 `PrefabUtility.LoadPrefabContents` 打开预制体时），放任它登记会在编辑期
    /// 造出一堆 `HideAndDontSave` 的幽灵材质。
    /// </summary>
    public class InkMaterialSwap : MonoBehaviour
    {
        [Tooltip("要换材质的渲染器；留空则取自身/子节点上的第一个")]
        public Renderer target;

        [Tooltip("原 PBR 材质组（与 inkMaterials 一一对应）")]
        public Material[] litMaterials;

        [Tooltip("水墨材质组（与 litMaterials 一一对应）")]
        public Material[] inkMaterials;

        private void OnEnable()
        {
            if (target == null) target = GetComponentInChildren<Renderer>();
            if (Application.isPlaying) InkStylePanel.Register(this);
        }

        private void OnDisable()
        {
            if (Application.isPlaying) InkStylePanel.Unregister(this);
        }

        /// <summary>槽位数对不上会静默错位（第 2 个槽挂着第 1 个的材质），所以这里要吵。</summary>
        public bool Validate(out string problem)
        {
            if (target == null) { problem = "target 为空"; return false; }
            if (inkMaterials == null || inkMaterials.Length == 0) { problem = "inkMaterials 为空"; return false; }
            if (litMaterials != null && litMaterials.Length != inkMaterials.Length)
            {
                problem = string.Format("槽位数不符：lit={0} ink={1}（会错位）",
                                        litMaterials.Length, inkMaterials.Length);
                return false;
            }
            problem = null;
            return true;
        }
    }
}
