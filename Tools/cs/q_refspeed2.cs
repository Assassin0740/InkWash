// refSpeed 标定 v2：改量**脚踝骨骼**而不是网格最低点。
// v1 踩的坑：真人比例模型走路时脚掌会「脚跟→脚尖」滚动，脚底最低点在着地相内部
// 平移了约一个脚掌长（0.25m），被当成「脚相对根的后移」→ 速度虚高近一倍。
// 脚踝没有滚动伪影，是干净的量。
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

IEnumerator Body()
{
    var sb = new System.Text.StringBuilder();
    string projRoot = System.IO.Path.GetDirectoryName(Application.dataPath);

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
    if (stance != null) stance.enabled = false;
    bool animWas = anim.enabled;
    anim.enabled = false;

    // 关键：把「模型容器」的缩放打出来 —— 骨骼世界位移里已经含了它，
    // refSpeed 要的是世界量（与 CurrentSpeed 同量纲），所以**不该**再除它。
    Transform vis = null;
    foreach (var t in go.GetComponentsInChildren<Transform>(true))
        if (t.parent == go.transform && t.GetComponent<SkinnedMeshRenderer>() != null) { vis = t; break; }
    if (vis == null)
        foreach (var t in go.GetComponentsInChildren<Transform>(true))
            if (t.name == "Visual") { vis = t; break; }
    sb.AppendLine("Visual = " + (vis != null ? vis.name + "  localScale=" + vis.localScale.ToString("F3") + "  localPos=" + vis.localPosition.ToString("F3") : "<未找到>"));
    sb.AppendLine("角色根 pos = " + go.transform.position.ToString("F3") + "   Animator.isHuman = " + anim.isHuman);

    var lf = anim.GetBoneTransform(HumanBodyBones.LeftFoot);
    var rf = anim.GetBoneTransform(HumanBodyBones.RightFoot);
    var hips = anim.GetBoneTransform(HumanBodyBones.Hips);
    sb.AppendLine("LeftFoot=" + (lf != null ? lf.name : "<null>") + "  RightFoot=" + (rf != null ? rf.name : "<null>"));
    // 身高（逐顶点量，判读用）
    var smrs = go.GetComponentsInChildren<SkinnedMeshRenderer>(true);
    var bake = new Mesh(); bake.MarkDynamic();

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

    var cands = new List<(string label, AnimationClip clip, float assumedStepsPerLoop, string kind)>();
    cands.Add(("KayKit Walking_A", Sub("Assets/ThirdParty/KayKit/Animations/Rig_Medium/Rig_Medium_MovementBasic.fbx", "Walking_A"), 2f, "walk"));
    cands.Add(("Feng Walk", Anim("Assets/Char_Feng/Animation/Walk.anim"), 2f, "walk"));
    cands.Add(("KI Walk01_Forward", Sub("Assets/ThirdParty/KevinIglesias/Animations/Male/Movement/Walk/HumanM@Walk01_Forward.fbx", null), 2f, "walk"));
    cands.Add(("KayKit Running_A", Sub("Assets/ThirdParty/KayKit/Animations/Rig_Medium/Rig_Medium_MovementBasic.fbx", "Running_A"), 2f, "run"));
    cands.Add(("KI Run01_Forward", Sub("Assets/ThirdParty/KevinIglesias/Animations/Male/Movement/Run/HumanM@Run01_Forward.fbx", null), 2f, "run"));

    sb.AppendLine();
    sb.AppendLine("片段                  时长   原生地面速度  已着陆占比  步/循环  步频/s  单步位移  身高");
    sb.AppendLine("--------------------------------------------------------------------------------------");

    int N = 240;
    foreach (var (label, clip, spp, kind) in cands)
    {
        if (clip == null) { sb.AppendLine(string.Format("{0,-20} !! 缺资产", label)); continue; }
        float len = clip.length;
        float dt = len / N;

        var lx = new float[N + 1]; var lz = new float[N + 1]; var ly = new float[N + 1];
        var rx = new float[N + 1]; var rz = new float[N + 1]; var ry = new float[N + 1];
        float minLY = float.MaxValue, minRY = float.MaxValue, maxTop = float.MinValue;

        for (int i = 0; i <= N; i++)
        {
            clip.SampleAnimation(go, len * i / N);
            var lp = go.transform.InverseTransformPoint(lf.position);
            var rp = go.transform.InverseTransformPoint(rf.position);
            lx[i] = lp.x; ly[i] = lp.y; lz[i] = lp.z;
            rx[i] = rp.x; ry[i] = rp.y; rz[i] = rp.z;
            if (ly[i] < minLY) minLY = ly[i];
            if (ry[i] < minRY) minRY = ry[i];
            if (i == 0)
            {
                foreach (var s in smrs)
                {
                    s.BakeMesh(bake, true);
                    var m = s.transform.localToWorldMatrix;
                    foreach (var v in bake.vertices) { float y = m.MultiplyPoint3x4(v).y; if (y > maxTop) maxTop = y; }
                }
            }
        }

        // 逐脚：着地 = 脚踝高度进入「该脚最低点 + 4cm」带内，且竖直速度小
        var spd = new List<float>();
        int landed = 0;
        for (int i = 0; i < N; i++)
        {
            foreach (int s in new int[] { 0, 1 })
            {
                float y0 = s == 0 ? ly[i] : ry[i], y1 = s == 0 ? ly[i + 1] : ry[i + 1];
                float mn = s == 0 ? minLY : minRY;
                float x0 = s == 0 ? lx[i] : rx[i], x1 = s == 0 ? lx[i + 1] : rx[i + 1];
                float z0 = s == 0 ? lz[i] : rz[i], z1 = s == 0 ? lz[i + 1] : rz[i + 1];
                if (y0 > mn + 0.04f || y1 > mn + 0.04f) continue;
                landed++;
                spd.Add(Mathf.Sqrt((x1 - x0) * (x1 - x0) + (z1 - z0) * (z1 - z0)) / dt);
            }
        }
        spd.Sort();
        float med = spd.Count > 0 ? spd[spd.Count / 2] : 0f;
        float cadence = len > 0f ? spp / len : 0f;
        float stepLen = cadence > 0f ? med / cadence : 0f;

        sb.AppendLine(string.Format("{0,-20} {1,5:F3}s {2,9:F2} m/s {3,8:F0}% {4,7:F0} {5,7:F2} {6,8:F2}m  {7:F2}m",
            label, len, med, 100f * landed / (2f * N), spp, cadence, stepLen, maxTop));
    }

    Object.Destroy(bake);
    anim.enabled = animWas;
    if (ctl != null) ctl.enabled = true;
    if (ik != null) ik.enabled = true;
    if (stance != null) stance.enabled = true;

    sb.AppendLine();
    sb.AppendLine("判读：");
    sb.AppendLine("  · 原生地面速度 = 1.0× 播放时角色应匹配的世界速度 → 直接作为 refSpeed");
    sb.AppendLine("  · 单步位移常识校验：走路 0.6~0.8m / 慢跑 1.0~1.3m / 冲刺 1.6~2.0m");
    sb.AppendLine("  · 步频常识校验：走路 1.5~2.0 步/秒 / 跑步 2.6~4.4 步/秒");

    System.IO.File.WriteAllText(System.IO.Path.Combine(projRoot, "Tools/reports/q_refspeed2.txt"), sb.ToString());
    Debug.Log("[q_refspeed2] done");
    yield return null;
}
return Body();
