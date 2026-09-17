// s3_demo_b.cs —— 录像证据 B：第二波的墨偶远程开火（墨弹飞行）+ 全清后石门下沉开门。
//
// 录制上限 15 s，所以这里把波次节奏临时调快（spawnInterval / delayBefore 都是 public 字段），
// 否则光"刷到第二波"就要 7 s，什么都拍不到。调的是**这一次运行**的实例值，不动场景资产。
using System.Collections;
using UnityEngine;
using InkWash.Combat;
using InkWash.Enemies;
using InkWash.Player;

IEnumerator Body()
{
    var ph = Object.FindObjectOfType<PlayerHealth>();
    if (ph == null) { Debug.LogError("[s3_demo_b] 找不到 PlayerHealth"); yield break; }
    var go = ph.gameObject;
    var ctl = go.GetComponent<PlayerController>();
    var spawner = Object.FindObjectOfType<WaveSpawner>();
    var stance = go.GetComponentInChildren<CombatStance>(true);

    spawner.ResetForTest();
    foreach (var e in Object.FindObjectsOfType<EnemyBase>())
        if (e != null) Destroy(e.gameObject);
    yield return null;

    // 临时提速（只影响本次运行）
    spawner.spawnInterval = 0.08f;
    if (spawner.waves != null)
        for (int i = 0; i < spawner.waves.Length; i++)
            if (spawner.waves[i] != null) spawner.waves[i].delayBefore = 0.35f;

    var cc = go.GetComponent<CharacterController>();
    if (cc != null) cc.enabled = false;
    go.transform.position = new Vector3(0f, 0.45f, -2f);
    go.transform.rotation = Quaternion.LookRotation(Vector3.forward, Vector3.up);
    if (cc != null) cc.enabled = true;
    ctl.ResetToLocomotion();
    if (stance != null) { stance.combatExitDelay = 99999f; stance.ForceStance(true); }

    ctl.BeginInputOverride();
    ctl.SetInjectedMove(Vector2.zero, false);
    spawner.Begin();

    // 快进：第一波一出来就清掉，把镜头让给第二波（3 墨徒 + 2 墨偶）
    { float t = Time.time; while (spawner.SpawnedCount < 3 && Time.time - t < 6f) yield return null; }
    KillAll();

    // 段 1（5.5 s）：第二波入场，墨偶拉开距离投墨弹
    {
        float t = Time.time;
        while (spawner.SpawnedCount < 8 && Time.time - t < 6f) yield return null;
        t = Time.time;
        while (Time.time - t < 5.5f)
        {
            ctl.SetInjectedMove(new Vector2(Mathf.Sin(Time.time * 1.3f) * 0.7f, 0f), false);
            yield return null;
        }
    }

    // 段 2（4 s）：玩家挥砍
    {
        float t = Time.time; float next = 0f;
        while (Time.time - t < 4.0f)
        {
            ctl.SetInjectedMove(Vector2.zero, false);
            if (Time.time >= next) { ctl.RequestInjectedAttack(); next = Time.time + 0.9f; }
            yield return null;
        }
    }

    // 段 3：全清 → 石门下沉开门
    {
        float t = Time.time;
        while (!spawner.AllCleared && Time.time - t < 20f) { KillAll(); yield return null; }
        t = Time.time;
        while (Time.time - t < 2.8f) yield return null;   // 看门沉下去
    }

    ctl.SetInjectedMove(Vector2.zero, false);
    ctl.EndInputOverride();
    if (stance != null) stance.combatExitDelay = 6f;
    Debug.Log("[s3_demo_b] done  已清 " + spawner.WaveClearedCount + " 波  生成 " + spawner.SpawnedCount);
    yield return null;
}

void KillAll()
{
    foreach (var e in Object.FindObjectsOfType<EnemyBase>())
        if (e != null && e.IsAlive)
            e.TakeDamage(new DamageInfo { amount = 100000f, sourceFaction = Faction.Player });
}

return Body();
