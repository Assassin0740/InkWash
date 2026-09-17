// a_fix_fsm.cs —— 让"进行中的过渡"能被攻击/连招打断（幂等）
//
// 【为什么必须改资产而不是只改代码】
//   用户实测：「攻击结束之后立刻再攻击，招式不出来 / 卡在中间一个奇怪的动作」。
//   逐帧追踪（Tools/reports/a_retrigger2.txt）定位到两个入口，根因是同一个：
//     变体C：AI 判定"取消窗口还开着"（Atk1Rec 且 normalizedTime ≥ 0.3）→ AdvanceCombo
//            → SetTrigger(Attack2)。但那一刻状态机已经在跑 `Atk1Rec → Idle` 的退出混合，
//            而该出边的 interruptionSource = None ⇒ **已经开始的过渡无法被打断**，
//            Attack2 触发器根本没人消费，就一直挂着。
//     变体E：玩家按住前进（跑），按下攻击的那一帧动画正在跑 `Walk → Run` 的过渡
//            （同样是 None）⇒ Attack1 触发器被卡住，人还在往跑步演，
//            `Walk→Run` 这个过渡本身还要 0.36s，加上 `Run→Atk1`……
//            实测按下后 12 帧仍停在 Walk→Run 里，"按了攻击却在跑步"。
//
// 【改法】把这些出边的 interruptionSource 设为 Source（=「来自同一源状态的转移可以打断它」）。
//   · 上一帧证据：`Idle/Walk/Run` 之间的过渡 ← 打断方是 `X → Atk1`、`X → Dash`，源同为 X；
//   · 后摇退出 `Atk1Rec/Atk2Rec/Atk3 → Idle/Walk/Run/Dash` ← 打断方是 `Atk1Rec → Atk2` 等。
//   用 Source 而**不是** SourceThenDestination：后者会让目标状态侧也能打断，
//   例如 `Idle→Walk` 被 `Walk→Idle` 打断 ⇒ 原地来回抖。
//
// 【为什么不用反射写这个属性】先探一下真实类型名，避免团结引擎的枚举/成员命名差异
//   （本项目已踩过 ModelImporterAnimationType.Humanoid → 真名 Human）。
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
    if (ctrl == null) { Debug.LogError("[a_fix_fsm] 找不到 " + path); yield break; }
    var sm = ctrl.layers[0].stateMachine;

    // ---- 探针：interruptionSource 的真实类型名（不硬编码，防止版本差异）----
    var probe = sm.states[0].state.transitions[0];
    var prop = probe.GetType().GetProperty("interruptionSource");
    if (prop == null) { Debug.LogError("[a_fix_fsm] 没有 interruptionSource 属性"); yield break; }
    sb.AppendLine("interruptionSource 真实类型 = " + prop.PropertyType.FullName);
    sb.AppendLine("当前值 = " + prop.GetValue(probe));
    sb.AppendLine();

    // 要修的两组：源状态 → 允许被打断的目标集合
    var locomotion = new[] { "Idle", "Walk", "Run" };
    var recovery = new[] { "Atk1Rec", "Atk2Rec", "Atk3" };
    var all = new[] { "Idle", "Walk", "Run", "Dash" };

    int changed = 0, scanned = 0;
    foreach (var cs in sm.states)
    {
        string src = cs.state.name;
        bool srcIsLoco = System.Array.IndexOf(locomotion, src) >= 0 || src == "Dash";
        bool srcIsRec = System.Array.IndexOf(recovery, src) >= 0;
        if (!srcIsLoco && !srcIsRec) continue;

        foreach (var t in cs.state.transitions)
        {
            if (t.destinationState == null) continue;
            string dst = t.destinationState.name;
            scanned++;

            // 只修"在移动/后摇内部换状态"的那些边；进入攻击态（Atk1/Atk2/Atk3）的边本身不需要被谁打断
            bool shouldBeInterruptible = System.Array.IndexOf(all, dst) >= 0 && dst != src;
            if (!shouldBeInterruptible) continue;

            string before = prop.GetValue(t).ToString();
            bool isSource = before == "Source";
            sb.AppendLine((isSource ? "  = 已是 " : "  · 修正 ") + src + " → " + dst
                          + "   interruptionSource: " + before + " → Source"
                          + "  (hasExitTime=" + t.hasExitTime + " exitTime=" + t.exitTime.ToString("0.###") + ")");
            if (!isSource)
            {
                prop.SetValue(t, System.Enum.Parse(prop.PropertyType, "Source"));
                EditorUtility.SetDirty(ctrl);
                changed++;
            }
        }
    }

    sb.AppendLine();
    sb.AppendLine("扫描出边 " + scanned + " 条，本次改动 " + changed + " 条。");
    if (changed > 0)
    {
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        sb.AppendLine("已保存 Player.controller。");
    }
    else
    {
        sb.AppendLine("无需改动（幂等）。");
    }

    // ---- 复核：把所有边的 interruptionSource 打一遍 ----
    sb.AppendLine();
    sb.AppendLine("复核（全部出边）：");
    foreach (var cs in sm.states)
        foreach (var t in cs.state.transitions)
            sb.AppendLine("  " + cs.state.name + " → "
                + (t.destinationState != null ? t.destinationState.name : "?")
                + "   interrupt=" + prop.GetValue(t));

    System.IO.File.WriteAllText(System.IO.Path.Combine(
        System.IO.Path.GetDirectoryName(Application.dataPath),
        "Tools/reports/a_fix_fsm.txt"), sb.ToString());
    Debug.Log("[a_fix_fsm] done  changed=" + changed);
    yield return null;
}

return Body();
