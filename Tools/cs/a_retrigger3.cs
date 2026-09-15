// a_retrigger3.cs —— 覆盖 _restartQueued 这条新代码路径（第 3 段收招期间按键）
//
// 背景：`TryAttack` 原先在 `_comboStep >= 3` 时直接 `return false`，玩家的按键被**丢掉**。
// 第 3 段收招 + 后摇混合实测 0.5s 以上，远超 attackInputBuffer(0.25s)，
// 表现就是"我明明按了，人物没反应"。改法是记成"收完重新起手"。
//
// ★ 本脚本第一版把自己写挂了，教训记在这里：
//   对照组是"不按键时不该自动起手"，但第一版的按键是**每 12 帧盲按**，
//   打到第 3 段之后还在按 —— 而"第 3 段期间按"本身就该排队重新起手。
//   于是对照组把一个**正确行为**误判成了泄漏。
//   修法：按键必须**只在取消窗口打开的瞬间**注入，时机由代码控制、不靠盲按。
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;
using InkWash.Effects;
using InkWash.Player;
using InkWash.Roguelike;
using InkWash.UI;

IEnumerator Body()
{
    var sb = new StringBuilder();
    var ph = Object.FindObjectOfType<PlayerHealth>();
    if (ph == null) { Debug.LogError("[a_retrigger3] 找不到 PlayerHealth"); yield break; }
    var go = ph.gameObject;
    var ctl = go.GetComponent<PlayerController>();
    var anim = go.GetComponentInChildren<Animator>(true);
    var spawner = Object.FindObjectOfType<WaveSpawner>();
    var run = Object.FindObjectOfType<RunManager>();
    var panel = Object.FindObjectOfType<InkStylePanel>();
    var choice = Object.FindObjectOfType<SkillChoicePanel>();
    var stance = go.GetComponentInChildren<CombatStance>(true);

    if (panel != null) { panel.visible = false; panel.enabled = false; }
    if (choice != null) choice.Hide();
    ph.maxHealth = 100000f; ph.ResetHealth();
    if (spawner != null) { spawner.ResetForTest(); spawner.spawnInterval = 999f; }
    if (run != null) { run.ResetForTest(); run.SetSeed(20260916); run.nextRoomDelay = 999f; }

    ctl.ResetToLocomotion();
    if (stance != null) { stance.combatExitDelay = 99999f; stance.ForceStance(true); }
    ctl.BeginInputOverride();
    ctl.SetInjectedMove(Vector2.zero, true);
    if (run != null) run.StartRun();
    { float t = Time.unscaledTime; while (Time.unscaledTime - t < 1.6f) yield return null; }

    yield return RunCase(sb, ctl, anim, "变体F：打满三段，在第 3 段取消窗口按下一次 → 应自动重新起手第 1 段", true);
    yield return RunCase(sb, ctl, anim, "对照G：打满三段，之后**一次都不按** → 应安静回 Locomotion，不得有幽灵攻击", false);

    ctl.SetInjectedMove(Vector2.zero, true);
    ctl.EndInputOverride();
    if (stance != null) stance.combatExitDelay = 6f;
    File.WriteAllText(Path.Combine(Application.dataPath, "../Tools/reports/a_retrigger3.txt"), sb.ToString());
    Debug.Log("[a_retrigger3] done");
    yield return null;
}

