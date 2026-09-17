// q_diag_run.cs —— Run 状态实际播的是哪个片段？动画本体脚底在哪？
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

IEnumerator Body()
{
    var sb = new StringBuilder();
    string projRoot = Path.GetDirectoryName(Application.dataPath);
    yield return null; yield return null;

    // ---- A. 资产层：控制器 Run 状态挂的是什么 ----
    var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>("Assets/_Project/Animations/Player.controller");
    sb.AppendLine("=== A. 控制器资产 ===");
    foreach (var s in ctrl.layers[0].stateMachine.states)
        if (s.state.name == "Run" || s.state.name == "Idle" || s.state.name == "Walk")
            sb.AppendLine("  " + s.state.name + " → " + (s.state.motion != null ? s.state.motion.name : "<null>")
                + "  (" + (s.state.motion != null ? AssetDatabase.GetAssetPath(s.state.motion) : "-") + ")");

    sb.AppendLine();
    sb.AppendLine("=== B. 烘焙片段自身的曲线 ===");
    foreach (var p in new[] { "Assets/_Project/Animations/Baked/Run01_Ctrl.anim", "Assets/_Project/Animations/Baked/Run01_IdleArm.anim" })
    {
        var c = AssetDatabase.LoadAssetAtPath<AnimationClip>(p);
        if (c == null) { sb.AppendLine("  " + p + " 找不到"); continue; }
        sb.AppendLine("  " + c.name + "  len=" + c.length.ToString("F3") + "  绑定数=" + AnimationUtility.GetCurveBindings(c).Length
            + "  legacy=" + c.legacy + "  humanMotion=" + c.humanMotion
            + "  循环=" + c.isLooping);
    }

    // ---- C. 运行层：实际播放与脚底 ----
    var go = GameObject.Find("Player");
    if (go == null) { Debug.LogError("no Player"); yield break; }
    var anim = go.GetComponent<Animator>();
    var ctl = go.GetComponent<InkWash.Player.PlayerController>();
    var ik = go.GetComponent<InkWash.Player.FootIK>();
    var stance = go.GetComponentInChildren<InkWash.Player.CombatStance>(true);
    if (ctl != null) ctl.enabled = false;
    if (ik != null) ik.enableIk = false;
    if (stance != null) stance.enabled = false;

    var smrs = go.GetComponentsInChildren<SkinnedMeshRenderer>(true);
    var bake = new Mesh(); bake.MarkDynamic();
    float GroundY()
    {
        float g = go.transform.position.y;
        var hits = Physics.RaycastAll(go.transform.position + Vector3.up * 3f, Vector3.down, 10f);
        float best = float.MinValue;
        foreach (var h in hits)
        {
            if (h.collider != null && h.collider.transform.IsChildOf(go.transform)) continue;
            if (h.point.y > best) best = h.point.y;
        }
        return best > float.MinValue ? best : g;
    }
    float Lowest(float g)
    {
        float lo = float.MaxValue;
        for (int k = 0; k < smrs.Length; k++)
        {
            smrs[k].BakeMesh(bake);
            var verts = bake.vertices;
            var m = smrs[k].transform.localToWorldMatrix;
            for (int q = 0; q < verts.Length; q++) lo = Mathf.Min(lo, m.MultiplyPoint3x4(verts[q]).y - g);
        }
        return lo;
    }

    float groundY = GroundY();
    var buf = new List<AnimatorClipInfo>(4);
    sb.AppendLine();
    sb.AppendLine("=== C. 状态机实际播放（Animator.Play + Update）===");
    sb.AppendLine("  状态      实际片段              动画本体最低点min   max");
    foreach (var name in new[] { "Idle", "Walk", "Run" })
    {
        int hash = Animator.StringToHash(name);
        if (!anim.HasState(0, hash)) continue;
        float lo = 9999f, hi = -9999f;
        string played = "?";
        for (int i = 0; i <= 20; i++)
        {
            anim.Play(hash, 0, i / 20f);
            anim.Update(1f / 60f);
            buf.Clear();
            anim.GetCurrentAnimatorClipInfo(0, buf);
            if (buf.Count > 0 && buf[0].clip != null) played = buf[0].clip.name;
            float v = Lowest(groundY);
            if (v < lo) lo = v;
            if (v > hi) hi = v;
        }
        sb.AppendLine(string.Format("  {0,-9} {1,-22} {2,10:F3} m {3,8:F3} m", name, played, lo, hi));
        yield return null;
    }

    // ---- D. 直接 SampleAnimation 烘焙片段做对照 ----
    sb.AppendLine();
    sb.AppendLine("=== D. 直接 SampleAnimation（绕开状态机）===");
    foreach (var p in new[] { "Assets/_Project/Animations/Baked/Run01_Ctrl.anim", "Assets/_Project/Animations/Baked/Run01_IdleArm.anim" })
    {
        var c = AssetDatabase.LoadAssetAtPath<AnimationClip>(p);
        if (c == null) continue;
        float lo = 9999f, hi = -9999f;
        for (int i = 0; i <= 60; i++) { c.SampleAnimation(go, c.length * i / 60f); float v = Lowest(groundY); if (v < lo) lo = v; if (v > hi) hi = v; }
        sb.AppendLine(string.Format("  {0,-22} {1,10:F3} m {2,8:F3} m", c.name, lo, hi));
    }

    Object.Destroy(bake);
    if (ctl != null) ctl.enabled = true;
    if (ik != null) ik.enableIk = true;
    if (stance != null) stance.enabled = true;
    var cam = Camera.main;
    if (cam != null) { var rig = cam.GetComponent<InkWash.CameraRig.ThirdPersonCamera>(); if (rig != null) rig.SetMouseLookEnabled(true); }

    File.WriteAllText(Path.Combine(projRoot, "Tools/reports/q_diag_run.txt"), sb.ToString());
    Debug.Log("[q_diag_run] done");
    yield return null;
}

return Body();
