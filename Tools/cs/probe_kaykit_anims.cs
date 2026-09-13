// 探针：列出 KayKit 动画库里的全部剪辑名，并核对角色 FBX 的骨架类型
// 目的：确认"近战连招"到底叫什么名字，供之后重接 Animator 状态机用。
var sb = new System.Text.StringBuilder();

string[] animFiles = {
    "Assets/ThirdParty/KayKit/Animations/Rig_Medium/Rig_Medium_General.fbx",
    "Assets/ThirdParty/KayKit/Animations/Rig_Medium/Rig_Medium_MovementBasic.fbx",
    "Assets/ThirdParty/KayKit/Animations/Rig_Medium/Rig_Medium_MovementAdvanced.fbx",
    "Assets/ThirdParty/KayKit/Animations/Rig_Medium/Rig_Medium_CombatMelee.fbx",
    "Assets/ThirdParty/KayKit/Animations/Rig_Medium/Rig_Medium_CombatRanged.fbx",
    "Assets/ThirdParty/KayKit/Animations/Rig_Medium/Rig_Medium_Simulation.fbx",
    "Assets/ThirdParty/KayKit/Animations/Rig_Medium/Rig_Medium_Special.fbx",
    "Assets/ThirdParty/KayKit/Animations/Rig_Medium/Rig_Medium_Tools.fbx",
};

int total = 0;
foreach (var p in animFiles)
{
    var objs = UnityEditor.AssetDatabase.LoadAllAssetsAtPath(p);
    var all = new System.Collections.Generic.List<string>();
    foreach (var o in objs)
    {
        var c = o as UnityEngine.AnimationClip;
        if (c != null && !c.name.StartsWith("__preview__")) all.Add(c.name + "  [" + c.length.ToString("0.00") + "s]");
    }
    all.Sort();
    total += all.Count;
    sb.AppendLine("=== " + System.IO.Path.GetFileNameWithoutExtension(p) + "  (" + all.Count + " 个) ===");
    foreach (var n in all) sb.AppendLine("   " + n);
    sb.AppendLine();
}
sb.AppendLine("合计剪辑: " + total);
sb.AppendLine();

// --- 角色 FBX 的骨架类型 ---
sb.AppendLine("=== 角色 FBX 骨架 ===");
string[] chars = {
    "Assets/ThirdParty/KayKit/Adventurers/Characters/Knight.fbx",
    "Assets/ThirdParty/KayKit/Adventurers/Characters/Rogue_Hooded.fbx",
    "Assets/ThirdParty/KayKit/Adventurers/Characters/Barbarian.fbx",
    "Assets/ThirdParty/KayKit/Skeletons/Characters/Skeleton_Warrior.fbx",
};
foreach (var p in chars)
{
    var imp = UnityEditor.AssetImporter.GetAtPath(p) as UnityEditor.ModelImporter;
    if (imp == null) { sb.AppendLine("   [X] 拿不到 importer: " + p); continue; }
    sb.AppendLine(string.Format("   {0,-20} animationType={1}  bones={2}",
        System.IO.Path.GetFileNameWithoutExtension(p), imp.animationType, imp.transformPaths != null ? imp.transformPaths.Length : -1));
}

return sb.ToString();
