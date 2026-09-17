// q_pose5.cs —— 本轮三问取证
//   ① 持剑待机「跨立」：量两腿张开角 / 足尖外八角 / 髋高，并出图
//   ② 跑步「脚转 90°」：量足尖方向与角色前方的水平夹角（带符号，正=朝角色右手侧）
//   ③ 跑步「高出来一格」：逐帧量「真实网格最低点 − 真实地面」，并打 FootIK 内部量
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
    string imgDir = Path.Combine(projRoot, "Tools/screenshots/pose5");
    Directory.CreateDirectory(imgDir);
    yield return null; yield return null;

    var go = GameObject.Find("Player");
    if (go == null) { Debug.LogError("no Player"); yield break; }
    var anim = go.GetComponent<Animator>();
    var ctl = go.GetComponent<InkWash.Player.PlayerController>();
    var ik = go.GetComponent<InkWash.Player.FootIK>();
    var stance = go.GetComponentInChildren<InkWash.Player.CombatStance>(true);
    var pose = go.GetComponentInChildren<InkWash.Effects.WeaponHandPose>(true);
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

    float FootYaw(HumanBodyBones foot, HumanBodyBones toes)
    {
        var f = Bone(foot); var t = Bone(toes);
        if (f == null || t == null) return 0f;
        Vector3 d = t.position - f.position; d.y = 0f;
        Vector3 fwd = go.transform.forward; fwd.y = 0f;
        if (d.sqrMagnitude < 1e-8f || fwd.sqrMagnitude < 1e-8f) return 0f;
        return Vector3.SignedAngle(fwd, d, Vector3.up);   // 正 = 转向角色右手侧
    }

    float LegSpreadDeg()
    {
        var hips = Bone(HumanBodyBones.Hips);
        var l = Bone(HumanBodyBones.LeftFoot); var r = Bone(HumanBodyBones.RightFoot);
        if (hips == null || l == null || r == null) return 0f;
        Vector3 a = l.position - hips.position; a.y = 0f;
        Vector3 b = r.position - hips.position; b.y = 0f;
        if (a.sqrMagnitude < 1e-8f || b.sqrMagnitude < 1e-8f) return 0f;
        return Vector3.Angle(a, b);
    }

    float FootGapM()
    {
        var l = Bone(HumanBodyBones.LeftFoot); var r = Bone(HumanBodyBones.RightFoot);
        if (l == null || r == null) return 0f;
        Vector3 d = l.position - r.position; d.y = 0f;
        return d.magnitude;
    }

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

    // ================= ③ + ② 跑步实测（活体，组件全开）=================
    sb.AppendLine("=== ③ 跑步浮空 / ② 跑步脚旋转（活体逐帧，一个完整循环）===");
    if (ctl != null) ctl.BeginInputOverride();
    if (stance != null) stance.ForceStance(true);
    Park();
    if (ctl != null) ctl.SetInjectedMove(new Vector2(0f, 1f), true);
    {
        float tw = Time.time; while (Time.time - tw < 1.6f) yield return null;   // 等速度稳定

        string clipName = "";
        float clipLen = 0f;
        var cbuf = new List<AnimatorClipInfo>(4);
        anim.GetCurrentAnimatorClipInfo(0, cbuf);
        if (cbuf.Count > 0 && cbuf[0].clip != null) { clipName = cbuf[0].clip.name; clipLen = cbuf[0].clip.length; }
        sb.AppendLine("实际片段 = " + clipName + "  时长 " + clipLen.ToString("F3") + "s   速度 " + (ctl != null ? ctl.CurrentSpeed : 0f).ToString("F2"));
        sb.AppendLine("帧   状态        速度   FloatIK最低  基准偏移  抬升  当前偏移键        浮空量  左足yaw  右足yaw  地面ok");
        float dur = Mathf.Max(clipLen, 0.4f);
        float t0 = Time.time; int n = 0;
        var floatList = new List<float>();
        var yawLList = new List<float>(); var yawRList = new List<float>();
        var liftList = new List<float>();
        while (Time.time - t0 < dur)
        {
            float gy; bool gok = GroundY(out gy);
            float meshLow = ik != null ? ik.LowestMeshY + ik.AppliedBodyOffset : 0f;
            float above = meshLow - gy;
            float yl = FootYaw(HumanBodyBones.LeftFoot, HumanBodyBones.LeftToes);
            float yr = FootYaw(HumanBodyBones.RightFoot, HumanBodyBones.RightToes);
            if (gok) { floatList.Add(above); yawLList.Add(yl); yawRList.Add(yr); }
            liftList.Add(ik != null ? ik.BodyLift : 0f);
            if (n % 3 == 0)
                sb.AppendLine(string.Format("{0,3}  {1,-10} {2,5:F2}  {3,10:F4}  {4,8:F4}  {5,5:F3}  {6,-16}  {7,6:F4}  {8,7:F1}  {9,7:F1}  {10}",
                    n, StateName(anim), ctl != null ? ctl.CurrentSpeed : 0f,
                    ik != null ? ik.LowestMeshY : 0f, ik != null ? ik.BodyBase : 0f, ik != null ? ik.BodyLift : 0f,
                    ik != null ? (ik.CurrentOffsetKey ?? "<null>") : "?", above, yl, yr, gok ? "Y" : "N"));
            n++;
            yield return null;
        }
        sb.AppendLine(Stat("浮空量(m)  网格最低-地面", floatList));
        sb.AppendLine(Stat("左足 yaw(°) ", yawLList));
        sb.AppendLine(Stat("右足 yaw(°) ", yawRList));
        sb.AppendLine(Stat("BodyLift(m) ", liftList));
        sb.AppendLine("判读：浮空量应 ≈ +0.005（= groundClearance）；脚 yaw 应落在 −25°~+25°（跑步时脚跟/足尖有少量内外翻），" );
        sb.AppendLine("      ±80° 以上 = 脚被拧成横的。");
    }
    if (ctl != null) ctl.SetInjectedMove(Vector2.zero, false);
    Park();
    { float t = Time.time; while (Time.time - t < 1.2f) yield return null; }

    // ================= ① 持剑待机「跨立」（活体）=================
    sb.AppendLine();
    sb.AppendLine("=== ① 持剑待机「跨立」（活体，完整循环）===");
    if (stance != null) stance.ForceStance(true);
    Park();
    {
        float t = Time.time; while (Time.time - t < 0.8f) yield return null;
        int m = 0; var spread = new List<float>(); var gap = new List<float>();
        var ol = new List<float>(); var orr = new List<float>();
        var hipH = new List<float>();
        while (m < 60)
        {
            spread.Add(LegSpreadDeg());
            gap.Add(FootGapM());
            ol.Add(-FootYaw(HumanBodyBones.LeftFoot, HumanBodyBones.LeftToes));
            orr.Add(FootYaw(HumanBodyBones.RightFoot, HumanBodyBones.RightToes));
            float gy; bool gok = GroundY(out gy);
            var hips = Bone(HumanBodyBones.Hips);
            if (hips != null && gok) hipH.Add(hips.position.y - gy);
            m++;
            yield return null;
        }
        sb.AppendLine("当前战斗待机片段 = " + (stance != null ? stance.CurrentIdleClipName : "?"));
        sb.AppendLine(Stat("两腿张开角(°) ", spread));
        sb.AppendLine(Stat("双脚水平间距(m)", gap));
        sb.AppendLine(Stat("左足外八角(°) ", ol));
        sb.AppendLine(Stat("右足外八角(°) ", orr));
        sb.AppendLine(Stat("髋高(m)      ", hipH));
    }

    // ---------------- 冻结，做片段对照 ----------------
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
    string ual1 = "Assets/ThirdParty/Quaternius/UniversalAnimationLibrary/Unity/AnimationLibrary_Unity_Standard.fbx";
    string ual2 = "Assets/ThirdParty/Quaternius/UniversalAnimationLibrary2/Unity/UAL2_Standard.fbx";
    string ki = "Assets/ThirdParty/KevinIglesias/Animations/Male/Movement/Run/HumanM@Run01_Forward.fbx";
    string kkMelee = "Assets/ThirdParty/KayKit/Animations/Rig_Medium/Rig_Medium_CombatMelee.fbx";

    var idleCands = new List<(string label, AnimationClip clip)>
    {
        ("现用 Sword_Idle_Loop", AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/_Project/Animations/Sword_Idle_Loop.anim")),
        ("旧   Feng_Idle_Loop ", AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/_Project/Animations/Feng_Idle_Loop.anim")),
        ("UAL1 Rig|Idle_Loop  ", Sub(ual1, "Rig|Idle_Loop")),
        ("UAL1 Rig|Sword_Idle ", Sub(ual1, "Rig|Sword_Idle")),
        ("UAL1 Rig|Walk_Loop  ", Sub(ual1, "Rig|Walk_Loop")),
        ("UAL2 Idle_No_Loop   ", Sub(ual2, "Armature|Idle_No_Loop")),
        ("KK   Melee_2H_Idle  ", Sub(kkMelee, "Melee_2H_Idle")),
    };

    sb.AppendLine();
    sb.AppendLine("=== ①-b 待机候选「站姿」对照（片段采样，冻结）===");
    sb.AppendLine("候选                  时长   两腿张开角  双脚间距   左外八  右外八  髋高");
    foreach (var (label, clip) in idleCands)
    {
        if (clip == null) { sb.AppendLine(label + "  ★ 找不到"); continue; }
        clip.SampleAnimation(go, clip.length * 0.4f);
        float gy; bool gok = GroundY(out gy);
        var hips = Bone(HumanBodyBones.Hips);
        sb.AppendLine(string.Format("{0} {1:F2}s  {2,8:F1}°  {3,7:F3}m  {4,6:F1}°  {5,6:F1}°  {6,6:F3}m",
            label, clip.length, LegSpreadDeg(), FootGapM(),
            -FootYaw(HumanBodyBones.LeftFoot, HumanBodyBones.LeftToes),
            FootYaw(HumanBodyBones.RightFoot, HumanBodyBones.RightToes),
            (hips != null && gok) ? hips.position.y - gy : 0f));
    }
    sb.AppendLine("判读：两腿张开角 <25° 才像「自然站立」；>40° 就是跨立。");

    sb.AppendLine();
    sb.AppendLine("=== ②-b 跑步片段「脚旋转」对照（片段采样，冻结）===");
    sb.AppendLine("片段                    时长  左yaw范围        右yaw范围        左|yaw|max 右|yaw|max");
    var runCands = new List<(string label, AnimationClip clip)>
    {
        ("KI Run01_Forward 原始", Sub(ki, "Run01_Forward")),
        ("现用 Run01_Carry     ", AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/_Project/Animations/Baked/Run01_Carry.anim")),
        ("UAL1 Jog_Fwd_Loop    ", Sub(ual1, "Rig|Jog_Fwd_Loop")),
    };
    foreach (var (label, clip) in runCands)
    {
        if (clip == null) { sb.AppendLine(label + "  ★ 找不到"); continue; }
        float loL = 999f, hiL = -999f, loR = 999f, hiR = -999f, mxL = 0f, mxR = 0f;
        const int NS = 36;
        for (int i = 0; i <= NS; i++)
        {
            clip.SampleAnimation(go, clip.length * i / (float)NS);
            float yl = FootYaw(HumanBodyBones.LeftFoot, HumanBodyBones.LeftToes);
            float yr = FootYaw(HumanBodyBones.RightFoot, HumanBodyBones.RightToes);
            loL = Mathf.Min(loL, yl); hiL = Mathf.Max(hiL, yl);
            loR = Mathf.Min(loR, yr); hiR = Mathf.Max(hiR, yr);
            mxL = Mathf.Max(mxL, Mathf.Abs(yl)); mxR = Mathf.Max(mxR, Mathf.Abs(yr));
        }
        sb.AppendLine(string.Format("{0} {1:F2}s  [{2,6:F1},{3,6:F1}]°  [{4,6:F1},{5,6:F1}]°  {6,8:F1}°  {7,8:F1}°",
            label, clip.length, loL, hiL, loR, hiR, mxL, mxR));
    }

    // ---------------- 出图 ----------------
    var camGo = new GameObject("TmpPose5Cam");
    var tcam = camGo.AddComponent<Camera>();
    tcam.CopyFrom(Camera.main);
    tcam.tag = "Untagged"; tcam.enabled = false;
    tcam.clearFlags = CameraClearFlags.SolidColor;
    tcam.backgroundColor = new Color(0.16f, 0.17f, 0.20f);

    var keep = new HashSet<Renderer>(go.GetComponentsInChildren<Renderer>());
    var hidden = new List<Renderer>();
    foreach (var r in Object.FindObjectsOfType<Renderer>())
        if (r.enabled && !keep.Contains(r)) { r.enabled = false; hidden.Add(r); }

    int W = 250, H = 340;
    var vdir = new Vector3[5];
    var vname = new string[5];

    // 图 ①：待机候选 —— 俯视 + 正视 + 背视
    {
        var cl = idleCands.Where(x => x.clip != null).ToList();
        var sheet = new Texture2D(W * 3, H * cl.Count, TextureFormat.RGB24, false);
        for (int r = 0; r < cl.Count; r++)
        {
            var clip = cl[r].clip;
            clip.SampleAnimation(go, clip.length * 0.4f);
            yield return null;
            Vector3 center = new Vector3(go.transform.position.x, go.transform.position.y + 0.85f, go.transform.position.z);
            for (int v = 0; v < 3; v++)
            {
                Vector3 cp; Quaternion cr;
                if (v == 0) { cp = center + Vector3.up * 6f; cr = Quaternion.LookRotation(Vector3.down, go.transform.forward); }
                else if (v == 1) { cp = center + go.transform.forward * 6f; cr = Quaternion.LookRotation(center - cp, Vector3.up); }
                else { cp = center - go.transform.forward * 6f; cr = Quaternion.LookRotation(center - cp, Vector3.up); }
                tcam.orthographic = true; tcam.orthographicSize = 1.15f;
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
            sb.AppendLine("待机图行 " + r + " = " + cl[r].label);
        }
        sheet.Apply();
        File.WriteAllBytes(Path.Combine(imgDir, "_idle_stance.png"), sheet.EncodeToPNG());
        Object.Destroy(sheet);
        sb.AppendLine("待机图列 = 俯视 / 正视 / 背视");
    }

    // 图 ②：跑步候选 —— 8 帧侧视 + 8 帧俯视
    {
        var cl = runCands.Where(x => x.clip != null).ToList();
        const int COLS = 8;
        var sheet = new Texture2D(W * COLS, H * 2 * cl.Count, TextureFormat.RGB24, false);
        for (int r = 0; r < cl.Count; r++)
        {
            var clip = cl[r].clip;
            for (int c = 0; c < COLS; c++)
            {
                clip.SampleAnimation(go, clip.length * c / (float)COLS);
                yield return null;
                Vector3 center = new Vector3(go.transform.position.x, go.transform.position.y + 0.85f, go.transform.position.z);
                for (int v = 0; v < 2; v++)   // 0 = 侧视, 1 = 俯视
                {
                    Vector3 cp; Quaternion cr;
                    if (v == 0) { cp = center + go.transform.right * 6f; cr = Quaternion.LookRotation(center - cp, Vector3.up); }
                    else { cp = center + Vector3.up * 6f; cr = Quaternion.LookRotation(Vector3.down, go.transform.forward); }
                    tcam.orthographic = true; tcam.orthographicSize = 1.15f;
                    tcam.transform.position = cp; tcam.transform.rotation = cr;
                    var rt = RenderTexture.GetTemporary(W, H, 24);
                    tcam.targetTexture = rt; tcam.Render(); tcam.targetTexture = null;
                    var prev = RenderTexture.active; RenderTexture.active = rt;
                    var tile = new Texture2D(W, H, TextureFormat.RGB24, false);
                    tile.ReadPixels(new Rect(0, 0, W, H), 0, 0); tile.Apply();
                    RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt);
                    int rowIdx = r * 2 + (1 - v);
                    sheet.SetPixels(c * W, (2 * cl.Count - 1 - rowIdx) * H, W, H, tile.GetPixels());
                    Object.Destroy(tile);
                }
            }
            sb.AppendLine("跑步图 " + (r * 2) + "=侧视/" + (r * 2 + 1) + "=俯视，" + cl[r].label);
        }
        sheet.Apply();
        File.WriteAllBytes(Path.Combine(imgDir, "_run_feet.png"), sheet.EncodeToPNG());
        Object.Destroy(sheet);
    }

    foreach (var r in hidden) if (r != null) r.enabled = true;
    Object.Destroy(camGo);

    anim.enabled = animWas;
    if (ctl != null) ctl.enabled = true;
    if (ik != null) ik.enabled = true;
    if (stance != null) stance.enabled = true;
    if (stance != null) stance.ForceStance(false);
    if (ctl != null) ctl.EndInputOverride();
    if (rig != null) rig.SetMouseLookEnabled(true);

    File.WriteAllText(Path.Combine(projRoot, "Tools/reports/q_pose5.txt"), sb.ToString());
    Debug.Log("[q_pose5] done");
    yield return null;
}

string StateName(Animator an)
{
    var si = an.GetCurrentAnimatorStateInfo(0);
    if (si.IsName("Run")) return "Run";
    if (si.IsName("Walk")) return "Walk";
    if (si.IsName("Idle")) return "Idle";
    if (si.IsName("Dash")) return "Dash";
    if (si.IsName("Atk1")) return "Atk1";
    if (si.IsName("Atk1Rec")) return "Atk1Rec";
    if (si.IsName("Atk2")) return "Atk2";
    if (si.IsName("Atk2Rec")) return "Atk2Rec";
    if (si.IsName("Atk3")) return "Atk3";
    return "?";
}

string Stat(string label, List<float> xs)
{
    if (xs.Count == 0) return label + " <无样本>";
    float mn = xs.Min(), mx = xs.Max(), av = xs.Average();
    return string.Format("{0}  min {1,7:F3}  max {2,7:F3}  均值 {3,7:F3}  极差 {4,6:F3}  n={5}",
        label, mn, mx, av, mx - mn, xs.Count);
}

return Body();
