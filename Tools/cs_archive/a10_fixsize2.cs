// a10_fixsize2.cs —— 修正尺寸 v2
//
// a9 三个自己写的 bug，全在"测量口径"上，记下来避免再犯：
//
//   Bug1 `CollectVerts` 用 `go.transform.worldToLocalMatrix` 把**容器缩放除回去了**。
//        缩放生效后顶点世界坐标变化，而 worldToLocal 又按同一缩放反算 ⇒ 读数恒定不变
//        （表现为"改完 scale，包围盒一字不差"，看着像"没生效"，实为"量错了"）。
//        ⇒ 要量"模型相对于容器的大小"，必须用 **holder.worldToLocalMatrix**。
//
//   Bug2 「Hips→Head × 1.85」当身高 —— 骨骼距离是**当前姿态**的函数，
//        而 `anim.Update(0.033f)` 只推一帧，骨骼往往还在 bindpose（T-Pose 手臂张开）。
//        实测山怪算出 0.941 m 的"身高"就是它。
//        ⇒ 骨架法只用来**交叉验证**，不作为主判据；主判据用**顶点分位高度**。
//
//   Bug3 我写了"最低点 vs 1 分位"的二选一，但条件写反，
//        于是 MoGuai 的 -3.842（游离几何）被当成脚底 ⇒ 模型被抬高 3.84 m。
//        ⇒ 脚底一律取 **1 分位**（绝对最低点对小数量游离几何无抵抗力）。
//
// v2 判据（干净且可复核）：
//   1. 量 holder 局部空间的顶点 Y 分位 [p1, p99]，bodyH = p99 - p1
//   2. k = targetH / bodyH，应用到 holder.localScale
//   3. 重新量（这次用 holder 空间，缩放会正确反映）⇒ 求新的 p1
//   4. holder.localPosition.y -= p1_after（把 1 分位抬到 0）
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

var sb = new StringBuilder();
string root = Path.GetDirectoryName(Application.dataPath);

// ★ 量"相对于某个 transform 的局部坐标"
List<Vector3> VertsIn(GameObject go, Transform space)
{
    var all = new List<Vector3>();
    var toLocal = space.worldToLocalMatrix;
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

void YStats(List<Vector3> vs, out float lo, out float hi, out float p1, out float p99)
{
    lo = hi = p1 = p99 = 0f;
    if (vs.Count == 0) return;
    var ys = new List<float>(vs.Count);
    foreach (var v in vs) ys.Add(v.y);
    ys.Sort();
    lo = ys[0]; hi = ys[ys.Count - 1];
    p1 = ys[Mathf.Clamp((int)(ys.Count * 0.01f), 0, ys.Count - 1)];
    p99 = ys[Mathf.Clamp((int)(ys.Count * 0.99f), 0, ys.Count - 1)];
}

var jobs = new[]
{
    ("Z_Enemy_MoShan", 2.80f, "山怪"),
    ("Z_Enemy_MoGuai", 2.10f, "兽人"),
    ("Z_Enemy_MoGu",   1.90f, "不死兵"),
};

sb.AppendLine("========== 修正尺寸 v2（口径：holder 局部 + 顶点分位 + 1分位当脚底）==========");
sb.AppendLine();

foreach (var (nm, targetH, note) in jobs)
{
    string path = "Assets/_Project/Prefabs/Enemies/" + nm + ".prefab";
    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
    if (prefab == null) { sb.AppendLine("★ 读不到 " + path); continue; }

    var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
    go.transform.position = new Vector3(0f, 2000f, 0f);
    go.transform.rotation = Quaternion.identity;
    go.transform.localScale = Vector3.one;

    var visual = go.transform.Find("Visual");
    Transform holder = (visual != null && visual.childCount > 0) ? visual.GetChild(0) : null;
    if (holder == null) { sb.AppendLine("★ " + nm + " 结构异常"); UnityEngine.Object.DestroyImmediate(go); continue; }

    // ---- 先复位 holder，保证从干净状态起算 ----
    holder.localScale = Vector3.one;
    holder.localPosition = Vector3.zero;

    var (lo0, hi0, p10, p990) = (0f, 0f, 0f, 0f);
    {
        var vs = VertsIn(go, holder);
        YStats(vs, out lo0, out hi0, out p10, out p990);
        sb.AppendLine("--- " + nm + "（" + note + "，目标 " + targetH + " m）---");
        sb.AppendLine("  复位后（holder 空间）: 顶点 " + vs.Count
                      + "  Y范围=[" + lo0.ToString("F3") + "," + hi0.ToString("F3") + "]"
                      + "  1~99分位=[" + p10.ToString("F3") + "," + p990.ToString("F3") + "]"
                      + "  分位高=" + (p990 - p10).ToString("F3"));
    }

    float bodyH = p990 - p10;
    if (bodyH < 0.05f) { sb.AppendLine("  ★ 分位高异常（" + bodyH + "），跳过"); UnityEngine.Object.DestroyImmediate(go); continue; }

    float k = targetH / bodyH;
    holder.localScale = Vector3.one * k;

    // ---- 缩放后重量（此时 holder 空间会正确反映缩放）----
    var vs2 = VertsIn(go, holder);
    float lo2, hi2, p12, p992;
    YStats(vs2, out lo2, out hi2, out p12, out p992);
    sb.AppendLine("  k = " + targetH + " / " + bodyH.ToString("F3") + " = " + k.ToString("F4"));
    sb.AppendLine("  缩放后: Y范围=[" + lo2.ToString("F3") + "," + hi2.ToString("F3") + "]"
                  + "  1~99分位=[" + p12.ToString("F3") + "," + p992.ToString("F3") + "]"
                  + "  分位高=" + (p992 - p12).ToString("F3"));

    // ---- 落地：把 1 分位抬到 0（脚底）----
    float dy = -p12;
    holder.localPosition = new Vector3(0f, dy, 0f);
    sb.AppendLine("  落地：脚底(1分位)=" + p12.ToString("F3") + " ⇒ holder.localPosition.y = " + dy.ToString("F3"));

    // ---- 复核 ----
    var vs3 = VertsIn(go, holder);
    float lo3, hi3, p13, p993;
    YStats(vs3, out lo3, out hi3, out p13, out p993);
    // 复核用 go 空间（真正决定"站在地上多高"）
    var vsG = VertsIn(go, go.transform);
    float glo, ghi, gp1, gp99;
    YStats(vsG, out glo, out ghi, out gp1, out gp99);
    sb.AppendLine("  ★ 复核（go 空间，即世界里相对角色根）: Y范围=[" + glo.ToString("F3") + "," + ghi.ToString("F3")
                  + "]  分位高=" + (gp99 - gp1).ToString("F3")
                  + "   脚底偏离 0 的量 = " + gp1.ToString("F4"));

    PrefabUtility.ApplyPrefabInstance(go, InteractionMode.AutomatedAction);
    UnityEngine.Object.DestroyImmediate(go);
    sb.AppendLine("  已写回");
    sb.AppendLine();
}

AssetDatabase.SaveAssets();
AssetDatabase.Refresh();

File.WriteAllText(Path.Combine(root, "Tools/reports/a10_fixsize2.txt"), sb.ToString());
Debug.Log("[a10]\n" + sb.ToString());
