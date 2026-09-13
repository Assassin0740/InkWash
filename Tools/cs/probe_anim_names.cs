// 探针：筛出 S1 需要的移动类动画剪辑名（Idle / Run / Walk / Dash）
var sb = new System.Text.StringBuilder();
string p = "Assets/ThirdParty/Quaternius/UniversalAnimationLibrary2/Unity/UAL2_Standard.fbx";
var objs = UnityEditor.AssetDatabase.LoadAllAssetsAtPath(p);
var all = new System.Collections.Generic.List<string>();
foreach (var o in objs)
{
    var c = o as UnityEngine.AnimationClip;
    if (c != null && !c.name.StartsWith("__preview__")) all.Add(c.name);
}
all.Sort();
sb.AppendLine("非预览剪辑总数: " + all.Count);
sb.AppendLine();
string[] keys = { "Idle", "Run", "Walk", "Jog", "Sprint", "Dash", "Slide" };
foreach (var k in keys)
{
    sb.AppendLine("### 含 \"" + k + "\" :");
    int n = 0;
    foreach (var nm in all)
    {
        if (nm.IndexOf(k, System.StringComparison.OrdinalIgnoreCase) >= 0) { sb.AppendLine("   " + nm); n++; }
    }
    if (n == 0) sb.AppendLine("   (无)");
    sb.AppendLine();
}
sb.AppendLine("=== 全量清单（去掉 Armature| 前缀） ===");
foreach (var nm in all) sb.AppendLine("   " + nm.Replace("Armature|", ""));
return sb.ToString();
