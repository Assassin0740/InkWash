// 1) 把 Female_Peasant 退回 Generic（它的骨架缺 Head 骨骼 —— 服装包刻意去掉了头部，
//    因为按包内 Readme，头部应取用 Base Character 的。人形映射对它本身就无意义，
//    留着只会每次重导入都刷一条警告）。
// 2) 输出最终素材清点表，作为本次整理的验收依据。
var fix = "Assets/ThirdParty/Quaternius/ModularCharacterOutfits-Fantasy/Modular Character Outfits - Fantasy[Standard]/Exports/FBX (Unity)/Outfits/Female_Peasant.fbx";
var mi = UnityEditor.AssetImporter.GetAtPath(fix) as UnityEditor.ModelImporter;
if (mi != null)
{
    mi.animationType = UnityEditor.ModelImporterAnimationType.Generic;
    mi.avatarSetup = UnityEditor.ModelImporterAvatarSetup.NoAvatar;
    mi.SaveAndReimport();
}

UnityEditor.AssetDatabase.Refresh();

var sb = new System.Text.StringBuilder();
sb.AppendLine("=== 最终清点：Assets/ThirdParty 下所有模型 ===");
sb.AppendLine();

string[] dirs = new string[] {
    "Assets/ThirdParty/Quaternius/Bestiary-DungeonMonsters",
    "Assets/ThirdParty/Quaternius/UniversalAnimationLibrary2",
    "Assets/ThirdParty/Quaternius/ModularCharacterOutfits-Fantasy",
};

int totalFbx = 0, humanCount = 0, clipsTotal = 0;
foreach (var d in dirs)
{
    sb.AppendLine("[ " + System.IO.Path.GetFileName(d) + " ]");
    foreach (var g in UnityEditor.AssetDatabase.FindAssets("t:Model", new string[] { d }))
    {
        var p = UnityEditor.AssetDatabase.GUIDToAssetPath(g);
        if (!p.ToLower().EndsWith(".fbx")) continue;
        totalFbx++;

        string human = "Generic";
        int clips = 0;
        foreach (var a in UnityEditor.AssetDatabase.LoadAllAssetsAtPath(p))
        {
            var av = a as UnityEngine.Avatar;
            if (av != null && av.isHuman) human = "Human ✓";
            if (a is UnityEngine.AnimationClip) clips++;
        }
        clipsTotal += clips;
        if (human.StartsWith("Human")) humanCount++;

        sb.AppendLine(string.Format("   {0,-42} {1,-9} 剪辑={2}",
            System.IO.Path.GetFileName(p), human, clips));
    }
    sb.AppendLine();
}

sb.AppendLine("合计: 模型 " + totalFbx + " 个 | 人形可用 " + humanCount + " 个 | 动画剪辑共 " + clipsTotal + " 条");
return sb.ToString();
