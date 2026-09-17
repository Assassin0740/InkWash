// 试验：把 KayKit 角色 FBX 切成 Humanoid，看 Unity 自动映射认不认 .l/.r 命名
using UnityEditor;
using UnityEngine;

var sb = new System.Text.StringBuilder();
string p = "Assets/ThirdParty/KayKit/Adventurers/Characters/Rogue_Hooded.fbx";

var imp = AssetImporter.GetAtPath(p) as ModelImporter;
imp.animationType = ModelImporterAnimationType.Human;
imp.importAnimation = true;
imp.SaveAndReimport();

// 重新取 importer（reimport 后引用会变）
imp = AssetImporter.GetAtPath(p) as ModelImporter;
sb.AppendLine("animationType = " + imp.animationType);

var hd = imp.humanDescription;
if (hd.human != null)
{
    sb.AppendLine("human 映射条目数 = " + hd.human.Length);
    sb.AppendLine("=== 已映射的 Humanoid 骨 ===");
    int mapped = 0;
    foreach (var h in hd.human)
    {
        if (!string.IsNullOrEmpty(h.boneName)) mapped++;
        sb.AppendLine(string.Format("   {0,-22} -> {1}", h.humanName, string.IsNullOrEmpty(h.boneName) ? "(空)" : h.boneName));
    }
    sb.AppendLine("非空映射数 = " + mapped);
}

var objs = AssetDatabase.LoadAllAssetsAtPath(p);
foreach (var o in objs)
{
    var av = o as Avatar;
    if (av != null)
        sb.AppendLine(string.Format("Avatar={0} isHuman={1} isValid={2}", av.name, av.isHuman, av.isValid));
}
return sb.ToString();
