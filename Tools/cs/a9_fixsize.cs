// a9_fixsize.cs —— 修正三个新敌人的容器缩放与落地偏移
//
// a8 实测暴露的问题：
//   MoShan  高 2.554（目标 2.80）  Y范围[-0.004, 2.550]  ← 基本正常，只要缩放
//   MoGuai  高 5.592（目标 2.10）  Y范围[-3.842, 1.750]  ← ★ 原点在身体中部，且下方有 3.8m 的几何
//   MoGu     高 3.343（目标 1.90）  Y范围[-0.148, 3.195]  ← 原点偏高
//
// ★ 一个必须小心的陷阱：Z_Enemy_MoGuai 的"高 5.592"可能**不是模型真有那么高**，
//   而是某个游离部件（IK 控制骨、垃圾几何）把包围盒撑大了。如果直接按 5.592 归一，
//   结果是把真正的身体缩到很小。
//
//   所以本脚本的归一判据不用"整体包围盒高度"，而用 **Humanoid 的 Hips→Head 距离**
//   推算身高（这是骨架给出的、不受游离几何污染的"身体尺度"），
//   再用顶点分布的主簇（去掉离群 1% 分位）复核。
//
// 同时要把模型**落地**（最低点对齐到 y=0）：走容器的 localPosition.y。
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

var sb = new StringBuilder();
string root = Path.GetDirectoryName(Application.dataPath);

// 收集顶点（局部于 go 空间），并按 y 排序求分位
List<Vector3> CollectVerts(GameObject go)
{
    var all = new List<Vector3>();
    var toLocal = go.transform.worldToLocalMatrix;
    foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
    {
        var m = smr.sharedMesh;
        if (m == null) continue;
        var verts = m.vertices;
        var l2w = smr.transform.localToWorldMatrix;
        for (int i = 0; i < verts.Length; i++)
            all.Add(toLocal.MultiplyPoint3x4(l2w.MultiplyPoint3x4(verts[i])));
    }
    return all;
}

(float lo, float hi, float p1, float p99) YStats(List<Vector3> vs)
{
    if (vs.Count == 0) return (0, 0, 0, 0);
    var ys = new List<float>(vs.Count);
    foreach (var v in vs) ys.Add(v.y);
    ys.Sort();
    return (ys[0], ys[ys.Count - 1], ys[(int)(ys.Count * 0.01f)], ys[(int)(ys.Count * 0.99f)]);
}

var jobs = new[]
{
    // prefab 名, 目标身高, 说明
    ("Z_Enemy_MoShan", 2.80f, "山怪（精英模板）"),
    ("Z_Enemy_MoGuai", 2.10f, "兽人（近战模板）"),
    ("Z_Enemy_MoGu",   1.90f, "不死兵（远程模板）"),
};

sb.AppendLine("========== 修正容器缩放 + 落地 ==========");
sb.AppendLine();

