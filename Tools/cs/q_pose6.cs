// q_pose6.cs —— 本轮三问取证 v2
//   ① 持剑待机候选：站姿 / 剑位 度量 + 出图（含烘焙的 6 个候选）
//   ② 跑步脚朝向：用「以非战斗待机为基准的脚骨局部轴」测 yaw，避开 toes-foot 投影会翻转的坑
//   ③ 跑步浮空：逐帧量「网格最低点 − 真实地面」（整体 & 只取脚附近的顶点），给建议偏移
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
    string imgDir = Path.Combine(projRoot, "Tools/screenshots/pose6");
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

    Vector3 startPos = go.transform.position;
    Quaternion startRot = go.transform.rotation;
    void Park()
    {
        if (cc != null) cc.enabled = false;
        go.transform.position = startPos;
        go.transform.rotation = startRot;
        if (cc != null) cc.enabled = true;
    }

    // ---------------- 度量辅助 ----------------
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

    // 自己烘网格量最低点（可选只统计脚附近的顶点）
    var smrs = go.GetComponentsInChildren<SkinnedMeshRenderer>(true)
        .Where(s => s != null && s.enabled && !vfx.transform.IsChildOf(s.transform) && !s.transform.IsChildOf(vfx.transform))
        .ToArray();
    var bakeMesh = new Mesh { name = "Q6_Bake" };
    var vbuf = new List<Vector3>(16384);

    float LowestMesh(bool feetOnly, float reachM)
    {
        float lo = float.MaxValue;
        var fl = Bone(HumanBodyBones.LeftToes); var fr = Bone(HumanBodyBones.RightToes);
        for (int k = 0; k < smrs.Length; k++)
        {
            var s = smrs[k];
            s.BakeMesh(bakeMesh);
            bakeMesh.GetVertices(vbuf);
            var m = s.transform.localToWorldMatrix;
            for (int i = 0; i < vbuf.Count; i++)
            {
                var w = m.MultiplyPoint3x4(vbuf[i]);
                if (w.y >= lo) continue;
                if (feetOnly)
                {
                    bool near = false;
                    if (fl != null) { var d = w - fl.position; d.y = 0f; if (d.sqrMagnitude < reachM * reachM) near = true; }
                    if (!near && fr != null) { var d = w - fr.position; d.y = 0f; if (d.sqrMagnitude < reachM * reachM) near = true; }
                    if (!near) continue;
                }
                lo = w.y;
            }
        }
        return lo == float.MaxValue ? float.NaN : lo;
    }

    // 脚「前方」的局部轴，用正常站姿标定（此时脚大致朝前）
    Vector3 axisFootL = Vector3.up, axisFootR = Vector3.up;

    float FootYaw(bool left, out float pitch)
    {
        pitch = 0f;
        var f = Bone(left ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot);
        if (f == null) return 0f;
        Vector3 d = f.TransformDirection(left ? axisFootL : axisFootR);
        pitch = Mathf.Asin(Mathf.Clamp(-d.normalized.y, -1f, 1f)) * Mathf.Rad2Deg;
        d.y = 0f;
        Vector3 fwd = go.transform.forward; fwd.y = 0f;
        if (d.sqrMagnitude < 1e-8f) return 0f;
        return Vector3.SignedAngle(fwd, d.normalized, Vector3.up);
    }

    // ============ ① 先标定脚骨基准轴（非战斗待机，脚正常朝前）============
    if (ctl != null) ctl.BeginInputOverride();
    if (ctl != null) ctl.SetInjectedMove(Vector2.zero, false);
    if (stance != null) stance.ForceStance(false);
    Park();
    { float t = Time.time; while (Time.time - t < 1.2f) yield return null; }
    {
        var fl = Bone(HumanBodyBones.LeftFoot); var fr = Bone(HumanBodyBones.RightFoot);
        if (fl != null) axisFootL = fl.InverseTransformDirection(go.transform.forward);
        if (fr != null) axisFootR = fr.InverseTransformDirection(go.transform.forward);
        float pl, pr;
        sb.AppendLine("=== 脚骨基准轴标定（非战斗待机）===");
        sb.AppendLine("左足 yaw " + FootYaw(true, out pl).ToString("F1") + "°  右足 yaw " + FootYaw(false, out pr).ToString("F1")
            + "°   （应接近 0° —— 非 0 的残差是本姿势自带的，后续读数要减掉）");
        sb.AppendLine("基准残差 左=" + FootYaw(true, out pl).ToString("F1") + " 右=" + FootYaw(false, out pr).ToString("F1"));
        sb.AppendLine();
    }

    // ============ ③ 跑步浮空 + ② 脚朝向（活体逐帧）============
    sb.AppendLine("=== ③ 跑步浮空 / ② 脚朝向（活体逐帧，约 4 秒）===");
    if (stance != null) stance.ForceStance(true);
    Park();
    if (ctl != null) ctl.SetInjectedMove(new Vector2(0f, 1f), true);
    {
        float tw = Time.time; while (Time.time - tw < 1.6f) yield return null;

        var cbuf = new List<AnimatorClipInfo>(4);
        anim.GetCurrentAnimatorClipInfo(0, cbuf);
        string clipName = (cbuf.Count > 0 && cbuf[0].clip != null) ? cbuf[0].clip.name : "?";
        sb.AppendLine("实际片段 = " + clipName + "  速度 " + (ctl != null ? ctl.CurrentSpeed : 0f).ToString("F2"));

        var fAll = new List<float>(); var fFeet = new List<float>();
        var yawL = new List<float>(); var yawR = new List<float>();
        float minGapAll = 999f; float minGapFeet = 999f;
        float t0 = Time.time; int n = 0;
        while (Time.time - t0 < 4.0f)
        {
            float gy; bool gok = GroundY(out gy);
            if (gok)
            {
                float lowAll = LowestMesh(false, 0f);
                float lowFeet = LowestMesh(true, 0.30f);
                // 减掉本帧已施加的整体偏移 ⇒ 得到「动画本体」的最低点
                float off = ik != null ? ik.AppliedBodyOffset : 0f;
                if (!float.IsNaN(lowAll))
                {
                    float rel = lowAll - off - gy;
                    fAll.Add(rel); minGapAll = Mathf.Min(minGapAll, rel);
                }
                if (!float.IsNaN(lowFeet))
                {
                    float rel = lowFeet - off - gy;
                    fFeet.Add(rel); minGapFeet = Mathf.Min(minGapFeet, rel);
                }
                float pl, pr;
                yawL.Add(FootYaw(true, out pl));
                yawR.Add(FootYaw(false, out pr));
            }
            n++;
            yield return null;
        }
        sb.AppendLine("采样帧数 = " + n);
        sb.AppendLine(Stat("浮空量 全体网格(m)", fAll));
        sb.AppendLine(Stat("浮空量 只脚附近(m)", fFeet));
        sb.AppendLine(Stat("左足 yaw(°)      ", yawL));
        sb.AppendLine(Stat("右足 yaw(°)      ", yawR));
        sb.AppendLine("FootIK: key=" + (ik != null ? (ik.CurrentOffsetKey ?? "<null>") : "?")
            + "  BodyBase=" + (ik != null ? ik.BodyBase : 0f).ToString("F4")
            + "  BodyLift=" + (ik != null ? ik.BodyLift : 0f).ToString("F4"));
        float suggestAll = -minGapAll + (ik != null ? ik.groundClearance : 0.005f);
        float suggestFeet = -minGapFeet + (ik != null ? ik.groundClearance : 0.005f);
        sb.AppendLine(">>> 建议偏移（按全体网格最低点）= " + suggestAll.ToString("F4") + "   现值 = "
            + (ik != null ? ik.BodyBase : 0f).ToString("F4") + "   差 = " + (suggestAll - (ik != null ? ik.BodyBase : 0f)).ToString("F4"));
        sb.AppendLine(">>> 建议偏移（只按脚附近最低点）= " + suggestFeet.ToString("F4"));
        sb.AppendLine("判读：两者接近 ⇒ 最低点是鞋不是袍摆，按全体网格取偏移即可；差很多 ⇒ 得看是不是袍摆先触地。");
    }
    if (ctl != null) ctl.SetInjectedMove(Vector2.zero, false);
    Park();
    { float t = Time.time; while (Time.time - t < 1.0f) yield return null; }

    // ---------------- 冻结 ----------------
    if (ctl != null) ctl.enabled = false;
    if (ik != null) ik.enabled = false;
    if (stance != null) stance.enabled = false;
    if (pose != null) pose.enabled = false;
    bool animWas = anim.enabled;
    anim.enabled = false;

    AnimationClip Sub(string p, string nm)
    {
        foreach (var o in AssetDatabase.LoadAllAssetsAtPath(p))
        {
            var c = o as AnimationClip;
            if (c != null && !c.name.StartsWith("__preview__") && (nm == null || c.name == nm)) return c;
        }
        return null;
    }
    AnimationClip Proj(string nm) { return AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/_Project/Animations/" + nm + ".anim"); }
    string ual1 = "Assets/ThirdParty/Quaternius/UniversalAnimationLibrary/Unity/AnimationLibrary_Unity_Standard.fbx";
    string ual2 = "Assets/ThirdParty/Quaternius/UniversalAnimationLibrary2/Unity/UAL2_Standard.fbx";
    string ki = "Assets/ThirdParty/KevinIglesias/Animations/Male/Movement/Run/HumanM@Run01_Forward.fbx";
    string kkMelee = "Assets/ThirdParty/KayKit/Animations/Rig_Medium/Rig_Medium_CombatMelee.fbx";

    // ---- 剑身/剑尖 ----
    var weapon = vfx != null ? vfx.WeaponInstance : null;
    if (weapon == null) { sb.AppendLine("★ 武器实例为空 —— 战斗姿态没起来"); }
    Vector3 BladeTip()
    {
        if (weapon == null) return go.transform.position + Vector3.up;
        var rs = weapon.GetComponentsInChildren<Renderer>()
            .Where(r => r is MeshRenderer || r is SkinnedMeshRenderer).ToArray();
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

    var cands = new List<(string label, AnimationClip clip, float nt)>
    {
        ("现用 Sword_Idle_Loop", Proj("Sword_Idle_Loop"), 0.40f),
        ("烘焙 Idle_Carry_A   ", Proj("Baked/Idle_Carry_A"), 0.40f),
        ("烘焙 Idle_Carry_AT  ", Proj("Baked/Idle_Carry_AT"), 0.40f),
        ("烘焙 Idle_Carry_B   ", Proj("Baked/Idle_Carry_B"), 0.40f),
        ("烘焙 Idle_Carry_C   ", Proj("Baked/Idle_Carry_C"), 0.40f),
        ("烘焙 Idle_Carry_S   ", Proj("Baked/Idle_Carry_S"), 0.40f),
        ("烘焙 Idle_Carry_F   ", Proj("Baked/Idle_Carry_F"), 0.40f),
        ("UAL1 Rig|Idle_Loop  ", Sub(ual1, "Rig|Idle_Loop"), 0.40f),
        ("UAL2 Shield_Loop    ", Sub(ual2, "Armature|Idle_Shield_Loop"), 0.40f),
        ("KK   Melee_2H_Idle  ", Sub(kkMelee, "Melee_2H_Idle"), 0.40f),
        ("旧   Feng_Idle_Loop ", Proj("Feng_Idle_Loop"), 0.40f),
    };

    sb.AppendLine();
    sb.AppendLine("=== ① 持剑待机候选（片段采样 @nt=0.40）===");
    sb.AppendLine("候选                  时长   双脚横距 双脚纵距 张开角  左外八  右外八  髋高    剑→面部  剑轴·竖直 剑尖离地 剑尖行程");
    var head = Bone(HumanBodyBones.Head);
    foreach (var cd in cands)
    {
        if (cd.clip == null) { sb.AppendLine(cd.label + "  ★ 找不到"); continue; }
        cd.clip.SampleAnimation(go, cd.clip.length * cd.nt);
        yield return null;
        float gy; GroundY(out gy);
        var hips = Bone(HumanBodyBones.Hips);
        var lf = Bone(HumanBodyBones.LeftFoot); var rf = Bone(HumanBodyBones.RightFoot);
        Vector3 lr = lf != null && rf != null ? lf.position - rf.position : Vector3.zero; lr.y = 0f;
        float lateral = Mathf.Abs(Vector3.Dot(lr, go.transform.right));
        float foreAft = Mathf.Abs(Vector3.Dot(lr, go.transform.forward));
        float pl, pr;
        float ol = -FootYaw(true, out pl), orr = FootYaw(false, out pr);

        float dFace = 0f, axisDeg = 0f, tipH = 0f, travel = 0f;
        if (weapon != null)
        {
            var face = head != null ? head.position + head.up * 0.07f + head.forward * 0.07f : Vector3.zero;
            Vector3 grip = weapon.transform.position; Vector3 tip = BladeTip();
            dFace = DistPointSeg(face, grip, tip);
            axisDeg = Vector3.Angle((tip - grip).normalized, Vector3.up);
            tipH = tip.y - gy;
            // 剑尖总行程（角色自身坐标系）
            Vector3 prev = Vector3.zero; bool first = true;
            for (int i = 0; i <= 60; i++)
            {
                cd.clip.SampleAnimation(go, cd.clip.length * i / 60f);
                Vector3 lp = go.transform.InverseTransformPoint(BladeTip());
                if (!first) travel += Vector3.Distance(prev, lp);
                prev = lp; first = false;
            }
            cd.clip.SampleAnimation(go, cd.clip.length * cd.nt);
            yield return null;
        }

        sb.AppendLine(string.Format("{0} {1:F2}s  {2,6:F3}  {3,6:F3}  {4,5:F1}°  {5,5:F1}°  {6,5:F1}°  {7,5:F3}m  {8,7:F3}m  {9,6:F1}°  {10,7:F3}m  {11,6:F2}m",
            cd.label, cd.clip.length, lateral, foreAft, LegSpread(hips, lf, rf), ol, orr,
            (hips != null) ? hips.position.y - gy : 0f, dFace, axisDeg, tipH, travel));
    }
    sb.AppendLine("判读：双脚横距 <0.25m、张开角 <35° 算自然站姿；剑→面部 >0.45m 才不糊脸；");
    sb.AppendLine("      「剑尖行程」越小说明待机越稳（越像静止）。");

    // ---------------- 出图 ① 待机候选 ----------------
    var camGo = new GameObject("TmpPose6Cam");
    var tcam = camGo.AddComponent<Camera>();
    tcam.CopyFrom(Camera.main);
    tcam.tag = "Untagged"; tcam.enabled = false;
    tcam.clearFlags = CameraClearFlags.SolidColor;
    tcam.backgroundColor = new Color(0.16f, 0.17f, 0.20f);

    var keep = new HashSet<Renderer>(go.GetComponentsInChildren<Renderer>());
    var hidden = new List<Renderer>();
    foreach (var r in Object.FindObjectsOfType<Renderer>())
        if (r.enabled && !keep.Contains(r)) { r.enabled = false; hidden.Add(r); }

    var cl = cands.Where(x => x.clip != null).ToList();
    int W = 300, H = 400;
    {
        var sheet = new Texture2D(W * 3, H * cl.Count, TextureFormat.RGB24, false);
        for (int r = 0; r < cl.Count; r++)
        {
            cl[r].clip.SampleAnimation(go, cl[r].clip.length * cl[r].nt);
            yield return null;
            Vector3 center = new Vector3(go.transform.position.x, go.transform.position.y + 1.05f, go.transform.position.z);
            for (int v = 0; v < 3; v++)
            {
                Vector3 cp; Quaternion cr;
                if (v == 0) { cp = center + (-go.transform.forward * 0.75f + go.transform.right * 0.55f + Vector3.up * 0.38f).normalized * 6f; cr = Quaternion.LookRotation(center - cp, Vector3.up); }
                else if (v == 1) { cp = center + go.transform.forward * 6f; cr = Quaternion.LookRotation(center - cp, Vector3.up); }
                else { cp = center + Vector3.up * 6f; cr = Quaternion.LookRotation(Vector3.down, go.transform.forward); }
                tcam.orthographic = true; tcam.orthographicSize = 1.35f;
                tcam.transform.position = cp; tcam.transform.rotation = cr;
                var rt = RenderTexture.GetTemporary(W, H, 24);
                tcam.targetTexture = rt; tcam.Render(); tcam.targetTexture = null;
                var prev = RenderTexture.active; RenderTexture.active = rt;
                var tile = new Texture2D(W, H, TextureFormat.RGB24, false);
                tile.ReadPixels(new Rect(0, 0, W, H), 0, 0); tile.Apply();
                RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt);
                sheet.SetPixels(v * W, (cl.Count - 1 - r) * H, W, H, tile.GetPixels());
                Object.Destroy(tile);
            }
            sb.AppendLine("图行 " + r + " = " + cl[r].label);
        }
        sheet.Apply();
        File.WriteAllBytes(Path.Combine(imgDir, "_idle_cands.png"), sheet.EncodeToPNG());
        Object.Destroy(sheet);
        sb.AppendLine("图列 = 斜后上(游戏机位) / 正视 / 俯视");
    }

    // ---------------- 出图 ② 跑步脚部特写（冻结 A/B）----------------
    sb.AppendLine();
    sb.AppendLine("=== ② 跑步脚朝向：原始片段 vs 烘焙片段（脚部特写，俯视+侧视）===");
    var runCands = new List<(string label, AnimationClip clip)>
    {
        ("KI Run01_Forward 原始", Sub(ki, "Run01_Forward")),
        ("现用 Run01_Carry     ", Proj("Baked/Run01_Carry")),
        ("UAL1 Jog_Fwd_Loop    ", Sub(ual1, "Rig|Jog_Fwd_Loop")),
    };
    {
        const int COLS = 8;
        int Wf = 300, Hf = 220;
        var clr = runCands.Where(x => x.clip != null).ToList();
        var sheet = new Texture2D(Wf * COLS, Hf * 2 * clr.Count, TextureFormat.RGB24, false);
        var yawSb = new StringBuilder();
        for (int r = 0; r < clr.Count; r++)
        {
            var clip = clr[r].clip;
            // 量 yaw 范围
            float mnL = 999, mxL = -999, mnR = 999, mxR = -999;
            for (int i = 0; i <= 36; i++)
            {
                clip.SampleAnimation(go, clip.length * i / 36f);
                float pl, pr;
                float yl = FootYaw(true, out pl), yr = FootYaw(false, out pr);
                mnL = Mathf.Min(mnL, yl); mxL = Mathf.Max(mxL, yl);
                mnR = Mathf.Min(mnR, yr); mxR = Mathf.Max(mxR, yr);
            }
            yawSb.AppendLine(string.Format("{0} 左yaw [{1,6:F1},{2,6:F1}]°  右yaw [{3,6:F1},{4,6:F1}]°",
                clr[r].label, mnL, mxL, mnR, mxR));

            for (int c = 0; c < COLS; c++)
            {
                clip.SampleAnimation(go, clip.length * c / (float)COLS);
                yield return null;
                float gy; GroundY(out gy);
                Vector3 center = new Vector3(go.transform.position.x, gy + 0.18f, go.transform.position.z);
                for (int v = 0; v < 2; v++)
                {
                    Vector3 cp; Quaternion cr;
                    if (v == 0) { cp = center + go.transform.right * 6f; cr = Quaternion.LookRotation(center - cp, Vector3.up); }
                    else { cp = center + Vector3.up * 6f; cr = Quaternion.LookRotation(Vector3.down, go.transform.forward); }
                    tcam.orthographic = true; tcam.orthographicSize = 0.60f;
                    tcam.transform.position = cp; tcam.transform.rotation = cr;
                    var rt = RenderTexture.GetTemporary(Wf, Hf, 24);
                    tcam.targetTexture = rt; tcam.Render(); tcam.targetTexture = null;
                    var prev = RenderTexture.active; RenderTexture.active = rt;
                    var tile = new Texture2D(Wf, Hf, TextureFormat.RGB24, false);
                    tile.ReadPixels(new Rect(0, 0, Wf, Hf), 0, 0); tile.Apply();
                    RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt);
                    int rowIdx = r * 2 + v;        // v=0 侧视, v=1 俯视
                    sheet.SetPixels(c * Wf, (2 * clr.Count - 1 - rowIdx) * Hf, Wf, Hf, tile.GetPixels());
                    Object.Destroy(tile);
                }
            }
            sb.AppendLine("跑步脚特写 行" + (r * 2) + "=侧视 / 行" + (r * 2 + 1) + "=俯视  ← " + clr[r].label);
        }
        sheet.Apply();
        File.WriteAllBytes(Path.Combine(imgDir, "_run_feet.png"), sheet.EncodeToPNG());
        Object.Destroy(sheet);
        sb.AppendLine(yawSb.ToString());
    }

    foreach (var r in hidden) if (r != null) r.enabled = true;
    Object.Destroy(camGo);
    Object.Destroy(bakeMesh);

    anim.enabled = animWas;
    if (ctl != null) { ctl.enabled = true; ctl.EndInputOverride(); }
    if (ik != null) ik.enabled = true;
    if (stance != null) { stance.enabled = true; stance.ForceStance(false); }
    if (rig != null) rig.SetMouseLookEnabled(true);

    File.WriteAllText(Path.Combine(projRoot, "Tools/reports/q_pose6.txt"), sb.ToString());
    Debug.Log("[q_pose6] done");
    yield return null;
}

