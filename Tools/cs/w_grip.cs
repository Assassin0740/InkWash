// 握持诊断：把"剑应该朝哪"从猜变成量。
//
// 背景：动画包全是徒手动作（Quaternius UAL1/UAL2 + Kevin Iglesias 只有 Idle/Walk/Run 三个徒手片段），
// 手是张开的"放松手"，所以剑看着像搁在手心。本脚本做两件事：
//   1. 量出持剑手的解剖轴：指骨方向 fingerDir、指根张开方向 spreadDir（=食指根→小指根）、
//      掌心法向 palmNormal = cross(fingerDir, spreadDir)。
//      —— 拳头的"隧道轴"（剑柄该穿过的方向）就等于 spreadDir，这是握拳时指骨的铰链轴，
//         不是"手指伸出去的方向"。所以之前把剑对齐 hand +Y 是错的。
//   2. 把右手捏成拳（按 spreadDir 作铰链轴卷指骨，只有运行时改，不写资产），
//      然后在若干候选朝向下各拍一张特写，肉眼看哪个像"握住"。
//
// 挂点数学（推导见文档）：
//   设模型空间里剑尖在 -Y、剑柄中点在 +Gm，Align 缩放 s，Align 局部位移 P、局部旋转 R。
//   要"根原点落在拳头中心"就是 P + R·(s·(0,Gm,0)) = 0，而 R·(0,-1,0) = d（d = 根空间里的剑身方向）
//   ⇒ P = -R·(s·Gm·(0,1,0)) = s·Gm·d。
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// ---- 参数 ----
const float SCALE = 0.49f;      // Align 的缩放（与 prefab 现有一致）
const float G_MID = 0.4050f;    // 剑柄中点（模型空间 Y：护手到剑首是 0.3000~0.5100）

