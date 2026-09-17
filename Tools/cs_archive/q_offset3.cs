// q_offset3.cs —— 换片段后重测 FootIK 偏移（口径：脚全程踩地取最浅 hi，有腾空相取最深 lo）
//   本版片段表已更新：Run 状态现在是烘焙的 Run01_Carry，战斗待机是 Sword_Idle_Loop。
//   同时打印预制体上**当前**的 stateOffsets，便于对照。
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

IEnumerator Body()
{
    var sb = new StringBuilder();
    string projRoot = Path.GetDirectoryName(Application.dataPath);
    yield return null; yield return null;

    var go = GameObject.Find("Player");
    if (go == null) { Debug.LogError("no Player"); yield break; }
    var anim = go.GetComponent<Animator>();
    var ctl = go.GetComponent<InkWash.Player.PlayerController>();
    var ik = go.GetComponent<InkWash.Player.FootIK>();
    var stance = go.GetComponentInChildren<InkWash.Player.CombatStance>(true);
    if (ctl != null) ctl.enabled = false;
    if (ik != null) ik.enabled = false;
    if (stance != null) stance.enabled = false;
    bool animWas = anim.enabled;
    anim.enabled = false;

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

    sb.AppendLine("══════ 预制体当前 stateOffsets ══════");
    if (ik != null && ik.stateOffsets != null)
    {
        sb.AppendLine("  noGroundFixStates = " + (ik.noGroundFixStates != null ? string.Join(",", ik.noGroundFixStates) : "null")
                      + "   groundClearance = " + ik.groundClearance.ToString("F4"));
        foreach (var e in ik.stateOffsets)
            sb.AppendLine("  " + (e.state ?? "<null>").PadRight(28) + " y = " + e.y.ToString("F4"));
    }
    sb.AppendLine();

    var clips = new (string label, AnimationClip clip, bool flight)[]
    {
        ("Feng_Idle_Loop   非战斗待机",  FindOne("Assets/ThirdParty/Char_Feng/Animations", "Feng_Idle_Loop"), false),
        ("Sword_Idle_Loop  战斗待机",    AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/_Project/Animations/Sword_Idle_Loop.anim"), false),
        ("Feng_Walk_Loop   走路",        FindOne("Assets/ThirdParty/Char_Feng/Animations", "Feng_Walk_Loop"), false),
        ("Run01_Carry      跑步(烘焙)",  AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/_Project/Animations/Baked/Run01_Carry.anim"), true),
    };

    sb.AppendLine("地面Y = " + groundY.ToString("F3"));
    sb.AppendLine("片段                          时长    无IK最低min  无IK最低max   口径     建议偏移");
    sb.AppendLine("---------------------------- ------- ------------ ------------ -------- ----------");
    foreach (var (label, clip, flight) in clips)
    {
        if (clip == null) { sb.AppendLine(label + "  ★ 找不到"); continue; }
        const int N = 120;
        float lo = 9999f, hi = -9999f;
        for (int i = 0; i <= N; i++)
        {
            clip.SampleAnimation(go, clip.length * i / (float)N);
            float v = lowest();
            if (v < lo) lo = v;
            if (v > hi) hi = v;
        }
        float pick = flight ? lo : hi;
        float reco = -pick + 0.005f;
        sb.AppendLine(string.Format("{0,-28} {1:F3}s  {2,11:F4}  {3,12:F4}   {4}  {5,8:F4}",
            label, clip.length, lo, hi, flight ? "取最深" : "取最浅", reco));
    }

    sb.AppendLine();
    sb.AppendLine("口径说明：FootIK 的 BodyLift = Clamp(need, 0, maxLift) **只会往上抬**，");
    sb.AppendLine("          所以静态偏移取「最浅」= 保证不靠 IK 也不穿地；有腾空相的片段取「最深」");
    sb.AppendLine("          = 落地那一瞬正好触地、腾空帧自然离地。");

    anim.enabled = animWas;
    if (ctl != null) ctl.enabled = true;
    if (ik != null) ik.enabled = true;
    if (stance != null) stance.enabled = true;
    Object.Destroy(bake);
    var cam = Camera.main;
    if (cam != null) { var rig = cam.GetComponent<InkWash.CameraRig.ThirdPersonCamera>(); if (rig != null) rig.SetMouseLookEnabled(true); }

    File.WriteAllText(Path.Combine(projRoot, "Tools/reports/q_offset3.txt"), sb.ToString());
    Debug.Log("[q_offset3] done");
    yield return null;
}

static AnimationClip FindOne(string dir, string name)
{
    if (!Directory.Exists(dir)) return null;
    foreach (var p in Directory.GetFiles(dir, "*.fbx", SearchOption.AllDirectories))
    {
        foreach (var o in AssetDatabase.LoadAllAssetsAtPath(p.Replace('\\', '/')))
        {
            var c = o as AnimationClip;
            if (c != null && c.name == name) return c;
        }
    }
    return null;
}

return Body();
