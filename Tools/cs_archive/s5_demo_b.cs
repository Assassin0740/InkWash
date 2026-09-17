// s5_demo_b.cs —— M5 录像证据 ②：三选一升级 → 属性成长 → 清空三间房 → 通关结算
//
// 这是 M5「能完整跑完一局」的正面证据：一条片子里走完 主菜单之后的全流程
// （战斗 → 升级三选一 → 变强 → 逐间推进 → Victory 结算）。
//
// 时间驱动 + 一律用 Time.unscaledTime：奖励界面会 Time.timeScale = 0，
// 用 Time.time 计时会在"暂停那一拍"里原地卡住（本项目踩过这个坑）。
// 波次节奏只在**运行时内存**里加速（退出 Play 不落盘），否则 15 s 装不下一局。
// maxHealth 抬到 1000 同理 —— 这条片子要证明"循环跑得通"，不是"能扛几刀"。
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
    if (ph == null) { Debug.LogError("[s5_demo_b] 找不到 PlayerHealth"); yield break; }
    var go = ph.gameObject;
    var ctl = go.GetComponent<PlayerController>();
    var stance = go.GetComponentInChildren<CombatStance>(true);
    var panel = Object.FindObjectOfType<InkStylePanel>();
    var run = Object.FindObjectOfType<RunManager>();
    var choice = Object.FindObjectOfType<SkillChoicePanel>();
    var spawner = Object.FindObjectOfType<WaveSpawner>();
    var room = Object.FindObjectOfType<RoomController>();
    var level = go.GetComponent<LevelSystem>();
    var inv = go.GetComponent<SkillInventory>();

    if (run == null || choice == null || spawner == null || level == null)
    { Debug.LogError("[s5_demo_b] 场景缺件（RunManager/SkillChoicePanel/WaveSpawner/LevelSystem）"); yield break; }

    ph.maxHealth = 1000f;
    ph.ResetHealth();
    if (panel != null) { panel.ApplyStage(4); panel.visible = false; }
    choice.Hide();

    inv.ResetAll(); level.ResetAll();
    run.ResetForTest();
    run.SetSeed(20260915);
    spawner.ResetForTest();
    if (room != null) room.ResetForTest();

    // 加速波次（只改运行时内存）
    spawner.spawnInterval = 0.05f;
    run.nextRoomDelay = 0.25f;
    if (spawner.waves != null)
        for (int i = 0; i < spawner.waves.Length; i++)
            if (spawner.waves[i] != null) spawner.waves[i].delayBefore = 0.1f;

    ctl.ResetToLocomotion();
    var cc = go.GetComponent<CharacterController>();
    if (cc != null) cc.enabled = false;
    go.transform.position = new Vector3(-3f, 0.45f, -7f);
    go.transform.rotation = Quaternion.LookRotation(Vector3.forward, Vector3.up);
    if (cc != null) cc.enabled = true;
    if (stance != null) { stance.combatExitDelay = 99999f; stance.ForceStance(true); }
    ctl.BeginInputOverride();

    // 立刻清场（等价于验收里的 KillAllAlive）：演示要的是"房间会被清空"这一事实，
    // 不是逐刀实战的时长。
    int KillAll()
    {
        int n = 0;
        foreach (var e in Object.FindObjectsOfType<EnemyBase>())
        {
            if (e == null || !e.IsAlive) continue;
            e.TakeDamage(new DamageInfo
            {
                amount = 100000f, sourceFaction = Faction.Player, hitDirection = Vector3.zero,
                knockback = 0f, hitStun = 0f, hitStop = 0f,
            });
            n++;
        }
        return n;
    }

    run.StartRun();

    // ① 开局战斗
    { float t = Time.unscaledTime, last = t;
      while (Time.unscaledTime - t < 0.6f)
      {
          ctl.SetInjectedMove(Vector2.zero, false);
          if (Time.unscaledTime - last > 0.25f) { last = Time.unscaledTime; ctl.RequestInjectedAttack(); }
          yield return null;
      } }

    // ② 升级 → 三选一面板（停 2.8 s 让三张卡看清楚）
    level.GrantXp(level.XpToNext);
    { float t = Time.unscaledTime; while (Time.unscaledTime - t < 2.8f) yield return null; }
    choice.InjectChoice(0);
    { float t = Time.unscaledTime; while (Time.unscaledTime - t < 0.35f) yield return null; }

    // ③ 变强后再打两刀
    { float t = Time.unscaledTime, last = t;
      while (Time.unscaledTime - t < 1.7f)
      {
          ctl.SetInjectedMove(new Vector2(0.35f, 0f), false);
          if (Time.unscaledTime - last > 1.1f) { last = Time.unscaledTime; ctl.RequestInjectedAttack(); }
          yield return null;
      } }

    // ④ 逐间推进：每次升级停 0.3 s 自动选一张，直到通关
    ctl.SetInjectedMove(Vector2.zero, false);
    float tClear = Time.unscaledTime;
    float holdAt = -1f;
    int picks = 1;
    while (!run.IsRunOver && Time.unscaledTime - tClear < 7.6f)
    {
        if (run.State == RunState.Reward)
        {
            if (holdAt < 0f) holdAt = Time.unscaledTime;
            if (Time.unscaledTime - holdAt > 0.3f && choice.IsShowing)
            {
                int n = choice.Options != null ? choice.Options.Count : 0;
                choice.InjectChoice(picks % Mathf.Max(1, n));
                picks++; holdAt = -1f;
            }
            yield return null;
            continue;
        }
        KillAll();
        yield return null;
    }

    // ⑤ 结算界面
    { float t = Time.unscaledTime; while (Time.unscaledTime - t < 0.9f) yield return null; }

    ctl.SetInjectedMove(Vector2.zero, false);
    ctl.EndInputOverride();
    if (stance != null) stance.combatExitDelay = 6f;
    Debug.Log("[s5_demo_b] done　状态 " + run.State + "　房间 " + run.RoomIndex + "/" + run.roomsToClear
              + "　选择 " + picks + " 次　" + inv.Describe());
    yield return null;
}
return Body();
