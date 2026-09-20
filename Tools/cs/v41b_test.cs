// v41b_test.cs —— 运行时实拍怪物动画层：打 Attack 触发器，读实际播放的 clip
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
    yield return new WaitForSecondsRealtime(1.0f);

    var ph = Object.FindObjectOfType<InkWash.Player.PlayerHealth>();
    if (ph != null) ph.Heal(9999f);

    // 找一只活着的 MoGuai
    InkWash.Enemies.EnemyBase enemy = null;
    var sw0 = System.Diagnostics.Stopwatch.StartNew();
    while (enemy == null && sw0.Elapsed.TotalSeconds < 25.0)
    {
        foreach (var e in Object.FindObjectsOfType<InkWash.Enemies.EnemyBase>())
            if (e.IsAlive && e.name.Contains("MoGuai")) { enemy = e; break; }
        if (enemy == null) yield return null;
    }
    if (enemy == null) { sb.AppendLine("FAIL no MoGuai"); Debug.Log("[v41b]\n" + sb); yield break; }
    var anim = enemy.GetComponentInChildren<Animator>();
    sb.AppendLine("animator = " + (anim != null ? anim.runtimeAnimatorController.name : "NULL")
        + "  cullingMode=" + (anim != null ? anim.cullingMode.ToString() : "-")
        + "  enabled=" + (anim != null ? anim.enabled.ToString() : "-"));
    // 当前状态
    var st = anim.GetCurrentAnimatorStateInfo(0);
    sb.AppendLine("当前状态 hash=" + st.shortNameHash + " clip=" + (anim.GetCurrentAnimatorClipInfo(0).Length > 0 ? anim.GetCurrentAnimatorClipInfo(0)[0].clip.name : "NONE"));
    // 打 Attack 触发器
    anim.SetTrigger("Attack");
    for (int f = 0; f < 30; f++)
    {
        yield return null;
        if (f % 5 == 0 || f < 6)
        {
            var s = anim.GetCurrentAnimatorStateInfo(0);
            var ci = anim.GetCurrentAnimatorClipInfo(0);
            sb.AppendLine("f" + f + ": state=" + s.shortNameHash.ToString() + " nT=" + s.normalizedTime.ToString("F2")
                + " clip=" + (ci.Length > 0 ? ci[0].clip.name : "NONE")
                + " speed=" + anim.speed);
        }
        if (ph != null && ph.IsAlive && ph.HealthRatio < 0.9f) ph.Heal(999f);
    }
    sb.AppendLine("RESULT CHECK(看上方 clip 实拍)");
    Debug.Log("[v41b]\n" + sb.ToString());
    yield break;
}

return Body();
