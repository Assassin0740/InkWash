// a_retrigger.cs —— 复现「攻击结束后立刻再攻击，卡在中间怪姿势」（运行时逐帧追踪）
//
// 为什么必须逐帧追踪而不是看录像：
//   "卡住"可能发生在**任意一帧**，而录像只能看到结果。这里把每一帧的
//   「逻辑阶段 / 连击段 / Animator 当前状态与归一化时间 / 是否在过渡中 / 下一状态」
//   全打出来，卡在哪一环一目了然。
//
// 测两个变体（用户说的"立刻"有两种可能的时机，必须都覆盖）：
//   变体 A：逻辑阶段刚回到 Locomotion 的那一帧就再按攻击
//   变体 B：逻辑阶段回到 Locomotion 之后 5 帧再按攻击
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
    if (ph == null) { Debug.LogError("[a_retrigger] 找不到 PlayerHealth"); yield break; }
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

    sb.AppendLine("动画状态名表：Idle / Walk / Run / Dash / Atk1 / Atk1Rec / Atk2 / Atk2Rec / Atk3");
    sb.AppendLine();

    // ---------- 变体 A ----------
    yield return Variant(sb, ctl, anim, "变体A：阶段刚回 Locomotion 那一帧立刻再攻击", 0);

    // ---------- 变体 B ----------
    yield return Variant(sb, ctl, anim, "变体B：阶段回 Locomotion 之后 5 帧再攻击", 5);

    ctl.SetInjectedMove(Vector2.zero, true);
    ctl.EndInputOverride();
    if (stance != null) stance.combatExitDelay = 6f;

    File.WriteAllText(Path.Combine(Application.dataPath, "../Tools/reports/a_retrigger.txt"), sb.ToString());
    Debug.Log("[a_retrigger] done");
    yield return null;
}

// 一次完整的「打一刀 → 等它彻底结束 → 再打一刀」，逐帧打印
IEnumerator Variant(StringBuilder sb, PlayerController ctl, Animator anim, string title, int extraDelayFrames)
{
    sb.AppendLine("========== " + title + " ==========");
    ctl.ResetToLocomotion();
    { float t = Time.unscaledTime; while (Time.unscaledTime - t < 0.35f) yield return null; }

    ctl.RequestInjectedAttack();
    int f = 0;
    // 第一刀：跑到阶段回到 Locomotion
    bool wasAttack = true;
    while (f < 240)
    {
        yield return null; f++;
        if (f <= 40 || ctl.Phase != ActionPhase.Attack)
            sb.AppendLine(Line(anim, ctl, f, "刀1"));
        if (ctl.Phase != ActionPhase.Attack)
        {
            if (wasAttack) { sb.AppendLine("        ↑ 第 " + f + " 帧：阶段回到 Locomotion（刀1 结束）"); wasAttack = false; }
            if (extraDelayFrames > 0) { extraDelayFrames--; }
            else break;
        }
    }

    int endFrame = f;
    sb.AppendLine("---- 刀1 历时 " + endFrame + " 帧；此时 Animator = " + StateName(anim) + " ----");
    sb.AppendLine();

    // 第二刀：立刻再按
    ctl.RequestInjectedAttack();
    // 关键观察：第二刀的触发是否真的把 Animator 拽进 Atk1
    string firstStateAfter = null;
    int enteredAtk1At = -1;
    for (int i = 1; i <= 60; i++)
    {
        yield return null;
        string st = StateName(anim);
        if (i == 1) firstStateAfter = st;
        if (enteredAtk1At < 0 && (st == "Atk1" || st == "Atk2" || st == "Atk3"
                                  || st == "Atk1Rec" || st == "Atk2Rec")) enteredAtk1At = i;
        if (i <= 45) sb.AppendLine(Line(anim, ctl, endFrame + i, "刀2"));
    }

    sb.AppendLine(">>> 结论：再按攻击后第 1 帧动画状态 = " + firstStateAfter
                  + "；第 " + enteredAtk1At + " 帧进入攻击状态。");
    if (enteredAtk1At < 0)
        sb.AppendLine(">>> **攻击动画始终没有起来** —— 这就是用户看到的「卡在中间怪姿势」。");
    sb.AppendLine();
}

static string Line(Animator anim, PlayerController ctl, int f, string tag)
{
    string cur = StateName(anim);
    string nxt = "-";
    bool inTr = false;
    if (anim != null)
    {
        var s = anim.GetCurrentAnimatorStateInfo(0);
        inTr = anim.IsInTransition(0);
        if (inTr) nxt = StateName(anim.GetNextAnimatorStateInfo(0));
    }
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

static string StateName(Animator anim)
{
    if (anim == null) return "?";
    var s = anim.GetCurrentAnimatorStateInfo(0);
    foreach (var n in new[] { "Idle", "Walk", "Run", "Dash", "Atk1Rec", "Atk1", "Atk2Rec", "Atk2", "Atk3" })
        if (s.IsName(n)) return n;
    return "其他";
}

static string StateName(AnimatorStateInfo s)
{
    foreach (var n in new[] { "Idle", "Walk", "Run", "Dash", "Atk1Rec", "Atk1", "Atk2Rec", "Atk2", "Atk3" })
        if (s.IsName(n)) return n;
    return "其他";
}

return Body();
