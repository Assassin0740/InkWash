// v44a: 逐帧数值探针 —— 把「剑真正扫出去的时间窗」和「代码掉血时刻」对齐
// 每帧记录: 状态/nt/combo/phase/BladeSpeed/敌人HP/距离 + SwingStarted/HitMoment 事件
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
    if (ctl == null) { Debug.Log("[v44a] FAIL no ctl"); yield break; }
    var ph = Object.FindObjectOfType<InkWash.Combat.PlayerHealth>();
    var animator = ph != null ? ph.GetComponentInChildren<Animator>() : ctl.GetComponentInChildren<Animator>();
    var vfx = ctl.GetComponentInChildren<SwordVfx>(true);

    // 保命：玩家不死，避免敌人打断连击干扰测量
    if (ph != null) { ph.maxHealth = 100000f; ph.ResetHealth(); }

    var ctlType = ctl.GetType();
    var miTry = ctlType.GetMethod("TryAttack", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
    var miReset = ctlType.GetMethod("ResetToLocomotion", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
    if (miTry == null) { Debug.Log("[v44a] FAIL no TryAttack"); yield break; }

    // 找最近活敌
    EnemyBase enemy = null; float best = 1e9f;
    foreach (var e in Object.FindObjectsOfType<EnemyBase>())
    {
        if (!e.IsAlive) continue;
        float d = Vector3.Distance(e.transform.position, ctl.transform.position);
        if (d < best) { best = d; enemy = e; }
    }
    if (enemy == null) { Debug.Log("[v44a] FAIL no enemy alive"); yield break; }
    sb.AppendLine("enemy=" + enemy.name + " hp=" + enemy.Health + "/" + enemy.maxHealth + " dist=" + best.ToString("F2"));

    // 事件时间戳
    float t0 = Time.time;
    var marks = new StringBuilder();
    ctl.SwingStarted += s => marks.Append(" [SWING").Append(s).Append("@").Append((Time.time - t0).ToString("F3")).Append("]");
    ctl.HitMoment += s => marks.Append(" [HIT").Append(s).Append("@").Append((Time.time - t0).ToString("F3")).Append("]");

    System.Func<string> stateName = () =>
    {
        var st = animator.GetCurrentAnimatorStateInfo(0);
        string[] names = { "Idle", "Walk", "Run", "Dash", "Atk1", "Atk2", "Atk3", "Hit", "Cast", "Death" };
        foreach (var n in names) if (st.IsName(n)) return n;
        return "?";
    };

    // 把玩家摆到敌人面前 1.7m 并面向敌人
    System.Action face = () =>
    {
        Vector3 ep = enemy.transform.position;
        Vector3 dir = (ep - ctl.transform.position); dir.y = 0;
        float d = dir.magnitude;
        dir.Normalize();
        ctl.transform.position = ep - dir * 1.7f + Vector3.up * 0.05f;
        ctl.transform.rotation = Quaternion.LookRotation(dir);
    };

    float hpPrev = enemy.Health;

    IEnumerator Watch(StringBuilder bucket)
    {
        for (int i = 0; i < 110; i++)   // ~1.8s @60fps
        {
            yield return null;
            float hp = enemy.Health;
            string dmg = hp < hpPrev ? (" DMG-" + (hpPrev - hp).ToString("F0")) : "";
            hpPrev = hp;
            float bs = vfx != null ? vfx.BladeSpeed : -1f;
            var st = animator.GetCurrentAnimatorStateInfo(0);
            bucket.Append((Time.time - t0).ToString("F3"))
                  .Append(" ").Append(stateName())
                  .Append(" nt=").Append((st.normalizedTime % 1f).ToString("F2"))
                  .Append(" c=").Append(ctl.ComboStep)
                  .Append(" ph=").Append(ctl.PhaseTag)
                  .Append(" blade=").Append(bs.ToString("F1"))
                  .Append(" ehp=").Append(hp.ToString("F0"))
                  .Append(" d=").Append(Vector3.Distance(enemy.transform.position, ctl.transform.position).ToString("F2"))
                  .AppendLine(dmg);
            if (marks.Length > 0) { bucket.Append("   >>>").Append(marks.ToString()).AppendLine(); marks.Length = 0; }
        }
    }

    // ---- 三连击，窗口一开就按（复现玩家连打节奏）----
    var seg1 = new StringBuilder(); var seg2 = new StringBuilder(); var seg3 = new StringBuilder();

    face();
    miTry.Invoke(ctl, null);
    sb.AppendLine("=== press#1 @ " + (Time.time - t0).ToString("F3"));
    yield return Watch(seg1);

    // 等窗口开（Atk1 nt>=0.55）再按第二下
    float dl = Time.time + 2.5f;
    while (Time.time < dl)
    {
        var st = animator.GetCurrentAnimatorStateInfo(0);
        if (st.IsName("Atk1") && (st.normalizedTime % 1f) >= 0.55f) break;
        yield return null;
    }
    face();
    miTry.Invoke(ctl, null);
    sb.AppendLine("=== press#2 @ " + (Time.time - t0).ToString("F3"));
    yield return Watch(seg2);

    dl = Time.time + 2.5f;
    while (Time.time < dl)
    {
        var st = animator.GetCurrentAnimatorStateInfo(0);
        if (st.IsName("Atk2") && (st.normalizedTime % 1f) >= 0.55f) break;
        yield return null;
    }
    face();
    miTry.Invoke(ctl, null);
    sb.AppendLine("=== press#3 @ " + (Time.time - t0).ToString("F3"));
    yield return Watch(seg3);

    sb.AppendLine("---- Atk1 frames ----"); sb.Append(seg1);
    sb.AppendLine("---- Atk2 frames ----"); sb.Append(seg2);
    sb.AppendLine("---- Atk3 frames ----"); sb.Append(seg3);

    Debug.Log("[v44a]\n" + sb.ToString());
    yield break;
}

return Body();
