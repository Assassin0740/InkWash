// q_offset2.cs —— 量新片段的「无 IK 最低点」，给出建议偏移（口径：脚全程踩地取最浅，有腾空取最深）
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

    var clips = new (string label, AnimationClip clip, bool flight)[]
    {
        ("Rig|Idle_Loop (非战斗)", FindSub("Assets/ThirdParty/Quaternius/UniversalAnimationLibrary/Unity/AnimationLibrary_Unity_Standard.fbx", "Rig|Idle_Loop"), false),
        ("Sword_Idle_Loop (战斗)", AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/_Project/Animations/Sword_Idle_Loop.anim"), false),
        ("Run01_IdleArm (烘焙跑)", AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/_Project/Animations/Baked/Run01_IdleArm.anim"), true),
        ("Run01_Forward (原跑)", FindSub("Assets/ThirdParty/KevinIglesias/Animations/Male/Movement/Run/HumanM@Run01_Forward.fbx", null), true),
    };

    sb.AppendLine("地面Y = " + groundY.ToString("F3"));
    sb.AppendLine("片段                      时长    无IK最低点min  无IK最低点max   口径      建议偏移   现值");
    var cur = new Dictionary<string, float> { { "Idle", 0.0307f }, { "Run", 0.1735f } };
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
        string now = label.Contains("Idle") ? cur["Idle"].ToString("F4") : cur["Run"].ToString("F4");
        sb.AppendLine(string.Format("{0,-24} {1:F3}s  {2,11:F4}  {3,12:F4}   {4}   {5,8:F4}  {6}",
            label, clip.length, lo, hi, flight ? "取最深" : "取最浅", reco, now));
    }

    sb.AppendLine();
    sb.AppendLine("注意：Idle 状态的片段会在运行期由 CombatStance 在两段之间切换，");
    sb.AppendLine("      而 FootIK.stateOffsets 只按「状态名」查 ⇒ 一个数值不可能同时正确。");

    anim.enabled = animWas;
    if (ctl != null) ctl.enabled = true;
    if (ik != null) ik.enabled = true;
    if (stance != null) stance.enabled = true;
    Object.Destroy(bake);
    var cam = Camera.main;
    if (cam != null) { var rig = cam.GetComponent<InkWash.CameraRig.ThirdPersonCamera>(); if (rig != null) rig.SetMouseLookEnabled(true); }

    File.WriteAllText(Path.Combine(projRoot, "Tools/reports/q_offset2.txt"), sb.ToString());
    Debug.Log("[q_offset2] done");
    yield return null;
}

static AnimationClip FindSub(string p, string n)
{
    foreach (var o in AssetDatabase.LoadAllAssetsAtPath(p))
    {
        var c = o as AnimationClip;
        if (c != null && !c.name.StartsWith("__preview__") && (n == null || c.name == n)) return c;
    }
    return null;
}

return Body();
