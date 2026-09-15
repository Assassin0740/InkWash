// 宝剑挂点数值扫描 —— 用"穿模量"选挂点，而不是靠肉眼。
//
// 背景：这把剑的柄占总长 37.5%（真剑约 22%），从拳头到剑首的距离可观。
// 挂点太靠护手 → 剑首捅进前臂；挂点太靠剑首 → 拳头握在柄尾、护手离手空一截。
// 这个取舍必须量化，否则只能反复猜。
//
// 判据：
//   1. 把剑装到右手骨，在**缠绳握柄**范围内逐格移动挂点
//   2. 逐格算剑网格顶点与「前臂/上臂/躯干+袍/大腿/小腿」胶囊体的**穿插深度**
//   3. 同时报告"拳头→护手"的空隙与"剑尖达多远"（观感指标）
// 胶囊半径从角色自身网格**实测**（轴向投影在段内、距离的 10 分位 ×1.15），
// 不用解剖学估值 —— 角色是 2.17 m 的非常规身高，估值会系统性偏小。
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// 拳头中心相对手骨原点沿 +Y 的世界偏移。手骨原点=腕，拳头在腕前方。
// 世界 → 模型容器空间要除以 Visual.localScale = 2.213。
const float FIST_OFFSET = 0.045f / 2.213f;

