// q_run_probe.cs —— 「跑步时剑不随跑步动作改变」的事实清点
//
// 要一次分开三件事：
//   ① 哪些片段有**可用的右臂运动**（作为重烘焙的原料）
//   ② Run 状态下剑到底挂在**手里**还是**背上**（CombatStance: InCombat ? hand : backSocket）
//   ③ 手动推进 Run 时，剑的世界位移范围 vs 右手的世界位移范围
//      — 若剑范围≈0 ⇒ 剑是"焊死"的；若剑范围≈手范围 ⇒ 剑只是没有**次级运动**
using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;

public class QRunProbe
{
    static StringBuilder _sb;
    static void L(string s) { _sb.AppendLine(s); }

    static readonly string[] ArmChannels =
    {
        "Right Shoulder Down-Up", "Right Shoulder Front-Back",
        "Right Arm Down-Up", "Right Arm Front-Back", "Right Arm Twist In-Out",
        "Right Forearm Stretch", "Right Forearm Twist In-Out",
        "Right Hand Down-Up", "Right Hand In-Out",
    };
    static bool IsRightArm(string p) { return p.StartsWith("Right Shoulder") || p.StartsWith("Right Arm")
                                           || p.StartsWith("Right Forearm") || p.StartsWith("Right Hand"); }

    static string ArmActivity(AnimationClip c, out float maxRange)
    {
        maxRange = 0f; int act = 0;
        string best = "-"; float bestR = 0f;
        foreach (var b in AnimationUtility.GetCurveBindings(c))
        {
            if (!IsRightArm(b.propertyName)) continue;
            var rc = AnimationUtility.GetEditorCurve(c, b);
            if (rc == null || rc.length == 0) continue;
            float mn = float.MaxValue, mx = float.MinValue;
            for (int i = 0; i <= 24; i++)
            {
                float v = rc.Evaluate(c.length * i / 24f);
                mn = Mathf.Min(mn, v); mx = Mathf.Max(mx, v);
            }
            float r = mx - mn;
            if (r > 0.02f) act++;
            if (r > bestR) { bestR = r; best = b.propertyName; }
        }
        maxRange = bestR;
        return act + "/9(" + best + ")";
    }

