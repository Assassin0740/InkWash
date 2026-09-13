// 「苹果对苹果」横比跑步候选片段：老片段（Quaternius）vs 新片段（Kevin Iglesias）。
//
// 三个关键约束（前两版都在这上面翻车）：
//   1. 【不要动 Camera.main】注入的移动方向是**相机相对**的，动主相机角色就转向。
//      所以另建一台**手动渲染**的临时相机（tag 保持 Untagged，不抢 Camera.main）。
//   2. 【不要关 PlayerController，也不要 anim.Play】Play 模式下直接改 AnimatorController
//      资产（stRun.motion）之后再调 anim.Play，Animator 会直接罢工
//      （日志 "Animator does not have an AnimatorController"，实测踩过）。
//      正确做法：控制器照常跑，只把 Run 状态的 motion 换掉，动画自己就会用新片段。
//   3. 【背景要干净】角色在封闭擂台里跑，机位经常糊在墙上。
//      干脆在抓拍期间把**除角色以外**的所有 Renderer 关掉，只留角色剪影。
//
// 每个候选产出：骨盆/胸腔偏航、扭转 p-p，以及 back / side 两张 4x2 连拍。
// 产出：Tools/reports/S2_ab.txt、Tools/screenshots/ab/<n>_<clip>_<angle>.png

string OUT_TXT = "Tools/reports/S2_ab.txt";
string OUT_IMG = "Tools/screenshots/ab";

