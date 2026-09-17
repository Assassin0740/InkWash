using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// 把 Player.controller 里三段攻击的片段换成 UAL2 剑术库（Quaternius，CC0）
//   Atk1    : Melee_1H_Attack_Slice_Diagonal   → Armature|Sword_Regular_A
//   Atk1Rec : Slice_Diagonal_Rec               → Armature|Sword_Regular_A_Rec
//   Atk2    : Melee_1H_Attack_Slice_Horizontal → Armature|Sword_Regular_B
//   Atk2Rec : Slice_Horizontal_Rec             → Armature|Sword_Regular_B_Rec
//   Atk3    : Melee_1H_Attack_Stab             → Armature|Sword_Regular_C
string ctrlPath = "Assets/_Project/Animations/Player.controller";
string ual2 = "Assets/ThirdParty/Quaternius/UniversalAnimationLibrary2/Unity/UAL2_Standard.fbx";

var sb = new System.Text.StringBuilder();
var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(ctrlPath);
if (ctrl == null) return "!! 找不到 " + ctrlPath;

AnimationClip Src(string name)
{
    foreach (var o in AssetDatabase.LoadAllAssetsAtPath(ual2))
        if (o is AnimationClip c && !c.name.StartsWith("__preview__") && c.name == name) return c;
    return null;
}

var plan = new (string state, string clip, float exitTime)[]
{
    ("Atk1",    "Armature|Sword_Regular_A",     0.95f),
    ("Atk1Rec", "Armature|Sword_Regular_A_Rec", 0.85f),
    ("Atk2",    "Armature|Sword_Regular_B",     0.95f),
    ("Atk2Rec", "Armature|Sword_Regular_B_Rec", 0.85f),
    ("Atk3",    "Armature|Sword_Regular_C",     0.72f),
};

var sm = ctrl.layers[0].stateMachine;
var found = new Dictionary<string, AnimatorState>();
System.Action<AnimatorStateMachine> walk = null;
walk = (m) =>
{
    foreach (var cs in m.states) if (!found.ContainsKey(cs.state.name)) found[cs.state.name] = cs.state;
    foreach (var sub in m.stateMachines) walk(sub.stateMachine);
};
walk(sm);

foreach (var (state, clipName, exitTime) in plan)
{
    if (!found.TryGetValue(state, out var st)) { sb.AppendLine("!! 缺状态 " + state); continue; }
    var clip = Src(clipName);
    if (clip == null) { sb.AppendLine("!! 缺片段 " + clipName); continue; }

    string old = st.motion is AnimationClip oc ? oc.name + " (" + oc.length.ToString("F3") + "s)" : "<none>";
    st.motion = clip;
    sb.AppendLine(state.PadRight(8) + " " + old + "  →  " + clip.name + " (" + clip.length.ToString("F3") + "s)");

    // 只改「本状态自己出去」的那些转移的 exitTime（有 exitTime 的才改）
    foreach (var t in st.transitions)
    {
        if (!t.hasExitTime) continue;
        string dst = t.isExit ? "(Exit)" : (t.destinationState != null ? t.destinationState.name : "?");
        // Atk1/Atk2 出去的转移：把 0.85 提到 0.95，让快斩动作能播完
        if ((state == "Atk1" || state == "Atk2") && t.exitTime >= 0.80f)
        {
            sb.AppendLine("        转移 → " + dst + "  exitTime " + t.exitTime.ToString("F2") + " → " + exitTime.ToString("F2"));
            t.exitTime = exitTime;
        }
    }
}

EditorUtility.SetDirty(ctrl);
AssetDatabase.SaveAssets();
AssetDatabase.Refresh();
Debug.Log("[q_swap_atk] 完成");
return sb.ToString();
