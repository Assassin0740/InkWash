// v45a: 补齐攻击链出边（v44b 手术只落了状态/motion，转移没写进磁盘）
// 参数全部来自 git 历史 9ce119e~1 的老 Player.controller（Tools/parse_old_controller.py）
using System.Collections;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;

IEnumerator Body()
{
    var sb = new System.Text.StringBuilder();
    const string ctrlPath = "Assets/_Project/Animations/Player.controller";
    var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(ctrlPath);
    if (ctrl == null) { Debug.LogError("[v45a] no controller"); yield break; }
    var sm = ctrl.layers[0].stateMachine;

    System.Func<string, AnimatorState> find = n =>
    {
        foreach (var s in sm.states) if (s.state.name == n) return s.state;
        return null;
    };
    var names = new[] { "Atk1", "Atk2", "Atk3", "Atk1Rec", "Atk2Rec", "Idle", "Walk", "Run", "Dash" };
    var st = new System.Collections.Generic.Dictionary<string, AnimatorState>();
    foreach (var n in names)
    {
        var s = find(n);
        st[n] = s;
        sb.AppendLine("state " + n + " = " + (s != null ? "OK speed=" + s.speed.ToString("0.##")
            + " motion=" + (s.motion != null ? s.motion.name : "none") : "MISSING"));
    }

    sb.AppendLine("--- BEFORE out-edges ---");
    foreach (var n in new[] { "Atk1", "Atk2", "Atk3", "Atk1Rec", "Atk2Rec" })
        sb.AppendLine("  " + n + " out=" + (st[n] != null ? st[n].transitions.Length.ToString() : "?"));

    // Idle->Walk 上的移动条件（转给 AtkRec/Atk3 -> Walk/Run 用）
    AnimatorCondition[] moveCond = null;
    if (st["Idle"] != null && st["Walk"] != null)
        foreach (var t in st["Idle"].transitions)
            if (t.destinationState == st["Walk"]) { moveCond = t.conditions; break; }
    sb.AppendLine("moveCond=" + (moveCond != null ? moveCond.Length.ToString() : "null"));

    if (st["Atk1"] == null || st["Atk2"] == null || st["Atk3"] == null
        || st["Atk1Rec"] == null || st["Atk2Rec"] == null)
    {
        Debug.LogError("[v45a] missing attack states, abort");
        Debug.Log("[v45a]\n" + sb.ToString());
        yield break;
    }

    // 幂等：只在缺失时补。找不到就建，用 AddTransition(dst) 的返回实例（比 new 更稳）
    System.Func<AnimatorState, AnimatorState, AnimatorStateTransition> ensure = (src, dst) =>
    {
        foreach (var t in src.transitions)
            if (t.destinationState == dst) return t;
        return src.AddTransition(dst);
    };

    int added = 0;
    System.Action<AnimatorState, AnimatorState, float, bool, float, int, string> cfg =
        (src, dst, exit, hasExit, dur, intsrc, cond) =>
        {
            var t = ensure(src, dst);
            bool wasNew = t.conditions.Length == 0 && t.duration == 0f;
            t.hasExitTime = hasExit;
            t.exitTime = exit;
            t.duration = dur;
            t.interruptionSource = (TransitionInterruptionSource)intsrc;
            if (!string.IsNullOrEmpty(cond))
            {
                bool has = false;
                foreach (var c in t.conditions) if (c.parameter == cond) has = true;
                if (!has) t.AddCondition(AnimatorConditionMode.If, 0f, cond);
            }
            if (wasNew) added++;
        };

    // ---- 老拓扑逐条复刻 ----
    cfg(st["Atk1"], st["Atk1Rec"], 0.95f, true, 0.08f, 0, null);
    cfg(st["Atk2"], st["Atk2Rec"], 0.95f, true, 0.08f, 0, null);

    cfg(st["Atk1Rec"], st["Atk2"], 0f, false, 0.06f, 0, "Attack2");
    cfg(st["Atk2Rec"], st["Atk3"], 0f, false, 0.06f, 0, "Attack3");

    foreach (var rec in new[] { st["Atk1Rec"], st["Atk2Rec"] })
    {
        cfg(rec, st["Dash"], 0.3f, true, 0.08f, 1, "Dash");
        cfg(rec, st["Idle"], 0.85f, true, 0.16f, 1, null);
        cfg(rec, st["Walk"], 0.55f, true, 0.16f, 1, null);
        cfg(rec, st["Run"], 0.55f, true, 0.16f, 1, null);
        if (moveCond != null)
            foreach (var t in rec.transitions)
                if (t.destinationState == st["Walk"] || t.destinationState == st["Run"])
                    foreach (var c in moveCond)
                    {
                        bool has = false;
                        foreach (var e in t.conditions) if (e.parameter == c.parameter) has = true;
                        if (!has) t.AddCondition(c.mode, c.threshold, c.parameter);
                    }
    }

    cfg(st["Atk3"], st["Dash"], 0.45f, true, 0.1f, 1, "Dash");
    cfg(st["Atk3"], st["Idle"], 0.72f, true, 0.2f, 1, null);
    cfg(st["Atk3"], st["Walk"], 0.7f, true, 0.2f, 1, null);
    cfg(st["Atk3"], st["Run"], 0.7f, true, 0.2f, 1, null);
    if (moveCond != null)
        foreach (var t in st["Atk3"].transitions)
            if (t.destinationState == st["Walk"] || t.destinationState == st["Run"])
                foreach (var c in moveCond)
                {
                    bool has = false;
                    foreach (var e in t.conditions) if (e.parameter == c.parameter) has = true;
                    if (!has) t.AddCondition(c.mode, c.threshold, c.parameter);
                }

    // 进攻入口（Idle/Walk/Run/Dash -> Atk1）
    foreach (var src in new[] { st["Idle"], st["Walk"], st["Run"], st["Dash"] })
    {
        if (src == null) continue;
        cfg(src, st["Atk1"], src == st["Dash"] ? 0.6f : 0f, src == st["Dash"], 0.08f, 0, "Attack1");
    }

    EditorUtility.SetDirty(ctrl);
    AssetDatabase.SaveAssets();

    sb.AppendLine("--- AFTER out-edges (added=" + added + ") ---");
    foreach (var n in new[] { "Atk1", "Atk2", "Atk3", "Atk1Rec", "Atk2Rec", "Idle", "Walk", "Run", "Dash" })
    {
        if (st[n] == null) continue;
        sb.Append("  " + n + " ->");
        foreach (var t in st[n].transitions)
        {
            string cs = "";
            foreach (var c in t.conditions) cs += c.parameter + "/" + c.mode + " ";
            sb.Append(" " + (t.destinationState ? t.destinationState.name : "?")
                      + "[exit" + t.exitTime.ToString("0.##") + (t.hasExitTime ? "" : ",noExit")
                      + " d" + t.duration.ToString("0.##") + " int" + (int)t.interruptionSource
                      + (cs.Length > 0 ? " " + cs : "") + "]");
        }
        sb.AppendLine();
    }

    sb.AppendLine("--- AnyState ---");
    foreach (var t in sm.anyStateTransitions)
    {
        string cs = "";
        foreach (var c in t.conditions) cs += c.parameter + " ";
        sb.AppendLine("  -> " + (t.destinationState ? t.destinationState.name : "?") + " [" + cs + "]");
    }

    Debug.Log("[v45a]\n" + sb.ToString());
    yield break;
}

return Body();
