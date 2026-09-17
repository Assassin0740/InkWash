// 把 KayKit 角色的标定结果落地到 Player.prefab。
//
// 三组值，全部来自实测（Tools/reports/S3_footik.txt / S3_measure.txt）：
//
//   1) Visual.localPosition.y —— KayKit 模型的原点在脚底上方，先把它压到根原点（0.088）
//   2) FootIK.stateOffsets    —— 每段动画的竖直基准偏移（该段「无 IK 网格最低点」取负 + 3mm）
//
//      ⚠ Run 的值取自 121 点归一化时间细扫（Tools/cs/s3_diag.cs A 段），不是最早那版
//        标定脚本的结果：那版按"真实帧"采样并硬性截断在 90 帧，编辑器高帧率下只能覆盖
//        Running_A 不到半个循环，量到的 0.0723 是假的。细扫与姿态体检一致：0.1626。
//        教训：按**归一化时间**扫，不要按帧数扫。
//      其余各段用姿势体检的无 IK 列（21 点采样，与细扫一致）。
//   3) PlayerController 速度  —— 参考速度换成 KayKit 原生地面速度（走 0.69 / 跑 3.03），
//                               walkSpeed / runSpeed 反推回真人步频区间
//
// 只用 PrefabUtility 改预制体资源，不碰场景实例 —— 场景里那份会在下次 Play 时重新实例化。
using UnityEditor;
using UnityEngine;

const string PrefabPath = "Assets/_Project/Prefabs/Player/Player.prefab";
const float VisualY = 0.088f;

// 走 = Walking_A 原生 0.69 m/s；跑 = Running_A 原生 3.03 m/s（Tools/cs/s3_probe_refspeed_kaykit.cs 实测）
const float WalkRef = 0.69f;
const float RunRef = 3.03f;
// 步频 = 2 步/循环 ÷ (片段时长 / 播放倍率)；Walk 1.067s、Run 0.800s
//   走 1.0/0.69 = 1.449× -> 2.716 步/秒（真人走路 2.0 上下，上限断言 3.0）
//   跑 4.0/3.03 = 1.320× -> 3.300 步/秒（真人跑步 2.6~4.4，且 > 走×1.15 = 3.124）
const float WalkSpeed = 1.0f;
const float RunSpeed = 4.0f;
// Dodge_Forward 片段 0.400s —— 冲刺时长必须与它一致，状态机才按 1.0× 播（否则动作被拉慢）
const float DashDuration = 0.40f;

var root = UnityEditor.PrefabUtility.LoadPrefabContents(PrefabPath);
if (root == null) return "[ERR] 载入预制体失败: " + PrefabPath;

var sb = new System.Text.StringBuilder();
sb.AppendLine("=== 落地 Player.prefab ===");
sb.AppendLine("路径 " + PrefabPath);
sb.AppendLine();

var vis = root.transform.Find("Visual");
if (vis == null) { UnityEditor.PrefabUtility.UnloadPrefabContents(root); return "[ERR] 找不到 Visual 子节点"; }
var oldVis = vis.localPosition;
vis.localPosition = new Vector3(oldVis.x, VisualY, oldVis.z);
sb.AppendLine(string.Format("Visual.localPosition.y  {0:F4} -> {1:F4}", oldVis.y, VisualY));

var ik = root.GetComponent<InkWash.Player.FootIK>();
if (ik == null) { UnityEditor.PrefabUtility.UnloadPrefabContents(root); return "[ERR] 根节点没有 FootIK"; }

sb.AppendLine();
sb.AppendLine("FootIK.stateOffsets：");
var oldOffsets = ik.stateOffsets;
if (oldOffsets != null)
    foreach (var o in oldOffsets)
        sb.AppendLine(string.Format("  旧  {0,-9} {1,8:F4}", o.state, o.y));

