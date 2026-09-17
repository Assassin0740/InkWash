// 列出 Player.controller 的 layer0 状态 -> 片段 -> 时长 -> 播放速度设定。
// 步频断言对片段时长敏感，重标定速度前先看清真实长度。
using UnityEditor;

var ac = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(
    "Assets/_Project/Animations/Player.controller");
if (ac == null) return "[ERR] 找不到控制器";

var sb = new System.Text.StringBuilder();
sb.AppendLine("=== Player.controller layer0 ===");
foreach (var s in ac.layers[0].stateMachine.states)
{
    var m = s.state.motion as UnityEngine.AnimationClip;
    float len = m == null ? 0f : m.length;
    string speed = s.state.speedParameterActive ? ("参数:" + s.state.speedParameter) : s.state.speed.ToString("F3");
    float eff = len <= 0f ? 0f
        : (s.state.speedParameterActive ? len : len / Mathf.Max(s.state.speed, 0.0001f));
    sb.AppendLine(string.Format("  {0,-9} 片段={1,-34} 时长={2,6:F3}s  速度={3,-12} 有效时长≈{4:F3}s",
        s.state.name, m == null ? "(无)" : m.name, len, speed, eff));
}
sb.AppendLine();
sb.AppendLine("循环片段检查（loopTime）：");
foreach (var s in ac.layers[0].stateMachine.states)
{
    var m = s.state.motion as UnityEngine.AnimationClip;
    if (m == null) continue;
    sb.AppendLine(string.Format("  {0,-9} loop={1}", s.state.name, m.isLooping));
}
return sb.ToString();
