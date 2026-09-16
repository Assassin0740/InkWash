// z16_clean2 —— 精确清理 + 用「网格本地包围盒」重构尺寸归一化
//
// 教训：
//   · SkinnedMeshRenderer.bounds / sharedMesh.vertices × localToWorldMatrix 会随 bindpose 与
//     当前姿态变化 ⇒ 两次测同一个 prefab 得到 9.00 和 4.43 两个答案（差 2 倍）
//   · 所以「归一化依据」必须选一个**姿态无关**的量：用 sharedMesh.bounds（本地空间，bindpose 固定）
//     乘以 renderer 的 lossyScale 得到稳定的参考尺寸
//
// 同时精确删除已确认的垃圾（山怪平面 / 不死兵头顶碎片）
using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

var sb = new StringBuilder();
const string PD = "Assets/_Project/Prefabs/Ziyuan";

// ---------- 通用：姿态无关的参考尺寸 ----------
// 用 mesh.bounds（本地 + bindpose 固定）× 各级 scale 的累积
Vector3 StableSize(GameObject go)
{
    var lo = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
    var hi = new Vector3(float.MinValue, float.MinValue, float.MinValue);
    bool any = false;
    foreach (var r in go.GetComponentsInChildren<Renderer>())
    {
        Mesh m = null;
        var mf = r.GetComponent<MeshFilter>();
        if (mf != null) m = mf.sharedMesh;
        else { var sm = r as SkinnedMeshRenderer; if (sm != null) m = sm.sharedMesh; }
        if (m == null) continue;

        var mb = m.bounds;                       // 本地 bindpose 包围盒
        var ls = r.transform.lossyScale;
        // 把本地包围盒 8 角点按 lossyScale 变换
        for (int i = 0; i < 8; i++)
        {
            float x = (i & 1) == 0 ? mb.min.x : mb.max.x;
            float y = (i & 2) == 0 ? mb.min.y : mb.max.y;
            float z = (i & 4) == 0 ? mb.min.z : mb.max.z;
            var p = Vector3.Scale(new Vector3(x, y, z), ls);
            lo = Vector3.Min(lo, p); hi = Vector3.Max(hi, p);
            any = true;
        }
    }
    if (!any) return Vector3.one;
    return hi - lo;
}

Vector3 StableMin(GameObject go)
{
    var lo = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
    bool any = false;
    foreach (var r in go.GetComponentsInChildren<Renderer>())
    {
        Mesh m = null;
        var mf = r.GetComponent<MeshFilter>();
        if (mf != null) m = mf.sharedMesh;
        else { var sm = r as SkinnedMeshRenderer; if (sm != null) m = sm.sharedMesh; }
        if (m == null) continue;
        var mb = m.bounds;
        var ls = r.transform.lossyScale;
        for (int i = 0; i < 8; i++)
        {
            float x = (i & 1) == 0 ? mb.min.x : mb.max.x;
            float y = (i & 2) == 0 ? mb.min.y : mb.max.y;
            float z = (i & 4) == 0 ? mb.min.z : mb.max.z;
            var p = Vector3.Scale(new Vector3(x, y, z), ls);
            lo = Vector3.Min(lo, p);
            any = true;
        }
    }
    return any ? lo : Vector3.zero;
}

// ---------- 1. 精确删除垃圾 ----------
// nameContains -> prefab 名
var kills = new (string prefab, string[] names, string why)[]
{
    ("Z_Orge",   new[] { "Plane.001_Material_0" }, "Sketchfab 地面底座平面"),
    ("Z_Undead", new[] { "Object_4","Object_6","Object_8","Object_10","Object_12","Object_14","Object_16" },
                 "头顶悬空碎片（32 顶点小片 ×7）"),
};

sb.AppendLine("=== 精确清理 ===");
foreach (var k in kills)
{
    string pp = PD + "/" + k.prefab + ".prefab";
    var root = PrefabUtility.LoadPrefabContents(pp);
    // 用「路径尾名」匹配
    var all = root.GetComponentsInChildren<Transform>(true);
    int hit = 0;
    foreach (var t in all)
    {
        if (t == root.transform) continue;
        if (Array.IndexOf(k.names, t.name) < 0) continue;
        sb.AppendLine($"  [{k.prefab}] 删 {t.name}  ({k.why})");
        UnityEngine.Object.DestroyImmediate(t.gameObject);
        hit++;
    }
    PrefabUtility.SaveAsPrefabAsset(root, pp);
    PrefabUtility.UnloadPrefabContents(root);
    sb.AppendLine($"  => {k.prefab} 共删 {hit} 个");
}
sb.AppendLine();

// ---------- 2. 用「姿态无关尺寸」重新归一化 ----------
var defs = new (string name, float target, char axis, string note)[]
{
    ("Z_Orge",      2.60f, 'y', "山怪"),
    ("Z_Orc",       2.05f, 'y', "兽人兵"),
    ("Z_Undead",    1.95f, 'y', "不死兵"),
    ("Z_Dragon",    9.00f, 'z', "Boss龙 体长"),
    ("Z_Dragon_LP", 7.00f, 'z', "静态龙 体长"),
};

sb.AppendLine("=== 姿态无关归一化 ===");
foreach (var d in defs)
{
    string pp = PD + "/" + d.name + ".prefab";
    var root = PrefabUtility.LoadPrefabContents(pp);
    root.transform.localPosition = Vector3.zero;
    root.transform.localRotation = Quaternion.identity;
    root.transform.localScale = Vector3.one;

    var s0 = StableSize(root);
    float cur = d.axis == 'y' ? s0.y : s0.z;
    float k = cur > 1e-5f ? d.target / cur : 1f;
    root.transform.localScale = Vector3.one * k;

    var s1 = StableSize(root);
    var mn = StableMin(root);

    sb.AppendLine($"{d.name} ({d.note}):");
    sb.AppendLine($"   归一前 size=({s0.x:F3},{s0.y:F3},{s0.z:F3}) {d.axis}={cur:F3}");
    sb.AppendLine($"   k={k:F4}");
    sb.AppendLine($"   归一后 size=({s1.x:F3},{s1.y:F3},{s1.z:F3})  底部min=({mn.x:F3},{mn.y:F3},{mn.z:F3})");

    PrefabUtility.SaveAsPrefabAsset(root, pp);
    PrefabUtility.UnloadPrefabContents(root);
}

AssetDatabase.SaveAssets();
AssetDatabase.Refresh();
Directory.CreateDirectory("Tools/reports");
File.WriteAllText("Tools/reports/z16_clean2.txt", sb.ToString(), Encoding.UTF8);
return "WROTE";
