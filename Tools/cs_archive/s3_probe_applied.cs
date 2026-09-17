// 落地复核：在 Play 里读**场景实例**的真实值（预制体改了但场景有覆盖时，只有这里能看出来）。
using System.Collections;

string OUT = "Tools/reports/S3_applied.txt";

IEnumerator Body()
{
    var sb = new System.Text.StringBuilder();
    var ctl = Object.FindObjectOfType<InkWash.Player.PlayerController>();
    if (ctl == null) { sb.AppendLine("[ERR] 场景里没有 PlayerController"); System.IO.File.WriteAllText(OUT, sb.ToString()); yield break; }

    var root = ctl.transform;
    var vis = root.Find("Visual");
    var ik = root.GetComponent<InkWash.Player.FootIK>();
    var anim = ctl.animator;

    sb.AppendLine("=== 场景实例实际生效值 ===");
    sb.AppendLine("walkSpeed    = " + ctl.walkSpeed);
    sb.AppendLine("runSpeed     = " + ctl.runSpeed);
    sb.AppendLine("walkRefSpeed = " + ctl.walkRefSpeed);
    sb.AppendLine("runRefSpeed  = " + ctl.runRefSpeed);
    sb.AppendLine("dashDuration = " + ctl.dashDuration);
    sb.AppendLine("comboSwingDuration = [" + string.Join(", ", ctl.comboSwingDuration) + "]");
    sb.AppendLine("Visual.localPosition = " + (vis != null ? vis.localPosition.ToString("F4") : "(缺失)"));
    sb.AppendLine("Animator.avatar = " + (anim != null && anim.avatar != null ? anim.avatar.name : "(无)")
        + "   isHuman=" + (anim != null ? anim.isHuman.ToString() : "-"));
    sb.AppendLine();
    sb.AppendLine("FootIK.stateOffsets:");
    if (ik != null && ik.stateOffsets != null)
        foreach (var o in ik.stateOffsets) sb.AppendLine(string.Format("  {0,-9} {1,8:F4}", o.state, o.y));
    sb.AppendLine("FootIK.noGroundFixStates = [" + (ik != null ? string.Join(", ", ik.noGroundFixStates) : "-") + "]");

    sb.AppendLine();
    sb.AppendLine("当前 layer0 状态：");
    yield return null;
    var st = anim.GetCurrentAnimatorStateInfo(0);
    sb.AppendLine("  shortNameHash = " + st.shortNameHash + "  length = " + st.length.ToString("F3") + "s");

    System.IO.File.WriteAllText(OUT, sb.ToString());
    Debug.Log("[applied] " + sb.ToString());
}
return Body();
