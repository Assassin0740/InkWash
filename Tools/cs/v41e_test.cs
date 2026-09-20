// v41e_test.cs —— 连拍：一次攻击循环内 8 张截图 + 每张对应播放的 clip
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
    if (enemy == null) { sb.AppendLine("FAIL"); Debug.Log("[v41e]\n" + sb); yield break; }
    if (player != null) enemy.Transform.position = player.position + player.forward * 2.0f;
    var anim = enemy.GetComponentInChildren<Animator>();

    // 相机对准怪物（借用主相机）
    var cam = Camera.main;
    for (int shot = 0; shot < 8; shot++)
    {
        if (ph != null && ph.IsAlive && ph.HealthRatio < 0.85f) ph.Heal(999f);
        // 相机看怪物
        if (cam != null && player != null)
        {
            Vector3 toEnemy = (enemy.Transform.position - player.position);
            cam.transform.position = player.position - toEnemy.normalized * 3.2f + Vector3.up * 1.6f;
            cam.transform.LookAt(enemy.Transform.position + Vector3.up * 1.2f);
        }
        string fn = "v41_burst_" + shot + ".png";
        ScreenCapture.CaptureScreenshot("Assets/../Tools/screenshots/" + fn);
        var ci = anim.GetCurrentAnimatorClipInfo(0);
        sb.AppendLine(shot + ": clip=" + (ci.Length > 0 ? ci[0].clip.name : "NONE")
            + " nT=" + anim.GetCurrentAnimatorStateInfo(0).normalizedTime.ToString("F2"));
        yield return new WaitForSecondsRealtime(0.22f);
    }
    Debug.Log("[v41e]\n" + sb.ToString());
    yield break;
}

return Body();
