// a11_fixsize3.cs —— 修正尺寸 v3：用 BakeMesh 拿真正的蒙皮后顶点
//
// 前三版全在同一处栽跟头，这次把根因彻底写清楚：
//
//   ★ SkinnedMeshRenderer 的顶点**不能用 `smr.transform.localToWorldMatrix × vertex` 推算**。
//     蒙皮后的顶点位置是**骨骼矩阵的加权线性混合**（每顶点有权重，通常 4 根骨），
//     `smr.transform` 只是这个渲染器挂载的节点，它对最终形状**没有决定作用**。
//     所以：
//       a9  —— 用 worldToLocal 把缩放除回去，读数不变
//       a10 —— holder 缩放明明改了，但顶点位置由骨骼决定，读数还是不变
//     **两次都是"公式本身不适用于蒙皮网格"，不是缩放没生效。**
//
//   正确做法：`SkinnedMeshRenderer.BakeMesh(mesh, true)` 得到**当前姿态的蒙皮结果**，
//   这才是"看起来很真实"的顶点；再乘 `smr.transform.localToWorldMatrix` 转到世界。
//
//   项目记忆里其实记过这个（M6 掩码"必须用真渲染而非推算"），但这里是另一个面：
//   连"量尺寸"这个最基础的操作，蒙皮网格也不能用静态顶点公式。
//
// 判据：
//   1. BakeMesh → 世界顶点 → 求 Y 的 [p1, p99]，bodyH
//   2. k = targetH / bodyH
//   3. 应用 scale 到 holder，**再用 BakeMesh 复量**（这次会正确反映）
//   4. 脚底对齐：holder.localPosition.y -= p1
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

var sb = new StringBuilder();
string root = Path.GetDirectoryName(Application.dataPath);

// ★ 用 BakeMesh 拿蒙皮后顶点（世界坐标）
List<Vector3> BakedWorldVerts(GameObject go)
{
    var all = new List<Vector3>();
    var tmp = new Mesh();
    foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
    {
        if (smr.sharedMesh == null) continue;
        smr.BakeMesh(tmp, true);
        var l2w = smr.transform.localToWorldMatrix;
        var vs = tmp.vertices;
        for (int i = 0; i < vs.Length; i++)
            all.Add(l2w.MultiplyPoint3x4(vs[i]));
    }
    UnityEngine.Object.DestroyImmediate(tmp);
    return all;
}

// 转到 go 根空间
List<Vector3> ToLocal(List<Vector3> world, Transform space)
{
    var m = space.worldToLocalMatrix;
    var r = new List<Vector3>(world.Count);
    foreach (var w in world) r.Add(m.MultiplyPoint3x4(w));
    return r;
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

sb.AppendLine("========== 修正尺寸 v3（BakeMesh 真蒙皮顶点）==========");
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

    holder.localScale = Vector3.one;
    holder.localPosition = Vector3.zero;

    sb.AppendLine("--- " + nm + "（" + note + "，目标 " + targetH + " m）---");

    // 先让它处于一个稳定姿态（推几帧，别停在 bindpose）
    var anim = go.GetComponentInChildren<Animator>(true);
    if (anim != null && anim.runtimeAnimatorController != null)
    {
        anim.Play("Idle", 0, 0f);
        for (int i = 0; i < 4; i++) anim.Update(0.033f);
    }

    var w0 = BakedWorldVerts(go);
    var l0 = ToLocal(w0, go.transform);
    float lo0, hi0, p10, p990;
    YStats(l0, out lo0, out hi0, out p10, out p990);
    sb.AppendLine("  复位后（BakeMesh，go 空间）: 顶点 " + l0.Count
                  + "  Y范围=[" + lo0.ToString("F3") + "," + hi0.ToString("F3") + "]"
                  + "  1~99分位=[" + p10.ToString("F3") + "," + p990.ToString("F3") + "]"
                  + "  分位高=" + (p990 - p10).ToString("F3"));

    float bodyH = p990 - p10;
    if (bodyH < 0.05f) { sb.AppendLine("  ★ 分位高异常，跳过"); UnityEngine.Object.DestroyImmediate(go); continue; }

    float k = targetH / bodyH;
    float prevK = holder.localScale.x;
    holder.localScale = Vector3.one * (prevK * k);   // 累乘（prefab 里可能已有旧值）
    sb.AppendLine("  k = " + targetH + " / " + bodyH.ToString("F3") + " = " + k.ToString("F4")
                  + "（holder 先复位为 1，故最终 scale = " + (prevK * k).ToString("F4") + "）");

    // 复量（这次用 BakeMesh，缩放会正确反映）
    if (anim != null && anim.runtimeAnimatorController != null)
        for (int i = 0; i < 3; i++) anim.Update(0.033f);

    var w2 = BakedWorldVerts(go);
    var l2 = ToLocal(w2, go.transform);
    float lo2, hi2, p12, p992;
    YStats(l2, out lo2, out hi2, out p12, out p992);
    sb.AppendLine("  缩放后: Y范围=[" + lo2.ToString("F3") + "," + hi2.ToString("F3") + "]"
                  + "  1~99分位=[" + p12.ToString("F3") + "," + p992.ToString("F3") + "]"
                  + "  分位高=" + (p992 - p12).ToString("F3"));

    // 落地
    float dy = -p12;
    holder.localPosition = new Vector3(0f, dy, 0f);
    sb.AppendLine("  落地：1分位(脚底)=" + p12.ToString("F3") + " ⇒ localPosition.y = " + dy.ToString("F3"));

    if (anim != null && anim.runtimeAnimatorController != null)
        for (int i = 0; i < 3; i++) anim.Update(0.033f);

    var w3 = BakedWorldVerts(go);
    var l3 = ToLocal(w3, go.transform);
    float lo3, hi3, p13, p993;
    YStats(l3, out lo3, out hi3, out p13, out p993);
    sb.AppendLine("  ★ 复核: Y范围=[" + lo3.ToString("F3") + "," + hi3.ToString("F3")
                  + "]  分位高=" + (p993 - p13).ToString("F3")
                  + "  脚底=" + p13.ToString("F4"));

    PrefabUtility.ApplyPrefabInstance(go, InteractionMode.AutomatedAction);
    UnityEngine.Object.DestroyImmediate(go);
    sb.AppendLine("  已写回");
    sb.AppendLine();
}

AssetDatabase.SaveAssets();
AssetDatabase.Refresh();

File.WriteAllText(Path.Combine(root, "Tools/reports/a11_fixsize3.txt"), sb.ToString());
Debug.Log("[a11]\n" + sb.ToString());
