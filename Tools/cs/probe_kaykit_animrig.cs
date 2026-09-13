// 探针：动画 FBX 的层级 + 自动映射结果，用来判断是否需要手写 Humanoid 映射
using UnityEditor;
using UnityEngine;

var sb = new System.Text.StringBuilder();
string[] files = {
    "Assets/ThirdParty/KayKit/Animations/Rig_Medium/Rig_Medium_CombatMelee.fbx",
    "Assets/ThirdParty/KayKit/Animations/Rig_Medium/Rig_Medium_MovementBasic.fbx",
    "Assets/ThirdParty/KayKit/Animations/Rig_Medium/Rig_Medium_General.fbx",
};

foreach (var p in files)
{
    sb.AppendLine("========== " + System.IO.Path.GetFileNameWithoutExtension(p) + " ==========");
    var imp = AssetImporter.GetAtPath(p) as ModelImporter;
    if (imp == null) { sb.AppendLine("   [X] 无 importer"); continue; }
    sb.AppendLine("animationType = " + imp.animationType + "  节点数 = " + imp.transformPaths.Length);

    // 打印层级（只到第 4 层，够看骨架了）
    var seen = new System.Collections.Generic.HashSet<string>();
    foreach (var t in imp.transformPaths)
    {
        int depth = t.Split('/').Length;
        if (depth <= 4 && seen.Add(t)) sb.AppendLine("   " + t);
    }

    // 切成 Humanoid 看自动映射
    imp.animationType = ModelImporterAnimationType.Human;
    imp.SaveAndReimport();
    imp = AssetImporter.GetAtPath(p) as ModelImporter;
    var hd = imp.humanDescription;
    sb.AppendLine("   --- 自动映射 ---");
    if (hd.human != null)
        foreach (var h in hd.human)
            if (!string.IsNullOrEmpty(h.boneName))
                sb.AppendLine(string.Format("      {0,-20} -> {1}", h.humanName, h.boneName));
    var objs = AssetDatabase.LoadAllAssetsAtPath(p);
    foreach (var o in objs)
    {
        var av = o as Avatar;
        if (av != null) sb.AppendLine(string.Format("   Avatar isHuman={0} isValid={1}", av.isHuman, av.isValid));
    }
    sb.AppendLine();
}
return sb.ToString();
