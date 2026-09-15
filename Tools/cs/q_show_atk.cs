// 新攻击片段验证：连拍条带（背后视角 = 用户截图视角）+ 左臂外展曲线
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

IEnumerator Body()
{
    var sb = new System.Text.StringBuilder();
    string projRoot = System.IO.Path.GetDirectoryName(Application.dataPath);
    string imgDir = System.IO.Path.Combine(projRoot, "Tools/screenshots/atk_show");
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
    var stance = go.GetComponentInChildren<InkWash.Player.CombatStance>(true);
    if (stance != null) stance.ForceStance(true);
    yield return null;

    var lU = anim.GetBoneTransform(HumanBodyBones.LeftUpperArm);
    var lL = anim.GetBoneTransform(HumanBodyBones.LeftLowerArm);
    var rU = anim.GetBoneTransform(HumanBodyBones.RightUpperArm);
    var rL = anim.GetBoneTransform(HumanBodyBones.RightLowerArm);

    // 找出脚（用 SkinnedMeshRenderer 逐顶点量最低点，判断有没有穿地/悬空）
    var smrs = go.GetComponentsInChildren<SkinnedMeshRenderer>(true);
    var bake = new Mesh();

    var plans = new List<(string label, AnimationClip clip, string coverKey)>();
    AnimationClip C(string p) { return AssetDatabase.LoadAssetAtPath<AnimationClip>(p); }
    plans.Add(("新Atk1_Slash", C("Assets/_Project/Animations/Attacks/Atk1_Slash.anim"), "Melee_1H_Attack_Slice_Diagonal"));
    plans.Add(("新Atk2_Rise", C("Assets/_Project/Animations/Attacks/Atk2_Rise.anim"), "Melee_1H_Attack_Slice_Horizontal"));
    plans.Add(("新Atk3_Heavy", C("Assets/_Project/Animations/Attacks/Atk3_Heavy.anim"), "Melee_1H_Attack_Stab"));

    string[] states = { "Atk1", "Atk2", "Atk3" };

    var original = anim.runtimeAnimatorController;
    var baseCtl = original;
    if (original is AnimatorOverrideController o0 && o0.runtimeAnimatorController != null)
        baseCtl = o0.runtimeAnimatorController;

    var camGo = new GameObject("TmpAtkShowCam");
    var tcam = camGo.AddComponent<Camera>();
    tcam.CopyFrom(Camera.main);
    tcam.tag = "Untagged"; tcam.enabled = false;
    tcam.clearFlags = CameraClearFlags.SolidColor;
    tcam.backgroundColor = new Color(0.16f, 0.17f, 0.20f);

    var keep = new HashSet<Renderer>(go.GetComponentsInChildren<Renderer>());
    var hidden = new List<Renderer>();
    foreach (var r in Object.FindObjectsOfType<Renderer>())
        if (r.enabled && !keep.Contains(r)) { r.enabled = false; hidden.Add(r); }

    int TW = 300, TH = 380, COLS = 8;
    var sheet = new Texture2D(TW * COLS, TH * 3, TextureFormat.RGB24, false);

    for (int pi = 0; pi < plans.Count; pi++)
    {
        var (label, clip, coverKey) = plans[pi];
        if (clip == null) { sb.AppendLine("!! 缺资产 " + label); continue; }

        var ovr = new AnimatorOverrideController(baseCtl);
        var pairs = new List<KeyValuePair<AnimationClip, AnimationClip>>();
        ovr.GetOverrides(pairs);
        bool hit = false;
        for (int i = 0; i < pairs.Count; i++)
            if (pairs[i].Key != null && pairs[i].Key.name == coverKey)
            { pairs[i] = new KeyValuePair<AnimationClip, AnimationClip>(pairs[i].Key, clip); hit = true; }
        if (!hit) { sb.AppendLine("!! 覆盖键未找到 " + coverKey); continue; }
        ovr.ApplyOverrides(pairs);
        anim.runtimeAnimatorController = ovr;

        sb.AppendLine("════ " + label + "  (" + clip.length.ToString("F2") + "s) ════");
        sb.AppendLine("   nt     左臂外展   右臂外展   脚最低点(相对角色原点)");

        int N = Mathf.Max(24, Mathf.RoundToInt(clip.length * 60f));
        int over = 0;
        float lMax = 0f;
        int lastSlot = -1;

        for (int i = 0; i <= N; i++)
        {
            float nt = i / (float)N;
            anim.Play(states[pi], 0, nt);
            anim.Update(0f);
            anim.Update(0f);

            Vector3 ld = (lL.position - lU.position).normalized;
            Vector3 rd = (rL.position - rU.position).normalized;
            float la = Vector3.Angle(ld, -go.transform.up);
            float ra = Vector3.Angle(rd, -go.transform.up);
            if (la > 55f) over++;
            if (la > lMax) lMax = la;

            // 采样：均匀取 COLS 帧打印 + 出图
            int slot = Mathf.Min(COLS - 1, Mathf.FloorToInt(nt * COLS));
            if (slot != lastSlot)
            {
                lastSlot = slot;
                float footMin = float.MaxValue, footMax = float.MinValue, headMax = float.MinValue;
                foreach (var s in smrs)
                {
                    s.BakeMesh(bake, true);
                    var m = s.transform.localToWorldMatrix;
                    foreach (var v in bake.vertices)
                    {
                        Vector3 wp = m.MultiplyPoint3x4(v);
                        if (wp.y < footMin) footMin = wp.y;
                        if (wp.y > headMax) headMax = wp.y;
                    }
                }
                footMax = headMax;
                sb.AppendLine("   " + nt.ToString("F3") + "   " + la.ToString("F1").PadLeft(6) + "°   "
                    + ra.ToString("F1").PadLeft(6) + "°   "
                    + "  " + (footMin - go.transform.position.y).ToString("+0.000;-0.000").PadLeft(7) + "m");

                // 出图
                Vector3 fwd = go.transform.forward; fwd.y = 0f; fwd.Normalize();
                Vector3 viewDir = -fwd;
                Vector3 center = new Vector3(go.transform.position.x, go.transform.position.y + 1.15f, go.transform.position.z);
                tcam.orthographic = false; tcam.fieldOfView = 42f;
                Vector3 cp = center + viewDir * 4.3f + Vector3.up * 0.1f;
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

                sheet.SetPixels(slot * TW, (2 - pi) * TH, TW, TH, tile.GetPixels());
                Object.Destroy(tile);
                yield return null;
            }
        }
        sb.AppendLine("   → 左臂峰值 " + lMax.ToString("F1") + "°   >55°占比 " + (100f * over / (N + 1)).ToString("F0") + "%");
        sb.AppendLine();
    }

    sheet.Apply();
    System.IO.File.WriteAllBytes(System.IO.Path.Combine(imgDir, "_sheet.png"), sheet.EncodeToPNG());
    Object.Destroy(sheet);

    foreach (var r in hidden) if (r != null) r.enabled = true;
    Object.Destroy(camGo);
    Object.Destroy(bake);
    anim.runtimeAnimatorController = original;
    if (ctl != null) ctl.enabled = true;
    if (ik != null) ik.enabled = true;

    System.IO.File.WriteAllText(System.IO.Path.Combine(projRoot, "Tools/reports/q_show_atk.txt"), sb.ToString());
    Debug.Log("[q_show_atk] done");
    yield return null;
}
return Body();
