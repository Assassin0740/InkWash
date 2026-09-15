using UnityEngine;

namespace InkWash.Combat
{
    /// <summary>
    /// 玩家的全局只读引用。
    /// 敌人找玩家如果靠 `GameObject.Find("Player")`，那么节点一改名/一换层级就静默失效
    /// （本项目已经在这种"按名字找"上栽过两次）。改成由玩家自己在 Awake 注册 —— 明确、可检。
    /// </summary>
    public static class PlayerRef
    {
        public static Transform Instance;
        public static GameObject GameObject;

        public static void Register(Transform t)
        {
            Instance = t;
            GameObject = t != null ? t.gameObject : null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlay() { Instance = null; GameObject = null; }

        public static Vector3 Position => Instance != null ? Instance.position : Vector3.zero;
        public static bool Exists => Instance != null;
    }
}
