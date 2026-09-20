// v45d: 动作体检 —— ①三连击衔接 ②走/跑滑步 ③跑步中死亡能否播死亡动画
using System.Collections;
using System.Text;
using UnityEngine;
using InkWash.Player;
using InkWash.Combat;

IEnumerator Body()
{
    var sb = new StringBuilder();
    yield return new WaitForSecondsRealtime(1.5f);

    var run = Object.FindObjectOfType<InkWash.Roguelike.RunManager>();
    var sw = System.Diagnostics.Stopwatch.StartNew();
    while (run != null && run.State != InkWash.Roguelike.RunState.Playing && sw.Elapsed.TotalSeconds < 25.0)
        yield return null;

    var ctl = Object.FindObjectOfType<PlayerController>();
    if (ctl == null) { Debug.Log("[v45d] FAIL no ctl"); yield break; }
    var ph = Object.FindObjectOfType<PlayerHealth>();
    var animator = ph != null ? ph.GetComponentInChildren<Animator>() : ctl.GetComponentInChildren<Animator>();
    var lf = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
    var rf = animator.GetBoneTransform(HumanBodyBones.RightFoot);

    string[] names = { "Idle", "Walk", "Run", "Dash", "Atk1Rec", "Atk2Rec", "Atk1", "Atk2", "Atk3", "Hit", "Cast", "Death" };
    System.Func<string> stateName = () =>
    {
        var st = animator.GetCurrentAnimatorStateInfo(0);
        foreach (var n in names) if (st.IsName(n)) return n;
        return "?";
    };

    // ---------- 滑步测量：着地帧的脚部世界水平速度 ----------
    // 判据：脚贴地时若还在水平移动 = 滑步。无滑步时应接近 0。
    IEnumerator MeasureSlip(string label, bool runHeld, float seconds)
    {
        ctl.BeginInputOverride();
        ctl.SetInjectedMove(new Vector2(0f, 1f), runHeld);
        yield return new WaitForSecondsRealtime(1.0f);   // 等速度稳定

        float end = Time.realtimeSinceStartup + seconds;
        Vector3 lp = lf.position, rp = rf.position;
        float tPrev = Time.realtimeSinceStartup;
        // 先整段采样（脚踝 y / 位移 / dt），再离线按「最低脚踝 + 8cm」自适应判定着地相
        var ys = new System.Collections.Generic.List<float>();
        var vs = new System.Collections.Generic.List<float>();
        var dts = new System.Collections.Generic.List<float>();
        int frames = 0;
        float bodySum = 0f, motSum = 0f;
        string stName = "?";

        while (Time.realtimeSinceStartup < end)
        {
            yield return null;
            float now = Time.realtimeSinceStartup;
            float dt = now - tPrev;
            if (dt < 1e-4f) continue;
            tPrev = now;

            // 支撑脚 = 两脚中较低的那个
            bool leftLower = lf.position.y <= rf.position.y;
            Vector3 p = leftLower ? lf.position : rf.position;
            Vector3 prev = leftLower ? lp : rp;
            Vector3 d = p - prev; d.y = 0f;
            ys.Add(p.y);
            vs.Add(d.magnitude / dt);
            dts.Add(dt);
            lp = lf.position; rp = rf.position;
            bodySum += ctl.CurrentSpeed;
            motSum += animator.GetFloat("MotionSpeed");
            frames++;
            stName = stateName();
        }

        float minY = float.MaxValue;
        foreach (var y in ys) minY = Mathf.Min(minY, y);
        int n = 0; float sum = 0f, max = 0f;
        for (int i = 0; i < ys.Count; i++)
        {
            if (ys[i] > minY + 0.08f) continue;   // 摆动相，不纳入
            sum += vs[i]; max = Mathf.Max(max, vs[i]); n++;
        }
        float slip = n > 0 ? sum / n : -1f;
        float body = bodySum / Mathf.Max(1, frames);
        float mot = motSum / Mathf.Max(1, frames);
        sb.AppendLine($"[{label}] state={stName} 总帧={frames} 着地帧={n}(阈值y<{minY + 0.08f:0.##}) "
                      + $"滑步均值={slip:0.##}m/s 峰值={max:0.##} 身体速度={body:0.##} 动画倍率={mot:0.##} "
                      + $"理论脚步速度={mot * (runHeld ? 4.53f : 1.62f):0.##}m/s");
        ctl.SetInjectedMove(Vector2.zero, false);
        ctl.EndInputOverride();
        yield return new WaitForSecondsRealtime(0.3f);
    }

    yield return MeasureSlip("走路", false, 1.0f);
    yield return MeasureSlip("跑步", true, 1.0f);

    // ---------- 三连击 ----------
    ctl.BeginInputOverride();
    float t0 = Time.realtimeSinceStartup;
    var seq = new StringBuilder();
    string last = "";
    System.Action mark = () =>
    {
        string s = stateName() + "/c" + ctl.ComboStep;
        if (s != last) { seq.Append((Time.realtimeSinceStartup - t0).ToString("0.00")).Append(" ").Append(s).Append(" | "); last = s; }
    };

    ctl.RequestInjectedAttack();
    float end1 = Time.realtimeSinceStartup + 0.6f;
    while (Time.realtimeSinceStartup < end1) { yield return null; mark(); }

    // 等 Atk1Rec 窗口
    float dl = Time.realtimeSinceStartup + 2.0f;
    while (Time.realtimeSinceStartup < dl)
    {
        yield return null; mark();
        if (stateName() == "Atk1Rec" && animator.GetCurrentAnimatorStateInfo(0).normalizedTime % 1f >= 0.35f) break;
    }
    ctl.RequestInjectedAttack();
    end1 = Time.realtimeSinceStartup + 0.6f;
    while (Time.realtimeSinceStartup < end1) { yield return null; mark(); }

    dl = Time.realtimeSinceStartup + 2.0f;
    while (Time.realtimeSinceStartup < dl)
    {
        yield return null; mark();
        if (stateName() == "Atk2Rec" && animator.GetCurrentAnimatorStateInfo(0).normalizedTime % 1f >= 0.35f) break;
    }
    ctl.RequestInjectedAttack();
    end1 = Time.realtimeSinceStartup + 1.8f;
    while (Time.realtimeSinceStartup < end1) { yield return null; mark(); }

    bool sawAtk2 = seq.ToString().Contains("Atk2/") || seq.ToString().Contains("Atk2Rec/");
    bool sawAtk3 = seq.ToString().Contains("Atk3/");
    sb.AppendLine("[三连击] " + seq);
    sb.AppendLine("[三连击] 见到第二段=" + sawAtk2 + " 见到第三段=" + sawAtk3 + " → " + (sawAtk2 && sawAtk3 ? "PASS" : "FAIL"));
    ctl.EndInputOverride();

    // ---------- 跑步中死亡 ----------
    ctl.BeginInputOverride();
    ctl.SetInjectedMove(new Vector2(0f, 1f), true);
    yield return new WaitForSecondsRealtime(0.8f);
    string beforeDeath = stateName();
    if (ph != null)
    {
        ph.ClearInvincibility();
        ph.TakeDamage(DamageInfo.Simple(99999f, Faction.Enemy));
    }
    yield return new WaitForSecondsRealtime(0.8f);
    string afterDeath = stateName();
    sb.AppendLine($"[死亡] 死前状态={beforeDeath} → 死后状态={afterDeath} → "
                  + (afterDeath == "Death" ? "PASS（跑步中也能播死亡动画）" : "FAIL"));
    ctl.SetInjectedMove(Vector2.zero, false);
    ctl.EndInputOverride();

    Debug.Log("[v45d]\n" + sb.ToString());
    yield break;
}

return Body();
