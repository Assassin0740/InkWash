// 把 Universal Base Characters 的 2 个基础体型设为 Humanoid 并生成 Avatar。
// 原因：它们是 Generic，而 UAL2 的 86 条动画是 Humanoid —— 不映射则一条都传不到主角身上。
// 说明：Monster 包（50 个）自带 1026 条动画，是自足的，不强行人形化
//       （Dog/Chicken/Dragon 之类四足/飞行体强行映射只会刷警告）。
// 注意：团结引擎的枚举是 Human，不是国际版的 Humanoid。
var targets = new string[] {
    "Assets/ThirdParty/Quaternius/UniversalBaseCharacters/Base Characters/Unity/Superhero_Male_FullBody.fbx",
    "Assets/ThirdParty/Quaternius/UniversalBaseCharacters/Base Characters/Unity/Superhero_Female_FullBody.fbx",
};

var sb = new System.Text.StringBuilder();
int ok = 0, fail = 0;

foreach (var p in targets)
{
    string fn = System.IO.Path.GetFileName(p);
    var mi = UnityEditor.AssetImporter.GetAtPath(p) as UnityEditor.ModelImporter;
    if (mi == null) { sb.AppendLine("✗ " + fn + "  找不到 ModelImporter（路径错？）"); fail++; continue; }

    try
    {
        mi.animationType = UnityEditor.ModelImporterAnimationType.Human;
        mi.avatarSetup = UnityEditor.ModelImporterAvatarSetup.CreateFromThisModel;
        mi.importAnimation = true;
        mi.SaveAndReimport();

        string human = "?";
        int bones = 0;
        foreach (var a in UnityEditor.AssetDatabase.LoadAllAssetsAtPath(p))
        {
            var av = a as UnityEngine.Avatar;
            if (av != null)
            {
                human = av.isHuman ? "isHuman=TRUE" : "isHuman=FALSE(映射失败)";
                bones = av.humanDescription.skeleton != null ? av.humanDescription.skeleton.Length : 0;
            }
        }
        if (human == "?") { human = "无 Avatar"; fail++; } else { ok++; }
        sb.AppendLine("✓ " + fn + "  →  " + human + "  映射骨骼数=" + bones);
    }
    catch (System.Exception e)
    {
        sb.AppendLine("✗ " + fn + "  异常: " + e.Message);
        fail++;
    }
}

UnityEditor.AssetDatabase.Refresh();

// 复核：这三个角色现在是否都能吃 UAL2 的动画
sb.AppendLine("");
sb.AppendLine("--- 动画源与角色的人形状态复核 ---");
string[] check = {
    "Assets/ThirdParty/Quaternius/UniversalAnimationLibrary2/Unity/UAL2_Standard.fbx",
    "Assets/ThirdParty/Quaternius/UniversalBaseCharacters/Base Characters/Unity/Superhero_Male_FullBody.fbx",
    "Assets/ThirdParty/Quaternius/UniversalBaseCharacters/Base Characters/Unity/Superhero_Female_FullBody.fbx",
    "Assets/ThirdParty/Quaternius/ModularCharacterOutfits-Fantasy/Modular Character Outfits - Fantasy[Standard]/Exports/FBX (Unity)/Outfits/Male_Ranger.fbx",
};
foreach (var p in check)
{
    string state = "无 Avatar";
    int clips = 0;
    foreach (var a in UnityEditor.AssetDatabase.LoadAllAssetsAtPath(p))
    {
        var av = a as UnityEngine.Avatar;
        if (av != null) state = av.isHuman ? "Human" : "非 Human";
        if (a is UnityEngine.AnimationClip) clips++;
    }
    sb.AppendLine("   " + System.IO.Path.GetFileName(p) + "  →  " + state + "  剪辑=" + clips);
}

sb.AppendLine("");
sb.AppendLine("完成：成功 " + ok + " / 失败 " + fail);
return sb.ToString();
