// 摸清 Player 的实际装配：层级、组件、Animator 归属、控制器字段
var sb = new System.Text.StringBuilder();

void Dump(Transform t, string indent)
{
    var comps = new System.Collections.Generic.List<string>();
    foreach (var c in t.GetComponents<Component>())
        comps.Add(c == null ? "<Missing>" : c.GetType().Name);
    string extra = "";
    var a = t.GetComponent<Animator>();
    if (a != null)
    {
        string ctrl = a.runtimeAnimatorController != null ? a.runtimeAnimatorController.name : "null";
        extra = string.Format("  [Animator: ctrl={0}, human={1}, enabled={2}, avatar={3}]",
            ctrl, a.isHuman, a.enabled, a.avatar != null ? a.avatar.name : "null");
    }
    sb.AppendLine(indent + t.name + " :: " + string.Join(", ", comps) + extra);
    for (int i = 0; i < t.childCount; i++) Dump(t.GetChild(i), indent + "    ");
}

sb.AppendLine("========== 场景根对象 ==========");
foreach (var go in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
    sb.AppendLine("  " + go.name + (go.activeSelf ? "" : " [未激活]"));
sb.AppendLine();

sb.AppendLine("========== Player 层级 ==========");
var player = GameObject.Find("Player");
if (player == null) sb.AppendLine("  找不到名为 Player 的对象");
else Dump(player.transform, "  ");
sb.AppendLine();

sb.AppendLine("========== PlayerController 字段 ==========");
var pc = UnityEngine.Object.FindObjectOfType<InkWash.Player.PlayerController>();
if (pc == null) sb.AppendLine("  场景里没有 PlayerController");
else
{
    sb.AppendLine("  对象        : " + pc.gameObject.name);
    sb.AppendLine("  animator    : " + (pc.animator != null ? pc.animator.gameObject.name + " (ctrl="
        + (pc.animator.runtimeAnimatorController != null ? pc.animator.runtimeAnimatorController.name : "null") + ")" : "null"));
    sb.AppendLine("  cameraRig   : " + (pc.cameraRig != null ? pc.cameraRig.gameObject.name : "null"));
    sb.AppendLine("  cameraXform : " + (pc.cameraTransform != null ? pc.cameraTransform.gameObject.name : "null"));
    sb.AppendLine("  walkSpeed   : " + pc.walkSpeed + "   runSpeed: " + pc.runSpeed);
    sb.AppendLine("  footSyncRef : " + pc.footSyncReferenceSpeed);
    sb.AppendLine("  dashSpeed   : " + pc.dashSpeed + "  dashDuration: " + pc.dashDuration);
}
sb.AppendLine();

sb.AppendLine("========== 相机 ==========");
var cam = Camera.main;
if (cam == null) sb.AppendLine("  没有 MainCamera");
else
{
    var tpc = cam.GetComponent<InkWash.CameraRig.ThirdPersonCamera>();
    sb.AppendLine("  对象        : " + cam.gameObject.name);
    sb.AppendLine("  FOV         : " + cam.fieldOfView + "   nearClip: " + cam.nearClipPlane);
    sb.AppendLine("  ThirdPersonCamera: " + (tpc != null
        ? string.Format("distance={0} piv={1} pitch={2} 目标={3}", tpc.distance, tpc.pivotOffset, tpc.pitch,
            tpc.target != null ? tpc.target.name : "null")
        : "无"));
    var brain = cam.GetComponent<Cinemachine.CinemachineBrain>();
    sb.AppendLine("  CinemachineBrain: " + (brain != null ? "存在, enabled=" + brain.enabled : "无"));
}
sb.AppendLine();

sb.AppendLine("========== SwordVfx 是否已挂 ==========");
var vfx = UnityEngine.Object.FindObjectOfType<InkWash.Effects.SwordVfx>();
sb.AppendLine("  " + (vfx != null ? "已挂在 " + vfx.gameObject.name : "尚未挂载"));

return sb.ToString();
