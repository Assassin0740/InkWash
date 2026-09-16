// z11_normalize —— 在 Unity 侧用「顶点真值」把 5 个 prefab 归一化到目标尺寸
//
// 为什么不在 Blender 里做：Blender 的 bindpose 顶点高度 ≠ Unity 里 skinned mesh 的渲染高度，
// 两者差 25%~180%（骨架姿态、蒙皮空间不同）。归一化必须在**同一个坐标系**里做才有意义。
// 这里直接改 prefab 根节点的 localScale —— 所见即所得，没有中间换算。
//
// 同时输出每个模型的「落地偏移」（min.y），供摆位时把脚踩到地面上。
using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

var sb = new StringBuilder();
const string PD = "Assets/_Project/Prefabs/Ziyuan";

Bounds VertexBounds(GameObject go)
{
    var b = new Bounds();
    bool first = true;
    foreach (var r in go.GetComponentsInChildren<Renderer>())
    {
        Mesh m = null;
        var mf = r.GetComponent<MeshFilter>();
        if (mf != null) m = mf.sharedMesh;
        else { var sm = r as SkinnedMeshRenderer; if (sm != null) m = sm.sharedMesh; }
        if (m == null) continue;
        var xf = r.transform.localToWorldMatrix;
        var verts = m.vertices;
        for (int i = 0; i < verts.Length; i++)
        {
            var w = xf.MultiplyPoint3x4(verts[i]);
            if (first) { b = new Bounds(w, Vector3.zero); first = false; }
            else b.Encapsulate(w);
        }
    }
    return b;
}

// 目标：按「主维度」归一
//   · 两足人形 → 按 Y（身高）
//   · 蛇形/趴伏的龙 → 按 Z（体长）
var defs = new (string name, float target, char axis)[]
{
    ("Z_Orge",      2.60f, 'y'),   // 山怪：比主角(1.75)高一头多
    ("Z_Orc",       2.05f, 'y'),   // 兽人兵：比主角略高
    ("Z_Undead",    1.95f, 'y'),   // 不死兵：和主角相当
    ("Z_Dragon",    9.00f, 'z'),   // Boss 龙：按体长 9 m
    ("Z_Dragon_LP", 7.00f, 'z'),   // 静态龙雕塑：体长 7 m
};

var result = new List<string>();

foreach (var d in defs)
{
    string pp = PD + "/" + d.name + ".prefab";
    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(pp);
    if (prefab == null) { sb.AppendLine($"[{d.name}] 加载失败"); continue; }

    // 用 prefab 内容直接改（LoadPrefabContents 是官方推荐的改 prefab 方式）
    var root = PrefabUtility.LoadPrefabContents(pp);
    root.transform.localPosition = Vector3.zero;
    root.transform.localRotation = Quaternion.identity;
    root.transform.localScale = Vector3.one;

    var b0 = VertexBounds(root);
    float cur = d.axis == 'y' ? b0.size.y : b0.size.z;
    float k = cur > 1e-5f ? d.target / cur : 1f;

    root.transform.localScale = Vector3.one * k;

    // 再量一次确认
    var b1 = VertexBounds(root);

    sb.AppendLine($"--- {d.name} ---");
    sb.AppendLine($"  归一前: size=({b0.size.x:F3},{b0.size.y:F3},{b0.size.z:F3})  主维度({d.axis})={cur:F3}");
    sb.AppendLine($"  缩放倍率 k = {k:F4}");
    sb.AppendLine($"  归一后: size=({b1.size.x:F3},{b1.size.y:F3},{b1.size.z:F3})  min.y={b1.min.y:F3}");

    PrefabUtility.SaveAsPrefabAsset(root, pp);
    PrefabUtility.UnloadPrefabContents(root);

    result.Add($"{d.name}: k={k:F4} 高={b1.size.y:F2} 体长={b1.size.z:F2} 落地偏移={-b1.min.y:F3}");
}

AssetDatabase.SaveAssets();
AssetDatabase.Refresh();

sb.AppendLine();
sb.AppendLine("=== 汇总（供摆位用）===");
foreach (var r in result) sb.AppendLine("  " + r);

Directory.CreateDirectory("Tools/reports");
File.WriteAllText("Tools/reports/z11_normalize.txt", sb.ToString(), Encoding.UTF8);
return "WROTE";
