using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// 读 Player.controller 里 Atk1/Atk2/Atk3 三个状态的完整配置（motion / speed / 过渡条件）
var sb = new StringBuilder();
string path = "Assets/_Project/Animations/Player.controller";
var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
if (ctrl == null) return "!! 找不到 " + path;

sb.AppendLine("控制器 " + path + "  layers=" + ctrl.layers.Length);

System.Action<AnimatorStateMachine, string> dump = null;
dump = (sm, indent) =>
{
    foreach (var cs in sm.states)
    {
        var st = cs.state;
        string motion = st.motion == null ? "<null>"
            : (st.motion is AnimationClip ac ? "clip:" + ac.name + " (" + ac.length.ToString("F3") + "s)"
               : (st.motion is BlendTree bt ? "blendTree:" + bt.name : st.motion.GetType().Name));
        sb.AppendLine(indent + "STATE " + st.name
            + "   motion=" + motion
            + "   speed=" + st.speed.ToString("F3")
            + "   speedParam=" + (st.speedParameterActive ? st.speedParameter : "-")
            + "   writeDefault=" + st.writeDefaultValues);

        foreach (var t in st.transitions)
        {
            sb.Append(indent + "   → " + (t.isExit ? "(Exit)" : (t.destinationState != null ? t.destinationState.name : "?"))
                + "   hasExitTime=" + t.hasExitTime
                + "   exitTime=" + t.exitTime.ToString("F3")
                + "   duration=" + t.duration.ToString("F3")
                + "   offset=" + t.offset.ToString("F3")
                + "   cond=[");
            foreach (var c in t.conditions)
                sb.Append(c.parameter + " " + c.mode + " " + c.threshold.ToString("F2") + " ");
            sb.AppendLine("]");
        }
    }
    foreach (var sub in sm.stateMachines)
        dump(sub.stateMachine, indent + "  ");
};

foreach (var layer in ctrl.layers)
{
    sb.AppendLine();
    sb.AppendLine("════ layer " + layer.name + "  (defaultState=" + (layer.stateMachine.defaultState != null ? layer.stateMachine.defaultState.name : "null") + ") ════");
    dump(layer.stateMachine, "  ");
}

sb.AppendLine();
sb.AppendLine("════ 参数 ════");
foreach (var p in ctrl.parameters)
    sb.AppendLine("  " + p.name + " : " + p.type);

Debug.Log("[q_atkstate] 完成");
return sb.ToString();
