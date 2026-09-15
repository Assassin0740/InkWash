// q_socket.cs —— 求「背部挂点（BackSocket）」在骨架局部的位姿，并渲染确认
// 目标（与 Tools/screenshots/stand_back/S2_斜跨背40度 一致）：
//   剑尖方向 = down + 0.55·right（右肩柄 → 左腰尖）
//   刃厚法向 = back（宽面贴背）
//   位置     = Chest + back·0.14 − up·0.03
// 武器预制体约定：根节点局部 +Y = 指向剑尖，根节点局部 −Z = 刃厚法向。
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

IEnumerator Body()
{
    var sb = new System.Text.StringBuilder();
    string projRoot = System.IO.Path.GetDirectoryName(Application.dataPath);
    string imgDir = System.IO.Path.Combine(projRoot, "Tools/screenshots/stand_socket");
    System.IO.Directory.CreateDirectory(imgDir);

    yield return null;
    yield return null;

    var go = GameObject.Find("Player");
    if (go == null) { Debug.LogError("no Player"); yield break; }
    var anim = go.GetComponent<Animator>();
    var ctl = go.GetComponent<InkWash.Player.PlayerController>();
    var ik = go.GetComponent<InkWash.Player.FootIK>();
    var pose = go.GetComponentInChildren<InkWash.Effects.WeaponHandPose>(true);
    if (ctl != null) ctl.enabled = false;
    if (ik != null) ik.enabled = false;

    var vfx = go.GetComponentInChildren<InkWash.Effects.SwordVfx>(true);
    Transform weaponRoot = vfx != null && vfx.WeaponInstance != null ? vfx.WeaponInstance.transform : null;
    if (weaponRoot == null)
    {
        // 退化路径：直接找大网格
        foreach (var f in go.GetComponentsInChildren<MeshFilter>(true))
            if (f.sharedMesh != null && f.sharedMesh.vertexCount > 20000) { weaponRoot = f.transform; break; }
    }
    if (weaponRoot == null) { Debug.LogError("no weapon"); yield break; }
    sb.AppendLine("武器实例: " + weaponRoot.name + "  父=" + (weaponRoot.parent != null ? weaponRoot.parent.name : "-"));

    var chest = anim.GetBoneTransform(HumanBodyBones.UpperChest);
    if (chest == null) chest = anim.GetBoneTransform(HumanBodyBones.Chest);
    sb.AppendLine("Chest 骨骼: " + (chest != null ? chest.name : "null"));
    if (chest == null) { Debug.LogError("no chest bone"); yield break; }

    // ---- 1. 先把姿态固定到「垂手待机」 ----
    AnimationClip ual1 = Clip("Assets/ThirdParty/Quaternius/UniversalAnimationLibrary/Unity/AnimationLibrary_Unity_Standard.fbx", "Rig|Idle_Loop");
    var original = anim.runtimeAnimatorController;
    var ovr = new AnimatorOverrideController(original);
    var pairs = new List<KeyValuePair<AnimationClip, AnimationClip>>();
    ovr.GetOverrides(pairs);
    for (int i = 0; i < pairs.Count; i++)
        if (pairs[i].Key != null && pairs[i].Key.name == "Idle")
            pairs[i] = new KeyValuePair<AnimationClip, AnimationClip>(pairs[i].Key, ual1);
    ovr.ApplyOverrides(pairs);
    anim.runtimeAnimatorController = ovr;
    anim.Play("Idle", 0, 0f);
    anim.Update(1f / 60f);
    anim.Play("Idle", 0, 0.35f);
    anim.Update(1f / 60f);
    yield return null;
    yield return null;
    if (pose != null) pose.enabled = false;   // 垂手不需要握拳

    sb.AppendLine("姿态片段: " + (anim.GetCurrentAnimatorClipInfo(0).Length > 0 ? anim.GetCurrentAnimatorClipInfo(0)[0].clip.name : "<空>"));
    sb.AppendLine();

    // ---- 2. 求目标世界位姿 → 换算成 Chest 下的局部 ----
    Vector3 fwd = go.transform.forward; fwd.y = 0f; fwd.Normalize();
    Vector3 right = go.transform.right; right.y = 0f; right.Normalize();
    Vector3 back = -fwd;

    Vector3 bladeDir = (Vector3.down + right * 0.55f).normalized;   // 剑尖方向
    Vector3 flatDir = back;                                          // 刃厚法向（宽面贴背）
    Vector3 targetPos = chest.position + back * 0.14f - Vector3.up * 0.03f;

    // 自动探明：把「网格节点的 −Y（剑尖）/ +Z（刃厚）」换算到【武器根局部空间】，
    // 不假设根局部哪根轴朝剑尖 —— Align 上还留着 FBX 的原生旋转（实测把 −Y 映射到 +Z），
    // 凭记忆写轴必错。
    var probeMf = weaponRoot.GetComponentInChildren<MeshFilter>();
    Transform meshT = probeMf.transform;
    Vector3 bladeLocalInRoot = weaponRoot.InverseTransformDirection(meshT.TransformDirection(Vector3.down)).normalized;
    Vector3 thickLocalInRoot = weaponRoot.InverseTransformDirection(meshT.TransformDirection(Vector3.forward)).normalized;
    sb.AppendLine("轴向探明(武器根局部空间): 剑尖=" + bladeLocalInRoot.ToString("F4") + "  刃厚=" + thickLocalInRoot.ToString("F4"));
    if (meshT.parent != null)
        sb.AppendLine("  父节点 " + meshT.parent.name + " localEuler=" + meshT.parent.localEulerAngles.ToString("F2")
                      + " localScale=" + meshT.parent.localScale.ToString("F3"));

    Quaternion targetRot = Quaternion.FromToRotation(bladeLocalInRoot, bladeDir);
    Vector3 thickW0 = targetRot * thickLocalInRoot;
    float roll0 = Vector3.SignedAngle(thickW0, flatDir, bladeDir);
    targetRot = Quaternion.AngleAxis(roll0, bladeDir) * targetRot;
    sb.AppendLine("  滚转修正=" + roll0.ToString("F2") + "°");

    sb.AppendLine("目标(世界): pos=" + targetPos.ToString("F4") + " rot(euler)=" + targetRot.eulerAngles.ToString("F2"));
    sb.AppendLine("  剑尖方向=" + bladeDir.ToString("F4") + "  刃厚法向=" + flatDir.ToString("F4"));
    sb.AppendLine("  chest.position=" + chest.position.ToString("F4"));
    sb.AppendLine("  go.forward=" + fwd.ToString("F4") + "  go.right=" + right.ToString("F4"));
    sb.AppendLine();

    var socket = new GameObject("BackSocket").transform;
    socket.SetParent(chest, false);
    socket.position = targetPos;
    socket.rotation = targetRot;
    socket.localScale = Vector3.one;
    // ---- 3. 真把武器挂上去，验证与 S2 预演一致 ----
    Vector3 op = weaponRoot.localPosition; Quaternion oq = weaponRoot.localRotation; Vector3 os = weaponRoot.localScale;
    Transform origParent = weaponRoot.parent;
    weaponRoot.SetParent(socket, false);
    weaponRoot.localPosition = Vector3.zero;
    weaponRoot.localRotation = Quaternion.identity;
    weaponRoot.localScale = Vector3.one;
    yield return null;

    // 位置锚点与预演图（stand_back 的 S2）对齐：把【网格中心】放到 targetPos，
    // 而不是把武器根原点放到 targetPos —— 两者差约 0.47 m，直接摆根会让整把剑明显偏低。
    var centerMf = weaponRoot.GetComponentInChildren<MeshFilter>();
    Vector3 delta = targetPos - centerMf.transform.position;
    socket.position += delta;
    yield return null;
    sb.AppendLine(string.Format("位置修正(网格中心 → 目标): delta={0}  修正后网格中心={1}  目标={2}",
        delta.ToString("F4"), centerMf.transform.position.ToString("F4"), targetPos.ToString("F4")));
    sb.AppendLine();

    sb.AppendLine("========== 写进 prefab 的常量（已含位置修正）==========");
    sb.AppendLine("  BACK_SOCKET_LOCAL_POS    = new Vector3(" + socket.localPosition.x.ToString("F6") + "f, " + socket.localPosition.y.ToString("F6") + "f, " + socket.localPosition.z.ToString("F6") + "f);");
    sb.AppendLine("  BACK_SOCKET_LOCAL_EULER  = new Vector3(" + socket.localEulerAngles.x.ToString("F4") + "f, " + socket.localEulerAngles.y.ToString("F4") + "f, " + socket.localEulerAngles.z.ToString("F4") + "f);");
    sb.AppendLine("  BACK_SOCKET_LOCAL_ROT    = new Quaternion(" + socket.localRotation.x.ToString("F6") + "f, " + socket.localRotation.y.ToString("F6") + "f, " + socket.localRotation.z.ToString("F6") + "f, " + socket.localRotation.w.ToString("F6") + "f);");
    sb.AppendLine("  socket.localScale = " + socket.localScale.ToString("F3") + "（应为 1）");
    sb.AppendLine();

    // 用网格 8 角点算世界包围盒（沿剑轴取最远点才是真尖端）
    var mf = weaponRoot.GetComponentInChildren<MeshFilter>();
    var mesh = mf.sharedMesh;
    var b = mesh.bounds;
    Vector3 mn = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
    Vector3 mx = new Vector3(float.MinValue, float.MinValue, float.MinValue);
    Vector3 tipW = Vector3.zero; float tipDot = -1f;
    Matrix4x4 mtx = mf.transform.localToWorldMatrix;
    foreach (var v in mesh.vertices)
    {
        Vector3 w = mtx.MultiplyPoint3x4(v);
        mn = Vector3.Min(mn, w); mx = Vector3.Max(mx, w);
        float d = Vector3.Dot(w, bladeDir);
        if (d > tipDot) { tipDot = d; tipW = w; }
    }
    var mb = mf.transform.TransformDirection(Vector3.down);   // 网格空间 −Y = 剑尖（世界方向）
    sb.AppendLine("挂上后自检:");
    sb.AppendLine("  世界包围盒 min=" + mn.ToString("F4") + " max=" + mx.ToString("F4"));
    sb.AppendLine("  沿剑轴最远点(剑尖)=" + tipW.ToString("F4") + "  高度=" + tipW.y.ToString("F4"));
    sb.AppendLine("  网格 −Y 世界方向=" + mb.ToString("F4") + "（应与剑尖方向一致）");
    sb.AppendLine("  与该方向夹角=" + Vector3.Angle(mb, bladeDir).ToString("F2") + "°  (期望 ≈ 0°)");
    float dShaft = DistanceToAxis(tipW, socket.position, bladeDir);
    sb.AppendLine("  剑尖在剑轴上? 垂距=" + dShaft.ToString("F4") + "m (期望 ≈ 0)");
    sb.AppendLine("  武器根局部 += " + weaponRoot.localPosition.ToString("F4") + " " + weaponRoot.localEulerAngles.ToString("F2") + " " + weaponRoot.localScale.ToString("F3"));
    sb.AppendLine();

    // ---- 4. 渲染三视图 ----
    var smrs = go.GetComponentsInChildren<SkinnedMeshRenderer>(true);
    var bake = new Mesh();
    var camGo = new GameObject("TmpSocketCam");
    var tcam = camGo.AddComponent<Camera>();
    tcam.CopyFrom(Camera.main);
    tcam.tag = "Untagged"; tcam.enabled = false;
    tcam.clearFlags = CameraClearFlags.SolidColor;
    tcam.backgroundColor = new Color(0.16f, 0.17f, 0.20f);
    var keep = new HashSet<Renderer>(go.GetComponentsInChildren<Renderer>());
    var hidden = new List<Renderer>();
    foreach (var r in Object.FindObjectsOfType<Renderer>())
        if (r.enabled && !keep.Contains(r)) { r.enabled = false; hidden.Add(r); }

    string[] vn = { "front", "side", "back" };
    int TW = 460, TH = 620;
    for (int vi = 0; vi < 3; vi++)
    {
        Vector3 viewDir = vi == 0 ? fwd : (vi == 1 ? right : back);
        float mnY = float.MaxValue, mxY = float.MinValue;
        foreach (var s in smrs)
        {
            s.BakeMesh(bake, true);
            var m = s.transform.localToWorldMatrix;
            foreach (var v in bake.vertices) { float y = m.MultiplyPoint3x4(v).y; if (y < mnY) mnY = y; if (y > mxY) mxY = y; }
        }
        Vector3 center = new Vector3(go.transform.position.x, (mnY + mxY) * 0.5f, go.transform.position.z);
        float hh = Mathf.Max(1f, mxY - mnY);
        tcam.orthographic = false; tcam.fieldOfView = 36f;
        Vector3 cp = center + viewDir * (hh * 2.15f) + Vector3.up * (hh * 0.04f);
        tcam.transform.position = cp;
        tcam.transform.rotation = Quaternion.LookRotation(center - cp, Vector3.up);

        var rt = RenderTexture.GetTemporary(TW, TH, 24);
        tcam.targetTexture = rt; tcam.Render(); tcam.targetTexture = null;
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var tile = new Texture2D(TW, TH, TextureFormat.RGB24, false);
        tile.ReadPixels(new Rect(0, 0, TW, TH), 0, 0); tile.Apply();
        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(rt);
        System.IO.File.WriteAllBytes(System.IO.Path.Combine(imgDir, "socket_" + vn[vi] + ".png"), tile.EncodeToPNG());
        Object.Destroy(tile);
    }

    // ---- 还原 ----
    weaponRoot.SetParent(origParent, false);
    weaponRoot.localPosition = op; weaponRoot.localRotation = oq; weaponRoot.localScale = os;
    Object.Destroy(socket.gameObject);
    Object.Destroy(bake);
    Object.Destroy(camGo);
    foreach (var r in hidden) if (r != null) r.enabled = true;
    anim.runtimeAnimatorController = original;
    if (ctl != null) ctl.enabled = true;
    if (ik != null) ik.enabled = true;
    if (pose != null) pose.enabled = true;

    System.IO.File.WriteAllText(System.IO.Path.Combine(projRoot, "Tools/reports/q_socket.txt"), sb.ToString());
    Debug.Log("[q_socket] done");
    yield return null;
}

float DistanceToAxis(Vector3 p, Vector3 origin, Vector3 dir)
{
    Vector3 d = p - origin;
    return (d - Vector3.Dot(d, dir) * dir).magnitude;
}

AnimationClip Clip(string path, string subName)
{
    foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path))
    {
        if (!(o is AnimationClip c)) continue;
        if (c.name.StartsWith("__preview__")) continue;
        if (subName == null || c.name == subName) return c;
    }
    return null;
}

return Body();
