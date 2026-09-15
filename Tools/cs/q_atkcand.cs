// 攻击片段候选对比：量化「手臂张开度」，并出图（背后视角 = 用户抱怨的那个视角）
//   判据：左大臂方向与角色「竖直向下」的夹角 —— 垂手≈0°、水平外伸≈90°
//   另记 |局部 x| 峰值，用来区分「横向张开」还是「前后摆动」
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

IEnumerator Body()
{
    var sb = new System.Text.StringBuilder();
    string projRoot = System.IO.Path.GetDirectoryName(Application.dataPath);
    string imgDir = System.IO.Path.Combine(projRoot, "Tools/screenshots/atk_cand");
    System.IO.Directory.CreateDirectory(imgDir);

    yield return null;
    yield return null;

    var go = GameObject.Find("Player");
    if (go == null) { Debug.LogError("no Player"); yield break; }
    var anim = go.GetComponent<Animator>();
    if (anim == null) { Debug.LogError("no Animator"); yield break; }

    var ctl = go.GetComponent<InkWash.Player.PlayerController>();
    var ik = go.GetComponent<InkWash.Player.FootIK>();
    var pose = go.GetComponentInChildren<InkWash.Effects.WeaponHandPose>(true);
    if (ctl != null) ctl.enabled = false;
    if (ik != null) ik.enabled = false;

    var stance = go.GetComponentInChildren<InkWash.Player.CombatStance>(true);
    if (stance != null) stance.ForceStance(true);          // 手里有剑才看得真切
    yield return null;

    var lU = anim.GetBoneTransform(HumanBodyBones.LeftUpperArm);
    var lL = anim.GetBoneTransform(HumanBodyBones.LeftLowerArm);
    var rU = anim.GetBoneTransform(HumanBodyBones.RightUpperArm);
    var rL = anim.GetBoneTransform(HumanBodyBones.RightLowerArm);
    if (lU == null || lL == null || rU == null || rL == null) { Debug.LogError("no bones"); yield break; }

    string kkM = "Assets/ThirdParty/KayKit/Animations/Rig_Medium/Rig_Medium_CombatMelee.fbx";
    string ual2 = "Assets/ThirdParty/Quaternius/UniversalAnimationLibrary2/Unity/UAL2_Standard.fbx";
    string ual1 = "Assets/ThirdParty/Quaternius/UniversalAnimationLibrary/Unity/AnimationLibrary_Unity_Standard.fbx";

    var plans = new List<(string label, AnimationClip clip, string src)>();
    void Add(string label, string path, string clip)
    {
        plans.Add((label, Clip(path, clip), clip));
    }

    // 现状（KayKit Q 版）
    Add("K1_Diag_现状Atk1", kkM, "Melee_1H_Attack_Slice_Diagonal");
    Add("K2_Horiz_现状Atk2", kkM, "Melee_1H_Attack_Slice_Horizontal");
    Add("K3_Stab_现状Atk3", kkM, "Melee_1H_Attack_Stab");
    // 候选（Quaternius UAL2 剑术库，CC0）
    Add("Q_SwordRegularA", ual2, "Armature|Sword_Regular_A");
    Add("Q_SwordRegularB", ual2, "Armature|Sword_Regular_B");
    Add("Q_SwordRegularC", ual2, "Armature|Sword_Regular_C");
    Add("Q_SwordRegularCombo", ual2, "Armature|Sword_Regular_Combo");
    Add("Q_SwordHeavyCombo", ual2, "Armature|Sword_Heavy_Combo");
    Add("Q_SwordDash", ual2, "Armature|Sword_Dash");
    Add("Q1_SwordAttack", ual1, "Rig|Sword_Attack");

    string stateName = "Atk1";                       // 借 Atk1 这个状态位来播候选
    string coverKey = "Melee_1H_Attack_Slice_Diagonal";  // Atk1 状态原本引用的片段

    var original = anim.runtimeAnimatorController;
    var baseCtl = original;
    if (original is AnimatorOverrideController or0 && or0.runtimeAnimatorController != null)
        baseCtl = or0.runtimeAnimatorController;

    // ---------- ① 度量 ----------
    var peaks = new float[plans.Count];
    var peakClips = new AnimationClip[plans.Count];

    sb.AppendLine("════ 攻击片段候选：手臂张开度（俯视判据 = 左大臂与「竖直向下」夹角）════");
    sb.AppendLine("   垂手≈0~20° ｜ 斜下≈40~55° ｜ 水平外伸≈80~95°（用户截图那种）");
    sb.AppendLine();
    sb.AppendLine("  候选                        时长   左臂峰值   峰值@nt   左臂>55°占比   左臂横向峰值|local.x|  右臂峰值");

    for (int pi = 0; pi < plans.Count; pi++)
    {
        var (label, clip, src) = plans[pi];
        if (clip == null) { sb.AppendLine("  !! 缺片段: " + label + " <" + src + ">"); continue; }

        var ovr = new AnimatorOverrideController(baseCtl);
        var pairs = new List<KeyValuePair<AnimationClip, AnimationClip>>();
        ovr.GetOverrides(pairs);
        bool hit = false;
        for (int i = 0; i < pairs.Count; i++)
            if (pairs[i].Key != null && pairs[i].Key.name == coverKey)
            {
                pairs[i] = new KeyValuePair<AnimationClip, AnimationClip>(pairs[i].Key, clip);
                hit = true;
            }
        if (!hit) { sb.AppendLine("  !! 覆盖键未找到 " + coverKey); break; }
        ovr.ApplyOverrides(pairs);
        anim.runtimeAnimatorController = ovr;

        int N = Mathf.Max(30, Mathf.RoundToInt(clip.length * 60f));
        float leftMax = 0f, rightMax = 0f, leftXMax = 0f, peakNt = 0f;
        int over = 0;

        for (int i = 0; i <= N; i++)
        {
            float nt = i / (float)N;
            anim.Play(stateName, 0, nt);
            anim.Update(0f);
            anim.Update(0f);

            Vector3 ld = (lL.position - lU.position).normalized;
            Vector3 rd = (rL.position - rU.position).normalized;
            float la = Vector3.Angle(ld, -go.transform.up);
            float ra = Vector3.Angle(rd, -go.transform.up);
            Vector3 ll = go.transform.InverseTransformDirection(ld);

            if (la > leftMax) { leftMax = la; peakNt = nt; }
            rightMax = Mathf.Max(rightMax, ra);
            leftXMax = Mathf.Max(leftXMax, Mathf.Abs(ll.x));
            if (la > 55f) over++;
        }

        peaks[pi] = peakNt; peakClips[pi] = clip;

        sb.AppendLine("  " + label.PadRight(26)
            + clip.length.ToString("F2") + "s"
            + "   " + leftMax.ToString("F1").PadLeft(6) + "°"
            + "   " + peakNt.ToString("F2").PadLeft(6)
            + "   " + (100f * over / (N + 1)).ToString("F0").PadLeft(6) + "%"
            + "        " + leftXMax.ToString("F3").PadLeft(7)
            + "          " + rightMax.ToString("F1").PadLeft(6) + "°");
    }

    // ---------- ② 出图（背后视角，各候选外展峰值帧）----------
    sb.AppendLine();
    sb.AppendLine("出图目录 " + imgDir + "（每个候选一张「外展峰值帧」，视角 = 角色背后 = 用户截图视角）");

    var camGo = new GameObject("TmpAtkCandCam");
    var tcam = camGo.AddComponent<Camera>();
    tcam.CopyFrom(Camera.main);
    tcam.tag = "Untagged"; tcam.enabled = false;
    tcam.clearFlags = CameraClearFlags.SolidColor;
    tcam.backgroundColor = new Color(0.16f, 0.17f, 0.20f);

    var keep = new HashSet<Renderer>(go.GetComponentsInChildren<Renderer>());
    var hidden = new List<Renderer>();
    foreach (var r in Object.FindObjectsOfType<Renderer>())
        if (r.enabled && !keep.Contains(r)) { r.enabled = false; hidden.Add(r); }

    int TW = 440, TH = 560, COLS = 4;
    int rows = Mathf.CeilToInt(plans.Count / (float)COLS);
    var sheet = new Texture2D(TW * COLS, TH * rows, TextureFormat.RGB24, false);

    for (int pi = 0; pi < plans.Count; pi++)
    {
        var (label, clip, src) = plans[pi];
        if (clip == null) continue;

        var ovr = new AnimatorOverrideController(baseCtl);
        var pairs = new List<KeyValuePair<AnimationClip, AnimationClip>>();
        ovr.GetOverrides(pairs);
        for (int i = 0; i < pairs.Count; i++)
            if (pairs[i].Key != null && pairs[i].Key.name == coverKey)
                pairs[i] = new KeyValuePair<AnimationClip, AnimationClip>(pairs[i].Key, clip);
        ovr.ApplyOverrides(pairs);
        anim.runtimeAnimatorController = ovr;

        anim.Play(stateName, 0, peaks[pi]);
        anim.Update(0f);
        anim.Update(0f);
        yield return null;

        Vector3 fwd = go.transform.forward; fwd.y = 0f; fwd.Normalize();
        Vector3 viewDir = -fwd;                       // 背后

        Vector3 center = new Vector3(go.transform.position.x,
                                     go.transform.position.y + 1.2f, go.transform.position.z);
        tcam.orthographic = false; tcam.fieldOfView = 40f;
        Vector3 cp = center + viewDir * 4.2f + Vector3.up * 0.15f;
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

        int col = pi % COLS, row = pi / COLS;
        sheet.SetPixels(col * TW, (rows - 1 - row) * TH, TW, TH, tile.GetPixels());
        System.IO.File.WriteAllBytes(System.IO.Path.Combine(imgDir, label + ".png"), tile.EncodeToPNG());
        Object.Destroy(tile);
    }

    sheet.Apply();
    System.IO.File.WriteAllBytes(System.IO.Path.Combine(imgDir, "_sheet_peak.png"), sheet.EncodeToPNG());
    Object.Destroy(sheet);

    foreach (var r in hidden) if (r != null) r.enabled = true;
    Object.Destroy(camGo);
    anim.runtimeAnimatorController = original;
    if (ctl != null) ctl.enabled = true;
    if (ik != null) ik.enabled = true;
    if (stance != null) stance.ForceStance(true);

    System.IO.File.WriteAllText(System.IO.Path.Combine(projRoot, "Tools/reports/q_atkcand.txt"), sb.ToString());
    Debug.Log("[q_atkcand] done");
    yield return null;
}

AnimationClip Clip(string path, string subName)
{
    foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path))
    {
        if (!(o is AnimationClip c)) continue;
        if (c.name.StartsWith("__preview__")) continue;
        if (c.name == subName) return c;
    }
    return null;
}

return Body();
