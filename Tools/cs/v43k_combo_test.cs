// v43k: runtime combo verification (play mode) — single attack auto-return + full 3-hit chain
using System.Collections;
using UnityEngine;

IEnumerator Body()
{
    var sb = new System.Text.StringBuilder();
    yield return new WaitForSecondsRealtime(1.5f);

    var run = Object.FindObjectOfType<InkWash.Roguelike.RunManager>();
    var sw3 = System.Diagnostics.Stopwatch.StartNew();
    while (run != null && run.State != InkWash.Roguelike.RunState.Playing && sw3.Elapsed.TotalSeconds < 25.0)
        yield return null;

    var ctl = Object.FindObjectOfType<InkWash.Player.PlayerController>();
    if (ctl == null) { Debug.Log("[v43k]\nRESULT FAIL no PlayerController"); yield break; }
    var ph = Object.FindObjectOfType<InkWash.Combat.PlayerHealth>();
    var animator = ph != null ? ph.GetComponentInChildren<Animator>() : ctl.GetComponentInChildren<Animator>();
    if (animator == null) { Debug.Log("[v43k]\nRESULT FAIL no animator"); yield break; }

    var ctlType = ctl.GetType();
    var miTry = ctlType.GetMethod("TryAttack", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
    if (miTry == null) { Debug.Log("[v43k]\nRESULT FAIL no TryAttack"); yield break; }

    var log = new System.Text.StringBuilder();
    float t0 = Time.realtimeSinceStartup;
    System.Func<string> stateName = () =>
    {
        var st = animator.GetCurrentAnimatorStateInfo(0);
        string[] names = { "Idle", "Walk", "Run", "Dash", "Atk1", "Atk2", "Atk3", "Hit", "Cast", "Death" };
        foreach (var n in names) if (st.IsName(n)) return n;
        return "?";
    };
    System.Func<float> nt = () => animator.GetCurrentAnimatorStateInfo(0).normalizedTime % 1f;
    System.Func<string> tag = () => stateName() + " nt=" + nt().ToString("F2") + " combo=" + ctl.ComboStep + " " + ctl.PhaseTag;

    IEnumerator WaitLog(float sec)
    {
        yield return new WaitForSecondsRealtime(sec);
        log.AppendLine("t+" + (Time.realtimeSinceStartup - t0).ToString("F2") + " " + tag());
    }

    int hp0 = ph != null ? (int)ph.Health : -1;

    // ---- 场景 A：单击 → 验证自动收势回 Idle（新拓扑 Atk1→Idle 出边） ----
    yield return WaitLog(0.3f);
    miTry.Invoke(ctl, null);
    log.AppendLine(">>> [A] press#1");
    for (int i = 0; i < 52; i++) yield return WaitLog(0.05f); // 2.6s
    sb.AppendLine("[A] end state = " + stateName() + " (期望 Idle)");
    sb.AppendLine("[A] timeline:");
    sb.AppendLine(log.ToString());
    log.Length = 0;

    // ---- 场景 B：三连击 ----
    yield return WaitLog(0.5f);
    miTry.Invoke(ctl, null);
    log.AppendLine(">>> [B] press#1");
    float deadline = Time.realtimeSinceStartup + 1.6f;
    while (!(stateName() == "Atk1" && nt() >= 0.55f) && Time.realtimeSinceStartup < deadline)
        yield return WaitLog(0.02f);
    bool w1 = stateName() == "Atk1" && nt() >= 0.55f;
    ScreenCapture.CaptureScreenshot("Assets/../Tools/screenshots/v43_run_atk1.png");
    yield return WaitLog(0.06f);
    miTry.Invoke(ctl, null);
    log.AppendLine(">>> [B] press#2 window1=" + w1);
    deadline = Time.realtimeSinceStartup + 1.6f;
    while (!(stateName() == "Atk2" && nt() >= 0.55f) && Time.realtimeSinceStartup < deadline)
        yield return WaitLog(0.02f);
    bool w2 = stateName() == "Atk2" && nt() >= 0.55f;
    ScreenCapture.CaptureScreenshot("Assets/../Tools/screenshots/v43_run_atk2.png");
    yield return WaitLog(0.06f);
    miTry.Invoke(ctl, null);
    log.AppendLine(">>> [B] press#3 window2=" + w2);
    yield return WaitLog(0.30f);
    ScreenCapture.CaptureScreenshot("Assets/../Tools/screenshots/v43_run_atk3.png");
    deadline = Time.realtimeSinceStartup + 3.0f;
    while (!(stateName() == "Idle" || stateName() == "Walk" || stateName() == "Run") && Time.realtimeSinceStartup < deadline)
        yield return WaitLog(0.02f);
    yield return WaitLog(0.1f);
    int hp1 = ph != null ? (int)ph.Health : -1;
    sb.AppendLine("[B] end state = " + stateName() + " combo=" + ctl.ComboStep);
    sb.AppendLine("[B] timeline:");
    sb.AppendLine(log.ToString());
    sb.AppendLine("hp " + hp0 + "->" + hp1 + "（掉血说明被敌人打断，时间线里应见 Hit）");
    sb.AppendLine("RESULT " + (w1 && w2 ? "PASS chain" : "CHECK chain w1=" + w1 + " w2=" + w2));
    Debug.Log("[v43k]\n" + sb);
    yield break;
}

return Body();
