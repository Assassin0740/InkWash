// 探针：确认 PlaytestHarness 编译通过 + 当前是否在 Play 模式
var sb = new System.Text.StringBuilder();
sb.AppendLine("isPlaying   = " + UnityEditor.EditorApplication.isPlaying);
sb.AppendLine("isCompiling = " + UnityEditor.EditorApplication.isCompiling);
sb.AppendLine("isUpdating  = " + UnityEditor.EditorApplication.isUpdating);

var t = System.Type.GetType("InkWash.Utils.PlaytestHarness, Assembly-CSharp");
sb.AppendLine("PlaytestHarness: " + (t == null ? "未找到（编译未完成或报错）" : "OK"));
if (t != null)
{
    var m = t.GetMethod("S1LocomotionFlow");
    sb.AppendLine("   S1LocomotionFlow: " + (m == null ? "缺失" : m.ReturnType.Name));
}

var tp = System.Type.GetType("InkWash.Player.PlayerController, Assembly-CSharp");
if (tp != null)
{
    sb.AppendLine("PlayerController 注入入口:");
    foreach (var name in new string[] { "BeginInputOverride", "EndInputOverride", "SetInjectedMove", "RequestInjectedDash" })
    {
        var mi = tp.GetMethod(name);
        sb.AppendLine("   " + name + ": " + (mi == null ? "缺失" : "OK"));
    }
}
return sb.ToString();
