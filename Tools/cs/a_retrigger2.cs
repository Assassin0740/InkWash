// a_retrigger2.cs —— 复现第二轮：更贴近真实操作的三种时机
//
// 变体A（上一脚本）测的是「逻辑阶段回到 Locomotion 那一帧再按」，结果**正常**。
// 说明用户遇到的不是那个时机。真实玩家说"攻击结束"看的是**画面**：
// 刀收回来、人不再摆架势 —— 那一刻 Animator 其实还停在 Atk1Rec，
// 只是正在往 Idle 做混合（0.16 归一化时长）。所以这里补三个变体：
//   C：后摇过渡**进行中**就再按（玩家眼里「刚结束」）
//   D：连点（真实玩家的连续敲击）
//   E：跑动中攻击，结束后立刻再攻击
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
    if (ph == null) { Debug.LogError("[a_retrigger2] 找不到 PlayerHealth"); yield break; }
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

    // 变体 C：等到 Atk1Rec 开始往 Idle 混合的那一刻再按
    yield return VariantC(sb, ctl, anim);
    // 变体 D：连点
    yield return VariantD(sb, ctl, anim);
    // 变体 E：跑动中攻击
    yield return VariantE(sb, ctl, anim);

    ctl.SetInjectedMove(Vector2.zero, true);
    ctl.EndInputOverride();
    if (stance != null) stance.combatExitDelay = 6f;
    File.WriteAllText(Path.Combine(Application.dataPath, "../Tools/reports/a_retrigger2.txt"), sb.ToString());
    Debug.Log("[a_retrigger2] done");
    yield return null;
}

// ---------------- 变体 C ----------------
IEnumerator VariantC(StringBuilder sb, PlayerController ctl, Animator anim)
{
    sb.AppendLine("========== 变体C：后摇往 Idle 混合的途中再按（玩家眼里「刚结束」） ==========");
    ctl.ResetToLocomotion();
    { float t = Time.unscaledTime; while (Time.unscaledTime - t < 0.35f) yield return null; }
    ctl.RequestInjectedAttack();

    int f = 0, fireFrame = -1;
    while (f < 300)
    {
        yield return null; f++;
        if (f % 1 == 0 && (f < 12 || anim.IsInTransition(0) || ctl.Phase != ActionPhase.Attack))
            sb.AppendLine(L(anim, ctl, f, "刀1"));

        // 一旦发现「当前是 Atk1Rec 且正在往某个非攻击状态混合」= 后摇退出开始
        if (fireFrame < 0 && anim.IsInTransition(0))
        {
            string cur = SN(anim), nxt = SN(anim.GetNextAnimatorStateInfo(0));
            if (cur == "Atk1Rec" && (nxt == "Idle" || nxt == "Walk" || nxt == "Run"))
            { fireFrame = f; break; }
        }
    }
    sb.AppendLine("        ↑ 第 " + fireFrame + " 帧：Atk1Rec 开始往 Idle 混合 —— 此刻再按攻击");
    // 坑：迭代器方法里不能 `return;`（CS1622），必须 `yield break;`
    if (fireFrame < 0) { sb.AppendLine(">>> 没等到后摇退出，异常"); yield break; }

    ctl.RequestInjectedAttack();
    for (int i = 1; i <= 50; i++)
    {
        yield return null;
        sb.AppendLine(L(anim, ctl, fireFrame + i, "刀2"));
    }
    sb.AppendLine();
}

// ---------------- 变体 D：连点 ----------------
IEnumerator VariantD(StringBuilder sb, PlayerController ctl, Animator anim)
{
    sb.AppendLine("========== 变体D：连点 6 下（每 0.25s 一次，真实玩家敲击） ==========");
    ctl.ResetToLocomotion();
    { float t = Time.unscaledTime; while (Time.unscaledTime - t < 0.35f) yield return null; }

    var seq = new StringBuilder();
    int f = 0;
    float nextClick = 0f;
    int clicks = 0;
    while (f < 260 && clicks < 6)
    {
        yield return null; f++;
        float u = Time.unscaledTime;
        if (u >= nextClick) { ctl.RequestInjectedAttack(); clicks++; nextClick = u + 0.25f; seq.AppendLine("        [点击" + clicks + " @f" + f + "] 段=" + ctl.ComboStep + " 动画=" + SN(anim)); }
        seq.AppendLine(L(anim, ctl, f, "刀" + ctl.ComboStep));
    }
    sb.AppendLine(seq.ToString());
    sb.AppendLine(">>> 6 次点击的落点与时序如上；检查是否出现「段位涨了但动画却没进对应状态」。");
    sb.AppendLine();
}

// ---------------- 变体 E：跑动中攻击 ----------------
IEnumerator VariantE(StringBuilder sb, PlayerController ctl, Animator anim)
{
    sb.AppendLine("========== 变体E：跑动中攻击 → 结束后立刻再攻击 ==========");
    ctl.ResetToLocomotion();
    ctl.SetInjectedMove(new Vector2(0f, 1f), true);       // 按住前进（默认跑档）
    { float t = Time.unscaledTime; while (Time.unscaledTime - t < 1.0f) yield return null; }
    sb.AppendLine("  跑起来后：动画=" + SN(anim) + " 实际速度=" + ctl.CurrentSpeed.ToString("0.00")
                  + " 动画速=" + anim.speed.ToString("0.##"));

    ctl.RequestInjectedAttack();
    int f = 0;
    while (f < 300)
    {
        yield return null; f++;
        if (f <= 14) sb.AppendLine(L(anim, ctl, f, "刀1"));
        if (ctl.Phase != ActionPhase.Attack) break;
    }
    sb.AppendLine("  刀1 结束 @f" + f + " 动画=" + SN(anim));

    ctl.RequestInjectedAttack();
    for (int i = 1; i <= 40; i++)
    {
        yield return null;
        sb.AppendLine(L(anim, ctl, f + i, "刀2"));
    }
    ctl.SetInjectedMove(Vector2.zero, true);
    sb.AppendLine();
}

static string L(Animator anim, PlayerController ctl, int f, string tag)
{
    string cur = SN(anim);
    bool inTr = anim != null && anim.IsInTransition(0);
    string nxt = inTr ? SN(anim.GetNextAnimatorStateInfo(0)) : "-";
    float nt = anim != null ? anim.GetCurrentAnimatorStateInfo(0).normalizedTime % 1f : 0f;
    return "  f" + f.ToString("000") + " [" + tag + "] 阶段=" + ctl.Phase
         + " 段=" + ctl.ComboStep
         + " 相时=" + ctl.PhaseTime.ToString("0.000")
         + " 动画=" + cur + "@" + nt.ToString("0.00")
         + (inTr ? " →" + nxt : "")
         + " 取消窗=" + (ctl.IsCancelWindowOpen ? "开" : "关")
         + " 见过攻态=" + (ctl.AttackStateSeen ? "是" : "否")
         + " 速=" + anim.speed.ToString("0.##");
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
