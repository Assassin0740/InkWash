// 探针：全项目动画家底排查 —— 到底有没有 Walk / Run
var sb = new System.Text.StringBuilder();

sb.AppendLine("=== UniversalAnimationLibrary2 目录实况 ===");
foreach (var d in System.IO.Directory.GetDirectories("Assets/ThirdParty/Quaternius/UniversalAnimationLibrary2", "*", System.IO.SearchOption.AllDirectories))
    sb.AppendLine("   [D] " + d.Replace("\\", "/"));
foreach (var f in System.IO.Directory.GetFiles("Assets/ThirdParty/Quaternius/UniversalAnimationLibrary2", "*", System.IO.SearchOption.AllDirectories))
{
    if (!f.EndsWith(".meta")) sb.AppendLine("       " + f.Replace("\\", "/"));
}

sb.AppendLine();
sb.AppendLine("=== 全 ThirdParty 范围内，剪辑名含 Walk/Run 的 FBX（排除 __preview__） ===");
string[] keys = { "walk", "run", "jog", "sprint", "locomot" };
foreach (var g in UnityEditor.AssetDatabase.FindAssets("t:Model", new string[] { "Assets/ThirdParty" }))
{
    string path = UnityEditor.AssetDatabase.GUIDToAssetPath(g);
    var objs = UnityEditor.AssetDatabase.LoadAllAssetsAtPath(path);
    var hits = new System.Collections.Generic.List<string>();
    foreach (var o in objs)
    {
        var c = o as UnityEngine.AnimationClip;
        if (c == null) continue;
        string lower = c.name.ToLowerInvariant();
        if (lower.Contains("preview")) continue;
        foreach (var k in keys) { if (lower.Contains(k)) { hits.Add(c.name); break; } }
    }
    if (hits.Count > 0)
    {
        sb.AppendLine("   " + path);
        foreach (var h in hits) sb.AppendLine("        " + h);
    }
}

sb.AppendLine();
sb.AppendLine("=== UltimateMonsters 剪辑总览（取前 40 条 + 总数） ===");
foreach (var g in UnityEditor.AssetDatabase.FindAssets("t:Model", new string[] { "Assets/ThirdParty/Quaternius/UltimateMonsters" }))
{
    string path = UnityEditor.AssetDatabase.GUIDToAssetPath(g);
    var objs = UnityEditor.AssetDatabase.LoadAllAssetsAtPath(path);
    var names = new System.Collections.Generic.List<string>();
    foreach (var o in objs) { var c = o as UnityEngine.AnimationClip; if (c != null && !c.name.Contains("preview")) names.Add(c.name); }
    if (names.Count > 0)
    {
        sb.AppendLine("   " + System.IO.Path.GetFileName(path) + "  -> " + names.Count + " 条");
        names.Sort();
        int lim = System.Math.Min(40, names.Count);
        for (int i = 0; i < lim; i++) sb.AppendLine("        " + names[i]);
    }
}

return sb.ToString();
