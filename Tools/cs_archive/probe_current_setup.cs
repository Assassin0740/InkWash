// 探针：把当前的 Player 层级、AnimatorController 结构、Cinemachine 虚拟相机、主相机设置摊开。
var sb = new System.Text.StringBuilder();

// ---------- 1. Player 层级 ----------
sb.AppendLine("=== Player 层级 ===");
var player = UnityEngine.GameObject.Find("Player");
if (player == null) sb.AppendLine("  (场景里找不到 Player)");
else
{
    System.Action<UnityEngine.Transform, int> dump = null;
    dump = (t, d) =>
    {
        var comps = t.GetComponents<UnityEngine.Component>();
        var names = new System.Collections.Generic.List<string>();
        foreach (var c in comps) names.Add(c.GetType().Name);
        sb.AppendLine(new string(' ', d * 2) + "- " + t.name
            + "  [" + string.Join(",", names.ToArray()) + "]"
            + "  localPos=" + t.localPosition.ToString("F3")
            + " localScale=" + t.localScale.ToString("F3"));
        for (int i = 0; i < t.childCount; i++) dump(t.GetChild(i), d + 1);
    };
    dump(player.transform, 1);
}

// ---------- 2. AnimatorController ----------
sb.AppendLine();
sb.AppendLine("=== AnimatorController ===");
var ac = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(
    "Assets/_Project/Animations/Player.controller");
if (ac == null) sb.AppendLine("  (未找到 Player.controller)");
else
{
    sb.AppendLine("  层数 " + ac.layers.Length);
    foreach (var L in ac.layers)
        sb.AppendLine("    layer: " + L.name + "  默认权重 " + L.defaultWeight
            + "  遮罩 " + (L.avatarMask == null ? "(无)" : L.avatarMask.name));
    sb.AppendLine("  参数:");
    foreach (var p in ac.parameters)
        sb.AppendLine("    " + p.name + " : " + p.type + "  默认 " + p.defaultFloat + "/" + p.defaultBool);
    sb.AppendLine("  状态:");
    foreach (var s in ac.layers[0].stateMachine.states)
    {
        var st = s.state;
        int clipCount = st.motion == null ? 0 : 1;
        string clipName = "(无)";
        if (st.motion is UnityEngine.AnimationClip c) clipName = c.name + "  " + c.length.ToString("F2") + "s";
        else if (st.motion is UnityEditor.Animations.BlendTree bt) clipName = "BlendTree(" + bt.name + ")";
        sb.AppendLine("    " + st.name + "  speed=" + st.speed + "  motion=" + clipName);
    }
    sb.AppendLine("  连线:");
    foreach (var s in ac.layers[0].stateMachine.states)
        foreach (var tr in s.state.transitions)
            sb.AppendLine("    " + s.state.name + " -> " + tr.destinationState.name
                + "   hasExitTime=" + tr.hasExitTime + "  duration=" + tr.duration
                + "  conditions=" + tr.conditions.Length);
}

// ---------- 3. Cinemachine ----------
sb.AppendLine();
sb.AppendLine("=== Cinemachine 虚拟相机 ===");
foreach (var vc in UnityEngine.Object.FindObjectsOfType<Cinemachine.CinemachineVirtualCamera>(true))
{
    sb.AppendLine("  " + vc.name + "  FOV=" + vc.m_Lens.FieldOfView
        + "  近裁=" + vc.m_Lens.NearClipPlane + "  远裁=" + vc.m_Lens.FarClipPlane);
    var tr = vc.GetCinemachineComponent<Cinemachine.CinemachineTransposer>();
    if (tr != null)
        sb.AppendLine("      Transposer  m_FollowOffset=" + tr.m_FollowOffset.ToString("F3")
            + "  bindingMode=" + tr.m_BindingMode
            + "  damping=" + tr.m_XDamping + "/" + tr.m_YDamping + "/" + tr.m_ZDamping);
    var cp = vc.GetCinemachineComponent<Cinemachine.CinemachineComposer>();
    if (cp != null)
        sb.AppendLine("      Composer  trackedObjectOffset=" + cp.m_TrackedObjectOffset.ToString("F3")
            + "  ScreenX=" + cp.m_ScreenX + "  ScreenY=" + cp.m_ScreenY);
    var orb = vc.GetCinemachineComponent<Cinemachine.CinemachineOrbitalTransposer>();
    if (orb != null)
        sb.AppendLine("      OrbitalTransposer  m_FollowOffset=" + orb.m_FollowOffset.ToString("F3")
            + "  最近输入=" + orb.m_Heading.m_Definition
            + "  速度阻尼=" + orb.m_Heading.m_VelocityFilterStrength);
    sb.AppendLine("      LookAt=" + (vc.LookAt == null ? "(null)" : vc.LookAt.name)
        + "  Follow=" + (vc.Follow == null ? "(null)" : vc.Follow.name));
}
var brain = UnityEngine.Object.FindObjectOfType<Cinemachine.CinemachineBrain>();
sb.AppendLine("  Brain: " + (brain == null ? "(无)" : brain.name + "  更新方式=" + brain.m_UpdateMethod + "  混合=" + brain.m_DefaultBlend.m_Style));

// ---------- 4. 主相机 ----------
sb.AppendLine();
sb.AppendLine("=== 主相机 ===");
var cam = UnityEngine.Camera.main;
if (cam == null) sb.AppendLine("  (无 MainCamera)");
else
{
    sb.AppendLine("  " + cam.name + "  FOV=" + cam.fieldOfView + "  位置=" + cam.transform.position.ToString("F3")
        + "  欧拉角=" + cam.transform.eulerAngles.ToString("F1"));
    sb.AppendLine("  局部位置(相对父)=" + cam.transform.localPosition.ToString("F3")
        + "  父=" + (cam.transform.parent == null ? "(无)" : cam.transform.parent.name));
}

return sb.ToString();
