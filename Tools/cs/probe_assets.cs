// 探针：摸清玩家模型与动画剪辑的确切路径 / 名称（建场景与状态机要用）
var sb = new System.Text.StringBuilder();

sb.AppendLine("=== Universal Base Characters 下的 FBX ===");
var ubG = UnityEditor.AssetDatabase.FindAssets("t:Model", new string[] { "Assets/ThirdParty/Quaternius/UniversalBaseCharacters" });
if (ubG.Length == 0) sb.AppendLine("   (未找到，检查目录)");
var ubList = new System.Collections.Generic.List<string>();
foreach (var g in ubG) ubList.Add(UnityEditor.AssetDatabase.GUIDToAssetPath(g));
ubList.Sort();
foreach (var p in ubList)
{
    var imp = UnityEditor.AssetImporter.GetAtPath(p) as UnityEditor.ModelImporter;
    var go = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(p);
    int bones = 0;
    if (go != null) bones = go.GetComponentsInChildren<UnityEngine.Transform>(true).Length;
    sb.AppendLine("   " + p);
    sb.AppendLine("        animType=" + (imp != null ? imp.animationType.ToString() : "?")
        + "  isEmptyAvatar=" + (imp != null ? imp.sourceAvatar == null : true)
        + "  节点数=" + bones);
}

sb.AppendLine();
sb.AppendLine("=== UAL2 动画剪辑全量（含 Humanoid 状态） ===");
string[] animCandidates = {
    "Assets/ThirdParty/Quaternius/UniversalAnimationLibrary2/Unity/UAL2_Standard.fbx",
    "Assets/ThirdParty/Quaternius/UniversalAnimationLibrary2/Unity/UAL2_Standard_RM.fbx"
};
foreach (var p in animCandidates)
{
    var objs = UnityEditor.AssetDatabase.LoadAllAssetsAtPath(p);
    int n = 0;
    var names = new System.Collections.Generic.List<string>();
    foreach (var o in objs) { var c = o as UnityEngine.AnimationClip; if (c != null) { n++; names.Add(c.name); } }
    sb.AppendLine("   " + p + "  ->  " + n + " 条剪辑");
    names.Sort();
    foreach (var nm in names) sb.AppendLine("        " + nm);
}

sb.AppendLine();
sb.AppendLine("=== 现有 Humanoid Avatar 资产 ===");
foreach (var g in UnityEditor.AssetDatabase.FindAssets("t:Avatar"))
{
    string p = UnityEditor.AssetDatabase.GUIDToAssetPath(g);
    if (p.Contains("ThirdParty"))
        sb.AppendLine("   " + p + "   isHuman=" + (UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Avatar>(p) != null
            ? UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Avatar>(p).isHuman.ToString() : "?"));
}

sb.AppendLine();
sb.AppendLine("=== Nature MegaKit 前 12 个可当院景的模型名 ===");
var natG = UnityEditor.AssetDatabase.FindAssets("t:Model", new string[] { "Assets/ThirdParty/Quaternius/StylizedNatureMegaKit" });
var nat = new System.Collections.Generic.List<string>();
foreach (var g in natG) nat.Add(System.IO.Path.GetFileNameWithoutExtension(UnityEditor.AssetDatabase.GUIDToAssetPath(g)));
nat.Sort();
int c12 = System.Math.Min(12, nat.Count);
for (int i = 0; i < c12; i++) sb.AppendLine("   " + nat[i]);
sb.AppendLine("   (共 " + nat.Count + " 个模型)");

return sb.ToString();
