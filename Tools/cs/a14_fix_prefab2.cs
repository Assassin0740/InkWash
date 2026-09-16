// a14_fix_prefab2.cs —— 修好三个新敌人 prefab（真正终版）
//
// ============ a13 暴露的两个"口径"错误，都记下来 ============
//
// a13 已经把 KayKit 残留删干净了（Visual 只剩 Model 一个子节点），
// 那部分是对的，保留。但它在"确认缩放生效"这一步用了**自我抵消的口径**：
//
//   【错 1】在 holder 自己的空间里量高度，用来证明 holder.localScale 生效。
//           holder 是自己的参考系 ⇒ 它的缩放**必然**被除回去 ⇒ 读数**永远不变**。
//           这和 a10 的 bug1 是同一个错误：**用被测量对象自己当参考系**。
//           正确口径：在**角色的根坐标系（go 空间）**里量 —— 那才是"站在地上多高"。
//
//   【错 2】只乘了 holder 自己的缩放，**漏了中间容器 Visual 的 localScale**。
//           模板 Visual 上带着 KayKit 时代的缩放（MoShan 0.6032 / MoGuai 0.8079 /
//           MoGu 0.7848）—— a13 没动它，它就会一路乘进来。
//           实测：2.274 × 1.2311 × 0.6032 = 1.689（与 a13 报的 go 空间高**完全吻合**，
//           三个怪都吻合到小数点后 3~4 位）⇒ 假设确证。
//
// ============ 本版口径（干净、可自证）============
//   1. 删 Visual 下非 Model 子节点（沿用 a13，已生效，这里做幂等复核）
//   2. **把所有中间容器的缩放归一到 1**（Visual.localScale = 1，holder.localScale = 1）
//      —— 消除"缩放藏在哪个节点上"的歧义，全程只用 go 空间一个口径
//   3. 在 **go 空间**量 [p1, p99] ⇒ bodyH、脚底 = p1
//   4. k = targetH / bodyH 写进 **holder.localScale**
//   5. 在 **go 空间**重量 ⇒ 断言"读数确实变了且 ≈ targetH"（这才叫验证生效）
//   6. holder.localPosition.y -= p1_after
//   7. 复核 go 空间：身高 / 脚底偏离 0
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

var sb = new StringBuilder();
string root = Path.GetDirectoryName(Application.dataPath);
const string EnemyDir = "Assets/_Project/Prefabs/Enemies";

var jobs = new[]
{
    ("Z_Enemy_MoShan", 2.80f, "山怪"),
    ("Z_Enemy_MoGuai", 2.10f, "兽人"),
    ("Z_Enemy_MoGu",   1.90f, "不死兵"),
};