ik.stateOffsets = new[]
{
    new InkWash.Player.FootIK.StateYOffset { state = "Idle",    y = -0.1129f },
    new InkWash.Player.FootIK.StateYOffset { state = "Walk",    y = -0.0733f },
    new InkWash.Player.FootIK.StateYOffset { state = "Run",     y = -0.160f  },
    new InkWash.Player.FootIK.StateYOffset { state = "Dash",    y = -0.0908f },
    new InkWash.Player.FootIK.StateYOffset { state = "Atk1",    y = -0.1069f },
    new InkWash.Player.FootIK.StateYOffset { state = "Atk1Rec", y = -0.1122f },
    new InkWash.Player.FootIK.StateYOffset { state = "Atk2",    y = -0.0927f },
    new InkWash.Player.FootIK.StateYOffset { state = "Atk2Rec", y = -0.1117f },
    new InkWash.Player.FootIK.StateYOffset { state = "Atk3",    y = -0.1049f },
};
foreach (var o in ik.stateOffsets)
    sb.AppendLine(string.Format("  新  {0,-9} {1,8:F4}", o.state, o.y));

var ctl = root.GetComponent<InkWash.Player.PlayerController>();
if (ctl == null) { UnityEditor.PrefabUtility.UnloadPrefabContents(root); return "[ERR] 根节点没有 PlayerController"; }

sb.AppendLine();
sb.AppendLine("PlayerController：");
System.Action<string, float, float> setF = (name, oldV, newV) =>
    sb.AppendLine(string.Format("  {0,-18} {1,8:F4} -> {2,8:F4}", name, oldV, newV));

setF("walkSpeed", ctl.walkSpeed, WalkSpeed); ctl.walkSpeed = WalkSpeed;
setF("runSpeed", ctl.runSpeed, RunSpeed); ctl.runSpeed = RunSpeed;
setF("walkRefSpeed", ctl.walkRefSpeed, WalkRef); ctl.walkRefSpeed = WalkRef;
setF("runRefSpeed", ctl.runRefSpeed, RunRef); ctl.runRefSpeed = RunRef;
setF("dashDuration", ctl.dashDuration, DashDuration); ctl.dashDuration = DashDuration;

// 逻辑侧的三段挥砍时长，与 Player.controller 里的状态时长（片段原长）对齐，报告才不误导
var oldSwing = ctl.comboSwingDuration;
ctl.comboSwingDuration = new[] { 1.0f, 1.367f, 1.6f };
sb.AppendLine("  comboSwingDuration  " + string.Join(", ", oldSwing)
    + "  ->  " + string.Join(", ", ctl.comboSwingDuration) + "  （= 片段原长 1.000/1.367/1.600）");

// ---- 复核派生量 ----
float walkClipLen = 1.067f, runClipLen = 0.800f;   // Walking_A / Running_A 片段实测时长
float walkPlay = WalkSpeed / WalkRef, runPlay = RunSpeed / RunRef;
float walkCad = 2f * walkPlay / walkClipLen, runCad = 2f * runPlay / runClipLen;
sb.AppendLine();
sb.AppendLine("派生量复核：");
sb.AppendLine(string.Format("  走路 播放倍率 {0:F3}×  步频 {1:F3} 步/秒（断言 ≤ 3.0）", walkPlay, walkCad));
sb.AppendLine(string.Format("  跑步 播放倍率 {0:F3}×  步频 {1:F3} 步/秒（断言 ≤ 4.4，且 > 走×1.15 = {2:F3}）",
    runPlay, runCad, walkCad * 1.15f));
sb.AppendLine(string.Format("  播放倍率上限 motionSpeedMax = {0:F2}，两者都在其下", ctl.motionSpeedMax));
sb.AppendLine(string.Format("  冲刺 {0:F1} m/s × {1:F2}s ≈ {2:F2} m（断言 > 2m）",
    ctl.dashSpeed, DashDuration, ctl.dashSpeed * DashDuration * 0.85f));

UnityEditor.PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
UnityEditor.PrefabUtility.UnloadPrefabContents(root);
UnityEditor.AssetDatabase.SaveAssets();
UnityEditor.AssetDatabase.Refresh();

sb.AppendLine();
sb.AppendLine("已保存。");
return sb.ToString();
