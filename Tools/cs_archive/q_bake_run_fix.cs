// q_bake_run_fix.cs —— 把跑步片段的**右臂**还原成 Kevin Iglesias 原始动画
//
// 事实（q_run_probe / r3_probe 实测）：
//   Run01_Forward（原始，HumanM@Run01_Forward.fbx）  len=0.600  右臂 9/9  range=1.9634
//   Run04_KI_Carry（项目内烘焙产物）                  len=0.600  右臂 0/9  range=0.0000
//   Run04_KI_Carry 的**左臂** range=1.9605 完全正常
//   ⇒ 角色跑步时"一边甩臂、一边僵死"（用户报的"剑没有随着跑步动作而改变"）
//
// 为什么可以直接还原（不需要 delta 法）：
//   两者长度完全相同（0.600 s）、同为 humanoid 肌肉空间 ⇒ 逐通道直接拷贝即可。
//   原烘焙把右臂写成常数的理由（保持"搬运/持剑"手型）在跑步这条路上**不成立** ——
//   CombatStance 在非战斗时把剑挂到 `BackSocket`（实测父链确认），此时手上没有武器，
//   右臂没有任何理由被冻住。
//
// ★ 只改 `Run04_KI_Carry.anim`；`Idle_Carry_A.anim` 的右臂冻结是**有意为之**
//   （战斗待机要求"屈肘持剑、剑身固死在手骨"），不动它。
using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;

public class QBakeRunFix
{
    static StringBuilder _sb;
    static void L(string s) { _sb.AppendLine(s); }

    const string TARGET = "Assets/_Project/Animations/Baked/Run04_KI_Carry.anim";
    const string SOURCE_FBX = "Assets/ThirdParty/KevinIglesias/Animations/Male/Movement/Run/HumanM@Run01_Forward.fbx";
    const string SOURCE_CLIP = "Run01_Forward";

    // gain=1 ⇒ 完全用原始；<1 ⇒ 只借一部分摆幅（保留"持械跑"的收敛感）
    const float GAIN = 1.0f;

    static bool IsRightArm(string p)
    {
        return p.StartsWith("Right Shoulder") || p.StartsWith("Right Arm")
            || p.StartsWith("Right Forearm") || p.StartsWith("Right Hand");
    }

    static float Range(AnimationClip c, string prop)
    {
        foreach (var b in AnimationUtility.GetCurveBindings(c))
        {
            if (b.propertyName != prop) continue;
            var rc = AnimationUtility.GetEditorCurve(c, b);
            if (rc == null || rc.length == 0) return 0f;
            float mn = float.MaxValue, mx = float.MinValue;
            for (int i = 0; i <= 48; i++) { float v = rc.Evaluate(c.length * i / 48f); mn = Mathf.Min(mn, v); mx = Mathf.Max(mx, v); }
            return mx - mn;
        }
        return 0f;
    }

    static string ArmSummary(AnimationClip c)
    {
        int act = 0; float best = 0;
        foreach (var b in AnimationUtility.GetCurveBindings(c))
        {
            if (!IsRightArm(b.propertyName)) continue;
            float r = Range(c, b.propertyName);
            if (r > 0.02f) act++;
            best = Mathf.Max(best, r);
        }
        return act + "/9  最大range=" + best.ToString("F4");
    }

