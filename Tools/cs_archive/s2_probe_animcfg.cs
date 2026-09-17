// 静态 dump 动画状态机 layer0 的配置：状态（片段/时长/是否循环）+ 过渡（退出时间/条件）。
// 用来解释"后摇取消窗口为什么打不开" —— 那要求 Atk1Rec 的 normalizedTime 能跑到 0.3 以上，
// 若出 Atk1Rec 的过渡是 hasExitTime=false 或 exitTime 很小，状态会被提前切走。
var sb = new System.Text.StringBuilder();

var player = GameObject.Find("Player");
if (player == null) return "[ERR] 找不到 Player";
var anim = player.GetComponent<Animator>();
var ctrl = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(
    UnityEditor.AssetDatabase.GetAssetPath(anim.runtimeAnimatorController));
if (ctrl == null) return "[ERR] 读不到 AnimatorController";

sb.AppendLine("controller = " + ctrl.name + "  层数 = " + ctrl.layers.Length);
for (int L = 0; L < ctrl.layers.Length; L++)
{
    var layer = ctrl.layers[L];
    var sm = layer.stateMachine;
    sb.AppendLine();
    sb.AppendLine("== layer " + L + "  " + layer.name + "  iKPass=" + layer.iKPass
        + "  weight=" + layer.defaultWeight + "  mask=" + (layer.avatarMask != null ? layer.avatarMask.name : "(无)"));
    sb.AppendLine("  状态:");
    foreach (var cs in sm.states)
    {
        var st = cs.state;
        var clip = st.motion as AnimationClip;
        sb.AppendLine(string.Format("    {0,-12} speed={1,-5:F2} loop={2,-5} clip={3}  len={4}",
            st.name, st.speed, st.motion is AnimationClip c0 && c0.isLooping,
            clip != null ? clip.name : (st.motion != null ? st.motion.name : "(无)"),
            clip != null ? clip.length.ToString("F3") + "s" : "-"));
        foreach (var tr in st.transitions)
            sb.AppendLine(string.Format("        → {0,-12} hasExit={1,-5} exitT={2,-5:F2} dur={3,-5:F2} cond=[{4}]",
                tr.destinationState != null ? tr.destinationState.name : "?",
                tr.hasExitTime, tr.exitTime, tr.duration, Describe(tr)));
    }
    sb.AppendLine("  AnyState 过渡:");
    foreach (var tr in sm.anyStateTransitions)
        sb.AppendLine(string.Format("    → {0,-12} hasExit={1,-5} exitT={2,-5:F2} dur={3,-5:F2} cond=[{4}]",
            tr.destinationState != null ? tr.destinationState.name : "?",
            tr.hasExitTime, tr.exitTime, tr.duration, Describe(tr)));
    sb.AppendLine("  Entry:");
    foreach (var tr in sm.entryTransitions)
        sb.AppendLine("    → " + (tr.destinationState != null ? tr.destinationState.name : "?"));
    sb.AppendLine("  参数:");
    foreach (var p in ctrl.parameters)
        sb.AppendLine("    " + p.name + " : " + p.type);
}

return sb.ToString();

string Describe(UnityEditor.Animations.AnimatorStateTransition tr)
{
    var parts = new System.Collections.Generic.List<string>();
    foreach (var c in tr.conditions)
        parts.Add(c.parameter + " " + c.mode + " " + c.threshold.ToString("F2"));
    return parts.Count > 0 ? string.Join(" && ", parts) : "(无)";
}
