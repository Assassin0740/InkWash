using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// ① 移动片段去 Q 版化（用户：走路还是张臂的）
//      Walk : KayKit Walking_A  → Feng 自带 Walk（同作者同骨架，外展峰值 21°、>55° 占比 0%）
//      Run  : KayKit Running_A  → KevinIglesias Run01_Forward（单步 1.24m 合理）
// ② 攻击提速（用户：动作有点慢）
//      斩击本体 ×1.25（保留力量感），收招段 ×1.40（"慢"的主要来源，占一轮连击 69%）
string ctrlPath = "Assets/_Project/Animations/Player.controller";
string fengWalk = "Assets/Char_Feng/Animation/Walk.anim";
string kiRun = "Assets/ThirdParty/KevinIglesias/Animations/Male/Movement/Run/HumanM@Run01_Forward.fbx";

var sb = new System.Text.StringBuilder();
var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(ctrlPath);
if (ctrl == null) return "!! 找不到 " + ctrlPath;

AnimationClip FromFbx(string path)
{
    foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path))
    {
        var c = o as AnimationClip;
        if (c != null && !c.name.StartsWith("__preview__")) return c;
    }
    return null;
}

var fengWalkClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(fengWalk);
var kiRunClip = FromFbx(kiRun);
sb.AppendLine("Feng Walk  = " + (fengWalkClip != null ? fengWalkClip.name + " " + fengWalkClip.length.ToString("F3") + "s" : "<null>"));
sb.AppendLine("KI  Run    = " + (kiRunClip != null ? kiRunClip.name + " " + kiRunClip.length.ToString("F3") + "s" : "<null>"));
sb.AppendLine();

var sm = ctrl.layers[0].stateMachine;
var found = new Dictionary<string, AnimatorState>();
System.Action<AnimatorStateMachine> walk = null;
walk = (m) =>
{
    foreach (var cs in m.states) if (!found.ContainsKey(cs.state.name)) found[cs.state.name] = cs.state;
    foreach (var sub in m.stateMachines) walk(sub.stateMachine);
};
walk(sm);

// ---- ① 换移动片段 ----
var swap = new (string state, AnimationClip clip)[]
{
    ("Walk", fengWalkClip),
    ("Run",  kiRunClip),
};
foreach (var (state, clip) in swap)
{
    if (clip == null) { sb.AppendLine("!! 片段为空，跳过 " + state); continue; }
    if (!found.TryGetValue(state, out var st)) { sb.AppendLine("!! 缺状态 " + state); continue; }
    string old = st.motion is AnimationClip oc ? oc.name + " (" + oc.length.ToString("F3") + "s)" : "<none>";
    st.motion = clip;
    sb.AppendLine(state.PadRight(5) + "  " + old + "  →  " + clip.name + " (" + clip.length.ToString("F3") + "s)");
}
sb.AppendLine();

// ---- ② 攻击提速 ----
// 主段 1.25（斩击仍利落，不飘）/ 收招 1.40（把 0.97~1.03s 的等待感砍掉）
var speeds = new (string state, float speed, string why)[]
{
    ("Atk1",    1.25f, "斩击本体"),
    ("Atk1Rec", 1.40f, "收招"),
    ("Atk2",    1.25f, "斩击本体"),
    ("Atk2Rec", 1.40f, "收招"),
    ("Atk3",    1.25f, "重斩"),
};
foreach (var (state, spd, why) in speeds)
{
    if (!found.TryGetValue(state, out var st)) { sb.AppendLine("!! 缺状态 " + state); continue; }
    float oldLen = st.motion is AnimationClip mc ? mc.length : 0f;
    sb.AppendLine(state.PadRight(8) + " speed " + st.speed.ToString("F2") + " → " + spd.ToString("F2")
        + "   （" + why + "；有效时长 "
        + (oldLen * Mathf.Min(1f, st.transitions.Length > 0 && st.transitions[0].hasExitTime ? st.transitions[0].exitTime : 1f) / st.speed).ToString("F3")
        + "s → " + (oldLen * Mathf.Min(1f, st.transitions.Length > 0 && st.transitions[0].hasExitTime ? st.transitions[0].exitTime : 1f) / spd).ToString("F3") + "s）");
    st.speed = spd;
}

EditorUtility.SetDirty(ctrl);
AssetDatabase.SaveAssets();
AssetDatabase.Refresh();
Debug.Log("[q_swap_move] 完成");
return sb.ToString();
