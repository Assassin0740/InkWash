// 探针：列出 KayKit 角色 FBX 的骨骼名，判断能否自动映射成 Unity Humanoid
var sb = new System.Text.StringBuilder();
string p = "Assets/ThirdParty/KayKit/Adventurers/Characters/Rogue_Hooded.fbx";

var imp = UnityEditor.AssetImporter.GetAtPath(p) as UnityEditor.ModelImporter;
sb.AppendLine("animationType = " + imp.animationType);
sb.AppendLine("transformPaths 数量 = " + (imp.transformPaths != null ? imp.transformPaths.Length : -1));
sb.AppendLine();
sb.AppendLine("=== 全部节点名 ===");
if (imp.transformPaths != null)
{
    foreach (var t in imp.transformPaths) sb.AppendLine("   " + t);
}
sb.AppendLine();

// 现有 avatar
var objs = UnityEditor.AssetDatabase.LoadAllAssetsAtPath(p);
foreach (var o in objs)
{
    var av = o as UnityEngine.Avatar;
    if (av != null)
        sb.AppendLine("现有 Avatar: " + av.name + "  isHuman=" + av.isHuman + "  isValid=" + av.isValid);
}

// 关键骨：Humanoid 自动映射看重这些名字
sb.AppendLine();
sb.AppendLine("=== Humanoid 关键骨是否存在 ===");
string[] want = { "Hips", "Spine", "Chest", "Neck", "Head", "Shoulder", "UpperArm", "LowerArm", "Hand",
                  "UpperLeg", "LowerLeg", "Foot", "Toe" };
foreach (var w in want)
{
    bool found = false;
    if (imp.transformPaths != null)
        foreach (var t in imp.transformPaths)
            if (t.IndexOf(w, System.StringComparison.OrdinalIgnoreCase) >= 0) { found = true; break; }
    sb.AppendLine(string.Format("   {0,-12} {1}", w, found ? "有" : "—"));
}
return sb.ToString();
