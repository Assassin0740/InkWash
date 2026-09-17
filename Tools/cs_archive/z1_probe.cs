// z1_probe —— 体检新导入的 Ziyuan 素材：骨架、动画、Humanoid 可行性
// 落盘到 Tools/reports/z1_probe.txt（桥会截断长 return）
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

var sb = new StringBuilder();
string root = "Assets/ThirdParty/Ziyuan/_fbx";

sb.AppendLine("=== ZIYUAN FBX 体检 ===");
sb.AppendLine("path: " + root);
sb.AppendLine();

string[] names = { "mountain_orge", "low-poly_orc", "cursed_undead_soldier_rig", "lowpoly_textured_chinese_dragon" };

foreach (var n in names)
{
    string p = root + "/" + n + ".fbx";
    var imp = AssetImporter.GetAtPath(p) as ModelImporter;
    if (imp == null) { sb.AppendLine($"[{n}] !! 没有 ModelImporter（导入失败？）"); continue; }

    sb.AppendLine($"--- {n} ---");
    sb.AppendLine($"  animationType = {imp.animationType}");
    sb.AppendLine($"  clips = {imp.clipAnimations.Length}");

    var go = AssetDatabase.LoadAssetAtPath<GameObject>(p);
    if (go == null) { sb.AppendLine("  !! LoadAssetAtPath<GameObject> 返回 null"); continue; }

    // 骨架
    var anim = go.GetComponentInChildren<Animator>();
    if (anim != null)
    {
        sb.AppendLine($"  Animator: avatar(null)={(anim.avatar == null)}");
        var desc = anim.avatar != null ? anim.avatar : null;
    }
    var skin = go.GetComponentInChildren<SkinnedMeshRenderer>();
    if (skin != null && skin.bones != null && skin.bones.Length > 0)
    {
        var b0 = skin.bones[0];
        sb.AppendLine($"  bones = {skin.bones.Length}");
        sb.AppendLine($"  bone[0] = {b0.name}");
        // 采样几个骨骼名判断骨架类型
        var sb2 = new StringBuilder();
        int show = Mathf.Min(skin.bones.Length, 6);
        for (int i = 0; i < show; i++) sb2.Append(skin.bones[i].name + " ");
        // 末几个
        for (int i = Mathf.Max(0, skin.bones.Length - 4); i < skin.bones.Length; i++) sb2.Append("| " + skin.bones[i].name);
        sb.AppendLine($"  bone names: {sb2}");
        // 是否含 mixamorig / Bip001
        bool mixamo = false, bip = false, ik = false;
        foreach (var b in skin.bones)
        {
            string bn = b.name;
            if (bn.StartsWith("mixamorig")) mixamo = true;
            if (bn.StartsWith("Bip001")) bip = true;
            if (bn.Contains("IK") || bn.Contains("POLE") || bn.Contains("Target")) ik = true;
        }
        sb.AppendLine($"  骨架风格: mixamorig={mixamo} Bip001={bip} 含IK控制骨={ik}");
    }

    // 动画片段
    var clips = AssetDatabase.LoadAllAssetsAtPath(p);
    int clipCount = 0;
    foreach (var o in clips)
    {
        var c = o as AnimationClip;
        if (c == null) continue;
        clipCount++;
        int nc = 0;
        try { nc = AnimationUtility.GetCurveBindings(c).Length; } catch { nc = -1; }
        sb.AppendLine($"    clip '{c.name}' len={c.length:F3}s frames={Mathf.RoundToInt(c.length * c.frameRate)} rate={c.frameRate} legacy={c.legacy} curves={nc}");
    }
    sb.AppendLine($"  => 实际片段数 = {clipCount}");

    // 网格三角面
    if (skin != null)
    {
        int tris = 0;
        foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
            if (mf.sharedMesh != null) tris += mf.sharedMesh.triangles.Length / 3;
        foreach (var sm in go.GetComponentsInChildren<SkinnedMeshRenderer>())
            if (sm.sharedMesh != null) tris += sm.sharedMesh.triangles.Length / 3;
        sb.AppendLine($"  三角面 = {tris}");
    }
    sb.AppendLine();
}

// 龙的进度
string dragonFbx = root + "/chinese_dragon.fbx";
sb.AppendLine("=== 龙的 FBX 是否已落地 ===");
sb.AppendLine(File.Exists(dragonFbx) ? "  YES 已存在" : "  NO 还没拷进来");

string rp = "Tools/reports/z1_probe.txt";
Directory.CreateDirectory("Tools/reports");
File.WriteAllText(rp, sb.ToString(), Encoding.UTF8);
return "WROTE " + rp + "\n" + sb.ToString();
