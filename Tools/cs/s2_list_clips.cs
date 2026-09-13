// 列出 UAL2 里所有动画片段（名字 + 时长 + 是否循环），供动画选型决策。
var sb = new System.Text.StringBuilder();

string[] fbs =
{
    "Assets/ThirdParty/Quaternius/UniversalAnimationLibrary2/Unity/UAL2_Standard.fbx",
    "Assets/ThirdParty/Quaternius/UniversalAnimationLibrary2/Unity/UAL2_Standard_RM.fbx",
};

foreach (var f in fbs)
{
    sb.AppendLine("================================================");
    sb.AppendLine("FBX: " + f);
    sb.AppendLine("================================================");
    var list = new System.Collections.Generic.List<string>();
    foreach (var o in UnityEditor.AssetDatabase.LoadAllAssetsAtPath(f))
    {
        var c = o as UnityEngine.AnimationClip;
        if (c == null || c.name.StartsWith("__preview__")) continue;
        string n = c.name;
        if (n.StartsWith("Armature|")) n = n.Substring("Armature|".Length);
        list.Add(string.Format("{0,-42} {1,6:F2}s  循环={2}", n, c.length, c.isLooping));
    }
    list.Sort(System.StringComparer.OrdinalIgnoreCase);
    sb.AppendLine("共 " + list.Count + " 个片段：");
    foreach (var s in list) sb.AppendLine("  " + s);
    sb.AppendLine();
}

// 关键字过滤：走 / 跑 / 冲刺 / 攻击 / 待机
sb.AppendLine("=== 关键字命中 ===");
string[] keys = { "Walk", "Run", "Jog", "Sprint", "Dash", "Slide", "Idle", "Sword", "Attack", "Carry" };
foreach (var o in UnityEditor.AssetDatabase.LoadAllAssetsAtPath(fbs[0]))
{
    var c = o as UnityEngine.AnimationClip;
    if (c == null || c.name.StartsWith("__preview__")) continue;
    foreach (var k in keys)
        if (c.name.IndexOf(k, System.StringComparison.OrdinalIgnoreCase) >= 0)
        {
            sb.AppendLine("  [" + k + "] " + c.name + "  " + c.length.ToString("F2") + "s");
            break;
        }
}

return sb.ToString();
