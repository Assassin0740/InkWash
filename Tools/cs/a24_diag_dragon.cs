// a24_diag_dragon.cs —— 诊断 a23 为什么"量不到缩放"且"模型在 -7000"
//
// 两个症状：
//   ① 落地算出的 1 分位 = -7000.005 ⇒ 模型顶点在 go 空间的 Y ≈ -7000
//   ② 给了 k=1.618 之后 bbox 依然是 4.326（= 缩放前值）⇒ 缩放像是没生效
//
// 这两个都可能是我"又量错了"，也可能是"真的错了"。
// a13~a14 的教训：**先用 dump 把真层级摊开，别再猜测量口径。**
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

var sb = new StringBuilder();
string root = Path.GetDirectoryName(Application.dataPath);

string path = "Assets/_Project/Prefabs/Enemies/Z_Enemy_MoLong.prefab";
var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
sb.AppendLine("========== a24 诊断 Z_Enemy_MoLong ==========");
if (prefab == null) { sb.AppendLine("★ 读不到"); File.WriteAllText(Path.Combine(root, "Tools/reports/a24_diag_dragon.txt"), sb.ToString()); Debug.Log(sb.ToString()); return; }

var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
go.transform.position = Vector3.zero;      // ★ 放在原点，排除 7000 的干扰
go.transform.rotation = Quaternion.identity;
go.transform.localScale = Vector3.one;

sb.AppendLine("根 localPosition = " + go.transform.localPosition.ToString("F4"));
sb.AppendLine();

// ---- 真层级（含每个节点的 localScale/localPosition）----
Walk(go.transform, 0, 5, sb);

// ---- 用两种口径各量一次，对照 ----
sb.AppendLine();
sb.AppendLine("---- 测量口径对照 ----");

// 口径 A：go 空间（worldToLocalMatrix 会**除回** go 自己的缩放；go 缩放=1 所以无影响）
{
    var inv = go.transform.worldToLocalMatrix;
    var mn = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
    var mx = new Vector3(float.MinValue, float.MinValue, float.MinValue);
    int cnt = 0;
    foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
    {
        if (smr.sharedMesh == null) continue;
        var baked = new Mesh();
        smr.BakeMesh(baked, true);
        var l2w = smr.transform.localToWorldMatrix;
        foreach (var v in baked.vertices)
        { var p = inv.MultiplyPoint3x4(l2w.MultiplyPoint3x4(v)); mn = Vector3.Min(mn, p); mx = Vector3.Max(mx, p); cnt++; }
        UnityEngine.Object.DestroyImmediate(baked);
    }
    sb.AppendLine("  [A] go 空间 (BakeMesh)  顶点=" + cnt
                  + "  X[" + mn.x.ToString("F3") + "," + mx.x.ToString("F3") + "]"
                  + "  Y[" + mn.y.ToString("F3") + "," + mx.y.ToString("F3") + "]"
                  + "  Z[" + mn.z.ToString("F3") + "," + mx.z.ToString("F3") + "]");
}

// 口径 B：世界空间（不除任何东西）
{
    var mn = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
    var mx = new Vector3(float.MinValue, float.MinValue, float.MinValue);
    foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
    {
        if (smr.sharedMesh == null) continue;
        var baked = new Mesh();
        smr.BakeMesh(baked, true);
        var l2w = smr.transform.localToWorldMatrix;
        foreach (var v in baked.vertices)
        { var p = l2w.MultiplyPoint3x4(v); mn = Vector3.Min(mn, p); mx = Vector3.Max(mx, p); }
        UnityEngine.Object.DestroyImmediate(baked);
    }
    sb.AppendLine("  [B] 世界空间 (BakeMesh)  X[" + mn.x.ToString("F3") + "," + mx.x.ToString("F3") + "]"
                  + "  Y[" + mn.y.ToString("F3") + "," + mx.y.ToString("F3") + "]"
                  + "  Z[" + mn.z.ToString("F3") + "," + mx.z.ToString("F3") + "]");
}

// 口径 C：sharedMesh.vertices（bind pose，不做蒙皮）—— 用来判断"是不是蒙皮把它挪走了"
{
    var mn = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
    var mx = new Vector3(float.MinValue, float.MinValue, float.MinValue);
    int cnt = 0;
    var inv = go.transform.worldToLocalMatrix;
    foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
    {
        if (smr.sharedMesh == null) continue;
        var l2w = smr.transform.localToWorldMatrix;
        foreach (var v in smr.sharedMesh.vertices)
        { var p = inv.MultiplyPoint3x4(l2w.MultiplyPoint3x4(v)); mn = Vector3.Min(mn, p); mx = Vector3.Max(mx, p); cnt++; }
        sb.AppendLine("      " + PathOf(smr.transform) + "  rootBone=" + (smr.rootBone != null ? smr.rootBone.name : "null")
                      + "  bones=" + (smr.bones != null ? smr.bones.Length : 0)
                      + "  l2w.scale=" + smr.transform.lossyScale.ToString("F4"));
    }
    sb.AppendLine("  [C] go 空间 (bind pose)  顶点=" + cnt
                  + "  X[" + mn.x.ToString("F3") + "," + mx.x.ToString("F3") + "]"
                  + "  Y[" + mn.y.ToString("F3") + "," + mx.y.ToString("F3") + "]"
                  + "  Z[" + mn.z.ToString("F3") + "," + mx.z.ToString("F3") + "]");
}

// ---- 骨骼的实际世界位置 ----
sb.AppendLine();
sb.AppendLine("---- 骨骼世界位置（前 12 个）----");
int n = 0;
foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
{
    if (smr.bones == null) continue;
    foreach (var b in smr.bones)
    {
        if (b == null) continue;
        if (n++ >= 12) break;
        sb.AppendLine("  " + b.name.PadRight(20) + " world=" + b.position.ToString("F3"));
    }
    if (n >= 12) break;
}
if (n == 0) sb.AppendLine("  （没有 bones 数组）");

UnityEngine.Object.DestroyImmediate(go);
File.WriteAllText(Path.Combine(root, "Tools/reports/a24_diag_dragon.txt"), sb.ToString());
Debug.Log("[a24]\n" + sb.ToString());

void Walk(Transform t, int depth, int maxD, StringBuilder s)
{
    if (depth > maxD) return;
    s.AppendLine(new string(' ', depth * 2) + t.name
        + "  lScale=" + t.localScale.ToString("F4")
        + "  lPos=" + t.localPosition.ToString("F4"));
    for (int i = 0; i < t.childCount; i++) Walk(t.GetChild(i), depth + 1, maxD, s);
}

string PathOf(Transform t)
{
    var l = new List<string>();
    var c = t;
    while (c != null) { l.Insert(0, c.name); c = c.parent; }
    return string.Join("/", l.ToArray());
}
