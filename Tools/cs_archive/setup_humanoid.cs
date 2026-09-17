// 把角色/动画类 FBX 的骨骼类型设为 Humanoid，并生成 Avatar。
// 这是动画能否跨模型重定向(retarget)的总开关：
//   Generic + NoAvatar  → 各管各的，UAL2 的 86 个动画传不到角色上
//   Humanoid + Avatar   → Unity 的 Avatar 系统自动做骨骼映射，动画可通用
var targets = new string[] {
    "Assets/ThirdParty/Quaternius/UniversalAnimationLibrary2/Unity/UAL2_Standard.fbx",
    "Assets/ThirdParty/Quaternius/UniversalAnimationLibrary2/Unity/UAL2_Standard_RM.fbx",
    "Assets/ThirdParty/Quaternius/UniversalAnimationLibrary2/Female Mannequin/Unity/Mannequin_F.fbx",
    // 服装：含完整身体的版本可直接当角色用
    "Assets/ThirdParty/Quaternius/ModularCharacterOutfits-Fantasy/Modular Character Outfits - Fantasy[Standard]/Exports/FBX (Unity)/Outfits/Male_Ranger.fbx",
    "Assets/ThirdParty/Quaternius/ModularCharacterOutfits-Fantasy/Modular Character Outfits - Fantasy[Standard]/Exports/FBX (Unity)/Outfits/Female_Ranger.fbx",
    "Assets/ThirdParty/Quaternius/ModularCharacterOutfits-Fantasy/Modular Character Outfits - Fantasy[Standard]/Exports/FBX (Unity)/Outfits/Male_Peasant.fbx",
    "Assets/ThirdParty/Quaternius/ModularCharacterOutfits-Fantasy/Modular Character Outfits - Fantasy[Standard]/Exports/FBX (Unity)/Outfits/Female_Peasant.fbx",
    // 怪物：先试人形映射，失败只是留下警告，不影响其他文件
    "Assets/ThirdParty/Quaternius/Bestiary-DungeonMonsters/Exports/FBX (Unity)/Imp.fbx",
    "Assets/ThirdParty/Quaternius/Bestiary-DungeonMonsters/Exports/FBX (Unity)/Puglin.fbx",
};

var sb = new System.Text.StringBuilder();
int ok = 0, fail = 0;

foreach (var p in targets)
{
    string fn = System.IO.Path.GetFileName(p);
    var mi = UnityEditor.AssetImporter.GetAtPath(p) as UnityEditor.ModelImporter;
    if (mi == null) { sb.AppendLine("✗ " + fn + "  找不到 ModelImporter"); fail++; continue; }

    try
    {
        mi.animationType = UnityEditor.ModelImporterAnimationType.Human;   // 注意：团结引擎是 Human，国际版才是 Humanoid
        mi.avatarSetup = UnityEditor.ModelImporterAvatarSetup.CreateFromThisModel;
        mi.importAnimation = true;
        mi.SaveAndReimport();

        string human = "?";
        int clips = 0;
        foreach (var a in UnityEditor.AssetDatabase.LoadAllAssetsAtPath(p))
        {
            var av = a as UnityEngine.Avatar;
            if (av != null) human = av.isHuman ? "isHuman=TRUE" : "isHuman=FALSE(映射失败)";
            if (a is UnityEngine.AnimationClip) clips++;
        }
        if (human == "?") { human = "无 Avatar"; fail++; } else { ok++; }
        sb.AppendLine("✓ " + fn + "  →  " + human + "  动画剪辑=" + clips);
    }
    catch (System.Exception e)
    {
        sb.AppendLine("✗ " + fn + "  异常: " + e.Message);
        fail++;
    }
}

UnityEditor.AssetDatabase.Refresh();
sb.AppendLine();
sb.AppendLine("完成：成功 " + ok + " / 失败 " + fail);
return sb.ToString();