foreach (var (nm, targetH, note) in jobs)
{
    string path = "Assets/_Project/Prefabs/Enemies/" + nm + ".prefab";
    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
    if (prefab == null) { sb.AppendLine("★ 读不到 " + path); continue; }

    // ★ 用**场景实例 + ApplyPrefabInstance** 改 scale（LoadPrefabContents 改 root scale 会被覆盖）
    var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
    go.transform.position = new Vector3(0f, 2000f, 0f);
    go.transform.rotation = Quaternion.identity;
    go.transform.localScale = Vector3.one;

    var visual = go.transform.Find("Visual");
    if (visual == null) { sb.AppendLine("★ " + nm + " 没有 Visual"); UnityEngine.Object.DestroyImmediate(go); continue; }

    // 找到模型容器（Visual 下的第一个子对象）
    Transform holder = visual.childCount > 0 ? visual.GetChild(0) : null;
    if (holder == null) { sb.AppendLine("★ " + nm + " Visual 下无子对象"); UnityEngine.Object.DestroyImmediate(go); continue; }

    var anim = go.GetComponentInChildren<Animator>(true);

    // ---------- 1) 用骨架 Hips→Head 估身体尺度（不受游离几何污染）----------
    float skeletonH = -1f;
    if (anim != null && anim.avatar != null && anim.avatar.isHuman)
    {
        anim.Play("Idle", 0, 0f);
        anim.Update(0.033f);
        var hips = anim.GetBoneTransform(HumanBodyBones.Hips);
        var head = anim.GetBoneTransform(HumanBodyBones.Head);
        if (hips != null && head != null)
        {
            // 站姿下 Hips→Head 大约是身高的 0.52~0.58 倍（人类比例）
            float d = Vector3.Distance(hips.position, head.position);
            skeletonH = d * 1.85f;   // 粗估身高
            sb.AppendLine(nm + ": Hips→Head = " + d.ToString("F3") + " m  ⇒ 估算身高 ≈ " + skeletonH.ToString("F3"));
        }
    }

    // ---------- 2) 顶点分布（分位裁剪后）----------
    var vs = CollectVerts(go);
    var (lo, hi, p1, p99) = YStats(vs);
    float rawH = hi - lo;
    float bodyH = p99 - p1;
    sb.AppendLine("  顶点 " + vs.Count + " 个  Y范围=[" + lo.ToString("F3") + "," + hi.ToString("F3")
                  + "]  整体高=" + rawH.ToString("F3")
                  + "  1~99分位高=" + bodyH.ToString("F3"));

    // ---------- 3) 定缩放：优先骨架估算，骨架取不到用分位高度 ----------
    float baseH = skeletonH > 0.3f ? skeletonH : bodyH;
    float k = baseH > 0.05f ? targetH / baseH : 1f;
    sb.AppendLine("  判据基准高 = " + baseH.ToString("F3") + "（" + (skeletonH > 0.3f ? "骨架估算" : "顶点分位") + "）"
                  + "  ⇒ 缩放 k = " + k.ToString("F4"));

    holder.localScale = Vector3.one * k;

    // ---------- 4) 落地：把缩放后的最低点对齐到 y=0 ----------
    // 重新采集（缩放后世界坐标变了，但我们要的是相对 go 的 y）
    var vs2 = CollectVerts(go);
    var (lo2, hi2, p12, p992) = YStats(vs2);
    // 用 1 分位而不是绝对最低点 —— 绝对最低点常被游离几何拉下去
    float footY = lo2;
    float bodyBottom = p12;
    // 若最低点远低于分位（说明有游离几何），用分位当"脚底"
    float useFoot = (bodyBottom - lo2) > 0.25f * (p992 - p12) ? bodyBottom : lo2;
    float dy = -useFoot;
    holder.localPosition = new Vector3(holder.localPosition.x, holder.localPosition.y + dy, holder.localPosition.z);

    sb.AppendLine("  缩放后 Y范围=[" + lo2.ToString("F3") + "," + hi2.ToString("F3")
                  + "]，采脚底 y=" + useFoot.ToString("F3") + "  ⇒ 容器上移 " + dy.ToString("F3"));

    // ---------- 5) 复核 ----------
    var vs3 = CollectVerts(go);
    var (lo3, hi3, p13, p993) = YStats(vs3);
    sb.AppendLine("  ★ 复核：缩放=" + k.ToString("F4") + "  最终 Y范围=[" + lo3.ToString("F3") + "," + hi3.ToString("F3")
                  + "]  整体高=" + (hi3 - lo3).ToString("F3")
                  + "  1~99分位高=" + (p993 - p13).ToString("F3"));

    // 存回 prefab
    PrefabUtility.ApplyPrefabInstance(go, InteractionMode.AutomatedAction);
    UnityEngine.Object.DestroyImmediate(go);
    sb.AppendLine("  已写回 " + path);
    sb.AppendLine();
}

AssetDatabase.SaveAssets();
AssetDatabase.Refresh();

File.WriteAllText(Path.Combine(root, "Tools/reports/a9_fixsize.txt"), sb.ToString());
Debug.Log("[a9]\n" + sb.ToString());
