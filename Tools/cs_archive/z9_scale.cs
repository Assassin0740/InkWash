// z9_scale —— 诊断 FBX 层级里的缩放异常（逐节点打印 localScale + 用顶点真值量世界尺寸）
using System.IO;
using System.Text;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

var sb = new StringBuilder();
const string ROOT = "Assets/ThirdParty/Ziyuan/_fbx";

// 用顶点真值算世界包围盒（不信 Renderer.bounds，它会受错误缩放污染）
Bounds VertexBounds(GameObject go)
{
    var b = new Bounds();
    bool first = true;
    foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
    {
        var m = mf.sharedMesh;
        if (m == null) continue;
        var xf = mf.transform.localToWorldMatrix;
        foreach (var v in m.vertices)
        {
            var w = xf.MultiplyPoint3x4(v);
            if (first) { b = new Bounds(w, Vector3.zero); first = false; }
            else b.Encapsulate(w);
        }
    }
    foreach (var sm in go.GetComponentsInChildren<SkinnedMeshRenderer>())
    {
        var m = sm.sharedMesh;
        if (m == null) continue;
        var xf = sm.transform.localToWorldMatrix;
        foreach (var v in m.vertices)
        {
            var w = xf.MultiplyPoint3x4(v);
            if (first) { b = new Bounds(w, Vector3.zero); first = false; }
            else b.Encapsulate(w);
        }
    }
    return b;
}

string[] names = { "mountain_orge", "low-poly_orc", "cursed_undead_soldier_rig", "chinese_dragon", "lowpoly_textured_chinese_dragon" };

foreach (var n in names)
{
    string p = ROOT + "/" + n + ".fbx";
    var src = AssetDatabase.LoadAssetAtPath<GameObject>(p);
    if (src == null) { sb.AppendLine($"[{n}] 加载失败"); continue; }

    var go = UnityEngine.Object.Instantiate(src) as GameObject;
    var vb = VertexBounds(go);
    sb.AppendLine($"=== {n} ===");
    sb.AppendLine($"  顶点真值包围盒: 尺寸={vb.size} (高={vb.size.y:F3})  min={vb.min}  max={vb.max}");

    // 打印前 3 层节点的 localScale
    sb.AppendLine("  层级 localScale:");
    void Walk(Transform t, int depth)
    {
        if (depth > 3) return;
        sb.AppendLine($"    {"".PadLeft(depth * 2)}({depth}) {t.name} localScale={t.localScale} localPos={t.localPosition}");
        for (int i = 0; i < t.childCount && i < 8; i++) Walk(t.GetChild(i), depth + 1);
    }
    Walk(go.transform, 0);

    UnityEngine.Object.DestroyImmediate(go);
    sb.AppendLine();
}

// 再看 FBX 的 ModelImporter 全局缩放
sb.AppendLine("=== ModelImporter 全局缩放 ===");
foreach (var n in names)
{
    string p = ROOT + "/" + n + ".fbx";
    var imp = AssetImporter.GetAtPath(p) as ModelImporter;
    sb.AppendLine($"{n}: globalScale={imp?.globalScale} useFileScale={imp?.useFileScale} fileScale={imp?.fileScale}");
}

Directory.CreateDirectory("Tools/reports");
File.WriteAllText("Tools/reports/z9_scale.txt", sb.ToString(), Encoding.UTF8);
return "WROTE";
