// v39_test.cs —— B 组帧驱动冒烟：拖尾 / 墨渍 / 门涡
using System.Collections;
using UnityEngine;

IEnumerator Body()
{
    var sb = new System.Text.StringBuilder();
    Application.runInBackground = true;
    var run = Object.FindObjectOfType<InkWash.Roguelike.RunManager>();
    if (run == null) { Debug.Log("[v39] FAIL no RunManager"); yield break; }

    // ---- ① 冲刺拖尾装配 ----
    var trail = Object.FindObjectOfType<InkWash.Effects.DashInkTrail>();
    sb.AppendLine("DashInkTrail = " + (trail != null ? "已装配" : "NULL"));
    TrailRenderer tr = null;
    if (trail != null) tr = trail.GetComponentInChildren<TrailRenderer>();
    sb.AppendLine("  TrailRenderer = " + (tr != null ? "ok time=" + tr.time : "NULL"));

    // 帧驱动冲刺：注入 Dash 后跑 12 帧，统计 emitting 帧数与拖尾点数
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
        sb.AppendLine("  冲刺中 emitting 帧数 = " + emitFrames + "/12  trail.positionCount=" + tr.positionCount);
    }

    // ---- ② 开局打怪 → 墨渍 ----
    run.ResetForTest(); yield return null;
    run.StartRun();
    int guard = 0;
    while (run.State != InkWash.Roguelike.RunState.Playing && guard++ < 900) yield return null;
    guard = 0;
    InkWash.Enemies.EnemyBase enemy = null;
    while (enemy == null && guard++ < 1200)
    {
        foreach (var e in Object.FindObjectsOfType<InkWash.Enemies.EnemyBase>())
            if (e.IsAlive) { enemy = e; break; }
        if (enemy == null) yield return null;
    }
    if (enemy == null) { sb.AppendLine("FAIL 找不到活敌人"); Debug.Log("[v39]\n" + sb); yield break; }

    int before = Object.FindObjectsOfType<InkWash.Effects.InkStain>().Length; // driver 数量，恒 1
    var info = InkWash.Combat.DamageInfo.Simple(15f, InkWash.Combat.Faction.Player, null);
    info.hitPoint = enemy.Transform.position + Vector3.up * 1.0f;
    info.hitDirection = Vector3.forward;
    enemy.TakeDamage(info);
    yield return null; yield return null;

    // 数场上墨渍 quad
    int stains = 0;
    foreach (var g in Object.FindObjectsOfType<GameObject>())
        if (g.name == "InkStain") stains++;
    sb.AppendLine("受击一次后 InkStain quad 数 = " + stains + "（期望 ≥1）");

    // ---- ③ 清房 → 门涡（InkHitVfx 计数 + 门下墨渍）----
    int stainsBeforeDoor = stains;
    enemy.TakeDamage(new InkWash.Combat.DamageInfo { amount = 99999f, sourceFaction = InkWash.Combat.Faction.Player, hitPoint = enemy.Transform.position, hitDirection = Vector3.forward });
    // 杀光剩余
    guard = 0;
    while (guard++ < 600)
    {
        bool anyAlive = false;
        foreach (var e in Object.FindObjectsOfType<InkWash.Enemies.EnemyBase>())
            if (e.IsAlive) { anyAlive = true; e.TakeDamage(InkWash.Combat.DamageInfo.Simple(99999f, InkWash.Combat.Faction.Player, null)); }
        if (!anyAlive) break;
        yield return null;
    }
    guard = 0;
    while (!run.AwaitingDoorCross && guard++ < 600) yield return null;
    yield return null; yield return null;
    int stainsAfterDoor = 0;
    foreach (var g in Object.FindObjectsOfType<GameObject>())
        if (g.name == "InkStain") stainsAfterDoor++;
    sb.AppendLine("门开前墨渍=" + stainsBeforeDoor + " → 门开后墨渍=" + stainsAfterDoor + "（期望增加 = 门数）");
    sb.AppendLine("AwaitingDoorCross = " + run.AwaitingDoorCross);

    sb.AppendLine("RESULT " + (emitFrames >= 8 && stains >= 1 && stainsAfterDoor > stainsBeforeDoor ? "PASS" : "CHECK"));
    Debug.Log("[v39]\n" + sb.ToString());
    yield break;
}

return Body();
