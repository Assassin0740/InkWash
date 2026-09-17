// 把 Player.controller 的 Idle 状态片段从 KayKit Idle_A 换成 Char_Feng 自带 Idle。
//
// 理由（实测）：
//   · KayKit Idle_A 是给三头身 Q 版做的「立正」待机，套在写实古风角色上很怪；
//   · 更根本的是它是**徒手**待机 —— 手臂自然下垂时前臂竖直，
//     而剑身与手骨固定、近似垂直于前臂 → 剑必然横着，靠腕部扭转也拉不到朝下
//     （实测扭转只能让剑绕前臂画圈：0°/45°/90°/135° → 与向下夹角 109.9/99.9/83.0/68.3，收敛在水平附近）。
//   · Feng 自带 Idle 是**持剑**待机（左手收袖、右手竖剑于肩前），同作者同骨架，零下载。
//
// 只改 state.motion，不动转移条件（Idle 是循环状态，转移与时长无关）。
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

var sb = new System.Text.StringBuilder();
string ctrlPath = "Assets/_Project/Animations/Player.controller";
string newClipPath = "Assets/Char_Feng/Animation/Idle.anim";

var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(ctrlPath);
if (ctrl == null) { Debug.LogError("找不到 " + ctrlPath); return "ERR"; }
var newClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(newClipPath);
if (newClip == null) { Debug.LogError("找不到 " + newClipPath); return "ERR"; }

sb.AppendLine("controller = " + ctrl.name + "  →  目标片段 = " + newClip.name + " (" + newClip.length.ToString("F2") + "s)");
sb.AppendLine();

bool done = false;
foreach (var layer in ctrl.layers)
{
    foreach (var cs in layer.stateMachine.states)
    {
        if (cs.state.name != "Idle") continue;
        var old = cs.state.motion as AnimationClip;
        sb.AppendLine("Idle 状态原片段 = " + (old == null ? "<null>" : old.name + "  时长 " + old.length.ToString("F2") + "s"));
        cs.state.motion = newClip;
        done = true;
    }
}

if (!done) { Debug.LogError("没找到 Idle 状态"); return "ERR"; }

EditorUtility.SetDirty(ctrl);
AssetDatabase.SaveAssets();
AssetDatabase.Refresh();

// 重新读盘复核
var reread = AssetDatabase.LoadAssetAtPath<AnimatorController>(ctrlPath);
foreach (var layer in reread.layers)
    foreach (var cs in layer.stateMachine.states)
        if (cs.state.name == "Idle")
        {
            var c = cs.state.motion as AnimationClip;
            sb.AppendLine("重新读盘确认 = " + (c == null ? "<null>" : c.name)
                          + "  src=" + (c == null ? "-" : AssetDatabase.GetAssetPath(c)));
        }

System.IO.File.WriteAllText(
    System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Application.dataPath), "Tools/reports/q_setidle.txt"),
    sb.ToString());
Debug.Log("[q_setidle] 已换 Idle 片段");
return sb.ToString();
