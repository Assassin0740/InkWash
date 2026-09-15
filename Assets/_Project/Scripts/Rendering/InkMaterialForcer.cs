using System.Collections.Generic;
using UnityEngine;
using InkWash.UI;

namespace InkWash.Rendering
{
    /// <summary>
    /// 把"漏在水墨管线之外"的渲染器强制纳入水墨着色，并把漏网的列出来。
    ///
    /// 为什么需要它（增量 F 的根因，实测证据 Tools/reports/f_mats.txt）：
    ///   骷髅有 8 个子渲染器（Body/Head/Jaw/Cloak/Arm×2/Leg×2/Eyes），
    ///   而 `InkMaterialSwap` 只有**一个 target 渲染器** —— 于是只有 `ArmLeft` 被换成了
    ///   水墨材质，其余 7 个保持 KayKit 的 URP/Lit 原材质：
    ///     · `skeleton` —— 亮蓝灰骨头 + 橙斗篷（画面上**唯一的高饱和色**，对比最强，抢走全部视线）
    ///     · `Glow`     —— 眼睛发光
    ///   全场景统计：skeleton ×21、Glow ×3、M_W_Sword ×1（剑也是 URP/Lit，金属反光）。
    ///   这正是用户说的「像普通积木玩偶一样」——不是 shader 不够水墨，
    ///   是**它们根本没走我们的 shader**。
    ///
    /// 设计要点：
    ///   · **材质来源不靠字符串找资产**，而是从该角色自己已有的 `InkMaterialSwap.inkMaterials`
    ///     里取 —— 既尊重美术在 Inspector 里的选择，也避免改名后静默失效。
    ///   · 换完**自己复核**：还有任何一个渲染器不是水墨 shader，就直接 LogError。
    ///     本项目硬规矩「失配要吵」：静默失效的东西会在截图里伪装成"shader 写错了"。
    /// </summary>
    public static class InkMaterialForcer
    {
        /// <summary>剑的专用水墨材质（与角色同一套墨阶表，但基础墨阶更浅，好让刃口读出来）。</summary>
        const string SwordInkPath = "Assets/_Project/Art/Materials/M_W_Sword_Ink.mat";

        public static bool IsInk(Material m)
        {
            if (m == null || m.shader == null) return false;
            string n = m.shader.name;
            return n.StartsWith("InkWash/") || n.StartsWith("Hidden/InkWash/");
        }

        /// <summary>场景里所有带 InkMaterialSwap 的根（玩家、预放置的敌人）各强制一次。</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        public static void ForceAll()
        {
            foreach (var sw in Object.FindObjectsOfType<InkMaterialSwap>(true))
                ForceInk(sw.gameObject, sw.gameObject.name, true);
        }

        /// <summary>把 root 下所有渲染器纳入水墨。返回被改动的渲染器数量。</summary>
        public static int ForceInk(GameObject root, string tag, bool log)
        {
            if (root == null) return 0;

            Material ink = ResolveInk(root);
            if (ink == null)
            {
                Debug.LogError("[InkMaterialForcer] " + tag
                    + "：找不到可用的水墨材质（该角色没有配置 InkMaterialSwap.inkMaterials）—— 什么都没改。");
                return 0;
            }

            Material swordInk = null;
            bool swordSought = false;
            int changed = 0;
            var leftovers = new List<string>();

            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                if (mats == null || mats.Length == 0) continue;

                bool need = false;
                for (int i = 0; i < mats.Length; i++) if (!IsInk(mats[i])) { need = true; break; }
                if (!need) continue;

                // 剑单独处理：它挂在骨头插槽下，且是 MeshRenderer 而不是蒙皮
                bool isSword = false;
                for (int i = 0; i < mats.Length; i++)
                    if (mats[i] != null && mats[i].name.StartsWith("M_W_Sword")) { isSword = true; break; }

                Material use = ink;
                if (isSword)
                {
                    if (!swordSought) { swordSought = true; swordInk = Load(SwordInkPath); }
                    if (swordInk != null) use = swordInk;
                    else leftovers.Add(r.name + "（剑：水墨材质加载失败，退回角色墨材质）");
                }

                for (int i = 0; i < mats.Length; i++) mats[i] = use;
                r.sharedMaterials = mats;
                changed++;
            }

            // ---- 自检：复核有没有漏网 ----
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                for (int i = 0; mats != null && i < mats.Length; i++)
                    if (!IsInk(mats[i]))
                        leftovers.Add(r.name + " ← " + (mats[i] == null ? "<空>" : mats[i].shader.name));
            }

            if (log)
            {
                if (leftovers.Count > 0)
                    Debug.LogError("[InkMaterialForcer] " + tag + "：改 " + changed
                        + " 个，**仍有 " + leftovers.Count + " 个渲染器不是水墨**：\n  · "
                        + string.Join("\n  · ", leftovers.ToArray()));
                else
                    Debug.Log("[InkMaterialForcer] " + tag + "：改 " + changed + " 个渲染器，全部水墨 ✓");
            }
            return changed;
        }

        static Material ResolveInk(GameObject root)
        {
            var sw = root.GetComponentInChildren<InkMaterialSwap>(true);
            if (sw == null || sw.inkMaterials == null || sw.inkMaterials.Length == 0) return null;
            for (int i = 0; i < sw.inkMaterials.Length; i++)
                if (sw.inkMaterials[i] != null) return sw.inkMaterials[i];
            return null;
        }

        static Material Load(string path)
        {
#if UNITY_EDITOR
            return UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(path);
#else
            // 非编辑器构建下没有 AssetDatabase。此时退回角色墨材质（forceink 的调用方会兜底）。
            return null;
#endif
        }
    }
}
