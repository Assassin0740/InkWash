// z10_measure —— 用「网格顶点真值」量 5 个 prefab 的真实尺寸（不信 Renderer.bounds）
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

var sb = new StringBuilder();
const string PD = "Assets/_Project/Prefabs/Ziyuan";

// 顶点真值包围盒。对 SkinnedMesh，用 sharedMesh 的 vertices + transform 矩阵
// （bindpose 空间；对静态网格就是真值）
Bounds VertexBounds(GameObject go)
{
    var b = new Bounds();
    bool first = true;
    foreach (var r in go.GetComponentsInChildren<Renderer>())
    {
        Mesh m = null;
        var mf = r.GetComponent<MeshFilter>();
        if (mf != null) m = mf.sharedMesh;
        else
        {
            var sm = r as SkinnedMeshRenderer;
            if (sm != null) m = sm.sharedMesh;
        }
        if (m == null) continue;
        var xf = r.transform.localToWorldMatrix;
        var verts = m.vertices;
        foreach (var v in verts)
        {
            var w = xf.MultiplyPoint3x4(v);
            if (first) { b = new Bounds(w, Vector3.zero); first = false; }
            else b.Encapsulate(w);
        }
    }
    return b;
}

Bounds BoundsBounds(GameObject go)
{
    var b = new Bounds();
    bool first = true;
    foreach (var r in go.GetComponentsInChildren<Renderer>())
    {
        if (first) { b = r.bounds; first = false; }
        else b.Encapsulate(r.bounds);
    }
    return b;
}

string[] names = { "Z_Orge", "Z_Orc", "Z_Undead", "Z_Dragon", "Z_Dragon_LP" };
foreach (var n in names)
{
    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PD + "/" + n + ".prefab");
    if (prefab == null) { sb.AppendLine($"[{n}] 加载失败"); continue; }
    var go = UnityEngine.Object.Instantiate(prefab) as GameObject;
    go.transform.position = Vector3.zero;
    go.transform.rotation = Quaternion.identity;
    // ★ 不要重置 localScale —— prefab 的缩放就是被测对象本身

    var vb = VertexBounds(go);
    var bb = BoundsBounds(go);
    sb.AppendLine($"--- {n} ---");
    sb.AppendLine($"  顶点真值  : size=({vb.size.x:F3}, {vb.size.y:F3}, {vb.size.z:F3})  min.y={vb.min.y:F3}");
    sb.AppendLine($"  渲染bounds: size=({bb.size.x:F3}, {bb.size.y:F3}, {bb.size.z:F3})  min.y={bb.min.y:F3}");

    UnityEngine.Object.DestroyImmediate(go);
}

Directory.CreateDirectory("Tools/reports");
File.WriteAllText("Tools/reports/z10_measure.txt", sb.ToString(), Encoding.UTF8);
return "WROTE";
