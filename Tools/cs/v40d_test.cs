// v40d_test.cs —— 竖直分量 A/B 消融：同一条龙同在盘旋态，
// swimWaveVertRatio=0 采样 2s → 0.45 采样 2s，对比竖直包络。
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

    var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
        "Assets/_Project/Prefabs/Enemies/Z_Enemy_MoLong.prefab");
    var dragonGo = Object.Instantiate(prefab, player.position + new Vector3(8f, 0f, 6f), Quaternion.identity);
    var dragon = dragonGo.GetComponent<InkWash.Enemies.EnemyDragon>();
    var dType = typeof(InkWash.Enemies.EnemyDragon);
    var pAir = dType.GetProperty("IsAirborneForTest");
    yield return new WaitForSecondsRealtime(1.5f);

    // 强制进盘旋
    var swA = System.Diagnostics.Stopwatch.StartNew();
    bool air = false;
    while (swA.Elapsed.TotalSeconds < 12.0)
    {
        if (ph != null && ph.IsAlive && ph.HealthRatio < 0.9f) ph.Heal(999f);
        dType.GetMethod("ForceNextAttackForTest")?.Invoke(dragon, new object[] { "HoverOrbit" });
        dType.GetMethod("ForceEnterAttackForTest")?.Invoke(dragon, null);
        dType.GetMethod("ForceBeginHoverForTest")?.Invoke(dragon, null);
        yield return null; yield return null;
        air = (bool)(pAir != null ? pAir.GetValue(dragon, null) : false);
        if (air) break;
        yield return new WaitForSecondsRealtime(0.4f);
    }
    sb.AppendLine("airborne = " + air);
    if (!air) { sb.AppendLine("RESULT CHECK(no hover)"); Debug.Log("[v40d]\n" + sb); yield break; }

    var fSpine = dType.GetField("_spine", BindingFlags.NonPublic | BindingFlags.Instance);
    var spine = fSpine?.GetValue(dragon) as System.Collections.Generic.List<Transform>;
    if (spine == null || spine.Count < 3) { sb.AppendLine("FAIL spine"); Debug.Log("[v40d]\n" + sb); yield break; }

    System.Func<float> SampleSpan = () =>
    {
        float minY = float.MaxValue, maxY = float.MinValue;
        for (int i = 0; i < spine.Count; i++)
        {
            float y = spine[i].position.y;
            if (y < minY) minY = y; if (y > maxY) maxY = y;
        }
        return maxY - minY;
    };

    // A：ratio = 0（关竖直分量）
    dragon.swimWaveVertRatio = 0f;
    yield return new WaitForSecondsRealtime(0.6f);   // 等旧姿态滑出
    float aMax = 0f, aMin = float.MaxValue;
    var swS = System.Diagnostics.Stopwatch.StartNew();
    while (swS.Elapsed.TotalSeconds < 2.2)
    {
        if (ph != null && ph.IsAlive && ph.HealthRatio < 0.9f) ph.Heal(999f);
        float v = SampleSpan();
        if (v > aMax) aMax = v; if (v < aMin) aMin = v;
        yield return null;
    }

    // B：ratio = 0.45（开竖直分量，prefab/C# 默认）
    dragon.swimWaveVertRatio = 0.45f;
    yield return new WaitForSecondsRealtime(0.6f);
    float bMax = 0f, bMin = float.MaxValue;
    swS.Restart();
    while (swS.Elapsed.TotalSeconds < 2.2)
    {
        if (ph != null && ph.IsAlive && ph.HealthRatio < 0.9f) ph.Heal(999f);
        float v = SampleSpan();
        if (v > bMax) bMax = v; if (v < bMin) bMin = v;
        yield return null;
    }

    sb.AppendLine(string.Format("A(ratio=0)  竖直包络 [{0:F2}, {1:F2}] m", aMin, aMax));
    sb.AppendLine(string.Format("B(ratio=.45) 竖直包络 [{0:F2}, {1:F2}] m", bMin, bMax));
    sb.AppendLine(string.Format("Δmax = {0:F2} m（>0.25 即竖直波实锤）", bMax - aMax));
    dragon.TakeDamage(InkWash.Combat.DamageInfo.Simple(9999f, InkWash.Combat.Faction.Player, null));

    bool pass = (bMax - aMax) >= 0.25f;
    sb.AppendLine("RESULT " + (pass ? "PASS" : "CHECK"));
    Debug.Log("[v40d]\n" + sb.ToString());
    yield break;
}

return Body();
