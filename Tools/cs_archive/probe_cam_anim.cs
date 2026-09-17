// 探针（精简）：只看 Animator 归属 + AnimatorController 结构 + Cinemachine + 主相机。
var sb = new System.Text.StringBuilder();

// ---------- 1. 两个 Animator 分别是什么 ----------
sb.AppendLine("=== Animator 归属 ===");
foreach (var a in UnityEngine.Object.FindObjectsOfType<UnityEngine.Animator>(true))
{
    string path = a.gameObject.name;
    var p = a.transform.parent;
    while (p != null) { path = p.name + "/" + path; p = p.parent; }
    sb.AppendLine("  " + path
        + "  controller=" + (a.runtimeAnimatorController == null ? "(null)" : a.runtimeAnimatorController.name)
        + "  avatar=" + (a.avatar == null ? "(null)" : a.avatar.name)
        + "  applyRootMotion=" + a.applyRootMotion
        + "  层数=" + a.layerCount);
}

// ---------- 2. AnimatorController ----------
sb.AppendLine();
sb.AppendLine("=== Player.controller ===");
var ac = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(
    "Assets/_Project/Animations/Player.controller");
if (ac == null) sb.AppendLine("  (未找到)");
else
{
    foreach (var L in ac.layers)
        sb.AppendLine("  层 " + L.name + "  权重=" + L.defaultWeight
            + "  遮罩=" + (L.avatarMask == null ? "(无)" : L.avatarMask.name)
            + "  blendingMode=" + L.blendingMode + "  IKPass=" + L.iKPass);
    sb.AppendLine("  参数: ");
    foreach (var p in ac.parameters)
        sb.AppendLine("      " + p.name + " : " + p.type);
    foreach (var s in ac.layers[0].stateMachine.states)
    {
        string mn = "(无)";
        if (s.state.motion is UnityEngine.AnimationClip c) mn = c.name + " " + c.length.ToString("F2") + "s";
        sb.AppendLine("  状态 " + s.state.name + "  speed=" + s.state.speed + "  motion=" + mn);
    }
    foreach (var s in ac.layers[0].stateMachine.states)
        foreach (var t in s.state.transitions)
        {
            var conds = new System.Collections.Generic.List<string>();
            foreach (var c in t.conditions) conds.Add(c.mode + " " + c.parameter);
            sb.AppendLine("  连线 " + s.state.name + " -> " + t.destinationState.name
                + "  hasExitTime=" + t.hasExitTime + "  dur=" + t.duration
                + "  when[" + string.Join(";", conds.ToArray()) + "]");
        }
}

// ---------- 3. Cinemachine ----------
sb.AppendLine();
sb.AppendLine("=== Cinemachine ===");
var vcs = UnityEngine.Object.FindObjectsOfType<Cinemachine.CinemachineVirtualCamera>(true);
sb.AppendLine("  虚拟相机数 " + vcs.Length);
foreach (var vc in vcs)
{
    sb.AppendLine("  " + vc.name + "  FOV=" + vc.m_Lens.FieldOfView
        + "  Follow=" + (vc.Follow == null ? "(null)" : vc.Follow.name)
        + "  LookAt=" + (vc.LookAt == null ? "(null)" : vc.LookAt.name));
    var tr = vc.GetCinemachineComponent<Cinemachine.CinemachineTransposer>();
    if (tr != null) sb.AppendLine("      Transposer offset=" + tr.m_FollowOffset.ToString("F3")
        + " mode=" + tr.m_BindingMode + " damp=" + tr.m_XDamping + "/" + tr.m_YDamping + "/" + tr.m_ZDamping);
    var cp = vc.GetCinemachineComponent<Cinemachine.CinemachineComposer>();
    if (cp != null) sb.AppendLine("      Composer trackedOffset=" + cp.m_TrackedObjectOffset.ToString("F3")
        + " ScreenX=" + cp.m_ScreenX + " ScreenY=" + cp.m_ScreenY);
}
var brain = UnityEngine.Object.FindObjectOfType<Cinemachine.CinemachineBrain>();
if (brain != null)
    sb.AppendLine("  Brain on " + brain.name + "  更新=" + brain.m_UpdateMethod
        + "  混合style=" + brain.m_DefaultBlend.m_Style + "  time=" + brain.m_DefaultBlend.m_Time);

// ---------- 4. 主相机 ----------
sb.AppendLine();
sb.AppendLine("=== 主相机 ===");
var cam = UnityEngine.Camera.main;
if (cam == null) sb.AppendLine("  (无)");
else
    sb.AppendLine("  " + cam.name + " FOV=" + cam.fieldOfView
        + " 世界位置=" + cam.transform.position.ToString("F3")
        + " 欧拉=" + cam.transform.eulerAngles.ToString("F1")
        + " 父=" + (cam.transform.parent == null ? "(无)" : cam.transform.parent.name));

// ---------- 5. 玩家与相机相对关系 ----------
sb.AppendLine();
sb.AppendLine("=== 玩家与相机距离 ===");
var pl = UnityEngine.GameObject.Find("Player");
if (pl != null && cam != null)
{
    var d = cam.transform.position - pl.transform.position;
    sb.AppendLine("  直线距离 " + d.magnitude.ToString("F2") + " m"
        + "   水平距离 " + new Vector2(d.x, d.z).magnitude.ToString("F2") + " m"
        + "   高度差 " + d.y.ToString("F2") + " m");
}

return sb.ToString();
