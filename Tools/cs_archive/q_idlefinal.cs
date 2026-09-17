// 待机候选终选：3 个最优方案 × 正面/侧面，大图对比。
//   A = Feng 自带 Idle（同作者同骨架）
//   B = KevinIglesias Idle01（真人比例待机）
//   C = 现状 KayKit Idle_A + 绕前臂轴的手腕扭转（把剑拉回朝下）
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

IEnumerator Body()
{
    var sb = new System.Text.StringBuilder();
    string projRoot = System.IO.Path.GetDirectoryName(Application.dataPath);
    string imgDir = System.IO.Path.Combine(projRoot, "Tools/screenshots/idle_final");
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

    Transform weapon = null;
    foreach (var f in go.GetComponentsInChildren<MeshFilter>(true))
        if (f.sharedMesh != null && f.sharedMesh.vertexCount > 20000) { weapon = f.transform; break; }
    if (weapon == null) { Debug.LogError("no weapon"); yield break; }

    var smrs = go.GetComponentsInChildren<SkinnedMeshRenderer>(true);
    var bake = new Mesh();

    AnimationClip fengIdle = Clip("Assets/Char_Feng/Animation/Idle.anim", "Idle");
    AnimationClip kiIdle = Clip("Assets/ThirdParty/KevinIglesias/Animations/Male/Idles/HumanM@Idle01.fbx", "Idle01");
    AnimationClip kayIdle = Clip("Assets/ThirdParty/KayKit/Animations/Rig_Medium/Rig_Medium_General.fbx", "Idle_A");

    var plans = new List<(string label, AnimationClip clip, bool twist)>();
    plans.Add(("A_Feng_Idle", fengIdle, false));
    plans.Add(("B_KI_Idle01", kiIdle, false));
    plans.Add(("C_现状+腕部扭转", kayIdle, true));

    var original = anim.runtimeAnimatorController;

    var camGo = new GameObject("TmpIdleFinalCam");
    var tcam = camGo.AddComponent<Camera>();
    tcam.CopyFrom(Camera.main);
    tcam.tag = "Untagged"; tcam.enabled = false;
    tcam.clearFlags = CameraClearFlags.SolidColor;
    tcam.backgroundColor = new Color(0.16f, 0.17f, 0.20f);

    var keep = new HashSet<Renderer>(go.GetComponentsInChildren<Renderer>());
    var hidden = new List<Renderer>();
    foreach (var r in Object.FindObjectsOfType<Renderer>())
        if (r.enabled && !keep.Contains(r)) { r.enabled = false; hidden.Add(r); }

    int TW = 480, TH = 600;
    var sheet = new Texture2D(TW * 3, TH * 2, TextureFormat.RGB24, false);

    for (int vi = 0; vi < 2; vi++)          // 0 = 正面, 1 = 右侧
    {
        for (int pi = 0; pi < plans.Count; pi++)
        {
            var (label, clip, twist) = plans[pi];

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

            float twistDeg = 0f;
            if (twist)
            {
                Vector3 axis = (hand.position - elbow.position).normalized;
                Vector3 b = weapon.TransformDirection(Vector3.down).normalized;
                Vector3 bPerp = Vector3.ProjectOnPlane(b, axis).normalized;
                Vector3 dPerp = Vector3.ProjectOnPlane(Vector3.down, axis).normalized;
                twistDeg = Vector3.SignedAngle(bPerp, dPerp, axis);
                hand.rotation = Quaternion.AngleAxis(twistDeg, axis) * hand.rotation;
            }

            Vector3 blade = weapon.TransformDirection(Vector3.down).normalized;
            Vector3 fwd = go.transform.forward; fwd.y = 0f; fwd.Normalize();
            var cur = anim.GetCurrentAnimatorClipInfo(0);
            sb.AppendLine(label + "  实际播放=" + (cur.Length > 0 ? cur[0].clip.name : "<空>")
                          + "  扭转=" + twistDeg.ToString("F1") + "°"
                          + "  剑与向下=" + Vector3.Angle(blade, Vector3.down).ToString("F1") + "°"
                          + "  与前方=" + Vector3.Angle(blade, fwd).ToString("F1") + "°");

            Vector3 viewDir = vi == 0 ? fwd : Vector3.Cross(Vector3.up, fwd).normalized;
            float mnY = float.MaxValue, mxY = float.MinValue;
            foreach (var s in smrs)
            {
                s.BakeMesh(bake, true);
                var m = s.transform.localToWorldMatrix;
                foreach (var v in bake.vertices) { float y = m.MultiplyPoint3x4(v).y; if (y < mnY) mnY = y; if (y > mxY) mxY = y; }
            }
            Vector3 center = new Vector3(go.transform.position.x, (mnY + mxY) * 0.5f, go.transform.position.z);
            float hh = Mathf.Max(1f, mxY - mnY);
            tcam.orthographic = false; tcam.fieldOfView = 34f;
            Vector3 cp = center + viewDir * (hh * 1.85f) + Vector3.up * (hh * 0.05f);
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

            sheet.SetPixels(pi * TW, (1 - vi) * TH, TW, TH, tile.GetPixels());
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(imgDir, string.Format("{0}_{1}.png", label, vi == 0 ? "front" : "side")), tile.EncodeToPNG());
            Object.Destroy(tile);

            anim.runtimeAnimatorController = original;
        }
    }

    sheet.Apply();
    System.IO.File.WriteAllBytes(System.IO.Path.Combine(imgDir, "_sheet.png"), sheet.EncodeToPNG());
    Object.Destroy(sheet);

    foreach (var r in hidden) if (r != null) r.enabled = true;
    Object.Destroy(bake);
    Object.Destroy(camGo);
    if (ctl != null) ctl.enabled = true;
    if (ik != null) ik.enabled = true;
    System.IO.File.WriteAllText(System.IO.Path.Combine(projRoot, "Tools/reports/q_idlefinal.txt"), sb.ToString());
    Debug.Log("[q_idlefinal] done");
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
