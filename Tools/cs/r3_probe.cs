// r3_probe.cs —— 编辑态取证：哪些片段的右臂被"冻结"了
//
// 背景：q_bake_run5.cs 第 101~113 行把整条右臂通道（Right Shoulder/Arm/Forearm/Hand 的
//   全部 muscle 曲线）替换成了**两个同值 keyframe 的常数**，值取自 Rig|Sword_Idle @0.35。
//   ⇒ 跑步时右臂完全不动 ⇒ 剑固死在手骨上 ⇒ 用户看到的「剑没随着跑步动作改变」。
//
// 本探针不问"哪一行写的"，只量事实：
//   对每个被控制器/CombatStance 引用的片段，逐条打印右臂肌肉曲线的 [min,max] 与 range。
//   range ≈ 0 的条目 = 被冻结。同时给出左臂作为对照（左臂没被动过 ⇒ 活动量就是"应有"的量级）。
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine.Animations;

public class R3Probe
{
    static StringBuilder _sb;
    static void L(string s) { _sb.AppendLine(s); }

    static readonly string[] ArmTokens =
    {
        "Right Shoulder", "Right Arm", "Right Forearm", "Right Hand",
        "Left Shoulder",  "Left Arm",  "Left Forearm",  "Left Hand",
    };

    static string ArmSide(string prop)
    {
        if (prop.StartsWith("Right")) return "R";
        if (prop.StartsWith("Left")) return "L";
        return null;
    }

    /// 返回 (右臂活动曲线数, 右臂总数, 右臂最大 range, 左臂活动曲线数, 左臂总数, 左臂最大 range, 明细)
    static void ArmStats(AnimationClip c, out int rMoved, out int rTotal, out float rMax,
                         out int lMoved, out int lTotal, out float lMax, out string detail)
    {
        rMoved = rTotal = lMoved = lTotal = 0; rMax = lMax = 0f;
        var sb = new StringBuilder();
        if (c == null) { detail = "(null clip)"; return; }

        foreach (var b in AnimationUtility.GetCurveBindings(c))
        {
            string side = null;
            bool hit = false;
            for (int i = 0; i < ArmTokens.Length; i++)
                if (b.propertyName.StartsWith(ArmTokens[i])) { side = ArmSide(b.propertyName); hit = true; break; }
            if (!hit || side == null) continue;

            var curve = AnimationUtility.GetEditorCurve(c, b);
            if (curve == null || curve.keys.Length == 0) continue;
            float mn = float.MaxValue, mx = float.MinValue;
            for (int i = 0; i < curve.keys.Length; i++)
            { float v = curve.keys[i].value; if (v < mn) mn = v; if (v > mx) mx = v; }
            float range = mx - mn;

            if (side == "R")
            {
                rTotal++;
                if (range > 0.02f) rMoved++;
                if (range > rMax) rMax = range;
                if (range <= 0.02f)
                    sb.AppendLine("          [冻结] " + b.propertyName + "  常数 " + mx.ToString("F4"));
            }
            else
            {
                lTotal++;
                if (range > 0.02f) lMoved++;
                if (range > lMax) lMax = range;
            }
        }
        detail = sb.ToString();
    }