    public static string Run()
    {
        _sb = new StringBuilder();
        L("================ q_run_probe ================");

        // ---------- ① 片段清单 ----------
        L("");
        L("---------- ① 含 Run/Walk/Sprint/Carry 的片段：右臂活动量 ----------");
        var guids = AssetDatabase.FindAssets("t:AnimationClip");
        var rows = new List<string>();
        foreach (var g in guids)
        {
            string p = AssetDatabase.GUIDToAssetPath(g);
            var c = AssetDatabase.LoadAssetAtPath<AnimationClip>(p);
            if (c == null) continue;
            string n = c.name;
            if (!(n.Contains("Run") || n.Contains("Walk") || n.Contains("Sprint")
                  || n.Contains("Carry") || n.Contains("Jog"))) continue;
            float mr; string a = ArmActivity(c, out mr);
            rows.Add(string.Format("  {0,-34} len={1,6:F3}  右臂活动 {2,-28} 最大range={3:F4}   [{4}]",
                                   n, c.length, a, mr, p));
        }
        foreach (var r in rows.OrderBy(x => x)) L(r);

        // ---------- ② 目标片段 vs 候选源：逐通道对照 ----------
        string bakedPath = "Assets/_Project/Animations/Baked/Run04_KI_Carry.anim";
        var baked = AssetDatabase.LoadAssetAtPath<AnimationClip>(bakedPath);
        L("");
        L("---------- ② 目标片段 " + (baked == null ? "**找不到**" : baked.name + " (len=" + baked.length.ToString("F3") + ")") + " ----------");
        if (baked != null)
        {
            L("  全部肌肉通道的 24 点极值（只看右臂 + 少数参照）:");
            foreach (var b in AnimationUtility.GetCurveBindings(baked))
            {
                if (!(IsRightArm(b.propertyName) || b.propertyName.StartsWith("Left Shoulder")
                      || b.propertyName.StartsWith("Left Arm") || b.propertyName.StartsWith("Left Forearm"))) continue;
                var rc = AnimationUtility.GetEditorCurve(baked, b);
                if (rc == null || rc.length == 0) continue;
                float mn = float.MaxValue, mx = float.MinValue;
                for (int i = 0; i <= 24; i++) { float v = rc.Evaluate(baked.length * i / 24f); mn = Mathf.Min(mn, v); mx = Mathf.Max(mx, v); }
                L(string.Format("    {0,-30} min={1,8:F4} max={2,8:F4} range={3:F4}{4}",
                    b.propertyName, mn, mx, mx - mn, (mx - mn) <= 0.02f ? "   ← **常数（冻结）**" : ""));
            }
        }

        // 候选源：Kevin Iglesias 的原始跑步
        L("");
        L("---------- ③ 候选源片段（KevinIglesias Run01 / Feng 自己的步伐）----------");
        string[] cands = {
            "Assets/ThirdParty/KevinIglesias/Animations/Male/Movement/Run/HumanM@Run01_Forward.fbx",
            "Assets/_Project/Animations/Feng_Walk_Loop.anim",
            "Assets/_Project/Animations/Feng_Idle_Loop.anim",
            "Assets/_Project/Animations/Sword_Idle_Loop.anim",
        };
        foreach (var cp in cands)
        {
            var all = AssetDatabase.LoadAllAssetsAtPath(cp).OfType<AnimationClip>().ToArray();
            if (all.Length == 0) { L("  !! 无片段 " + cp); continue; }
            L("  " + cp);
            foreach (var c in all)
            {
                float mr; string a = ArmActivity(c, out mr);
                L(string.Format("     {0,-30} len={1,6:F3} 右臂 {2,-28} range={3:F4}", c.name, c.length, a, mr));
            }
        }

        // ---------- ④ 运行时：剑挂在哪 + 位移范围 ----------
        L("");
        L("---------- ④ 运行时：Run 状态下剑与右手的世界位移 ----------");
        var pc = UnityEngine.Object.FindObjectOfType<InkWash.Player.PlayerController>();
        var swordGo = GameObject.Find("W_Sword");
        if (pc == null) L("  !! 没找到 PlayerController（是否在 Play？）");
        else if (swordGo == null) L("  !! 场景里没有 W_Sword（可能尚未实例化）");
        else
        {
            var anim = pc.GetComponentInChildren<Animator>();
            var t = swordGo.transform;
            var chain = new List<string>();
            while (t != null) { chain.Add(t.name); t = t.parent; }
            L("  剑的父链: " + string.Join(" < ", chain.ToArray()));
            L("  剑父节点是背部 socket? " + (chain.Count > 1 && chain[1].Contains("BackSocket") ? "是" : "否（应是手里）"));
            if (anim == null) L("  !! 没找到 Animator");
            else
            {
                var rh = anim.GetBoneTransform(HumanBodyBones.RightHand);
                var ra = anim.GetBoneTransform(HumanBodyBones.RightLowerArm);
                L("  右手骨=" + (rh == null ? "null" : rh.name) + "  右下臂骨=" + (ra == null ? "null" : ra.name));
                L("  animator.runtimeAnimatorController=" + (anim.runtimeAnimatorController == null ? "null" : anim.runtimeAnimatorController.name));

                anim.applyRootMotion = false;
                anim.Play("Run", 0, 0f);
                anim.Update(0f);   // 让 Play 生效

                Vector3 p0 = swordGo.transform.position;
                Vector3 sMin = p0, sMax = p0, hMin = Vector3.zero, hMax = Vector3.zero, oMin = Vector3.zero, oMax = Vector3.zero;
                bool first = true;
                var armVals = new Dictionary<string, float[]>();
                for (int i = 0; i < 120; i++)
                {
                    anim.Update(1f / 60f);
                    Vector3 sp = swordGo.transform.position;
                    sMin = Vector3.Min(sMin, sp); sMax = Vector3.Max(sMax, sp);
                    if (rh != null) { var hp = rh.position; if (first) { hMin = hp; hMax = hp; } else { hMin = Vector3.Min(hMin, hp); hMax = Vector3.Max(hMax, hp); } }
                    // 剑相对右手的偏移（判断"焊死"）
                    if (rh != null) { var off = rh.InverseTransformPoint(sp); if (first) { oMin = off; oMax = off; } else { oMin = Vector3.Min(oMin, off); oMax = Vector3.Max(oMax, off); } }
                    // 右臂肌肉实时值
                    foreach (var ch in ArmChannels)
                    {
                        if (!armVals.ContainsKey(ch)) armVals[ch] = new float[] { float.MaxValue, float.MinValue };
                        float v = anim.GetFloat(ch);
                        armVals[ch][0] = Mathf.Min(armVals[ch][0], v);
                        armVals[ch][1] = Mathf.Max(armVals[ch][1], v);
                    }
                    first = false;
                }
                L(string.Format("  剑  世界位移范围: {0}", (sMax - sMin).ToString("F4")));
                if (rh != null)
                {
                    L(string.Format("  右手世界位移范围: {0}", (hMax - hMin).ToString("F4")));
                    L(string.Format("  剑在右手局部系下的偏移范围: {0}   ← 全 0 = 剑相对手完全焊死",
                        (oMax - oMin).ToString("F4")));
                }
                L("  右臂肌肉实时 range（手动推进 Run 120 帧）:");
                foreach (var kv in armVals)
                {
                    float r = kv.Value[1] - kv.Value[0];
                    L(string.Format("    {0,-30} range={1:F4}{2}", kv.Key, r, r <= 0.02f ? "   ← 冻结" : ""));
                }
            }
        }

        L("");
        L("================ end ================");
        string outp = Path.GetFullPath(Path.Combine(Application.dataPath, "../Tools/reports/q_run_probe.txt"));
        File.WriteAllText(outp, _sb.ToString());
        return "written: " + outp + "  (" + _sb.Length + " chars)";
    }
}

return QRunProbe.Run();
