// v30_s5diag2.cs —— S5 房间推进诊断②：自动选卡 + 2 秒一条时间线，定位 120s 卡点
using System;
using System.Collections;
using UnityEngine;

IEnumerator Body()
{
    var run = Object.FindObjectOfType<InkWash.Roguelike.RunManager>();
    var spawner = Object.FindObjectOfType<InkWash.Enemies.WaveSpawner>();
    var room = Object.FindObjectOfType<InkWash.Enemies.RoomController>();
    var choice = Object.FindObjectOfType<InkWash.Roguelike.SkillChoicePanel>();
    var health = Object.FindObjectOfType<InkWash.Player.PlayerHealth>();
    if (run == null || spawner == null || choice == null) { Debug.LogError("[diag2] 缺件"); yield break; }

    run.ResetForTest(); run.SetSeed(20260915);
    spawner.ResetForTest();
    if (room != null) room.ResetForTest();
    spawner.spawnInterval = 0.06f;
    run.nextRoomDelay = 0.35f;
    if (spawner.waves != null)
        foreach (var w in spawner.waves) if (w != null) w.delayBefore = 0.1f;
    if (health != null) { health.maxHealth = 100000f; health.ResetHealth(); }
    run.StartRun();

    float t0 = Time.unscaledTime;
    float nextLog = 0f;
    int rewards = 0;
    int lastSpawned = -1;
    while (run.State != InkWash.Roguelike.RunState.Victory
           && run.State != InkWash.Roguelike.RunState.GameOver
           && Time.unscaledTime - t0 < 40f)
    {
        if (run.State == InkWash.Roguelike.RunState.Reward)
        {
            yield return new WaitForSecondsRealtime(0.2f);
            if (choice.IsShowing)
            {
                int pick = rewards % Mathf.Max(1, choice.Options.Count);
                if (choice.InjectChoice(pick)) rewards++;
            }
            continue;
        }
        foreach (var e in Object.FindObjectsOfType<InkWash.Enemies.EnemyBase>())
        {
            if (e == null || !e.IsAlive) continue;
            e.TakeDamage(new InkWash.Combat.DamageInfo
            {
                amount = 100000f, sourceFaction = InkWash.Combat.Faction.Player,
                hitDirection = Vector3.zero, knockback = 0f, hitStun = 0f, hitStop = 0f,
            });
        }
        yield return null;

        if (Time.unscaledTime - t0 >= nextLog)
        {
            nextLog += 2f;
            string spawnDelta = (spawner.SpawnedCount != lastSpawned ? ("+" + (spawner.SpawnedCount - Mathf.Max(0,lastSpawned))) : "=0");
            lastSpawned = spawner.SpawnedCount;
            Debug.Log("[diag2] t=" + (Time.unscaledTime - t0).ToString("F0")
                + " state=" + run.State
                + " room=" + run.RoomIndex + "/" + run.roomsToClear
                + " wave=" + spawner.CurrentWave + "/" + spawner.WaveCount
                + " spawned=" + spawner.SpawnedCount + "(" + spawnDelta + ")"
                + " alive=" + spawner.AliveEnemies
                + " cleared=" + spawner.WaveClearedCount
                + " allClr=" + spawner.AllCleared
                + " rewards=" + rewards
                + " timeScale=" + Time.timeScale.ToString("F2"));
        }
    }
    Debug.Log("[diag2] END state=" + run.State + " room=" + run.RoomIndex + "/" + run.roomsToClear
        + " rewards=" + rewards + " spawned=" + spawner.SpawnedCount
        + " waveCleared=" + spawner.WaveClearedCount + " allClr=" + spawner.AllCleared
        + " elapsed=" + (Time.unscaledTime - t0).ToString("F1") + "s");
    yield return null;
}
return Body();
