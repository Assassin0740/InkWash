// v45c: 死亡动画只在 Idle 下能播 —— 补 AnyState->Death 并提到最高优先级
// 现状：YAML 里只有 Idle -> Death(cond Dead)。跑步/攻击中被打死时状态机进不去 Death，
//       Dead 触发器挂住不生效，玩家倒地却不播死亡动画（只有程序化兜底）。
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using System.Text;
using System.Collections.Generic;

var sb = new System.Text.StringBuilder();
var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>("Assets/_Project/Animations/Player.controller");
if (ctrl == null) return "no controller";
var sm = ctrl.layers[0].stateMachine;

AnimatorState death = null;
foreach (var s in sm.states) if (s.state.name == "Death") death = s.state;
if (death == null) return "no Death state";

// 已有的 AnyState 转移
var olds = new List<AnimatorStateTransition>(sm.anyStateTransitions);
sb.AppendLine("BEFORE anyState: " + olds.Count);
foreach (var t in olds)
    sb.AppendLine("   -> " + (t.destinationState ? t.destinationState.name : "?"));

// 若已存在 ->Death 就复用，否则新建
AnimatorStateTransition dt = null;
foreach (var t in olds)
    if (t.destinationState == death) dt = t;
if (dt == null)
{
    dt = sm.AddAnyStateTransition(death);
    sb.AppendLine("created AnyState->Death");
}
dt.hasExitTime = false;
dt.exitTime = 0f;
dt.duration = 0.15f;
dt.interruptionSource = TransitionInterruptionSource.None;
bool hasDead = false;
foreach (var c in dt.conditions) if (c.parameter == "Dead") hasDead = true;
if (!hasDead) dt.AddCondition(AnimatorConditionMode.If, 0f, "Dead");
dt.canTransitionToSelf = false;

// 重排：Death 放最前（AnyState 转移按数组顺序求值，死亡必须压过 Hit/Cast）
var list = new List<AnimatorStateTransition>(sm.anyStateTransitions);
list.Remove(dt);
list.Insert(0, dt);
sm.anyStateTransitions = list.ToArray();

EditorUtility.SetDirty(ctrl);
AssetDatabase.SaveAssets();

sb.AppendLine("AFTER anyState (顺序=优先级):");
foreach (var t in sm.anyStateTransitions)
{
    string cs = "";
    foreach (var c in t.conditions) cs += c.parameter + " ";
    sb.AppendLine("   -> " + (t.destinationState ? t.destinationState.name : "?")
        + " [d" + t.duration.ToString("0.##") + " " + cs + "]");
}
sb.AppendLine("Death out-edges: " + death.transitions.Length);
return sb.ToString();
