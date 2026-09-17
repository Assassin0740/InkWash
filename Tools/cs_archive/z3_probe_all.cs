// z3_probe_all —— 全部 5 个 Ziyuan 素材完整体检（含龙）
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

var sb = new StringBuilder();
string root = "Assets/ThirdParty/Ziyuan/_fbx";
string[] names = { "mountain_orge", "low-poly_orc", "cursed_undead_soldier_rig", "lowpoly_textured_chinese_dragon", "chinese_dragon" };

sb.AppendLine("=== ZIYUAN 素材完整体检 (z3) ===");
sb.AppendLine();

foreach (var n in names)
{
    string p = root + "/" + n + ".fbx";
    if (!File.Exists(p)) { sb.AppendLine($"[{n}] 文件不存在"); continue; }

    var go = AssetDatabase.LoadAssetAtPath<GameObject>(p);
    if (go == null) { sb.AppendLine($"[{n}] !! 加载失败"); continue; }

    var imp = AssetImporter.GetAtPath(p) as ModelImporter;
    sb.AppendLine($"--- {n} ---");
    sb.AppendLine($"  animationType = {imp?.animationType}");

    var anim = go.GetComponentInChildren<Animator>();
    var av = anim != null ? anim.avatar : null;
    sb.AppendLine($"  avatar = {(av == null ? "null" : av.name + " isHuman=" + av.isHuman + " isValid=" + av.isValid)}");

    var skin = go.GetComponentInChildren<SkinnedMeshRenderer>();
    if (skin != null && skin.bones != null)
        sb.AppendLine($"  bones = {skin.bones.Length}  bone[0]={skin.bones[0].name}");

    int tris = 0;
    foreach (var sm in go.GetComponentsInChildren<SkinnedMeshRenderer>())
        if (sm.sharedMesh != null) tris += sm.sharedMesh.triangles.Length / 3;
    foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
        if (mf.sharedMesh != null) tris += mf.sharedMesh.triangles.Length / 3;
    sb.AppendLine($"  triangles = {tris}");

    // 网格数 & 材质数
    var smrs = go.GetComponentsInChildren<SkinnedMeshRenderer>();
    sb.AppendLine($"  skinnedMeshRenderers = {smrs.Length}");
    int matCount = 0;
    foreach (var s in smrs) if (s.sharedMaterials != null) matCount += s.sharedMaterials.Length;
    sb.AppendLine($"  材质槽总数 = {matCount}");

    // 动画片段（排除 __preview__）
    int real = 0;
    foreach (var o in AssetDatabase.LoadAllAssetsAtPath(p))
    {
        var c = o as AnimationClip;
        if (c == null) continue;
        if (c.name.StartsWith("__preview__")) continue;
        real++;
        sb.AppendLine($"    AUDIO? clip '{c.name}' {c.length:F2}s");
    }
    sb.AppendLine($"  => 非preview片段数 = {real}");
    sb.AppendLine();
}

Directory.CreateDirectory("Tools/reports");
File.WriteAllText("Tools/reports/z3_probe_all.txt", sb.ToString(), Encoding.UTF8);
return "WROTE Tools/reports/z3_probe_all.txt";
