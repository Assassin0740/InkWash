// 握持诊断 v2：把挂点补上"拳头中心"，再谈朝向。
//
// v1 的错误：挂点公式写成了 P = s·Gm·d，隐含假设"根原点（=腕关节）就是目标挂点"。
//   实测腕→拳心 = 0.141 m，所以剑柄中点被摆在了 **腕** 上，拳头正好卡在 **护手** 上
//   （护手在腕前 0.1475 m，与拳心 0.141 m 吻合）。
//
// v2 公式：p_root = P + R·(s·v_model)，要让模型上的握持点 v0=(0,Gm,0) 落到拳心 f（根空间）：
//   f = P + R·(s·(0,Gm,0))，而 R·(0,-1,0)=d ⇒ R·(0,1,0) = -d
//   ⇒ P = f - s·Gm·(-d) = **f + s·Gm·d**
//
// 朝向候选改用四元数直接构造：R 把模型 -Y（模型里剑尖所在）转到 d，再叠一个绕剑轴的 roll。
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

const float SCALE = 0.49f;      // Align 缩放（与 prefab 一致）
const float G_MID = 0.4050f;    // 剑柄中点（模型空间 Y：护手 0.3000 → 剑首 0.5100）

IEnumerator Body()
{
    var sb = new System.Text.StringBuilder();
    string projRoot = System.IO.Path.GetDirectoryName(Application.dataPath);
    string imgDir = System.IO.Path.Combine(projRoot, "Tools/screenshots/sword_grip");
    System.IO.Directory.CreateDirectory(imgDir);
    yield return null;

    var go = GameObject.Find("Player");
    if (go == null) { Debug.LogError("[w_grip2] no Player"); yield break; }
    var anim = go.GetComponent<Animator>();
    var ctl = go.GetComponent<InkWash.Player.PlayerController>();
    var ik = go.GetComponent<InkWash.Player.FootIK>();
    if (ctl != null) ctl.enabled = false;
    if (ik != null) ik.enabled = false;

    var hand = anim.GetBoneTransform(HumanBodyBones.RightHand);
    Transform weapon = null;
    foreach (var f in go.GetComponentsInChildren<MeshFilter>(true))
        if (f.sharedMesh != null && f.sharedMesh.vertexCount > 20000) { weapon = f.transform; break; }
    if (weapon == null || hand == null) { Debug.LogError("[w_grip2] 缺手骨或武器"); yield break; }
    var align = weapon.parent;
    var socket = align.parent;

    // 只认手骨下的真指骨（CC_Base 的 *Toe1 是脚趾）
    var chains = new Dictionary<string, List<Transform>>();
    foreach (var t in go.GetComponentsInChildren<Transform>(true))
    {
        string n = t.name;
        if (!n.Contains("R_") || n.Contains("Toe") || !t.IsChildOf(hand)) continue;
        string key = null;
        foreach (var k in new[] { "Thumb", "Index", "Mid", "Ring", "Pinky" })
            if (n.Contains(k)) { key = k; break; }
        if (key == null) continue;
        if (!chains.ContainsKey(key)) chains[key] = new List<Transform>();
        chains[key].Add(t);
    }

    anim.Play("Idle", 0, 0.30f);
    anim.Update(1f / 60f);
    yield return null;

    Vector3 fingerDir = hand.TransformDirection(Vector3.up).normalized;
    Transform i1 = null, p1 = null;
    foreach (var t in chains["Index"]) if (t.name.EndsWith("1") && !t.name.Contains("Toe")) i1 = t;
    foreach (var t in chains["Pinky"]) if (t.name.EndsWith("1") && !t.name.Contains("Toe")) p1 = t;
    Vector3 spreadDir = (p1.position - i1.position).normalized;
    Vector3 palmN = Vector3.Cross(fingerDir, spreadDir).normalized;

    // ---- 捏拳 ----
    float[] curlBig = { 70f, 85f, 55f };
    foreach (var kv in chains)
    {
        if (kv.Key == "Thumb") continue;
        foreach (var t in kv.Value)
        {
            int seg = t.name.EndsWith("1") ? 0 : t.name.EndsWith("2") ? 1 : 2;
            Vector3 ax = t.InverseTransformDirection(spreadDir);
            t.localRotation = t.localRotation * Quaternion.AngleAxis(curlBig[seg], ax);
        }
    }
    float[] curlTh = { 30f, 30f, 25f };
    foreach (var t in chains["Thumb"])
    {
        int seg = t.name.EndsWith("1") ? 0 : t.name.EndsWith("2") ? 1 : 2;
        Vector3 ax = t.InverseTransformDirection(palmN);
        t.localRotation = t.localRotation * Quaternion.AngleAxis(curlTh[seg], ax);
    }
    yield return null;

    // 拳心（捏拳之后）
    Vector3 fistC = Vector3.zero; int fc = 0;
    foreach (var kv in chains)
    {
        if (kv.Key == "Thumb") continue;
        foreach (var t in kv.Value) if (t.name.EndsWith("1")) { fistC += t.position; fc++; }
    }
    fistC /= Mathf.Max(1, fc);
    fistC += palmN * 0.015f;

    Vector3 fLocal = hand.InverseTransformPoint(fistC);   // 根空间（= 手骨空间）坐标，单位与 root-local 一致

    sb.AppendLine("========== 挂点复核 ==========");
    sb.AppendLine("  手骨 lossyScale = " + hand.lossyScale.x.ToString("F4") + " ｜ 根(lossyScale) = " + socket.lossyScale.x.ToString("F4"));
    sb.AppendLine("  拳心（世界） = " + fistC.ToString("F4"));
    sb.AppendLine("  拳心在根空间 = " + fLocal.ToString("F4") + "   |f|=" + fLocal.magnitude.ToString("F4") + " root单位  (×" + socket.lossyScale.x.ToString("F3") + " = " + (fLocal.magnitude * socket.lossyScale.x).ToString("F4") + " m)");
    sb.AppendLine("  fingerDir=" + fingerDir.ToString("F3") + "  spreadDir=" + spreadDir.ToString("F3") + "  palmN=" + palmN.ToString("F3"));

    // ---- 相机 ----
    var camGo = new GameObject("TmpGrip2Cam");
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

    var p0 = align.localPosition; var r0 = align.localRotation;

    // ---- 候选：(名字, 方向, roll) ----
    var cand = new (string tag, Vector3 d, float roll)[]
    {
        ("D1_spread_r0",   spreadDir,  0f),
        ("D2_spread_r90",  spreadDir, 90f),
        ("D3_nspread_r0", -spreadDir,  0f),
        ("D4_nspread_r90",-spreadDir, 90f),
        ("D5_finger",      fingerDir,  0f),
        ("D6_nfinger",    -fingerDir,  0f),
    };

    sb.AppendLine();
    sb.AppendLine("========== 候选（挂点已补上拳心）==========");
    sb.AppendLine("  拳心到剑轴垂距（越小越「在手里」）｜ 剑首相对拳心的位置");
    foreach (var (tag, d, roll) in cand)
    {
        Vector3 dl = hand.InverseTransformDirection(d).normalized;
        Quaternion R = Quaternion.AngleAxis(roll, dl) * Quaternion.FromToRotation(Vector3.down, dl);
        align.localRotation = R;
        align.localPosition = fLocal + SCALE * G_MID * dl;
        yield return null;

        Vector3 wAxis = weapon.TransformDirection(Vector3.up).normalized;
        Vector3 rel = fistC - socket.position;
        float t = Vector3.Dot(rel, wAxis);
        float perp = (rel - wAxis * t).magnitude;
        // 剑首（模型 +Y 端）到拳心的距离，正数=在拳心另一侧
        Vector3 pomW = weapon.TransformPoint(new Vector3(0f, 0.5988f, 0f));
        float pomAlong = Vector3.Dot(pomW - fistC, wAxis);
        sb.AppendLine("  " + tag.PadRight(16) + " 拳心垂距 " + perp.ToString("F4") + " m ｜ 剑首在拳心" + (pomAlong >= 0 ? "前方 " : "后方 ") + Mathf.Abs(pomAlong).ToString("F4") + " m");

        yield return Shot(tcam, imgDir, tag, "45", hand);
    }

    // 张开手 + 当前朝向做对照
    align.localPosition = p0; align.localRotation = r0;
    yield return null;
    yield return Shot(tcam, imgDir, "D0_baseline_open", "45", hand);

    foreach (var r in hidden) if (r != null) r.enabled = true;
    Object.Destroy(camGo);
    if (ctl != null) ctl.enabled = true;
    if (ik != null) ik.enabled = true;
    Debug.Log(sb.ToString());
    Debug.Log("[w_grip2] done -> " + imgDir);
    yield return null;
}

IEnumerator Shot(Camera cam, string dir, string name, string view, Transform hand)
{
    var player = GameObject.Find("Player");
    Vector3 fwd = player.transform.forward; fwd.y = 0f; fwd.Normalize();
    Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized;
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
