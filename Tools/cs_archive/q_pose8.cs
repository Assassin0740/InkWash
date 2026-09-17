// q_pose8.cs —— 本轮取证 v4：全部改成「活体实测」+ 给出鞋底（而非袍摆）的贴地判据
//
// 为什么推翻 v3 的片段采样：`AnimationClip.SampleAnimation` 在关闭 Animator 后
// 写的是 Animator 内部的人形姿势缓存，读 Transform 拿到的是**上一状态的骨头**，
// 于是 v3 里 7 个候选量出来几乎一模一样（假数据）。活体测才是真值。
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
    string imgDir = Path.Combine(projRoot, "Tools/screenshots/pose8");
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
        go.transform.position = startPos;
        go.transform.rotation = startRot;
        if (cc != null) cc.enabled = true;
    }

    var weapon = vfx != null ? vfx.WeaponInstance : null;
    var weaponRends = weapon != null ? new HashSet<Renderer>(weapon.GetComponentsInChildren<Renderer>(true)) : new HashSet<Renderer>();
    var smrs = go.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(s => s != null && s.enabled && !weaponRends.Contains(s)).ToArray();

    Transform Bone(HumanBodyBones b) { return anim.GetBoneTransform(b); }

    // ---- 鞋底顶点掩码：权重落在 Foot/Toes 骨上的顶点 ----
    var shoeMask = new Dictionary<SkinnedMeshRenderer, bool[]>();
    int maskTotal = 0, maskHit = 0;
    foreach (var s in smrs)
    {
        var mesh = s.sharedMesh; if (mesh == null) continue;
        var ids = new HashSet<int>();
        var foots = new[] { Bone(HumanBodyBones.LeftFoot), Bone(HumanBodyBones.LeftToes), Bone(HumanBodyBones.RightFoot), Bone(HumanBodyBones.RightToes) };
        var bones = s.bones;
        for (int i = 0; i < bones.Length; i++)
            if (bones[i] != null && foots.Any(f => f != null && f == bones[i])) ids.Add(i);
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
            maskTotal++;
            if (mask[v]) maskHit++;
        }
        shoeMask[s] = mask;
    }
    sb.AppendLine("蒙皮网格 = " + smrs.Length + "  总顶点 = " + maskTotal + "  鞋底顶点 = " + maskHit
        + "（" + (maskTotal > 0 ? (100f * maskHit / maskTotal).ToString("F1") : "0") + "%）");
    sb.AppendLine();

    var bakeMesh = new Mesh { name = "Q8_Bake" };
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
            s.BakeMesh(bakeMesh);
            bakeMesh.GetVertices(vbuf);
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

    // ---- 刀身 ----
    Vector3 BladeTip()
    {
        if (weapon == null) return go.transform.position + Vector3.up;
        var rs = weapon.GetComponentsInChildren<Renderer>().Where(r => r is MeshRenderer || r is SkinnedMeshRenderer).ToArray();
        if (rs.Length == 0) return weapon.transform.position;
        Bounds b = rs[0].bounds;
        for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
        Vector3 sz = b.size;
        Vector3 ax = sz.x >= sz.y && sz.x >= sz.z ? Vector3.right : (sz.y >= sz.z ? Vector3.up : Vector3.forward);
        float half = Mathf.Max(sz.x, Mathf.Max(sz.y, sz.z)) * 0.5f;
        Vector3 p1 = b.center + ax * half, p2 = b.center - ax * half;
        Vector3 grip = weapon.transform.position;
        return Vector3.Distance(p1, grip) >= Vector3.Distance(p2, grip) ? p1 : p2;
    }

    // ---- 相机 ----
    var camGo = new GameObject("TmpPose8Cam");
    var tcam = camGo.AddComponent<Camera>();
    tcam.CopyFrom(Camera.main);
    tcam.tag = "Untagged"; tcam.enabled = false;
    tcam.clearFlags = CameraClearFlags.SolidColor;
    tcam.backgroundColor = new Color(0.15f, 0.16f, 0.19f);
    var keep = new HashSet<Renderer>(go.GetComponentsInChildren<Renderer>());
    var hidden = new List<Renderer>();
    foreach (var r in Object.FindObjectsOfType<Renderer>()) if (r.enabled && !keep.Contains(r)) { r.enabled = false; hidden.Add(r); }

    const int W = 400, H = 520;
    Texture2D Shot(Vector3 center, Vector3 dir, float ortho, Quaternion? look = null)
    {
        Vector3 cp = center + dir * 6f;
        tcam.orthographic = true; tcam.orthographicSize = ortho;
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
    Vector3 BodyC() { return new Vector3(go.transform.position.x, go.transform.position.y + 1.05f, go.transform.position.z); }
    Vector3 FeetC() { float gy; GroundY(out gy); return new Vector3(go.transform.position.x, gy + 0.22f, go.transform.position.z); }
    Vector3 GameDir() { return (-go.transform.forward * 0.75f + go.transform.right * 0.55f + Vector3.up * 0.38f).normalized; }

    // ---- 脚部基准（非战斗待机，多帧平均）----
    Vector3 vIdleL = Vector3.forward, vIdleR = Vector3.forward;
    if (ctl != null) ctl.BeginInputOverride();
    if (ctl != null) ctl.SetInjectedMove(Vector2.zero, false);
    if (stance != null) stance.ForceStance(false);
    Park();
    {
        float t = Time.time; while (Time.time - t < 1.2f) yield return null;
        var fl = Bone(HumanBodyBones.LeftFoot); var tl = Bone(HumanBodyBones.LeftToes);
        var fr = Bone(HumanBodyBones.RightFoot); var tr = Bone(HumanBodyBones.RightToes);
        Vector3 sl = Vector3.zero, sr = Vector3.zero;
        for (int i = 0; i < 30; i++)
        {
            sl += (tl.position - fl.position).normalized;
            sr += (tr.position - fr.position).normalized;
            yield return null;
        }
        vIdleL = sl.normalized; vIdleR = sr.normalized;
        sb.AppendLine("脚基准向量（非战斗待机）= L" + vIdleL.ToString("F3") + " R" + vIdleR.ToString("F3"));

        float gy; GroundY(out gy);
        var lf = Bone(HumanBodyBones.LeftFoot); var rf = Bone(HumanBodyBones.RightFoot);
        Vector3 lr = lf.position - rf.position; lr.y = 0f;
        var hips = Bone(HumanBodyBones.Hips);
        sb.AppendLine("【对照】非战斗待机：双脚横距 " + Mathf.Abs(Vector3.Dot(lr, go.transform.right)).ToString("F3")
            + " m  纵距 " + Mathf.Abs(Vector3.Dot(lr, go.transform.forward)).ToString("F3")
            + " m  髋高 " + (hips.position.y - gy).ToString("F3") + " m");
        var img = Shot(BodyC(), GameDir(), 1.40f);
        File.WriteAllBytes(Path.Combine(imgDir, "_ctrl_noncombat.png"), img.EncodeToPNG());
        Object.Destroy(img);
        sb.AppendLine();
    }

    // ================= ① 持剑待机候选（活体）=================
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

    var cands = new List<(string label, AnimationClip clip)>
    {
        ("A 现用 Sword_Idle_Loop", Proj("Sword_Idle_Loop")),
        ("B 烘焙 Idle_Carry_A   ", Proj("Baked/Idle_Carry_A")),
        ("C 烘焙 Idle_Carry_AT  ", Proj("Baked/Idle_Carry_AT")),
        ("D 烘焙 Idle_Carry_S   ", Proj("Baked/Idle_Carry_S")),
        ("E UAL1 Rig|Idle_Loop  ", ClipAt(ual1, "Rig|Idle_Loop")),
        ("F KK   Melee_2H_Idle  ", ClipAt(kkMelee, "Melee_2H_Idle")),
        ("G 旧   Feng_Idle_Loop ", Proj("Feng_Idle_Loop")),
    };
    sb.AppendLine("=== ① 持剑待机候选（活体；每项等 0.6s 再采 24 帧平均）===");
    sb.AppendLine("候选                    实际播放片段          双脚横距  纵距   髋高    脚偏角L 脚偏角R  剑→面部  剑轴·竖直 剑尖离地");
    var head = Bone(HumanBodyBones.Head);
    var cbuf = new List<AnimatorClipInfo>(4);
    var shots = new List<(string label, Texture2D img)>();
    foreach (var cd in cands)
    {
        if (cd.clip == null) { sb.AppendLine(cd.label + "  ★ 找不到"); continue; }
        if (stance != null)
        {
            stance.combatIdleClip = cd.clip;
            stance.ForceStance(false);
            stance.ForceStance(true);
        }
        float wt = Time.time; while (Time.time - wt < 0.6f) yield return null;

        float lat = 0, fa = 0, hip = 0, devL = 0, devR = 0, dFace = 0, bladeV = 0, tipH = 0;
        string playing = "?";
        int N = 24;
        for (int i = 0; i < N; i++)
        {
            float gy; GroundY(out gy);
            var lf = Bone(HumanBodyBones.LeftFoot); var rf = Bone(HumanBodyBones.RightFoot);
            var tl = Bone(HumanBodyBones.LeftToes); var tr = Bone(HumanBodyBones.RightToes);
            var hips = Bone(HumanBodyBones.Hips);
            Vector3 lr = lf.position - rf.position; lr.y = 0f;
            lat += Mathf.Abs(Vector3.Dot(lr, go.transform.right));
            fa += Mathf.Abs(Vector3.Dot(lr, go.transform.forward));
            hip += hips.position.y - gy;
            devL += Vector3.Angle(vIdleL, (tl.position - lf.position).normalized);
            devR += Vector3.Angle(vIdleR, (tr.position - rf.position).normalized);
            if (weapon != null)
            {
                var face = head.position + head.up * 0.07f + head.forward * 0.07f;
                Vector3 grip = weapon.transform.position, tip = BladeTip();
                dFace += DistPointSeg(face, grip, tip);
                bladeV += Vector3.Angle((tip - grip).normalized, Vector3.up);
                tipH += tip.y - gy;
            }
            cbuf.Clear(); anim.GetCurrentAnimatorClipInfo(0, cbuf);
            if (cbuf.Count > 0 && cbuf[0].clip != null) playing = cbuf[0].clip.name;
            yield return null;
        }
        sb.AppendLine(string.Format("{0} {1,-20} {2,8:F3}  {3,5:F3}  {4,6:F3}m  {5,7:F1}° {6,7:F1}°  {7,7:F3}m  {8,8:F1}°  {9,7:F3}m",
            cd.label, playing, lat / N, fa / N, hip / N, devL / N, devR / N, dFace / N, bladeV / N, tipH / N));

        shots.Add((cd.label, Shot(BodyC(), GameDir(), 1.40f)));
        shots.Add((cd.label, Shot(BodyC(), go.transform.forward, 1.40f)));
    }
    {
        var sh = new Texture2D(W * 2, H * shots.Count, TextureFormat.RGB24, false);
        for (int i = 0; i < shots.Count; i++)
            sh.SetPixels((i % 2) * W, (shots.Count - 1 - i) * H, W, H, shots[i].img.GetPixels());
        sh.Apply();
        File.WriteAllBytes(Path.Combine(imgDir, "_idle_ab.png"), sh.EncodeToPNG());
        Object.Destroy(sh);
        for (int i = 0; i < shots.Count; i += 2)
            sb.AppendLine("  A/B 图行 " + (i / 2) + " = " + shots[i].label + "（左=斜后上游戏机位 / 右=正视）");
        foreach (var s in shots) Object.Destroy(s.img);
        shots.Clear();
    }
    sb.AppendLine("判读：双脚横距 参考【对照】非战斗待机；脚偏角 越接近 0 越像正常站姿；剑→面部 >0.45m 才不糊脸。");

    // ================= ③ 跑步浮空 + ② 脚朝向（活体）=================
    sb.AppendLine();
    sb.AppendLine("=== ③ 跑步浮空（分「全体网格」与「只鞋底」两口径）+ ② 脚朝向 ===");
    if (stance != null) stance.ForceStance(true);
    Park();
    if (ctl != null) ctl.SetInjectedMove(new Vector2(0f, 1f), true);
    var runShots = new List<Texture2D>();
    {
        float tw = Time.time; while (Time.time - tw < 1.6f) yield return null;
        cbuf.Clear(); anim.GetCurrentAnimatorClipInfo(0, cbuf);
        sb.AppendLine("实际片段 = " + ((cbuf.Count > 0 && cbuf[0].clip != null) ? cbuf[0].clip.name : "?"));
        var aAll = new List<float>(); var aShoe = new List<float>();
        var devL = new List<float>(); var devR = new List<float>();
        float t0 = Time.time; int n = 0;
        while (Time.time - t0 < 4.0f)
        {
            float gy; bool gok = GroundY(out gy);
            if (gok)
            {
                float off = ik != null ? ik.AppliedBodyOffset : 0f;
                float la = Lowest(false), ls = Lowest(true);
                if (la < float.MaxValue) aAll.Add(la - off - gy);
                if (ls < float.MaxValue) aShoe.Add(ls - off - gy);
                var fl = Bone(HumanBodyBones.LeftFoot); var tl = Bone(HumanBodyBones.LeftToes);
                var fr = Bone(HumanBodyBones.RightFoot); var tr = Bone(HumanBodyBones.RightToes);
                devL.Add(Vector3.Angle(vIdleL, (tl.position - fl.position).normalized));
                devR.Add(Vector3.Angle(vIdleR, (tr.position - fr.position).normalized));
            }
            if (n % 8 == 0 && runShots.Count < 32)
            {
                runShots.Add(Shot(FeetC(), -go.transform.right, 0.75f));                                  // 侧视（从角色右侧看）脚部
                runShots.Add(Shot(BodyC(), GameDir(), 1.40f));                                            // 游戏机位
            }
            n++;
            yield return null;
        }
        sb.AppendLine("采样帧数 = " + n);
        sb.AppendLine(Stat("浮空量 全体网格(m)", aAll));
        sb.AppendLine(Stat("浮空量 只鞋底(m)  ", aShoe));
        sb.AppendLine(Stat("左脚偏角(°)      ", devL));
        sb.AppendLine(Stat("右脚偏角(°)      ", devR));
        float mAll = aAll.Count > 0 ? aAll.Min() : 0f, mShoe = aShoe.Count > 0 ? aShoe.Min() : 0f;
        float clear = ik != null ? ik.groundClearance : 0.005f;
        sb.AppendLine(">>> 若按「全体网格最深」贴地：偏移 = " + (-mAll + clear).ToString("F4"));
        sb.AppendLine(">>> 若按「只鞋底最深」贴地：偏移 = " + (-mShoe + clear).ToString("F4")
            + "（差 " + (mAll - mShoe).ToString("F4") + " m；差大说明袍摆比鞋低，被袍摆拖住了）");
        sb.AppendLine(">>> 若按「鞋底中位」贴地：偏移 = " + (-aShoe.OrderBy(x => x).ToList()[aShoe.Count / 2] + clear).ToString("F4")
            + "  ← 这一档最不容易整体飘");
        sb.AppendLine("现值偏移 = " + (ik != null ? ik.BodyBase : 0f).ToString("F4"));
        sb.AppendLine("判读：脚偏角 >40° 才算「脚被拧了」（正常跑步踝关节前后摆 20~35°）。");
    }
    if (ctl != null) ctl.SetInjectedMove(Vector2.zero, false);
    Park();
    {
        var shF = new Texture2D(W * 8, H, TextureFormat.RGB24, false);
        var shB = new Texture2D(W * 8, H, TextureFormat.RGB24, false);
        for (int c = 0; c < 8; c++)
        {
            if (c * 2 < runShots.Count) shF.SetPixels(c * W, 0, W, H, runShots[c * 2].GetPixels());
            if (c * 2 + 1 < runShots.Count) shB.SetPixels(c * W, 0, W, H, runShots[c * 2 + 1].GetPixels());
        }
        shF.Apply(); shB.Apply();
        File.WriteAllBytes(Path.Combine(imgDir, "_run_feet_side.png"), shF.EncodeToPNG());
        File.WriteAllBytes(Path.Combine(imgDir, "_run_body.png"), shB.EncodeToPNG());
        Object.Destroy(shF); Object.Destroy(shB);
    }
    foreach (var t in runShots) Object.Destroy(t);

    foreach (var r in hidden) if (r != null) r.enabled = true;
    Object.Destroy(camGo); Object.Destroy(bakeMesh);

    if (ctl != null) ctl.EndInputOverride();
    if (stance != null) { stance.combatIdleClip = oldCombatIdle; stance.ForceStance(false); stance.combatExitDelay = oldExit; }
    if (rig != null) rig.SetMouseLookEnabled(true);

    File.WriteAllText(Path.Combine(projRoot, "Tools/reports/q_pose8.txt"), sb.ToString());
    Debug.Log("[q_pose8] done");
    yield return null;
}

string Stat(string label, List<float> xs)
{
    if (xs.Count == 0) return label + " <无样本>";
    var s = xs.OrderBy(x => x).ToList();
    float p5 = s[(int)Mathf.Clamp(Mathf.Floor(s.Count * 0.05f), 0, s.Count - 1)];
    return string.Format("{0}  min {1,7:F3}  p5 {2,7:F3}  中位 {3,7:F3}  max {4,7:F3}  n={5}",
        label, s[0], p5, s[s.Count / 2], s[s.Count - 1], xs.Count);
}

float DistPointSeg(Vector3 p, Vector3 a, Vector3 b)
{
    Vector3 ab = b - a; float L2 = ab.sqrMagnitude;
    if (L2 < 1e-9f) return Vector3.Distance(p, a);
    float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / L2);
    return Vector3.Distance(p, a + ab * t);
}

return Body();
