// 现场探针：把 Animator / PlayerController 的实时状态打出来，排查
// "Animator does not have an AnimatorController" 是从哪来的。
var sb = new System.Text.StringBuilder();
sb.AppendLine("isPlaying = " + UnityEngine.Application.isPlaying);

var anims = UnityEngine.Object.FindObjectsOfType<UnityEngine.Animator>();
sb.AppendLine("场景 Animator 数量 = " + anims.Length);
foreach (var a in anims)
{
    sb.AppendLine(string.Format("  [{0}] enabled={1} activeInHierarchy={2} ctrl={3} avatar={4} isHuman={5} speed={6:F2}",
        a.gameObject.name, a.enabled, a.gameObject.activeInHierarchy,
        a.runtimeAnimatorController == null ? "(null)" : a.runtimeAnimatorController.name,
        a.avatar == null ? "(null)" : a.avatar.name,
        a.isHuman, a.speed));
    if (a.runtimeAnimatorController != null && a.isInitialized)
    {
        var si = a.GetCurrentAnimatorStateInfo(0);
        sb.AppendLine("        curState hash=" + si.fullPathHash + " normTime=" + si.normalizedTime.ToString("F2"));
    }
}

var ctl = UnityEngine.Object.FindObjectOfType<InkWash.Player.PlayerController>();
if (ctl != null)
{
    sb.AppendLine("PlayerController: enabled=" + ctl.enabled
        + " animator=" + (ctl.animator == null ? "(null)" : ctl.animator.gameObject.name)
        + " pos=" + ctl.transform.position.ToString("F1")
        + " speed=" + ctl.CurrentSpeed.ToString("F2")
        + " inputOverridden=" + ctl.IsInputOverridden);
}
else sb.AppendLine("[!] 场景里没有 PlayerController");

var cam = UnityEngine.Camera.main;
if (cam != null)
{
    sb.AppendLine("Camera: " + cam.gameObject.name + " targetTexture=" + (cam.targetTexture == null ? "(null)" : cam.targetTexture.name)
        + " fov=" + cam.fieldOfView.ToString("F1"));
    var rig = cam.GetComponent("ThirdPersonCamera") as UnityEngine.MonoBehaviour;
    sb.AppendLine("  rig = " + (rig == null ? "(none)" : ("enabled=" + rig.enabled)));
}

return sb.ToString();
