// FootIK 偏移表标定 v2：同时打印**最深/最浅**，建议值按「最浅」取。
//
// 为什么要最浅：BodyLift 只能向上补（need<0 时归零）。
//   BodyBase 取得过深（按最深处标）→ 浅的那些帧 need<0 → 抬不动 → 角色整段悬空。
//   取最浅 → 最浅那帧正好贴地，其余帧靠 BodyLift 逐帧抬。
using System.Collections;
using System.Collections.Generic;

string OUT_TXT = "Tools/reports/q_footik2.txt";

IEnumerator Body()
{
    var sb = new System.Text.StringBuilder();
    string projRoot = System.IO.Path.GetDirectoryName(Application.dataPath);
    System.Action flush = () => System.IO.File.WriteAllText(System.IO.Path.Combine(projRoot, OUT_TXT), sb.ToString());

    var ctl = Object.FindObjectOfType<InkWash.Player.PlayerController>();
    if (ctl == null) { sb.AppendLine("[ERR] no PlayerController"); flush(); yield break; }
    var root = ctl.transform;
    var anim = ctl.animator;
    var vis = root.Find("Visual");
    var ik = root.GetComponent<InkWash.Player.FootIK>();

    sb.AppendLine("Animator.avatar = " + (anim == null || anim.avatar == null ? "NULL" : anim.avatar.name));
    sb.AppendLine("Visual.localScale = " + (vis != null ? vis.localScale.ToString("F4") : "?") + "   localPosition.y = " + (vis != null ? vis.localPosition.y.ToString("F4") : "?"));

    if (ik != null) ik.enabled = false;
    ctl.enabled = false;
    yield return null;
    yield return null;

    float groundY = root.position.y;
    {
        var hits = Physics.RaycastAll(root.position + Vector3.up * 3f, Vector3.down, 10f, ~0, QueryTriggerInteraction.Ignore);
        float best = float.MinValue;
        foreach (var h in hits) { if (h.collider != null && h.collider.transform.IsChildOf(root)) continue; if (h.point.y > best) best = h.point.y; }
        if (best > float.MinValue) groundY = best;
    }
    sb.AppendLine("root.y = " + root.position.y.ToString("F4") + "   地面 Y = " + groundY.ToString("F4"));
    sb.AppendLine();

    var smrs = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
    var bake = new Mesh(); bake.MarkDynamic();
    var vb = new List<Vector3>(32768);

    System.Func<float> rel = () =>
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

    // 当前 prefab 采用值（读组件的序列化表）
    var cur = new Dictionary<string, float>();
    if (ik != null && ik.stateOffsets != null)
        foreach (var o in ik.stateOffsets) if (!string.IsNullOrEmpty(o.state)) cur[o.state] = o.y;

    string[] states = { "Idle", "Walk", "Run", "Dash", "Atk1", "Atk1Rec", "Atk2", "Atk2Rec", "Atk3" };
    const float CLEARANCE = 0.005f;
    sb.AppendLine("状态       无IK最深   无IK最浅   建议(-最浅+clearance)   现采用   差值");
    sb.AppendLine("---------------------------------------------------------------------------");

    foreach (var name in states)
    {
        anim.Play(name, 0, 0f);
        yield return null;
        float lo = 9999f, hi = -9999f;
        const int steps = 120;
        for (int i = 0; i <= steps; i++)
        {
            anim.Play(name, 0, i / (float)steps);
            anim.Update(1f / 60f);
            float v = rel();
            if (v < lo) lo = v;
            if (v > hi) hi = v;
            yield return null;
        }
        float suggest = -hi + CLEARANCE;
        float has = cur.ContainsKey(name) ? cur[name] : float.NaN;
        sb.AppendLine(string.Format("{0,-9} {1,10:F4} {2,10:F4} {3,15:F4} {4,15:F4} {5,9}",
            name, lo, hi, suggest, has, float.IsNaN(has) ? "-" : (has - suggest).ToString("+0.0000;-0.0000")));
    }

    Object.Destroy(bake);
    ctl.enabled = true;
    if (ik != null) ik.enabled = true;
    flush();
    Debug.Log("[q_footik2] done");
    yield return null;
}
return Body();
