// v40c_test.cs —— 终版：①IsAirborneForTest 是属性（GetMethod 坑）②确定性盘旋
// （ForceNextAttackForTest("HoverOrbit") + ForceEnterAttackForTest）③全程回血护玩家
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
    var pc = Object.FindObjectOfType<InkWash.Player.PlayerController>();
    var player = pc != null ? pc.transform : null;
    var ph = player != null ? player.GetComponent<InkWash.Player.PlayerHealth>() : null;

    // ---- ① 生成龙 + 确定性进盘旋 ----
    var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
        "Assets/_Project/Prefabs/Enemies/Z_Enemy_MoLong.prefab");
    if (prefab == null) { sb.AppendLine("FAIL dragon prefab missing"); Debug.Log("[v40c]\n" + sb); yield break; }
    Vector3 spawnPos = (player != null ? player.position : Vector3.zero) + new Vector3(8f, 0f, 6f);
    var dragonGo = Object.Instantiate(prefab, spawnPos, Quaternion.identity);
    var dragon = dragonGo.GetComponent<InkWash.Enemies.EnemyDragon>();
    var dType = typeof(InkWash.Enemies.EnemyDragon);
    var mForceHover = dType.GetMethod("ForceBeginHoverForTest");
    var mForceNext = dType.GetMethod("ForceNextAttackForTest");
    var mForceEnter = dType.GetMethod("ForceEnterAttackForTest");
    var pAir = dType.GetProperty("IsAirborneForTest");
    sb.AppendLine("反射: ForceBegin=" + (mForceHover != null) + " ForceNext=" + (mForceNext != null)
        + " ForceEnter=" + (mForceEnter != null) + " IsAirborneProp=" + (pAir != null));
    yield return new WaitForSecondsRealtime(1.5f);   // 出场/索敌初始化

    bool air = false;
    var swA = System.Diagnostics.Stopwatch.StartNew();
    while (swA.Elapsed.TotalSeconds < 12.0)
    {
        if (ph != null && ph.IsAlive && ph.HealthRatio < 0.9f) ph.Heal(999f);
        mForceNext?.Invoke(dragon, new object[] { "HoverOrbit" });
        mForceEnter?.Invoke(dragon, null);
        mForceHover?.Invoke(dragon, null);
        yield return null; yield return null;
        air = (bool)(pAir != null ? pAir.GetValue(dragon, null) : false);
        if (air) break;
        yield return new WaitForSecondsRealtime(0.4f);
    }
    sb.AppendLine("airborne = " + air);

    // ---- ② 采样竖直包络（4s 窗口，动态口径）----
    var fSpine = dType.GetField("_spine", BindingFlags.NonPublic | BindingFlags.Instance);
    var spine = fSpine?.GetValue(dragon) as System.Collections.Generic.List<Transform>;
    if (spine == null || spine.Count < 3) { sb.AppendLine("FAIL spine list"); Debug.Log("[v40c]\n" + sb); yield break; }
    float vSpanMax = 0f, vSpanMin = float.MaxValue;
    int sampling = 0;
    var swS = System.Diagnostics.Stopwatch.StartNew();
    while (swS.Elapsed.TotalSeconds < 4.0)
    {
        if (ph != null && ph.IsAlive && ph.HealthRatio < 0.9f) ph.Heal(999f);
        // 保持盘旋（攻击冷却可能把龙拽下去）
        if (!air) break;
        float minY = float.MaxValue, maxY = float.MinValue;
        for (int i = 0; i < spine.Count; i++)
        {
            float y = spine[i].position.y;
            if (y < minY) minY = y; if (y > maxY) maxY = y;
        }
        float vSpan = maxY - minY;
        if (vSpan > vSpanMax) vSpanMax = vSpan;
        if (vSpan < vSpanMin) vSpanMin = vSpan;
        sampling++;
        yield return null;
    }
    air = (bool)(pAir != null ? pAir.GetValue(dragon, null) : false);
    sb.AppendLine(string.Format("竖直包络: max={0:F2}m min={1:F2}m 动态差={2:F2}m  采样帧={3}  采样末 airborne={4}",
        vSpanMax, vSpanMin, vSpanMax - vSpanMin, sampling, air));

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
    int injectFailOnce = 0;
    while (swC.Elapsed.TotalSeconds < 150.0)
    {
        if (run.AwaitingDoorCross) { cleared = true; break; }
        if (run.State == InkWash.Roguelike.RunState.GameOver)
        {
            sb.AppendLine("玩家死亡（t=" + swC.Elapsed.TotalSeconds.ToString("F0") + "s）→ 提前终止");
            break;
        }
        var panel = Object.FindObjectOfType<InkWash.UI.SkillChoicePanel>();
        if (panel != null && Time.timeScale == 0f)
        {
            bool ok = false;
            try { ok = panel.InjectChoice(0); } catch { }
            if (!ok && injectFailOnce < 3) { injectFailOnce++; sb.AppendLine("InjectChoice false @t=" + swC.Elapsed.TotalSeconds.ToString("F0")); }
        }
        if (ph != null && ph.IsAlive && ph.HealthRatio < 0.9f) ph.Heal(999f);
        foreach (var e in Object.FindObjectsOfType<InkWash.Enemies.EnemyBase>())
            if (e.IsAlive) e.TakeDamage(InkWash.Combat.DamageInfo.Simple(80f, InkWash.Combat.Faction.Player, null));
        if (swC.Elapsed.TotalSeconds >= nextReport)
        {
            nextReport += 10f;
            int alive = 0;
            foreach (var e in Object.FindObjectsOfType<InkWash.Enemies.EnemyBase>()) if (e.IsAlive) alive++;
            sb.AppendLine(string.Format("t={0:F0}s state={1} timeScale={2} alive={3}",
                swC.Elapsed.TotalSeconds, run.State, Time.timeScale, alive));
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
    Debug.Log("[v40c]\n" + sb.ToString());
    yield break;
}

return Body();
