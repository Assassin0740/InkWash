// 两个未通过项的取数诊断：
//   A) 姿态体检里 Run 报「浮空 0.094」，但我用 s3_calib_footik 量到 Run 的无 IK 最低只有 0.0723。
//      差异怀疑来自采样密度：姿态体检 nt 只取 21 个点（步长 0.05），
//      跑步的落地相很短，可能整段被跳过。这里用 121 个点细扫一遍看清楚。
//   B) 三段连击没推到第 3 段。打点记录每次状态变化 + 段位 + 取消窗口，看是触发器没落地还是被拽回去了。
using System.Collections;
using System.Collections.Generic;

string OUT = "Tools/reports/S3_diag.txt";

IEnumerator Body()
{
    var sb = new System.Text.StringBuilder();
    string projRoot = System.IO.Path.GetDirectoryName(Application.dataPath);
    System.Action flush = () => System.IO.File.WriteAllText(System.IO.Path.Combine(projRoot, OUT), sb.ToString());

    var ctl = Object.FindObjectOfType<InkWash.Player.PlayerController>();
    if (ctl == null) { sb.AppendLine("[ERR] no PlayerController"); flush(); yield break; }
    var root = ctl.transform;
    var anim = ctl.animator;
    var ik = root.GetComponent<InkWash.Player.FootIK>();
    var smrs = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
    var bake = new Mesh(); bake.MarkDynamic();

    System.Func<float> lowest = () =>
    {
        float lo = float.MaxValue;
        foreach (var s in smrs)
        {
            if (s == null || !s.enabled) continue;
            s.BakeMesh(bake);
            var verts = bake.vertices;
            var m = s.transform.localToWorldMatrix;
            for (int i = 0; i < verts.Length; i++) lo = Mathf.Min(lo, m.MultiplyPoint3x4(verts[i]).y);
        }
        return lo;
    };

    System.Func<Vector3, Transform, float> groundUnder = (p, self) =>
    {
        var hits = Physics.RaycastAll(p + Vector3.up * 0.6f, Vector3.down, 2.0f, ~0, QueryTriggerInteraction.Ignore);
        float best = float.MinValue;
        foreach (var h in hits)
        {
            if (h.collider.transform == self || h.collider.transform.IsChildOf(self)) continue;
            if (h.point.y > best) best = h.point.y;
        }
        return best;
    };

    // ---------------- A. Run 细扫 ----------------
    sb.AppendLine("========== A. Run 落地相细扫（121 点）==========");
    ctl.enabled = false;
    bool ikWasEnabled = ik != null && ik.enabled;
    float savedGc = ik != null ? ik.groundClearance : 0f;

    string[] states = { "Idle", "Run", "Walk" };
    foreach (var nm in states)
    {
        // 无 IK：把 FootIK 整个关掉，并且把 bodyPosition 归零，确保量的是动画本体
        if (ik != null) ik.enabled = false;
        anim.Play(nm, 0, 0f);
        anim.Update(1f / 60f);
        Vector3 bp0 = anim.bodyPosition;
        sb.AppendLine();
        sb.AppendLine("[" + nm + "] 无IK  bodyPosition=" + bp0.ToString("F4"));
        float gmin = float.MaxValue; float gminNt = -1f;
        var profile = new List<string>();
        for (int i = 0; i <= 120; i++)
        {
            float nt = i / 120f;
            anim.Play(nm, 0, nt);
            anim.Update(1f / 60f);
            float g = groundUnder(root.position, root);
            float lo = (g > float.MinValue ? lowest() - g : float.NaN);
            if (!float.IsNaN(lo) && lo < gmin) { gmin = lo; gminNt = nt; }
            if (i % 10 == 0) profile.Add(string.Format("{0:F2}:{1:F3}", nt, lo));
            yield return null;
        }
        sb.AppendLine("  细扫最低 " + gmin.ToString("F4") + " 出现在 nt=" + gminNt.ToString("F3"));
        sb.AppendLine("  每 0.1nt 剖面: " + string.Join("  ", profile.ToArray()));

        // 有 IK：走真实链路（enableIk=true + liftSmooth 拉到瞬时）
        if (ik != null) { ik.enabled = true; ik.enableIk = true; ik.liftSmooth = 1e6f; }
        anim.Play(nm, 0, 0f); anim.Update(1f / 60f);
        float imin = float.MaxValue; float iminNt = -1f;
        for (int i = 0; i <= 120; i++)
        {
            float nt = i / 120f;
            anim.Play(nm, 0, nt);
            anim.Update(1f / 60f);
            float g = groundUnder(root.position, root);
            float lo = (g > float.MinValue ? lowest() - g : float.NaN);
            if (!float.IsNaN(lo) && lo < imin) { imin = lo; iminNt = nt; }
            yield return null;
        }
        sb.AppendLine("  贴地后最低 " + imin.ToString("F4") + " 出现在 nt=" + iminNt.ToString("F3")
            + "   (FootIK bodyPosition=" + anim.bodyPosition.ToString("F4") + ")");

        // 还原
        if (ik != null) { ik.enableIk = true; ik.liftSmooth = savedGc > 0 ? 14f : 14f; }
        anim.Play(nm, 0, 0f); anim.Update(1f / 60f);
    }

    if (ik != null) { ik.enabled = ikWasEnabled; ik.enableIk = true; ik.groundClearance = savedGc; ik.liftSmooth = 14f; }
    ctl.enabled = true;
    ctl.ResetToLocomotion();
    yield return null;

    // ---------------- B. 三段连击打点 ----------------
    sb.AppendLine();
    sb.AppendLine("========== B. 三段连击打点（每 0.1s 补按一次攻击）==========");
    yield return null;
    ctl.BeginInputOverride();
    ctl.SetInjectedMove(Vector2.zero, false);
    ctl.ResetToLocomotion();
    yield return null;
    yield return null;

    string lastKey = "";
    float t0 = Time.time;
    float lastMash = -9f;
    int guard = 0;
    while (Time.time - t0 < 4.5f && guard < 3000)
    {
        guard++;
        float t = Time.time - t0;
        var ci = anim.GetCurrentAnimatorClipInfo(0);
        string nm = ci.Length > 0 ? ci[0].clip.name : "(none)";
        var st = anim.GetCurrentAnimatorStateInfo(0);
        string key = nm + "|" + ctl.ComboStep + "|" + ctl.IsCancelWindowOpen + "|" + ctl.Phase;
        if (key != lastKey)
        {
            sb.AppendLine(string.Format("  t={0,6:F3}  片段={1,-34} nt={2:F3}  段位={3} 窗口={4} 阶段={5}",
                t, nm, st.normalizedTime % 1f, ctl.ComboStep,
                ctl.IsCancelWindowOpen ? "开" : "关", ctl.Phase));
            lastKey = key;
        }

        bool canPress = ctl.Phase != InkWash.Player.ActionPhase.Attack || ctl.IsCancelWindowOpen;
        if (canPress && t - lastMash > 0.10f) { lastMash = t; ctl.RequestInjectedAttack(); }
        yield return null;
    }
    sb.AppendLine("  最终段位 = " + ctl.ComboStep);
    ctl.EndInputOverride();
    Object.Destroy(bake);
    flush();
    Debug.Log("[diag] " + sb.ToString());
}
return Body();
