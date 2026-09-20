// v44d: 老链回滚后的运行时验收 —— 命中时刻 vs 剑速 vs 掉血 + 连击衔接
// （v2：waitState 改为协程；原 lambda 忙循环无 yield 会冻死主线程——本轮实测教训）
using System.Collections;
using System.Text;
using UnityEngine;
using InkWash.Player;
using InkWash.Effects;
using InkWash.Enemies;

IEnumerator Body()
{
    var sb = new StringBuilder();
    yield return new WaitForSecondsRealtime(1.5f);

    var run = Object.FindObjectOfType<InkWash.Roguelike.RunManager>();
    var sw3 = System.Diagnostics.Stopwatch.StartNew();
    while (run != null && run.State != InkWash.Roguelike.RunState.Playing && sw3.Elapsed.TotalSeconds < 25.0)
        yield return null;

    var ctl = Object.FindObjectOfType<PlayerController>();
    if (ctl == null) { Debug.Log("[v44d] FAIL no ctl"); yield break; }
    var ph = Object.FindObjectOfType<InkWash.Combat.PlayerHealth>();
    var animator = ph != null ? ph.GetComponentInChildren<Animator>() : ctl.GetComponentInChildren<Animator>();
    var vfx = ctl.GetComponentInChildren<SwordVfx>(true);
    if (ph != null) { ph.maxHealth = 100000f; ph.ResetHealth(); }

    var ctlType = ctl.GetType();
    var miTry = ctlType.GetMethod("TryAttack", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
    if (miTry == null) { Debug.Log("[v44d] FAIL no TryAttack"); yield break; }

    EnemyBase enemy = null; float best = 1e9f;
    foreach (var e in Object.FindObjectsOfType<EnemyBase>())
    {
        if (!e.IsAlive) continue;
        float d = Vector3.Distance(e.transform.position, ctl.transform.position);
        if (d < best) { best = d; enemy = e; }
    }
    if (enemy == null) { Debug.Log("[v44d] FAIL no enemy alive"); yield break; }
    sb.AppendLine("enemy=" + enemy.name + " hp=" + enemy.Health + "/" + enemy.maxHealth + " dist=" + best.ToString("F2"));

    float t0 = Time.time;
    var marks = new StringBuilder();
    ctl.SwingStarted += s => marks.Append(" [SWING").Append(s).Append("@").Append((Time.time - t0).ToString("F3")).Append("]");
    ctl.HitMoment += s => marks.Append(" [HIT").Append(s).Append("@").Append((Time.time - t0).ToString("F3")).Append("]");

    System.Func<string> stateName = () =>
    {
        var st = animator.GetCurrentAnimatorStateInfo(0);
        string[] names = { "Idle", "Walk", "Run", "Dash", "Atk1Rec", "Atk2Rec", "Atk1", "Atk2", "Atk3", "Hit", "Cast", "Death" };
        foreach (var n in names) if (st.IsName(n)) return n;
        return "?";
    };

    System.Action face = () =>
    {
        Vector3 dir = enemy.transform.position - ctl.transform.position; dir.y = 0;
        dir.Normalize();
        ctl.transform.position = enemy.transform.position - dir * 1.7f + Vector3.up * 0.05f;
        ctl.transform.rotation = Quaternion.LookRotation(dir);
    };

    float hpPrev = enemy.Health;

    IEnumerator Watch(float seconds, string shotPrefix)
    {
        int shot = 0; float nextShot = 0f;
        float end = Time.time + seconds;
        while (Time.time < end)
        {
            yield return null;
            float hp = enemy.Health;
            string dmg = hp < hpPrev ? (" DMG-" + (hpPrev - hp).ToString("F0")) : "";
            hpPrev = hp;
            float bs = vfx != null ? vfx.BladeSpeed : -1f;
            var st = animator.GetCurrentAnimatorStateInfo(0);
            sb.Append((Time.time - t0).ToString("F3"))
              .Append(" ").Append(stateName())
              .Append(" nt=").Append((st.normalizedTime % 1f).ToString("F2"))
              .Append(" c=").Append(ctl.ComboStep)
              .Append(" blade=").Append(bs.ToString("F1"))
              .Append(" ehp=").Append(hp.ToString("F0"))
              .AppendLine(dmg);
            if (marks.Length > 0) { sb.Append("   >>>").Append(marks.ToString()).AppendLine(); marks.Length = 0; }
            if (shotPrefix != null && Time.time - t0 >= nextShot && shot < 14)
            {
                ScreenCapture.CaptureScreenshot("Assets/../Tools/screenshots/" + shotPrefix + shot.ToString("00") + ".png");
                shot++; nextShot = (Time.time - t0) + 0.1f;
            }
        }
    }

    // 协程版窗口等待：等到「仍处于该状态且 nt 越线」，过期返回 false
    bool _wResult = false;
    IEnumerator WaitWindow(string name, float ntMin)
    {
        float dl = Time.time + 2.5f;
        _wResult = false;
        while (Time.time < dl)
        {
            yield return null;
            var st = animator.GetCurrentAnimatorStateInfo(0);
            if (st.IsName(name) && (st.normalizedTime % 1f) >= ntMin) { _wResult = true; yield break; }
            if (!st.IsName(name) && !animator.IsInTransition(0)) yield break;   // 已离开该状态
        }
    }

    // ---- 场景A：单击自动收势 ----
    face();
    miTry.Invoke(ctl, null);
    sb.AppendLine("=== A press#1 @ " + (Time.time - t0).ToString("F3"));
    yield return Watch(1.4f, null);
    sb.AppendLine("A end state = " + stateName() + " (期望 Idle)");

    yield return new WaitForSeconds(0.5f);

    // ---- 场景B：三连击（窗口 nt>=0.3 接段）----
    face();
    miTry.Invoke(ctl, null);
    sb.AppendLine("=== B press#1 @ " + (Time.time - t0).ToString("F3"));
    yield return Watch(0.45f, "v44_b1_");
    yield return WaitWindow("Atk1Rec", ctl.comboRecCancelStart[0]);
    bool w1 = _wResult;
    face(); miTry.Invoke(ctl, null);
    sb.AppendLine("=== B press#2 window=" + w1 + " @ " + (Time.time - t0).ToString("F3"));
    yield return Watch(0.35f, null);
    yield return WaitWindow("Atk2Rec", ctl.comboRecCancelStart[1]);
    bool w2 = _wResult;
    face(); miTry.Invoke(ctl, null);
    sb.AppendLine("=== B press#3 window=" + w2 + " @ " + (Time.time - t0).ToString("F3"));
    yield return Watch(1.9f, null);
    sb.AppendLine("B end state = " + stateName() + " combo=" + ctl.ComboStep);
    sb.AppendLine("RESULT " + (w1 && w2 ? "PASS" : "CHECK w1=" + w1 + " w2=" + w2));
    Debug.Log("[v44d]\n" + sb.ToString());
    yield break;
}

return Body();
