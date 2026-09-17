// 为 Char_Feng 重标定 FootIK 的 stateOffsets。
//
// 与 Tools/cs/s3_calib_footik.cs 同一套方法，只有一处参数不同：
//   VisualY 由 0.088 改为 0 —— Feng 的模型原点就在脚底（KayKit 的原点在脚底上方 0.122m）。
//
// 原理（读 FootIK.cs 得到）：
//   BodyBase = stateOffsets[state].y          —— 每状态的固定下压/上抬
//   need     = (地面 + clearance) - 网格最低点
//   BodyLift = Lerp(BodyLift, Clamp(need, 0, maxLift), k)   —— 只会上抬，不会下压
//   最终      bodyPosition.y += BodyBase + BodyLift
// 取值惯例：offset ≈ -(该状态在无 IK 下网格最低点高于地面的距离) + 3mm 余量。
//
// ⚠ 采样必须按**归一化时间**扫 120 点，不能按帧数（高帧率下会漏掉落地相）。
using System.Collections;

string OUT_TXT = "Tools/reports/F_footik.txt";

// Feng 模型原点就在脚底 → 静态竖直抵消为 0
const float VisualY = 0f;

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
    sb.AppendLine("Visual.localScale = " + vis.localScale.ToString("F4") + "  （应为 2.2130）");
    sb.AppendLine("蒙皮网格 " + root.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length + " 个");

    // 把模型放回自然位置
    vis.localPosition = new Vector3(vis.localPosition.x, VisualY, vis.localPosition.z);
    if (ik != null) ik.enabled = false;
    ctl.enabled = false;

    // 先等 CC 把角色顶到台面上
    yield return null;
    yield return null;

    float startY = root.position.y;
    float groundY = startY;
    {
        var hits = Physics.RaycastAll(root.position + Vector3.up * 3f, Vector3.down, 10f, ~0, QueryTriggerInteraction.Ignore);
        float best = float.MinValue;
        foreach (var h in hits) { if (h.collider.transform.IsChildOf(root)) continue; if (h.point.y > best) best = h.point.y; }
        if (best > float.MinValue) groundY = best;
    }
    sb.AppendLine("root.y = " + startY.ToString("F4") + "   地面 Y = " + groundY.ToString("F4"));
    sb.AppendLine("Visual.localPosition.y 设为 " + VisualY.ToString("F4"));
    sb.AppendLine();

    var smrs = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
    var bake = new Mesh(); bake.MarkDynamic();
    var vb = new System.Collections.Generic.List<Vector3>(32768);

    System.Func<float> lowestRelGround = () =>
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

    string[] states = { "Idle", "Walk", "Run", "Dash", "Atk1", "Atk1Rec", "Atk2", "Atk2Rec", "Atk3" };
    sb.AppendLine("状态      采样最低(相对地面)   建议 stateOffsets.y");
    sb.AppendLine("----------------------------------------------------");
    foreach (var name in states)
    {
        anim.Play(name, 0, 0f);
        yield return null;
        float lo = 9999f;
        const int steps = 120;
        for (int i = 0; i <= steps; i++)
        {
            anim.Play(name, 0, i / (float)steps);
            anim.Update(1f / 60f);
            float v = lowestRelGround();
            if (v < lo) lo = v;
            yield return null;
        }
        sb.AppendLine(string.Format("{0,-9} {1,10:F4}            {2,10:F4}", name, lo, -(lo) + 0.003f));
    }

    Object.Destroy(bake);
    ctl.enabled = true;
    if (ik != null) ik.enabled = true;
    flush();
    Debug.Log("[footik-feng] " + sb.ToString());
}
return Body();
