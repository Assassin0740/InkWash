// q_pose7.cs —— 本轮三问取证 v3（修正 v2 的两个硬伤：武器被自动收剑收回背后 / 网格过滤把角色渲染器全排除）
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
    string imgDir = Path.Combine(projRoot, "Tools/screenshots/pose7");
    Directory.CreateDirectory(imgDir);
    yield return null; yield return null;

    var go = GameObject.Find("Player");
    if (go == null) { Debug.LogError("no Player"); yield break; }
    var anim = go.GetComponent<Animator>();
    var ctl = go.GetComponent<InkWash.Player.PlayerController>();
    var ik = go.GetComponent<InkWash.Player.FootIK>();
    var stance = go.GetComponentInChildren<InkWash.Player.CombatStance>(true);
    var pose = go.GetComponentInChildren<InkWash.Effects.WeaponHandPose>(true);
    var vfx = go.GetComponentInChildren<InkWash.Effects.SwordVfx>(true);
    var cc = go.GetComponent<CharacterController>();
    var rig = Camera.main != null ? Camera.main.GetComponent<InkWash.CameraRig.ThirdPersonCamera>() : null;

    // ★ 关键：关掉「静置 6 秒自动收剑」，否则测量中途武器被搬回背后
    float oldExit = stance != null ? stance.combatExitDelay : 6f;
    if (stance != null) stance.combatExitDelay = 99999f;

    Vector3 startPos = go.transform.position;
    Quaternion startRot = go.transform.rotation;
    void Park()
    {
        if (cc != null) cc.enabled = false;
        go.transform.position = startPos;
        go.transform.rotation = startRot;
        if (cc != null) cc.enabled = true;
    }

    var weapon = vfx != null ? vfx.WeaponInstance : null;
    var weaponRends = weapon != null
        ? new HashSet<Renderer>(weapon.GetComponentsInChildren<Renderer>(true))
        : new HashSet<Renderer>();
    var smrs = go.GetComponentsInChildren<SkinnedMeshRenderer>(true)
        .Where(s => s != null && s.enabled && !weaponRends.Contains(s)).ToArray();
    sb.AppendLine("武器实例 = " + (weapon != null ? weapon.name : "<null>")
        + "  武器渲染器 = " + weaponRends.Count
        + "  角色蒙皮网格 = " + smrs.Length);

    var bakeMesh = new Mesh { name = "Q7_Bake" };
    var vbuf = new List<Vector3>(16384);

    Transform Bone(HumanBodyBones b) { return anim.GetBoneTransform(b); }

    bool GroundY(out float y)
    {
        y = go.transform.position.y; bool ok = false; float best = float.MinValue;
        var hits = Physics.RaycastAll(go.transform.position + Vector3.up * 1.0f, Vector3.down, 3.5f,
            ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < hits.Length; i++)
        {
            var tr = hits[i].collider.transform;
            if (tr == go.transform || tr.IsChildOf(go.transform)) continue;
            if (hits[i].point.y > best) { best = hits[i].point.y; ok = true; }
        }
        if (ok) y = best;
        return ok;
    }

    // ---------------- 相机（隐藏其它渲染器）----------------
    var camGo = new GameObject("TmpPose7Cam");
    var tcam = camGo.AddComponent<Camera>();
    tcam.CopyFrom(Camera.main);
    tcam.tag = "Untagged"; tcam.enabled = false;
    tcam.clearFlags = CameraClearFlags.SolidColor;
    tcam.backgroundColor = new Color(0.15f, 0.16f, 0.19f);

    var keep = new HashSet<Renderer>(go.GetComponentsInChildren<Renderer>());
    var hidden = new List<Renderer>();
    foreach (var r in Object.FindObjectsOfType<Renderer>())
        if (r.enabled && !keep.Contains(r)) { r.enabled = false; hidden.Add(r); }

    const int W = 460, H = 600;

    Texture2D Shot(Vector3 center, Vector3 dir, float orthoSize, Quaternion? look = null)
    {
        Vector3 cp = center + dir * 6f;
        tcam.orthographic = true; tcam.orthographicSize = orthoSize;
        tcam.transform.position = cp;
        tcam.transform.rotation = look ?? Quaternion.LookRotation(center - cp, Vector3.up);
        var rt = RenderTexture.GetTemporary(W, H, 24);
        tcam.targetTexture = rt; tcam.Render(); tcam.targetTexture = null;
        var prev = RenderTexture.active; RenderTexture.active = rt;
        var tile = new Texture2D(W, H, TextureFormat.RGB24, false);
        tile.ReadPixels(new Rect(0, 0, W, H), 0, 0); tile.Apply();
        RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt);
        return tile;
    }

    Vector3 BodyCenter() { return new Vector3(go.transform.position.x, go.transform.position.y + 1.05f, go.transform.position.z); }
    Vector3 FeetCenter() { float gy; GroundY(out gy); return new Vector3(go.transform.position.x, gy + 0.20f, go.transform.position.z); }
    Vector3 GameDir() { return (-go.transform.forward * 0.75f + go.transform.right * 0.55f + Vector3.up * 0.38f).normalized; }

    // ================= ③ 跑步浮空（活体，每帧）=================
    sb.AppendLine();
    sb.AppendLine("=== ③ 跑步浮空（活体，每帧 4 秒；网格过滤已修正）===");
    if (ctl != null) ctl.BeginInputOverride();
    if (stance != null) stance.ForceStance(true);
    Park();
    if (ctl != null) ctl.SetInjectedMove(new Vector2(0f, 1f), true);
    var runShots = new List<Texture2D>();
    {
        float tw = Time.time; while (Time.time - tw < 1.6f) yield return null;
        var cbuf = new List<AnimatorClipInfo>(4);
        anim.GetCurrentAnimatorClipInfo(0, cbuf);
        sb.AppendLine("实际片段 = " + ((cbuf.Count > 0 && cbuf[0].clip != null) ? cbuf[0].clip.name : "?")
            + "  速度 " + (ctl != null ? ctl.CurrentSpeed : 0f).ToString("F2"));

        var rel = new List<float>();
        float t0 = Time.time; int n = 0;
        while (Time.time - t0 < 4.0f)
        {
            float gy; bool gok = GroundY(out gy);
            if (gok)
            {
                float lo = float.MaxValue;
                for (int k = 0; k < smrs.Length; k++)
                {
                    var s = smrs[k];
                    s.BakeMesh(bakeMesh);
                    bakeMesh.GetVertices(vbuf);
                    var m = s.transform.localToWorldMatrix;
                    for (int i = 0; i < vbuf.Count; i++)
                    {
                        float y = m.m10 * vbuf[i].x + m.m11 * vbuf[i].y + m.m12 * vbuf[i].z + m.m13;
                        if (y < lo) lo = y;
                    }
                }
                if (lo < float.MaxValue)
                    rel.Add(lo - (ik != null ? ik.AppliedBodyOffset : 0f) - gy);
            }
            if (n % 7 == 0 && runShots.Count < 16)
            {
                runShots.Add(Shot(FeetCenter(), Vector3.up, 0.55f, Quaternion.LookRotation(Vector3.down, go.transform.forward)));
                runShots.Add(Shot(BodyCenter(), GameDir(), 1.40f));
            }
            n++;
            yield return null;
        }
        sb.AppendLine("采样帧数 = " + n);
        sb.AppendLine(Stat("浮空量(m) 全体网格", rel));
        float mn = rel.Count > 0 ? rel.Min() : 0f;
        float suggest = -mn + (ik != null ? ik.groundClearance : 0.005f);
        sb.AppendLine(">>> 最深穿透 = " + mn.ToString("F4") + " m  ⇒ 建议 stateOffsets 偏移 = " + suggest.ToString("F4")
            + "   现值 = " + (ik != null ? ik.BodyBase : 0f).ToString("F4"));
        sb.AppendLine("（判读：浮空量应 ≈ +0.005；越正越飘。建议值 = 最深处抬到 groundClearance）");
    }
    if (ctl != null) ctl.SetInjectedMove(Vector2.zero, false);
    Park();
    { float t = Time.time; while (Time.time - t < 1.0f) yield return null; }
    // 跑步出图：8 帧脚部俯视 + 8 帧全身斜后上
    {
        var sheetF = new Texture2D(W * 8, H, TextureFormat.RGB24, false);
        for (int c = 0; c < 8 && c * 2 < runShots.Count; c++)
            sheetF.SetPixels(c * W, 0, W, H, runShots[c * 2].GetPixels());
        sheetF.Apply();
        File.WriteAllBytes(Path.Combine(imgDir, "_run_feet_top.png"), sheetF.EncodeToPNG());
        Object.Destroy(sheetF);

        var sheetB = new Texture2D(W * 8, H, TextureFormat.RGB24, false);
        for (int c = 0; c < 8 && c * 2 + 1 < runShots.Count; c++)
            sheetB.SetPixels(c * W, 0, W, H, runShots[c * 2 + 1].GetPixels());
        sheetB.Apply();
        File.WriteAllBytes(Path.Combine(imgDir, "_run_body.png"), sheetB.EncodeToPNG());
        Object.Destroy(sheetB);
    }
    foreach (var t in runShots) Object.Destroy(t);
    runShots.Clear();

    // ================= ① 持剑待机候选 =================
    sb.AppendLine();
    sb.AppendLine("=== ① 持剑待机候选（活体战斗姿态，武器确认在手上）===");
    if (stance != null) stance.ForceStance(true);
    Park();
    if (ctl != null) ctl.BeginInputOverride();
    {
        float t = Time.time; while (Time.time - t < 0.9f) yield return null;
        sb.AppendLine("挂点 = " + (stance != null ? stance.WeaponMountPath : "?")
            + "  InCombat = " + (stance != null ? stance.InCombat : false)
            + "  Idle 片段 = " + (stance != null ? stance.CurrentIdleClipName : "?"));
        float gy; GroundY(out gy);
        var hips = Bone(HumanBodyBones.Hips);
        var lf = Bone(HumanBodyBones.LeftFoot); var rf = Bone(HumanBodyBones.RightFoot);
        Vector3 lr = (lf != null && rf != null) ? lf.position - rf.position : Vector3.zero; lr.y = 0f;
        sb.AppendLine("双脚横距 " + Mathf.Abs(Vector3.Dot(lr, go.transform.right)).ToString("F3")
            + " m  纵距 " + Mathf.Abs(Vector3.Dot(lr, go.transform.forward)).ToString("F3")
            + " m  髋高 " + (hips != null ? (hips.position.y - gy).ToString("F3") : "?") + " m");

        var liveIdle = Shot(BodyCenter(), GameDir(), 1.40f);
        var liveIdleFront = Shot(BodyCenter(), go.transform.forward, 1.40f);
        var liveIdleFeet = Shot(FeetCenter(), Vector3.up, 0.55f, Quaternion.LookRotation(Vector3.down, go.transform.forward));
        var sheet = new Texture2D(W * 3, H, TextureFormat.RGB24, false);
        sheet.SetPixels(0, 0, W, H, liveIdle.GetPixels());
        sheet.SetPixels(W, 0, W, H, liveIdleFront.GetPixels());
        sheet.SetPixels(2 * W, 0, W, H, liveIdleFeet.GetPixels());
        sheet.Apply();
        File.WriteAllBytes(Path.Combine(imgDir, "_live_idle.png"), sheet.EncodeToPNG());
        Object.Destroy(sheet); Object.Destroy(liveIdle); Object.Destroy(liveIdleFront); Object.Destroy(liveIdleFeet);
        sb.AppendLine("活体待机图 = _live_idle.png（列：斜后上游戏机位 / 正视 / 脚部俯视）");
    }

    // ================= 对比：非战斗待机（对照） + 候选片段 =================
    AnimationClip ClipAt(string p, string n)
    {
        foreach (var o in AssetDatabase.LoadAllAssetsAtPath(p))
        {
            var c = o as AnimationClip;
            if (c != null && !c.name.StartsWith("__preview__") && (n == null || c.name == n)) return c;
        }
        return null;
    }
    AnimationClip Proj(string n) { return AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/_Project/Animations/" + n + ".anim"); }
    string ual1 = "Assets/ThirdParty/Quaternius/UniversalAnimationLibrary/Unity/AnimationLibrary_Unity_Standard.fbx";
    string kkMelee = "Assets/ThirdParty/KayKit/Animations/Rig_Medium/Rig_Medium_CombatMelee.fbx";

    // 非战斗待机对照（活体）
    if (stance != null) stance.ForceStance(false);
    { float t = Time.time; while (Time.time - t < 0.8f) yield return null; }
    var nonCombat = Shot(BodyCenter(), GameDir(), 1.40f);

    // ★ 拍完对照必须把武器搬回手上 —— 否则后面所有候选的剑都还在背后
    if (stance != null) stance.ForceStance(true);
    { float t = Time.time; while (Time.time - t < 0.4f) yield return null; }
    sb.AppendLine("冻结前 挂点 = " + (stance != null ? stance.WeaponMountPath : "?"));

    if (ctl != null) ctl.enabled = false;
    if (ik != null) ik.enabled = false;
    if (stance != null) stance.enabled = false;
    if (pose != null) pose.enabled = false;
    bool animWas = anim.enabled;
    anim.enabled = false;

    var cands = new List<(string label, AnimationClip clip)>
    {
        ("A 现用 Sword_Idle_Loop", Proj("Sword_Idle_Loop")),
        ("B 烘焙 Carry_A         ", Proj("Baked/Idle_Carry_A")),
        ("C 烘焙 Carry_AT        ", Proj("Baked/Idle_Carry_AT")),
        ("D 烘焙 Carry_S         ", Proj("Baked/Idle_Carry_S")),
        ("E UAL1 Rig|Idle_Loop   ", ClipAt(ual1, "Rig|Idle_Loop")),
        ("F KK   Melee_2H_Idle   ", ClipAt(kkMelee, "Melee_2H_Idle")),
        ("G 旧   Feng_Idle_Loop  ", Proj("Feng_Idle_Loop")),
    };
    var cl = cands.Where(x => x.clip != null).ToList();
    sb.AppendLine();
    sb.AppendLine("=== ①-b 候选（冻结采样 + 出图）===");
    var sheet2 = new Texture2D(W * 3, H * (cl.Count + 1), TextureFormat.RGB24, false);
    sheet2.SetPixels(0, cl.Count * H, W, H, nonCombat.GetPixels());
    sb.AppendLine("图行 0 = 【对照】非战斗待机（Rig|Idle_Loop 活体）");
    Object.Destroy(nonCombat);
    var swordHead = Bone(HumanBodyBones.Head);
    for (int r = 0; r < cl.Count; r++)
    {
        cl[r].clip.SampleAnimation(go, cl[r].clip.length * 0.40f);
        yield return null;
        // 采样后把武器重新摆到手上：SampleAnimation 不动武器（它是独立 GameObject），
        // 但武器的世界位置取决于手骨 ⇒ 不用管，手骨动了武器自然跟着动
        var a = Shot(BodyCenter(), GameDir(), 1.40f);
        var b = Shot(BodyCenter(), go.transform.forward, 1.40f);
        var c2 = Shot(FeetCenter(), Vector3.up, 0.55f, Quaternion.LookRotation(Vector3.down, go.transform.forward));
        int rowY = (cl.Count - 1 - r) * H;
        sheet2.SetPixels(0, rowY, W, H, a.GetPixels());
        sheet2.SetPixels(W, rowY, W, H, b.GetPixels());
        sheet2.SetPixels(2 * W, rowY, W, H, c2.GetPixels());
        Object.Destroy(a); Object.Destroy(b); Object.Destroy(c2);

        float gy; GroundY(out gy);
        var hips = Bone(HumanBodyBones.Hips);
        var lf = Bone(HumanBodyBones.LeftFoot); var rf = Bone(HumanBodyBones.RightFoot);
        Vector3 lr = (lf != null && rf != null) ? lf.position - rf.position : Vector3.zero; lr.y = 0f;
        sb.AppendLine(string.Format("图行 {0} = {1} 横距 {2:F3} 纵距 {3:F3} 髋高 {4:F3}",
            r + 1, cl[r].label,
            Mathf.Abs(Vector3.Dot(lr, go.transform.right)),
            Mathf.Abs(Vector3.Dot(lr, go.transform.forward)),
            hips != null ? hips.position.y - gy : 0f));
    }
    sheet2.Apply();
    File.WriteAllBytes(Path.Combine(imgDir, "_idle_cands.png"), sheet2.EncodeToPNG());
    Object.Destroy(sheet2);
    sb.AppendLine("图列 = 斜后上游戏机位 / 正视 / 脚部俯视");

    foreach (var r in hidden) if (r != null) r.enabled = true;
    Object.Destroy(camGo);
    Object.Destroy(bakeMesh);

    anim.enabled = animWas;
    if (ctl != null) { ctl.enabled = true; ctl.EndInputOverride(); }
    if (ik != null) ik.enabled = true;
    if (stance != null) { stance.enabled = true; stance.ForceStance(false); stance.combatExitDelay = oldExit; }
    if (rig != null) rig.SetMouseLookEnabled(true);

    File.WriteAllText(Path.Combine(projRoot, "Tools/reports/q_pose7.txt"), sb.ToString());
    Debug.Log("[q_pose7] done");
    yield return null;
}

string Stat(string label, List<float> xs)
{
    if (xs.Count == 0) return label + " <无样本>";
    var s = xs.OrderBy(x => x).ToList();
    float p = s[(int)Mathf.Clamp(Mathf.Floor(s.Count * 0.05f), 0, s.Count - 1)];
    return string.Format("{0}  min {1,7:F3}  p5 {2,7:F3}  中位 {3,7:F3}  max {4,7:F3}  n={5}",
        label, s[0], p, s[s.Count / 2], s[s.Count - 1], xs.Count);
}

return Body();
