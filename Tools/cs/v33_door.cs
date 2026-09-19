// v33_door.cs —— 以撒式逐房间推进冒烟：
//   ① 清房后不再自动推进（等穿门）② 走到门洞触发推进 + 传送回房间中心
//   ③ 过场淡入存在 ④ 下一波已开刷
using System;
using System.Collections;
using UnityEngine;

IEnumerator Body()
{
    var sb = new System.Text.StringBuilder();
    var run = Object.FindObjectOfType<InkWash.Roguelike.RunManager>();
    if (run == null) { Debug.Log("[v33] FAIL: RunManager 不在场"); yield break; }
    var room = run.room;
    var choice = Object.FindObjectOfType<InkWash.Roguelike.SkillChoicePanel>();

    Application.runInBackground = true;

    // 干净开局
    run.ResetForTest();
    yield return null;
    run.StartRun();
    int guard = 0;
    while (run.State != InkWash.Roguelike.RunState.Playing && guard++ < 900) yield return null;
    if (run.State != InkWash.Roguelike.RunState.Playing) { Debug.Log("[v33] FAIL 未进 Playing"); yield break; }

    var player = run.playerHealth.transform;
    Vector3 startPos = player.position;
    int roomIdx0 = run.RoomIndex;

    // ---- 杀光所有波次（复用 PlaytestHarness 的 Reward 注入纪律）----
    int rewards = 0;
    guard = 0;
    float t0 = Time.unscaledTime;
    while (room != null && !room.IsCleared && guard++ < 200000 && Time.unscaledTime - t0 < 90f)
    {
        if (run.State == InkWash.Roguelike.RunState.Reward)
        {
            yield return new WaitForSecondsRealtime(0.2f);
            if (choice != null && choice.IsShowing)
            {
                int pick = rewards % Mathf.Max(1, choice.Options.Count);
                if (choice.InjectChoice(pick)) rewards++;
            }
            continue;
        }
        foreach (var e in Object.FindObjectsOfType<InkWash.Enemies.EnemyBase>())
        {
            if (!e.IsAlive) continue;
            e.TakeDamage(new InkWash.Combat.DamageInfo
            {
                amount = 100000f,
                sourceFaction = InkWash.Combat.Faction.Player,
                hitDirection = Vector3.zero,
                knockback = 0f,
                hitStun = 0f,
                hitStop = 0f,
            });
        }
        yield return null;
    }
    yield return new WaitForSecondsRealtime(0.3f);

    // Reward 竞态兜底（清最后一波触发升级）
    if (run.State == InkWash.Roguelike.RunState.Reward)
    {
        yield return new WaitForSecondsRealtime(0.2f);
        if (choice != null && choice.IsShowing) choice.InjectChoice(0);
        yield return new WaitForSecondsRealtime(0.3f);
    }

    bool cleared = room != null && room.IsCleared;
    bool awaiting = run.AwaitingDoorCross;
    int idxAfterClear = run.RoomIndex;

    // ---- 判据 1：清房后不自动推进（等 2.5s，房间号不动）----
    yield return new WaitForSecondsRealtime(2.5f);
    bool noAutoAdvance = run.RoomIndex == roomIdx0 && run.AwaitingDoorCross;

    // ---- 判据 2：把玩家放到门洞 → 推进 + 传送回起点 ----
    Transform gate0 = (room != null && room.gates != null && room.gates.Length > 0) ? room.gates[0] : null;
    if (gate0 == null) { Debug.Log("[v33] FAIL: 无门可走 sb=" + sb); yield break; }
    Vector3 doorPos = gate0.position; doorPos.y = player.position.y;
    var cc = player.GetComponent<CharacterController>();
    if (cc != null) cc.enabled = false;
    player.position = doorPos;
    if (cc != null) cc.enabled = true;

    guard = 0;
    while (run.RoomIndex == roomIdx0 && guard++ < 600) yield return null;       // 等 Update 判过门
    yield return new WaitForSecondsRealtime(0.25f);
    bool advanced = run.RoomIndex == roomIdx0 + 1 && !run.AwaitingDoorCross;
    bool teleported = Vector3.Distance(player.position, startPos) < 1.5f;

    // ---- 判据 3：过场淡入（DoorFade alpha 曾 > 0）----
    var pres = Object.FindObjectOfType<InkWash.UI.RunPresentation>();
    float fadeA = 0f;
    if (pres != null) fadeA = pres.DoorFadeAlphaForTest;
    bool fadeSeen = fadeA > 0.01f;

    // ---- 判据 4：下一波已开刷 ----
    var sp = run.spawner;
    bool nextWaveStarted = sp == null || (sp.SpawnedCount > 0 || sp.CurrentWave >= 0);

    sb.Append("clear=").Append(cleared)
      .Append(" await=").Append(awaiting)
      .Append(" idxAfterClear=").Append(idxAfterClear)
      .Append(" noAuto=").Append(noAutoAdvance)
      .Append(" advanced=").Append(advanced).Append("(idx=").Append(run.RoomIndex).Append(")")
      .Append(" teleport=").Append(teleported)
      .Append(" fadeAlpha=").Append(fadeA.ToString("F2"))
      .Append(" nextWave=").Append(nextWaveStarted)
      .Append(" state=").Append(run.State)
      .Append(" => ").Append((cleared && awaiting && noAutoAdvance && advanced && teleported && fadeSeen && nextWaveStarted) ? "PASS" : "FAIL");
    Debug.Log("[v33] " + sb);
    yield return null;
}
return Body();