IEnumerator Body()
{
    var sb = new System.Text.StringBuilder();
    string projRoot = System.IO.Path.GetDirectoryName(Application.dataPath);
    System.Action flush = () => System.IO.File.WriteAllText(
        System.IO.Path.Combine(projRoot, "Tools/reports/w_fit.txt"), sb.ToString());

    var ctl = Object.FindObjectOfType<InkWash.Player.PlayerController>();
    if (ctl == null) { sb.AppendLine("[ERR] 找不到 PlayerController（本脚本要在 Play 模式跑）"); flush(); yield break; }
    var root = ctl.transform;
    var anim = root.GetComponent<Animator>();
    if (anim == null) { sb.AppendLine("[ERR] 找不到 Animator"); flush(); yield break; }

    var ik = root.GetComponent<InkWash.Player.FootIK>();
    var vfx = root.GetComponentInChildren<InkWash.Effects.SwordVfx>(true);
    ctl.enabled = false;
    if (ik != null) ik.enabled = false;
    if (vfx != null) vfx.enabled = false;

    var hand = anim.GetBoneTransform(HumanBodyBones.RightHand);
    if (hand == null) { sb.AppendLine("[ERR] 拿不到右手骨"); flush(); yield break; }

    var fbx = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/W_Sword/W_Sword.FBX");
    if (fbx == null) { sb.AppendLine("[ERR] 加载不到 W_Sword.FBX"); flush(); yield break; }
    var sword = (GameObject)Object.Instantiate(fbx);
    sword.transform.SetParent(hand, false);
    var swMF = sword.GetComponentInChildren<MeshFilter>();
    if (swMF == null) { sb.AppendLine("[ERR] 剑没有 MeshFilter"); flush(); yield break; }
    var swLocal = swMF.sharedMesh.vertices;
    var swRend = sword.GetComponentInChildren<MeshRenderer>();
    if (swRend != null) swRend.enabled = false;

    // 模型实测的轴向边界（见 w_probe2.txt）
    const float TIP_Y = -0.5988f;
    const float GUARD_FAR_Y = 0.1497f;   // 剑身/剑格 分界
    const float GUARD_NEAR_Y = 0.3000f;  // 剑格/握柄 分界
    const float GRIP_END_Y = 0.5100f;    // 握柄/剑首 分界
    const float POMMEL_Y = 0.5988f;      // 剑首端

    sb.AppendLine("========== 宝剑挂点扫描 ==========");
    sb.AppendLine(string.Format("模型本地 Y：剑尖 {0:F4} / 剑格刃侧 {1:F4} / 剑格柄侧 {2:F4} / 柄末 {3:F4} / 剑首端 {4:F4}",
        TIP_Y, GUARD_FAR_Y, GUARD_NEAR_Y, GRIP_END_Y, POMMEL_Y));
    sb.AppendLine("剑网格顶点 " + swLocal.Length);
    sb.AppendLine("Visual.localScale = " + root.Find("Visual").localScale.x.ToString("F4"));
    sb.AppendLine();

    string[] poses = { "Idle", "Atk1", "Atk2" };
    float[] poseNt = { 0.00f, 0.35f, 0.45f };

    const int STEPS = 13;
    float[] scales = { 0.49f, 0.44f };
    var table = new List<string>();

    for (int si = 0; si < scales.Length; si++)
    {
        float s = scales[si];
        sb.AppendLine(string.Format("########## 缩放 s = {0:F4} → 世界总长 {1:F3} m / 剑身 {2:F3} m / 剑格展 {3:F3} m / 拳→剑尖 {4:F3} m ##########",
            s, 1.1976f * s * 2.213f, 0.7485f * s * 2.213f, 0.2324f * s * 2.213f,
            (FIST_OFFSET + s * (0.44f - TIP_Y)) * 2.213f));

        for (int k = 0; k < STEPS; k++)
        {
            float frac = k / (float)(STEPS - 1);
            float G = Mathf.Lerp(GUARD_NEAR_Y, GRIP_END_Y, frac);

            float worstPen = -999f;
            string penWhere = "";

            for (int p = 0; p < poses.Length; p++)
            {
                anim.Play(poses[p], 0, poseNt[p]);
                anim.Update(1f / 60f);

                var charVerts = CollectCharVerts(root);
                var caps = BuildCapsules(anim, charVerts, sb, si == 0 && k == 0 && p == 0, hand);

                var t = sword.transform;
                t.localPosition = new Vector3(0f, FIST_OFFSET + s * G, 0f);
                t.localEulerAngles = new Vector3(180f, 0f, 0f);
                t.localScale = new Vector3(s, s, s);

                var m = t.localToWorldMatrix;
                for (int ci = 0; ci < caps.Count; ci++)
                {
                    var c = caps[ci];
                    int inside = 0; float maxPen = -999f;
                    for (int i = 0; i < swLocal.Length; i++)
                    {
                        Vector3 w = m.MultiplyPoint3x4(swLocal[i]);
                        float pen = Penetration(c, w);
                        if (pen > 0f) { inside++; if (pen > maxPen) maxPen = pen; }
                    }
                    if (maxPen > worstPen) { worstPen = maxPen; penWhere = poses[p] + "/" + c.name + "(" + inside + "点)"; }
                }
            }

            float fistToGuard = (GUARD_NEAR_Y - G) * s * 2.213f;
            float pommelBehindWrist = (FIST_OFFSET + s * G - s * POMMEL_Y) * 2.213f * -1f;
            float tipReach = (FIST_OFFSET + s * (G - TIP_Y)) * 2.213f;

            string verdict = worstPen <= 0.001f ? "无穿插 OK" : (worstPen < 0.02f ? "轻微" : "穿模 XX");
            table.Add(string.Format("s={0:F2} 挂点比例{1,4:F2} 模型Y {2:F3} | 拳→格 {3,5:F3}m | 剑首在腕后 {4,5:F3}m | 剑尖达 {5,5:F3}m | 最大穿插 {6,6:F3}m {7,-9} {8}",
                s, frac, G, fistToGuard, pommelBehindWrist, tipReach, worstPen, verdict,
                worstPen > 0.001f ? penWhere : ""));
        }
        sb.AppendLine();
    }

    sb.AppendLine("========== 扫描结果 ==========");
    sb.AppendLine("判据偏好：① 最大穿插 = 0 ② 在满足①的前提下「拳→格」尽量小（拳头贴近护手才像握剑）");
    sb.AppendLine();
    foreach (var line in table) sb.AppendLine(line);
    sb.AppendLine();

    // ---------- 出图 ----------
    sb.AppendLine("========== 候选位出图 ==========");
    string imgDir = System.IO.Path.Combine(projRoot, "Tools/screenshots/sword_fit");
    System.IO.Directory.CreateDirectory(imgDir);
    if (swRend != null) swRend.enabled = true;
    if (vfx != null) vfx.enabled = false;

    var camGo = new GameObject("TmpFitCam");
    var cam = camGo.AddComponent<Camera>();
    var mainCam = Camera.main;
    if (mainCam != null) cam.CopyFrom(mainCam);
    cam.tag = "Untagged"; cam.enabled = false;
    cam.clearFlags = CameraClearFlags.SolidColor;
    cam.backgroundColor = new Color(0.16f, 0.17f, 0.20f);

    var keep = new HashSet<Renderer>(root.GetComponentsInChildren<Renderer>());
    var hidden = new List<Renderer>();
    foreach (var r in Object.FindObjectsOfType<Renderer>())
        if (r.enabled && !keep.Contains(r)) { r.enabled = false; hidden.Add(r); }

    anim.Play("Idle", 0, 0f);
    anim.Update(1f / 60f);
    yield return null;

    Vector3 side = hand.position - root.position; side.y = 0f;
    if (side.sqrMagnitude < 1e-6f) side = root.right; else side.Normalize();
    Vector3 bodyCtr = root.position + Vector3.up * 1.10f;

    float[] fracs = { 0.00f, 0.33f, 0.66f, 1.00f };
    float sUse = scales[0];
    foreach (var frac in fracs)
    {
        float G = Mathf.Lerp(GUARD_NEAR_Y, GRIP_END_Y, frac);
        var t = sword.transform;
        t.localPosition = new Vector3(0f, FIST_OFFSET + sUse * G, 0f);
        t.localEulerAngles = new Vector3(180f, 0f, 0f);
        t.localScale = new Vector3(sUse, sUse, sUse);
        yield return null;

        string tag = "fit_" + frac.ToString("F2").Replace('.', 'p');
        Shot(cam, imgDir, tag + "_body", 460, 880, bodyCtr, bodyCtr + side * 4.2f, 1.32f);
        Shot(cam, imgDir, tag + "_hand", 620, 560, hand.position, hand.position + side * 0.62f, -1f);
        sb.AppendLine(string.Format("  {0}：挂点比例 {1:F2}（模型 Y {2:F3}） 拳→格 {3:F3}m 剑首在腕后 {4:F3}m",
            tag, frac, G, (GUARD_NEAR_Y - G) * sUse * 2.213f, (FIST_OFFSET + sUse * G - sUse * POMMEL_Y) * 2.213f * -1f));
    }

    foreach (var r in hidden) if (r != null) r.enabled = true;
    Object.Destroy(camGo);
    Object.Destroy(sword);
    ctl.enabled = true; if (ik != null) ik.enabled = true;
    flush();
    Debug.Log(sb.ToString());
}

