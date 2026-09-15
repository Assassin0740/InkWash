// q_foot3.cs —— 活体实测跑步候选：脚朝向（总偏转角/yaw/pitch）+ 鞋底触地分布 + 每循环位移 + 脚部特写图
//
// 为什么换度量：上一轮用「脚趾骨位置 − 脚骨位置」的水平投影测脚偏角，脚大幅俯仰时该向量会翻转，
// 读出 −174°~160° 的假数据。本脚本改用两条互相独立的量：
//   ① Quaternion.Angle(基准脚旋转, 当前脚旋转)  → 「脚被扭了多少度」的直接量化（用户说的 90° 就是它）
//   ② 脚骨自身 forward 的水平投影 vs 角色 forward → 绝对偏角，与俯仰解耦
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
    string imgDir = Path.Combine(projRoot, "Tools/screenshots/foot3");
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

    // 鞋底顶点掩码（只统计权重落在脚/脚趾骨上的顶点）
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
    var bakeMesh = new Mesh { name = "Qf3_Bake" };
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

    // 脚部朝向度量
    float FootYaw(Transform foot)
    {
        Vector3 f = foot.rotation * Vector3.forward; f.y = 0f;
        Vector3 b = go.transform.forward; b.y = 0f;
        if (f.sqrMagnitude < 1e-8f || b.sqrMagnitude < 1e-8f) return 0f;
        return Vector3.SignedAngle(b.normalized, f.normalized, Vector3.up);
    }
    float FootPitch(Transform foot)
    {
        Vector3 f = foot.rotation * Vector3.forward;
        return Mathf.Asin(Mathf.Clamp(-f.y, -1f, 1f)) * Mathf.Rad2Deg;
    }

    // 相机
    var camGo = new GameObject("TmpFoot3Cam");
    var tcam = camGo.AddComponent<Camera>();
    tcam.CopyFrom(Camera.main); tcam.tag = "Untagged"; tcam.enabled = false;
    tcam.clearFlags = CameraClearFlags.SolidColor; tcam.backgroundColor = new Color(0.15f, 0.16f, 0.19f);
    var keepSet = new HashSet<Renderer>(go.GetComponentsInChildren<Renderer>());
    var hidden = new List<Renderer>();
    foreach (var r in Object.FindObjectsOfType<Renderer>()) if (r.enabled && !keepSet.Contains(r)) { r.enabled = false; hidden.Add(r); }

    int CW = 360, CH = 300;
    Texture2D Shot(Vector3 center, Vector3 dir, float ortho)
    {
        Vector3 cp = center + dir * 1.5f;
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
    AnimationClip FromPack(string pack, string name)
    {
        foreach (var o in AssetDatabase.LoadAllAssetsAtPath(pack))
        {
            var c = o as AnimationClip;
            if (c != null && c.name == name) return c;
        }
        return null;
    }
    string ual1 = "Assets/ThirdParty/Quaternius/UniversalAnimationLibrary/Unity/AnimationLibrary_Unity_Standard.fbx";
    string kiRun = "Assets/ThirdParty/KevinIglesias/Animations/Male/Movement/Run/HumanM@Run01_Forward.fbx";
    AnimationClip KiRun()
    {
        foreach (var o in AssetDatabase.LoadAllAssetsAtPath(kiRun))
        {
            var c = o as AnimationClip;
            if (c != null && !c.name.StartsWith("__preview__")) return c;
        }
        return null;
    }

    var ovr = anim.runtimeAnimatorController as AnimatorOverrideController;
    AnimationClip runKey = null, runOrig = null;
    if (ovr != null)
    {
        var pairs = new List<KeyValuePair<AnimationClip, AnimationClip>>();
        ovr.GetOverrides(pairs);
        foreach (var kv in pairs)
            if (kv.Key != null && kv.Key.name.StartsWith("Run")) { runKey = kv.Key; runOrig = kv.Value; break; }
    }
    sb.AppendLine("Run 槽位 key = " + (runKey != null ? runKey.name : "<未找到>") + "  原 value = " + (runOrig != null ? runOrig.name : "<null>"));
    sb.AppendLine();

    if (ctl != null) ctl.BeginInputOverride();
    if (ctl != null) ctl.SetInjectedMove(Vector2.zero, false);
    if (stance != null) stance.ForceStance(false);
    { float t = Time.time; while (Time.time - t < 0.8f) yield return null; }

    // ---------- 基准：非战斗待机下的脚旋转 ----------
    var qIdleL = Quaternion.identity; var qIdleR = Quaternion.identity;
    {
        var accL = new List<Quaternion>(); var accR = new List<Quaternion>();
        for (int i = 0; i < 24; i++) { accL.Add(lf.rotation); accR.Add(rf.rotation); yield return null; }
        // 分量的简单平均后归一化
        Vector4 aL = Vector4.zero, aR = Vector4.zero;
        foreach (var q in accL) { aL.x += q.x; aL.y += q.y; aL.z += q.z; aL.w += q.w; }
        foreach (var q in accR) { aR.x += q.x; aR.y += q.y; aR.z += q.z; aR.w += q.w; }
        qIdleL = new Quaternion(aL.x, aL.y, aL.z, aL.w).normalized;
        qIdleR = new Quaternion(aR.x, aR.y, aR.z, aR.w).normalized;
        sb.AppendLine(string.Format("基准（非战斗待机 Rig|Idle_Loop 24 帧均值）：左脚 yaw {0,6:F1}°  pitch {1,6:F1}°  ｜ 右脚 yaw {2,6:F1}°  pitch {3,6:F1}°",
            FootYaw(lf), FootPitch(lf), FootYaw(rf), FootPitch(rf)));
        sb.AppendLine();
    }

    var cands = new (string label, AnimationClip clip, bool move)[]
    {
        ("KI Run01_Forward（现用源）",     KiRun(),                                true),
        ("Baked/Run01_Carry（现用片段）",  Proj("Baked/Run01_Carry"),               true),
        ("UAL1 Rig|Jog_Fwd_Loop",          FromPack(ual1, "Rig|Jog_Fwd_Loop"),      true),
        ("UAL1 Rig|Sprint_Loop",           FromPack(ual1, "Rig|Sprint_Loop"),       true),
        ("UAL1 Rig|Walk_Loop（对照·正常）", FromPack(ual1, "Rig|Walk_Loop"),         true),
    };

    int ROWS = cands.Length;
    var fig = new Texture2D(CW * 4, CH * ROWS, TextureFormat.RGB24, false);
    sb.AppendLine("候选                        播放片段         脚总偏角L 脚总偏角R (度)   脚yaw L/R(度)     脚pitch L/R(度)    鞋底去偏移 min/中位/max(m)   每循环位移(m)");
    sb.AppendLine("--------------------------------------------------------------------------------------------------------------------------------------------------");

    for (int ci = 0; ci < ROWS; ci++)
    {
        var c = cands[ci];
        if (c.clip == null) { sb.AppendLine(c.label + "  ★ 找不到片段"); continue; }
        if (ovr != null && runKey != null) ovr[runKey] = c.clip;
        Park();
        if (ctl != null) ctl.SetInjectedMove(new Vector2(0f, 1f), true);
        float wt = Time.time; while (Time.time - wt < 1.7f) yield return null;

        string playing = "?";
        var angL = new List<float>(); var angR = new List<float>();
        var yawL = new List<float>(); var yawR = new List<float>();
        var pitL = new List<float>(); var pitR = new List<float>();
        var lows = new List<float>();
        Vector3 p0 = go.transform.position; float t0 = Time.time;
        var tiles = new List<Texture2D>();
        int n = 0;
        while (Time.time - t0 < 3.0f)
        {
            angL.Add(Quaternion.Angle(qIdleL, lf.rotation));
            angR.Add(Quaternion.Angle(qIdleR, rf.rotation));
            yawL.Add(FootYaw(lf)); yawR.Add(FootYaw(rf));
            pitL.Add(FootPitch(lf)); pitR.Add(FootPitch(rf));
            float gy; bool gok = GroundY(out gy);
            float lo = LowestShoe();
            if (gok && lo < float.MaxValue) lows.Add(lo - (ik != null ? ik.AppliedBodyOffset : 0f) - gy);
            var cbuf = new List<AnimatorClipInfo>(4); anim.GetCurrentAnimatorClipInfo(0, cbuf);
            if (cbuf.Count > 0 && cbuf[0].clip != null) playing = cbuf[0].clip.name;
            if (n % 26 == 0 && tiles.Count < 4)
            {
                var fc = new Vector3(go.transform.position.x, go.transform.position.y + 0.13f, go.transform.position.z);
                var dirs = new[] { go.transform.right, -go.transform.forward, Vector3.up, (go.transform.right * 0.7f - go.transform.forward * 0.6f + Vector3.up * 0.5f).normalized };
                tiles.Add(Shot(fc, dirs[tiles.Count], 0.50f));
            }
            n++;
            yield return null;
        }
        float dt = Time.time - t0;
        Vector3 p1 = go.transform.position;
        if (ctl != null) ctl.SetInjectedMove(Vector2.zero, false);

        var sr = lows.OrderBy(x => x).ToList();
        float lmn = sr.Count > 0 ? sr[0] : 0f, lmd = sr.Count > 0 ? sr[sr.Count / 2] : 0f, lmx = sr.Count > 0 ? sr[sr.Count - 1] : 0f;
        float perLoop = dt > 0f ? Vector3.Distance(new Vector3(p0.x, 0, p0.z), new Vector3(p1.x, 0, p1.z)) / (dt / c.clip.length) : 0f;
        sb.AppendLine(string.Format("{0,-26} {1,-16} {2,8:F1} {3,9:F1}    {4,6:F1}/{5,-6:F1}   {6,6:F1}/{7,-6:F1}   {8,7:F3}/{9,7:F3}/{10,7:F3}   {11,7:F3}",
            c.label, playing, angL.Average(), angR.Average(),
            yawL.Average(), yawR.Average(), pitL.Average(), pitR.Average(), lmn, lmd, lmx, perLoop));

        for (int k = 0; k < tiles.Count && k < 4; k++)
            fig.SetPixels(k * CW, (ROWS - 1 - ci) * CH, CW, CH, tiles[k].GetPixels());
        foreach (var t in tiles) Object.Destroy(t);
        { float t = Time.time; while (Time.time - t < 0.4f) yield return null; }
    }

    fig.Apply();
    File.WriteAllBytes(Path.Combine(imgDir, "_run_feet_cmp.png"), fig.EncodeToPNG());
    Object.Destroy(fig);
    sb.AppendLine();
    sb.AppendLine("图 = _run_feet_cmp.png  行 0.." + (ROWS - 1) + " 对应上表顺序，列 0=右侧视 1=正视 2=俯视 3=斜视");
    sb.AppendLine("判读：脚总偏角 = 相对非战斗待机脚旋转的四元数夹角。若全程 > 40° 即为「脚被拧」，");
    sb.AppendLine("      且左右差异大 ⇒ 不是动作设计。鞋底去偏移 min/中位/max 越集中，越容易同时满足体检两条断言。");

    // 还原
    if (ovr != null && runKey != null && runOrig != null) ovr[runKey] = runOrig;
    foreach (var r in hidden) if (r != null) r.enabled = true;
    Object.Destroy(camGo); Object.Destroy(bakeMesh);
    if (ctl != null) { ctl.SetInjectedMove(Vector2.zero, false); ctl.EndInputOverride(); }
    if (stance != null) stance.combatExitDelay = oldExit;
    Park();

    File.WriteAllText(Path.Combine(projRoot, "Tools/reports/q_foot3.txt"), sb.ToString());
    Debug.Log("[q_foot3] done");
    yield return null;
}

return Body();
