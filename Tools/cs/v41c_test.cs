// v41c_test.cs —— 观测 MoGuai AI 状态机 12s：到底进没进 Attack、触发器发没发
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
    yield return new WaitForSecondsRealtime(1.0f);
    var pc = Object.FindObjectOfType<InkWash.Player.PlayerController>();
    var player = pc != null ? pc.transform : null;
    var ph = player != null ? player.GetComponent<InkWash.Player.PlayerHealth>() : null;

    InkWash.Enemies.EnemyBase enemy = null;
    var sw0 = System.Diagnostics.Stopwatch.StartNew();
    while (enemy == null && sw0.Elapsed.TotalSeconds < 25.0)
    {
        foreach (var e in Object.FindObjectsOfType<InkWash.Enemies.EnemyBase>())
            if (e.IsAlive && e.name.Contains("MoGuai")) { enemy = e; break; }
        if (enemy == null) yield return null;
    }
    if (enemy == null) { sb.AppendLine("FAIL no MoGuai"); Debug.Log("[v41c]\n" + sb); yield break; }

    // 反射读私有状态
    var eType = typeof(InkWash.Enemies.EnemyBase);
    var fState = eType.GetField("_state", BindingFlags.NonPublic | BindingFlags.Instance);
    var fCool = eType.GetField("_cooldownTimer", BindingFlags.NonPublic | BindingFlags.Instance);
    var fWindup = eType.GetField("attackWindup", BindingFlags.Public | BindingFlags.Instance);
    var fRange = eType.GetField("attackRange", BindingFlags.Public | BindingFlags.Instance);
    sb.AppendLine("attackWindup=" + (fWindup != null ? fWindup.GetValue(enemy) : "?")
        + " attackRange=" + (fRange != null ? fRange.GetValue(enemy) : "?"));

    // 传送到玩家旁边，强制交战
    if (player != null)
        enemy.Transform.position = player.position + player.forward * 1.8f;
    var anim = enemy.GetComponentInChildren<Animator>();
    string lastState = "";
    var sw = System.Diagnostics.Stopwatch.StartNew();
    while (sw.Elapsed.TotalSeconds < 12.0)
    {
        if (ph != null && ph.IsAlive && ph.HealthRatio < 0.85f) ph.Heal(999f);
        var s = fState != null ? fState.GetValue(enemy).ToString() : "?";
        if (s != lastState)
        {
            lastState = s;
            var ci = anim != null ? anim.GetCurrentAnimatorClipInfo(0) : null;
            sb.AppendLine("t=" + sw.Elapsed.TotalSeconds.ToString("F1") + "s → " + s
                + "  dist=" + (player != null ? Vector3.Distance(enemy.Transform.position, player.position).ToString("F1") : "-")
                + "  clip=" + (ci != null && ci.Length > 0 ? ci[0].clip.name : "NONE"));
        }
        yield return null;
    }
    sb.AppendLine("最终 dist=" + (player != null ? Vector3.Distance(enemy.Transform.position, player.position).ToString("F1") : "-"));
    Debug.Log("[v41c]\n" + sb.ToString());
    yield break;
}

return Body();
