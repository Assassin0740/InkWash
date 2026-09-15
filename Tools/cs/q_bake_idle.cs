// q_bake_idle.cs —— 烘焙「持剑待机」候选：以自然站姿为底座，只换手臂（按 humanoid 肌肉通道分组）
//
// 为什么需要：现行持剑待机 = UAL1 Rig|Sword_Idle，实测它是**出剑姿势**而非静止待机 ——
//   两腿张开 120°、双脚水平间距 0.60 m、左臂平举后甩（用户：「这个跨立太丑了吧」）。
//   而 UAL1 Rig|Idle_Loop（非战斗待机）站姿自然、用户已认可（「待机动作没问题」）。
// 手法：humanoid 片段曲线 = 一组具名「肌肉通道」，按通道名前缀分组即可只换某一条肢体。
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

var sb = new StringBuilder();
const string outDir = "Assets/_Project/Animations/Baked";
if (!AssetDatabase.IsValidFolder(outDir))
    AssetDatabase.CreateFolder("Assets/_Project/Animations", "Baked");

AnimationClip Clip(string p, string n)
{
    foreach (var o in AssetDatabase.LoadAllAssetsAtPath(p))
    {
        var c = o as AnimationClip;
        if (c != null && !c.name.StartsWith("__preview__") && (n == null || c.name == n)) return c;
    }
    return null;
}

string ual1 = "Assets/ThirdParty/Quaternius/UniversalAnimationLibrary/Unity/AnimationLibrary_Unity_Standard.fbx";
string ual2 = "Assets/ThirdParty/Quaternius/UniversalAnimationLibrary2/Unity/UAL2_Standard.fbx";

var baseIdle = Clip(ual1, "Rig|Idle_Loop");
var swIdle = Clip(ual1, "Rig|Sword_Idle");
var shIdle = Clip(ual2, "Armature|Idle_Shield_Loop");
var faIdle = Clip(ual2, "Armature|Idle_FoldArms_Loop");
sb.AppendLine("baseIdle=" + (baseIdle != null ? baseIdle.name + " " + baseIdle.length.ToString("F2") + "s" : "<缺>"));
sb.AppendLine("swIdle=" + (swIdle != null ? swIdle.name + " " + swIdle.length.ToString("F2") + "s" : "<缺>"));
sb.AppendLine("shIdle=" + (shIdle != null ? shIdle.name + " " + shIdle.length.ToString("F2") + "s" : "<缺>"));
sb.AppendLine("faIdle=" + (faIdle != null ? faIdle.name + " " + faIdle.length.ToString("F2") + "s" : "<缺>"));
sb.AppendLine();

// 通道分组（注意 "Right Arm" 与 "Right Upper Leg" 不冲突；别用 EndsWith 之类的模糊判据）
bool InGroup(string g, string p)
{
    if (g == "RArm")
        return p.StartsWith("Right Sh") || p.StartsWith("Right Arm")
            || p.StartsWith("Right Forearm") || p.StartsWith("Right Hand");
    if (g == "LArm")
        return p.StartsWith("Left Sh") || p.StartsWith("Left Arm")
            || p.StartsWith("Left Forearm") || p.StartsWith("Left Hand");
    if (g == "Legs")
        return p.StartsWith("Left Upper Leg") || p.StartsWith("Left Lower Leg")
            || p.StartsWith("Left Foot") || p.StartsWith("Left Toes")
            || p.StartsWith("Right Upper Leg") || p.StartsWith("Right Lower Leg")
            || p.StartsWith("Right Foot") || p.StartsWith("Right Toes");
    if (g == "Torso")
        return p.StartsWith("Spine") || p.StartsWith("Chest") || p.StartsWith("Neck") || p.StartsWith("Head");
    return false;
}

var allBinds = AnimationUtility.GetCurveBindings(baseIdle);
sb.AppendLine("baseIdle 通道数 = " + allBinds.Length);
foreach (var g in new[] { "RArm", "LArm", "Legs", "Torso" })
    sb.AppendLine("  " + g + " = " + allBinds.Count(b => InGroup(g, b.propertyName)));
sb.AppendLine();

// 变体：parts = 要整段替换成「参考片段某时刻常数」的通道组
var variants = new (string name, (string grp, AnimationClip refClip, float refT)[] parts)[]
{
    ("Idle_Carry_A",   new[] { ("RArm", swIdle, 0.35f) }),
    ("Idle_Carry_AT",  new[] { ("RArm", swIdle, 0.35f), ("Torso", swIdle, 0.35f) }),
    ("Idle_Carry_B",   new[] { ("RArm", swIdle, 0.05f) }),
    ("Idle_Carry_C",   new[] { ("RArm", swIdle, 0.70f) }),
    ("Idle_Carry_S",   new[] { ("RArm", shIdle, 0.50f) }),
    ("Idle_Carry_F",   new[] { ("RArm", faIdle, 0.50f) }),
};

foreach (var v in variants)
{
    var c = Object.Instantiate(baseIdle);
    c.name = v.name;
    int replaced = 0, kept = 0;

    foreach (var b in AnimationUtility.GetCurveBindings(c))
    {
        foreach (var p in v.parts)
        {
            if (!InGroup(p.grp, b.propertyName)) continue;
            if (p.refClip == null) break;

            var rc = AnimationUtility.GetEditorCurve(p.refClip, b);
            if (rc == null || rc.length == 0) { kept++; break; }   // 参考片段没这条通道 → 保留原曲线

            float val = rc.Evaluate(p.refT * p.refClip.length);
            if (float.IsNaN(val) || float.IsInfinity(val)) { kept++; break; }
            AnimationUtility.SetEditorCurve(c, b,
                new AnimationCurve(new Keyframe(0f, val), new Keyframe(c.length, val)));
            replaced++;
            break;
        }
    }

    var settings = AnimationUtility.GetAnimationClipSettings(c);
    settings.loopTime = true;
    AnimationUtility.SetAnimationClipSettings(c, settings);

    string path = outDir + "/" + v.name + ".anim";
    AssetDatabase.DeleteAsset(path);
    AssetDatabase.CreateAsset(c, path);
    sb.AppendLine("写出 " + path + "  len=" + c.length.ToString("F3") + " loop=" + c.isLooping
        + "  替换通道 " + replaced + " / 保留 " + kept);
}

AssetDatabase.SaveAssets();
AssetDatabase.Refresh();
File.WriteAllText("Tools/reports/q_bake_idle.txt", sb.ToString(), new UTF8Encoding(false));
return sb.ToString();