IEnumerator Body()
{
    var sb = new System.Text.StringBuilder();
    string projRoot = System.IO.Path.GetDirectoryName(Application.dataPath);
    string imgDir = System.IO.Path.Combine(projRoot, "Tools/screenshots/sword_grip");
    System.IO.Directory.CreateDirectory(imgDir);

    yield return null;

    var go = GameObject.Find("Player");
    if (go == null) { Debug.LogError("[w_grip] no Player"); yield break; }
    var anim = go.GetComponent<Animator>();
    var ctl = go.GetComponent<InkWash.Player.PlayerController>();
    var ik = go.GetComponent<InkWash.Player.FootIK>();
    if (ctl != null) ctl.enabled = false;
    if (ik != null) ik.enabled = false;

    var hand = anim.GetBoneTransform(HumanBodyBones.RightHand);
    if (hand == null) { Debug.LogError("[w_grip] no RightHand bone"); yield break; }

    // 找武器（按顶点数，正式模型 29,959）
    Transform weapon = null;
    foreach (var f in go.GetComponentsInChildren<MeshFilter>(true))
    {
        if (f.sharedMesh != null && f.sharedMesh.vertexCount > 20000) { weapon = f.transform; break; }
    }
    if (weapon == null) { Debug.LogError("[w_grip] 找不到武器网格"); yield break; }
    var align = weapon.parent;                       // Align
    var socket = align != null ? align.parent : null; // 根节点（挂点）

    // ---- 指骨清单 ----
    sb.AppendLine("========== 持剑手骨骼清单 ==========");
    var chains = new Dictionary<string, List<Transform>>();
    foreach (var t in go.GetComponentsInChildren<Transform>(true))
    {
        string n = t.name;
        if (!n.Contains("R_")) continue;
        string key = null;
        foreach (var k in new[] { "Thumb", "Index", "Mid", "Ring", "Pinky" })
            if (n.Contains(k)) { key = k; break; }
        if (key == null) continue;
        if (n.Contains("Forearm") || n.Contains("Upperarm") || n.Contains("Clavicle")) continue;
        // ⚠ CC_Base 骨架把脚趾也叫 Index/Mid/Ring/PinkyToe1 —— 必须排除，否则"拳心"会被拖到脚上
        if (n.Contains("Toe")) continue;
        // 只认真正挂在手骨下的
        if (!t.IsChildOf(hand)) continue;
        if (!chains.ContainsKey(key)) chains[key] = new List<Transform>();
        chains[key].Add(t);
    }
    foreach (var kv in chains)
    {
        sb.Append("  右手 " + kv.Key.PadRight(6) + " : ");
        foreach (var t in kv.Value) sb.Append(t.name + " ");
        sb.AppendLine();
    }
    if (chains.Count == 0) sb.AppendLine("  （没找到 —— 命名前缀可能不是 R_，需要换关键词重扫）");

    Transform index1 = null, pinky1 = null;
    if (chains.ContainsKey("Index")) foreach (var t in chains["Index"]) if (t.name.EndsWith("1")) index1 = t;
    if (chains.ContainsKey("Pinky")) foreach (var t in chains["Pinky"]) if (t.name.EndsWith("1")) pinky1 = t;

    // ---- 解剖轴（需在 Idle 姿态下量）----
    anim.Play("Idle", 0, 0.30f);
    anim.Update(1f / 60f);
    yield return null;

    Vector3 fingerDir = hand.TransformDirection(Vector3.up).normalized;
    Vector3 spreadDir;
    if (index1 != null && pinky1 != null)
        spreadDir = (pinky1.position - index1.position).normalized;
    else
        spreadDir = hand.TransformDirection(Vector3.forward).normalized;
    Vector3 palmN = Vector3.Cross(fingerDir, spreadDir).normalized;
    Vector3 spreadLocal = hand.InverseTransformDirection(spreadDir);

    // 拳心：四指指根的中点 + 往前臂方向挪一点点（拳头中心在指根掌侧）
    Vector3 fistC = Vector3.zero; int fc = 0;
    foreach (var kv in chains)
    {
        if (kv.Key == "Thumb") continue;
        foreach (var t in kv.Value) if (t.name.EndsWith("1")) { fistC += t.position; fc++; }
    }
    if (fc > 0) fistC /= fc;
    fistC += palmN * 0.015f;   // MCP 均值偏手背侧，拳头隧道中心在掌心侧，往掌心挪 1.5cm

    sb.AppendLine();
    sb.AppendLine("========== 解剖轴（世界，Idle nt=0.30）==========");
    sb.AppendLine("  手骨原点（腕） = " + hand.position.ToString("F4"));
    sb.AppendLine("  fingerDir  = " + fingerDir.ToString("F3") + "   （手骨 +Y，指骨伸出方向）");
    sb.AppendLine("  spreadDir  = " + spreadDir.ToString("F3") + "   （食指根→小指根，= 拳头隧道轴）");
    sb.AppendLine("  palmNormal = " + palmN.ToString("F3"));
    sb.AppendLine("  拳心（四指根均值） = " + fistC.ToString("F4"));
    sb.AppendLine("  腕→拳心距离 = " + Vector3.Distance(hand.position, fistC).ToString("F4") + " m");

    Vector3 curBlade = weapon.TransformDirection(Vector3.up).normalized;
    sb.AppendLine();
    sb.AppendLine("  当前剑身方向（Align +Y 世界） = " + curBlade.ToString("F3"));
    sb.AppendLine("    与 fingerDir 夹角 = " + Vector3.Angle(curBlade, fingerDir).ToString("F1") + "°");
    sb.AppendLine("    与 spreadDir 夹角 = " + Vector3.Angle(curBlade, spreadDir).ToString("F1") + "°");
    sb.AppendLine("  ⚠ 注意：网格模型空间里剑尖在 -Y（modelY 尖 -0.5988 / 首 +0.5988），");
    sb.AppendLine("     所以 Align 的 +Y 才是「指向剑尖」的方向，节点 TransformDirection(up) 就是剑身方向。");

    // ---- 相机 ----
    var camGo = new GameObject("TmpGripCam");
    var tcam = camGo.AddComponent<Camera>();
    tcam.CopyFrom(Camera.main);
    tcam.tag = "Untagged"; tcam.enabled = false;
    tcam.clearFlags = CameraClearFlags.SolidColor;
    tcam.backgroundColor = new Color(0.13f, 0.15f, 0.18f);
    tcam.orthographic = false; tcam.fieldOfView = 42f;

    var keep = new HashSet<Renderer>(go.GetComponentsInChildren<Renderer>());
    var hidden = new List<Renderer>();
    foreach (var r in Object.FindObjectsOfType<Renderer>())
        if (r.enabled && !keep.Contains(r)) { r.enabled = false; hidden.Add(r); }

    // 记下初始状态，便于还原
    var p0 = align.localPosition; var r0 = align.localRotation;

    // ---- A. 张开手基线（当前朝向）----
    yield return Shot(tcam, imgDir, "A0_open_current", "45", hand);

    // ---- B. 握拳（铰链轴 = spreadDir），朝向不变 ----
    // 卷指：远端多、近端少，拇指轻卷
    float[] curlBig = { 70f, 85f, 55f };   // 四指 3 节
    foreach (var kv in chains)
    {
        if (kv.Key == "Thumb") continue;
        foreach (var t in kv.Value)
        {
            int seg = t.name.EndsWith("1") ? 0 : t.name.EndsWith("2") ? 1 : 2;
            // 每根骨头的局部铰链轴（蜷之前算，等价于"绕固定局部轴旋转"的铰链）
            Vector3 ax = t.InverseTransformDirection(spreadDir);
            t.localRotation = t.localRotation * Quaternion.AngleAxis(curlBig[seg], ax);
        }
    }
    if (chains.ContainsKey("Thumb"))
    {
        float[] curlTh = { 25f, 22f, 18f };
        foreach (var t in chains["Thumb"])
        {
            int seg = t.name.EndsWith("1") ? 0 : t.name.EndsWith("2") ? 1 : 2;
            Vector3 ax = t.InverseTransformDirection(fingerDir);
            t.localRotation = t.localRotation * Quaternion.AngleAxis(curlTh[seg], ax);
        }
    }

    yield return Shot(tcam, imgDir, "B0_fist_current", "45", hand);
    yield return Shot(tcam, imgDir, "B1_fist_current_side", "90", hand);

    // 拳心随指骨旋转而变，重新量一次（现在才是真正的"握住的位置"）
    Vector3 fistC2 = Vector3.zero; int fc2 = 0;
    foreach (var kv in chains)
    {
        if (kv.Key == "Thumb") continue;
        foreach (var t in kv.Value) if (t.name.EndsWith("1")) { fistC2 += t.position; fc2++; }
    }
    if (fc2 > 0) fistC2 /= fc2;
    fistC2 += palmN * 0.015f;
    sb.AppendLine();
    sb.AppendLine("========== 握拳后 ==========");
    sb.AppendLine("  拳心 = " + fistC2.ToString("F4") + "  （原张开手掌心 " + fistC.ToString("F4") + "）");
    sb.AppendLine("  拳心偏移 = " + (fistC2 - fistC).ToString("F4"));

    // ---- C. 候选朝向 ----
    var dirs = new (string tag, Vector3 d)[]
    {
        ("C1_finger",    fingerDir),
        ("C2_nfinger",  -fingerDir),
        ("C3_spread",    spreadDir),
        ("C4_nspread",  -spreadDir),
        ("C5_palmN",     palmN),
        ("C6_npalmN",   -palmN),
    };
    sb.AppendLine();
    sb.AppendLine("========== 候选朝向 ==========");
    foreach (var (tag, d) in dirs)
    {
        Vector3 dl = hand.InverseTransformDirection(d).normalized;
        Quaternion R = Quaternion.FromToRotation(Vector3.down, dl);
        align.localRotation = R;
        align.localPosition = SCALE * G_MID * dl;   // P = s·Gm·d
        yield return null;
        yield return Shot(tcam, imgDir, tag, "45", hand);

        // 量：拳头中心到剑身轴线的垂距 + 剑柄是否真的穿过拳头
        Vector3 wAxis = weapon.TransformDirection(Vector3.up).normalized;
        // 拳头中心在剑轴上的投影参数（相对挂点 = socket 原点）
        Vector3 rel = fistC2 - socket.position;
        float t = Vector3.Dot(rel, wAxis);
        float perp = (rel - wAxis * t).magnitude;
        sb.AppendLine("  " + tag.PadRight(12) + " 与拳心夹角 " + Vector3.Angle(d, Vector3.Normalize(fistC2 - hand.position)).ToString("F1") + "°"
            + " ｜ 拳心在轴上投影 t=" + t.ToString("F3") + "m ｜ 垂距 " + perp.ToString("F4") + "m");
    }

    // ---- 还原 ----
    align.localPosition = p0; align.localRotation = r0;
    foreach (var r in hidden) if (r != null) r.enabled = true;
    Object.Destroy(camGo);
    if (ctl != null) ctl.enabled = true;
    if (ik != null) ik.enabled = true;

    Debug.Log(sb.ToString());
    Debug.Log("[w_grip] done -> " + imgDir);
    yield return null;
}

