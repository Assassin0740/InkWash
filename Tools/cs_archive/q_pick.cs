// q_pick.cs —— 定稿用的对比：持剑待机高清对比 + 跑步候选的参考速度/脚位波动
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
    string imgDir = Path.Combine(projRoot, "Tools/screenshots/pick");
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

    if (stance != null) stance.ForceStance(true);
    float tw = Time.time; while (Time.time - tw < 0.4f) yield return null;
    var weapon = vfx != null ? vfx.WeaponInstance : null;

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

    var idleCands = new List<(string label, AnimationClip clip)>
    {
        ("A_当前Feng_Idle", AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/_Project/Animations/Feng_Idle_Loop.anim")),
        ("B_UAL1_Sword_Idle", Sub(ual1, "Rig|Sword_Idle")),
    };

    // ---- 用网格世界 AABB 定剑尖 ----
    Vector3 BladeTip()
    {
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

    var head = anim.GetBoneTransform(HumanBodyBones.Head);
    sb.AppendLine("=== 持剑待机候选（高清对比）===");
    sb.AppendLine("候选              时长   剑轴·竖直   剑→面部最小   剑→头骨最小   剑尖相对头骨(dx,dy,dz)");
    foreach (var (label, clip) in idleCands)
    {
        if (clip == null) { sb.AppendLine(label + " ★ 找不到"); continue; }
        float minFace = 999f, minHead = 999f;
        Vector3 lastRel = Vector3.zero; float lastAng = 0f;
        const int NS = 40;
        for (int i = 0; i <= NS; i++)
        {
            clip.SampleAnimation(go, clip.length * i / (float)NS);
            Vector3 grip = weapon.transform.position, tip = BladeTip();
            Vector3 face = head.position + head.up * 0.07f + head.forward * 0.07f;
            minFace = Mathf.Min(minFace, DistPointSeg(face, grip, tip));
            minHead = Mathf.Min(minHead, DistPointSeg(head.position, grip, tip));
            lastRel = tip - head.position;
            lastAng = Vector3.Angle((tip - grip).normalized, Vector3.up);
        }
        sb.AppendLine(string.Format("{0} {1:F2}s  {2,6:F1}°   {3,8:F3}    {4,8:F3}    ({5,6:F2},{6,6:F2},{7,6:F2})",
            label, clip.length, lastAng, minFace, minHead, lastRel.x, lastRel.y, lastRel.z));
    }

    // ---- 出图：高清 3 视角 ----
    var camGo = new GameObject("TmpPickCam");
    var tcam = camGo.AddComponent<Camera>();
    tcam.CopyFrom(Camera.main);
    tcam.tag = "Untagged"; tcam.enabled = false;
    tcam.clearFlags = CameraClearFlags.SolidColor;
    tcam.backgroundColor = new Color(0.15f, 0.16f, 0.19f);

    var keep = new HashSet<Renderer>(go.GetComponentsInChildren<Renderer>());
    var hidden = new List<Renderer>();
    foreach (var r in Object.FindObjectsOfType<Renderer>())
        if (r.enabled && !keep.Contains(r)) { r.enabled = false; hidden.Add(r); }

    int W = 400, H = 520;
    Vector3 center = new Vector3(go.transform.position.x, go.transform.position.y + 1.20f, go.transform.position.z);
    Vector3[] vdir = {
        (-go.transform.forward * 0.82f + go.transform.right * 0.57f).normalized,  // 斜后45°（模拟游戏机位）
        go.transform.right,                                                       // 右侧视
        go.transform.forward,                                                     // 正脸
    };
    string[] vname = { "斜后45°", "右侧视", "正视" };

    int rows = idleCands.Count(x => x.clip != null);
    var sheet = new Texture2D(W * vdir.Length, H * rows, TextureFormat.RGB24, false);
    int row = 0;
    foreach (var (label, clip) in idleCands)
    {
        if (clip == null) continue;
        // 取最能代表姿态的一帧（1/3 处）
        clip.SampleAnimation(go, clip.length * 0.33f);
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
        row++;
    }
    sheet.Apply();
    File.WriteAllBytes(Path.Combine(imgDir, "_idle_hd.png"), sheet.EncodeToPNG());
    Object.Destroy(sheet);
    sb.AppendLine("出图 _idle_hd.png  行=候选(上=当前A, 下=候选B)  列=" + string.Join("/", vname));

    foreach (var r in hidden) if (r != null) r.enabled = true;
    Object.Destroy(camGo);

    // ================= 跑步候选：参考速度（脚踝法）+ 脚位波动 =================
    sb.AppendLine();
    sb.AppendLine("=== 跑步候选：参考速度（脚踝法）+ 脚位波动 ===");
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

    var runCands = new List<(string label, AnimationClip clip)>
    {
        ("当前 KI Run01_Forward", Sub("Assets/ThirdParty/KevinIglesias/Animations/Male/Movement/Run/HumanM@Run01_Forward.fbx", null)),
        ("UAL1 Rig|Jog_Fwd_Loop", Sub(ual1, "Rig|Jog_Fwd_Loop")),
        ("UAL1 Rig|Sprint_Loop ", Sub(ual1, "Rig|Sprint_Loop")),
    };

    sb.AppendLine("候选                    时长    脚位波动跨度   脚踝法参考速度   单步位移   反推步频(步/s)");
    foreach (var (label, clip) in runCands)
    {
        if (clip == null) { sb.AppendLine(label + " ★ 找不到"); continue; }
        const int NS = 120;
        float lo = 9999f, hi = -9999f;
        var samples = new List<(float t, float lfY, float rfY, float lfZ, float rfZ, float lfX, float rfX)>();
        for (int i = 0; i <= NS; i++)
        {
            float t = clip.length * i / (float)NS;
            clip.SampleAnimation(go, t);
            float v = lowest();
            if (v < lo) lo = v;
            if (v > hi) hi = v;
            Vector3 a = lfB != null ? go.transform.InverseTransformPoint(lfB.position) : Vector3.zero;
            Vector3 b = rfB != null ? go.transform.InverseTransformPoint(rfB.position) : Vector3.zero;
            samples.Add((t, lfB != null ? lfB.position.y - groundY : 0f, rfB != null ? rfB.position.y - groundY : 0f,
                a.z, b.z, a.x, b.x));
        }
        // 脚踝法：每只脚在「该脚最低点 +4cm」内视为着地，量它相对根的水平后移速度，取中位数
        var speeds = new List<float>();
        for (int foot = 0; foot < 2; foot++)
        {
            float minY = samples.Min(s => foot == 0 ? s.lfY : s.rfY);
            for (int i = 1; i < samples.Count; i++)
            {
                var s0 = samples[i - 1]; var s1 = samples[i];
                float y0 = foot == 0 ? s0.lfY : s0.rfY, y1 = foot == 0 ? s1.lfY : s1.rfY;
                if (y0 > minY + 0.04f || y1 > minY + 0.04f) continue;
                float dz = (foot == 0 ? s1.lfZ : s1.rfZ) - (foot == 0 ? s0.lfZ : s0.rfZ);
                float dx = (foot == 0 ? s1.lfX : s1.rfX) - (foot == 0 ? s0.lfX : s0.rfX);
                float dt = s1.t - s0.t;
                if (dt <= 0f) continue;
                float v = Mathf.Sqrt(dx * dx + dz * dz) / dt;
                if (v < 8f) speeds.Add(v);   // 剔掉跳变
            }
        }
        float med = speeds.Count > 0 ? Median(speeds) : 0f;
        int steps = 2;   // 一个循环两步
        float cadence = steps / clip.length;
        sb.AppendLine(string.Format("{0} {1:F3}s   {2,9:F3} m    {3,10:F3} m/s   {4,8:F3} m   {5,8:F2}",
            label, clip.length, hi - lo, med, med * (clip.length / steps), cadence));
    }
    sb.AppendLine("判读：参考速度 = 片段 1.0× 播放时角色应走多快（用于 MotionSpeed）。单步位移应对得上常识。");
    sb.AppendLine("      步频（片段原生 1.0×）= 2/时长，实际步频 = 该值 × MotionSpeed。");
    Object.Destroy(bake);

    anim.enabled = animWas;
    if (ctl != null) ctl.enabled = true;
    if (ik != null) ik.enabled = true;
    if (stance != null) stance.enabled = true;
    if (stance != null) stance.ForceStance(false);
    var rig = Camera.main != null ? Camera.main.GetComponent<InkWash.CameraRig.ThirdPersonCamera>() : null;
    if (rig != null) rig.SetMouseLookEnabled(true);

    File.WriteAllText(Path.Combine(projRoot, "Tools/reports/q_pick.txt"), sb.ToString());
    Debug.Log("[q_pick] done");
    yield return null;
}

static float Median(List<float> a)
{
    a.Sort();
    return a.Count % 2 == 1 ? a[a.Count / 2] : 0.5f * (a[a.Count / 2 - 1] + a[a.Count / 2]);
}
static float DistPointSeg(Vector3 p, Vector3 a, Vector3 b)
{
    Vector3 ab = b - a; float L2 = ab.sqrMagnitude;
    if (L2 < 1e-9f) return Vector3.Distance(p, a);
    float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / L2);
    return Vector3.Distance(p, a + ab * t);
}

return Body();
