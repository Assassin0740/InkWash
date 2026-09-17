// 把 FootIK 的反馈环逐帧打出来：无 IK 最低 / BodyBase / BodyLift / FootIK 自测最低 / 修正后最低。
// 目的：解释 Walk 实测 +0.134 与"BodyBase 预测 +0.003"之间的差从哪来。
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

IEnumerator Body()
{
    var sb = new System.Text.StringBuilder();
    string projRoot = System.IO.Path.GetDirectoryName(Application.dataPath);
    System.Action flush = () => System.IO.File.WriteAllText(System.IO.Path.Combine(projRoot, "Tools/reports/f_probe_loop.txt"), sb.ToString());

    var go = GameObject.Find("Player");
    if (go == null) { sb.AppendLine("[ERR] no Player"); flush(); yield break; }
    var ik = go.GetComponent<InkWash.Player.FootIK>();
    var anim = go.GetComponent<Animator>();
    var ctl = go.GetComponent<InkWash.Player.PlayerController>();
    if (ctl != null) ctl.enabled = false;

    float groundY = go.transform.position.y;
    {
        var hits = Physics.RaycastAll(go.transform.position + Vector3.up * 3f, Vector3.down, 10f, ~0, QueryTriggerInteraction.Ignore);
        float best = float.MinValue;
        foreach (var h in hits) { if (h.collider.transform.IsChildOf(go.transform)) continue; if (h.point.y > best) best = h.point.y; }
        if (best > float.MinValue) groundY = best;
    }
    sb.AppendLine("groundY = " + groundY.ToString("F4") + "   root.y = " + go.transform.position.y.ToString("F4"));
    sb.AppendLine("FootIK.enabled=" + (ik != null && ik.enabled) + " enableIk=" + (ik != null && ik.enableIk)
        + " OffsetTarget=" + (ik != null && ik.OffsetTarget != null ? ik.OffsetTarget.name : "NULL")
        + " 当前Applied=" + (ik != null ? ik.AppliedBodyOffset.ToString("F4") : "-"));
    sb.AppendLine();

    var smrs = go.GetComponentsInChildren<SkinnedMeshRenderer>(true);
    var bake = new Mesh(); bake.MarkDynamic();
    var vb = new List<Vector3>(32768);
    System.Func<float> lowest = () =>
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
        return lo;
    };

    sb.AppendLine("=== 关闭 FootIK（无 IK 基线）===");
    if (ik != null) ik.enabled = false;
    foreach (var st in new string[] { "Walk", "Run" })
    {
        float mn = 9999f;
        const int steps = 20;
        for (int i = 0; i <= steps; i++)
        {
            anim.Play(st, 0, i / (float)steps);
            anim.Update(1f / 60f);
            mn = Mathf.Min(mn, lowest() - groundY);
        }
        sb.AppendLine(string.Format("  {0,-6} 无IK最低 = {1:F4}", st, mn));
    }

    sb.AppendLine();
    sb.AppendLine("=== 打开 FootIK，逐点打印反馈环 ===");
    if (ik != null) ik.enabled = true;
    foreach (var st in new string[] { "Walk", "Run" })
    {
        sb.AppendLine("  --- " + st + " ---");
        sb.AppendLine("   nt   state     BodyBase BodyLift  ik.LowestY  RawPen   post(rel)  bp.y     ikOn");
        const int steps = 20;
        for (int i = 0; i <= steps; i++)
        {
            float nt = i / (float)steps;
            anim.Play(st, 0, nt);
            anim.Update(1f / 60f);
            float post = lowest() - groundY;
            sb.AppendLine(string.Format("  {0:F2}  {1,-8} {2,8:F4} {3,8:F4} {4,11:F4} {5,7:F4} {6,10:F4} {7,7:F4}  {8}",
                nt,
                ik != null ? ik.CurrentState : "-",
                ik != null ? ik.BodyBase : 0f,
                ik != null ? ik.BodyLift : 0f,
                ik != null ? ik.LowestMeshY - groundY : 0f,
                ik != null ? ik.RawPenetration : 0f,
                post,
                anim.bodyPosition.y,
                ik != null ? ik.HasGroundInfo.ToString() : "-"));
        }
    }

    Object.Destroy(bake);
    if (ctl != null) ctl.enabled = true;
    flush();
    Debug.Log("[probe-loop] 见 Tools/reports/f_probe_loop.txt");
    yield return null;
}
return Body();
