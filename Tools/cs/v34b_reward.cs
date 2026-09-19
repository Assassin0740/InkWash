// v34b_reward.cs —— UGUI 三选一冒烟：
//   ① 升级触发 Reward ② UGUI Canvas 面板激活、光标解锁 ③ InjectChoice 有效 ④ 选择后回 Playing
using System;
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;

IEnumerator Body()
{
    var sb = new System.Text.StringBuilder();
    var run = Object.FindObjectOfType<InkWash.Roguelike.RunManager>();
    if (run == null) { Debug.Log("[v34b] FAIL: RunManager 不在场"); yield break; }
    var choice = Object.FindObjectOfType<InkWash.Roguelike.SkillChoicePanel>();
    Application.runInBackground = true;

    run.ResetForTest();
    yield return null;
    run.StartRun();
    int guard = 0;
    while (run.State != InkWash.Roguelike.RunState.Playing && guard++ < 900) yield return null;
    if (run.State != InkWash.Roguelike.RunState.Playing) { Debug.Log("[v34b] FAIL 未进 Playing"); yield break; }

    // 杀怪攒经验直到弹 Reward（复用 S5 纪律：Reward 注入循环）
    guard = 0;
    float t0 = Time.unscaledTime;
    while (run.State != InkWash.Roguelike.RunState.Reward && guard++ < 200000 && Time.unscaledTime - t0 < 60f)
    {
        foreach (var e in Object.FindObjectsOfType<InkWash.Enemies.EnemyBase>())
            if (e.IsAlive) e.TakeDamage(new InkWash.Combat.DamageInfo
            {
                amount = 100000f,
                sourceFaction = InkWash.Combat.Faction.Player,
                hitDirection = Vector3.zero,
                knockback = 0f, hitStun = 0f, hitStop = 0f,
            });
        yield return null;
    }

    bool rewardEntered = run.State == InkWash.Roguelike.RunState.Reward;
    yield return new WaitForSecondsRealtime(0.3f);
    bool showing = choice != null && choice.IsShowing;
    int optCount = showing ? choice.Options.Count : -1;

    // UGUI 面板真的在场景里且激活？
    var panelGo = GameObject.Find("SkillChoiceCanvas/RewardPanel");
    bool uguiActive = panelGo != null && panelGo.activeInHierarchy;
    bool esReady = FindObjectOfType<EventSystem>() != null;
    bool cursorFree = Cursor.lockState == CursorLockMode.None && Cursor.visible;

    int rewards = 0;

    // 注入选择 → 应回 Playing（一刀爆多级会排队多张奖励票，循环注入到回 Playing 为止）
    string firstName = "";
    bool injected = showing && choice.InjectChoice(0);
    if (injected) firstName = choice.ChosenName;
    guard = 0;
    while (run.State == InkWash.Roguelike.RunState.Reward && guard++ < 200)
    {
        yield return new WaitForSecondsRealtime(0.2f);
        if (choice.IsShowing) choice.InjectChoice(rewards % Mathf.Max(1, choice.Options.Count));
        rewards++;
    }
    yield return new WaitForSecondsRealtime(0.3f);
    bool backToPlaying = run.State == InkWash.Roguelike.RunState.Playing;
    bool panelClosed = panelGo == null || !panelGo.activeInHierarchy;

    sb.Append("reward=").Append(rewardEntered)
      .Append(" showing=").Append(showing).Append("(").Append(optCount).Append(")")
      .Append(" uguiActive=").Append(uguiActive)
      .Append(" es=").Append(esReady)
      .Append(" cursorFree=").Append(cursorFree)
      .Append(" inject=").Append(injected).Append("→").Append(firstName)
      .Append(" rewards=").Append(rewards)
      .Append(" back=").Append(backToPlaying)
      .Append(" closed=").Append(panelClosed)
      .Append(" => ").Append((rewardEntered && showing && uguiActive && esReady && cursorFree && injected && backToPlaying && panelClosed) ? "PASS" : "FAIL");
    Debug.Log("[v34b] " + sb);
    yield return null;
}
return Body();
