// z2_humanoid —— 尝试把三个怪设成 Humanoid，并报告自动映射结果
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

var sb = new StringBuilder();
string root = "Assets/ThirdParty/Ziyuan/_fbx";
string[] names = { "mountain_orge", "low-poly_orc", "cursed_undead_soldier_rig" };

foreach (var n in names)
{
    string p = root + "/" + n + ".fbx";
    var imp = AssetImporter.GetAtPath(p) as ModelImporter;
    if (imp == null) { sb.AppendLine($"[{n}] no importer"); continue; }

    sb.AppendLine($"=== {n} ===");
    sb.AppendLine($"  before: animationType={imp.animationType}");

    imp.animationType = ModelImporterAnimationType.Human;
    imp.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
    imp.SaveAndReimport();

    var go = AssetDatabase.LoadAssetAtPath<GameObject>(p);
    var anim = go != null ? go.GetComponentInChildren<Animator>() : null;
    if (anim == null || anim.avatar == null)
    {
        sb.AppendLine("  !! 没有 avatar");
        continue;
    }

    var av = anim.avatar;
    sb.AppendLine($"  avatar={av.name} isHuman={av.isHuman} isValid={av.isValid}");

    if (av.isHuman)
    {
        // 检查关键骨骼
        string[] keys = { "Hips", "Spine", "Head", "LeftUpperArm", "RightUpperArm", "LeftUpperLeg", "RightUpperLeg" };
        var bones = new HumanBone[] { };
        int ok = 0;
        foreach (var k in keys)
        {
            var hb = av.humanDescription.human;
            foreach (var h in hb)
                if (h.humanName == k) { ok++; sb.AppendLine($"    {k} <- {h.boneName}"); break; }
        }
        sb.AppendLine($"  关键骨骼命中 {ok}/7");
    }
    else
    {
        // 报告哪个骨骼缺失
        sb.AppendLine("  !! isHuman=false，缺骨骼：");
        var hb = av.humanDescription.human;
        var missing = new StringBuilder();
        foreach (var h in hb)
            if (string.IsNullOrEmpty(h.boneName)) missing.Append(h.humanName + " ");
        sb.AppendLine("    " + missing);
    }
    sb.AppendLine();
}

Directory.CreateDirectory("Tools/reports");
File.WriteAllText("Tools/reports/z2_humanoid.txt", sb.ToString(), Encoding.UTF8);
return sb.ToString();
