using System;
using System.IO;
using System.Text;
using UnityEngine;

// drg_scale26.cs —— 第二十六轮收尾：把「半径比 1.3252」钉死
//
// 背景：drg_headfix 的「头簇相对挂点 半径比」在**每一行**都恰好 = 1.3252
// （恒定 ⇒ 是标尺差不是姿态差），但这个 1.3252 到底长在哪一级没查。
// 两个 prefab 的 YAML 里都**没有**非单位 m_LocalScale，所以只能怀疑运行时/导入设置。
//
// 本探针**只读**：用 LoadPrefabContents 把两个 prefab 各加载进预览场景，
// 打印骨骼的 localScale / lossyScale 与完整祖先链，看哪一级不是 1。
public class drg_scale26 : MonoBehaviour
{
    const string RP = "D:/Unity Project/InkWash/Tools/reports/drg_scale26.txt";
    static readonly string[] PATHS = new string[] {
        "Assets/_Project/Prefabs/Enemies/Z_Enemy_MoLong.prefab",
        "Assets/_Project/Prefabs/Ziyuan/Z_Dragon.prefab",
    };
    static readonly string[] BONES = new string[] { "drgon_025", "drgon_0146", "drgon_03" };

    void Start()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== drg_scale26：两 prefab 骨架缩放对比（只读，不改资产）===");
        foreach (var p in PATHS)
        {
            sb.AppendLine();
            sb.AppendLine("── " + p + " ──");
            GameObject root = null;
            try { root = UnityEditor.PrefabUtility.LoadPrefabContents(p); }
            catch (Exception e) { sb.AppendLine("  × 加载异常 " + e.GetType().Name + ": " + e.Message); }
            if (root == null) { sb.AppendLine("  × 加载失败"); continue; }
            try
            {
                sb.AppendLine("  根 " + root.name + "  local=" + F(root.transform.localScale)
                              + "  lossy=" + F(root.transform.lossyScale));

                var smrs = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                sb.AppendLine("  SkinnedMeshRenderer " + smrs.Length + " 个");
                for (int i = 0; i < smrs.Length && i < 4; i++)
                {
                    var m = smrs[i].sharedMesh;
                    sb.AppendLine("    [" + i + "] " + Chain(smrs[i].transform)
                                  + "\n         mesh=" + (m != null ? UnityEditor.AssetDatabase.GetAssetPath(m) : "-")
                                  + "  骨骼根=" + (smrs[i].rootBone != null ? smrs[i].rootBone.name : "-")
                                  + "  lossy=" + F(smrs[i].transform.lossyScale));
                }

                foreach (var b in BONES)
                {
                    var t = FindDeep(root.transform, b);
                    if (t == null) { sb.AppendLine("  骨 " + b + "：未找到"); continue; }
                    sb.AppendLine("  骨 " + b + "  local=" + F(t.localScale) + "  lossy=" + F(t.lossyScale));
                    var cur = t; int depth = 0;
                    while (cur != null && depth < 14)
                    {
                        sb.AppendLine("      [" + depth + "] " + cur.name
                                      + "  local=" + F(cur.localScale) + "  lossy=" + F(cur.lossyScale));
                        cur = cur.parent; depth++;
                    }
                }
            }
            finally { UnityEditor.PrefabUtility.UnloadPrefabContents(root); }
        }
        sb.AppendLine();
        sb.AppendLine("★ 读法：某一级 lossy 不是 1 ⇒ 1.3252 就长在那；");
        sb.AppendLine("  全链都精确 = (1,1,1) ⇒ 1.3252 **不在资产里**，要去运行时（scene 侧）找。");
        sb.AppendLine("  另：1.3252 只影响「半径比」这一列，Δdir 已归一化、与它无关。");
        try { File.WriteAllText(RP, sb.ToString()); } catch { }
        Debug.Log("[drg_scale26] 写入 " + RP);
    }

    static string F(Vector3 v)
    {
        return "(" + v.x.ToString("F4") + "," + v.y.ToString("F4") + "," + v.z.ToString("F4") + ")";
    }

    static string Chain(Transform t)
    {
        string s = t.name; var c = t.parent; int n = 0;
        while (c != null && n < 6) { s = c.name + "/" + s; c = c.parent; n++; }
        return s;
    }

    static Transform FindDeep(Transform root, string name)
    {
        if (root.name == name) return root;
        for (int i = 0; i < root.childCount; i++)
        {
            var r = FindDeep(root.GetChild(i), name);
            if (r != null) return r;
        }
        return null;
    }
}

var __hostS26 = new GameObject("drg_scale26");
__hostS26.AddComponent<drg_scale26>();
return "DRG_SCALE26_STARTED";
