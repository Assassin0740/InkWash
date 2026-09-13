// 终验：在**真实游戏参数**下抓跑步连拍，确认修完之后观感 OK。
//
// 与之前几版的区别 —— 这次要的是"玩家实际看到的样子"：
//   · 不动 Camera.main（注入方向是相机相对的），另建手动渲染相机
//   · 不断开 PlayerController，保持 MotionSpeed 是真实值（4.2/4.49 = 0.94×）
//   · 把重力临时关掉并把角色抬到半空，这样它不会撞墙停住（撞墙会让实际速度掉到 0、
//     MotionSpeed 被夹到下限 0.6，拍出来的就不是真实播放速度了）
//   · 抓拍期间藏掉非角色 Renderer，背景干净
//
// 产出：Tools/reports/S2_verify_run.txt、Tools/screenshots/verify/{back,side}.png

string OUT_TXT = "Tools/reports/S2_verify_run.txt";
string OUT_IMG = "Tools/screenshots/verify";

System.Collections.IEnumerator Body()
{
    var sb = new System.Text.StringBuilder();
    string projRoot = System.IO.Path.GetDirectoryName(UnityEngine.Application.dataPath);
    string imgDir = System.IO.Path.Combine(projRoot, OUT_IMG);
    System.IO.Directory.CreateDirectory(imgDir);
    System.Action flush = () => System.IO.File.WriteAllText(System.IO.Path.Combine(projRoot, OUT_TXT), sb.ToString());

    var ctl = UnityEngine.Object.FindObjectOfType<InkWash.Player.PlayerController>();
    if (ctl == null) { sb.AppendLine("[ERR] no PlayerController"); flush(); yield break; }
    var anim = ctl.animator;
    var root = ctl.transform;
    var mainCam = UnityEngine.Camera.main;
    if (anim == null || mainCam == null) { sb.AppendLine("[ERR] anim/mainCam null"); flush(); yield break; }

    UnityEngine.Vector3 startPos = root.position;
    float startGravity = ctl.gravity;

    var hips = anim.GetBoneTransform(UnityEngine.HumanBodyBones.Hips);
    var chest = anim.GetBoneTransform(UnityEngine.HumanBodyBones.UpperChest);
    if (chest == null) chest = anim.GetBoneTransform(UnityEngine.HumanBodyBones.Chest);
    var lHip = anim.GetBoneTransform(UnityEngine.HumanBodyBones.LeftUpperLeg);
    var rHip = anim.GetBoneTransform(UnityEngine.HumanBodyBones.RightUpperLeg);
    var lSh = anim.GetBoneTransform(UnityEngine.HumanBodyBones.LeftUpperArm);
    var rSh = anim.GetBoneTransform(UnityEngine.HumanBodyBones.RightUpperArm);

    System.Func<UnityEngine.Vector3, UnityEngine.Vector3, float> lineYaw =
        (a, b) =>
        {
            UnityEngine.Vector3 d = root.InverseTransformDirection(b - a);
            return UnityEngine.Mathf.Atan2(d.x, d.z) * UnityEngine.Mathf.Rad2Deg;
        };

    // 临时相机
    var camGo = new UnityEngine.GameObject("TmpVerifyCam");
    var tcam = camGo.AddComponent<UnityEngine.Camera>();
    tcam.CopyFrom(mainCam);
    tcam.tag = "Untagged";
    tcam.enabled = false;
    tcam.clearFlags = UnityEngine.CameraClearFlags.SolidColor;
    tcam.backgroundColor = new UnityEngine.Color(0.12f, 0.14f, 0.17f);
    tcam.fieldOfView = 40f;

    // 藏非角色 Renderer
    var keep = new System.Collections.Generic.HashSet<UnityEngine.Renderer>(root.GetComponentsInChildren<UnityEngine.Renderer>());
    var hidden = new System.Collections.Generic.List<UnityEngine.Renderer>();
    foreach (var r in UnityEngine.Object.FindObjectsOfType<UnityEngine.Renderer>())
        if (r.enabled && !keep.Contains(r)) { r.enabled = false; hidden.Add(r); }

    // 抬到半空 + 关重力，保证不会撞墙
    root.position = startPos + UnityEngine.Vector3.up * 26f;
    ctl.gravity = 0f;

    ctl.BeginInputOverride();
    ctl.SetInjectedMove(new UnityEngine.Vector2(0f, 1f), true);
    float settle = UnityEngine.Time.time + 3.0f;
    while (UnityEngine.Time.time < settle) yield return null;

    float curClipLen = 0.6f;
    // ---- 测量：真实参数下的速度 / 倍率 / 步频 / 扭转 ----
    float vSum = 0f, msSum = 0f, stepSum = 0f, hMin = 9999f, hMax = -9999f, sMin = 9999f, sMax = -9999f;
    int n = 0;
    float t0 = UnityEngine.Time.time;
    while (UnityEngine.Time.time - t0 < 2.6f)
    {
        float hy = lineYaw(lHip.position, rHip.position);
        float sy = lineYaw(lSh.position, rSh.position);
        hMin = UnityEngine.Mathf.Min(hMin, hy); hMax = UnityEngine.Mathf.Max(hMax, hy);
        sMin = UnityEngine.Mathf.Min(sMin, sy); sMax = UnityEngine.Mathf.Max(sMax, sy);
        vSum += ctl.CurrentSpeed;
        float ms = anim.GetFloat("MotionSpeed");
        msSum += ms;
        stepSum += (2f / curClipLen) * ms;
        n++;
        yield return null;
    }
    var si = anim.GetCurrentAnimatorStateInfo(0);
    sb.AppendLine("=== 终验（真实参数：runSpeed=" + ctl.runSpeed.ToString("F2")
        + " runRefSpeed=" + ctl.runRefSpeed.ToString("F2") + "）===");
    sb.AppendLine("  state        = " + (si.IsName("Run") ? "Run" : "OTHER") + "   frames=" + n);
    sb.AppendLine("  实际速度     = " + (n > 0 ? vSum / n : 0f).ToString("F2") + " m/s");
    sb.AppendLine("  MotionSpeed  = " + (n > 0 ? msSum / n : 0f).ToString("F3") + "×   （期望 ≈ runSpeed/runRefSpeed）");
    sb.AppendLine("  步频         = " + (n > 0 ? stepSum / n : 0f).ToString("F2") + " 步/秒   （真人跑步 2.6~4.4）");
    sb.AppendLine("  髋线偏航 p-p = " + (hMax - hMin).ToString("F1") + " deg");
    sb.AppendLine("  肩线偏航 p-p = " + (sMax - sMin).ToString("F1") + " deg");
    sb.AppendLine();

    // ---- 连拍 ----
    int COLS = 4, ROWS = 2, CW = 480, CH = 270;
    string[] angles = { "back", "side" };
    float dist = 3.1f;
    float animSpeedOrig = anim.speed;
    anim.speed = 0.10f;
    float s2 = UnityEngine.Time.time + 0.35f;
    while (UnityEngine.Time.time < s2) yield return null;

    foreach (var an in angles)
    {
        var sheet = new UnityEngine.Texture2D(COLS * CW, ROWS * CH, UnityEngine.TextureFormat.RGB24, false);
        sheet.SetPixels32(new UnityEngine.Color32[COLS * CW * ROWS * CH]);
        for (int i = 0; i < COLS * ROWS; i++)
        {
            UnityEngine.Vector3 facing = root.forward; facing.y = 0f;
            if (facing.sqrMagnitude < 1e-6f) facing = UnityEngine.Vector3.forward;
            facing.Normalize();
            UnityEngine.Vector3 rightAxis = UnityEngine.Vector3.Cross(UnityEngine.Vector3.up, facing).normalized;
            UnityEngine.Vector3 p = root.position + UnityEngine.Vector3.up * 0.95f;
            UnityEngine.Vector3 dir = (an == "back") ? -facing : rightAxis;
            UnityEngine.Vector3 camPos = p + dir * dist + UnityEngine.Vector3.up * 0.12f;
            tcam.transform.position = camPos;
            tcam.transform.rotation = UnityEngine.Quaternion.LookRotation(p - camPos, UnityEngine.Vector3.up);

            var rt = UnityEngine.RenderTexture.GetTemporary(CW, CH, 24);
            tcam.targetTexture = rt; tcam.Render(); tcam.targetTexture = null;
            var prev = UnityEngine.RenderTexture.active;
            UnityEngine.RenderTexture.active = rt;
            var snap = new UnityEngine.Texture2D(CW, CH, UnityEngine.TextureFormat.RGB24, false);
            snap.ReadPixels(new UnityEngine.Rect(0, 0, CW, CH), 0, 0); snap.Apply();
            UnityEngine.RenderTexture.active = prev;
            UnityEngine.RenderTexture.ReleaseTemporary(rt);

            int col = i % COLS;
            int row = ROWS - 1 - (i / COLS);
            sheet.SetPixels(col * CW, row * CH, CW, CH, snap.GetPixels());
            UnityEngine.Object.Destroy(snap);

            float step = UnityEngine.Time.time + 0.45f;
            while (UnityEngine.Time.time < step) yield return null;
        }
        sheet.Apply();
        System.IO.File.WriteAllBytes(System.IO.Path.Combine(imgDir, an + ".png"), sheet.EncodeToPNG());
        UnityEngine.Object.Destroy(sheet);
        sb.AppendLine("  写出 " + an + ".png");
    }

    // 复原
    anim.speed = animSpeedOrig <= 0f ? 1f : animSpeedOrig;
    foreach (var r in hidden) if (r != null) r.enabled = true;
    UnityEngine.Object.Destroy(camGo);
    ctl.SetInjectedMove(UnityEngine.Vector2.zero, false);
    ctl.EndInputOverride();
    ctl.gravity = startGravity;
    root.position = startPos;

    sb.AppendLine();
    sb.AppendLine("已复原：重力=" + ctl.gravity.ToString("F1") + "，Renderer " + hidden.Count + " 个已恢复");
    flush();
    UnityEngine.Debug.Log("[verify-run] written");
}

return Body();
