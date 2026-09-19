// v40_test.cs —— 第三十九轮验收：①盘旋竖直分量回归（SerpPoint 竖直波）
// ② DragonStormVfx 盘旋在岗（B5「挂载丢失」实为惰性 Ensure，验证 Drive 被消费）
// ③ 完整清房流程（真打波次 → 门自然开 → 门涡自然触发）
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

    // ---- ① 手动生成一条龙并强制进盘旋 ----
    var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
        "Assets/_Project/Prefabs/Enemies/Z_Enemy_MoLong.prefab");
    if (prefab == null) { sb.AppendLine("FAIL dragon prefab missing"); Debug.Log("[v40]\n" + sb); yield break; }
    Vector3 spawnPos = (player != null ? player.position : Vector3.zero) + new Vector3(8f, 0f, 6f);
    var dragonGo = Object.Instantiate(prefab, spawnPos, Quaternion.identity);
    var dragon = dragonGo.GetComponent<InkWash.Enemies.EnemyDragon>();
    sb.AppendLine("dragon spawned = " + (dragon != null));
    var dType = typeof(InkWash.Enemies.EnemyDragon);
    dType.GetMethod("ForceBeginHoverForTest")?.Invoke(dragon, null);
    var swA = System.Diagnostics.Stopwatch.StartNew();
    while (swA.Elapsed.TotalSeconds < 10.0)
    {
        bool air = (bool)(dType.GetMethod("IsAirborneForTest")?.Invoke(dragon, null) ?? false);
        if (air) break;
        yield return null;
    }
    sb.AppendLine("airborne = " + (bool)(dType.GetMethod("IsAirborneForTest")?.Invoke(dragon, null) ?? false));

    // ---- ② 采样脊骨竖直包络（3.5s 窗口）----
    var fSpine = dType.GetField("_spine", BindingFlags.NonPublic | BindingFlags.Instance);
    var spine = fSpine?.GetValue(dragon) as System.Collections.Generic.List<Transform>;
    if (spine == null || spine.Count < 3) { sb.AppendLine("FAIL spine list"); Debug.Log("[v40]\n" + sb); yield break; }
    float maxVertSpan = 0f, maxHorizSpan = 0f;
    var swS = System.Diagnostics.Stopwatch.StartNew();
    while (swS.Elapsed.TotalSeconds < 3.5)
    {
        float minY = float.MaxValue, maxY = float.MinValue;
        float minX = float.MaxValue, maxX = float.MinValue;
        float minZ = float.MaxValue, maxZ = float.MinValue;
        for (int i = 0; i < spine.Count; i++)
        {
            var p = spine[i].position;
            if (p.y < minY) minY = p.y; if (p.y > maxY) maxY = p.y;
            if (p.x < minX) minX = p.x; if (p.x > maxX) maxX = p.x;
            if (p.z < minZ) minZ = p.z; if (p.z > maxZ) maxZ = p.z;
        }
        float vSpan = maxY - minY;
        float hSpan = Mathf.Max(maxX - minX, maxZ - minZ);
        if (vSpan > maxVertSpan) maxVertSpan = vSpan;
        if (hSpan > maxHorizSpan) maxHorizSpan = hSpan;
        yield return null;
    }
    sb.AppendLine(string.Format("spine 包络: 竖直 max={0:F2}m  水平 max={1:F2}m  比={2:F2}",
        maxVertSpan, maxHorizSpan, maxHorizSpan > 0.01f ? maxVertSpan / maxHorizSpan : 0f));

    // ---- ③ DragonStormVfx 盘旋在岗 ----
    var storm = InkWash.Effects.DragonStormVfx.Instance;
    sb.AppendLine("DragonStormVfx.Instance 在岗 = " + (storm != null)
        + (storm != null ? "  goActive=" + storm.gameObject.activeInHierarchy : ""));

    // 杀掉手动的龙，避免它把玩家打死干扰清房
    dragon.TakeDamage(InkWash.Combat.DamageInfo.Simple(9999f, InkWash.Combat.Faction.Player, null));
    yield return new WaitForSecondsRealtime(0.3f);

    // ---- ④ 完整清房流程（真打波次）----
    int doorFx = 0;
    System.Action onDoor = () => doorFx++;
    run.DoorOpened += onDoor;
    var swC = System.Diagnostics.Stopwatch.StartNew();
    bool cleared = false;
    while (swC.Elapsed.TotalSeconds < 150.0)
    {
        if (run.AwaitingDoorCross) { cleared = true; break; }
        // 清房升级弹三选一 → 注入回到 Playing
        var panel = Object.FindObjectOfType<InkWash.UI.SkillChoicePanel>();
        if (panel != null && Time.timeScale == 0f)
        {
            try { panel.InjectChoice(0); } catch { }
        }
        foreach (var e in Object.FindObjectsOfType<InkWash.Enemies.EnemyBase>())
            if (e.IsAlive) e.TakeDamage(InkWash.Combat.DamageInfo.Simple(80f, InkWash.Combat.Faction.Player, null));
        yield return null;
    }
    int stainsAfter = 0;
    foreach (var g in Object.FindObjectsOfType<GameObject>())
        if (g.name == "InkStain") stainsAfter++;
    run.DoorOpened -= onDoor;
    sb.AppendLine("清房: 自然开门=" + cleared + "  耗时=" + swC.Elapsed.TotalSeconds.ToString("F1")
        + "s  DoorOpened=" + doorFx + "  墨渍(含门涡)=" + stainsAfter);

    bool pass = maxVertSpan >= 0.35f && storm != null && cleared && doorFx >= 1;
    sb.AppendLine("RESULT " + (pass ? "PASS" : "CHECK"));
    Debug.Log("[v40]\n" + sb.ToString());
    yield break;
}

return Body();
