// 修「三段连击推不到第 3 段」。
//
// 现象（Tools/reports/S3_diag.txt B 段打点）：
//   同一段逻辑连跑两次，Atk1Rec 的取消窗口都在 nt≈0.33 打开、都立刻置了 Attack2 触发器，
//   但第一次转移没发生（Atk1Rec 一路走到 85% 回 Idle），第二次却发生了。
//
// 根因：这条转移同时挂了两个时间口径 ——
//   1) 状态机的 hasExitTime=0.30（exitTime 在 nt=0.30 处被跨过）
//   2) C# 的 comboRecCancelStart[0]=0.30（窗口打开后才置触发器）
//   两者是同一个阈值、但判定时刻差一帧。触发器到达时若 exitTime 刚好已被跨过，
//   这条转移就有概率不成立 —— 表现就是"有时候能连、有时候断"。
//
// 修法：时间口径只留一份（C# 的取消窗口），转移本身改为**纯触发器驱动**。
//   窗口没开 → C# 不会置触发器 → 转移不会发生；
//   窗口一开 → 触发器落地 → 立刻转移。与 Idle/Walk/Run -> Atk1 的写法保持一致。
using UnityEditor;
using UnityEngine;

const string CtrlPath = "Assets/_Project/Animations/Player.controller";

var ac = AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(CtrlPath);
if (ac == null) return "[ERR] 找不到 " + CtrlPath;

var sb = new System.Text.StringBuilder();
sb.AppendLine("=== 修正连击推进转移 ===");

// (源状态, 目标状态) —— 只改这两条「接下一段」的路径
var fix = new (string from, string to)[] { ("Atk1Rec", "Atk2"), ("Atk2Rec", "Atk3") };

var sm = ac.layers[0].stateMachine;
int changed = 0;

foreach (var cs in sm.states)
{
    foreach (var pair in fix)
    {
        if (cs.state.name != pair.from) continue;
        foreach (var tr in cs.state.transitions)
        {
            var dst = tr.destinationState;
            if (dst == null || dst.name != pair.to) continue;
            sb.AppendLine(string.Format("  {0} -> {1}   hasExitTime {2} -> False, exitTime {3:F2} -> 0.00",
                pair.from, pair.to, tr.hasExitTime, tr.exitTime));
            tr.hasExitTime = false;
            tr.exitTime = 0f;
            changed++;
        }
    }
}
sb.AppendLine("  改动 " + changed + " 条（应为 2）");

// 复核：把 Atk1Rec / Atk2Rec 的全部出边列出来
sb.AppendLine();
sb.AppendLine("复核 Atk1Rec / Atk2Rec 的出边：");
foreach (var cs in sm.states)
{
    if (cs.state.name != "Atk1Rec" && cs.state.name != "Atk2Rec") continue;
    foreach (var tr in cs.state.transitions)
    {
        string cond = "";
        if (tr.conditions != null)
            foreach (var c in tr.conditions)
                cond += (cond.Length > 0 ? " & " : "") + c.parameter + " " + c.mode + " " + c.threshold;
        sb.AppendLine(string.Format("  {0,-8} -> {1,-8} exitTime={2} {3:F2}  dur={4:F2}  条件[{5}]",
            cs.state.name,
            tr.destinationState == null ? "(null)" : tr.destinationState.name,
            tr.hasExitTime ? "on " : "off", tr.exitTime, tr.duration,
            cond.Length > 0 ? cond : "无"));
    }
}

EditorUtility.SetDirty(ac);
AssetDatabase.SaveAssets();
sb.AppendLine();
sb.AppendLine("已保存。");
return sb.ToString();
