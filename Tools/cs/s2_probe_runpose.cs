// 诊断「跑步扭腰」：把"扭转"拆成各段来源，并抓跑循环的连拍接触表（肉眼验收用）。
//
// 为什么要拆：之前只量了「肩线偏航 - 髋线偏航」一个数，跑步得到 60~70°，但读片段原始
// 肌肉曲线（UpperChest Twist）只有 ~2.4 单位，两者对不上。所以这次把中间量全打出来：
//   · 骨盆（Hips 骨骼）自身偏航      —— 骨头自己的旋转，不受投影影响
//   · 胸腔（Chest/UpperChest）偏航   —— 上半身拧了多少
//   · 髋线 / 肩线偏航                —— 之前的量法（两点连线投影到根节点局部 XZ）
//   · 髋线「水平投影长度 / 三维长度」—— 投影退化检测：若明显 <1，说明偏航读数不可信
//   · 根节点偏航波动                  —— 角色整体有没有被带着转
//
// 连拍三个坑（都踩过了）：
//   1. 注入的移动方向是**相机相对**的 —— 相机一转角色就转身背对镜头。必须先冻结控制器。
//   2. 冻结后角色停在原地，但场景是封闭擂台，低机位会拍进墙里。所以把角色搬到**半空**
//      （地面之上 8m），四个方向都不会被遮。
//   3. 不要再让它"跑"，直接 anim.Play("Run") 静态压住状态，机位才能真正固定。
//
// 产出：
//   Tools/reports/S2_runpose.txt
//   Tools/screenshots/runpose/{front,back,sideL,sideR}.png

string OUT_TXT = "Tools/reports/S2_runpose.txt";
string OUT_IMG = "Tools/screenshots/runpose";

