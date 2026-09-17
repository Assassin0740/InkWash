// q_bake_run3.cs —— 换跑步底座：改用 UAL1 的跑步片段（脚部数据正常），右臂统一接持剑姿势
//
// 为什么换：KI 的 HumanM@Run01_Forward.fbx 右脚数据损坏 ——
//   Right Foot Twist In-Out = [-4.012, -0.876]（同片左脚 [0.016,0.279]，同作者的 Walk01 右脚 [−0.015,0.211]）。
//   活体实测：右脚相对待机站姿的总偏角 138.6°，左脚 78.8°（左右差 56°）；换 UAL1 Sprint 后为 67.0°/61.5°（差 5.5°）。
//   ⇒ 不是重定向配置、不是 Feng 骨架、不是烘焙脚本（只动右臂），是那份 FBX 的数据本身。
//
// 另修一处不一致：现用 Run01_Carry 的右臂取自 Rig|Idle_Loop（自然下垂），
//   而本轮选用的持剑待机 Idle_Carry_A 右臂取自 Rig|Sword_Idle（持剑姿势）⇒ 待机/跑步切换时手臂会跳。
//   本脚本统一用 Rig|Sword_Idle @ t=0.35，与 Idle_Carry_A 完全同源。
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

List<AnimationClip> Clips(string p)
{
    var l = new List<AnimationClip>();
    foreach (var o in AssetDatabase.LoadAllAssetsAtPath(p))
    {
        var c = o as AnimationClip;
        if (c != null && !c.name.StartsWith("__preview__")) l.Add(c);
    }
    return l;
}

string ual1 = "Assets/ThirdParty/Quaternius/UniversalAnimationLibrary/Unity/AnimationLibrary_Unity_Standard.fbx";
var ual1Clips = Clips(ual1);
AnimationClip G(string n) { return ual1Clips.FirstOrDefault(c => c.name == n); }

var refSwordIdle = G("Rig|Sword_Idle");
var baseSprint = G("Rig|Sprint_Loop");
var baseJog = G("Rig|Jog_Fwd_Loop");
sb.AppendLine("refSwordIdle=" + (refSwordIdle != null ? refSwordIdle.name + " " + refSwordIdle.length.ToString("F3") + "s" : "<缺>"));
sb.AppendLine("baseSprint  =" + (baseSprint != null ? baseSprint.name + " " + baseSprint.length.ToString("F3") + "s" : "<缺>"));
sb.AppendLine("baseJog     =" + (baseJog != null ? baseJog.name + " " + baseJog.length.ToString("F3") + "s" : "<缺>"));
sb.AppendLine();

// 右臂通道判据（与 q_bake_run.cs 一致；注意别误伤 "Right Upper Leg"）
bool IsRightArm(string p) =>
    p.StartsWith("Right Sh") || p.StartsWith("Right Arm") || p.StartsWith("Right Forearm")
    || p.StartsWith("Right Hand") || p.StartsWith("RightHand");

var refBindings = AnimationUtility.GetCurveBindings(refSwordIdle);
sb.AppendLine("参考片段 Rig|Sword_Idle 通道数 = " + refBindings.Length + "（右臂 " + refBindings.Count(b => IsRightArm(b.propertyName)) + " 条）");
sb.AppendLine();

var variants = new (string name, AnimationClip baseClip, float refT)[]
{
    ("Run02_Sprint_Carry", baseSprint, 0.35f),
    ("Run02_Jog_Carry",    baseJog,    0.35f),
};

foreach (var v in variants)
{
    if (v.baseClip == null) { sb.AppendLine(v.name + " ★ 底座缺失，跳过"); continue; }
    var c = Object.Instantiate(v.baseClip);
    c.name = v.name;
    int replaced = 0, kept = 0;

    foreach (var b in AnimationUtility.GetCurveBindings(c))
    {
        if (!IsRightArm(b.propertyName)) continue;
        var rc = AnimationUtility.GetEditorCurve(refSwordIdle, b);
        if (rc == null || rc.keys.Length == 0) { kept++; continue; }
        float val = rc.Evaluate(v.refT * refSwordIdle.length);
        if (float.IsNaN(val) || float.IsInfinity(val)) { kept++; continue; }
        AnimationUtility.SetEditorCurve(c, b,
            new AnimationCurve(new Keyframe(0f, val), new Keyframe(c.length, val)));
        replaced++;
    }

    var settings = AnimationUtility.GetAnimationClipSettings(c);
    settings.loopTime = true;
    AnimationUtility.SetAnimationClipSettings(c, settings);

    string path = outDir + "/" + v.name + ".anim";
    AssetDatabase.DeleteAsset(path);
    AssetDatabase.CreateAsset(c, path);
    sb.AppendLine("写出 " + path + "  len=" + c.length.ToString("F3") + " loop=" + c.isLooping
        + "  右臂替换 " + replaced + " 条 / 保留 " + kept + " 条");
}

// 自检：新片段右脚 Twist 是否恢复正常（对比 KI 的 [-4.012,-0.876]）
sb.AppendLine();
sb.AppendLine("=== 自检：右脚脚部通道范围（对比 KI Run01_Forward 的 Twist [-4.012,-0.876] mean -2.623）===");
foreach (var v in variants)
{
    var c = AssetDatabase.LoadAssetAtPath<AnimationClip>(outDir + "/" + v.name + ".anim");
    if (c == null) continue;
    foreach (var b in AnimationUtility.GetCurveBindings(c).Where(b => b.propertyName.StartsWith("Right Foot") || b.propertyName.StartsWith("Left Foot")))
    {
        var cu = AnimationUtility.GetEditorCurve(c, b);
        if (cu == null || cu.keys.Length == 0) continue;
        sb.AppendLine(string.Format("  {0,-24} {1,-30} [{2,7:F3},{3,7:F3}] mean{4,7:F3}",
            v.name, b.propertyName, cu.keys.Min(k => k.value), cu.keys.Max(k => k.value), cu.keys.Average(k => k.value)));
    }
}

AssetDatabase.SaveAssets();
AssetDatabase.Refresh();
File.WriteAllText("Tools/reports/q_bake_run3.txt", sb.ToString(), new UTF8Encoding(false));
return sb.ToString();
