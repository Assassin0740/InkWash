using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// 查 Player.controller 每个状态挂的片段，以及片段的来源、时长、是否 HumanMotion
var sb = new StringBuilder();
string ctrlPath = "Assets/_Project/Animations/Player.controller";
var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(ctrlPath);
sb.AppendLine("controller = " + (ctrl == null ? "<null>" : ctrl.name) + "  层数=" + (ctrl == null ? 0 : ctrl.layers.Length));

if (ctrl != null)
{
    for (int li = 0; li < ctrl.layers.Length; li++)
    {
        var sm = ctrl.layers[li].stateMachine;
        sb.AppendLine("── 层[" + li + "] " + ctrl.layers[li].name + "  状态数=" + sm.states.Length);
        foreach (var cs in sm.states)
        {
            string clipName = "<无 motion>";
            string clipInfo = "";
            if (cs.state.motion is AnimationClip c)
            {
                clipName = c.name;
                string src = AssetDatabase.GetAssetPath(c);
                clipInfo = "  len=" + c.length.ToString("F3") + "s  humanMotion=" + c.isHumanMotion
                         + "  legacy=" + c.legacy + "  loop=" + (c.isLooping ? "是" : "否")
                         + "  src=" + src;
            }
            else if (cs.state.motion is BlendTree bt)
            {
                clipName = "<BlendTree> " + bt.name + " blendType=" + bt.blendType;
                var kids = new List<string>();
                CollectLeaves(bt, kids);
                clipInfo = "  叶子=[" + string.Join(", ", kids) + "]";
            }
            sb.AppendLine("   state " + cs.state.name + "  →  " + clipName + clipInfo);
        }
    }
}

// 顺带把 Idle 状态单独标出来
sb.AppendLine();
sb.AppendLine(">>> Idle 状态的片段 = " + DescribeState(ctrl, "Idle"));
sb.AppendLine(">>> 片段总数（工程内 .anim/.fbx 里的）不在此列，本脚本只读控制器");

Debug.Log("[q_idle] 完成");
return sb.ToString();

static void CollectLeaves(BlendTree bt, List<string> outNames)
{
    foreach (var ch in bt.children)
    {
        if (ch.motion is AnimationClip c) outNames.Add(c.name + "(" + c.length.ToString("F2") + "s)");
        else if (ch.motion is BlendTree sub) CollectLeaves(sub, outNames);
        else outNames.Add("<null>");
    }
}

static string DescribeState(AnimatorController ctrl, string stateName)
{
    if (ctrl == null) return "<no controller>";
    foreach (var layer in ctrl.layers)
    {
        foreach (var cs in layer.stateMachine.states)
        {
            if (cs.state.name != stateName) continue;
            if (cs.state.motion is AnimationClip c)
                return c.name + "  len=" + c.length.ToString("F3") + "s  src=" + AssetDatabase.GetAssetPath(c);
            if (cs.state.motion is BlendTree b) return "<BlendTree " + b.name + ">";
            return "<null motion>";
        }
    }
    return "<未找到该状态>";
}