// ---- 顶点真值（★ 唯一可靠口径）：BakeMesh 蒙皮后顶点 → 世界 → space 局部 ----
List<Vector3> VertsIn(GameObject go, Transform space)
{
    var all = new List<Vector3>();
    var toLocal = space.worldToLocalMatrix;
    foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
    {
        if (smr.sharedMesh == null) continue;
        var baked = new Mesh();
        smr.BakeMesh(baked, true);
        var verts = baked.vertices;
        var l2w = smr.transform.localToWorldMatrix;
        for (int i = 0; i < verts.Length; i++)
            all.Add(toLocal.MultiplyPoint3x4(l2w.MultiplyPoint3x4(verts[i])));
        UnityEngine.Object.DestroyImmediate(baked);
    }
    foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
    {
        if (mf.GetComponent<SkinnedMeshRenderer>() != null) continue;
        if (mf.sharedMesh == null) continue;
        var verts = mf.sharedMesh.vertices;
        var l2w = mf.transform.localToWorldMatrix;
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

sb.AppendLine("========== a14 修 prefab v2（统一 go 空间口径 + 归一中间容器缩放）==========");

foreach (var (nm, targetH, note) in jobs)
{
    string path = EnemyDir + "/" + nm + ".prefab";
    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
    sb.AppendLine();
    sb.AppendLine("================ " + nm + "（" + note + "，目标 " + targetH.ToString("F2") + " m）================");
    if (prefab == null) { sb.AppendLine("  ★ 读不到"); continue; }

    var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
    go.transform.position = new Vector3(0f, 4000f, 0f);
    go.transform.rotation = Quaternion.identity;
    go.transform.localScale = Vector3.one;

    var visual = go.transform.Find("Visual");
    if (visual == null) { sb.AppendLine("  ★ 没有 Visual"); UnityEngine.Object.DestroyImmediate(go); continue; }

    // ---- 1) 幂等清残留 ----
    var killed = new List<string>();
    for (int i = visual.childCount - 1; i >= 0; i--)
    {
        var c = visual.GetChild(i);
        if (c.name == "Model") continue;
        killed.Add(c.name);
        UnityEngine.Object.DestroyImmediate(c.gameObject);
    }
    if (killed.Count > 0)
        sb.AppendLine("  [1] 补删残留 ×" + killed.Count + " → " + string.Join(", ", killed.ToArray()));
    else
        sb.AppendLine("  [1] 无残留（a13 已清）");

    var holder = visual.Find("Model");
    if (holder == null) { sb.AppendLine("  ★ 找不到 Visual/Model"); UnityEngine.Object.DestroyImmediate(go); continue; }

    // ---- 2) 归一所有中间容器的缩放（消除"缩放藏在哪"的歧义）----
    float visScaleBefore = visual.localScale.x;
    visual.localScale = Vector3.one;
    holder.localScale = Vector3.one;
    holder.localPosition = Vector3.zero;
    sb.AppendLine("  [2] Visual.localScale " + visScaleBefore.ToString("F4") + " → 1"
                  + "；holder 复位（scale=1, posY=0）");

    // ---- 3) go 空间量真值 ----
    var vs0 = VertsIn(go, go.transform);
    float lo0, hi0, p10, p990;
    YStats(vs0, out lo0, out hi0, out p10, out p990);
    float bodyH = p990 - p10;
    sb.AppendLine("  [3] go 空间顶点 " + vs0.Count
                  + "  Y范围=[" + lo0.ToString("F3") + "," + hi0.ToString("F3") + "]"
                  + "  1~99分位=[" + p10.ToString("F3") + "," + p990.ToString("F3") + "]"
                  + "  分位高=" + bodyH.ToString("F3"));
    if (bodyH < 0.05f) { sb.AppendLine("  ★ 分位高异常，跳过"); UnityEngine.Object.DestroyImmediate(go); continue; }

    // ---- 4) 缩放（写进 holder）----
    float k = targetH / bodyH;
    holder.localScale = Vector3.one * k;

    // ---- 5) ★ 在 go 空间重量，证明缩放生效 ----
    var vs1 = VertsIn(go, go.transform);
    float lo1, hi1, p11, p991;
    YStats(vs1, out lo1, out hi1, out p11, out p991);
    float h1 = p991 - p11;
    sb.AppendLine("  [4] k = " + targetH.ToString("F2") + " / " + bodyH.ToString("F3")
                  + " = " + k.ToString("F4"));
    sb.AppendLine("  [5] ★go 空间重量（证明缩放生效）: 分位高 " + bodyH.ToString("F3")
                  + " → " + h1.ToString("F3") + "  （目标 " + targetH.ToString("F2") + "）"
                  + (Mathf.Abs(h1 - targetH) < 0.02f ? "  ✓" : "  ★"));

    // ---- 6) 落地 ----
    float dy = -p11;
    holder.localPosition = new Vector3(0f, dy, 0f);
    sb.AppendLine("  [6] 落地: 脚底(1分位)=" + p11.ToString("F4") + " ⇒ holder.localPosition.y = " + dy.ToString("F4"));

    // ---- 7) 复核 ----
    var vs2 = VertsIn(go, go.transform);
    float lo2, hi2, p12, p992;
    YStats(vs2, out lo2, out hi2, out p12, out p992);
    sb.AppendLine("  [7] ★复核(go 空间): Y范围=[" + lo2.ToString("F3") + "," + hi2.ToString("F3")
                  + "]  分位高=" + (p992 - p12).ToString("F3")
                  + "  脚底偏离0=" + p12.ToString("F4"));

    // ---- 8) 碰撞体 / 枪口 ----
    var cap = go.GetComponent<CapsuleCollider>();
    if (cap != null)
    {
        cap.height = targetH * 0.92f;
        cap.radius = Mathf.Max(0.25f, targetH * 0.22f);
        cap.center = new Vector3(cap.center.x, targetH * 0.5f, cap.center.z);
        sb.AppendLine("  [8] CapsuleCollider h=" + cap.height.ToString("F2")
                      + " r=" + cap.radius.ToString("F2") + " centerY=" + cap.center.y.ToString("F2"));
    }
    var muzzle = go.transform.Find("Muzzle");
    if (muzzle != null)
    {
        muzzle.localPosition = new Vector3(muzzle.localPosition.x, targetH * 0.62f, muzzle.localPosition.z);
        sb.AppendLine("  [8] Muzzle.localPosition.y = " + muzzle.localPosition.y.ToString("F3"));
    }

    PrefabUtility.ApplyPrefabInstance(go, InteractionMode.AutomatedAction);
    UnityEngine.Object.DestroyImmediate(go);
    sb.AppendLine("  [9] 已写回");
}

AssetDatabase.SaveAssets();
AssetDatabase.Refresh();

File.WriteAllText(Path.Combine(root, "Tools/reports/a14_fix_prefab2.txt"), sb.ToString());
Debug.Log("[a14]\n" + sb.ToString());
