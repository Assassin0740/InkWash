// Play 态剧照 v2（新握持解：剑身沿手骨 +Z 朝前，剑柄中点 = 拳头中心）。
//
// 与上一版的差别：
//   · 剑身方向不再用节点 up 判（模型尖端在 mesh 顶点空间 -Y，节点 up 天然反向，会误报 180°）；
//     改为**实测**：把 mesh 顶点包围盒沿 Align 的局部轴投影，取最远点 = 剑尖。
//   · 新增刀光锚点核对：锚点必须落在剑身上（到剑轴的垂距 + 沿轴百分比）。
//   · 新增 WeaponHandPose 状态（Armed / CurledBoneCount）。
//
// 取景硬约束（沿用）：
//   1. 攻击段必须**升序**（Atk1 → Atk1Rec → Atk2 → Atk2Rec → Atk3）。倒序无反向转移，会拍到 bind pose。
//   2. **同一状态不要连拍两张**（对已在播的状态重复 Play() 不会重新 seek）。
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

IEnumerator Body()
{
    var sb = new System.Text.StringBuilder();
    string projRoot = System.IO.Path.GetDirectoryName(Application.dataPath);
    string imgDir = System.IO.Path.Combine(projRoot, "Tools/screenshots/sword_ingame2");
    System.IO.Directory.CreateDirectory(imgDir);

    yield return null;
    yield return null;

    var go = GameObject.Find("Player");
    if (go == null) { Debug.LogError("no Player"); yield break; }
    var anim = go.GetComponent<Animator>();
    var ctl = go.GetComponent<InkWash.Player.PlayerController>();
    var ik = go.GetComponent<InkWash.Player.FootIK>();
    var vfx = go.GetComponentInChildren<InkWash.Effects.SwordVfx>(true);
    var pose = go.GetComponent<InkWash.Effects.WeaponHandPose>();
    if (ctl != null) ctl.enabled = false;
    if (ik != null) ik.enabled = false;

    var hand = anim.GetBoneTransform(HumanBodyBones.RightHand);
    var elbow = anim.GetBoneTransform(HumanBodyBones.RightLowerArm);

    Transform weapon = null; MeshFilter wmf = null;
    foreach (var f in go.GetComponentsInChildren<MeshFilter>(true))
        if (f.sharedMesh != null && f.sharedMesh.vertexCount > 20000) { weapon = f.transform; wmf = f; break; }
    if (weapon == null) { Debug.LogError("[WARN] 找不到武器网格"); yield break; }
    var align = weapon.parent;
    var socket = align.parent;

    sb.AppendLine("========== 正式武器实测（新握持）==========");
    sb.AppendLine("SwordVfx.WeaponName = " + (vfx != null ? vfx.WeaponName : "(无 SwordVfx)"));
    sb.AppendLine("SwordVfx.HasWeapon  = " + (vfx != null ? vfx.HasWeapon.ToString() : "-"));
    sb.AppendLine("WeaponHandPose      = " + (pose != null ? ("存在 armed=" + pose.Armed + " 已卷指骨=" + pose.CurledBoneCount) : "(缺失)"));
    sb.AppendLine("武器节点路径 = " + FullPath(weapon, go.transform));
    sb.AppendLine("  网格 " + wmf.sharedMesh.vertexCount + " 顶点 / " + (wmf.sharedMesh.triangles.Length / 3) + " 三角");
    sb.AppendLine("  Align pos=" + align.localPosition.ToString("F6") + "  rot=" + align.localRotation.eulerAngles.ToString("F3"));

    // ---- 实测剑尖/剑首：把 mesh 的 8 个包围盒角点变换到世界，沿剑轴取最远/最近 ----
    var b = wmf.sharedMesh.bounds;
    var l2w = weapon.localToWorldMatrix;
    var corners = new Vector3[8];
    for (int i = 0; i < 8; i++)
        corners[i] = l2w.MultiplyPoint3x4(new Vector3(
            (i & 1) == 0 ? b.min.x : b.max.x,
            (i & 2) == 0 ? b.min.y : b.max.y,
            (i & 4) == 0 ? b.min.z : b.max.z));

    // 剑轴世界方向 = 模型 -Y 经 Align 旋转后的方向。模型尖端在 mesh 顶点空间 Y 最小，所以
    // "从剑首指向剑尖" = 把顶点空间 (0,-1,0) 变换过去。
    Vector3 bladeW = weapon.TransformDirection(Vector3.down).normalized;
    Vector3 origin = fistCenter(anim, go);
    float bestT = float.MinValue, worstT = float.MaxValue;
    Vector3 tipW = Vector3.zero, pommelW = Vector3.zero;
    foreach (var c in corners)
    {
        float t = Vector3.Dot(c - origin, bladeW);
        if (t > bestT) { bestT = t; tipW = c; }
        if (t < worstT) { worstT = t; pommelW = c; }
    }
    sb.AppendLine();
    sb.AppendLine("========== 剑身几何 ==========");
    sb.AppendLine("  剑身世界方向 = " + bladeW.ToString("F3"));
    sb.AppendLine("  剑尖 距拳心 " + bestT.ToString("F3") + " m   世界 " + tipW.ToString("F3"));
    sb.AppendLine("  剑首 距拳心 " + worstT.ToString("F3") + " m");
    sb.AppendLine("  剑身世界长度 ≈ " + (bestT - worstT).ToString("F3") + " m");

    // ---- 刀光锚点核对 ----
    var anchors = go.GetComponentsInChildren<Transform>(true);
    Transform tanchor = null;
    foreach (var t in anchors) if (t.name == "BladeTrail_Anchor") { tanchor = t; break; }
    sb.AppendLine();
    sb.AppendLine("========== 刀光锚点核对 ==========");
    if (tanchor != null)
    {
        float al = Vector3.Dot(tanchor.position - origin, bladeW);
        float perp = (tanchor.position - origin - bladeW * al).magnitude;
        float span = bestT - worstT;
        float pct = span > 1e-4f ? (al - worstT) / span * 100f : -1f;
        sb.AppendLine("  锚点世界 = " + tanchor.position.ToString("F3"));
        sb.AppendLine("  沿剑轴距拳心 = " + al.ToString("F3") + " m ｜ 到剑轴垂距 = " + perp.ToString("F4") + " m");
        sb.AppendLine("  在剑身上所处位置 = " + pct.ToString("F1") + " %（0%=剑首，100%=剑尖）");
        sb.AppendLine("  " + (perp < 0.06f && pct > 5f && pct < 95f ? "[通过] 锚点落在剑身上" : "[未通过] 锚点偏离剑身"));
    }
    else sb.AppendLine("  [未通过] 找不到 BladeTrail_Anchor");

    // ---- 与前臂/躯干穿插 ----
    sb.AppendLine();
    sb.AppendLine("========== 穿插检查 ==========");
    if (elbow != null && hand != null)
    {
        var verts = wmf.sharedMesh.vertices;
        var m2 = weapon.localToWorldMatrix;
        Vector3 ab = hand.position - elbow.position; float L2 = ab.sqrMagnitude;
        int insideArm = 0; float maxPen = -999f;
        for (int i = 0; i < verts.Length; i++)
        {
            Vector3 w = m2.MultiplyPoint3x4(verts[i]);
            float tt = L2 > 1e-9f ? Mathf.Clamp01(Vector3.Dot(w - elbow.position, ab) / L2) : 0f;
            float pen = 0.0573f - Vector3.Distance(w, elbow.position + ab * tt);
            if (pen > 0f) { insideArm++; if (pen > maxPen) maxPen = pen; }
        }
        sb.AppendLine("  剑顶点落在前臂胶囊(半径 0.0573)内 = " + insideArm + " / " + verts.Length + " 个，最大穿入 " + maxPen.ToString("F4") + " m");
        sb.AppendLine("  " + (insideArm < verts.Length / 20 ? "[通过] 剑没有大范围插进前臂" : "[未通过] 剑与前臂大面积穿插"));
    }
    Debug.Log(sb.ToString());

    // ---- 相机 ----
    var camGo = new GameObject("TmpSwordPlayCam2");
    var tcam = camGo.AddComponent<Camera>();
    tcam.CopyFrom(Camera.main);
    tcam.tag = "Untagged"; tcam.enabled = false;
    tcam.clearFlags = CameraClearFlags.SolidColor;
    tcam.backgroundColor = new Color(0.13f, 0.15f, 0.18f);

    var keep = new HashSet<Renderer>(go.GetComponentsInChildren<Renderer>());
    var hidden = new List<Renderer>();
    foreach (var r in Object.FindObjectsOfType<Renderer>())
        if (r.enabled && !keep.Contains(r)) { r.enabled = false; hidden.Add(r); }

    var bake = new Mesh();
    var smrs = go.GetComponentsInChildren<SkinnedMeshRenderer>(true);

    string[] shots = {
        "Idle|0.30|sword_idle_front|F|body",
        "Idle|0.30|sword_idle_side|S|body",
        "Idle|0.30|sword_idle|45|body",
        "Idle|0.30|sword_hand_idle|45|hand",
        "Walk|0.10|sword_walk|45|body",
        "Run|0.10|sword_run|45|body",
        "Atk1|0.30|sword_atk1|45|body",
        "Atk1Rec|0.55|sword_hand_atk1rec|45|hand",
        "Atk2|0.40|sword_atk2|45|body",
        "Atk2Rec|0.55|sword_hand_atk2rec|45|hand",
        "Atk3|0.50|sword_atk3|45|body",
    };

    foreach (var spec in shots)
    {
        var parts = spec.Split('|');
        string state = parts[0]; float nt = float.Parse(parts[1]);
        string name = parts[2]; string view = parts[3]; string frame = parts[4];

        anim.Play("Idle", 0, 0f);
        anim.Update(1f / 60f);
        anim.Play(state, 0, nt);
        anim.Update(1f / 60f);
        // 多等一帧：否则可能在本帧 LateUpdate 之前就渲染，WeaponHandPose 的卷指还没落到骨骼上
        yield return null;
        yield return null;

        Vector3 fwd = go.transform.forward; fwd.y = 0f; fwd.Normalize();
        Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized;
        Vector3 viewDir = view == "F" ? fwd : view == "S" ? right : (fwd * 0.6f + right * 0.8f).normalized;

        if (frame == "hand")
        {
            tcam.orthographic = false; tcam.fieldOfView = 40f;
            Vector3 c = hand.position;
            Vector3 cp = c + viewDir * 0.62f + Vector3.up * 0.06f;
            tcam.transform.position = cp;
            tcam.transform.rotation = Quaternion.LookRotation(c - cp, Vector3.up);
        }
        else
        {
            tcam.orthographic = false; tcam.fieldOfView = 35f;
            float mnY = float.MaxValue, mxY = float.MinValue;
            foreach (var s in smrs)
            {
                s.BakeMesh(bake, true);
                var m = s.transform.localToWorldMatrix;
                foreach (var v in bake.vertices) { float y = m.MultiplyPoint3x4(v).y; if (y < mnY) mnY = y; if (y > mxY) mxY = y; }
            }
            Vector3 center = new Vector3(go.transform.position.x, (mnY + mxY) * 0.5f, go.transform.position.z);
            float h = mxY - mnY;
            Vector3 cp = center + viewDir * (h * 1.55f);
            tcam.transform.position = cp;
            tcam.transform.rotation = Quaternion.LookRotation(center - cp, Vector3.up);
        }

        int W = frame == "hand" ? 620 : 512, H = frame == "hand" ? 560 : 640;
        var rt = RenderTexture.GetTemporary(W, H, 24);
        tcam.targetTexture = rt; tcam.Render(); tcam.targetTexture = null;
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var snap = new Texture2D(W, H, TextureFormat.RGB24, false);
        snap.ReadPixels(new Rect(0, 0, W, H), 0, 0); snap.Apply();
        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(rt);
        System.IO.File.WriteAllBytes(System.IO.Path.Combine(imgDir, name + ".png"), snap.EncodeToPNG());
        Object.Destroy(snap);
    }

    foreach (var r in hidden) if (r != null) r.enabled = true;
    Object.Destroy(bake);
    Object.Destroy(camGo);
    if (ctl != null) ctl.enabled = true;
    if (ik != null) ik.enabled = true;
    System.IO.File.WriteAllText(System.IO.Path.Combine(projRoot, "Tools/reports/w_shot_play2.txt"), sb.ToString());
    Debug.Log("[w_shot_play2] done -> " + imgDir);
    yield return null;
}

