// v39b_test.cs —— B 组冒烟 v2：dash 移到 Playing 后测；门涡用事件计数；墨渍即时数
using System.Collections;
using UnityEngine;

IEnumerator Body()
{
    var sb = new System.Text.StringBuilder();
    Application.runInBackground = true;
    var run = Object.FindObjectOfType<InkWash.Roguelike.RunManager>();
    if (run == null) { Debug.Log("[v39b] FAIL no RunManager"); yield break; }

    // ---- 开局 ----
    run.ResetForTest(); yield return null;
    run.StartRun();
    int guard = 0;
    while (run.State != InkWash.Roguelike.RunState.Playing && guard++ < 900) yield return null;
    yield return new WaitForSeconds(0.3f);

    // ---- ① 冲刺拖尾（Playing 状态下注入）----
    var trail = Object.FindObjectOfType<InkWash.Effects.DashInkTrail>();
    var tr = trail != null ? trail.GetComponentInChildren<TrailRenderer>() : null;
    var pc = Object.FindObjectOfType<InkWash.Player.PlayerController>();
    int emitFrames = 0;
    if (pc != null && tr != null)
    {
        pc.RequestInjectedDash();
        for (int i = 0; i < 12; i++)
        {
            yield return null;
            if (tr.emitting) emitFrames++;
        }
    }
    sb.AppendLine("① 冲刺 emitting = " + emitFrames + "/12  positionCount=" + (tr != null ? tr.positionCount : 0));

    // ---- ② 受击墨渍 ----
    InkWash.Enemies.EnemyBase enemy = null;
    guard = 0;
    while (enemy == null && guard++ < 1200)
    {
        foreach (var e in Object.FindObjectsOfType<InkWash.Enemies.EnemyBase>())
            if (e.IsAlive) { enemy = e; break; }
        if (enemy == null) yield return null;
    }
    if (enemy == null) { sb.AppendLine("FAIL no enemy"); Debug.Log("[v39b]\n" + sb); yield break; }

    var info = InkWash.Combat.DamageInfo.Simple(15f, InkWash.Combat.Faction.Player, null);
    info.hitPoint = enemy.Transform.position + Vector3.up * 1.0f;
    info.hitDirection = Vector3.forward;
    enemy.TakeDamage(info);
    yield return null; yield return null;
    int stains = 0;
    foreach (var g in Object.FindObjectsOfType<GameObject>())
        if (g.name == "InkStain") stains++;
    sb.AppendLine("② 受击后墨渍 = " + stains + "（期望 ≥1）");

    // ---- ③ 门涡：事件计数 ----
    int doorFx = 0;
    System.Action onDoor = () => doorFx++;
    run.DoorOpened += onDoor;

    // 杀光（低伤多点几次，别一次 99999 让死亡结算混乱）
    guard = 0;
    while (guard++ < 900)
    {
        bool anyAlive = false;
        foreach (var e in Object.FindObjectsOfType<InkWash.Enemies.EnemyBase>())
            if (e.IsAlive) { anyAlive = true; break; }
        if (!anyAlive) break;
        foreach (var e in Object.FindObjectsOfType<InkWash.Enemies.EnemyBase>())
            if (e.IsAlive) e.TakeDamage(InkWash.Combat.DamageInfo.Simple(60f, InkWash.Combat.Faction.Player, null));
        yield return null;
    }
    guard = 0;
    while (!run.AwaitingDoorCross && guard++ < 900) yield return null;
    yield return new WaitForSeconds(0.2f);
    sb.AppendLine("③ 门涡 DoorOpened 触发 = " + doorFx + " 次（期望 ≥1）  AwaitingDoorCross=" + run.AwaitingDoorCross);
    run.DoorOpened -= onDoor;

    sb.AppendLine("RESULT " + (emitFrames >= 8 && stains >= 1 && doorFx >= 1 ? "PASS" : "CHECK"));
    Debug.Log("[v39b]\n" + sb.ToString());
    yield break;
}

return Body();
