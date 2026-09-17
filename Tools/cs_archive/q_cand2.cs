// q_cand2.cs —— 持剑待机候选 + 跑步候选：拿剑渲染 + 量「剑到头/脸」的距离
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
    string imgDir = Path.Combine(projRoot, "Tools/screenshots/cand2");
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

    // 先把武器挂到手上
    if (stance != null) stance.ForceStance(true);
    float tw = Time.time; while (Time.time - tw < 0.4f) yield return null;
    var weapon = vfx != null ? vfx.WeaponInstance : null;
    sb.AppendLine("武器实例 = " + (weapon != null ? weapon.name : "<null>") + "  挂点 = " + (stance != null ? stance.WeaponMountPath : "?"));

    var head = anim.GetBoneTransform(HumanBodyBones.Head);

    // 冻结：采样直接写骨骼，别的组件别再改
    if (ctl != null) ctl.enabled = false;
    if (ik != null) ik.enabled = false;
    if (stance != null) stance.enabled = false;
    if (pose != null) pose.enabled = false;
    bool animWas = anim.enabled;
    anim.enabled = false;

    AnimationClip Sub(string p, string n)
    {
        foreach (var o in AssetDatabase.LoadAllAssetsAtPath(p))
        {
            var c = o as AnimationClip;
            if (c != null && !c.name.StartsWith("__preview__") && (n == null || c.name == n)) return c;
        }
        return null;
    }
    string ual1 = "Assets/ThirdParty/Quaternius/UniversalAnimationLibrary/Unity/AnimationLibrary_Unity_Standard.fbx";
    string ual2 = "Assets/ThirdParty/Quaternius/UniversalAnimationLibrary2/Unity/UAL2_Standard.fbx";
    string ki = "Assets/ThirdParty/KevinIglesias/Animations/Male/Movement/Run/HumanM@Run01_Forward.fbx";
    string kk = "Assets/ThirdParty/KayKit/Animations/Rig_Medium/Rig_Medium_MovementBasic.fbx";
    string kkMelee = "Assets/ThirdParty/KayKit/Animations/Rig_Medium/Rig_Medium_CombatMelee.fbx";

    var idleCands = new List<(string label, AnimationClip clip)>
    {
        ("当前: Feng_Idle ", AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/_Project/Animations/Feng_Idle_Loop.anim")),
        ("UAL1 Sword_Idle", Sub(ual1, "Rig|Sword_Idle")),
        ("UAL1 Idle_Loop  ", Sub(ual1, "Rig|Idle_Loop")),
        ("UAL2 Idle_No_Lp ", Sub(ual2, "Armature|Idle_No_Loop")),
        ("KK   Melee_2H_Id", Sub(kkMelee, "Melee_2H_Idle")),
        ("KI   Idle01     ", Sub("Assets/ThirdParty/KevinIglesias/Animations/Male/Idle/HumanM@Idle01.fbx", "Idle01")),
    };

    // ---- 剑身线段（用武器渲染体的世界 AABB 定主轴）----
    Vector3 BladeTip()
    {
        var rs = weapon.GetComponentsInChildren<Renderer>()
            .Where(r => r is MeshRenderer || r is SkinnedMeshRenderer).ToArray();
        if (rs.Length == 0) return weapon.transform.position;
        Bounds b = rs[0].bounds;
        for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
        // 主轴 = AABB 最长边
        Vector3 sz = b.size;
        Vector3 ax = sz.x >= sz.y && sz.x >= sz.z ? Vector3.right : (sz.y >= sz.z ? Vector3.up : Vector3.forward);
        float half = Mathf.Max(sz.x, Mathf.Max(sz.y, sz.z)) * 0.5f;
        Vector3 p1 = b.center + ax * half, p2 = b.center - ax * half;
        // 离握柄远的那端是剑尖
        Vector3 grip = weapon.transform.position;
        return Vector3.Distance(p1, grip) >= Vector3.Distance(p2, grip) ? p1 : p2;
    }

    sb.AppendLine();
    sb.AppendLine("=== 持剑待机候选：剑身线段 → 头骨 / 面部点 最近距离 ===");
    sb.AppendLine("候选               时长   剑轴·竖直   剑尖相对头骨(dx,dy,dz)      剑→头骨    剑→面部   剑尖→头骨");
    foreach (var (label, clip) in idleCands)
    {
        if (clip == null) { sb.AppendLine(label + "  ★ 找不到"); continue; }
        clip.SampleAnimation(go, clip.length * 0.4f);
        Vector3 grip = weapon.transform.position;
        Vector3 tip = BladeTip();
        Vector3 axis = (tip - grip).normalized;
        Vector3 face = head.position + head.up * 0.07f + head.forward * 0.07f;
        Vector3 rel = tip - head.position;
        float dHead = DistPointSeg(head.position, grip, tip);
        float dFace = DistPointSeg(face, grip, tip);
        sb.AppendLine(string.Format("{0} {1:F2}s  {2,6:F1}°   ({3,6:F2},{4,6:F2},{5,6:F2})   {6,8:F3}  {7,8:F3}  {8,8:F3}",
            label, clip.length, Vector3.Angle(axis, Vector3.up), rel.x, rel.y, rel.z, dHead, dFace, Vector3.Distance(tip, head.position)));
    }
    sb.AppendLine("判读：剑→面部 越接近 0.30 m 越容易在三人称机位上「糊住脑袋」。>0.45 m 才算干净。");

    // ---- 出图 ①：持剑待机候选 × 4 视角 ----
    var camGo = new GameObject("TmpCandCam");
    var tcam = camGo.AddComponent<Camera>();
    tcam.CopyFrom(Camera.main);
    tcam.tag = "Untagged"; tcam.enabled = false;
    tcam.clearFlags = CameraClearFlags.SolidColor;
    tcam.backgroundColor = new Color(0.16f, 0.17f, 0.20f);

    var keep = new HashSet<Renderer>(go.GetComponentsInChildren<Renderer>());
    var hidden = new List<Renderer>();
    foreach (var r in Object.FindObjectsOfType<Renderer>())
        if (r.enabled && !keep.Contains(r)) { r.enabled = false; hidden.Add(r); }

    int W = 240, H = 330;
    Vector3 center = new Vector3(go.transform.position.x, go.transform.position.y + 1.20f, go.transform.position.z);
    // 视角：背 / 斜后45°(模拟游戏机位) / 右侧 / 正
    Vector3[] vdir = {
        -go.transform.forward,
        (-go.transform.forward * 0.82f + go.transform.right * 0.57f).normalized,
        go.transform.right,
        go.transform.forward,
    };
    string[] vname = { "背视", "斜后45°", "右侧视", "正视" };

    int rows = idleCands.Count(x => x.clip != null);
    var sheet = new Texture2D(W * vdir.Length, H * rows, TextureFormat.RGB24, false);
    int row = 0;
    foreach (var (label, clip) in idleCands)
    {
        if (clip == null) continue;
        clip.SampleAnimation(go, clip.length * 0.4f);
        yield return null;
        for (int v = 0; v < vdir.Length; v++)
        {
            Vector3 cp = center + vdir[v] * 6f;
            tcam.orthographic = true; tcam.orthographicSize = 1.30f;
            tcam.transform.position = cp;
            tcam.transform.rotation = Quaternion.LookRotation(center - cp, Vector3.up);
            var rt = RenderTexture.GetTemporary(W, H, 24);
            tcam.targetTexture = rt; tcam.Render(); tcam.targetTexture = null;
            var prev = RenderTexture.active; RenderTexture.active = rt;
            var tile = new Texture2D(W, H, TextureFormat.RGB24, false);
            tile.ReadPixels(new Rect(0, 0, W, H), 0, 0); tile.Apply();
            RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt);
            sheet.SetPixels(v * W, (rows - 1 - row) * H, W, H, tile.GetPixels());
            Object.Destroy(tile);
        }
        sb.AppendLine("出图行 " + row + " = " + label);
        row++;
    }
    sheet.Apply();
    File.WriteAllBytes(Path.Combine(imgDir, "_idle_cands.png"), sheet.EncodeToPNG());
    Object.Destroy(sheet);
    sb.AppendLine("列顺序 = " + string.Join(" / ", vname));

    // ---- 出图 ②：跑步候选 × 8 帧侧视 ----
    var runCands = new List<(string label, AnimationClip clip)>
    {
        ("当前 KI Run01_Forward", Sub(ki, null)),
        ("UAL1 Rig|Jog_Fwd_Loop", Sub(ual1, "Rig|Jog_Fwd_Loop")),
        ("UAL1 Rig|Sprint_Loop ", Sub(ual1, "Rig|Sprint_Loop")),
        ("KK   Running_A       ", Sub(kk, "Running_A")),
    };
    var runRows = runCands.Count(x => x.clip != null);
    int COLS = 8;
    var sheet2 = new Texture2D(W * COLS, H * runRows, TextureFormat.RGB24, false);
    row = 0;
    var travelSb = new StringBuilder();
    var bones = new (string n, HumanBodyBones b)[]
    {
        ("hips", HumanBodyBones.Hips), ("chest", HumanBodyBones.Chest), ("head", HumanBodyBones.Head),
        ("L.Shoulder", HumanBodyBones.LeftUpperArm), ("R.Shoulder", HumanBodyBones.RightUpperArm),
        ("L.Elbow", HumanBodyBones.LeftLowerArm), ("R.Elbow", HumanBodyBones.RightLowerArm),
        ("L.Thigh", HumanBodyBones.LeftUpperLeg), ("R.Thigh", HumanBodyBones.RightUpperLeg),
        ("L.Knee", HumanBodyBones.LeftLowerLeg), ("R.Knee", HumanBodyBones.RightLowerLeg),
    };
    travelSb.AppendLine("片段                    时长   " + string.Join(" ", bones.Select(b => Pad(b.n, 10))));
    foreach (var (label, clip) in runCands)
    {
        if (clip == null) { travelSb.AppendLine(label + " ★ 找不到"); continue; }
        var travel = new float[bones.Length];
        var prevQ = new Quaternion[bones.Length];
        const int NS = 60;
        for (int i = 0; i <= NS; i++)
        {
            clip.SampleAnimation(go, clip.length * i / (float)NS);
            for (int b = 0; b < bones.Length; b++)
            {
                var tr = anim.GetBoneTransform(bones[b].b);
                if (tr == null) continue;
                if (i > 0) travel[b] += Quaternion.Angle(prevQ[b], tr.rotation);
                prevQ[b] = tr.rotation;
            }
        }
        travelSb.AppendLine(Pad(label, 22) + " " + clip.length.ToString("F3") + "  " + string.Join(" ", travel.Select(x => Pad(x.ToString("F0") + "°", 10))));

        float groundY = go.transform.position.y;
        for (int c = 0; c < COLS; c++)
        {
            clip.SampleAnimation(go, clip.length * c / (float)COLS);
            yield return null;
            Vector3 cc = new Vector3(go.transform.position.x, groundY + 0.95f, go.transform.position.z);
            Vector3 cp = cc + go.transform.right * 6f;
            tcam.orthographic = true; tcam.orthographicSize = 1.25f;
            tcam.transform.position = cp;
            tcam.transform.rotation = Quaternion.LookRotation(cc - cp, Vector3.up);
            var rt = RenderTexture.GetTemporary(W, H, 24);
            tcam.targetTexture = rt; tcam.Render(); tcam.targetTexture = null;
            var prev = RenderTexture.active; RenderTexture.active = rt;
            var tile = new Texture2D(W, H, TextureFormat.RGB24, false);
            tile.ReadPixels(new Rect(0, 0, W, H), 0, 0); tile.Apply();
            RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt);
            sheet2.SetPixels(c * W, (runRows - 1 - row) * H, W, H, tile.GetPixels());
            Object.Destroy(tile);
        }
        row++;
    }
    sheet2.Apply();
    File.WriteAllBytes(Path.Combine(imgDir, "_run_cands.png"), sheet2.EncodeToPNG());
    Object.Destroy(sheet2);
    sb.AppendLine();
    sb.AppendLine("=== 跑步候选「各骨角度行程」（越大越活，太小=僵）===");
    sb.AppendLine(travelSb.ToString());

    foreach (var r in hidden) if (r != null) r.enabled = true;
    Object.Destroy(camGo);

    anim.enabled = animWas;
    if (ctl != null) ctl.enabled = true;
    if (ik != null) ik.enabled = true;
    if (stance != null) stance.enabled = true;
    if (stance != null) stance.ForceStance(false);
    var rig = Camera.main != null ? Camera.main.GetComponent<InkWash.CameraRig.ThirdPersonCamera>() : null;
    if (rig != null) rig.SetMouseLookEnabled(true);

    File.WriteAllText(Path.Combine(projRoot, "Tools/reports/q_cand2.txt"), sb.ToString());
    Debug.Log("[q_cand2] done");
    yield return null;
}

static string Pad(string s, int n) { return s.Length >= n ? s.Substring(0, n) : s + new string(' ', n - s.Length); }
static float DistPointSeg(Vector3 p, Vector3 a, Vector3 b)
{
    Vector3 ab = b - a; float L2 = ab.sqrMagnitude;
    if (L2 < 1e-9f) return Vector3.Distance(p, a);
    float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / L2);
    return Vector3.Distance(p, a + ab * t);
}

return Body();
