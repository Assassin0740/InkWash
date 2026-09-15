// s5_demo_a.cs —— M5 录像证据 ①：主菜单 → 进局 → 水墨战斗（墨线 + 宣纸 + 墨晕全开，含溅墨与落地墨花）
//
// 几个刻意的选择：
//   · 时间驱动（不是帧数驱动）：录制会把帧率压到 20~35，按帧数等会把段长放大好几倍。
//     而且升级时 timeScale = 0 —— 所以脚本自己的计时一律用 Time.unscaledTime。
//   · 演示期间把 maxHealth 抬到 1000（**只改运行时内存，退出 Play 不落盘**）：
//     这条片子要证明的是"循环跑得通、墨味在"，不是"玩家能扛住几刀"。
//   · 战斗中途升级会真弹三选一 —— 这里停 0.7 s 后自动选一张再继续，
//     让"击杀 → 升级 → 选择 → 变强"在一条片子里连贯出现（D2/D3/D4 的可视证据）。
using System.Collections;
using UnityEngine;
using InkWash.Combat;
using InkWash.Enemies;
using InkWash.Player;
using InkWash.Roguelike;
using InkWash.UI;

IEnumerator Body()
{
    var ph = Object.FindObjectOfType<PlayerHealth>();
    if (ph == null) { Debug.LogError("[s5_demo_a] 找不到 PlayerHealth"); yield break; }
    var go = ph.gameObject;
    var ctl = go.GetComponent<PlayerController>();
    var stance = go.GetComponentInChildren<CombatStance>(true);
    var panel = Object.FindObjectOfType<InkStylePanel>();
    var run = Object.FindObjectOfType<RunManager>();
    var choice = Object.FindObjectOfType<SkillChoicePanel>();
    var spawner = Object.FindObjectOfType<WaveSpawner>();

    ph.maxHealth = 1000f;
    ph.ResetHealth();

    // 第 5 阶段 = 墨线 + 宣纸 + 墨晕全开；参数面板收起来，画面干净
    if (panel != null) { panel.ApplyStage(4); panel.visible = false; }
    if (choice != null) choice.Hide();
    if (spawner != null) spawner.ResetForTest();
    if (run != null) run.ResetForTest();
    ctl.ResetToLocomotion();

    var cc = go.GetComponent<CharacterController>();
    if (cc != null) cc.enabled = false;
    go.transform.position = new Vector3(-3f, 0.45f, -7f);
    go.transform.rotation = Quaternion.LookRotation(Vector3.forward, Vector3.up);
    if (cc != null) cc.enabled = true;
    if (stance != null) { stance.combatExitDelay = 99999f; stance.ForceStance(true); }
    ctl.BeginInputOverride();

    // 一段"演出"：按住移动输入若干秒，期间周期性挥砍 / 冲刺一次；
    // 中途升级弹出三选一时停一拍、自动选一张再继续。
    float rewardHold = -1f;
    int picks = 0;
    IEnumerator Act(float seconds, Vector2 move, bool runHeld, float attackPeriod, bool dashOnce)
    {
        float t0 = Time.unscaledTime, lastAtk = t0, flipT = t0;
        bool dashed = false;
        while (Time.unscaledTime - t0 < seconds)
        {
            if (run != null && run.State == RunState.Reward)
            {
                ctl.SetInjectedMove(Vector2.zero, false);
                if (rewardHold < 0f) rewardHold = Time.unscaledTime;
                if (Time.unscaledTime - rewardHold > 0.7f && choice != null && choice.IsShowing)
                {
                    int n = choice.Options != null ? choice.Options.Count : 0;
                    choice.InjectChoice(picks % Mathf.Max(1, n));
                    picks++; rewardHold = -1f;
                }
                yield return null;
                continue;
            }

            if (move.x != 0f)
            {
                if (Time.unscaledTime - flipT > 1.2f) { flipT = Time.unscaledTime; move.x = -move.x; }
                ctl.SetInjectedMove(move, runHeld);
            }
            else ctl.SetInjectedMove(Vector2.zero, false);

            if (attackPeriod > 0f && Time.unscaledTime - lastAtk > attackPeriod)
            { lastAtk = Time.unscaledTime; ctl.RequestInjectedAttack(); }
            if (dashOnce && !dashed && Time.unscaledTime - t0 > 1.0f)
            { dashed = true; ctl.RequestInjectedDash(); }
            yield return null;
        }
    }

    yield return Act(2.6f, Vector2.zero, false, 0f, false);              // ① 主菜单
    if (run != null) run.StartRun();                                     // ② 点「开始一局」
    yield return Act(4.2f, Vector2.zero, false, 1.4f, false);            // ③ 三段连击
    yield return Act(3.4f, new Vector2(1f, 0f), true, 0f, true);         // ④ 走位 + 冲刺（落地墨花）
    yield return Act(3.2f, new Vector2(0.5f, 0f), false, 1.2f, false);   // ⑤ 连击收尾

    ctl.SetInjectedMove(Vector2.zero, false);
    ctl.EndInputOverride();
    if (stance != null) stance.combatExitDelay = 6f;
    Debug.Log("[s5_demo_a] done　共选择 " + picks + " 次");
    yield return null;
}
return Body();
