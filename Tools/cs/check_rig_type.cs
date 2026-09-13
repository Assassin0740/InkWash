// 检查各 FBX 的骨骼类型与人形 Avatar 状态。
// 这决定了动画能不能在模型之间"重定向"(retarget)：
//   Humanoid + isHuman=true → 可以和 Mixamo / 其他人形动画库互通
//   Generic → 只能用它自己带的动画
var sb = new System.Text.StringBuilder();

string[] paths = new string[] {
    "Assets/ThirdParty/Quaternius/Bestiary-DungeonMonsters/Exports/FBX (Unity)/Imp.fbx",
    "Assets/ThirdParty/Quaternius/Bestiary-DungeonMonsters/Exports/FBX (Unity)/Puglin.fbx",
    "Assets/ThirdParty/Quaternius/ModularCharacterOutfits-Fantasy/Modular Character Outfits - Fantasy[Standard]/Exports/FBX (Unity)/Outfits/Male_Ranger.fbx",
    "Assets/ThirdParty/Quaternius/ModularCharacterOutfits-Fantasy/Modular Character Outfits - Fantasy[Standard]/Exports/FBX (Unity)/Outfits/Female_Ranger.fbx",
    "Assets/ThirdParty/Quaternius/UniversalAnimationLibrary2/Unity/UAL2_Standard.fbx",
    "Assets/ThirdParty/Quaternius/UniversalAnimationLibrary2/Unity/UAL2_Standard_RM.fbx",
    "Assets/ThirdParty/Quaternius/UniversalAnimationLibrary2/Female Mannequin/Unity/Mannequin_F.fbx",
};

sb.AppendLine("文件 | 动画导入 | 骨骼类型 | AvatarSetup | Avatar | isHuman | 剪辑数");
sb.AppendLine("-----|---------|---------|------------|--------|---------|------");

foreach (var p in paths)
{
    var mi = UnityEditor.AssetImporter.GetAtPath(p) as UnityEditor.ModelImporter;
    string fn = System.IO.Path.GetFileName(p);
    if (mi == null) { sb.AppendLine(fn + "  ==> 未找到导入器"); continue; }

    string avatarInfo = "(无)";
    string isHuman = "-";
    foreach (var a in UnityEditor.AssetDatabase.LoadAllAssetsAtPath(p))
    {
        var av = a as UnityEngine.Avatar;
        if (av != null)
        {
            avatarInfo = av.name;
            isHuman = av.isHuman.ToString();
        }
    }

    sb.AppendLine(string.Format("{0} | {1} | {2} | {3} | {4} | {5} | {6}",
        fn,
        mi.importAnimation,
        mi.animationType,
        mi.avatarSetup,
        avatarInfo,
        isHuman,
        (mi.clipAnimations == null ? 0 : mi.clipAnimations.Length)));
}

return sb.ToString();
