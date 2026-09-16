// z4_fix —— 1) 清掉 mountain_orge 的 T-Pose 空动画警告  2) 试给龙的骨架做 Humanoid（预期失败，但要知道原因）
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

var sb = new StringBuilder();
sb.AppendLine("=== z4 修正与探索 ===");
sb.AppendLine();

// ---- 1. mountain_orge: 排除 T-Pose 片段 ----
string p1 = "Assets/ThirdParty/Ziyuan/_fbx/mountain_orge.fbx";
var imp1 = AssetImporter.GetAtPath(p1) as ModelImporter;
if (imp1 != null)
{
    var clips = new System.Collections.Generic.List<ModelImporterClipAnimation>(imp1.clipAnimations);
    var kept = new System.Collections.Generic.List<ModelImporterClipAnimation>();
    foreach (var c in clips)
    {
        if (c.name.Contains("T-Pose")) { sb.AppendLine($"  [mountain_orge] 排除片段 '{c.name}'"); continue; }
        kept.Add(c);
    }
    imp1.clipAnimations = kept.ToArray();
    imp1.SaveAndReimport();
    sb.AppendLine($"  [mountain_orge] 片段 {clips.Count} -> {kept.Count}，已重导入");
}
sb.AppendLine();

// ---- 2. 龙：试设 Humanoid，看 Unity 怎么说 ----
string p2 = "Assets/ThirdParty/Ziyuan/_fbx/chinese_dragon.fbx";
var imp2 = AssetImporter.GetAtPath(p2) as ModelImporter;
if (imp2 != null)
{
    sb.AppendLine($"  [chinese_dragon] before animationType={imp2.animationType}");
    imp2.animationType = ModelImporterAnimationType.Human;
    imp2.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
    imp2.SaveAndReimport();

    var go2 = AssetDatabase.LoadAssetAtPath<GameObject>(p2);
    var an2 = go2 != null ? go2.GetComponentInChildren<Animator>() : null;
    var av2 = an2 != null ? an2.avatar : null;
    if (av2 == null) sb.AppendLine("  [chinese_dragon] avatar=null（Humanoid 失败）");
    else
    {
        sb.AppendLine($"  [chinese_dragon] avatar isHuman={av2.isHuman} isValid={av2.isValid}");
        var hb = av2.humanDescription.human;
        var missing = new StringBuilder();
        int filled = 0;
        foreach (var h in hb) { if (string.IsNullOrEmpty(h.boneName)) missing.Append(h.humanName + " "); else filled++; }
        sb.AppendLine($"  [chinese_dragon] 已映射 {filled} 个骨骼");
        sb.AppendLine($"  [chinese_dragon] 缺失: {missing}");
    }
}
sb.AppendLine();

// ---- 3. 回读确认 ----
foreach (var n in new[] { "mountain_orge", "chinese_dragon" })
{
    string p = "Assets/ThirdParty/Ziyuan/_fbx/" + n + ".fbx";
    var im = AssetImporter.GetAtPath(p) as ModelImporter;
    int real = 0;
    foreach (var o in AssetDatabase.LoadAllAssetsAtPath(p))
    {
        var c = o as AnimationClip;
        if (c == null || c.name.StartsWith("__preview__")) continue;
        real++;
    }
    sb.AppendLine($"  复核 {n}: type={im?.animationType} clips={im?.clipAnimations.Length} 实际={real}");
}

Directory.CreateDirectory("Tools/reports");
File.WriteAllText("Tools/reports/z4_fix.txt", sb.ToString(), Encoding.UTF8);
return "WROTE";
