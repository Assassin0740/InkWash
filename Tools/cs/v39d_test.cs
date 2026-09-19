// v39d_test.cs —— 只测：开局 → 找敌 → 受击墨渍（v37 同构，已知能跑通）
using System.Collections;
using UnityEngine;

IEnumerator Body()
{
    var sb = new System.Text.StringBuilder();
    Application.runInBackground = true;
    var run = Object.FindObjectOfType<InkWash.Roguelike.RunManager>();
    if (run == null) { Debug.Log("[v39d] FAIL no RunManager"); yield break; }

    run.ResetForTest(); yield return null;
    run.StartRun();
    var sw3 = System.Diagnostics.Stopwatch.StartNew();
    while (run.State != InkWash.Roguelike.RunState.Playing && sw3.Elapsed.TotalSeconds < 25.0) yield return null;
    sb.AppendLine("State=" + run.State + " (waited " + sw3.ElapsedMilliseconds + "ms)");

    InkWash.Enemies.EnemyBase enemy = null;
    var sw0 = System.Diagnostics.Stopwatch.StartNew();
    while (enemy == null && sw0.Elapsed.TotalSeconds < 25.0)
    {
        foreach (var e in Object.FindObjectsOfType<InkWash.Enemies.EnemyBase>())
            if (e.IsAlive) { enemy = e; break; }
        if (enemy == null) yield return null;
    }
    sb.AppendLine("enemy = " + (enemy != null ? enemy.name + " hp=" + enemy.Health : "NULL (waited " + sw0.ElapsedMilliseconds + "ms)"));
    if (enemy == null) { Debug.Log("[v39d]\n" + sb); yield break; }

    var info = InkWash.Combat.DamageInfo.Simple(15f, InkWash.Combat.Faction.Player, null);
    info.hitPoint = enemy.Transform.position + Vector3.up * 1.0f;
    info.hitDirection = Vector3.forward;
    enemy.TakeDamage(info);
    yield return null; yield return null;
    int stains = 0;
    foreach (var g in Object.FindObjectsOfType<GameObject>())
        if (g.name == "InkStain") stains++;
    sb.AppendLine("墨渍 = " + stains);
    Debug.Log("[v39d]\n" + sb.ToString());
    yield break;
}

return Body();
