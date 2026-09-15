// q_stand2.cs —— 非战斗（平时站立）待机候选对比
// 用户反馈：平时站着应该是「自然双手下垂」，不是持剑架势。
// 渲染 4 个垂手待机候选（剑隐藏），外加 1 张「垂手但剑仍在手上」的反例。
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

IEnumerator Body()
{
    var sb = new System.Text.StringBuilder();
    string projRoot = System.IO.Path.GetDirectoryName(Application.dataPath);
    string imgDir = System.IO.Path.Combine(projRoot, "Tools/screenshots/stand_cand");
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

    Transform weapon = null;
    foreach (var f in go.GetComponentsInChildren<MeshFilter>(true))
        if (f.sharedMesh != null && f.sharedMesh.vertexCount > 20000) { weapon = f.transform; break; }
    if (weapon == null) { Debug.LogError("no weapon"); yield break; }
    var weaponRend = weapon.GetComponent<Renderer>();

    var smrs = go.GetComponentsInChildren<SkinnedMeshRenderer>(true);
    var bake = new Mesh();

    AnimationClip ual1 = Clip("Assets/ThirdParty/Quaternius/UniversalAnimationLibrary/Unity/AnimationLibrary_Unity_Standard.fbx", "Rig|Idle_Loop");
    AnimationClip ual2 = Clip("Assets/ThirdParty/Quaternius/UniversalAnimationLibrary2/Unity/UAL2_Standard.fbx", "Armature|Idle_No_Loop");
    AnimationClip ki01 = Clip("Assets/ThirdParty/KevinIglesias/Animations/Male/Idles/HumanM@Idle01.fbx", "Idle01");
    AnimationClip kayA = Clip("Assets/ThirdParty/KayKit/Animations/Rig_Medium/Rig_Medium_General.fbx", "Idle_A");
    AnimationClip feng = Clip("Assets/Char_Feng/Animation/Idle.anim", "Idle");

    var plans = new List<(string label, AnimationClip clip, bool hideSword)>();
    plans.Add(("A_UAL1_IdleLoop", ual1, true));
    plans.Add(("B_UAL2_IdleNoLoop", ual2, true));
    plans.Add(("C_KI_Idle01", ki01, true));
    plans.Add(("D_KayKit_IdleA", kayA, true));
    plans.Add(("E_反例_垂手但剑在手", ual1, false));
    plans.Add(("F_Feng_持剑待机", feng, false));

    var original = anim.runtimeAnimatorController;

    var camGo = new GameObject("TmpStandCam");
    var tcam = camGo.AddComponent<Camera>();
    tcam.CopyFrom(Camera.main);
    tcam.tag = "Untagged"; tcam.enabled = false;
    tcam.clearFlags = CameraClearFlags.SolidColor;
    tcam.backgroundColor = new Color(0.16f, 0.17f, 0.20f);

    var keep = new HashSet<Renderer>(go.GetComponentsInChildren<Renderer>());
    var hidden = new List<Renderer>();
    foreach (var r in Object.FindObjectsOfType<Renderer>())
        if (r.enabled && !keep.Contains(r)) { r.enabled = false; hidden.Add(r); }

    int TW = 400, TH = 560;
    var sheet = new Texture2D(TW * plans.Count, TH * 2, TextureFormat.RGB24, false);

    for (int vi = 0; vi < 2; vi++)
    {
        for (int pi = 0; pi < plans.Count; pi++)
        {
            var (label, clip, hideSword) = plans[pi];

            var ovr = new AnimatorOverrideController(original);
            var pairs = new List<KeyValuePair<AnimationClip, AnimationClip>>();
            ovr.GetOverrides(pairs);
            for (int i = 0; i < pairs.Count; i++)
                if (pairs[i].Key != null && pairs[i].Key.name == "Idle")
                    pairs[i] = new KeyValuePair<AnimationClip, AnimationClip>(pairs[i].Key, clip);
            ovr.ApplyOverrides(pairs);
            anim.runtimeAnimatorController = ovr;

            anim.Play("Idle", 0, 0f);
            anim.Update(1f / 60f);
            anim.Play("Idle", 0, 0.35f);
            anim.Update(1f / 60f);
            yield return null;
            yield return null;

            if (weaponRend != null) weaponRend.enabled = !hideSword;

            Vector3 blade = weapon.TransformDirection(Vector3.up).normalized;
            Vector3 fwd = go.transform.forward; fwd.y = 0f; fwd.Normalize();
            var cur = anim.GetCurrentAnimatorClipInfo(0);
            sb.AppendLine(string.Format("{0,-24} 实际={1,-18} 剑与向下={2,6:F1}° 剑与前方={3,6:F1}° 剑显示={4}",
                label, (cur.Length > 0 ? cur[0].clip.name : "<空>"),
                Vector3.Angle(blade, Vector3.down), Vector3.Angle(blade, fwd), !hideSword));

            Vector3 viewDir = vi == 0 ? fwd : Vector3.Cross(Vector3.up, fwd).normalized;
            if (vi == 1) viewDir = -viewDir;   // 侧面从角色右侧看

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
            Vector3 cp = center + viewDir * (hh * 2.05f) + Vector3.up * (hh * 0.05f);
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

            if (weaponRend != null) weaponRend.enabled = true;
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
    System.IO.File.WriteAllText(System.IO.Path.Combine(projRoot, "Tools/reports/q_stand2.txt"), sb.ToString());
    Debug.Log("[q_stand2] done");
    yield return null;
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
