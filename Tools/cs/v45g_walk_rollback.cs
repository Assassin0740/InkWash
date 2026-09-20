// v45g: 走路片段换回 Feng_Walk_Loop（原地、跑步机速度 0.75）
// 依据：v45f 实测 Mixamo walk 的跑步机速度只有 0.37 m/s（老片段 0.75），
//       配 1.25 m/s 移动需要 3.4× 播放 → 竞走级小碎步；跑步 Mixamo 正常（3.92），保留。
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using System.Text;

var sb = new System.Text.StringBuilder();
var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>("Assets/_Project/Animations/Player.controller");
if (ctrl == null) return "no controller";
var sm = ctrl.layers[0].stateMachine;

var walkClip = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/_Project/Animations/Feng_Walk_Loop.anim");
if (walkClip == null) return "no Feng_Walk_Loop.anim";

foreach (var s in sm.states)
{
    if (s.state.name != "Walk") continue;
    sb.AppendLine("Walk before: " + (s.state.motion != null ? s.state.motion.name : "none"));
    s.state.motion = walkClip;
    sb.AppendLine("Walk after : " + s.state.motion.name);
}

EditorUtility.SetDirty(ctrl);
AssetDatabase.SaveAssets();

sb.AppendLine("--- 现状核对 ---");
foreach (var s in sm.states)
{
    if (s.state.name == "Walk" || s.state.name == "Run" || s.state.name == "Idle")
        sb.AppendLine(s.state.name + " = " + (s.state.motion != null ? s.state.motion.name : "none"));
}
return sb.ToString();
