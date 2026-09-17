// q_foot5.cs —— 活体 A/B：KI 底座 vs UAL1 Sprint 底座 vs UAL1 Jog 底座
//   度量：① 脚总偏角（Quaternion.Angle，不依赖骨骼轴定义 —— 脚骨 transform.forward 在 CC 骨架上近乎垂直，
//            任何基于投影的 yaw 度量都会出噪声，这是前两轮踩过的坑）
//         ② 鞋底最低点分布（只统计权重落在脚/脚趾骨上的顶点）
//         ③ 每循环位移 / 触地帧占比
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

IEnumerator Body()
{
    var sb = new StringBuilder();
    string projRoot = Path.GetDirectoryName(Application.dataPath);
    string imgDir = Path.Combine(projRoot, "Tools/screenshots/foot5");
    Directory.CreateDirectory(imgDir);
    yield return null; yield return null;

    var go = GameObject.Find("Player");
    if (go == null) { Debug.LogError("no Player"); yield break; }
    var anim = go.GetComponent<Animator>();
    var ctl = go.GetComponent<InkWash.Player.PlayerController>();
    var ik = go.GetComponent<InkWash.Player.FootIK>();
    var stance = go.GetComponentInChildren<InkWash.Player.CombatStance>(true);
    var cc = go.GetComponent<CharacterController>();

    float oldExit = stance != null ? stance.combatExitDelay : 6f;
    if (stance != null) stance.combatExitDelay = 99999f;

    Vector3 startPos = go.transform.position;
    Quaternion startRot = go.transform.rotation;
    void Park()
    {
        if (cc != null) cc.enabled = false;
        go.transform.position = startPos; go.transform.rotation = startRot;
        if (cc != null) cc.enabled = true;
    }

    Transform Bone(HumanBodyBones b) { return anim.GetBoneTransform(b); }
    var lf = Bone(HumanBodyBones.LeftFoot);
    var rf = Bone(HumanBodyBones.RightFoot);

    var weapon = go.GetComponentInChildren<InkWash.Effects.SwordVfx>(true);
    var wInst = weapon != null ? weapon.WeaponInstance : null;
    var weaponRends = wInst != null ? new HashSet<Renderer>(wInst.GetComponentsInChildren<Renderer>(true)) : new HashSet<Renderer>();
    var smrs = go.GetComponentsInChildren<SkinnedMeshRenderer>(true)
        .Where(s => s != null && s.enabled && !weaponRends.Contains(s)).ToArray();
    var shoeMask = new Dictionary<SkinnedMeshRenderer, bool[]>();
    foreach (var s in smrs)
    {
        var mesh = s.sharedMesh; if (mesh == null) continue;
        var foots = new[] { Bone(HumanBodyBones.LeftFoot), Bone(HumanBodyBones.LeftToes),
                            Bone(HumanBodyBones.RightFoot), Bone(HumanBodyBones.RightToes) };
        var ids = new HashSet<int>();
        for (int i = 0; i < s.bones.Length; i++)
            if (s.bones[i] != null && foots.Any(f => f != null && f == s.bones[i])) ids.Add(i);
        var bw = mesh.boneWeights;
        var mask = new bool[mesh.vertexCount];
        for (int v = 0; v < mask.Length && v < bw.Length; v++)
        {
            var b = bw[v]; float w = 0f;
            if (ids.Contains(b.boneIndex0)) w += b.weight0;
            if (ids.Contains(b.boneIndex1)) w += b.weight1;
            if (ids.Contains(b.boneIndex2)) w += b.weight2;
            if (ids.Contains(b.boneIndex3)) w += b.weight3;
            mask[v] = w > 0.5f;
        }
        shoeMask[s] = mask;
    }
    var bakeMesh = new Mesh { name = "Qf5_Bake" };
    var vbuf = new List<Vector3>(16384);
    float LowestShoe()
    {
        float lo = float.MaxValue;
        for (int k = 0; k < smrs.Length; k++)
        {
            var s = smrs[k];
            if (!shoeMask.TryGetValue(s, out var mk)) continue;
            s.BakeMesh(bakeMesh); bakeMesh.GetVertices(vbuf);
            var m = s.transform.localToWorldMatrix;
            for (int i = 0; i < vbuf.Count; i++)
            {
                if (!mk[i]) continue;
                float y = m.m10 * vbuf[i].x + m.m11 * vbuf[i].y + m.m12 * vbuf[i].z + m.m13;
                if (y < lo) lo = y;
            }
        }
        return lo;
    }
    bool GroundY(out float y)
    {
        y = go.transform.position.y; bool ok = false; float best = float.MinValue;
        var hits = Physics.RaycastAll(go.transform.position + Vector3.up * 1.0f, Vector3.down, 3.5f, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < hits.Length; i++)
        {
            var tr = hits[i].collider.transform;
            if (tr == go.transform || tr.IsChildOf(go.transform)) continue;
            if (hits[i].point.y > best) { best = hits[i].point.y; ok = true; }
        }
        if (ok) y = best;
        return ok;
    }

    // 相机
    var camGo = new GameObject("TmpFoot4Cam");
    var tcam = camGo.AddComponent<Camera>();
    tcam.CopyFrom(Camera.main); tcam.tag = "Untagged"; tcam.enabled = false;
    tcam.clearFlags = CameraClearFlags.SolidColor; tcam.backgroundColor = new Color(0.15f, 0.16f, 0.19f);
    var keepSet = new HashSet<Renderer>(go.GetComponentsInChildren<Renderer>());
    var hidden = new List<Renderer>();
    foreach (var r in Object.FindObjectsOfType<Renderer>()) if (r.enabled && !keepSet.Contains(r)) { r.enabled = false; hidden.Add(r); }

    int CW = 380, CH = 340;
    Texture2D Shot(Vector3 center, Vector3 dir, float ortho)
    {
        Vector3 cp = center + dir * 2f;
        tcam.orthographic = true; tcam.orthographicSize = ortho;
        tcam.transform.position = cp; tcam.transform.rotation = Quaternion.LookRotation(center - cp, Vector3.up);
        var rt = RenderTexture.GetTemporary(CW, CH, 24);
        tcam.targetTexture = rt; tcam.Render(); tcam.targetTexture = null;
        var prev = RenderTexture.active; RenderTexture.active = rt;
        var tile = new Texture2D(CW, CH, TextureFormat.RGB24, false);
        tile.ReadPixels(new Rect(0, 0, CW, CH), 0, 0); tile.Apply();
        RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt);
        return tile;
    }

    AnimationClip Proj(string n) { return AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/_Project/Animations/" + n + ".anim"); }

    var ovr = anim.runtimeAnimatorController as AnimatorOverrideController;
    AnimationClip runKey = null, runOrig = null;
    if (ovr != null)
    {
        var pairs = new List<KeyValuePair<AnimationClip, AnimationClip>>();
        ovr.GetOverrides(pairs);
        foreach (var kv in pairs)
            if (kv.Key != null && kv.Key.name.StartsWith("Run")) { runKey = kv.Key; runOrig = kv.Value; break; }
    }
    sb.AppendLine("Run 槽位 key = " + (runKey != null ? runKey.name : "<未找到>"));
    sb.AppendLine("FootIK 现值：BodyBase = " + (ik != null ? ik.BodyBase.ToString("F4") : "?")
        + "  groundClearance = " + (ik != null ? ik.groundClearance.ToString("F4") : "?"));
    sb.AppendLine();

    if (ctl != null) ctl.BeginInputOverride();
    if (ctl != null) ctl.SetInjectedMove(Vector2.zero, false);
    if (stance != null) stance.ForceStance(false);
    { float t = Time.time; while (Time.time - t < 0.8f) yield return null; }

    // 基准：非战斗待机下的脚旋转（多帧分量均值）
    var qIdleL = Quaternion.identity; var qIdleR = Quaternion.identity;
    {
        Vector4 aL = Vector4.zero, aR = Vector4.zero; int m = 0;
        for (int i = 0; i < 24; i++)
        {
            var ql = lf.rotation; var qr = rf.rotation;
            aL.x += ql.x; aL.y += ql.y; aL.z += ql.z; aL.w += ql.w;
            aR.x += qr.x; aR.y += qr.y; aR.z += qr.z; aR.w += qr.w;
            m++; yield return null;
        }
        qIdleL = new Quaternion(aL.x / m, aL.y / m, aL.z / m, aL.w / m).normalized;
        qIdleR = new Quaternion(aR.x / m, aR.y / m, aR.z / m, aR.w / m).normalized;
    }

    var cands = new (string label, AnimationClip clip)[]
    {
        ("对照 Baked/Run01_Carry 原KI",  Proj("Baked/Run01_Carry")),
        ("候选 Baked/Run03_KI_Carry",    Proj("Baked/Run03_KI_Carry")),
        ("候选 Baked/Run02_Sprint_Carry",Proj("Baked/Run02_Sprint_Carry")),
    };

    int ROWS = cands.Length;
    var fig = new Texture2D(CW * 4, CH * ROWS, TextureFormat.RGB24, false);
    sb.AppendLine("候选                         播放片段             脚总偏角L/R(度)  左右差   鞋底去偏移 min/p10/中位/p90/max (m)         触地帧%  每循环位移");
    sb.AppendLine("----------------------------------------------------------------------------------------------------------------------------------------------");

    for (int ci = 0; ci < ROWS; ci++)
    {
        var c = cands[ci];
        if (c.clip == null) { sb.AppendLine(c.label + "  ★ 找不到"); continue; }
        if (ovr != null && runKey != null) ovr[runKey] = c.clip;
        Park();
        if (ctl != null) ctl.SetInjectedMove(new Vector2(0f, 1f), true);
        float wt = Time.time; while (Time.time - wt < 1.7f) yield return null;

        string playing = "?";
        var angL = new List<float>(); var angR = new List<float>();
        var lows = new List<float>();
        Vector3 p0 = go.transform.position; float t0 = Time.time;
        var tiles = new List<Texture2D>();
        int n = 0;
        while (Time.time - t0 < 3.0f)
        {
            angL.Add(Quaternion.Angle(qIdleL, lf.rotation));
            angR.Add(Quaternion.Angle(qIdleR, rf.rotation));
            float gy; bool gok = GroundY(out gy);
            float lo = LowestShoe();
            if (gok && lo < float.MaxValue) lows.Add(lo - (ik != null ? ik.AppliedBodyOffset : 0f) - gy);
            var cbuf = new List<AnimatorClipInfo>(4); anim.GetCurrentAnimatorClipInfo(0, cbuf);
            if (cbuf.Count > 0 && cbuf[0].clip != null) playing = cbuf[0].clip.name;
            if (n % 26 == 0 && tiles.Count < 4)
            {
                var fc = new Vector3(go.transform.position.x, go.transform.position.y + 0.16f, go.transform.position.z);
                var dirs = new[] { go.transform.right, -go.transform.forward, Vector3.up,
                    (go.transform.right * 0.75f - go.transform.forward * 0.5f + Vector3.up * 0.45f).normalized };
                tiles.Add(Shot(fc, dirs[tiles.Count], 0.62f));
            }
            n++;
            yield return null;
        }
        float dt = Time.time - t0;
        Vector3 p1 = go.transform.position;
        if (ctl != null) ctl.SetInjectedMove(Vector2.zero, false);

        var sr = lows.OrderBy(x => x).ToList();
        float mn = sr.Count > 0 ? sr[0] : 0f;
        float p10 = sr.Count > 0 ? sr[(int)(sr.Count * 0.10f)] : 0f;
        float md = sr.Count > 0 ? sr[sr.Count / 2] : 0f;
        float p90 = sr.Count > 0 ? sr[(int)(sr.Count * 0.90f)] : 0f;
        float mx = sr.Count > 0 ? sr[sr.Count - 1] : 0f;
        float touch = sr.Count > 0 ? sr.Count(x => x < 0.02f) * 100f / sr.Count : 0f;
        float perLoop = dt > 0f ? Vector3.Distance(new Vector3(p0.x, 0, p0.z), new Vector3(p1.x, 0, p1.z)) / (dt / c.clip.length) : 0f;
        float aL = angL.Average(), aR = angR.Average();
        sb.AppendLine(string.Format("{0,-28} {1,-20} {2,6:F1}/{3,-6:F1} {4,7:F1}   {5,7:F3}/{6,7:F3}/{7,7:F3}/{8,7:F3}/{9,7:F3}   {10,6:F1}  {11,7:F3}",
            c.label, playing, aL, aR, Mathf.Abs(aR - aL), mn, p10, md, p90, mx, touch, perLoop));
        sb.AppendLine(string.Format("       → 若以此片段全循环最低点贴地：偏移 O = {0:F4}（体检不穿地需 O ≥ {1:F4}，不浮空需 O ≤ {2:F4}）",
            -mn, -0.02f - mn, 0.05f - mn));

        for (int k = 0; k < tiles.Count && k < 4; k++)
            fig.SetPixels(k * CW, (ROWS - 1 - ci) * CH, CW, CH, tiles[k].GetPixels());
        foreach (var t in tiles) Object.Destroy(t);
        { float t = Time.time; while (Time.time - t < 0.4f) yield return null; }
    }

    fig.Apply();
    File.WriteAllBytes(Path.Combine(imgDir, "_run_cmp3.png"), fig.EncodeToPNG());
    Object.Destroy(fig);
    sb.AppendLine();
    sb.AppendLine("图 = _run_cmp3.png  行对应上表顺序，列 0=右侧视 1=正视 2=俯视 3=斜视（相机已对准双脚，ortho 0.62）");
    sb.AppendLine("判读：左右差 = |右脚总偏角 − 左脚总偏角|。KI 底座实测 56°，UAL1 底座应在 10° 以内。");

    // 上一轮在这里出错：写了 runOrig != null 判断，而 runOrig 本来就是 null ⇒ 还原被跳过，
    // 残留的 override 污染了后续脚本。改为无条件还原。
    if (ovr != null && runKey != null) ovr[runKey] = runOrig;
    foreach (var r in hidden) if (r != null) r.enabled = true;
    Object.Destroy(camGo); Object.Destroy(bakeMesh);
    if (ctl != null) { ctl.SetInjectedMove(Vector2.zero, false); ctl.EndInputOverride(); }
    if (stance != null) stance.combatExitDelay = oldExit;
    Park();

    File.WriteAllText(Path.Combine(projRoot, "Tools/reports/q_foot5.txt"), sb.ToString());
    Debug.Log("[q_foot5] done");
    yield return null;
}

return Body();