    public static string Run()
    {
        _sb = new StringBuilder();
        L("================ q_bake_run_fix ================");

        var target = AssetDatabase.LoadAssetAtPath<AnimationClip>(TARGET);
        if (target == null) { L("!! 找不到目标片段 " + TARGET); return Flush(); }

        var srcAll = AssetDatabase.LoadAllAssetsAtPath(SOURCE_FBX).OfType<AnimationClip>().ToArray();
        var src = srcAll.FirstOrDefault(c => c.name == SOURCE_CLIP);
        if (src == null) { L("!! 源片段 " + SOURCE_CLIP + " 不在 " + SOURCE_FBX
                              + "（里面是：" + string.Join(", ", srcAll.Select(c => c.name).ToArray()) + "）"); return Flush(); }

        L("");
        L("  目标 " + target.name + "  len=" + target.length.ToString("F3") + "  右臂 " + ArmSummary(target));
        L("  源   " + src.name    + "  len=" + src.length.ToString("F3")    + "  右臂 " + ArmSummary(src));

        // 源片段的右臂曲线表
        var srcMap = new Dictionary<string, AnimationCurve>();
        foreach (var b in AnimationUtility.GetCurveBindings(src))
            if (IsRightArm(b.propertyName)) srcMap[b.propertyName] = AnimationUtility.GetEditorCurve(src, b);

        L("");
        L("  源片段里可用的右臂通道 " + srcMap.Count + " 条：");
        foreach (var kv in srcMap.OrderBy(k => k.Key))
            L("    " + kv.Key.PadRight(30) + " 关键帧=" + kv.Value.length
              + "  均值=" + Mean(kv.Value).ToString("F4"));

        // 目标里要被替换/补齐的通道
        var tgtBindings = AnimationUtility.GetCurveBindings(target).Where(b => IsRightArm(b.propertyName)).ToList();
        L("");
        L("  目标片段现有右臂通道 " + tgtBindings.Count + " 条，旧值（当前的搬运常数值）：");

        int replaced = 0, added = 0;
        var done = new HashSet<string>();
        foreach (var b in tgtBindings)
        {
            var oldCurve = AnimationUtility.GetEditorCurve(target, b);
            float oldVal = Mean(oldCurve);
            if (!srcMap.ContainsKey(b.propertyName))
            {
                L("    !! 源里没有 " + b.propertyName + "（保持原样 " + oldVal.ToString("F4") + "）");
                continue;
            }
            var sc = srcMap[b.propertyName];
            var nc = new AnimationCurve();
            float srcMean = Mean(sc);
            // 时间对齐：两段等长则 1:1；否则按比例映射（保险）
            float k = (target.length > 0.0001f) ? (src.length / target.length) : 1f;
            foreach (var kf in sc.keys)
            {
                float t = Mathf.Clamp(kf.time / k, 0f, target.length);
                float v = GAIN * kf.value + (1f - GAIN) * srcMean;
                nc.AddKey(new Keyframe(t, v, kf.inTangent, kf.outTangent));
            }
            AnimationUtility.SetEditorCurve(target, b, nc);
            L(string.Format("    {0,-30} 旧常数={1,8:F4} → 新 range={2:F4}", b.propertyName, oldVal, Range(target, b.propertyName)));
            replaced++; done.Add(b.propertyName);
        }
        // 源里有、目标里没有的通道 → 补上（否则手臂会缺自由度）
        foreach (var kv in srcMap)
        {
            if (done.Contains(kv.Key)) continue;
            var nc = new AnimationCurve();
            float k = (target.length > 0.0001f) ? (src.length / target.length) : 1f;
            foreach (var kf in kv.Value.keys)
                nc.AddKey(new Keyframe(Mathf.Clamp(kf.time / k, 0f, target.length), kf.value, kf.inTangent, kf.outTangent));
            AnimationUtility.SetEditorCurve(target, new EditorCurveBinding
            {
                path = "", type = typeof(Animator), propertyName = kv.Key
            }, nc);
            L("    + 补入缺失通道 " + kv.Key + "  新 range=" + Range(target, kv.Key).ToString("F4"));
            added++;
        }

        EditorUtility.SetDirty(target);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        // 复核（重新从磁盘读）
        var re = AssetDatabase.LoadAssetAtPath<AnimationClip>(TARGET);
        L("");
        L("  ==== 复核（重新读盘） ====");
        L("  " + re.name + "  len=" + re.length.ToString("F3") + "  右臂 " + ArmSummary(re));
        L("  替换 " + replaced + " 条，补入 " + added + " 条");
        L("  左臂对照（应保持不变）:");
        foreach (var b in AnimationUtility.GetCurveBindings(re))
            if (b.propertyName.StartsWith("Left Shoulder") || b.propertyName.StartsWith("Left Arm")
                || b.propertyName.StartsWith("Left Forearm"))
                L("    " + b.propertyName.PadRight(30) + " range=" + Range(re, b.propertyName).ToString("F4"));

        return Flush();
    }

    static float Mean(AnimationCurve c)
    {
        if (c == null || c.length == 0) return 0f;
        float s = 0;
        for (int i = 0; i < c.length; i++) s += c.keys[i].value;
        return s / c.length;
    }

    static string Flush()
    {
        L("");
        L("================ end ================");
        string outp = Path.GetFullPath(Path.Combine(Application.dataPath, "../Tools/reports/q_bake_run_fix.txt"));
        File.WriteAllText(outp, _sb.ToString());
        return "written: " + outp + "  (" + _sb.Length + " chars)";
    }
}

return QBakeRunFix.Run();
