// 移动片段候选对比：走路手臂是否外张 + 原生地面速度 + 步频。
//   判据分两类：
//     ① 「张臂」用**左臂外展角的 >55° 时间占比**（不是峰值）—— 峰值都在 40~60° 分辨不出来，
//        占比才说明「整段都摆着外张姿势」还是「只在摆臂瞬间抬一下」。
//     ② refSpeed 用「着地帧（脚竖直速度≈0）脚底水平后移速度的中位数」，方法同 s3_probe_refspeed_kaykit。
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

IEnumerator Body()
{
    var sb = new System.Text.StringBuilder();
    string projRoot = System.IO.Path.GetDirectoryName(Application.dataPath);
    string imgDir = System.IO.Path.Combine(projRoot, "Tools/screenshots/move_cand");
    System.IO.Directory.CreateDirectory(imgDir);

    yield return null;
    yield return null;

    var go = GameObject.Find("Player");
    if (go == null) { Debug.LogError("no Player"); yield break; }
    var anim = go.GetComponent<Animator>();
    var ctl = go.GetComponent<InkWash.Player.PlayerController>();
    var ik = go.GetComponent<InkWash.Player.FootIK>();
    if (ctl != null) ctl.enabled = false;
    if (ik != null) ik.enabled = false;
    var stance = go.GetComponentInChildren<InkWash.Player.CombatStance>(true);
    if (stance != null) stance.enabled = false;   // 别让它来改控制器
    // SampleAnimation 直接写骨骼；Animator 若同时启用会在下一帧覆盖回去
    bool animWas = anim.enabled;
    anim.enabled = false;

    var lU = anim.GetBoneTransform(HumanBodyBones.LeftUpperArm);
    var lL = anim.GetBoneTransform(HumanBodyBones.LeftLowerArm);
    var smrs = go.GetComponentsInChildren<SkinnedMeshRenderer>(true);
    var bake = new Mesh();
    bake.MarkDynamic();

    float groundY = go.transform.position.y;
    var hits = Physics.RaycastAll(go.transform.position + Vector3.up * 3f, Vector3.down, 10f);
    float best = float.MinValue;
    foreach (var h in hits)
    {
        if (h.collider != null && h.collider.transform.IsChildOf(go.transform)) continue;
        if (h.point.y > best) best = h.point.y;
    }
    if (best > float.MinValue) groundY = best;

    // ---- 收集候选 ----
    var walkPlans = new List<(string label, AnimationClip clip, string src)>();
    var runPlans = new List<(string label, AnimationClip clip, string src)>();

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

    walkPlans.Add(("W1_KayKit_Walking_A(现状)", Sub("Assets/ThirdParty/KayKit/Animations/Rig_Medium/Rig_Medium_MovementBasic.fbx", "Walking_A"), "KayKit"));
    walkPlans.Add(("W2_Feng_Walk", Anim("Assets/Char_Feng/Animation/Walk.anim"), "Feng"));
    walkPlans.Add(("W3_KI_Walk01_Forward", Sub("Assets/ThirdParty/KevinIglesias/Animations/Male/Movement/Walk/HumanM@Walk01_Forward.fbx", null), "KevinIglesias"));

    runPlans.Add(("R1_KayKit_Running_A(现状)", Sub("Assets/ThirdParty/KayKit/Animations/Rig_Medium/Rig_Medium_MovementBasic.fbx", "Running_A"), "KayKit"));
    runPlans.Add(("R2_KI_Run01_Forward", Sub("Assets/ThirdParty/KevinIglesias/Animations/Male/Movement/Run/HumanM@Run01_Forward.fbx", null), "KevinIglesias"));

    var plans = new List<(string label, AnimationClip clip)>();
    foreach (var p in walkPlans) plans.Add((p.label, p.clip));
    foreach (var p in runPlans) plans.Add((p.label, p.clip));

    // ---- 出图相机 ----
    var camGo = new GameObject("TmpMoveCandCam");
    var tcam = camGo.AddComponent<Camera>();
    tcam.CopyFrom(Camera.main);
    tcam.tag = "Untagged"; tcam.enabled = false;
    tcam.clearFlags = CameraClearFlags.SolidColor;
    tcam.backgroundColor = new Color(0.16f, 0.17f, 0.20f);

    var keep = new HashSet<Renderer>(go.GetComponentsInChildren<Renderer>());
    var hidden = new List<Renderer>();
    foreach (var r in Object.FindObjectsOfType<Renderer>())
        if (r.enabled && !keep.Contains(r)) { r.enabled = false; hidden.Add(r); }

    int TW = 260, TH = 340, COLS = 6;
    var sheet = new Texture2D(TW * COLS, TH * 2 * plans.Count, TextureFormat.RGB24, false);
    int rows = 0;

    sb.AppendLine("地面 Y = " + groundY.ToString("F3") + "   Avatar isHuman=" + anim.isHuman);
    sb.AppendLine();

    foreach (var (label, clip) in plans)
    {
        if (clip == null) { sb.AppendLine("!! 缺资产 " + label); continue; }

        int N = 240;
        float len = clip.length;
        var wx = new float[N + 1]; var wz = new float[N + 1]; var wy = new float[N + 1];
        var side = new int[N + 1]; var abduct = new float[N + 1];
        float minH = float.MaxValue, abMax = 0f; int abOver = 0;
        var hips = new Vector3[N + 1];

        for (int i = 0; i <= N; i++)
        {
            clip.SampleAnimation(go, len * i / N);
            float lo = float.MaxValue; Vector3 bp = Vector3.zero;
            for (int k = 0; k < smrs.Length; k++)
            {
                smrs[k].BakeMesh(bake);
                var verts = bake.vertices;
                var m = smrs[k].transform.localToWorldMatrix;
                for (int v = 0; v < verts.Length; v++)
                {
                    var wp = m.MultiplyPoint3x4(verts[v]);
                    if (wp.y < lo) { lo = wp.y; bp = wp; }
                }
            }
            var lp = go.transform.InverseTransformPoint(bp);
            wx[i] = bp.x; wz[i] = bp.z; wy[i] = lo - groundY;
            side[i] = lp.x < 0f ? -1 : 1;
            if (wy[i] < minH) minH = wy[i];

            Vector3 ld = (lL.position - lU.position).normalized;
            abduct[i] = Vector3.Angle(ld, -go.transform.up);
            if (abduct[i] > 55f) abOver++;
            if (abduct[i] > abMax) abMax = abduct[i];

            hips[i] = go.transform.InverseTransformPoint(anim.GetBoneTransform(HumanBodyBones.Hips).position);
        }

        // refSpeed：着地帧的脚底水平后移速度中位数
        float dt = len / N;
        var spd = new List<float>();
        for (int i = 0; i + 1 <= N; i++)
        {
            if (side[i] != side[i + 1]) continue;
            float vy = Mathf.Abs((wy[i + 1] - wy[i])) / dt;
            if (vy > 0.35f) continue;
            float dx = wx[i + 1] - wx[i], dz = wz[i + 1] - wz[i];
            spd.Add(Mathf.Sqrt(dx * dx + dz * dz) / dt);
        }
        spd.Sort();
        float med = spd.Count > 0 ? spd[spd.Count / 2] : 0f;
        float cadence = len > 0f ? 2f / len : 0f;
        float stepLen = cadence > 0f ? med / cadence : 0f;

        float rootTravel = new Vector2(hips[N].x - hips[0].x, hips[N].z - hips[0].z).magnitude;

        sb.AppendLine("════ " + label + "   (" + len.ToString("F3") + "s) ════");
        sb.AppendLine("  原生地面速度 " + med.ToString("F2") + " m/s   步频 " + cadence.ToString("F2")
            + " 步/秒   单步 " + stepLen.ToString("F2") + " m   髋部片段内位移 " + rootTravel.ToString("F3") + " m");
        sb.AppendLine("  左臂外展 峰值 " + abMax.ToString("F1") + "°   >55° 占比 " + (100f * abOver / (N + 1)).ToString("F0")
            + "%   原始最低点 " + minH.ToString("+0.000;-0.000") + " m");
        sb.Append("  外展曲线: ");
        for (int s = 0; s < 12; s++) sb.Append(abduct[s * N / 11].ToString("F0") + " ");
        sb.AppendLine();
        sb.AppendLine();

        // 连拍两行：上行正面、下行背面（用户截图视角）
        for (int vi = 0; vi < 2; vi++)
        {
            for (int c = 0; c < COLS; c++)
            {
                float nt = c / (float)(COLS - 1) * 0.999f;
                clip.SampleAnimation(go, len * nt);
                yield return null;

                Vector3 fwd = go.transform.forward; fwd.y = 0f; fwd.Normalize();
                Vector3 viewDir = vi == 0 ? fwd : -fwd;
                Vector3 center = new Vector3(go.transform.position.x, go.transform.position.y + 1.05f, go.transform.position.z);
                tcam.orthographic = false; tcam.fieldOfView = 36f;
                Vector3 cp = center + viewDir * 4.0f + Vector3.up * 0.15f;
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

                sheet.SetPixels(c * TW, (2 * plans.Count - 1 - (rows * 2 + vi)) * TH, TW, TH, tile.GetPixels());
                Object.Destroy(tile);
            }
        }
        rows++;
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

    System.IO.File.WriteAllText(System.IO.Path.Combine(projRoot, "Tools/reports/q_movecand.txt"), sb.ToString());
    Debug.Log("[q_movecand] done");
    yield return null;
}
return Body();