System.Collections.IEnumerator Body()
{
    var sb = new System.Text.StringBuilder();
    string projRoot = System.IO.Path.GetDirectoryName(UnityEngine.Application.dataPath);
    string imgDir = System.IO.Path.Combine(projRoot, OUT_IMG);
    System.IO.Directory.CreateDirectory(imgDir);
    System.Action<string> flush = (s) => System.IO.File.WriteAllText(System.IO.Path.Combine(projRoot, OUT_TXT), s);

    const string FbxUAL1 = "Assets/ThirdParty/Quaternius/UniversalAnimationLibrary/Unity/AnimationLibrary_Unity_Standard.fbx";
    const string FbxKI = "Assets/ThirdParty/KevinIglesias/Animations/Male/Movement/Run/HumanM@Run01_Forward.fbx";

    var ctl = UnityEngine.Object.FindObjectOfType<InkWash.Player.PlayerController>();
    if (ctl == null) { flush("[ERR] no PlayerController"); yield break; }
    var anim = ctl.animator;
    var root = ctl.transform;
    var mainCam = UnityEngine.Camera.main;
    if (anim == null || mainCam == null) { flush("[ERR] anim/mainCam null"); yield break; }

    var hips = anim.GetBoneTransform(UnityEngine.HumanBodyBones.Hips);
    var chest = anim.GetBoneTransform(UnityEngine.HumanBodyBones.UpperChest);
    if (chest == null) chest = anim.GetBoneTransform(UnityEngine.HumanBodyBones.Chest);
    var lHip = anim.GetBoneTransform(UnityEngine.HumanBodyBones.LeftUpperLeg);
    var rHip = anim.GetBoneTransform(UnityEngine.HumanBodyBones.RightUpperLeg);
    var lSh = anim.GetBoneTransform(UnityEngine.HumanBodyBones.LeftUpperArm);
    var rSh = anim.GetBoneTransform(UnityEngine.HumanBodyBones.RightUpperArm);
    if (hips == null || chest == null || lHip == null || rHip == null || lSh == null || rSh == null)
    { flush("[ERR] bones missing"); yield break; }

    var ac = anim.runtimeAnimatorController as UnityEditor.Animations.AnimatorController;
    if (ac == null) { flush("[ERR] not AnimatorController"); yield break; }
    UnityEditor.Animations.AnimatorState stRun = null;
    foreach (var s in ac.layers[0].stateMachine.states) if (s.state.name == "Run") stRun = s.state;
    if (stRun == null) { flush("[ERR] no Run state"); yield break; }

    System.Func<string, System.Collections.Generic.Dictionary<string, UnityEngine.AnimationClip>>
        load = (path) =>
        {
            var d = new System.Collections.Generic.Dictionary<string, UnityEngine.AnimationClip>();
            foreach (var o in UnityEditor.AssetDatabase.LoadAllAssetsAtPath(path))
            {
                var c = o as UnityEngine.AnimationClip;
                if (c == null || c.name.StartsWith("__preview__")) continue;
                string k = c.name; int bar = k.LastIndexOf('|');
                if (bar >= 0 && bar + 1 < k.Length) k = k.Substring(bar + 1);
                if (!d.ContainsKey(k)) d[k] = c;
            }
            return d;
        };
    var ual1 = load(FbxUAL1);
    var ki = load(FbxKI);
    if (!ki.ContainsKey("Run01_Forward")) { flush("[ERR] KI Run01_Forward 没载入"); yield break; }

    var names = new System.Collections.Generic.List<string>();
    var clips = new System.Collections.Generic.List<UnityEngine.AnimationClip>();
    System.Action<string, System.Collections.Generic.Dictionary<string, UnityEngine.AnimationClip>> add =
        (nm, d) => { if (d.ContainsKey(nm)) { names.Add(nm); clips.Add(d[nm]); } else sb.AppendLine("[!] 缺: " + nm); };
    add("Sprint_Loop", ual1);
    add("Jog_Fwd_Loop", ual1);
    add("Run01_Forward", ki);

    // ---- 临时渲染相机（不抢 Camera.main）----
    var camGo = new UnityEngine.GameObject("TmpPoseCam");
    var tcam = camGo.AddComponent<UnityEngine.Camera>();
    tcam.CopyFrom(mainCam);
    tcam.tag = "Untagged";
    tcam.enabled = false;
    tcam.clearFlags = UnityEngine.CameraClearFlags.SolidColor;
    tcam.backgroundColor = new UnityEngine.Color(0.13f, 0.15f, 0.18f);
    tcam.fieldOfView = 40f;
    tcam.targetTexture = null;

    // ---- 跑起来（控制器照常工作）----
    ctl.BeginInputOverride();
    ctl.SetInjectedMove(new UnityEngine.Vector2(0f, 1f), true);
    float settle = UnityEngine.Time.time + 2.6f;
    while (UnityEngine.Time.time < settle) yield return null;

    // ---- 藏起除角色外的渲染器，背景变纯色 ----
    var keep = new System.Collections.Generic.HashSet<UnityEngine.Renderer>(root.GetComponentsInChildren<UnityEngine.Renderer>());
    var hidden = new System.Collections.Generic.List<UnityEngine.Renderer>();
    foreach (var r in UnityEngine.Object.FindObjectsOfType<UnityEngine.Renderer>())
        if (r.enabled && !keep.Contains(r)) { r.enabled = false; hidden.Add(r); }
    sb.AppendLine("已隐藏非角色 Renderer: " + hidden.Count + " 个");
    sb.AppendLine();

    System.Func<UnityEngine.Vector3, UnityEngine.Vector3, float> lineYaw =
        (a, b) =>
        {
            UnityEngine.Vector3 d = root.InverseTransformDirection(b - a);
            return UnityEngine.Mathf.Atan2(d.x, d.z) * UnityEngine.Mathf.Rad2Deg;
        };
    System.Func<UnityEngine.Transform, UnityEngine.Vector3, float> axisYaw =
        (t, axis) =>
        {
            UnityEngine.Vector3 d = root.InverseTransformDirection(t.TransformDirection(axis));
            return UnityEngine.Mathf.Atan2(d.x, d.z) * UnityEngine.Mathf.Rad2Deg;
        };

    int COLS = 4, ROWS = 2, CW = 480, CH = 270;
    string[] angles = { "back", "side" };
    float dist = 3.1f;
    float animSpeedOrig = anim.speed;

    sb.AppendLine("=== 横比（控制器照常跑 / 同角色同 Avatar / 同机位）===");
    sb.AppendLine();

    for (int ci = 0; ci < clips.Count; ci++)
    {
        stRun.motion = clips[ci];
        float w = UnityEngine.Time.time + 0.9f;
        while (UnityEngine.Time.time < w) yield return null;

        var si = anim.GetCurrentAnimatorStateInfo(0);

        // ---- 测量（原生速度 2.4s）----
        float hMin = 9999f, hMax = -9999f, sMin = 9999f, sMax = -9999f;
        float pHMin = 9999f, pHMax = -9999f, pCMin = 9999f, pCMax = -9999f;
        float msSum = 0f, spdSum = 0f;
        float t0 = UnityEngine.Time.time;
        int n = 0;
        while (UnityEngine.Time.time - t0 < 2.4f)
        {
            float hy = lineYaw(lHip.position, rHip.position);
            float sy = lineYaw(lSh.position, rSh.position);
            float py = axisYaw(hips, UnityEngine.Vector3.right);
            float cy = axisYaw(chest, UnityEngine.Vector3.right);
            hMin = UnityEngine.Mathf.Min(hMin, hy); hMax = UnityEngine.Mathf.Max(hMax, hy);
            sMin = UnityEngine.Mathf.Min(sMin, sy); sMax = UnityEngine.Mathf.Max(sMax, sy);
            pHMin = UnityEngine.Mathf.Min(pHMin, py); pHMax = UnityEngine.Mathf.Max(pHMax, py);
            pCMin = UnityEngine.Mathf.Min(pCMin, cy); pCMax = UnityEngine.Mathf.Max(pCMax, cy);
            msSum += anim.GetFloat("MotionSpeed");
            spdSum += ctl.CurrentSpeed;
            n++;
            yield return null;
        }

        sb.AppendLine(string.Format("{0}. {1}   len={2:F3}s   state={3}   v={4:F1} m/s   MotionSpeed={5:F2}",
            ci + 1, clips[ci].name, clips[ci].length,
            si.IsName("Run") ? "Run" : "OTHER", n > 0 ? spdSum / n : 0f, n > 0 ? msSum / n : 0f));
        sb.AppendLine(string.Format("     髋线偏航 p-p = {0,6:F1} deg", hMax - hMin));
        sb.AppendLine(string.Format("     肩线偏航 p-p = {0,6:F1} deg", sMax - sMin));
        sb.AppendLine(string.Format("     骨盆骨骼 p-p = {0,6:F1} deg   <- 扭腰的主要来源", pHMax - pHMin));
        sb.AppendLine(string.Format("     胸腔骨骼 p-p = {0,6:F1} deg", pCMax - pCMin));
        sb.AppendLine(string.Format("     扭转(髋+肩) = {0,6:F1} deg", (hMax - hMin) + (sMax - sMin)));
        sb.AppendLine();

        // ---- 连拍（放慢）----
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
                tcam.targetTexture = rt;
                tcam.Render();
                tcam.targetTexture = null;
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

                float step = UnityEngine.Time.time + 0.45f;
                while (UnityEngine.Time.time < step) yield return null;
            }
            sheet.Apply();
            string safe = clips[ci].name.Replace('|', '_');
            string fn = (ci + 1) + "_" + safe + "_" + an + ".png";
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(imgDir, fn), sheet.EncodeToPNG());
            UnityEngine.Object.Destroy(sheet);
            sb.AppendLine("  写出 " + fn);
        }
        anim.speed = animSpeedOrig <= 0f ? 1f : animSpeedOrig;
        sb.AppendLine();
        flush(sb.ToString());   // 每轮都落盘，中途出错也不至于丢结果
    }

    // ---- 复原 ----
    stRun.motion = ki["Run01_Forward"];
    anim.speed = animSpeedOrig <= 0f ? 1f : animSpeedOrig;
    foreach (var r in hidden) if (r != null) r.enabled = true;
    UnityEngine.Object.Destroy(camGo);
    ctl.SetInjectedMove(UnityEngine.Vector2.zero, false);
    ctl.EndInputOverride();

    sb.AppendLine("已复原：Run 片段 = " + (stRun.motion != null ? stRun.motion.name : "null")
        + "，Renderer " + hidden.Count + " 个已恢复");
    flush(sb.ToString());
    UnityEngine.Debug.Log("[ab] written");
}

return Body();
