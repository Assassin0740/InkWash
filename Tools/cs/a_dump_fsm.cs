// a_dump_fsm.cs —— 把 Player.controller 的状态机**结构化**导出（YAML 读起来太绕）。
//
// 排查「攻击结束后立刻再攻击，卡在中间怪姿势」时最需要的就是这张表：
// 每个状态有哪些出边、出边是不是 AnyState、HasExitTime/exitTime/过渡时长各是多少、
// 触发条件是什么。所有这些在 YAML 里都是数字 id，只有从 Unity 里走 API 才是可读的。
using System.Collections;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

IEnumerator Body()
{
    var sb = new StringBuilder();
    yield return null;

    const string path = "Assets/_Project/Animations/Player.controller";
    var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
    if (ctrl == null) { Debug.LogError("[a_dump_fsm] 找不到 " + path); yield break; }

    sb.AppendLine("控制器 = " + path + "   参数：" + ctrl.parameters.Length + "   层：" + ctrl.layers.Length);
    foreach (var p in ctrl.parameters)
        sb.AppendLine("  param " + p.name + " : " + p.type + "  default=" + p.defaultBool
            + "/" + p.defaultFloat + "/" + p.defaultInt);

    for (int L = 0; L < ctrl.layers.Length; L++)
    {
        var layer = ctrl.layers[L];
        sb.AppendLine();
        // 坑：本版本（团结引擎）的 AnimatorControllerLayer **没有** ikPass 成员，
        // 直接写会 CS1061 编译失败。需要哪个成员先反射探针确认，别照搬国际版文档。
        sb.AppendLine("=== 层 " + L + " \"" + layer.name + "\"  权重=" + layer.defaultWeight
            + "  blending=" + layer.blendingMode + " ===");
        if (layer.stateMachine == null) continue;
        DumpSM(sb, layer.stateMachine, "  ", 0);
    }

    System.IO.File.WriteAllText(System.IO.Path.Combine(
        System.IO.Path.GetDirectoryName(Application.dataPath),
        "Tools/reports/a_fsm.txt"), sb.ToString());
    Debug.Log("[a_dump_fsm] done");
    yield return null;
}

void DumpSM(StringBuilder sb, AnimatorStateMachine sm, string ind, int depth)
{
    if (depth > 3) return;
    sb.AppendLine(ind + "状态机 \"" + sm.name + "\"  默认状态 = "
        + (sm.defaultState != null ? sm.defaultState.name : "(无)"));

    foreach (var cs in sm.states)
    {
        var st = cs.state;
        sb.AppendLine(ind + "  [状态] " + st.name
            + "   motion=" + (st.motion != null ? st.motion.name + "(" + st.motion.GetType().Name + ")" : "null")
            + " speed=" + st.speed + "  speedParam=" + (st.speedParameterActive ? st.speedParameter : "-")
            + "  writeDefaults=" + st.writeDefaultValues
            + "  mirror=" + st.mirror);
    }

    // AnyState 转移：攻击/冲刺一律走这里，最容易配错
    sb.AppendLine(ind + "  -- AnyState 转移 --");
    foreach (var t in sm.anyStateTransitions) DumpT(sb, t, ind + "    ");

    // 全局转移
    if (sm.entryTransitions.Length > 0)
    {
        sb.AppendLine(ind + "  -- Entry 转移 --");
        foreach (var t in sm.entryTransitions)
            foreach (var d in t.destinationStateMachine != null
                ? new[] { t.destinationStateMachine.name }
                : new[] { t.destinationState != null ? t.destinationState.name : "?" })
                sb.AppendLine(ind + "    " + t.conditions.Length + " 个条件 → " + d);
    }

    foreach (var cs in sm.states)
    {
        var st = cs.state;
        sb.AppendLine(ind + "  -- \"" + st.name + "\" 的出边 --");
        foreach (var t in st.transitions) DumpT(sb, t, ind + "    ");
        if (st.transitions.Length == 0) sb.AppendLine(ind + "    (无)");
    }

    foreach (var sub in sm.stateMachines)
    {
        sb.AppendLine(ind + "  [子状态机] " + sub.stateMachine.name);
        DumpSM(sb, sub.stateMachine, ind + "      ", depth + 1);
    }
}

void DumpT(StringBuilder sb, AnimatorStateTransition t, string ind)
{
    string dst = t.destinationState != null ? "状态\"" + t.destinationState.name + "\""
               : t.destinationStateMachine != null ? "子机\"" + t.destinationStateMachine.name + "\""
               : t.isExit ? "Exit" : "?";
    sb.AppendLine(ind + "→ " + dst
        + "   hasExitTime=" + t.hasExitTime
        + "  exitTime=" + F(t.exitTime)
        + "  时长=" + F(t.duration)
        + "  offset=" + F(t.offset)
        + "  interrupt=" + t.interruptionSource
        + "  有序中断=" + t.orderedInterruption
        + "  可自转移=" + t.canTransitionToSelf
        + "  mute=" + t.mute
        + "  solo=" + t.solo
        + "  条件[" + t.conditions.Length + "]=" + Conds(t));
}

string Conds(AnimatorStateTransition t)
{
    if (t.conditions.Length == 0) return "(无)";
    var s = new StringBuilder();
    for (int i = 0; i < t.conditions.Length; i++)
    {
        if (i > 0) s.Append(" && ");
        s.Append(t.conditions[i].parameter);
        switch (t.conditions[i].mode)
        {
            case AnimatorConditionMode.If: s.Append(" == true"); break;
            case AnimatorConditionMode.IfNot: s.Append(" == false"); break;
            case AnimatorConditionMode.Greater: s.Append(" > " + F(t.conditions[i].threshold)); break;
            case AnimatorConditionMode.Less: s.Append(" < " + F(t.conditions[i].threshold)); break;
            case AnimatorConditionMode.Equals: s.Append(" == " + F(t.conditions[i].threshold)); break;
            case AnimatorConditionMode.NotEqual: s.Append(" != " + F(t.conditions[i].threshold)); break;
        }
    }
    return s.ToString();
}

string F(float v) { return v.ToString("0.###"); }

return Body();
