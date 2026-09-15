// q_walk_show.cs —— 走路候选的「脚位波动」量化 + 侧视连拍
// 判据：原地走路动画的「网格最低点」应长期贴近地面（总有一只脚在支撑）。
//       波动跨度大 → 身体必须跟着上下浮才能贴地 → 走路一颠一颠（廉价感的机器层面来源）。
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

IEnumerator Body()
{
    var sb = new System.Text.StringBuilder();
    string projRoot = System.IO.Path.GetDirectoryName(Application.dataPath);
    string imgDir = System.IO.Path.Combine(projRoot, "Tools/screenshots/walk_cmp");
    System.IO.Directory.CreateDirectory(imgDir);

    yield return null; yield return null;

    var go = GameObject.Find("Player");
    if (go == null) { Debug.LogError("no Player"); yield break; }
    var anim = go.GetComponent<Animator>();
    var ctl = go.GetComponent<InkWash.Player.PlayerController>();
    var ik = go.GetComponent<InkWash.Player.FootIK>();
    var stance = go.GetComponentInChildren<InkWash.Player.CombatStance>(true);
    if (ctl != null) ctl.enabled = false;
    if (ik != null) ik.enabled = false;
    if (stance != null) stance.enabled = false;
    bool animWas = anim.enabled;
    anim.enabled = false;   // SampleAnimation 直接写骨骼，Animator 会覆盖回去

    var lfB = anim.GetBoneTransform(HumanBodyBones.LeftFoot);
    var rfB = anim.GetBoneTransform(HumanBodyBones.RightFoot);
    var smrs = go.GetComponentsInChildren<SkinnedMeshRenderer>(true);
    var bake = new Mesh(); bake.MarkDynamic();

    float groundY = go.transform.position.y;
    {
        var hits = Physics.RaycastAll(go.transform.position + Vector3.up * 3f, Vector3.down, 10f);
        float best = float.MinValue;
        foreach (var h in hits)
        {
            if (h.collider != null && h.collider.transform.IsChildOf(go.transform)) continue;
            if (h.point.y > best) best = h.point.y;
        }
        if (best > float.MinValue) groundY = best;
    }

    AnimationClip Anim(string p) { return AssetDatabase.LoadAssetAtPath<AnimationClip>(p); }
    AnimationClip Sub(string p, string n)
    {
        foreach (var o in AssetDatabase.LoadAllAssetsAtPath(p))
        {
            var c = o as AnimationClip;
            if (c != null && !c.name.StartsWith("__preview__") && (n == null || c.name == n)) return c;
        }
        return null;
    }

    var cands = new List<(string label, AnimationClip clip)>();
    cands.Add(("Feng_Walk", Anim("Assets/Char_Feng/Animation/Walk.anim")));
    cands.Add(("KI_Walk01_Forward", Sub("Assets/ThirdParty/KevinIglesias/Animations/Male/Movement/Walk/HumanM@Walk01_Forward.fbx", null)));
    cands.Add(("KayKit_Walking_A", Sub("Assets/ThirdParty/KayKit/Animations/Rig_Medium/Rig_Medium_MovementBasic.fbx", "Walking_A")));

    System.Func<float> lowest = () =>
    {
        float lo = float.MaxValue;
        for (int k = 0; k < smrs.Length; k++)
        {
            smrs[k].BakeMesh(bake);
            var verts = bake.vertices;
            var m = smrs[k].transform.localToWorldMatrix;
            for (int q = 0; q < verts.Length; q++)
            {
                float y = m.MultiplyPoint3x4(verts[q]).y;
                if (y < lo) lo = y;
            }
        }
        return lo - groundY;
    };

    sb.AppendLine("地面Y = " + groundY.ToString("F3") + "   角色根Y = " + go.transform.position.y.ToString("F3"));
    sb.AppendLine("Visual.localScale = " + go.transform.Find("Visual").localScale.ToString("F3"));
    sb.AppendLine();
    sb.AppendLine("候选                  时长    最低点min  最低点max   跨度  |  左踝min 左踝max  右踝min 右踝max");
    sb.AppendLine("-------------------------------------------------------------------------------------------");

    const int N = 120;
    foreach (var (label, clip) in cands)
    {
        if (clip == null) { sb.AppendLine(label + "   ★ 找不到"); continue; }
        float lo = 9999f, hi = -9999f, l0 = 9999f, l1 = -9999f, r0 = 9999f, r1 = -9999f;
        for (int i = 0; i <= N; i++)
        {
            clip.SampleAnimation(go, clip.length * i / N);
            float v = lowest();
            if (v < lo) lo = v;
            if (v > hi) hi = v;
            float a = lfB != null ? lfB.position.y - groundY : 0f;
            float b = rfB != null ? rfB.position.y - groundY : 0f;
            if (a < l0) l0 = a; if (a > l1) l1 = a;
            if (b < r0) r0 = b; if (b > r1) r1 = b;
        }
        sb.AppendLine(string.Format("{0,-22} {1:F3}s  {2,9:F4}  {3,9:F4}  {4,6:F3}  |  {5,7:F4} {6,7:F4}  {7,7:F4} {8,7:F4}",
            label, clip.length, lo, hi, hi - lo, l0, l1, r0, r1));
    }
    sb.AppendLine();
    sb.AppendLine("判读：跨度越小越好（脚长期贴地）。脚踝行用于区分「脚抬起」还是「整体被抬高」。");

    // ---------- 出图：Feng 与 KI 各一行侧视 8 帧 ----------
    var camGo = new GameObject("TmpWalkCam");
    var tcam = camGo.AddComponent<Camera>();
    tcam.CopyFrom(Camera.main);
    tcam.tag = "Untagged"; tcam.enabled = false;
    tcam.clearFlags = CameraClearFlags.SolidColor;
    tcam.backgroundColor = new Color(0.16f, 0.17f, 0.20f);

    var keep = new HashSet<Renderer>(go.GetComponentsInChildren<Renderer>());
    var hidden = new List<Renderer>();
    foreach (var r in Object.FindObjectsOfType<Renderer>())
        if (r.enabled && !keep.Contains(r)) { r.enabled = false; hidden.Add(r); }

    var show = new List<(string label, AnimationClip clip)>();
    foreach (var c in cands) if (c.clip != null && c.label != "KayKit_Walking_A") show.Add(c);

    int TW = 200, TH = 300, COLS = 8;
    var sheet = new Texture2D(TW * COLS, TH * show.Count, TextureFormat.RGB24, false);

    for (int row = 0; row < show.Count; row++)
    {
        var clip = show[row].clip;
        for (int c = 0; c < COLS; c++)
        {
            float nt = c / (float)(COLS - 1) * 0.999f;
            clip.SampleAnimation(go, clip.length * nt);
            yield return null;

            Vector3 center = new Vector3(go.transform.position.x, go.transform.position.y + 0.95f, go.transform.position.z);
            Vector3 viewDir = go.transform.right;   // 侧视
            tcam.orthographic = true; tcam.orthographicSize = 1.15f;
            Vector3 cp = center + viewDir * 5.0f;
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

            sheet.SetPixels(c * TW, (show.Count - 1 - row) * TH, TW, TH, tile.GetPixels());
            Object.Destroy(tile);
        }
    }

    sheet.Apply();
    System.IO.File.WriteAllBytes(System.IO.Path.Combine(imgDir, "_sheet.png"), sheet.EncodeToPNG());
    Object.Destroy(sheet);

    foreach (var r in hidden) if (r != null) r.enabled = true;
    Object.Destroy(camGo);
    Object.Destroy(bake);
    anim.enabled = animWas;
    if (ctl != null) ctl.enabled = true;
    if (ik != null) ik.enabled = true;
    if (stance != null) stance.enabled = true;

    System.IO.File.WriteAllText(System.IO.Path.Combine(projRoot, "Tools/reports/q_walk_show.txt"), sb.ToString());
    Debug.Log("[q_walk_show] done");
    yield return null;
}
return Body();
