// v37_death_link.cs —— Player.controller 接入 Death.anim（桥上 API 编辑，不手编 YAML）
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

string ctrlPath = "Assets/_Project/Animations/Player.controller";
var ac = AssetDatabase.LoadAssetAtPath<AnimatorController>(ctrlPath);
if (ac == null) { Debug.LogError("[v37] controller 加载失败"); return; }

// 幂等：已有 Dead 参数/Death 状态则跳过
bool hasParam = false;
foreach (var p in ac.parameters) if (p.name == "Dead") { hasParam = true; break; }
if (!hasParam) ac.AddParameter("Dead", AnimatorControllerParameterType.Trigger);

var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Char_Feng/Animation/Death.anim");
if (clip == null) { Debug.LogError("[v37] Death.anim 加载失败"); return; }

var baseLayer = ac.layers[0];
var sm = baseLayer.stateMachine;
UnityEditor.Animations.AnimatorState deathState = null;
foreach (var st in sm.states) if (st.state.name == "Death") { deathState = st.state; break; }

if (deathState == null)
{
    deathState = sm.AddState("Death");
    deathState.motion = clip;

    // 从 Idle 转移：Dead 触发，快速过渡（0.15s），不等 exitTime —— 死亡要立刻切
    var idle = sm.defaultState;
    var trans = idle.AddTransition(deathState);
    trans.AddCondition(AnimatorConditionMode.If, 0f, "Dead");
    trans.hasExitTime = false;
    trans.duration = 0.15f;
    trans.interruptionSource = UnityEditor.Animations.TransitionInterruptionSource.None;
    Debug.Log("[v37] Death 状态已接入（Idle --Dead--> Death, clip=" + clip.name + " len=" + clip.length.ToString("F2") + "s）");
}
else Debug.Log("[v37] Death 状态已存在，跳过");

// Death 状态自身不回退：不加任何 exit 转移（停尸在末帧）
EditorUtility.SetDirty(ac);
AssetDatabase.SaveAssets();
Debug.Log("[v37] controller 已保存");