IEnumerator Shot(Camera cam, string dir, string name, string view, Transform hand)
{
    Vector3 fwd = Vector3.forward, right = Vector3.right;
    var player = GameObject.Find("Player");
    if (player != null)
    {
        fwd = player.transform.forward; fwd.y = 0f; fwd.Normalize();
        right = Vector3.Cross(Vector3.up, fwd).normalized;
    }
    Vector3 viewDir = view == "F" ? fwd : view == "90" ? right : (fwd * 0.6f + right * 0.8f).normalized;
    Vector3 c = hand.position;
    Vector3 cp = c + viewDir * 0.60f + Vector3.up * 0.05f;
    cam.transform.position = cp;
    cam.transform.rotation = Quaternion.LookRotation(c - cp, Vector3.up);

    yield return null;
    int W = 620, H = 560;
    var rt = RenderTexture.GetTemporary(W, H, 24);
    cam.targetTexture = rt; cam.Render(); cam.targetTexture = null;
    var prev = RenderTexture.active;
    RenderTexture.active = rt;
    var snap = new Texture2D(W, H, TextureFormat.RGB24, false);
    snap.ReadPixels(new Rect(0, 0, W, H), 0, 0); snap.Apply();
    RenderTexture.active = prev;
    RenderTexture.ReleaseTemporary(rt);
    System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, name + ".png"), snap.EncodeToPNG());
    Object.Destroy(snap);
}

return Body();
