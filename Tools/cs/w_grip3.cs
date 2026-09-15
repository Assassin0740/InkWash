// 握持诊断 v3：冻结姿势（else Animator 每帧覆盖指骨）+ 沿剑轴扫偏移 + 打印真实朝向核对。
//
// v2 的教训：w_grip/w_grip2 里在 yield return null 之后改指骨是**无效**的 ——
// Animator 下一帧就把 localRotation 写回去了，所谓"握拳图"其实一直是张开手。
// 解法：anim.Play(...) → anim.Update(1/60) → anim.enabled = false，姿势冻住再改骨骼。
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

const float G_MID = 0.4050f;   // 剑柄中点（模型空间 Y）

IEnumerator Body()
{
    var sb = new System.Text.StringBuilder();
    string projRoot = System.IO.Path.GetDirectoryName(Application.dataPath);
    string imgDir = System.IO.Path.Combine(projRoot, "Tools/screenshots/sword_grip3");
    System.IO.Directory.CreateDirectory(imgDir);
    yield return null;

    var go = GameObject.Find("Player");
    if (go == null) { Debug.LogError("[w_grip3] no Player"); yield break; }
    var anim = go.GetComponent<Animator>();
    var ctl = go.GetComponent<InkWash.Player.PlayerController>();
    if (ctl != null) ctl.enabled = false;
    var ik = go.GetComponent<InkWash.Player.FootIK>();
    if (ik != null) ik.enabled = false;

    var hand = anim.GetBoneTransform(HumanBodyBones.RightHand);
    Transform weapon = null;
    foreach (var f in go.GetComponentsInChildren<MeshFilter>(true))
        if (f.sharedMesh != null && f.sharedMesh.vertexCount > 20000) { weapon = f.transform; break; }
    var align = weapon.parent;
    var socket = align.parent;

    // ---- 冻结姿势 ----
    anim.Play("Idle", 0, 0.30f);
    anim.Update(1f / 60f);
    yield return null;
    anim.enabled = false;                       // ★ 关键：之后画的骨骼才不会被 WriteGuard 之外的 Animator 覆盖
    yield return null;

    // ---- 指骨 ----
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

    Vector3 fingerDir = hand.TransformDirection(Vector3.up).normalized;
    Transform i1 = null, p1 = null;
    foreach (var t in chains["Index"]) if (t.name.EndsWith("1")) i1 = t;
    foreach (var t in chains["Pinky"]) if (t.name.EndsWith("1")) p1 = t;
    Vector3 spreadDir = (p1.position - i1.position).normalized;
    Vector3 palmN = Vector3.Cross(fingerDir, spreadDir).normalized;

    // ---- 结构自检：把假设全打出来 ----
    sb.AppendLine("========== 结构自检 ==========");
    sb.AppendLine("  socket(" + socket.name + ") localRotation = " + socket.localRotation.eulerAngles.ToString("F2") + "  localScale = " + socket.localScale.ToString("F4"));
    sb.AppendLine("  align (" + align.name + ") localRotation = " + align.localRotation.eulerAngles.ToString("F2") + "  localScale = " + align.localScale.ToString("F4"));
    sb.AppendLine("  mesh  (" + weapon.name + ") localRotation = " + weapon.localRotation.eulerAngles.ToString("F2") + "  localScale = " + weapon.localScale.ToString("F4"));
    sb.AppendLine("  mesh 顶点空间包围盒 = " + weapon.GetComponent<MeshFilter>().sharedMesh.bounds.ToString("F4"));
    sb.AppendLine("  hand.lossyScale = " + hand.lossyScale.ToString("F4") + "  socket.lossyScale = " + socket.lossyScale.ToString("F4"));

    // ---- 捏拳 ----
    float[] curlBig = { 70f, 85f, 55f };
    foreach (var kv in chains)
    {
        if (kv.Key == "Thumb") continue;
        foreach (var t in kv.Value)
        {
            int seg = t.name.EndsWith("1") ? 0 : t.name.EndsWith("2") ? 1 : 2;
            t.localRotation = t.localRotation * Quaternion.AngleAxis(curlBig[seg], t.InverseTransformDirection(spreadDir));
        }
    }
    float[] curlTh = { 30f, 30f, 25f };
    foreach (var t in chains["Thumb"])
    {
        int seg = t.name.EndsWith("1") ? 0 : t.name.EndsWith("2") ? 1 : 2;
        t.localRotation = t.localRotation * Quaternion.AngleAxis(curlTh[seg], t.InverseTransformDirection(palmN));
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

    // 拳心：世界 / 根空间 / 手局部
    Vector3 fLocal = hand.InverseTransformPoint(fistC);
    sb.AppendLine();
    sb.AppendLine("  拳心(世界) = " + fistC.ToString("F4"));
    sb.AppendLine("  拳心(根空间) = " + fLocal.ToString("F4") + "  |f| = " + fLocal.magnitude.ToString("F4") + " root单位");
    sb.AppendLine("  fingerDir = " + fingerDir.ToString("F3"));
    sb.AppendLine("  spreadDir = " + spreadDir.ToString("F3"));
    sb.AppendLine("  palmN     = " + palmN.ToString("F3"));

    // ---- 相机 ----
    var camGo = new GameObject("TmpGrip3Cam");
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
    float s = align.localScale.x;

    sb.AppendLine();
    sb.AppendLine("========== 扫描：剑轴方向 = ±spreadDir，偏移 c 沿剑轴（root单位）==========");
    sb.AppendLine("  核对列：构造的 dl 与实测剑身方向的夹角（应 ≈0°；≈180° 说明我把 ±Y 搞反了）");

    float[] offsets = { 0.00f, 0.05f, 0.10f, 0.15f, 0.20f, 0.25f };
    foreach (var sgn in new[] { 1f, -1f })
    {
        Vector3 dW = spreadDir * sgn;
        Vector3 dl = hand.InverseTransformDirection(dW).normalized;
        Quaternion R = Quaternion.FromToRotation(Vector3.down, dl);
        align.localRotation = R;

        foreach (var c in offsets)
        {
            align.localPosition = fLocal + c * dl;
            yield return null;

            // 实测剑身方向：模型尖端在 mesh 顶点空间 -Y，而 mesh 节点在 Align 下
            Vector3 bladeW = -(weapon.TransformDirection(Vector3.up)).normalized;
            float mis = Vector3.Angle(bladeW, dW);

            // 剑首端（模型 +Y）世界位置，相对拳心的沿轴距离
            Vector3 pomW = weapon.TransformPoint(new Vector3(0f, 0.5988f, 0f));
            float pomAlong = Vector3.Dot(pomW - fistC, bladeW);

            string tag = (sgn > 0 ? "E1_p" : "E2_n") + "_c" + c.ToString("F2").Replace(".", "p");
            sb.AppendLine("  " + tag.PadRight(14) + " 夹角 " + mis.ToString("F1") + "° ｜ 剑首在拳心" + (pomAlong >= 0 ? "前 " : "后 ") + Mathf.Abs(pomAlong).ToString("F3") + " m");

            yield return Shot(tcam, imgDir, tag, "45", hand);
        }
    }

    align.localPosition = p0; align.localRotation = r0;
    anim.enabled = true;
    ctl.enabled = true; if (ik != null) ik.enabled = true;
    foreach (var r in hidden) if (r != null) r.enabled = true;
    Object.Destroy(camGo);
    Debug.Log(sb.ToString());
    Debug.Log("[w_grip3] done -> " + imgDir);
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
