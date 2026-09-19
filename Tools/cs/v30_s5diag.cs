// v30_s5diag.cs —— S5 房间推进 0/3 诊断：开一局、杀 10 秒，dump 波次/房间/流程状态
using System;
using System.Collections;
using System.Reflection;
using UnityEngine;

IEnumerator Body()
{
    var run = Object.FindObjectOfType<InkWash.Roguelike.RunManager>();
    var spawner = Object.FindObjectOfType<InkWash.Enemies.WaveSpawner>();
    var room = Object.FindObjectOfType<InkWash.Enemies.RoomController>();
    if (run == null || spawner == null) { Debug.LogError("[diag] 缺 RunManager/WaveSpawner"); yield break; }

    // 复位并开跑
    run.ResetForTest();
    run.SetSeed(20260915);
    spawner.ResetForTest();
    if (room != null) room.ResetForTest();
    spawner.spawnInterval = 0.06f;
    run.nextRoomDelay = 0.35f;
    if (spawner.waves != null)
        foreach (var w in spawner.waves) if (w != null) w.delayBefore = 0.1f;
    run.StartRun();
    Debug.Log("[diag] started state=" + run.State + " waves=" + spawner.WaveCount);

    // 杀 10 秒
    float t0 = Time.unscaledTime;
    while (Time.unscaledTime - t0 < 10f)
    {
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
    }

    var sb = new System.Text.StringBuilder();
    sb.Append("DIAG t=10s state=").Append(run.State)
      .Append(" room=").Append(run.RoomIndex).Append("/").Append(run.roomsToClear);
    // RunManager.room 引用是否接上
    var fRoom = typeof(InkWash.Roguelike.RunManager).GetField("room", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
    sb.Append(" run.room=").Append(fRoom != null ? (fRoom.GetValue(run) != null ? "linked" : "NULL") : "no-field");
    sb.Append(" || spawner: curWave=").Append(spawner.CurrentWave)
      .Append(" started=").Append(spawner.WaveStartedCount)
      .Append(" cleared=").Append(spawner.WaveClearedCount)
      .Append(" spawned=").Append(spawner.SpawnedCount)
      .Append(" failed=").Append(spawner.SpawnFailedCount)
      .Append(" alive=").Append(spawner.AliveEnemies)
      .Append(" allCleared=").Append(spawner.AllCleared);
    if (room != null)
        sb.Append(" || roomCtl: isCleared=").Append(room.IsCleared).Append(" gates=").Append(room.gates != null ? room.gates.Length : 0);
    else
        sb.Append(" || roomCtl: NONE-IN-SCENE");
    Debug.Log("[diag] " + sb.ToString());

    // 每波配置明细
    if (spawner.waves != null)
        for (int i = 0; i < spawner.waves.Length; i++)
        {
            var w = spawner.waves[i];
            if (w == null) { Debug.Log("[diag] wave " + i + " = null"); continue; }
            string parts = "";
            if (w.entries != null)
                foreach (var e in w.entries)
                    parts += (e != null && e.prefab != null ? e.prefab.name : "NULL-PREFAB") + "x" + (e != null ? e.count : 0) + " ";
            Debug.Log("[diag] wave " + i + " delay=" + w.delayBefore.ToString("F2") + " entries: " + parts);
        }
    yield return null;
}
return Body();
