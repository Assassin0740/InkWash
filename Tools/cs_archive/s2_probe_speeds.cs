// 核对 PlayerController 的移动参数：预制体上的序列化值 vs 场景实例的实时值。
// 起因：PlayerController.cs 默认 runSpeed=5.6，但 Tools/cs/s2_setup_combat.cs 里
// 明确写了「runSpeed 从 5.6 降到 4.2」（否则步频 4.5 步/秒，超真人冲刺，腿会原地倒腾）。
// 两者必须一致，否则"实际生效的是哪一个"就说不清。
var sb = new System.Text.StringBuilder();

System.Action<InkWash.Player.PlayerController, string> dump = (pc, tag) =>
{
    if (pc == null) { sb.AppendLine(tag + ": (无 PlayerController)"); return; }
    sb.AppendLine(tag);
    sb.AppendLine(string.Format("    walkSpeed={0:F2}  runSpeed={1:F2}  dashSpeed={2:F2}",
        pc.walkSpeed, pc.runSpeed, pc.dashSpeed));
    sb.AppendLine(string.Format("    walkRefSpeed={0:F2}  runRefSpeed={1:F2}  motionSpeed[{2:F2},{3:F2}]",
        pc.walkRefSpeed, pc.runRefSpeed, pc.motionSpeedMin, pc.motionSpeedMax));
    sb.AppendLine(string.Format("    runAnimEnter={0:F2} runAnimExit={1:F2}  → 跑步倍率 {2:F2}×  步频 {3:F2} 步/秒",
        pc.runAnimEnterSpeed, pc.runAnimExitSpeed,
        pc.runRefSpeed > 0f ? pc.runSpeed / pc.runRefSpeed : -1f,
        pc.runRefSpeed > 0f ? 2f / 0.600f * (pc.runSpeed / pc.runRefSpeed) : -1f));
    sb.AppendLine(string.Format("    走路倍率 {0:F2}×  步频 {1:F2} 步/秒",
        pc.walkRefSpeed > 0f ? pc.walkSpeed / pc.walkRefSpeed : -1f,
        pc.walkRefSpeed > 0f ? 2f / 1.333f * (pc.walkSpeed / pc.walkRefSpeed) : -1f));
};

foreach (var g in UnityEditor.AssetDatabase.FindAssets("t:Prefab"))
{
    var path = UnityEditor.AssetDatabase.GUIDToAssetPath(g);
    var go = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(path);
    if (go == null) continue;
    var pc = go.GetComponent<InkWash.Player.PlayerController>();
    if (pc != null) dump(pc, "预制体 " + path);
}

if (UnityEngine.Application.isPlaying)
{
    var ctl = UnityEngine.Object.FindObjectOfType<InkWash.Player.PlayerController>();
    dump(ctl, "场景实例（运行时）");
}

return sb.ToString();