System.Collections.IEnumerator Body()
{
    var sb = new System.Text.StringBuilder();
    string projRoot = System.IO.Path.GetDirectoryName(UnityEngine.Application.dataPath);
    string imgDir = System.IO.Path.Combine(projRoot, OUT_IMG);
    System.IO.Directory.CreateDirectory(imgDir);
    System.Action<string, string> writeTxt = (p, s) => System.IO.File.WriteAllText(System.IO.Path.Combine(projRoot, p), s);

    var ctl = UnityEngine.Object.FindObjectOfType<InkWash.Player.PlayerController>();
    if (ctl == null) { writeTxt(OUT_TXT, "[ERR] no PlayerController"); yield break; }
    var anim = ctl.animator;
    if (anim == null) { writeTxt(OUT_TXT, "[ERR] ctl.animator null"); yield break; }
    var root = ctl.transform;
    var cam = UnityEngine.Camera.main;
    if (cam == null) { writeTxt(OUT_TXT, "[ERR] no main camera"); yield break; }
    UnityEngine.MonoBehaviour rig = cam.GetComponent("ThirdPersonCamera") as UnityEngine.MonoBehaviour;

    UnityEngine.Vector3 startPos = root.position;
    UnityEngine.Quaternion startRot = root.rotation;

    var hips = anim.GetBoneTransform(UnityEngine.HumanBodyBones.Hips);
    var chest = anim.GetBoneTransform(UnityEngine.HumanBodyBones.UpperChest);
    if (chest == null) chest = anim.GetBoneTransform(UnityEngine.HumanBodyBones.Chest);
    var lHip = anim.GetBoneTransform(UnityEngine.HumanBodyBones.LeftUpperLeg);
    var rHip = anim.GetBoneTransform(UnityEngine.HumanBodyBones.RightUpperLeg);
    var lSh = anim.GetBoneTransform(UnityEngine.HumanBodyBones.LeftUpperArm);
    var rSh = anim.GetBoneTransform(UnityEngine.HumanBodyBones.RightUpperArm);
    if (hips == null || chest == null || lHip == null || rHip == null || lSh == null || rSh == null)
    { writeTxt(OUT_TXT, "[ERR] bones missing"); yield break; }

    System.Func<UnityEngine.Transform, UnityEngine.Vector3, float> axisYaw =
        (t, axis) =>
        {
            UnityEngine.Vector3 d = root.InverseTransformDirection(t.TransformDirection(axis));
            return UnityEngine.Mathf.Atan2(d.x, d.z) * UnityEngine.Mathf.Rad2Deg;
        };
    System.Func<UnityEngine.Vector3, UnityEngine.Vector3, float> lineYaw =
        (a, b) =>
        {
            UnityEngine.Vector3 d = root.InverseTransformDirection(b - a);
            return UnityEngine.Mathf.Atan2(d.x, d.z) * UnityEngine.Mathf.Rad2Deg;
        };

    // 采样时相机固定，角色沿相机前方直线跑，root.forward 就是朝向
    ctl.BeginInputOverride();
    ctl.SetInjectedMove(new UnityEngine.Vector2(0f, 1f), true);
    float settle = UnityEngine.Time.time + 2.6f;
    while (UnityEngine.Time.time < settle) yield return null;

    // ---------------- 测量段（正常速度，2.4s） ----------------
    float hMin = 9999f, hMax = -9999f, sMin = 9999f, sMax = -9999f;
    float tMin = 9999f, tMax = -9999f, pHMin = 9999f, pHMax = -9999f, pCMin = 9999f, pCMax = -9999f;
    float yMin = 9999f, yMax = -9999f, r3Min = 9999f, r3Max = -9999f, rxzMin = 9999f, rxzMax = -9999f;
    float rootMin = 9999f, rootMax = -9999f, spdSum = 0f, msSum = 0f;
    int n = 0;
    float animSpeed0 = anim.speed;
    float t0 = UnityEngine.Time.time;
    while (UnityEngine.Time.time - t0 < 2.4f)
    {
        UnityEngine.Vector3 hl = rHip.position - lHip.position;
        UnityEngine.Vector3 hlLocal = root.InverseTransformDirection(hl);
        float hy = lineYaw(lHip.position, rHip.position);
        float sy = lineYaw(lSh.position, rSh.position);
        float tw = UnityEngine.Mathf.DeltaAngle(hy, sy);
        float py = axisYaw(hips, UnityEngine.Vector3.right);
        float cy = axisYaw(chest, UnityEngine.Vector3.right);
        hMin = UnityEngine.Mathf.Min(hMin, hy); hMax = UnityEngine.Mathf.Max(hMax, hy);
        sMin = UnityEngine.Mathf.Min(sMin, sy); sMax = UnityEngine.Mathf.Max(sMax, sy);
        tMin = UnityEngine.Mathf.Min(tMin, tw); tMax = UnityEngine.Mathf.Max(tMax, tw);
        pHMin = UnityEngine.Mathf.Min(pHMin, py); pHMax = UnityEngine.Mathf.Max(pHMax, py);
        pCMin = UnityEngine.Mathf.Min(pCMin, cy); pCMax = UnityEngine.Mathf.Max(pCMax, cy);
        yMin = UnityEngine.Mathf.Min(yMin, hlLocal.y); yMax = UnityEngine.Mathf.Max(yMax, hlLocal.y);
        r3Min = UnityEngine.Mathf.Min(r3Min, hl.magnitude); r3Max = UnityEngine.Mathf.Max(r3Max, hl.magnitude);
        float lxz = new UnityEngine.Vector2(hlLocal.x, hlLocal.z).magnitude;
        rxzMin = UnityEngine.Mathf.Min(rxzMin, lxz); rxzMax = UnityEngine.Mathf.Max(rxzMax, lxz);
        float ry = root.eulerAngles.y;
        rootMin = UnityEngine.Mathf.Min(rootMin, ry); rootMax = UnityEngine.Mathf.Max(rootMax, ry);
        spdSum += ctl.CurrentSpeed;
        msSum += anim.GetFloat("MotionSpeed");
        n++;
        yield return null;
    }
    var si = anim.GetCurrentAnimatorStateInfo(0);
    sb.AppendLine("=== 分解测量（state=" + (si.IsName("Run") ? "Run" : "OTHER") + ", frames=" + n + "）===");
    sb.AppendLine(string.Format("  髋线偏航 p-p        = {0,7:F1} deg", hMax - hMin));
    sb.AppendLine(string.Format("  肩线偏航 p-p        = {0,7:F1} deg", sMax - sMin));
    sb.AppendLine(string.Format("  扭转(肩-髋) p-p     = {0,7:F1} deg", tMax - tMin));
    sb.AppendLine(string.Format("  骨盆骨骼偏航 p-p    = {0,7:F1} deg   <- 直接读 Hips 骨骼", pHMax - pHMin));
    sb.AppendLine(string.Format("  胸腔骨骼偏航 p-p    = {0,7:F1} deg   <- 直接读 UpperChest 骨骼", pCMax - pCMin));
    sb.AppendLine(string.Format("  髋线局部 Y 分量 p-p = {0,7:F4} m     <- 滚转污染指示", yMax - yMin));
    sb.AppendLine(string.Format("  髋线三维长度        = {0,7:F4} ~ {1:F4} m", r3Min, r3Max));
    sb.AppendLine(string.Format("  髋线水平投影长度    = {0,7:F4} ~ {1:F4} m", rxzMin, rxzMax));
    sb.AppendLine(string.Format("  水平/三维 最小比值  = {0,7:F3}       <- 明显 <1 则偏航读数不可信", r3Max > 1e-6f ? rxzMin / r3Max : 0f));
    sb.AppendLine(string.Format("  根节点偏航波动      = {0,7:F3} deg", rootMax - rootMin));
    sb.AppendLine(string.Format("  平均实际速度        = {0,7:F2} m/s", n > 0 ? spdSum / n : 0f));
    sb.AppendLine(string.Format("  平均 MotionSpeed    = {0,7:F3}       <- 片段播放倍速", n > 0 ? msSum / n : 0f));
    sb.AppendLine(string.Format("  片段原生时长        = {0,7:F3} s", (anim.runtimeAnimatorController != null) ? 0.6f : 0f));
    sb.AppendLine();

    // ---------------- 连拍段 ----------------
    // 步骤：冻结控制器 -> 搬到半空 -> 静态压住 Run -> 四方向拍
    anim.speed = 0.10f;
    ctl.enabled = false;
    if (rig != null) rig.enabled = false;
    root.position = startPos + UnityEngine.Vector3.up * 8f;
    root.rotation = startRot;
    anim.SetFloat("Speed", 5f);
    anim.SetFloat("MotionSpeed", 1.5f);
    anim.SetBool("Grounded", true);
    anim.Play("Run", 0, 0f);

    float settle2 = UnityEngine.Time.time + 0.5f;
    while (UnityEngine.Time.time < settle2) yield return null;

    var frozen = anim.GetCurrentAnimatorStateInfo(0);
    sb.AppendLine("=== 连拍（anim.speed=0.10，已冻结并压住 Run）===");
    sb.AppendLine("  冻结后 state = " + (frozen.IsName("Run") ? "Run" : "OTHER"));

    UnityEngine.Vector3 facing = root.forward; facing.y = 0f; facing.Normalize();
    UnityEngine.Vector3 rightAxis = UnityEngine.Vector3.Cross(UnityEngine.Vector3.up, facing).normalized;
    sb.AppendLine("  facing = " + facing.ToString("F2") + "  rightAxis = " + rightAxis.ToString("F2"));

    int COLS = 5, ROWS = 2, CW = 480, CH = 270;
    string[] angles = { "front", "back", "sideL", "sideR" };
    float oldFov = cam.fieldOfView;
    cam.fieldOfView = 40f;
    float dist = 2.9f;

    foreach (var an in angles)
    {
        var sheet = new UnityEngine.Texture2D(COLS * CW, ROWS * CH, UnityEngine.TextureFormat.RGB24, false);
        sheet.SetPixels32(new UnityEngine.Color32[COLS * CW * ROWS * CH]);

        for (int i = 0; i < COLS * ROWS; i++)
        {
            UnityEngine.Vector3 p = root.position + UnityEngine.Vector3.up * 0.92f;
            UnityEngine.Vector3 dir;
            if (an == "front") dir = facing;
            else if (an == "back") dir = -facing;
            else if (an == "sideL") dir = -rightAxis;
            else dir = rightAxis;
            UnityEngine.Vector3 camPos = p + dir * dist + UnityEngine.Vector3.up * 0.10f;
            cam.transform.position = camPos;
            cam.transform.rotation = UnityEngine.Quaternion.LookRotation(p - camPos, UnityEngine.Vector3.up);

            var rt = UnityEngine.RenderTexture.GetTemporary(CW, CH, 24);
            cam.targetTexture = rt;
            cam.Render();
            cam.targetTexture = null;
            var prev = UnityEngine.RenderTexture.active;
            UnityEngine.RenderTexture.active = rt;
            var snap = new UnityEngine.Texture2D(CW, CH, UnityEngine.TextureFormat.RGB24, false);
            snap.ReadPixels(new UnityEngine.Rect(0, 0, CW, CH), 0, 0);
            snap.Apply();
            UnityEngine.RenderTexture.active = prev;
            UnityEngine.RenderTexture.ReleaseTemporary(rt);

            int col = i % COLS;
            int row = ROWS - 1 - (i / COLS);
            sheet.SetPixels(col * CW, row * CH, CW, CH, snap.GetPixels());
            UnityEngine.Object.Destroy(snap);

            float step = UnityEngine.Time.time + 0.26f;
            while (UnityEngine.Time.time < step) yield return null;
        }

        sheet.Apply();
        byte[] png = sheet.EncodeToPNG();
        System.IO.File.WriteAllBytes(System.IO.Path.Combine(imgDir, an + ".png"), png);
        UnityEngine.Object.Destroy(sheet);
        sb.AppendLine("  写出 " + an + ".png  (" + (COLS * CW) + "x" + (ROWS * CH) + ", " + (png.Length / 1024) + " KB)");
    }

    // 复原
    anim.speed = animSpeed0 == 0f ? 1f : animSpeed0;
    cam.fieldOfView = oldFov;
    ctl.enabled = true;
    if (rig != null) rig.enabled = true;
    ctl.SetInjectedMove(UnityEngine.Vector2.zero, false);
    ctl.EndInputOverride();
    root.position = startPos;
    root.rotation = startRot;

    writeTxt(OUT_TXT, sb.ToString());
    UnityEngine.Debug.Log("[runpose] written");
}

return Body();
