// v39j_test.cs —— B 组终测 v2：dash 深诊 + 墨渍 + 反射直调 OnRoomCleared 验门涡
using System.Collections;
using System.Reflection;
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
    yield return new WaitForSecondsRealtime(0.5f);

    // ---- ① dash：注入 + 反射看 DashInkTrail 内部 ----
    var trail = Object.FindObjectOfType<InkWash.Effects.DashInkTrail>();
    var pc = Object.FindObjectOfType<InkWash.Player.PlayerController>();
    if (trail != null && pc != null)
    {
        var tType = typeof(InkWash.Effects.DashInkTrail);
        var fPc = tType.GetField("_pc", BindingFlags.NonPublic | BindingFlags.Instance);
        var fTr = tType.GetField("_trail", BindingFlags.NonPublic | BindingFlags.Instance);
        var innerPc = fPc.GetValue(trail) as InkWash.Player.PlayerController;
        var innerTr = fTr.GetValue(trail) as TrailRenderer;
        sb.AppendLine("DashInkTrail._pc==测试pc : " + (innerPc == pc) + "  _trail=" + (innerTr != null)
            + "  enabled=" + trail.enabled + "  goActive=" + trail.gameObject.activeInHierarchy);
        if (innerTr != null)
        {
            pc.BeginInputOverride();
            pc.RequestInjectedDash();
            int emitFrames = 0;
            for (int i = 0; i < 12; i++)
            {
                yield return null;
                if (innerTr.emitting) emitFrames++;
            }
            pc.EndInputOverride();
            sb.AppendLine("dash: emitting " + emitFrames + "/12  positionCount=" + innerTr.positionCount
                + "  trailGoActive=" + innerTr.gameObject.activeInHierarchy);
        }
    }
    else sb.AppendLine("dash 组件缺失: trail=" + (trail != null) + " pc=" + (pc != null));

    // ---- ② 墨渍 ----
    InkWash.Enemies.EnemyBase enemy = null;
    var sw0 = System.Diagnostics.Stopwatch.StartNew();
    while (enemy == null && sw0.Elapsed.TotalSeconds < 25.0)
    {
        foreach (var e in Object.FindObjectsOfType<InkWash.Enemies.EnemyBase>())
            if (e.IsAlive) { enemy = e; break; }
        if (enemy == null) yield return null;
    }
    if (enemy == null) { sb.AppendLine("FAIL no enemy"); Debug.Log("[v39j]\n" + sb); yield break; }
    var info = InkWash.Combat.DamageInfo.Simple(15f, InkWash.Combat.Faction.Player, null);
    info.hitPoint = enemy.Transform.position + Vector3.up * 1.0f;
    info.hitDirection = Vector3.forward;
    enemy.TakeDamage(info);
    yield return null; yield return null;
    int stains = 0;
    foreach (var g in Object.FindObjectsOfType<GameObject>())
        if (g.name == "InkStain") stains++;
    sb.AppendLine("受击墨渍 = " + stains);

    // ---- ③ 门涡：反射直调 OnRoomCleared（绕过波次长流程）----
    int doorFx = 0;
    System.Action onDoor = () => doorFx++;
    run.DoorOpened += onDoor;
    var mi = typeof(InkWash.Roguelike.RunManager).GetMethod("OnRoomCleared",
        BindingFlags.NonPublic | BindingFlags.Instance);
    sb.AppendLine("OnRoomCleared 反射 = " + (mi != null ? "ok" : "NULL"));
    if (mi != null) mi.Invoke(run, null);
    yield return null; yield return null;
    int doorStains = 0;
    foreach (var g in Object.FindObjectsOfType<GameObject>())
        if (g.name == "InkStain") doorStains++;
    run.DoorOpened -= onDoor;
    sb.AppendLine("门涡触发=" + doorFx + "（期望≥1）  门开=" + run.AwaitingDoorCross + "  墨渍=" + stains + "→" + doorStains);

    bool pass = doorFx >= 1 && stains >= 1;
    sb.AppendLine("RESULT " + (pass ? "PASS" : "CHECK"));
    Debug.Log("[v39j]\n" + sb.ToString());
    yield break;
}

return Body();