float LegSpread(Transform hips, Transform l, Transform r)
{
    if (hips == null || l == null || r == null) return 0f;
    Vector3 a = l.position - hips.position; a.y = 0f;
    Vector3 b = r.position - hips.position; b.y = 0f;
    if (a.sqrMagnitude < 1e-8f || b.sqrMagnitude < 1e-8f) return 0f;
    return Vector3.Angle(a, b);
}

string Stat(string label, List<float> xs)
{
    if (xs.Count == 0) return label + " <无样本>";
    var s = xs.OrderBy(x => x).ToList();
    float p = s[(int)Mathf.Clamp(Mathf.Floor(s.Count * 0.05f), 0, s.Count - 1)];
    float p50 = s[s.Count / 2];
    return string.Format("{0}  min {1,7:F3}  p5 {2,7:F3}  中位 {3,7:F3}  max {4,7:F3}  极差 {5,6:F3}  n={6}",
        label, s[0], p, p50, s[s.Count - 1], s[s.Count - 1] - s[0], xs.Count);
}

float DistPointSeg(Vector3 p, Vector3 a, Vector3 b)
{
    Vector3 ab = b - a; float L2 = ab.sqrMagnitude;
    if (L2 < 1e-9f) return Vector3.Distance(p, a);
    float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / L2);
    return Vector3.Distance(p, a + ab * t);
}

return Body();
