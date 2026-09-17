// 最小验证：新类型/字段在已编译的程序集里是否可解析。
var sb = new System.Text.StringBuilder();
var tIk = System.Type.GetType("InkWash.Player.FootIK, Assembly-CSharp");
sb.AppendLine("InkWash.Player.FootIK 类型 = " + (tIk != null ? "可解析" : "缺失"));

var tPc = System.Type.GetType("InkWash.Player.PlayerController, Assembly-CSharp");
if (tPc != null)
{
    string[] names = { "walkRefSpeed", "runRefSpeed", "runAnimEnterSpeed", "runAnimExitSpeed", "CurrentRefSpeed", "WantsRunAnimation" };
    foreach (var n in names)
        sb.AppendLine("  PlayerController." + n + " = " + (tPc.GetField(n) != null || tPc.GetProperty(n) != null ? "有" : "缺失"));
}
var tV = System.Type.GetType("InkWash.Effects.SwordVfx, Assembly-CSharp");
if (tV != null)
{
    string[] names = { "trailMinSpeed", "trailStartDelay", "trailHoldMinSpeed", "BladeSpeed", "EmitStartBladeSpeed", "TrailEmitStartCount" };
    foreach (var n in names)
        sb.AppendLine("  SwordVfx." + n + " = " + (tV.GetField(n) != null || tV.GetProperty(n) != null ? "有" : "缺失"));
}
sb.AppendLine("isCompiling = " + UnityEditor.EditorApplication.isCompiling);
return sb.ToString();
