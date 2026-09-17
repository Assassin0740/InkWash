// 编译/导入状态 + 关键类型可用性一次性体检
var sb = new System.Text.StringBuilder();
sb.AppendLine("isCompiling = " + UnityEditor.EditorApplication.isCompiling);
sb.AppendLine("isUpdating  = " + UnityEditor.EditorApplication.isUpdating);
sb.AppendLine();

// 我方程序集（默认 Assembly-CSharp）里能否解析到三方类型
// 坑（已修）：PrimeTween 的运行时程序集叫 PrimeTween.Runtime，不是 PrimeTween。
//   包名（com.kyrylokuzyk.primetween）与程序集名不是一回事，写错会得到假的"缺失"结论。
var probe = new (string label, string typeName)[]
{
    ("QFramework.AudioKit@AudioKit",                 "QFramework.AudioKit, AudioKit"),
    ("QFramework.AudioKitSettingsModel@AudioKit",    "QFramework.AudioKitSettingsModel, AudioKit"),
    // 不再探测 QFramework.MonoSingletonProperty：GameAudio 只用到 AudioKit 与 AudioKit.Settings，
    // 探测一个用不到的类型只会让报告里出现无从解释的"缺失"，徒增噪音。
    ("PrimeTween.Tween@PrimeTween.Runtime",          "PrimeTween.Tween, PrimeTween.Runtime"),
    ("InkWash.Player.PlayerController",              "InkWash.Player.PlayerController, Assembly-CSharp"),
    ("InkWash.Player.ActionPhase",                   "InkWash.Player.ActionPhase, Assembly-CSharp"),
    ("InkWash.CameraRig.ThirdPersonCamera",          "InkWash.CameraRig.ThirdPersonCamera, Assembly-CSharp"),
    ("InkWash.Effects.SwordVfx",                     "InkWash.Effects.SwordVfx, Assembly-CSharp"),
    ("InkWash.Core.GameAudio",                       "InkWash.Core.GameAudio, Assembly-CSharp"),
    ("InkWash.Core.AudioDirector",                   "InkWash.Core.AudioDirector, Assembly-CSharp"),
    ("InkWash.Utils.PlaytestHarness",                "InkWash.Utils.PlaytestHarness, Assembly-CSharp"),
};
foreach (var p in probe)
{
    sb.AppendLine(string.Format("  {0,-46} {1}", p.label,
        System.Type.GetType(p.typeName) != null ? "OK" : "缺失"));
}
return sb.ToString();
