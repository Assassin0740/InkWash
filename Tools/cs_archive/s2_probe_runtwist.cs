// 量化「扭腰」：分别量 走路 / 跑步 两种状态下的躯干扭转。
//
// 判据（都在**角色根节点的局部空间**里量，所以角色自身朝向不影响读数）：
//   · 髋线 = 左大腿根 -> 右大腿根   （这两根骨头是 Hips 的子节点，只转不平移，
//                                    连线方向即骨盆宽度方向 ≈ 骨盆偏航）
//   · 肩线 = 左上臂根 -> 右上臂根   （同理 ≈ 胸腔偏航）
//   · 躯干扭转角 = 肩线偏航 - 髋线偏航   ← 这就是「扭腰」的量化值
//   · 根节点 yaw 波动 = 动画是否把整个角色带着转（根旋转污染）
//
// 对照意义：走路扭得少、跑步扭 60° → Sprint_Loop 这段片段的问题；
//           走路也扭 60° → 重定向/骨架层面的全局问题。
//
// 结果写 Tools/reports/S2_runtwist_latest.txt。
// 注意：桥的顶层脚本不能直接 yield（CS7020），必须先声明迭代器方法再 return 它。

string OUT = "Tools/reports/S2_runtwist_latest.txt";

System.Collections.IEnumerator Body()
{
    var sb = new System.Text.StringBuilder();

    var ctl = UnityEngine.Object.FindObjectOfType<InkWash.Player.PlayerController>();
    if (ctl == null) { System.IO.File.WriteAllText(OUT, "[ERR] no PlayerController"); yield break; }
    var anim = ctl.animator;
    if (anim == null) { System.IO.File.WriteAllText(OUT, "[ERR] ctl.animator null"); yield break; }
    if (!anim.isHuman) { System.IO.File.WriteAllText(OUT, "[ERR] not Humanoid"); yield break; }

    var root = ctl.transform;
    var lHip = anim.GetBoneTransform(UnityEngine.HumanBodyBones.LeftUpperLeg);
    var rHip = anim.GetBoneTransform(UnityEngine.HumanBodyBones.RightUpperLeg);
    var lSh = anim.GetBoneTransform(UnityEngine.HumanBodyBones.LeftUpperArm);
    var rSh = anim.GetBoneTransform(UnityEngine.HumanBodyBones.RightUpperArm);
    if (lHip == null || rHip == null || lSh == null || rSh == null)
    { System.IO.File.WriteAllText(OUT, "[ERR] bones missing"); yield break; }

    System.Func<UnityEngine.Vector3, UnityEngine.Vector3, float> localYaw =
        (a, b) =>
        {
            UnityEngine.Vector3 d = root.InverseTransformDirection(b - a);
            return UnityEngine.Mathf.Atan2(d.x, d.z) * UnityEngine.Mathf.Rad2Deg;
        };

    ctl.BeginInputOverride();

    // ---------- 走路 ----------
    ctl.SetInjectedMove(new UnityEngine.Vector2(0f, 1f), false);
    float w = UnityEngine.Time.time + 1.8f;
    while (UnityEngine.Time.time < w) yield return null;

    float t1 = UnityEngine.Time.time;
    float hMin = 9999f, hMax = -9999f, sMin = 9999f, sMax = -9999f, twMin = 9999f, twMax = -9999f;
    float rMin = 9999f, rMax = -9999f;
    int n = 0;
    while (UnityEngine.Time.time - t1 < 2.4f)
    {
        float hy = localYaw(lHip.position, rHip.position);
        float sy = localYaw(lSh.position, rSh.position);
        float tw = UnityEngine.Mathf.DeltaAngle(hy, sy);
        hMin = UnityEngine.Mathf.Min(hMin, hy); hMax = UnityEngine.Mathf.Max(hMax, hy);
        sMin = UnityEngine.Mathf.Min(sMin, sy); sMax = UnityEngine.Mathf.Max(sMax, sy);
        twMin = UnityEngine.Mathf.Min(twMin, tw); twMax = UnityEngine.Mathf.Max(twMax, tw);
        float ry = root.eulerAngles.y;
        rMin = UnityEngine.Mathf.Min(rMin, ry); rMax = UnityEngine.Mathf.Max(rMax, ry);
        n++;
        yield return null;
    }
    var wsi = anim.GetCurrentAnimatorStateInfo(0);
    sb.AppendLine(string.Format("[Walk] state={0} frames={1}",
        wsi.IsName("Walk") ? "Walk" : (wsi.IsName("Run") ? "Run" : "Other"), n));
    sb.AppendLine(string.Format("  hip   yaw p-p = {0,6:F1} deg", hMax - hMin));
    sb.AppendLine(string.Format("  shldr yaw p-p = {0,6:F1} deg", sMax - sMin));
    sb.AppendLine(string.Format("  TWIST p-p     = {0,6:F1} deg", twMax - twMin));
    sb.AppendLine(string.Format("  root wobble   = {0,6:F3} deg", rMax - rMin));

    // ---------- 跑步 ----------
    ctl.SetInjectedMove(new UnityEngine.Vector2(0f, 1f), true);
    float w2 = UnityEngine.Time.time + 2.2f;
    while (UnityEngine.Time.time < w2) yield return null;

    t1 = UnityEngine.Time.time;
    hMin = 9999f; hMax = -9999f; sMin = 9999f; sMax = -9999f; twMin = 9999f; twMax = -9999f;
    rMin = 9999f; rMax = -9999f;
    n = 0;
    while (UnityEngine.Time.time - t1 < 2.4f)
    {
        float hy = localYaw(lHip.position, rHip.position);
        float sy = localYaw(lSh.position, rSh.position);
        float tw = UnityEngine.Mathf.DeltaAngle(hy, sy);
        hMin = UnityEngine.Mathf.Min(hMin, hy); hMax = UnityEngine.Mathf.Max(hMax, hy);
        sMin = UnityEngine.Mathf.Min(sMin, sy); sMax = UnityEngine.Mathf.Max(sMax, sy);
        twMin = UnityEngine.Mathf.Min(twMin, tw); twMax = UnityEngine.Mathf.Max(twMax, tw);
        float ry = root.eulerAngles.y;
        rMin = UnityEngine.Mathf.Min(rMin, ry); rMax = UnityEngine.Mathf.Max(rMax, ry);
        n++;
        yield return null;
    }
    var rsi = anim.GetCurrentAnimatorStateInfo(0);
    sb.AppendLine(string.Format("[Run] state={0} frames={1}",
        rsi.IsName("Run") ? "Run" : (rsi.IsName("Walk") ? "Walk" : "Other"), n));
    sb.AppendLine(string.Format("  hip   yaw p-p = {0,6:F1} deg", hMax - hMin));
    sb.AppendLine(string.Format("  shldr yaw p-p = {0,6:F1} deg", sMax - sMin));
    sb.AppendLine(string.Format("  TWIST p-p     = {0,6:F1} deg", twMax - twMin));
    sb.AppendLine(string.Format("  root wobble   = {0,6:F3} deg", rMax - rMin));

    ctl.SetInjectedMove(UnityEngine.Vector2.zero, false);
    ctl.EndInputOverride();

    System.IO.File.WriteAllText(OUT, sb.ToString());
    UnityEngine.Debug.Log("[runtwist] written: " + OUT);
}

return Body();
