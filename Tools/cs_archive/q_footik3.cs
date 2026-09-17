// q_footik3.cs —— Walk 脚位逐点诊断：区分「整体被抬高(root motion)」还是「抬腿相(正常)」
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

IEnumerator Body()
{
    var sb = new System.Text.StringBuilder();
    string projRoot = System.IO.Path.GetDirectoryName(Application.dataPath);

    var ctl = Object.FindObjectOfType<InkWash.Player.PlayerController>();
    if (ctl == null) { Debug.LogError("[q_footik3] no ctl"); yield break; }
    var root = ctl.transform;
    var anim = ctl.animator;
    var vis = root.Find("Visual");
    var ik = root.GetComponent<InkWash.Player.FootIK>();
    var cc = root.GetComponent<CharacterController>();

    yield return null; yield return null;

    var smrs = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
    var bake = new Mesh(); bake.MarkDynamic();
    var vb = new List<Vector3>(32768);

    System.Func<string, Transform> find = null;
    find = (n) =>
    {
        var st = new Stack<Transform>();
        st.Push(root);
        while (st.Count > 0)
        {
            var t = st.Pop();
            if (t.name == n) return t;
            foreach (Transform c in t) st.Push(c);
        }
        return null;
    };
    var lf = find("CC_Base_L_Foot");
    var rf = find("CC_Base_R_Foot");
    var hips = find("CC_Base_Hip");

    if (cc != null) cc.enabled = false;
    if (ik != null) ik.enabled = false;
    ctl.enabled = false;
    float savedVisY = vis != null ? vis.localPosition.y : 0f;
    if (vis != null) vis.localPosition = Vector3.zero;

    yield return null; yield return null;

    float groundY = root.position.y;
    {
        var hits = Physics.RaycastAll(root.position + Vector3.up * 3f, Vector3.down, 10f, ~0, QueryTriggerInteraction.Ignore);
        float best = float.MinValue;
        foreach (var h in hits)
        {
            if (h.collider != null && h.collider.transform.IsChildOf(root)) continue;
            if (h.point.y > best) best = h.point.y;
        }
        if (best > float.MinValue) groundY = best;
    }

    System.Func<float> low = () =>
    {
        float lo = float.MaxValue;
        foreach (var s in smrs)
        {
            s.BakeMesh(bake);
            bake.GetVertices(vb);
            var m = s.transform.localToWorldMatrix;
            for (int i = 0; i < vb.Count; i++)
            {
                var v = vb[i];
                float y = m.m10 * v.x + m.m11 * v.y + m.m12 * v.z + m.m13;
                if (y < lo) lo = y;
            }
        }
        return lo - groundY;
    };

    sb.AppendLine("根.y=" + root.position.y.ToString("F4") + "   地面.y=" + groundY.ToString("F4")
        + "   Visual.localY 已归零（原 " + savedVisY.ToString("F4") + "）");
    sb.AppendLine("脚踝 L=" + (lf != null ? lf.name : "<null>") + "  R=" + (rf != null ? rf.name : "<null>"));
    sb.AppendLine("Visual.localScale = " + (vis != null ? vis.localScale.ToString("F4") : "?"));
    sb.AppendLine();
    sb.AppendLine("Walk（KI Walk01_Forward, 0.800s）逐采样点，单位米（相对地面）：");
    sb.AppendLine("    nt      网格最低点     左踝Y      右踝Y      髋Y");

    const int steps = 24;
    float minLow = 9999f, maxLow = -9999f;
    for (int i = 0; i <= steps; i++)
    {
        anim.Play("Walk", 0, i / (float)steps);
        anim.Update(1f / 60f);
        float l = low();
        if (l < minLow) minLow = l;
        if (l > maxLow) maxLow = l;
        float ly = lf != null ? lf.position.y - groundY : float.NaN;
        float ry = rf != null ? rf.position.y - groundY : float.NaN;
        float hy = hips != null ? hips.position.y - groundY : float.NaN;
        sb.AppendLine(string.Format("  {0:F3}   {1,11:F4}  {2,9:F4}  {3,9:F4}  {4,9:F4}",
            i / (float)steps, l, ly, ry, hy));
        yield return null;
    }
    sb.AppendLine();
    sb.AppendLine("→ 网格最低点 区间 [" + minLow.ToString("F4") + ", " + maxLow.ToString("F4") + "]  跨度 "
        + (maxLow - minLow).ToString("F4") + " m");
    sb.AppendLine("  判读：走路应该「总有一只脚贴地」→ 最低点应长期贴近 0；");
    sb.AppendLine("        若最低点整段抬到 +0.2 以上 → 是 root motion 把整体抬起来了（不是抬腿相）。");

    // ---- 同样量一下 Idle 做对照 ----
    sb.AppendLine();
    sb.AppendLine("Idle（Feng_Idle_Loop）对照，只打区间：");
    float iLo = 9999f, iHi = -9999f;
    for (int i = 0; i <= steps; i++)
    {
        anim.Play("Idle", 0, i / (float)steps);
        anim.Update(1f / 60f);
        float l = low();
        if (l < iLo) iLo = l;
        if (l > iHi) iHi = l;
        yield return null;
    }
    sb.AppendLine("  区间 [" + iLo.ToString("F4") + ", " + iHi.ToString("F4") + "]");

    Object.Destroy(bake);
    ctl.enabled = true;
    if (cc != null) cc.enabled = true;
    if (ik != null) ik.enabled = true;
    System.IO.File.WriteAllText(System.IO.Path.Combine(projRoot, "Tools/reports/q_footik3.txt"), sb.ToString());
    Debug.Log("[q_footik3] done");
    yield return null;
}
return Body();
