// Play 态剧照（正式武器版）：确认宝剑真的挂在右手上、朝向对、不与前臂/躯干穿插。
//
// 与 f_shot_play.cs 的差别：刀不再是程序化占位物，而是 SwordVfx 在 Awake 里克隆的
// W_Sword.prefab。所以本脚本按"网格顶点数 > 20000"去找武器，而不是按名字找 "Blade"。
//
// ⚠ 沿用两条取景硬约束（都踩过）：
//   1. 攻击段必须**升序**（Atk1 → Atk1Rec → Atk2 → Atk2Rec → Atk3）。倒序回跳没有反向转移，会拍到 bind pose。
//   2. **同一个状态不要连拍两张**（对已在播的状态重复 Play() 时 Animator 不会重新 seek）。
//      所以手部特写各自挑**不同**的恢复段，不跟同状态的全身照共用。
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

IEnumerator Body()
{
    var sb = new System.Text.StringBuilder();
    string projRoot = System.IO.Path.GetDirectoryName(Application.dataPath);
    string imgDir = System.IO.Path.Combine(projRoot, "Tools/screenshots/sword_ingame");
    System.IO.Directory.CreateDirectory(imgDir);

    yield return null;
    yield return null;

    var go = GameObject.Find("Player");
    if (go == null) { Debug.LogError("no Player"); yield break; }
    var anim = go.GetComponent<Animator>();
    var ctl = go.GetComponent<InkWash.Player.PlayerController>();
    var ik = go.GetComponent<InkWash.Player.FootIK>();
    var vfx = go.GetComponentInChildren<InkWash.Effects.SwordVfx>(true);
    if (ctl != null) ctl.enabled = false;
    if (ik != null) ik.enabled = false;

    var hand = anim.GetBoneTransform(HumanBodyBones.RightHand);
    var elbow = anim.GetBoneTransform(HumanBodyBones.RightLowerArm);

    // ---- 找武器（按顶点数）----
    Transform weapon = null; MeshFilter wmf = null;
    foreach (var f in go.GetComponentsInChildren<MeshFilter>(true))
    {
        if (f.sharedMesh == null) continue;
        if (f.sharedMesh.vertexCount > 20000) { weapon = f.transform; wmf = f; break; }
    }
    if (weapon == null) { Debug.LogError("[WARN] 找不到武器网格 —— weaponPrefab 没接上？"); yield break; }

    sb.AppendLine("========== 正式武器实测 ==========");
    sb.AppendLine("SwordVfx.WeaponName = " + (vfx != null ? vfx.WeaponName : "(无 SwordVfx)"));
    sb.AppendLine("SwordVfx.HasWeapon  = " + (vfx != null ? vfx.HasWeapon.ToString() : "-"));
    sb.AppendLine("武器节点路径 = " + FullPath(weapon, go.transform));
    sb.AppendLine("  worldScale = " + weapon.lossyScale.ToString("F4"));
    sb.AppendLine("  网格 " + wmf.sharedMesh.vertexCount + " 顶点 / " + (wmf.sharedMesh.triangles.Length / 3) + " 三角");

    // ---- 世界包围盒（8 角点变换，带旋转时不能用 MultiplyVector(extents)）----
    var b = wmf.sharedMesh.bounds;
    var l2w = weapon.localToWorldMatrix;
    var mn = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
    var mx = new Vector3(float.MinValue, float.MinValue, float.MinValue);
    for (int i = 0; i < 8; i++)
    {
        var c = new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
        var w = l2w.MultiplyPoint3x4(c);
        mn = Vector3.Min(mn, w); mx = Vector3.Max(mx, w);
    }
    sb.AppendLine(string.Format("  世界包围盒 size = {0}   （长轴 {1}）", (mx - mn).ToString("F4"),
        (mx - mn).y > (mx - mn).x ? "Y" : "X"));
    sb.AppendLine(string.Format("  世界长度 {0:F3} m / 左右展 {1:F3} m / 前后厚 {2:F3} m",
        (mx - mn).y, (mx - mn).x, (mx - mn).z));

    // ---- 朝向核对：剑身长轴是否沿手骨 +Y ----
    Vector3 handUp = hand.TransformDirection(Vector3.up);
    Vector3 weaponUp = weapon.TransformDirection(Vector3.up);
    sb.AppendLine();
    sb.AppendLine("========== 朝向核对 ==========");
    sb.AppendLine("  手骨 +Y 世界方向   = " + handUp.ToString("F3"));
    sb.AppendLine("  武器 +Y 世界方向   = " + weaponUp.ToString("F3"));
    sb.AppendLine(string.Format("  两者夹角 = {0:F2}°  {1}", Vector3.Angle(handUp, weaponUp),
        Vector3.Angle(handUp, weaponUp) < 1f ? "→ 剑身与手骨轴完全对齐 ✅" : "→ 有偏角，需要查 Align 的旋转"));

    // ---- 剑首 vs 前臂（本次接入的核心风险）----
    sb.AppendLine();
    sb.AppendLine("========== 剑首 / 前臂 关系 ==========");
    if (elbow != null && hand != null)
    {
        // 用武器包围盒在挂点轴上的投影反推：包围盒最低的 8 角点里最靠 -Y 的那个 = 剑首端
        float lowest = float.MaxValue; Vector3 lowestP = Vector3.zero;
        for (int i = 0; i < 8; i++)
        {
            var c = new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
            var w = l2w.MultiplyPoint3x4(c);
            float d = Vector3.Dot(w - hand.position, handUp);
            if (d < lowest) { lowest = d; lowestP = w; }
        }
        sb.AppendLine(string.Format("  手骨原点（腕）世界坐标 = {0:F4}", hand.position.ToString("F4")));
        sb.AppendLine(string.Format("  剑首端沿手骨轴的位置   = {0:F4} m（负值 = 在腕后方）", lowest));
        // 到前臂轴线的垂距
        Vector3 ab = hand.position - elbow.position;
        float L2 = ab.sqrMagnitude;
        float t = L2 > 1e-9f ? Mathf.Clamp01(Vector3.Dot(lowestP - elbow.position, ab) / L2) : 0f;
        float dist = Vector3.Distance(lowestP, elbow.position + ab * t);
        sb.AppendLine(string.Format("  剑首端到前臂轴线的垂距 = {0:F4} m", dist));
        sb.AppendLine(string.Format("  参考：上一步扫描实测的前臂半径 0.0573 m → {0}",
            dist < 0.0573f ? "剑首在前臂内部（被宽袖遮住，视觉不可见）" : "剑首已在前臂之外"));

        // 全网格穿插统计（用前臂胶囊，半径取实测值）
        var m2 = weapon.localToWorldMatrix;
        var verts = wmf.sharedMesh.vertices;
        int inside = 0; float maxPen = -999f;
        for (int i = 0; i < verts.Length; i++)
        {
            Vector3 w = m2.MultiplyPoint3x4(verts[i]);
            float tt = L2 > 1e-9f ? Mathf.Clamp01(Vector3.Dot(w - elbow.position, ab) / L2) : 0f;
            float pen = 0.0573f - Vector3.Distance(w, elbow.position + ab * tt);
            if (pen > 0f) { inside++; if (pen > maxPen) maxPen = pen; }
        }
        sb.AppendLine(string.Format("  剑顶点落在前臂胶囊内 = {0} / {1} 个，最大穿入 {2:F4} m", inside, verts.Length, maxPen));
    }
    Debug.Log(sb.ToString());

    // ---- 相机 ----
    var camGo = new GameObject("TmpSwordPlayCam");
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
        "Atk1|0.38|sword_atk1|45|body",
        "Atk1Rec|0.50|sword_hand_atk1rec|45|hand",
        "Atk2|0.45|sword_atk2|45|body",
        "Atk2Rec|0.50|sword_hand_atk2rec|45|hand",
        "Atk3|0.55|sword_atk3|45|body",
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
    Debug.Log("[w_shot_play] done -> " + imgDir);
    yield return null;
}

string FullPath(Transform t, Transform root)
{
    var s = t.name;
    while (t.parent != null && t.parent != root) { t = t.parent; s = t.name + "/" + s; }
    return s;
}

return Body();