// 拳心：右手四指根均值（排除 CC_Base 的 *Toe1 脚趾骨）
Vector3 fistCenter(Animator anim, GameObject go)
{
    var hand = anim.GetBoneTransform(HumanBodyBones.RightHand);
    Vector3 sum = Vector3.zero; int n = 0;
    var all = hand.GetComponentsInChildren<Transform>(true);
    foreach (var t in all)
    {
        if (!t.name.Contains("R_") || t.name.Contains("Toe")) continue;
        if (!t.name.EndsWith("1")) continue;
        bool hit = false;
        foreach (var k in new[] { "Index", "Mid", "Ring", "Pinky" }) if (t.name.Contains(k)) hit = true;
        if (!hit) continue;
        sum += t.position; n++;
    }
    if (n == 0) return hand.position;
    Vector3 c = sum / n;
    Vector3 f = hand.TransformDirection(Vector3.up).normalized;
    Transform i1 = null, p1 = null;
    foreach (var t in all)
    {
        if (t.name.EndsWith("Index1")) i1 = t;
        if (t.name.EndsWith("Pinky1")) p1 = t;
    }
    if (i1 != null && p1 != null)
        c += Vector3.Cross(f, (p1.position - i1.position).normalized).normalized * 0.015f;
    return c;
}

string FullPath(Transform t, Transform root)
{
    var s = t.name;
    while (t.parent != null && t.parent != root) { t = t.parent; s = t.name + "/" + s; }
    return s;
}

return Body();
