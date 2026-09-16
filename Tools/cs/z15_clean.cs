// z15_clean —— 清理素材里的垃圾几何
//
// 已知垃圾（从对比图里肉眼确认）：
//   ① Z_Orge   脚下黑方块 —— 平面底座（面积巨大、厚度≈0）
//   ② Z_Undead 头顶碎片   —— 悬空孤立小网格
//   ③ 各素材可能还有类似物
//
// 判据（两条同时用，避免误删）：
//   A. 「平面片」：包围盒最薄维度 < 该模型高度的 2%，且另两维都 > 高度 * 30%
//   B. 「孤立小片」：该网格的顶点数 < 主网格的 1%，且其包围盒与主体不重叠（悬空）
using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

var sb = new StringBuilder();
const string PD = "Assets/_Project/Prefabs/Ziyuan";
string[] pnames = { "Z_Orge", "Z_Orc", "Z_Undead", "Z_Dragon", "Z_Dragon_LP" };

foreach (var pn in pnames)
{
    string pp = PD + "/" + pn + ".prefab";
    var root = PrefabUtility.LoadPrefabContents(pp);

    // 收集所有网格 renderer
    var infos = new List<(Renderer r, Mesh m, Bounds b, string path)>();
    foreach (var r in root.GetComponentsInChildren<Renderer>())
    {
        Mesh m = null;
        var mf = r.GetComponent<MeshFilter>();
        if (mf != null) m = mf.sharedMesh;
        else { var sm = r as SkinnedMeshRenderer; if (sm != null) m = sm.sharedMesh; }
        if (m == null) continue;
        var b = r.bounds;
        infos.Add((r, m, b, GetPath(r.transform, root.transform)));
    }
    if (infos.Count == 0) { PrefabUtility.UnloadPrefabContents(root); continue; }

    // 整体包围盒
    var all = new Bounds();
    bool first = true;
    foreach (var i in infos)
    {
        if (first) { all = i.b; first = false; } else all.Encapsulate(i.b);
    }
    float H = all.size.y;
    int totalVerts = 0;
    foreach (var i in infos) totalVerts += i.m.vertexCount;

    sb.AppendLine($"=== {pn} ===  渲染器={infos.Count} 总顶点={totalVerts} 整体高度={H:F3}");
    sb.AppendLine($"  整体包围盒 size=({all.size.x:F2},{all.size.y:F2},{all.size.z:F2})");

    var toDelete = new List<(Renderer r, string reason)>();
    foreach (var i in infos)
    {
        var s = i.b.size;
        float thin = Mathf.Min(s.x, Mathf.Min(s.y, s.z));
        float thick = Mathf.Max(s.x, Mathf.Max(s.y, s.z));

        // 判据 A：平面片
        bool isPlane = thin < H * 0.02f && s.x > H * 0.30f && s.z > H * 0.30f;
        // 判据 B：孤立小片（顶点极少 + 与主体不重叠）
        bool isTiny = i.m.vertexCount < Mathf.Max(20, totalVerts * 0.002f);
        bool disjoint = !all.Intersects(i.b) || (i.b.min.y > all.center.y + H * 0.35f);

        sb.AppendLine($"    [{i.path}] 顶点={i.m.vertexCount} size=({s.x:F2},{s.y:F2},{s.z:F2}) thin={thin:F3} " +
                      $"plane={isPlane} tiny={isTiny} disjoint={disjoint}");

        if (isPlane && i.m.vertexCount < totalVerts * 0.35f)
            toDelete.Add((i.r, $"平面片(薄{thin:F3} 面积{s.x:F1}x{s.z:F1})"));
        else if (isTiny && disjoint)
            toDelete.Add((i.r, $"孤立小片(顶点{i.m.vertexCount})"));
    }

    foreach (var (r, reason) in toDelete)
    {
        sb.AppendLine($"    ==> 删除 {r.name} 原因: {reason}");
        UnityEngine.Object.DestroyImmediate(r.gameObject);
    }

    PrefabUtility.SaveAsPrefabAsset(root, pp);
    PrefabUtility.UnloadPrefabContents(root);
    sb.AppendLine($"  共删 {toDelete.Count} 个");
    sb.AppendLine();
}

AssetDatabase.SaveAssets();
AssetDatabase.Refresh();
Directory.CreateDirectory("Tools/reports");
File.WriteAllText("Tools/reports/z15_clean.txt", sb.ToString(), Encoding.UTF8);
return "WROTE";

string GetPath(Transform t, Transform rootT)
{
    var s = new List<string>();
    while (t != null && t != rootT) { s.Insert(0, t.name); t = t.parent; }
    return string.Join("/", s);
}