List<Vector3> CollectCharVerts(Transform root)
{
    var list = new List<Vector3>();
    var bake = new Mesh();
    foreach (var s in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
    {
        if (s.sharedMesh == null) continue;
        s.BakeMesh(bake, true);
        var l2w = s.transform.localToWorldMatrix;
        var vs = bake.vertices;
        for (int i = 0; i < vs.Length; i++) list.Add(l2w.MultiplyPoint3x4(vs[i]));
    }
    Object.Destroy(bake);
    return list;
}

float EstimateRadius(Vector3 a, Vector3 b, List<Vector3> verts, float cap)
{
    Vector3 ab = b - a; float L2 = ab.sqrMagnitude;
    if (L2 < 1e-9f) return 0.05f;
    var ds = new List<float>();
    for (int i = 0; i < verts.Count; i++)
    {
        float t = Vector3.Dot(verts[i] - a, ab) / L2;
        if (t < 0.2f || t > 0.8f) continue;
        float d = Vector3.Distance(verts[i], a + ab * t);
        if (d < cap) ds.Add(d);
    }
    if (ds.Count < 30) return 0.05f;
    ds.Sort();
    int idx = Mathf.Clamp(Mathf.FloorToInt(ds.Count * 0.10f), 0, ds.Count - 1);
    return ds[idx] * 1.15f;
}

List<Caps> BuildCapsules(Animator anim, List<Vector3> charVerts, System.Text.StringBuilder sb, bool verbose, Transform hand)
{
    var list = new List<Caps>();

    AddCap(list, anim, charVerts, sb, verbose, "前臂R", HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand, 0.30f, 0f);
    AddCap(list, anim, charVerts, sb, verbose, "上臂R", HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, 0.30f, 0f);
    AddCap(list, anim, charVerts, sb, verbose, "前臂L", HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand, 0.30f, 0f);
    AddCap(list, anim, charVerts, sb, verbose, "上臂L", HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, 0.30f, 0f);
    AddCap(list, anim, charVerts, sb, verbose, "大腿R", HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, 0.45f, 0f);
    AddCap(list, anim, charVerts, sb, verbose, "小腿R", HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, 0.40f, 0f);
    AddCap(list, anim, charVerts, sb, verbose, "大腿L", HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, 0.45f, 0f);
    AddCap(list, anim, charVerts, sb, verbose, "小腿L", HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, 0.40f, 0f);
    // 躯干要把长袍裙摆算进去：实测半径只反映躯干本体，加 6 cm 兜住袍摆
    AddCap(list, anim, charVerts, sb, verbose, "躯干+袍", HumanBodyBones.Hips, HumanBodyBones.Chest, 0.60f, 0.06f);

    if (verbose)
    {
        // 拳头沿手骨 +Y 的实际范围 —— 用来校验 FIST_OFFSET 的取值
        Vector3 up = hand.up;
        var proj = new List<float>();
        for (int i = 0; i < charVerts.Count; i++)
        {
            if (Vector3.Distance(charVerts[i], hand.position) > 0.22f) continue;
            proj.Add(Vector3.Dot(charVerts[i] - hand.position, up));
        }
        if (proj.Count > 0)
        {
            proj.Sort();
            sb.AppendLine(string.Format("  手骨 +Y 世界方向 = {0:F3}", up.ToString("F3")));
            sb.AppendLine(string.Format("  手骨附近顶点 {0} 个，沿 +Y 投影范围 {1:F4} .. {2:F4}（中位 {3:F4}）",
                proj.Count, proj[0], proj[proj.Count - 1], proj[proj.Count / 2]));
            sb.AppendLine(string.Format("  → 拳头中心沿 +Y ≈ {0:F4} m；本脚本 FIST_OFFSET 用 {1:F4} m（世界）",
                proj[proj.Count / 2], FIST_OFFSET * 2.213f));
        }
        sb.AppendLine();
    }
    return list;
}

void AddCap(List<Caps> list, Animator anim, List<Vector3> verts, System.Text.StringBuilder sb, bool verbose,
            string name, HumanBodyBones ba, HumanBodyBones bb, float cap, float extra)
{
    var ta = anim.GetBoneTransform(ba);
    var tb = anim.GetBoneTransform(bb);
    if (ta == null || tb == null) return;
    float r = EstimateRadius(ta.position, tb.position, verts, cap) + extra;
    list.Add(new Caps { name = name, a = ta.position, b = tb.position, r = r });
    if (verbose) sb.AppendLine(string.Format("  胶囊 {0,-8} 半径(实测) {1:F4} m", name, r));
}

float Penetration(Caps c, Vector3 p)
{
    Vector3 ab = c.b - c.a; float L2 = ab.sqrMagnitude;
    float t = L2 > 1e-9f ? Mathf.Clamp01(Vector3.Dot(p - c.a, ab) / L2) : 0f;
    Vector3 q = c.a + ab * t;
    return c.r - Vector3.Distance(p, q);
}

void Shot(Camera cam, string imgDir, string name, int W, int H, Vector3 center, Vector3 camPos, float orthoSize)
{
    if (orthoSize > 0f) { cam.orthographic = true; cam.orthographicSize = orthoSize; }
    else cam.orthographic = false;
    cam.transform.position = camPos;
    cam.transform.rotation = Quaternion.LookRotation(center - camPos, Vector3.up);

    var rt = RenderTexture.GetTemporary(W, H, 24);
    cam.targetTexture = rt; cam.Render(); cam.targetTexture = null;
    var prev = RenderTexture.active;
    RenderTexture.active = rt;
    var snap = new Texture2D(W, H, TextureFormat.RGB24, false);
    snap.ReadPixels(new Rect(0, 0, W, H), 0, 0); snap.Apply();
    RenderTexture.active = prev;
    RenderTexture.ReleaseTemporary(rt);
    System.IO.File.WriteAllBytes(System.IO.Path.Combine(imgDir, name + ".png"), snap.EncodeToPNG());
    Object.Destroy(snap);
}

return Body();

class Caps
{
    public string name; public Vector3 a; public Vector3 b; public float r;
}
