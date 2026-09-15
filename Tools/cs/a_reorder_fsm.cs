// a_reorder_fsm.cs —— 把「动作类转移」提到「移动类转移」之前（幂等）
//
// 【为什么必须重排顺序】
//   Unity 的转移是**按列表顺序求值，先满足者胜**。原始列表里移动类排在前面：
//     Run:  [→Walk(Speed<2.5)] [→Idle(Speed<0.35)] [→Dash] [→Atk1]
//   于是只要移动条件成立，攻击触发器就永远抢不到。而 PushAnimatorState 写 Speed 用的是
//   **阻尼**（SetFloat(..., 0.10f, dt)），攻击开始后 Speed 还要 0.1~0.3s 才落到 0 ——
//   这段窗口里 `Run→Walk` 的条件一直成立。
//   实测（Tools/reports/a_retrigger2.txt 变体E）：
//     跑动中按下攻击后 20 帧，动画一直在 `Walk→Run→Walk` 之间打转，
//     Attack1 触发器挂着没人消费，`见过攻态` 始终为「否」——"按了攻击却在跑步"。
//   变体C 之所以能被修好，纯属运气：`Atk1Rec→Atk2` 本来就排在列表第一位。
//
// 【重排规则】稳定分区，不改变同组内的相对顺序：
//   条件里带 **Trigger** 的转移（Attack1/2/3、Dash）→ 排前面
//   其余（Float/Bool 条件、无条件的 exitTime 出口）→ 排后面
//   为什么用"条件是不是 Trigger"当判据，而不是"目标是不是攻击态"：
//     Trigger 就是"玩家按了一下"，它天然比"当前速度是多少"这种持续状态**优先级更高**。
//     这条规则也能自动覆盖 Dash，不必再单独枚举。
//
// 【为什么要重建而不是交换数组元素】
//   `AnimatorState.transitions` 返回的是**副本数组**，改它不起作用；
//   而先 Remove 再 Add 同一个实例依赖"Remove 不会销毁子资产"这一未文档化的行为。
//   所以这里**先把每条边的全部字段快照下来**，Remove 旧的，再用
//   `AddTransition(dest)` 新建、逐字段回填。哪怕 Remove 真的销毁了对象也不受影响。
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

class EdgeSpec
{
    public string dest;
    public bool hasExitTime;
    public float exitTime, duration, offset;
    public UnityEditor.Animations.TransitionInterruptionSource interruption;
    public bool orderedInterruption, canTransitionToSelf, mute, solo;
    public AnimatorConditionMode[] modes;
    public float[] thresholds;
    public string[] params_;
    public bool triggerDriven;
}

IEnumerator Body()
{
    var sb = new StringBuilder();
    yield return null;

    const string path = "Assets/_Project/Animations/Player.controller";
    var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
    if (ctrl == null) { Debug.LogError("[a_reorder_fsm] 找不到 " + path); yield break; }
    var sm = ctrl.layers[0].stateMachine;

    var triggers = new HashSet<string>(ctrl.parameters
        .Where(p => p.type == AnimatorControllerParameterType.Trigger)
        .Select(p => p.name));
    sb.AppendLine("Trigger 参数 = " + string.Join(", ", triggers));
    sb.AppendLine();

    var byName = new Dictionary<string, AnimatorState>();
    foreach (var cs in sm.states) byName[cs.state.name] = cs.state;

    int changedStates = 0;
    foreach (var cs in sm.states)
    {
        var st = cs.state;
        var specs = new List<EdgeSpec>();
        foreach (var t in st.transitions)
        {
            if (t.destinationState == null) continue;   // 本控制器没有指向子状态机的边
            var sp = new EdgeSpec
            {
                dest = t.destinationState.name,
                hasExitTime = t.hasExitTime,
                exitTime = t.exitTime,
                duration = t.duration,
                offset = t.offset,
                interruption = t.interruptionSource,
                orderedInterruption = t.orderedInterruption,
                canTransitionToSelf = t.canTransitionToSelf,
                mute = t.mute,
                solo = t.solo,
                modes = t.conditions.Select(c => c.mode).ToArray(),
                thresholds = t.conditions.Select(c => c.threshold).ToArray(),
                params_ = t.conditions.Select(c => c.parameter).ToArray(),
            };
            sp.triggerDriven = sp.params_.Length >= 1 && sp.params_.All(p => triggers.Contains(p));
            specs.Add(sp);
        }
        if (specs.Count == 0) continue;

        var ordered = specs.Where(s => s.triggerDriven)
                           .Concat(specs.Where(s => !s.triggerDriven)).ToList();

        bool same = !specs.Where((s, i) => s.dest != ordered[i].dest).Any();
        sb.AppendLine(st.name + "：");
        sb.AppendLine("  原顺序 = " + string.Join(" → ", specs.Select(s => s.dest)));
        sb.AppendLine("  目标   = " + string.Join(" → ", ordered.Select(s => s.dest)));
        if (same) { sb.AppendLine("  = 已符合，跳过"); continue; }

        foreach (var t in st.transitions.ToArray()) st.RemoveTransition(t);

        foreach (var s in ordered)
        {
            var nt = st.AddTransition(byName[s.dest]);
            nt.hasExitTime = s.hasExitTime;
            nt.exitTime = s.exitTime;
            nt.duration = s.duration;
            nt.offset = s.offset;
            nt.interruptionSource = s.interruption;
            nt.orderedInterruption = s.orderedInterruption;
            nt.canTransitionToSelf = s.canTransitionToSelf;
            nt.mute = s.mute;
            nt.solo = s.solo;
            for (int i = 0; i < s.modes.Length; i++)
                nt.AddCondition(s.modes[i], s.thresholds[i], s.params_[i]);
        }
        changedStates++;
        sb.AppendLine("  · 已重排");
    }

    sb.AppendLine();
    sb.AppendLine("重排的状态数 = " + changedStates);
    if (changedStates > 0)
    {
        EditorUtility.SetDirty(ctrl);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        sb.AppendLine("已保存 Player.controller。");
    }

    // ---- 复核：按目标顺序再读一遍，顺便确认字段没在重建中丢失 ----
    sb.AppendLine();
    sb.AppendLine("复核：");
    foreach (var cs in sm.states)
    {
        if (cs.state.transitions.Length == 0) continue;
        sb.AppendLine("  " + cs.state.name + "：");
        foreach (var t in cs.state.transitions)
            sb.AppendLine("    → " + t.destinationState.name
                + "  exitTime=" + (t.hasExitTime ? t.exitTime.ToString("0.###") : "-")
                + "  dur=" + t.duration.ToString("0.###")
                + "  interrupt=" + t.interruptionSource
                + "  自转移=" + t.canTransitionToSelf
                + "  条件=" + (t.conditions.Length == 0 ? "(无)"
                    : string.Join(" && ", t.conditions.Select(c => c.parameter + " " + c.mode + " " + c.threshold))));
    }

    System.IO.File.WriteAllText(System.IO.Path.Combine(
        System.IO.Path.GetDirectoryName(Application.dataPath),
        "Tools/reports/a_reorder_fsm.txt"), sb.ToString());
    Debug.Log("[a_reorder_fsm] done  changedStates=" + changedStates);
    yield return null;
}

return Body();
