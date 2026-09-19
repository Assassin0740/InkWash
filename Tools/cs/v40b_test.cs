// v40b_test.cs —— v40 修订：①起飞重试制 ②竖直包络动态口径（vSpanMax−vSpanMin）
// ③玩家持续回血防龙击杀 ④清房循环每 10s 报状态
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
    sb.AppendLine("run.State(开局) = " + run.State);
    var pc = Object.FindObjectOfType<InkWash.Player.PlayerController>();
    var player = pc != null ? pc.transform : null;
    var ph = player != null ? player.GetComponent<InkWash.Player.PlayerHealth>() : null;

    // ---- ① 生成龙，重试强制起飞 ----
    var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
        "Assets/_Project/Prefabs/Enemies/Z_Enemy_MoLong.prefab");
    if (prefab == null) { sb.AppendLine("FAIL dragon prefab missing"); Debug.Log("[v40b]\n" + sb); yield break; }
    Vector3 spawnPos = (player != null ? player.position : Vector3.zero) + new Vector3(8f, 0f, 6f);
    var dragonGo = Object.Instantiate(prefab, spawnPos, Quaternion.identity);
    var dragon = dragonGo.GetComponent<InkWash.Enemies.EnemyDragon>();
    var dType = typeof(InkWash.Enemies.EnemyDragon);
    var mForce = dType.GetMethod("ForceBeginHoverForTest");
    var mAir = dType.GetMethod("IsAirborneForTest");
    yield return new WaitForSecondsRealtime(1.0f);   // 让 Start/脊柱初始化先跑完
    bool air = false;
    var swA = System.Diagnostics.Stopwatch.StartNew();
    while (swA.Elapsed.TotalSeconds < 10.0)
    {
        mForce?.Invoke(dragon, null);
        yield return null; yield return null;
        air = (bool)(mAir?.Invoke(dragon, null) ?? false);
        if (air) break;
        yield return new WaitForSecondsRealtime(0.3f);
    }
    sb.AppendLine("airborne = " + air);

    // ---- ② 采样竖直包络（4s 窗口，动态口径）----
    var fSpine = dType.GetField("_spine", BindingFlags.NonPublic | BindingFlags.Instance);
    var spine = fSpine?.GetValue(dragon) as System.Collections.Generic.List<Transform>;
    if (spine == null || spine.Count < 3) { sb.AppendLine("FAIL spine list"); Debug.Log("[v40b]\n" + sb); yield break; }
    float vSpanMax = 0f, vSpanMin = float.MaxValue;
    var swS = System.Diagnostics.Stopwatch.StartNew();
    while (swS.Elapsed.TotalSeconds < 4.0)
    {
        float minY = float.MaxValue, maxY = float.MinValue;
        for (int i = 0; i < spine.Count; i++)
        {
            float y = spine[i].position.y;
            if (y < minY) minY = y; if (y > maxY) maxY = y;
        }
        float vSpan = maxY - minY;
        if (vSpan > vSpanMax) vSpanMax = vSpan;
        if (vSpan < vSpanMin) vSpanMin = vSpan;
        yield return null;
    }
    sb.AppendLine(string.Format("竖直包络: max={0:F2}m min={1:F2}m 动态差={2:F2}m（修复前应≈0）",
        vSpanMax, vSpanMin, vSpanMax - vSpanMin));

    var storm = InkWash.Effects.DragonStormVfx.Instance;
    sb.AppendLine("DragonStormVfx.Instance 在岗 = " + (storm != null));

    // 杀龙，防止干扰清房
    dragon.TakeDamage(InkWash.Combat.DamageInfo.Simple(9999f, InkWash.Combat.Faction.Player, null));
    yield return new WaitForSecondsRealtime(0.3f);
    sb.AppendLine("dragon killed = " + !dragon.IsAlive);

    // ---- ③ 完整清房（真打波次）----
    int doorFx = 0;
    System.Action onDoor = () => doorFx++;
    run.DoorOpened += onDoor;
    var swC = System.Diagnostics.Stopwatch.StartNew();
    float nextReport = 10f;
    bool cleared = false;
    while (swC.Elapsed.TotalSeconds < 150.0)
    {
        if (run.AwaitingDoorCross) { cleared = true; break; }
        var panel = Object.FindObjectOfType<InkWash.UI.SkillChoicePanel>();
        if (panel != null && Time.timeScale == 0f)
        {
            bool ok = false;
            try { ok = panel.InjectChoice(0); } catch { }
            if (!ok) sb.AppendLine("InjectChoice 返回 false（t=" + swC.Elapsed.TotalSeconds.ToString("F0") + "s）");
        }
        if (ph != null && ph.IsAlive && ph.HealthRatio < 0.9f) ph.Heal(999f);
        int alive = 0;
        foreach (var e in Object.FindObjectsOfType<InkWash.Enemies.EnemyBase>())
        {
            if (!e.IsAlive) continue;
            alive++;
            e.TakeDamage(InkWash.Combat.DamageInfo.Simple(80f, InkWash.Combat.Faction.Player, null));
        }
        if (swC.Elapsed.TotalSeconds >= nextReport)
        {
            nextReport += 10f;
            sb.AppendLine(string.Format("t={0:F0}s state={1} timeScale={2} alive={3} playerAlive={4}",
                swC.Elapsed.TotalSeconds, run.State, Time.timeScale, alive, ph != null ? ph.IsAlive : false));
        }
        yield return null;
    }
    int stainsAfter = 0;
    foreach (var g in Object.FindObjectsOfType<GameObject>())
        if (g.name == "InkStain") stainsAfter++;
    run.DoorOpened -= onDoor;
    sb.AppendLine("清房: 自然开门=" + cleared + "  耗时=" + swC.Elapsed.TotalSeconds.ToString("F1")
        + "s  DoorOpened=" + doorFx + "  墨渍(含门涡)=" + stainsAfter
        + "  终态=" + run.State);

    bool pass = air && (vSpanMax - vSpanMin) >= 0.30f && storm != null && cleared && doorFx >= 1;
    sb.AppendLine("RESULT " + (pass ? "PASS" : "CHECK"));
    Debug.Log("[v40b]\n" + sb.ToString());
    yield break;
}

return Body();
