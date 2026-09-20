// v44b: 编辑器态手术 —— 攻击链回滚到老 Quaternius 拓扑（用户判定 Mixamo 攻击段太慢/命中错位）
// 参数全部来自 git 历史 9ce119e~1 的老 Player.controller 解析（Tools/parse_old_controller.py）
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;

IEnumerator Body()
{
    var sb = new System.Text.StringBuilder();
    const string ctrlPath = "Assets/_Project/Animations/Player.controller";
    const string ualPath = "Assets/ThirdParty/Quaternius/UniversalAnimationLibrary2/Unity/UAL2_Standard.fbx";
    var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(ctrlPath);
    if (ctrl == null) { Debug.LogError("[v44b] no controller"); yield break; }
    var sm = ctrl.layers[0].stateMachine;

    // ---- UAL2 clips: localId -> clip ----
    var byLid = new System.Collections.Generic.Dictionary<long, AnimationClip>();
    foreach (var o in AssetDatabase.LoadAllAssetsAtPath(ualPath))
    {
        var c = o as AnimationClip;
        if (c == null) continue;
        ulong lid = UnityEditor.Unsupported.GetLocalIdentifierInFileForPersistentObject(c);
        byLid[unchecked((long)lid)] = c;
    }
    var want = new (long id, string role)[] {
        (3625414872004886998L, "Atk1"), (-1662512729948635066L, "Atk2"), (543990450412311274L, "Atk3"),
        (7400775424482280638L, "Atk1Rec"), (7742298430783984577L, "Atk2Rec"),
    };
    var clip = new System.Collections.Generic.Dictionary<string, AnimationClip>();
    foreach (var w in want)
    {
        if (byLid.TryGetValue(w.id, out var c)) { clip[w.role] = c; sb.AppendLine("clip " + w.role + " = " + c.name); }
        else sb.AppendLine("MISSING clip " + w.role);
    }

    System.Func<string, AnimatorState> find = n =>
    {
        foreach (var s in sm.states) if (s.state.name == n) return s.state;
        return null;
    };
    var atk1 = find("Atk1"); var atk2 = find("Atk2"); var atk3 = find("Atk3");
    var idle = find("Idle"); var walk = find("Walk"); var run = find("Run"); var dash = find("Dash");
    var rec1 = find("Atk1Rec"); var rec2 = find("Atk2Rec");
    if (rec1 == null) { rec1 = sm.AddState("Atk1Rec"); }
    if (rec2 == null) { rec2 = sm.AddState("Atk2Rec"); }
    rec1.speed = 1.7f; rec2.speed = 1.7f;
    rec1.motion = clip["Atk1Rec"]; rec2.motion = clip["Atk2Rec"];

    // 换回老片段 + 老速度
    atk1.motion = clip["Atk1"]; atk2.motion = clip["Atk2"]; atk3.motion = clip["Atk3"];
    atk1.speed = 1.25f; atk2.speed = 1.25f; atk3.speed = 1.25f;
    sb.AppendLine("motions: " + atk1.motion.name + " / " + atk2.motion.name + " / " + atk3.motion.name);

    // 参考条件：Idle→Walk 上的 Speed 条件
    AnimatorStateTransition idleWalk = null;
    foreach (var t in idle.transitions) if (t.destinationState == walk) { idleWalk = t; break; }

    // 清空五个状态的全部出边
    foreach (var st in new[] { atk1, atk2, atk3, rec1, rec2 })
    {
        var olds = st.transitions;
        for (int i = olds.Length - 1; i >= 0; i--) st.RemoveTransition(olds[i]);
    }

    System.Func<AnimatorState, AnimatorState, float, float, int, AnimatorStateTransition> mk =
        (src, dst, exit, dur, intsrc) =>
        {
            var t = new AnimatorStateTransition();
            t.destinationState = dst;
            t.hasExitTime = exit > 0f;
            t.exitTime = exit;
            t.duration = dur;
            t.interruptionSource = (TransitionInterruptionSource)intsrc;
            src.AddTransition(t);
            return t;
        };

    // 老拓扑逐条复刻
    mk(atk1, rec1, 0.95f, 0.08f, 0);
    mk(atk2, rec2, 0.95f, 0.08f, 0);

    var t12 = mk(rec1, atk2, 0f, 0.06f, 0); t12.AddCondition(AnimatorConditionMode.If, 0f, "Attack2");
    var t23 = mk(rec2, atk3, 0f, 0.06f, 0); t23.AddCondition(AnimatorConditionMode.If, 0f, "Attack3");

    foreach (var rec in new[] { rec1, rec2 })
    {
        var td = mk(rec, dash, 0.3f, 0.08f, 1); td.AddCondition(AnimatorConditionMode.If, 0f, "Dash");
        mk(rec, idle, 0.85f, 0.16f, 1);
        var tw = mk(rec, walk, 0.55f, 0.16f, 1);
        var tr = mk(rec, run, 0.55f, 0.16f, 1);
        if (idleWalk != null)
            foreach (var c in idleWalk.conditions) { tw.AddCondition(c.mode, c.threshold, c.parameter); tr.AddCondition(c.mode, c.threshold, c.parameter); }
    }

    var t3d = mk(atk3, dash, 0.45f, 0.1f, 1); t3d.AddCondition(AnimatorConditionMode.If, 0f, "Dash");
    mk(atk3, idle, 0.72f, 0.2f, 1);
    var t3w = mk(atk3, walk, 0.7f, 0.2f, 1);
    var t3r = mk(atk3, run, 0.7f, 0.2f, 1);
    if (idleWalk != null)
        foreach (var c in idleWalk.conditions) { t3w.AddCondition(c.mode, c.threshold, c.parameter); t3r.AddCondition(c.mode, c.threshold, c.parameter); }

    // 进攻入口（Idle/Walk/Run/Dash → Atk1）应当仍在，缺了就补
    foreach (var src in new[] { idle, walk, run, dash })
    {
        bool ok = false;
        foreach (var t in src.transitions) if (t.destinationState == atk1) { ok = true; break; }
        if (!ok)
        {
            var t = mk(src, atk1, src == dash ? 0.6f : 0f, 0.08f, 0);
            t.AddCondition(AnimatorConditionMode.If, 0f, "Attack1");
            sb.AppendLine("补进攻入口: " + src.name + " -> Atk1");
        }
    }

    EditorUtility.SetDirty(ctrl);
    AssetDatabase.SaveAssets();

    // 输出最终拓扑
    foreach (var st in new[] { atk1, atk2, atk3, rec1, rec2 })
    {
        sb.Append(st.name + " ->");
        foreach (var t in st.transitions)
            sb.Append(" " + (t.destinationState ? t.destinationState.name : "?")
                      + "(exit" + t.exitTime.ToString("0.##") + (t.hasExitTime ? "" : ",noExit")
                      + ",d" + t.duration.ToString("0.##") + ",int" + (int)t.interruptionSource + ")");
        sb.AppendLine();
    }
    Debug.Log("[v44b]\n" + sb.ToString());
    yield break;
}

return Body();
