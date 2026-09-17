// 核对当前动画装配与运行状态（编辑器模式，不需要进 Play）。
// 结果直接 return 出来。
var sb = new System.Text.StringBuilder();

sb.AppendLine("isPlaying = " + UnityEditor.EditorApplication.isPlaying);
sb.AppendLine("activeScene = " + UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);

const string CtrlPath = "Assets/_Project/Animations/Player.controller";
var ac = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(CtrlPath);
if (ac == null) sb.AppendLine("[ERR] 找不到控制器");
else
{
    sb.AppendLine("图层数 = " + ac.layers.Length);
    foreach (var s in ac.layers[0].stateMachine.states)
    {
        var m = s.state.motion as UnityEngine.AnimationClip;
        sb.AppendLine(string.Format("  {0,-9} motion={1,-26} speed={2:F3} speedParamActive={3} param={4}",
            s.state.name,
            m == null ? "(null)" : m.name,
            s.state.speed,
            s.state.speedParameterActive,
            s.state.speedParameterActive ? s.state.speedParameter : "-"));
    }
}

// PlayerController 的移动参数（用于推算 MotionSpeed）
var guids = UnityEditor.AssetDatabase.FindAssets("t:Prefab Player");
sb.AppendLine("Player 预制体候选: " + guids.Length);

return sb.ToString();
