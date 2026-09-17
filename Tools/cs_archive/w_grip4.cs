// 握持诊断 v4（收口）：方向取 −spreadDir（解剖上剑刃从虎口穿出），挂点 c = s·Gm，
// 只扫 roll（0 / 90），但**四个视角**各拍一张，一次看清是不是真"握住"。
//
// 结论链：
//   spreadDir = 食指根→小指根 = 指骨屈曲轴 = 拳头隧道轴；
//   剑刃从"掌根 → 虎口"对角线方向穿出，实测该对角线 ≈ −spreadDir（夹角 27°）；
//   挂点：P = f + s·Gm·d，f = 拳心在根空间的坐标，Gm = 剑柄中点 0.405。
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

const float G_MID = 0.4050f;

IEnumerator Body()
{
    var sb = new System.Text.StringBuilder();
    string projRoot = System.IO.Path.GetDirectoryName(Application.dataPath);
    string imgDir = System.IO.Path.Combine(projRoot, "Tools/screenshots/sword_grip4");
    System.IO.Directory.CreateDirectory(imgDir);
    yield return null;

    var go = GameObject.Find("Player");
    var anim = go.GetComponent<Animator>();
    var ctl = go.GetComponent<InkWash.Player.PlayerController>();
    var ik = go.GetComponent<InkWash.Player.FootIK>();
    if (ctl != null) ctl.enabled = false;
    if (ik != null) ik.enabled = false;

    var hand = anim.GetBoneTransform(HumanBodyBones.RightHand);
    Transform weapon = null;
    foreach (var f in go.GetComponentsInChildren<MeshFilter>(true))
        if (f.sharedMesh != null && f.sharedMesh.vertexCount > 20000) { weapon = f.transform; break; }
    var align = weapon.parent;
    var socket = align.parent;

    anim.Play("Idle", 0, 0.30f);
    anim.Update(1f / 60f);
    yield return null;
    anim.enabled = false;
    yield return null;

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

    Transform i1 = null, p1 = null, th1 = null;
    foreach (var t in chains["Index"]) if (t.name.EndsWith("1")) i1 = t;
    foreach (var t in chains["Pinky"]) if (t.name.EndsWith("1")) p1 = t;
    foreach (var t in chains["Thumb"]) if (t.name.EndsWith("1")) th1 = t;

    Vector3 fingerDir = hand.TransformDirection(Vector3.up).normalized;
    Vector3 spreadDir = (p1.position - i1.position).normalized;
    Vector3 palmN = Vector3.Cross(fingerDir, spreadDir).normalized;

    // 掌根（小指根与腕的中点）→ 虎口（拇指根与食指根中点）的对角线
    Vector3 heel = 0.5f * (p1.position + hand.position);
    Vector3 web = 0.5f * (th1.position + i1.position);
    Vector3 diag = (web - heel).normalized;

    // 捏拳
    foreach (var kv in chains)
    {
        if (kv.Key == "Thumb") continue;
        foreach (var t in kv.Value)
        {
            int seg = t.name.EndsWith("1") ? 0 : t.name.EndsWith("2") ? 1 : 2;
            float[] cu = { 70f, 85f, 55f };
            t.localRotation = t.localRotation * Quaternion.AngleAxis(cu[seg], t.InverseTransformDirection(spreadDir));
        }
    }
    foreach (var t in chains["Thumb"])
    {
        int seg = t.name.EndsWith("1") ? 0 : t.name.EndsWith("2") ? 1 : 2;
        float[] cu = { 30f, 30f, 25f };
        t.localRotation = t.localRotation * Quaternion.AngleAxis(cu[seg], t.InverseTransformDirection(palmN));
    }
    yield return null;

    Vector3 fistC = Vector3.zero; int fc = 0;
    foreach (var kv in chains)
    {
        if (kv.Key == "Thumb") continue;
        foreach (var t in kv.Value) if (t.name.EndsWith("1")) { fistC += t.position; fc++; }
    }
    fistC /= Mathf.Max(1, fc);
    fistC += palmN * 0.015f;
    Vector3 fLocal = hand.InverseTransformPoint(fistC);

    sb.AppendLine("========== v4 收口：方向与挂点 ==========");
    sb.AppendLine("  角色 forward = " + go.transform.forward.ToString("F3"));
    sb.AppendLine("  fingerDir = " + fingerDir.ToString("F3"));
    sb.AppendLine("  spreadDir = " + spreadDir.ToString("F3"));
    sb.AppendLine("  palmN     = " + palmN.ToString("F3"));
    sb.AppendLine("  掌根→虎口对角线 diag = " + diag.ToString("F3"));
    sb.AppendLine("    diag 与 -spreadDir 夹角 = " + Vector3.Angle(diag, -spreadDir).ToString("F1") + "°");
    sb.AppendLine("    diag 与 +spreadDir 夹角 = " + Vector3.Angle(diag, spreadDir).ToString("F1") + "°");
    sb.AppendLine("  ⇒ 采用 d = -spreadDir 作为剑身方向（从虎口穿出）");
    sb.AppendLine("    -spreadDir 与角色 forward 夹角 = " + Vector3.Angle(-spreadDir, go.transform.forward).ToString("F1") + "°（<90° = 剑指向前方）");
    sb.AppendLine("  拳心(根空间) = " + fLocal.ToString("F4"));

    var camGo = new GameObject("TmpGrip4Cam");
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

    float s = align.localScale.x;
    Vector3 dW = -spreadDir;
    Vector3 dl = hand.InverseTransformDirection(dW).normalized;

    sb.AppendLine();
    sb.AppendLine("========== 各 roll 下剑首/剑尖相对拳心的位置 ==========");
    foreach (var roll in new[] { 0f, 90f })
    {
        Quaternion R = Quaternion.AngleAxis(roll, dl) * Quaternion.FromToRotation(Vector3.down, dl);
        align.localRotation = R;
        align.localPosition = fLocal + s * G_MID * dl;
        yield return null;

        Vector3 bladeW = -(weapon.TransformDirection(Vector3.up)).normalized;
        Vector3 pomW = weapon.TransformPoint(new Vector3(0f, 0.5988f, 0f));
        Vector3 tipW = weapon.TransformPoint(new Vector3(0f, -0.5988f, 0f));
        sb.AppendLine(string.Format("  roll={0,3:F0}°  剑首(拳心→剑首) {1:F3} m ｜ 剑尖 {2:F3} m ｜ 剑身方向与 d 夹角 {3:F1}°",
            roll, Vector3.Distance(fistC, pomW), Vector3.Distance(fistC, tipW), Vector3.Angle(bladeW, dW)));
        sb.AppendLine("        剑尖世界坐标 = " + tipW.ToString("F3") + "   （角色前向 = " + go.transform.forward.ToString("F2") + "）");

        foreach (var (vtag, vdir) in new (string, Vector3)[]
        {
            ("F", go.transform.forward),
            ("R", go.transform.right),
            ("T", Vector3.up),
            ("P", palmN),
        })
            yield return Shot(tcam, imgDir, "G_roll" + roll.ToString("F0") + "_" + vtag, vdir, hand);
    }

    foreach (var r in hidden) if (r != null) r.enabled = true;
    Object.Destroy(camGo);
    anim.enabled = true; ctl.enabled = true; if (ik != null) ik.enabled = true;
    Debug.Log(sb.ToString());
    Debug.Log("[w_grip4] done -> " + imgDir);
    yield return null;
}

IEnumerator Shot(Camera cam, string dir, string name, Vector3 viewDir, Transform hand)
{
    Vector3 v = viewDir.normalized;
    Vector3 c = hand.position;
    Vector3 cp = c + v * 0.58f;
    Vector3 up = Mathf.Abs(Vector3.Dot(v, Vector3.up)) > 0.95f ? Vector3.forward : Vector3.up;
    cam.transform.position = cp;
    cam.transform.rotation = Quaternion.LookRotation(c - cp, up);
    yield return null;
    int W = 560, H = 560;
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
