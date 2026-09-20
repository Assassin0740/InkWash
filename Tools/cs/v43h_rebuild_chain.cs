// v43h: rebuild combo chain in Player.controller
//   Atk1=slash(5) 1.37s@1.25 | Atk2=slash(3) 1.57s@1.25 | Atk3=attack(4) 1.00s@1.0
//   Atk1->Atk2 (Attack2), Atk2->Atk3 (Attack3), ->Idle exits; remove Atk1Rec/Atk2Rec
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using System.Text;

var sb = new System.Text.StringBuilder();
var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>("Assets/_Project/Animations/Player.controller");
if (ctrl == null) return "no controller";
var sm = ctrl.layers[0].stateMachine;

System.Func<string, AnimatorState> find = (n) =>
{
    foreach (var cs in sm.states) if (cs.state.name == n) return cs.state;
    return null;
};
System.Func<string, AnimationClip> clipOf = (path) =>
{
    foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path))
        { var c = o as AnimationClip; if (c != null && !c.name.StartsWith("__preview")) return c; }
    return null;
};

var atk1 = find("Atk1"); var atk2 = find("Atk2"); var atk3 = find("Atk3");
var rec1 = find("Atk1Rec"); var rec2 = find("Atk2Rec"); var idle = find("Idle");
if (atk1 == null || atk2 == null || atk3 == null || rec1 == null || rec2 == null || idle == null)
    return "state missing: " + atk1 + "/" + atk2 + "/" + atk3 + "/" + rec1 + "/" + rec2 + "/" + idle;

var slash5 = clipOf("Assets/Mixamo/sword and shield slash (5).fbx");
var slash3 = clipOf("Assets/Mixamo/sword and shield slash (3).fbx");
var attack4 = clipOf("Assets/Mixamo/sword and shield attack (4).fbx");
if (slash5 == null || slash3 == null || attack4 == null) return "clip missing";

// 1) swap motions + speeds
atk1.motion = slash5; atk1.speed = 1.25f;
atk2.motion = slash3; atk2.speed = 1.25f;
atk3.motion = attack4; atk3.speed = 1.0f;

// 2) wipe Atk1/Atk2 outgoing (the old ->Rec edges)
while (atk1.transitions.Length > 0) atk1.RemoveTransition(atk1.transitions[0]);
while (atk2.transitions.Length > 0) atk2.RemoveTransition(atk2.transitions[0]);

// 3) new edges
System.Func<AnimatorState, AnimatorState, string, float, float, bool, int, AnimatorStateTransition> mk =
    (src, dst, cond, exitT, dur, hasExit, intSrc) =>
{
    var t = new AnimatorStateTransition(); // 团结引擎：非 ScriptableObject 派生，直接 new
    t.destinationState = dst;
    t.duration = dur;
    t.exitTime = exitT;
    t.hasExitTime = hasExit;
    t.interruptionSource = (TransitionInterruptionSource)intSrc;
    t.orderedInterruption = true;
    t.canTransitionToSelf = true;
    if (cond != null)
    {
        var c = new AnimatorCondition { mode = AnimatorConditionMode.If, parameter = cond, threshold = 0f };
        t.AddCondition(c.mode, c.threshold, c.parameter);
    }
    src.AddTransition(t);
    return t;
};
mk(atk1, atk2, "Attack2", 0f, 0.06f, false, 0);   // 接第二段
mk(atk1, idle, null, 0.88f, 0.16f, true, 2);      // 收势回 Idle（Source 可被相邻边打断）
mk(atk2, atk3, "Attack3", 0f, 0.06f, false, 0);
mk(atk2, idle, null, 0.88f, 0.16f, true, 2);

// 4) remove Rec states
sm.RemoveState(rec1);
sm.RemoveState(rec2);

AssetDatabase.SaveAssets();

// 5) verify
foreach (var cs in sm.states)
{
    var a = cs.state;
    var m = a.motion as AnimationClip;
    sb.AppendLine(a.name + " | clip=" + (m == null ? "?" : m.name) + " len=" + (m == null ? -1f : m.length).ToString("F2") + " speed=" + a.speed.ToString("F2"));
    foreach (var t in a.transitions)
        sb.AppendLine("   -> " + (t.destinationState == null ? "EXIT" : t.destinationState.name) +
            " exit=" + t.exitTime.ToString("F2") + "/" + t.hasExitTime + " dur=" + t.duration.ToString("F2") + " int=" + t.interruptionSource);
}
return sb.ToString();
