// q_pose3.cs —— 编辑态探针：控制器层/状态/遮罩 + 待机片段指向
// 目的：① 跑步僵硬是否源自「上半身遮罩层锁死手臂」；② 战斗待机到底播的是哪段
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

void DumpStates(StringBuilder sb, AnimatorStateMachine sm, string pad)
{
    foreach (var st in sm.states)
    {
        var s = st.state;
        string motion = s.motion == null ? "<null>" : s.motion.name + " [" + s.motion.GetType().Name + "]";
        string sp = "speed=" + s.speed;
        if (!string.IsNullOrEmpty(s.speedParameter)) sp += " speedParam=" + s.speedParameter;
        string mir = s.mirror ? " mirror=true" : "";
        sb.AppendLine(string.Format("{0}{1,-11} motion={2,-46} {3}{4}", pad, s.name, motion, sp, mir));
    }
    foreach (var sub in sm.stateMachines)
    {
        sb.AppendLine(pad + "[子状态机] " + sub.stateMachine.name);
        DumpStates(sb, sub.stateMachine, pad + "  ");
    }
}

void CollectClips(AnimatorStateMachine sm, HashSet<AnimationClip> set)
{
    foreach (var st in sm.states) { var c = st.state.motion as AnimationClip; if (c != null) set.Add(c); }
    foreach (var sub in sm.stateMachines) CollectClips(sub.stateMachine, set);
}

var sb = new StringBuilder();
string ctrlPath = "Assets/_Project/Animations/Player.controller";
var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(ctrlPath);
if (ctrl == null) return "ERR: 控制器找不到 " + ctrlPath;

sb.AppendLine("### A. 控制器层（关键：有几层、遮罩、混合权重）");
foreach (var L in ctrl.layers)
{
    string mask = L.avatarMask != null ? L.avatarMask.name : "<无>";
    sb.AppendLine(string.Format("层[{0}] \"{1}\"  遮罩={2}  混合={3}  默认权重={4}  IK={5}",
        L.name == null ? "?" : L.name, L.name, mask, L.blendingMode, L.defaultWeight, L.iKPass));
}

sb.AppendLine();
sb.AppendLine("### B. 层0 全部状态");
DumpStates(sb, ctrl.layers[0].stateMachine, "  ");
for (int i = 1; i < ctrl.layers.Length; i++)
{
    sb.AppendLine(string.Format("### B{0}. 层{1} \"{2}\"", i, i, ctrl.layers[i].name));
    DumpStates(sb, ctrl.layers[i].stateMachine, "  ");
}

sb.AppendLine();
sb.AppendLine("### C. 预制体上的 CombatStance 片段指向");
var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Player/Player.prefab");
if (prefab != null)
{
    foreach (var mb in prefab.GetComponentsInChildren<MonoBehaviour>(true))
    {
        if (mb == null) continue;
        string tn = mb.GetType().Name;
        if (tn != "CombatStance") continue;
        var so = new SerializedObject(mb);
        var it = so.GetIterator();
        while (it.NextVisible(true))
        {
            if (it.propertyType == SerializedPropertyType.ObjectReference
                && it.name.ToLower().Contains("clip"))
            {
                var o = it.objectReferenceValue;
                sb.AppendLine(string.Format("  {0}.{1} = {2}", tn, it.name,
                    o == null ? "<NULL>" : o.name + "   (" + AssetDatabase.GetAssetPath(o) + ")"));
            }
        }
    }
}

sb.AppendLine();
sb.AppendLine("### D. 控制器引用的 AnimationClip 循环状态");
var seen = new HashSet<AnimationClip>();
for (int li = 0; li < ctrl.layers.Length; li++) CollectClips(ctrl.layers[li].stateMachine, seen);
foreach (var c in seen.Where(x => x != null).OrderBy(x => x.name))
{
    sb.AppendLine(string.Format("  {0,-34} len={1:F3}  isLooping={2}  {3}",
        c.name, c.length, c.isLooping, AssetDatabase.GetAssetPath(c)));
}

string outPath = "Tools/reports/q_pose3.txt";
File.WriteAllText(outPath, sb.ToString(), new UTF8Encoding(false));
return sb.ToString();
