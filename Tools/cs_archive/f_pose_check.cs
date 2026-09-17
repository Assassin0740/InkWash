// 一次 Play 里干两件事：① 核对 FootIK 的偏移输出通道 ② 跑姿态体检，验证两级补偿不重叠。
var sb = new System.Text.StringBuilder();
var go = UnityEngine.GameObject.Find("Player");
var ik = go != null ? go.GetComponent<InkWash.Player.FootIK>() : null;
var vis = go != null ? go.transform.Find("Visual") : null;
sb.AppendLine("[通道探针] OffsetTarget = " + (ik != null && ik.OffsetTarget != null ? ik.OffsetTarget.name : "NULL")
    + "   AppliedBodyOffset = " + (ik != null ? ik.AppliedBodyOffset.ToString("F4") : "-"));
if (vis != null) sb.AppendLine("  Visual.localPosition = " + vis.localPosition.ToString("F4") + "  lossyScale = " + vis.lossyScale.ToString("F4"));
if (ik != null && ik.stateOffsets != null)
    foreach (var o in ik.stateOffsets) sb.AppendLine("  " + o.state + "  " + o.y.ToString("F4") + " (世界米)");
UnityEngine.Debug.Log(sb.ToString());

return InkWash.Utils.PlaytestHarness.PoseScanFlow();
