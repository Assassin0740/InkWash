// 把"握持解"算成可直接写进资产的确定数值（Play 态取 Idle 姿势 + 捏拳后的解剖量）。
//
// 输出（全部在**手骨局部空间**，与姿势无关，可直接序列化进 prefab）：
//   alignRot / alignPos        → 写进 W_Sword.prefab 的 Align 节点
//   bladeLocalOffset           → 写进 Player.prefab 的 SwordVfx（刀光挂点）
//
// 同时把两个 roll 候选各拍一张，肉眼定"剑身立起来还是平躺"。
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

const float G_MID = 0.4050f;      // 剑柄中点（模型空间 Y）
const float TRAIL_FRAC = 0.55f;    // 刀光锚点落在剑身长度的百分比处

IEnumerator Body()
{
    var sb = new System.Text.StringBuilder();
    string projRoot = System.IO.Path.GetDirectoryName(Application.dataPath);
    string imgDir = System.IO.Path.Combine(projRoot, "Tools/screenshots/sword_solve");
    System.IO.Directory.CreateDirectory(imgDir);
    yield return null;

    var go = GameObject.Find("Player");
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

    // 先算局部轴（用未卷指的姿态），再统一卷 —— 保证是"绕固定局部轴"的铰链
    var axisOf = new Dictionary<Transform, Vector3>();
    foreach (var kv in chains) foreach (var t in kv.Value)
        axisOf[t] = t.InverseTransformDirection(kv.Key == "Thumb" ? palmN : spreadDir);
    foreach (var kv in chains)
    {
        float[] cu = kv.Key == "Thumb" ? new float[] { 30f, 30f, 25f } : new float[] { 70f, 85f, 55f };
        foreach (var t in kv.Value)
        {
            int seg = t.name.EndsWith("1") ? 0 : t.name.EndsWith("2") ? 1 : 2;
            t.localRotation = t.localRotation * Quaternion.AngleAxis(cu[seg], axisOf[t]);
        }
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
    Vector3 pNlocal = hand.InverseTransformDirection(palmN).normalized;
    Vector3 dl = hand.InverseTransformDirection(-spreadDir).normalized;

    float s = align.localScale.x;

    sb.AppendLine("========== 握持解（手骨局部空间）==========");
    sb.AppendLine("  hand.lossyScale = " + hand.lossyScale.x.ToString("F6"));
    sb.AppendLine("  Align.localScale = " + align.localScale.ToString("F6"));
    sb.AppendLine("  fLocal(拳心)  = " + fLocal.ToString("F6"));
    sb.AppendLine("  dl(剑身方向)  = " + dl.ToString("F6"));
    sb.AppendLine("  pNlocal(掌法向) = " + pNlocal.ToString("F6"));
    sb.AppendLine("  校验 |dl| = " + dl.magnitude.ToString("F6") + "  dl·pNlocal = " + Vector3.Dot(dl, pNlocal).ToString("F4") + "（应≈0）");
    sb.AppendLine();
    sb.AppendLine("  snap(root 检查): dl 世界 = " + hand.TransformDirection(dl).ToString("F4") + "  应 ≈ " + (-spreadDir).ToString("F4"));

    Quaternion R0 = Quaternion.FromToRotation(Vector3.down, dl);

    // 剑身"平躺"还是"立起来"：让剑的宽轴（模型 X）对齐掌法向 → 剑身平面贴着掌面
    Vector3 n0 = R0 * Vector3.forward;                  // R0 下模型 Z 的去向
    Vector3 projN = Vector3.ProjectOnPlane(pNlocal, dl).normalized;
    Vector3 proj0 = Vector3.ProjectOnPlane(n0, dl).normalized;
    float signedRoll = Vector3.SignedAngle(proj0, projN, dl);
    Quaternion R_flatN = Quaternion.AngleAxis(signedRoll, dl) * R0;
    Quaternion R_flatN_neg = Quaternion.AngleAxis(signedRoll + 180f, dl) * R0;

    sb.AppendLine();
    sb.AppendLine("  R0 下 模型Z 世界去向与掌法向夹角 = " + Vector3.Angle(n0, pNlocal).ToString("F1") + "°");
    sb.AppendLine("  需要的 roll 角 = " + signedRoll.ToString("F2") + "°");

    foreach (var (tag, R) in new (string, Quaternion)[] { ("flatN", R_flatN), ("flatN_neg", R_flatN_neg) })
    {
        Vector3 got = R * Vector3.forward;
        sb.AppendLine();
        sb.AppendLine("  【" + tag + "】");
        sb.AppendLine("    R euler = " + R.eulerAngles.ToString("F6"));
        sb.AppendLine("    R = (" + R.x.ToString("F9") + ", " + R.y.ToString("F9") + ", " + R.z.ToString("F9") + ", " + R.w.ToString("F9") + ")");
        sb.AppendLine("    R·(0,0,1) 与掌法向夹角 = " + Vector3.Angle(got, pNlocal).ToString("F2") + "°（180° 表示朝另一侧）");
        Vector3 P = fLocal + s * G_MID * dl;
        sb.AppendLine("    alignPos = " + P.ToString("F9"));
        // 刀光锚点：剑身从护手(拳心前 0.114m)到剑尖(拳心前 1.089m)，取 55% 处
        // hand-local 单位 = 世界/2.213，与 root-local 1:1；模型单位 × s 即得 local
        float bladeFrom = 0.105f * s;                 // 护手（模型 Y 0.3000）到拳心
        float bladeTo = (0.5988f + G_MID) * s;        // 剑尖（模型 Y -0.5988）到拳心
        float trailD = Mathf.Lerp(bladeFrom, bladeTo, TRAIL_FRAC);
        Vector3 trailOff = fLocal + trailD * dl;
        sb.AppendLine("    护手距拳心(hand-local) = " + bladeFrom.ToString("F6") + "   剑尖 = " + bladeTo.ToString("F6"));
        sb.AppendLine("    bladeLocalOffset = " + trailOff.ToString("F9") + "   （剑身 " + (TRAIL_FRAC * 100f).ToString("F0") + "% 处）");
    }

    // ---- 渲染两个 roll 候选 ----
    var camGo = new GameObject("TmpSolveCam");
    var tcam = camGo.AddComponent<Camera>();
    tcam.CopyFrom(Camera.main);
    tcam.tag = "Untagged"; tcam.enabled = false;
    tcam.clearFlags = CameraClearFlags.SolidColor;
    tcam.backgroundColor = new Color(0.13f, 0.15f, 0.18f);
    tcam.orthographic = false; tcam.fieldOfView = 40f;
    var keep = new HashSet<Renderer>(go.GetComponentsInChildren<Renderer>());
    var hidden = new List<Renderer>();
    foreach (var r in Object.FindObjectsOfType<Renderer>())
        if (r.enabled && !keep.Contains(r)) { r.enabled = false; hidden.Add(r); }

    // 用全身取景，看剑与角色的关系
    var bake = new Mesh();
    var smrs = go.GetComponentsInChildren<SkinnedMeshRenderer>(true);
    foreach (var (tag, R) in new (string, Quaternion)[] { ("flatN", R_flatN), ("flatN_neg", R_flatN_neg) })
    {
        align.localRotation = R;
        align.localPosition = fLocal + s * G_MID * dl;
        yield return null;

        Vector3 fwd = go.transform.forward; fwd.y = 0f; fwd.Normalize();
        Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized;
        Vector3 viewDir = (fwd * 0.6f + right * 0.8f).normalized;

        float mnY = float.MaxValue, mxY = float.MinValue;
        foreach (var sm in smrs)
        {
            sm.BakeMesh(bake, true);
            var m = sm.transform.localToWorldMatrix;
            foreach (var v in bake.vertices) { float y = m.MultiplyPoint3x4(v).y; if (y < mnY) mnY = y; if (y > mxY) mxY = y; }
        }
        Vector3 center = new Vector3(go.transform.position.x, (mnY + mxY) * 0.5f, go.transform.position.z);
        float h = mxY - mnY;
        Vector3 cp = center + viewDir * (h * 1.55f);
        tcam.transform.position = cp;
        tcam.transform.rotation = Quaternion.LookRotation(center - cp, Vector3.up);
        yield return null;

        int W = 512, H = 640;
        var rt = RenderTexture.GetTemporary(W, H, 24);
        tcam.targetTexture = rt; tcam.Render(); tcam.targetTexture = null;
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var snap = new Texture2D(W, H, TextureFormat.RGB24, false);
        snap.ReadPixels(new Rect(0, 0, W, H), 0, 0); snap.Apply();
        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(rt);
        System.IO.File.WriteAllBytes(System.IO.Path.Combine(imgDir, "solve_" + tag + ".png"), snap.EncodeToPNG());
        Object.Destroy(snap);
    }
    Object.Destroy(bake);

    foreach (var r in hidden) if (r != null) r.enabled = true;
    Object.Destroy(camGo);
    anim.enabled = true; ctl.enabled = true; if (ik != null) ik.enabled = true;

    System.IO.File.WriteAllText(System.IO.Path.Combine(projRoot, "Tools/reports/w_solve.txt"), sb.ToString());
    Debug.Log(sb.ToString());
    Debug.Log("[w_solve] done -> " + imgDir);
    yield return null;
}

return Body();
