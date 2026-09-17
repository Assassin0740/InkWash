// 最终对比：旧 KayKit 攻击（Q 版，左臂僵直横伸） vs 新 UAL2 剑术（左臂收拢、有弓步动势）
//   每段取 4 个归一化时刻连拍，视角 = 角色背后（用户截图视角）
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

IEnumerator Body()
{
    var sb = new System.Text.StringBuilder();
    string projRoot = System.IO.Path.GetDirectoryName(Application.dataPath);
    string imgDir = System.IO.Path.Combine(projRoot, "Tools/screenshots/atk_cmp");
    System.IO.Directory.CreateDirectory(imgDir);

    yield return null; yield return null;

    var go = GameObject.Find("Player");
    if (go == null) { Debug.LogError("no Player"); yield break; }
    var anim = go.GetComponent<Animator>();
    var ctl = go.GetComponent<InkWash.Player.PlayerController>();
    if (ctl != null) ctl.enabled = false;
    var stance = go.GetComponentInChildren<InkWash.Player.CombatStance>(true);
    if (stance != null) stance.ForceStance(true);
    yield return null;

    string kkM = "Assets/ThirdParty/KayKit/Animations/Rig_Medium/Rig_Medium_CombatMelee.fbx";
    string ual2 = "Assets/ThirdParty/Quaternius/UniversalAnimationLibrary2/Unity/UAL2_Standard.fbx";
    AnimationClip Clip(string path, string name)
    {
        foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path))
            if (o is AnimationClip c && !c.name.StartsWith("__preview__") && c.name == name) return c;
        return null;
    }

    // 行顺序：旧1 新1 旧2 新2 旧3 新3
    var rows = new List<(string label, AnimationClip clip)>
    {
        ("旧_Atk1_KayKit_Diagonal",   Clip(kkM, "Melee_1H_Attack_Slice_Diagonal")),
        ("新_Atk1_UAL2_RegularA",     Clip(ual2, "Armature|Sword_Regular_A")),
        ("旧_Atk2_KayKit_Horizontal", Clip(kkM, "Melee_1H_Attack_Slice_Horizontal")),
        ("新_Atk2_UAL2_RegularB",     Clip(ual2, "Armature|Sword_Regular_B")),
        ("旧_Atk3_KayKit_Stab",       Clip(kkM, "Melee_1H_Attack_Stab")),
        ("新_Atk3_UAL2_RegularC",     Clip(ual2, "Armature|Sword_Regular_C")),
    };

    var original = anim.runtimeAnimatorController;
    var baseCtl = original;
    if (original is AnimatorOverrideController o0 && o0.runtimeAnimatorController != null)
        baseCtl = o0.runtimeAnimatorController;

    var camGo = new GameObject("TmpAtkCmpCam");
    var tcam = camGo.AddComponent<Camera>();
    tcam.CopyFrom(Camera.main);
    tcam.tag = "Untagged"; tcam.enabled = false;
    tcam.clearFlags = CameraClearFlags.SolidColor;
    tcam.backgroundColor = new Color(0.15f, 0.16f, 0.19f);

    var keep = new HashSet<Renderer>(go.GetComponentsInChildren<Renderer>());
    var hidden = new List<Renderer>();
    foreach (var r in Object.FindObjectsOfType<Renderer>())
        if (r.enabled && !keep.Contains(r)) { r.enabled = false; hidden.Add(r); }

    int TW = 300, TH = 360, COLS = 4;
    var sheet = new Texture2D(TW * COLS, TH * rows.Count, TextureFormat.RGB24, false);
    float[] nts = { 0.15f, 0.40f, 0.65f, 0.90f };

    for (int ri = 0; ri < rows.Count; ri++)
    {
        var (label, clip) = rows[ri];
        if (clip == null) { sb.AppendLine("!! 缺 " + label); continue; }

        var ovr = new AnimatorOverrideController(baseCtl);
        var pairs = new List<KeyValuePair<AnimationClip, AnimationClip>>();
        ovr.GetOverrides(pairs);
        for (int i = 0; i < pairs.Count; i++)
            if (pairs[i].Key != null && pairs[i].Key.name == "Armature|Sword_Regular_A")
                pairs[i] = new KeyValuePair<AnimationClip, AnimationClip>(pairs[i].Key, clip);
        ovr.ApplyOverrides(pairs);
        anim.runtimeAnimatorController = ovr;

        sb.Append(label + "  (" + clip.length.ToString("F2") + "s)");
        for (int c = 0; c < COLS; c++)
        {
            anim.Play("Atk1", 0, nts[c]);
            anim.Update(0f); anim.Update(0f);
            yield return null;

            Vector3 fwd = go.transform.forward; fwd.y = 0f; fwd.Normalize();
            Vector3 viewDir = -fwd;
            Vector3 center = new Vector3(go.transform.position.x, go.transform.position.y + 1.1f, go.transform.position.z);
            tcam.orthographic = false; tcam.fieldOfView = 44f;
            Vector3 cp = center + viewDir * 4.6f + Vector3.up * 0.1f;
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

            sheet.SetPixels(c * TW, (rows.Count - 1 - ri) * TH, TW, TH, tile.GetPixels());
            Object.Destroy(tile);
        }
        sb.AppendLine();
    }

    sheet.Apply();
    System.IO.File.WriteAllBytes(System.IO.Path.Combine(imgDir, "_sheet.png"), sheet.EncodeToPNG());
    Object.Destroy(sheet);

    foreach (var r in hidden) if (r != null) r.enabled = true;
    Object.Destroy(camGo);
    anim.runtimeAnimatorController = original;
    if (ctl != null) ctl.enabled = true;

    System.IO.File.WriteAllText(System.IO.Path.Combine(projRoot, "Tools/reports/q_atkcmp.txt"), sb.ToString());
    Debug.Log("[q_atkcmp] done");
    yield return null;
}
return Body();
