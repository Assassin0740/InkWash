// v39h_test.cs —— B 组终测：等敌 → dash逐帧 → 杀光 → 门涡+门下墨渍
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

    // ---- 等敌人出现 ----
    InkWash.Enemies.EnemyBase enemy = null;
    var sw0 = System.Diagnostics.Stopwatch.StartNew();
    while (enemy == null && sw0.Elapsed.TotalSeconds < 25.0)
    {
        foreach (var e in Object.FindObjectsOfType<InkWash.Enemies.EnemyBase>())
            if (e.IsAlive) { enemy = e; break; }
        if (enemy == null) yield return null;
    }
    Debug.Log("[v39h] step-enemy-found"); sb.AppendLine("enemy = " + (enemy != null ? enemy.name : "NULL"));
    if (enemy == null) { Debug.Log("[v39h]\n" + sb); yield break; }

    // ---- dash 逐帧诊断 ----
    var trail = Object.FindObjectOfType<InkWash.Effects.DashInkTrail>();
    var tr = trail != null ? trail.GetComponentInChildren<TrailRenderer>() : null;
    var pc = Object.FindObjectOfType<InkWash.Player.PlayerController>();
    int emitFrames = 0;
    if (pc != null && tr != null)
    {
        Debug.Log("[v39h] step-dash-inject"); pc.RequestInjectedDash(); Debug.Log("[v39h] step-dash-injected");
        var seq = "";
        for (int i = 0; i < 12; i++)
        {
            yield return null;
            bool em = tr.emitting;
            if (em) emitFrames++;
            if (i < 6) seq += pc.IsDashing ? "D" : ".";
        }
        sb.AppendLine("dash: emitting " + emitFrames + "/12  IsDashing seq=" + seq);
    }
    else sb.AppendLine("dash: 组件缺失 trail=" + (trail != null) + " tr=" + (tr != null));

    // ---- 杀光（先等敌出现，再每帧补刀）----
    var sw1 = System.Diagnostics.Stopwatch.StartNew();
    while (sw1.Elapsed.TotalSeconds < 30.0)
    {
        bool anyAlive = false;
        foreach (var e in Object.FindObjectsOfType<InkWash.Enemies.EnemyBase>())
            if (e.IsAlive) { anyAlive = true; break; }
        if (!anyAlive && sw1.Elapsed.TotalSeconds > 1.0) break;   // 至少跑 1s，避免还没刷怪就误判清场
        foreach (var e in Object.FindObjectsOfType<InkWash.Enemies.EnemyBase>())
            if (e.IsAlive) e.TakeDamage(InkWash.Combat.DamageInfo.Simple(80f, InkWash.Combat.Faction.Player, null));
        yield return null;
    }
    Debug.Log("[v39h] step-killed"); sb.AppendLine("杀光耗时=" + sw1.ElapsedMilliseconds + "ms");

    // ---- 等门开 ----
    int doorFx = 0;
    System.Action onDoor = () => doorFx++;
    run.DoorOpened += onDoor;
    var panel = Object.FindObjectOfType<InkWash.UI.SkillChoicePanel>();
    var sw2 = System.Diagnostics.Stopwatch.StartNew();
    while (!run.AwaitingDoorCross && sw2.Elapsed.TotalSeconds < 30.0)
    {
        // 清房升级会弹三选一（timeScale=0）——注入选择回到 Playing，门控才继续
        if (panel != null && Time.timeScale == 0f)
        {
            try { panel.InjectChoice(0); } catch { }
        }
        yield return null;
    }
    Debug.Log("[v39h] step-door-wait-done"); yield return new WaitForSecondsRealtime(0.2f);
    run.DoorOpened -= onDoor;

    // 门下墨渍
    int doorStains = 0;
    var room = run.room;
    if (room != null && room.gates != null)
        foreach (var g in Object.FindObjectsOfType<GameObject>())
            if (g.name == "InkStain") doorStains++;
    sb.AppendLine("门开=" + run.AwaitingDoorCross + " 门涡触发=" + doorFx + " 场上墨渍=" + doorStains);

    bool pass = emitFrames >= 8 && doorFx >= 1;
    sb.AppendLine("RESULT " + (pass ? "PASS" : "CHECK"));
    Debug.Log("[v39h]\n" + sb.ToString());
    yield break;
}

return Body();