    public static string Run()
    {
        _sb = new StringBuilder();
        L("================ r3_probe ================");

        // ---------------- 收集所有相关片段 ----------------
        var clips = new List<AnimationClip>();
        var seen = new HashSet<int>();

        var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>("Assets/_Project/Animations/Player.controller");
        if (ctrl != null)
        {
            var sm = ctrl.layers[0].stateMachine;
            foreach (var cs in sm.states)
            {
                var cl = cs.state.motion as AnimationClip;
                if (cl != null && seen.Add(cl.GetInstanceID())) clips.Add(cl);
            }
            foreach (var p in ctrl.parameters) L("  param " + p.name + " : " + p.type);
        }
        else L("!! 找不到 Player.controller");

        string[] extra = {
            "Assets/_Project/Animations/Feng_Walk_Loop.anim",
            "Assets/_Project/Animations/Feng_Idle_Loop.anim",
            "Assets/_Project/Animations/Sword_Idle_Loop.anim",
            "Assets/_Project/Animations/Baked/Idle_Carry_A.anim",
            "Assets/_Project/Animations/Baked/Run04_KI_Carry.anim",
        };
        foreach (var p in extra)
        {
            var c = AssetDatabase.LoadAssetAtPath<AnimationClip>(p);
            if (c != null && seen.Add(c.GetInstanceID())) clips.Add(c);
        }

        // ---------------- 逐片段量 ----------------
        L("");
        L("---------- ① 各片段的右臂活动量（左臂作对照） ----------");
        L(string.Format("  {0,-34} {1,14} {2,14}   {3}", "片段", "右臂 活动/总", "左臂 活动/总", "右臂最大range"));
        var frozen = new List<string>();
        foreach (var c in clips)
        {
            int rm, rt, lm, lt; float rmax, lmax; string detail;
            ArmStats(c, out rm, out rt, out rmax, out lm, out lt, out lmax, out detail);
            L(string.Format("  {0,-34} {1,14} {2,14}   {3:F4}   len={4:F3}",
                c.name, rm + "/" + rt, lm + "/" + lt, rmax, c.length));
            if (detail.Length > 0) { L(detail.TrimEnd()); }
            if (rt > 0 && rm == 0) frozen.Add(c.name);
        }

        L("");
        L("  ★ 右臂**完全冻结**的片段：" + (frozen.Count == 0 ? "(无)" : string.Join(", ", frozen.ToArray())));

        // ---------------- ② 地面材质现状 ----------------
        L("");
        L("---------- ② 地面/场景材质 ----------");
        var ground = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/Materials/M_Whitebox_Ground.mat");
        if (ground != null)
        {
            L("  M_Whitebox_Ground _Bands=" + ground.GetFloat("_Bands")
              + "  _BandBias=" + ground.GetFloat("_BandBias")
              + "  _GrainScale=" + ground.GetFloat("_GrainScale")
              + "  _GrainAmp=" + ground.GetFloat("_GrainAmp")
              + "  _StrokeScale=" + ground.GetFloat("_StrokeScale")
              + "  _StrokeStretch=" + ground.GetFloat("_StrokeStretch")
              + "  _StrokeAmp=" + ground.GetFloat("_StrokeAmp")
              + "  _BrushStrength=" + ground.GetFloat("_BrushStrength"));
        }

        // ---------------- ③ 场景里地面/台子的真实尺寸 ----------------
        L("");
        L("---------- ③ 场景里的地面/台子 ----------");
        var roots = UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects();
        foreach (var go in roots)
        {
            L("  root: " + go.name);
            foreach (var t in go.GetComponentsInChildren<Transform>(true))
            {
                var mr = t.GetComponent<MeshRenderer>();
                if (mr == null) continue;
                var mf = t.GetComponent<MeshFilter>();
                string meshName = mf != null && mf.sharedMesh != null ? mf.sharedMesh.name : "-";
                var b = mr.bounds;
                L("    " + Path2(t) + "   mesh=" + meshName + "  size=" + b.size.ToString("F2")
                  + "  scale=" + t.lossyScale.ToString("F2")
                  + "  mat=" + (mr.sharedMaterial != null ? mr.sharedMaterial.name : "null"));
            }
        }

        L("");
        L("================ end ================");
        string outp = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "../Tools/reports/r3_probe.txt"));
        System.IO.File.WriteAllText(outp, _sb.ToString());
        return "written: " + outp + "  (" + _sb.Length + " chars)";
    }

    static string Path2(Transform t)
    {
        var l = new List<string>();
        var c = t;
        while (c != null && l.Count < 4) { l.Add(c.name); c = c.parent; }
        l.Reverse();
        return string.Join("/", l.ToArray());
    }
}

return R3Probe.Run();
