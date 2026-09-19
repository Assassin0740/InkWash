// v39e_test.cs —— 只测：杀光 → 门涡（关键节点打日志定位卡点）
using System.Collections;
using UnityEngine;

IEnumerator Body()
{
    var sb = new System.Text.StringBuilder();
    Application.runInBackground = true;
    var run = Object.FindObjectOfType<InkWash.Roguelike.RunManager>();
    run.ResetForTest(); yield return null;
    run.StartRun();
    var sw3 = System.Diagnostics.Stopwatch.StartNew();
    while (run.State != InkWash.Roguelike.RunState.Playing && sw3.Elapsed.TotalSeconds < 25.0) yield return null;
    Debug.Log("[v39e] step1 Playing=" + (run.State == InkWash.Roguelike.RunState.Playing));

    int doorFx = 0;
    System.Action onDoor = () => { doorFx++; Debug.Log("[v39e] DoorOpened fired #" + doorFx); };
    run.DoorOpened += onDoor;

    var sw1 = System.Diagnostics.Stopwatch.StartNew();
    while (sw1.Elapsed.TotalSeconds < 30.0)
    {
        bool anyAlive = false;
        foreach (var e in Object.FindObjectsOfType<InkWash.Enemies.EnemyBase>())
            if (e.IsAlive) { anyAlive = true; break; }
        if (!anyAlive) break;
        foreach (var e in Object.FindObjectsOfType<InkWash.Enemies.EnemyBase>())
            if (e.IsAlive) e.TakeDamage(InkWash.Combat.DamageInfo.Simple(60f, InkWash.Combat.Faction.Player, null));
        yield return null;
    }
    Debug.Log("[v39e] step2 杀光循环退出耗时 " + sw1.ElapsedMilliseconds + "ms");
    sb.AppendLine("杀光耗时=" + sw1.ElapsedMilliseconds + "ms");

    var sw2 = System.Diagnostics.Stopwatch.StartNew();
    while (!run.AwaitingDoorCross && sw2.Elapsed.TotalSeconds < 30.0) yield return null;
    Debug.Log("[v39e] step3 AwaitingDoorCross=" + run.AwaitingDoorCross + " doorFx=" + doorFx + " (waited " + sw2.ElapsedMilliseconds + "ms)");
    sb.AppendLine("门开=" + run.AwaitingDoorCross + " 门涡触发=" + doorFx);
    run.DoorOpened -= onDoor;

    sb.AppendLine("RESULT " + (doorFx >= 1 ? "PASS" : "CHECK"));
    Debug.Log("[v39e] FINAL\n" + sb.ToString());
    yield break;
}

return Body();
