// 为 KayKit 角色重标定 FootIK 的 stateOffsets。
//
// ⚠ 2026-09-14 修正：本脚本原先按"真实帧数"采样且硬性截断在 90 帧 —— 编辑器 200+ fps 时
//   只够覆盖半个循环，Running_A（0.80s/循环）的落地相整段被跳过，量出 0.0723（真值 0.1626），
//   照它落地会让跑步沉进地里。现已改为**按归一化时间**均匀取 120 点。
//   交叉验证：Tools/cs/s3_diag.cs A 段（121 点细扫）与本脚本应给出同一组数。
//
// 原理（读 FootIK.cs 得到）：
//   BodyBase = stateOffsets[state].y          —— 每状态的固定下压/上抬
//   need     = (地面 + clearance) - 网格最低点
//   BodyLift = Lerp(BodyLift, Clamp(need, 0, maxLift), k)   —— 只会上抬，不会下压
//   最终      bodyPosition.y += BodyBase + BodyLift
// 所以"角色浮空"只能靠 stateOffsets 的**负值**压下去；
// 取值惯例：offset ≈ -(该状态在无 IK 下网格最低点高于地面的距离) + 3mm 余量。
using System.Collections;

string OUT_TXT = "Tools/reports/S3_footik.txt";

// 先定：KayKit 模型自身的原点在脚底上方多少（把脚底压到 root 原点）
const float VisualY = 0.088f;

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

    // 把模型放回自然位置
    vis.localPosition = new Vector3(vis.localPosition.x, VisualY, vis.localPosition.z);
    if (ik != null) ik.enabled = false;
    ctl.enabled = false;

    // 停到半空，避免被地面 Raycast 影响（我们量的是相对 root 的量）
    float startY = root.position.y;
    float groundY = startY;
    {
        var hits = Physics.RaycastAll(root.position + Vector3.up * 3f, Vector3.down, 10f);
        float best = float.MinValue;
        foreach (var h in hits) { if (h.collider.transform.IsChildOf(root)) continue; if (h.point.y > best) best = h.point.y; }
        if (best > float.MinValue) groundY = best;
    }
    sb.AppendLine("root.y = " + startY.ToString("F4") + "   地面 Y = " + groundY.ToString("F4"));
    sb.AppendLine("Visual.localPosition.y 设为 " + VisualY.ToString("F4"));
    sb.AppendLine();

    var smrs = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
    var bake = new Mesh(); bake.MarkDynamic();

    System.Func<float> lowestRelGround = () =>
    {
        float lo = float.MaxValue;
        foreach (var s in smrs)
        {
            s.BakeMesh(bake);
            var verts = bake.vertices;
            var m = s.transform.localToWorldMatrix;
            foreach (var v in verts) { float y = m.MultiplyPoint3x4(v).y; if (y < lo) lo = y; }
        }
        return lo - groundY;
    };

    // 顺序与 Player.controller 的 layer0 状态一致：后摇的实测基准值单独给，
    // 不拿攻击段的偏移凑合 —— KayKit 的单手挥砍是「起手+挥砍+收招」一体的，
    // 但切出来的后摇段落在片段尾部，身体高度与中段并不相同。
    string[] states = { "Idle", "Walk", "Run", "Dash", "Atk1", "Atk1Rec", "Atk2", "Atk2Rec", "Atk3" };
    sb.AppendLine("状态      采样最低(相对地面)   建议 stateOffsets.y");
    sb.AppendLine("----------------------------------------------------");
    foreach (var name in states)
    {
        // 按归一化时间扫满整个循环（121 点），不要按帧数扫 —— 见文件头的修正说明。
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
    Debug.Log("[footik] " + sb.ToString());
}
return Body();
