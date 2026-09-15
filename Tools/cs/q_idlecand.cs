// 待机片段候选对比：把候选 idle 逐一套到角色上，量剑的朝向 + 出对比图。
//
// 两个假设要验：
//   A. 现在的 Idle 是 KayKit 的 Idle_A（Q 版立正待机），换成 Feng 自带 Idle / KI Idle01 会好很多。
//   B. 剑横着往后戳，根因是**动画是徒手做的** —— 手腕没有为握剑做旋前/旋后。
//      若 B 成立，绕**前臂轴**把手腕转一个角度就能把剑拉回朝下，且不破坏手与臂的关系。
//
// 手法：AnimatorOverrideController 把 Idle 状态的片段临时替换掉（**不改资产**），
//       逐候选渲染，最后还原。
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

IEnumerator Body()
{
    var sb = new System.Text.StringBuilder();
    string projRoot = System.IO.Path.GetDirectoryName(Application.dataPath);
    string imgDir = System.IO.Path.Combine(projRoot, "Tools/screenshots/idle_cand");
    System.IO.Directory.CreateDirectory(imgDir);

    yield return null;
    yield return null;

    var go = GameObject.Find("Player");
    if (go == null) { Debug.LogError("no Player"); yield break; }
    var anim = go.GetComponent<Animator>();
    var ctl = go.GetComponent<InkWash.Player.PlayerController>();
    var ik = go.GetComponent<InkWash.Player.FootIK>();
    if (ctl != null) ctl.enabled = false;
    if (ik != null) ik.enabled = false;

    var hand = anim.GetBoneTransform(HumanBodyBones.RightHand);
    var elbow = anim.GetBoneTransform(HumanBodyBones.RightLowerArm);

    Transform weapon = null; MeshFilter wmf = null;
    foreach (var f in go.GetComponentsInChildren<MeshFilter>(true))
        if (f.sharedMesh != null && f.sharedMesh.vertexCount > 20000) { weapon = f.transform; wmf = f; break; }
    if (weapon == null) { Debug.LogError("[WARN] 找不到武器网格"); yield break; }

    var smrs = go.GetComponentsInChildren<SkinnedMeshRenderer>(true);
    var bake = new Mesh();

    // ---------- 候选 ----------
    var cands = new List<(string label, AnimationClip clip)>();
    cands.Add(("KayKit_Idle_A(现状)", Clip("Assets/ThirdParty/KayKit/Animations/Rig_Medium/Rig_Medium_General.fbx", "Idle_A")));
    cands.Add(("Feng_Idle", Clip("Assets/Char_Feng/Animation/Idle.anim", null)));
    cands.Add(("KI_Idle01", Clip("Assets/ThirdParty/KevinIglesias/Animations/Male/Idles/HumanM@Idle01.fbx", "Idle01")));
    cands.Add(("KayKit_Melee2H_Idle", Clip("Assets/ThirdParty/KayKit/Animations/Rig_Medium/Rig_Medium_CombatMelee.fbx", "Melee_2H_Idle")));
    cands.Add(("UAL2_Idle_Shield_Loop", Clip("Assets/ThirdParty/Quaternius/UniversalAnimationLibrary2/Unity/UAL2_Standard.fbx", "Armature|Idle_Shield_Loop")));
    cands.Add(("UAL2_Idle_No_Loop", Clip("Assets/ThirdParty/Quaternius/UniversalAnimationLibrary2/Unity/UAL2_Standard.fbx", "Armature|Idle_No_Loop")));

    var valid = new List<(string label, AnimationClip clip)>();
    foreach (var c in cands)
    {
        if (c.clip == null) { sb.AppendLine("[跳过] 取不到片段: " + c.label); continue; }
        sb.AppendLine("[候选] " + c.label + "  clip=" + c.clip.name + "  len=" + c.clip.length.ToString("F2")
                      + "s  human=" + c.clip.isHumanMotion);
        valid.Add(c);
    }
    sb.AppendLine();

    var original = anim.runtimeAnimatorController;

    // ---------- 相机 ----------
    var camGo = new GameObject("TmpIdleCandCam");
    var tcam = camGo.AddComponent<Camera>();
    tcam.CopyFrom(Camera.main);
    tcam.tag = "Untagged"; tcam.enabled = false;
    tcam.clearFlags = CameraClearFlags.SolidColor;
    tcam.backgroundColor = new Color(0.16f, 0.17f, 0.20f);

    var keep = new HashSet<Renderer>(go.GetComponentsInChildren<Renderer>());
    var hidden = new List<Renderer>();
    foreach (var r in Object.FindObjectsOfType<Renderer>())
        if (r.enabled && !keep.Contains(r)) { r.enabled = false; hidden.Add(r); }

    int TW = 320, TH = 400;
    string[] views = { "B", "S", "S_twist" };     // B=背后（复现用户截图角度）, S=右侧, S+腕部扭转
    var sheet = new Texture2D(TW * valid.Count, TH * views.Length, TextureFormat.RGB24, false);

    for (int vi = 0; vi < views.Length; vi++)
    {
        for (int ci = 0; ci < valid.Count; ci++)
        {
            var (label, clip) = valid[ci];

            // ---- 用 OverrideController 临时换掉 Idle 的片段（不改资产）----
            var ovr = new AnimatorOverrideController(original);
            var pairs = new List<KeyValuePair<AnimationClip, AnimationClip>>();
            ovr.GetOverrides(pairs);
            for (int i = 0; i < pairs.Count; i++)
                if (pairs[i].Key != null && pairs[i].Key.name == "Idle_A")
                    pairs[i] = new KeyValuePair<AnimationClip, AnimationClip>(pairs[i].Key, clip);
            ovr.ApplyOverrides(pairs);
            anim.runtimeAnimatorController = ovr;

            anim.Play("Idle", 0, 0f);
            anim.Update(1f / 60f);
            anim.Play("Idle", 0, 0.35f);
            anim.Update(1f / 60f);
            yield return null;
            yield return null;

            // 校验：真的换上去了吗
            var cur = anim.GetCurrentAnimatorClipInfo(0);

            // ---- 量剑的朝向 ----
            Vector3 blade = weapon.TransformDirection(Vector3.down).normalized;
            Vector3 fwd = go.transform.forward; fwd.y = 0f; fwd.Normalize();
            float angDown = Vector3.Angle(blade, Vector3.down);
            float angBack = Vector3.Angle(blade, -fwd);

            // ---- 腕部扭转：绕前臂轴，把剑拉到尽量朝下（旋前/旋后，解剖上成立的 DOF）----
            Quaternion handSaved = hand.rotation;
            float twistDeg = 0f;
            if (views[vi] == "S_twist" && elbow != null)
            {
                Vector3 axis = (hand.position - elbow.position).normalized;
                Vector3 bPerp = Vector3.ProjectOnPlane(blade, axis).normalized;
                Vector3 dPerp = Vector3.ProjectOnPlane(Vector3.down, axis).normalized;
                twistDeg = Vector3.SignedAngle(bPerp, dPerp, axis);
                hand.rotation = Quaternion.AngleAxis(twistDeg, axis) * hand.rotation;
                blade = weapon.TransformDirection(Vector3.down).normalized;
                angDown = Vector3.Angle(blade, Vector3.down);
                angBack = Vector3.Angle(blade, -fwd);
            }

            if (vi == 0)
            {
                sb.AppendLine("── " + label);
                sb.AppendLine("   当前实际播放 = " + (cur.Length > 0 ? cur[0].clip.name : "<空>"));
                sb.AppendLine("   剑身世界方向 = " + blade.ToString("F3")
                              + "  与「竖直向下」夹角 = " + angDown.ToString("F1") + "°"
                              + "  与「正后方」夹角 = " + angBack.ToString("F1") + "°");
            }
            if (views[vi] == "S_twist")
            {
                sb.AppendLine("      [腕部扭转] 需绕前臂轴 " + twistDeg.ToString("F1") + "° → 之后与向下夹角 = " + angDown.ToString("F1") + "°");
            }

            // ---- 渲染 ----
            Vector3 viewDir = views[vi] == "B" ? -fwd : Vector3.Cross(Vector3.up, fwd).normalized;
            float mnY = float.MaxValue, mxY = float.MinValue;
            foreach (var s in smrs)
            {
                s.BakeMesh(bake, true);
                var m = s.transform.localToWorldMatrix;
                foreach (var v in bake.vertices) { float y = m.MultiplyPoint3x4(v).y; if (y < mnY) mnY = y; if (y > mxY) mxY = y; }
            }
            Vector3 center = new Vector3(go.transform.position.x, (mnY + mxY) * 0.5f, go.transform.position.z);
            float hh = Mathf.Max(1f, mxY - mnY);
            tcam.orthographic = false; tcam.fieldOfView = 35f;
            Vector3 cp = center + viewDir * (hh * 1.75f) + Vector3.up * (hh * 0.06f);
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

            sheet.SetPixels(ci * TW, (views.Length - 1 - vi) * TH, TW, TH, tile.GetPixels());
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(imgDir, string.Format("{0}_{1}.png", ci, label)), tile.EncodeToPNG());
            Object.Destroy(tile);

            hand.rotation = handSaved;
            anim.runtimeAnimatorController = original;   // 每个候选后都还原一次，避免叠加
        }
    }

    sheet.Apply();
    System.IO.File.WriteAllBytes(System.IO.Path.Combine(imgDir, "_contact_sheet.png"), sheet.EncodeToPNG());
    Object.Destroy(sheet);

    foreach (var r in hidden) if (r != null) r.enabled = true;
    Object.Destroy(bake);
    Object.Destroy(camGo);
    if (ctl != null) ctl.enabled = true;
    if (ik != null) ik.enabled = true;

    sb.AppendLine();
    sb.AppendLine("对比图（行 = B 背后 / S 右侧 / S+腕部扭转；列 = 候选顺序）→ " + imgDir);
    System.IO.File.WriteAllText(System.IO.Path.Combine(projRoot, "Tools/reports/q_idlecand.txt"), sb.ToString());
    Debug.Log("[q_idlecand] done");
    yield return null;
}

AnimationClip Clip(string path, string subName)
{
    if (subName == null)
    {
        var c0 = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (c0 != null) return c0;
    }
    foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path))
    {
        if (!(o is AnimationClip c)) continue;
        if (c.name.StartsWith("__preview__")) continue;
        if (subName == null || c.name == subName) return c;
    }
    return null;
}

return Body();
