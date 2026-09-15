// 捏拳是否真的生效 —— 用数字判定，不靠肉眼猜。
// 做法：冻结姿态后，记录右手每节指骨的朝向（子节点-自身），卷指，再测一次，打印角度变化。
// 同时拍左手（不卷）当对照，看两只手外形是否明显不同。
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

IEnumerator Body()
{
    var sb = new System.Text.StringBuilder();
    string projRoot = System.IO.Path.GetDirectoryName(Application.dataPath);
    string imgDir = System.IO.Path.Combine(projRoot, "Tools/screenshots/sword_fist");
    System.IO.Directory.CreateDirectory(imgDir);
    yield return null;

    var go = GameObject.Find("Player");
    var anim = go.GetComponent<Animator>();
    var ctl = go.GetComponent<InkWash.Player.PlayerController>();
    if (ctl != null) ctl.enabled = false;
    var ik = go.GetComponent<InkWash.Player.FootIK>();
    if (ik != null) ik.enabled = false;

    var hand = anim.GetBoneTransform(HumanBodyBones.RightHand);
    var lhand = anim.GetBoneTransform(HumanBodyBones.LeftHand);

    anim.Play("Idle", 0, 0.30f);
    anim.Update(1f / 60f);
    yield return null;
    anim.enabled = false;
    yield return null;

    // 右手链条
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

    // ---- 卷指前：记朝向 ----
    var before = new Dictionary<Transform, Vector3>();
    var order = new List<Transform>();
    foreach (var kv in chains) foreach (var t in kv.Value)
    {
        if (t.childCount == 0) continue;                 // 末节没有子节点，用不到方向
        before[t] = (t.GetChild(0).position - t.position).normalized;
        order.Add(t);
    }

    // ---- 卷指 ----
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

    sb.AppendLine("========== 卷指前后：指骨朝向变化（度）==========");
    sb.AppendLine("  骨名                    卷前→卷后夹角");
    foreach (var t in order)
    {
        if (t.childCount == 0) continue;
        Vector3 now = (t.GetChild(0).position - t.position).normalized;
        sb.AppendLine("  " + t.name.PadRight(24) + Vector3.Angle(before[t], now).ToString("F1"));
    }
    // 指尖到腕的距离（卷起来应该明显变短）
    float rTipBefore = 0f, rTipAfter = 0f;
    Transform rEnd = null;
    foreach (var kv in chains) if (kv.Key == "Mid") foreach (var t in kv.Value) if (t.name.EndsWith("3")) rEnd = t;
    if (rEnd != null) rTipAfter = Vector3.Distance(hand.position, rEnd.position);
    sb.AppendLine();
    sb.AppendLine("  中指指尖到腕距离（卷后） = " + rTipAfter.ToString("F4") + " m");
    sb.AppendLine("  ⚠ 若上表夹角几乎全为 0 → 卷指没生效；若 70~90° 量级 → 生效");

    // ---- 拍两只手做对照 ----
    var camGo = new GameObject("TmpFistCam");
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

    Vector3 fwd = go.transform.forward, right = go.transform.right;
    yield return Shot(tcam, imgDir, "R_hand_outer", hand, (fwd * 0.35f + right * 0.85f + Vector3.up * 0.25f).normalized);
    yield return Shot(tcam, imgDir, "R_hand_front", hand, (fwd * 0.9f + right * 0.25f + Vector3.up * 0.15f).normalized);
    yield return Shot(tcam, imgDir, "L_hand_outer", lhand, (fwd * 0.35f - right * 0.85f + Vector3.up * 0.25f).normalized);
    yield return Shot(tcam, imgDir, "L_hand_front", lhand, (fwd * 0.9f - right * 0.25f + Vector3.up * 0.15f).normalized);

    foreach (var r in hidden) if (r != null) r.enabled = true;
    Object.Destroy(camGo);
    anim.enabled = true; ctl.enabled = true; if (ik != null) ik.enabled = true;
    Debug.Log(sb.ToString());
    Debug.Log("[w_fist] done -> " + imgDir);
    yield return null;
}

IEnumerator Shot(Camera cam, string dir, string name, Transform target, Vector3 viewDir)
{
    Vector3 c = target.position;
    Vector3 cp = c + viewDir * 0.52f;
    cam.transform.position = cp;
    cam.transform.rotation = Quaternion.LookRotation(c - cp, Vector3.up);
    yield return null;
    int W = 520, H = 520;
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
