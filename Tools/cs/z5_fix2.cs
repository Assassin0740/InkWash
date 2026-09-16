// z5_fix2 —— 龙改回 Generic；mountain_orge 用显式 clipAnimations 排除 T-Pose
using System.IO;
using System.Text;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

var sb = new StringBuilder();
sb.AppendLine("=== z5 修正 v2 ===");
sb.AppendLine();

// ---- 1. 龙：改回 Generic，拿回动画 ----
string pDragon = "Assets/ThirdParty/Ziyuan/_fbx/chinese_dragon.fbx";
var impD = AssetImporter.GetAtPath(pDragon) as ModelImporter;
if (impD != null)
{
    impD.animationType = ModelImporterAnimationType.Generic;
    impD.avatarSetup = ModelImporterAvatarSetup.NoAvatar;
    impD.SaveAndReimport();
    sb.AppendLine("[chinese_dragon] 已改回 Generic + NoAvatar");
}
sb.AppendLine();

// ---- 2. mountain_orge：显式重建 clipAnimations（排除 T-Pose）----
string pOrge = "Assets/ThirdParty/Ziyuan/_fbx/mountain_orge.fbx";
var impO = AssetImporter.GetAtPath(pOrge) as ModelImporter;
if (impO != null)
{
    var src = impO.defaultClipAnimations;
    sb.AppendLine($"[mountain_orge] defaultClipAnimations = {src.Length}");
    var kept = new List<ModelImporterClipAnimation>();
    foreach (var c in src)
    {
        if (c.name.Contains("T-Pose")) { sb.AppendLine($"  排除 '{c.name}'"); continue; }
        kept.Add(c);
    }
    impO.clipAnimations = kept.ToArray();
    impO.SaveAndReimport();
    sb.AppendLine($"[mountain_orge] 片段 {src.Length} -> {kept.Count}");
}
sb.AppendLine();

// ---- 3. 复核 ----
sb.AppendLine("=== 复核 ===");
foreach (var n in new[] { "mountain_orge", "low-poly_orc", "cursed_undead_soldier_rig", "chinese_dragon" })
{
    string p = "Assets/ThirdParty/Ziyuan/_fbx/" + n + ".fbx";
    var im = AssetImporter.GetAtPath(p) as ModelImporter;
    var go = AssetDatabase.LoadAssetAtPath<GameObject>(p);
    var an = go != null ? go.GetComponentInChildren<Animator>() : null;
    var av = an != null ? an.avatar : null;
    int real = 0; var names = new List<string>();
    foreach (var o in AssetDatabase.LoadAllAssetsAtPath(p))
    {
        var c = o as AnimationClip;
        if (c == null || c.name.StartsWith("__preview__")) continue;
        real++; names.Add(c.name);
    }
    sb.AppendLine($"{n}:");
    sb.AppendLine($"   type={im?.animationType} avatar={(av == null ? "null" : "isHuman=" + av.isHuman)} 片段={real}");
    foreach (var nm in names) sb.AppendLine($"     - {nm}");
}

Directory.CreateDirectory("Tools/reports");
File.WriteAllText("Tools/reports/z5_fix2.txt", sb.ToString(), Encoding.UTF8);
return "WROTE";