IEnumerator RunCase(StringBuilder sb, PlayerController ctl, Animator anim, string title, bool pressOnThirdWindow)
{
    sb.AppendLine("========== " + title + " ==========");
    ctl.ResetToLocomotion();
    { float t = Time.unscaledTime; while (Time.unscaledTime - t < 0.6f) yield return null; }

    ctl.RequestInjectedAttack();          // 起手第 1 段
    int f = 0;
    int pressed = 1;
    bool pressedThird = false;
    int thirdPressFrame = -1;
    int restartedAt = -1;

    // 三段推进：**只在取消窗口打开的帧**注入下一次攻击
    int end = 900;
    while (f < end)
    {
        yield return null; f++;
        sb.AppendLine(L(anim, ctl, f, "推段"));

        bool window = ctl.IsCancelWindowOpen;

        // 目标：推进到第 3 段
        if (ctl.ComboStep < 3 && ctl.ComboStep >= 1 && window)
        {
            ctl.RequestInjectedAttack(); pressed++;
            continue;
        }

        // 已经到第 3 段：窗口一开就是"按下去"的时刻
        if (ctl.ComboStep == 3 && window)
        {
            if (pressOnThirdWindow && !pressedThird)
            {
                pressedThird = true; thirdPressFrame = f;
                sb.AppendLine("        ↑ 第 " + f + " 帧：第 3 段取消窗口开着，按一次（逻辑上不能接第 4 段）");
                ctl.RequestInjectedAttack();
            }
            else if (!pressOnThirdWindow)
            {
                sb.AppendLine("        ↑ 第 " + f + " 帧：第 3 段取消窗口开着 —— 本组**不按**，看它会不会自动起手");
            }
        }

        // 观察：第 3 段之后有没有冒出新的第 1 段
        if (ctl.ComboStep == 1 && ctl.Phase == ActionPhase.Attack && restartedAt < 0
            && pressedThird)
            restartedAt = f;

        // 退出条件：第 3 段收完、回到 Locomotion 后再多观察 60 帧
        if (ctl.Phase == ActionPhase.Locomotion && ctl.ComboStep == 0 && f > 200)
        {
            int extra = 60;
            while (extra-- > 0) { yield return null; f++; sb.AppendLine(L(anim, ctl, f, "观察")); }
            // 本组只要没出现"第 1 段攻击"就算通过
            if (pressOnThirdWindow)
                sb.AppendLine(restartedAt > 0
                    ? ">>> [通过] 第 " + restartedAt + " 帧自动重新起手第 1 段（按下于 " + thirdPressFrame
                      + "，延迟 " + (restartedAt - thirdPressFrame) + " 帧）—— 这次按键没有被丢掉。"
                    : ">>> [未通过] 第 3 段收招期间按下的那一次**没有**兑现。");
            else
                sb.AppendLine(restartedAt > 0
                    ? ">>> [未通过] 没按键却自动起手了 —— _restartQueued 泄漏。"
                    : ">>> [通过] 没按键时安静回 Locomotion，没有幽灵攻击。");
            sb.AppendLine("  （本组共注入攻击 " + pressed + " 次）");
            sb.AppendLine();
            yield break;
        }
    }
    sb.AppendLine(">>> 超时（" + end + " 帧）未走完流程，pressOnThirdWindow=" + pressOnThirdWindow
                  + " 到过第3段=" + pressedThird + " 最终段=" + ctl.ComboStep + " 阶段=" + ctl.Phase);
    sb.AppendLine();
}

static string L(Animator anim, PlayerController ctl, int f, string tag)
{
    bool inTr = anim != null && anim.IsInTransition(0);
    return "  f" + f.ToString("000") + " [" + tag + "] 阶段=" + ctl.Phase
         + " 段=" + ctl.ComboStep
         + " 相时=" + ctl.PhaseTime.ToString("0.000")
         + " 动画=" + SN(anim)
         + (inTr ? " →" + SN(anim.GetNextAnimatorStateInfo(0)) : "")
         + " 取消窗=" + (ctl.IsCancelWindowOpen ? "开" : "关");
}

static string SN(Animator anim)
{
    if (anim == null) return "?";
    return SN(anim.GetCurrentAnimatorStateInfo(0));
}
static string SN(AnimatorStateInfo s)
{
    foreach (var n in new[] { "Idle", "Walk", "Run", "Dash", "Atk1Rec", "Atk1", "Atk2Rec", "Atk2", "Atk3" })
        if (s.IsName(n)) return n;
    return "其他";
}

return Body();
