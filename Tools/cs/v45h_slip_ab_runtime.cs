// v45h: 实机 A/B —— 走/跑参考速度候选消融，取滑步最小者
// 判据：支撑相（脚踝 y 低于该脚全程最低 +5%）内，较低那只脚的世界水平速度均值。
//       绝对值受采样精度影响，但**组间相对比较**有效。
using System.Collections;
using System.Text;
using UnityEngine;
using InkWash.Player;

IEnumerator Body()
{
    var sb = new StringBuilder();
    yield return new WaitForSecondsRealtime(1.5f);

    var run = Object.FindObjectOfType<InkWash.Roguelike.RunManager>();
    var sw = System.Diagnostics.Stopwatch.StartNew();
    while (run != null && run.State != InkWash.Roguelike.RunState.Playing && sw.Elapsed.TotalSeconds < 25.0)
        yield return null;

    var ctl = Object.FindObjectOfType<PlayerController>();
    if (ctl == null) { Debug.Log("[v45h] FAIL no ctl"); yield break; }
    var ph = Object.FindObjectOfType<InkWash.Combat.PlayerHealth>();
    var animator = ph != null ? ph.GetComponentInChildren<Animator>() : ctl.GetComponentInChildren<Animator>();
    var lf = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
    var rf = animator.GetBoneTransform(HumanBodyBones.RightFoot);
    if (ph != null) { ph.maxHealth = 1e6f; ph.ResetHealth(); }

    System.Func<string> stateName = () =>
    {
        var st = animator.GetCurrentAnimatorStateInfo(0);
        string[] ns = { "Idle", "Walk", "Run", "Dash", "Atk1Rec", "Atk2Rec", "Atk1", "Atk2", "Atk3", "Hit", "Cast", "Death" };
        foreach (var n in ns) if (st.IsName(n)) return n;
        return "?";
    };

    Vector3 home = ctl.transform.position;

    IEnumerator Measure(string label, bool runHeld, float seconds)
    {
        // 撞墙会让角色速度掉到 0（第一轮跑步数据就是这样废掉的）：
        // 每组先传送回开局点，采样时只认「真的在跑」的帧。
        ctl.transform.position = home;
        ctl.BeginInputOverride();
        ctl.SetInjectedMove(new Vector2(0f, 1f), runHeld);
        yield return new WaitForSecondsRealtime(1.0f);      // 等速度与状态稳定

        var ys = new System.Collections.Generic.List<float>();
        var vs = new System.Collections.Generic.List<float>();
        Vector3 lp = lf.position, rp = rf.position;
        float tp = Time.realtimeSinceStartup;
        float end = tp + seconds;
        float bodySum = 0f, motSum = 0f; int frames = 0, moving = 0; string stn = "?";
        float minBody = runHeld ? 2.5f : 0.8f;      // 「真的在动」的门槛

        while (Time.realtimeSinceStartup < end)
        {
            yield return null;
            float now = Time.realtimeSinceStartup;
            float dt = now - tp;
            if (dt < 1e-4f) continue;
            tp = now;
            bool leftLower = lf.position.y <= rf.position.y;
            Vector3 p = leftLower ? lf.position : rf.position;
            Vector3 prev = leftLower ? lp : rp;
            Vector3 d = p - prev; d.y = 0f;
            ys.Add(p.y); vs.Add(d.magnitude / dt);
            lp = lf.position; rp = rf.position;
            if (ctl.CurrentSpeed >= minBody) { bodySum += ctl.CurrentSpeed; motSum += animator.GetFloat("MotionSpeed"); moving++; }
            frames++; stn = stateName();
        }

        // 支撑相：低于该序列最低值 + 全程起伏的 5%
        float minY = float.MaxValue, maxY = float.MinValue;
        foreach (var y in ys) { minY = Mathf.Min(minY, y); maxY = Mathf.Max(maxY, y); }
        float th = minY + (maxY - minY) * 0.05f;
        float sum = 0f; int n = 0;
        for (int i = 0; i < ys.Count; i++) if (ys[i] <= th) { sum += vs[i]; n++; }

        float slip = n > 0 ? sum / n : -1f;
        float body = moving > 0 ? bodySum / moving : 0f;
        float mot = moving > 0 ? motSum / moving : 0f;
        string warn = moving < frames * 0.5 ? "  ⚠有效帧不足(可能撞墙)" : "";
        sb.AppendLine($"[{label}] state={stn} 帧={frames} 有效={moving} 支撑帧={n} 滑步={slip:0.##}m/s "
                      + $"身体={body:0.##} 倍率={mot:0.##} 滑步/身速={(body > 0.01f ? slip / body : -1f):0.##}{warn}");

        ctl.SetInjectedMove(Vector2.zero, false);
        ctl.EndInputOverride();
        yield return new WaitForSecondsRealtime(0.4f);
    }

    // ---- 走路（Feng_Walk_Loop）----
    ctl.walkRefSpeed = 0.91f;
    yield return Measure("走路 ref=0.91", false, 1.2f);
    ctl.walkRefSpeed = 0.75f;
    yield return Measure("走路 ref=0.75", false, 1.2f);
    ctl.walkRefSpeed = 0.91f;

    // ---- 跑步（Mixamo run）三档消融 ----
    foreach (var r in new float[] { 3.92f, 4.12f, 4.53f })
    {
        ctl.runRefSpeed = r;
        yield return Measure("跑步 ref=" + r.ToString("0.##"), true, 1.2f);
    }
    ctl.runRefSpeed = 3.92f;

    Debug.Log("[v45h]\n" + sb.ToString());
    yield break;
}

return Body();
