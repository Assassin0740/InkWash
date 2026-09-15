// q_pose9.cs —— 活体 A/B：① 持剑待机（大图）② 跑步脚踝压摆幅后的三段对照
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
    string imgDir = Path.Combine(projRoot, "Tools/screenshots/pose9");
    Directory.CreateDirectory(imgDir);
    yield return null; yield return null;

    var go = GameObject.Find("Player");
    if (go == null) { Debug.LogError("no Player"); yield break; }
    var anim = go.GetComponent<Animator>();
    var ctl = go.GetComponent<InkWash.Player.PlayerController>();
    var ik = go.GetComponent<InkWash.Player.FootIK>();
    var stance = go.GetComponentInChildren<InkWash.Player.CombatStance>(true);
    var vfx = go.GetComponentInChildren<InkWash.Effects.SwordVfx>(true);
    var cc = go.GetComponent<CharacterController>();
    var rig = Camera.main != null ? Camera.main.GetComponent<InkWash.CameraRig.ThirdPersonCamera>() : null;

    float oldExit = stance != null ? stance.combatExitDelay : 6f;
    if (stance != null) stance.combatExitDelay = 99999f;
    AnimationClip oldCombatIdle = stance != null ? stance.combatIdleClip : null;

    Vector3 startPos = go.transform.position;
    Quaternion startRot = go.transform.rotation;
    void Park()
    {
        if (cc != null) cc.enabled = false;
        go.transform.position = startPos; go.transform.rotation = startRot;
        if (cc != null) cc.enabled = true;
    }

    var weapon = vfx != null ? vfx.WeaponInstance : null;
    var weaponRends = weapon != null ? new HashSet<Renderer>(weapon.GetComponentsInChildren<Renderer>(true)) : new HashSet<Renderer>();
    var smrs = go.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(s => s != null && s.enabled && !weaponRends.Contains(s)).ToArray();
    Transform Bone(HumanBodyBones b) { return anim.GetBoneTransform(b); }

    var shoeMask = new Dictionary<SkinnedMeshRenderer, bool[]>();
    foreach (var s in smrs)
    {
        var mesh = s.sharedMesh; if (mesh == null) continue;
        var foots = new[] { Bone(HumanBodyBones.LeftFoot), Bone(HumanBodyBones.LeftToes), Bone(HumanBodyBones.RightFoot), Bone(HumanBodyBones.RightToes) };
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

    var bakeMesh = new Mesh { name = "Q9_Bake" };
    var vbuf = new List<Vector3>(16384);
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
    float Lowest(bool shoeOnly)
    {
        float lo = float.MaxValue;
        for (int k = 0; k < smrs.Length; k++)
        {
            var s = smrs[k];
            s.BakeMesh(bakeMesh); bakeMesh.GetVertices(vbuf);
            var m = s.transform.localToWorldMatrix;
            var mask = shoeOnly && shoeMask.TryGetValue(s, out var mk) ? mk : null;
            for (int i = 0; i < vbuf.Count; i++)
            {
                if (mask != null && !mask[i]) continue;
                float y = m.m10 * vbuf[i].x + m.m11 * vbuf[i].y + m.m12 * vbuf[i].z + m.m13;
                if (y < lo) lo = y;
            }
        }
        return lo;
    }

    var camGo = new GameObject("TmpPose9Cam");
    var tcam = camGo.AddComponent<Camera>();
    tcam.CopyFrom(Camera.main); tcam.tag = "Untagged"; tcam.enabled = false;
    tcam.clearFlags = CameraClearFlags.SolidColor; tcam.backgroundColor = new Color(0.15f, 0.16f, 0.19f);
    var keep = new HashSet<Renderer>(go.GetComponentsInChildren<Renderer>());
    var hidden = new List<Renderer>();
    foreach (var r in Object.FindObjectsOfType<Renderer>()) if (r.enabled && !keep.Contains(r)) { r.enabled = false; hidden.Add(r); }

    int CW = 400, CH = 520;
    Texture2D Shot(Vector3 center, Vector3 dir, float ortho, int w = -1, int h = -1)
    {
        int W = w > 0 ? w : CW, H = h > 0 ? h : CH;
        Vector3 cp = center + dir * 6f;
        tcam.orthographic = true; tcam.orthographicSize = ortho;
        tcam.transform.position = cp; tcam.transform.rotation = Quaternion.LookRotation(center - cp, Vector3.up);
        var rt = RenderTexture.GetTemporary(W, H, 24);
        tcam.targetTexture = rt; tcam.Render(); tcam.targetTexture = null;
        var prev = RenderTexture.active; RenderTexture.active = rt;
        var tile = new Texture2D(W, H, TextureFormat.RGB24, false);
        tile.ReadPixels(new Rect(0, 0, W, H), 0, 0); tile.Apply();
        RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt);
        return tile;
    }
    Vector3 BodyC() { return new Vector3(go.transform.position.x, go.transform.position.y + 1.05f, go.transform.position.z); }
    Vector3 GameDir() { return (-go.transform.forward * 0.75f + go.transform.right * 0.55f + Vector3.up * 0.38f).normalized; }

    AnimationClip Proj(string n) { return AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/_Project/Animations/" + n + ".anim"); }

    if (ctl != null) ctl.BeginInputOverride();
    if (ctl != null) ctl.SetInjectedMove(Vector2.zero, false);

    // ================= ① 持剑待机大图 =================
    sb.AppendLine("=== ① 持剑待机大图（左=斜后上游戏机位 / 右=正视）===");
    var idleCands = new List<(string label, AnimationClip clip)>
    {
        ("A 现用 Sword_Idle_Loop", Proj("Sword_Idle_Loop")),
        ("B 烘焙 Idle_Carry_A   ", Proj("Baked/Idle_Carry_A")),
        ("【参照】非战斗待机     ", null),
    };
    int W2 = 600, H2 = 780;
    var sh = new Texture2D(W2 * 2, H2 * idleCands.Count, TextureFormat.RGB24, false);
    for (int r = 0; r < idleCands.Count; r++)
    {
        if (idleCands[r].clip == null) { if (stance != null) stance.ForceStance(false); }
        else if (stance != null) { stance.combatIdleClip = idleCands[r].clip; stance.ForceStance(false); stance.ForceStance(true); }
        float t = Time.time; while (Time.time - t < 0.7f) yield return null;
        var a = Shot(BodyC(), GameDir(), 1.40f, W2, H2);
        var b = Shot(BodyC(), go.transform.forward, 1.40f, W2, H2);
        int y = (idleCands.Count - 1 - r) * H2;
        sh.SetPixels(0, y, W2, H2, a.GetPixels());
        sh.SetPixels(W2, y, W2, H2, b.GetPixels());
        Object.Destroy(a); Object.Destroy(b);
        var cbuf = new List<AnimatorClipInfo>(4); anim.GetCurrentAnimatorClipInfo(0, cbuf);
        sb.AppendLine("  图行 " + r + " = " + idleCands[r].label + "  实际播放 = "
            + ((cbuf.Count > 0 && cbuf[0].clip != null) ? cbuf[0].clip.name : "?"));
    }
    sh.Apply();
    File.WriteAllBytes(Path.Combine(imgDir, "_idle_big.png"), sh.EncodeToPNG());
    Object.Destroy(sh);

    // ================= ② 跑步变体对照（运行期换 Run 槽位，不动控制器资产）=================
    sb.AppendLine();
    sb.AppendLine("=== ② 跑步：脚踝摆幅 keep=1.0 / 0.5 / 0.2 对照（活体）===");
    var ovr = anim.runtimeAnimatorController as AnimatorOverrideController;
    AnimationClip runKey = null, runOrig = null;
    if (ovr != null)
    {
        var pairs = new List<KeyValuePair<AnimationClip, AnimationClip>>();
        ovr.GetOverrides(pairs);
        sb.AppendLine("override 槽位共 " + pairs.Count + " 个：");
        foreach (var kv in pairs)
            sb.AppendLine("   key=" + (kv.Key != null ? kv.Key.name : "<null>") + " → value=" + (kv.Value != null ? kv.Value.name : "<null>"));
        foreach (var kv in pairs)
            if (kv.Key != null && kv.Key.name.StartsWith("Run"))
            { runKey = kv.Key; runOrig = kv.Value; break; }
    }
    sb.AppendLine("Run 槽位 key = " + (runKey != null ? runKey.name : "<未找到>") + "  原 value = " + (runOrig != null ? runOrig.name : "<null>"));

    var runVars = new (string label, AnimationClip clip)[]
    {
        ("keep1.0 Run01_Carry  ", Proj("Baked/Run01_Carry")),
        ("keep0.5 Run01_Carry_F5", Proj("Baked/Run01_Carry_F5")),
        ("keep0.2 Run01_Carry_F2", Proj("Baked/Run01_Carry_F2")),
    };

    if (ctl != null) ctl.BeginInputOverride();
    if (stance != null) { stance.combatIdleClip = Proj("Baked/Idle_Carry_A"); stance.ForceStance(true); }
    var runShotList = new List<Texture2D>();
    var sum = new StringBuilder();
    foreach (var v in runVars)
    {
        if (v.clip == null) { sb.AppendLine(v.label + " ★ 找不到"); continue; }
        if (ovr != null && runKey != null) ovr[runKey] = v.clip;
        Park();
        if (ctl != null) ctl.SetInjectedMove(new Vector2(0f, 1f), true);
        float wt = Time.time; while (Time.time - wt < 1.7f) yield return null;

        string playing = "?";
        var aAll = new List<float>();
        float t0 = Time.time; int n = 0;
        while (Time.time - t0 < 3.0f)
        {
            float gy; bool gok = GroundY(out gy);
            if (gok)
            {
                float off = ik != null ? ik.AppliedBodyOffset : 0f;
                float la = Lowest(false), ls = Lowest(true);
                if (la < float.MaxValue && ls < float.MaxValue) aAll.Add(la - off - gy);
            }
            var cbuf = new List<AnimatorClipInfo>(4); anim.GetCurrentAnimatorClipInfo(0, cbuf);
            if (cbuf.Count > 0 && cbuf[0].clip != null) playing = cbuf[0].clip.name;
            if (n % 9 == 0 && runShotList.Count < 30) runShotList.Add(Shot(BodyC(), GameDir(), 1.40f));
            n++;
            yield return null;
        }
        if (ctl != null) ctl.SetInjectedMove(Vector2.zero, false);
        var srt = aAll.OrderBy(x => x).ToList();
        float mn = srt.Count > 0 ? srt[0] : 0f;
        float md = srt.Count > 0 ? srt[srt.Count / 2] : 0f;
        float mx = srt.Count > 0 ? srt[srt.Count - 1] : 0f;
        float clear = ik != null ? ik.groundClearance : 0.005f;
        sum.AppendLine(string.Format("{0}  实际={1,-14} 最低 {2,7:F3}  中位 {3,7:F3}  最高 {4,6:F3}  " +
            "按最低贴地 偏移={5,7:F4} 按中位贴地 偏移={6,7:F4}",
            v.label, playing, mn, md, mx, -mn + clear, -md + clear));
        { float t = Time.time; while (Time.time - t < 0.5f) yield return null; }
    }
    sb.AppendLine("变体                 实际片段       动画最低(去偏移) 中位    最高     建议偏移(贴最低) 建议偏移(贴中位)");
    sb.Append(sum);
    sb.AppendLine("现值偏移 = " + (ik != null ? ik.BodyBase : 0f).ToString("F4"));
    sb.AppendLine("判读：压摆幅的目标是让「最低」与「中位」靠拢 —— 两者越近，越能同时满足");
    sb.AppendLine("      体检「脚不穿地(≥−0.02)」与「不浮空(≤+0.05)」，也不会整段飘。");

    if (runShotList.Count >= 6)
    {
        var sh2 = new Texture2D(CW * 6, CH * 3, TextureFormat.RGB24, false);
        for (int i = 0; i < 18 && i < runShotList.Count; i++)
        {
            int row = i / 6, col = i % 6;
            sh2.SetPixels(col * CW, (2 - row) * CH, CW, CH, runShotList[i].GetPixels());
        }
        sh2.Apply();
        File.WriteAllBytes(Path.Combine(imgDir, "_run_vars.png"), sh2.EncodeToPNG());
        Object.Destroy(sh2);
        sb.AppendLine("跑步图 = _run_vars.png（行 0=keep1.0 / 1=keep0.5 / 2=keep0.2）");
    }
    foreach (var t in runShotList) Object.Destroy(t);

    // 还原
    if (ovr != null && runKey != null && runOrig != null) ovr[runKey] = runOrig;
    foreach (var r in hidden) if (r != null) r.enabled = true;
    Object.Destroy(camGo); Object.Destroy(bakeMesh);
    if (ctl != null) { ctl.SetInjectedMove(Vector2.zero, false); ctl.EndInputOverride(); }
    if (stance != null) { stance.combatIdleClip = oldCombatIdle; stance.ForceStance(false); stance.combatExitDelay = oldExit; }
    if (rig != null) rig.SetMouseLookEnabled(true);

    File.WriteAllText(Path.Combine(projRoot, "Tools/reports/q_pose9.txt"), sb.ToString());
    Debug.Log("[q_pose9] done");
    yield return null;
}

return Body();
