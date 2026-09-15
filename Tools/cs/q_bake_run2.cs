// q_bake_run2.cs —— 压缩「跑步脚踝摆幅」：把鞋从「斜着戳」压成「平踩」
//
// 实测（Tools/reports/q_pose8.txt）：Run01_Carry 一个循环里
//   「全体网格最低点」与「只统计鞋底顶点」**完全相等**（差 0.0000）⇒ 最低点就是鞋，不是袍摆；
//   但鞋底高度 min −0.142 / 中位 −0.042 ⇒ 鞋底只有极小部分时间戳到最低点，
//   即鞋是**斜着**触地（踝关节前后摆过大），于是静态偏移怎么取都别扭：
//     取最深贴地 ⇒ 中位帧整只脚浮空 10cm 以上（用户：「高出来一格」）
//     取中位贴地 ⇒ 最深帧鞋戳进地 10cm（体检「脚不穿地」判死）
// 对策：humanoid 的脚/脚趾肌肉通道单独压摆幅（绕各自均值缩放），保留腿的动作，只把鞋压平。
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

var sb = new StringBuilder();
const string outDir = "Assets/_Project/Animations/Baked";

var src = AssetDatabase.LoadAssetAtPath<AnimationClip>(outDir + "/Run01_Carry.anim");
if (src == null) return "★ 找不到 " + outDir + "/Run01_Carry.anim";

bool IsFoot(string p) =>
    p.StartsWith("Left Foot") || p.StartsWith("Left Toes")
    || p.StartsWith("Right Foot") || p.StartsWith("Right Toes");

var binds = AnimationUtility.GetCurveBindings(src);
var footBinds = binds.Where(b => IsFoot(b.propertyName)).OrderBy(b => b.propertyName).ToList();
sb.AppendLine("src=" + src.name + "  总通道 " + binds.Length + "  脚/趾通道 " + footBinds.Count);
foreach (var b in footBinds)
{
    var c = AnimationUtility.GetEditorCurve(src, b);
    sb.AppendLine("   " + b.propertyName + "  keys=" + (c != null ? c.keys.Length : -1)
        + "  范围[" + (c != null ? c.keys.Min(k => k.value).ToString("F3") : "?") + ", "
        + (c != null ? c.keys.Max(k => k.value).ToString("F3") : "?") + "]");
}
sb.AppendLine();

var variants = new (string name, float keep)[]
{
    ("Run01_Carry_F5", 0.50f),
    ("Run01_Carry_F2", 0.20f),
};

foreach (var v in variants)
{
    var c = Object.Instantiate(src);
    c.name = v.name;
    int n = 0;
    foreach (var b in AnimationUtility.GetCurveBindings(c))
    {
        if (!IsFoot(b.propertyName)) continue;
        var cur = AnimationUtility.GetEditorCurve(c, b);
        if (cur == null || cur.length == 0) continue;
        float mean = cur.keys.Average(k => k.value);
        var keys = cur.keys;
        for (int i = 0; i < keys.Length; i++) keys[i].value = mean + v.keep * (keys[i].value - mean);
        cur.keys = keys;
        AnimationUtility.SetEditorCurve(c, b, cur);
        n++;
    }
    var st = AnimationUtility.GetAnimationClipSettings(c);
    st.loopTime = true;
    AnimationUtility.SetAnimationClipSettings(c, st);

    string path = outDir + "/" + v.name + ".anim";
    AssetDatabase.DeleteAsset(path);
    AssetDatabase.CreateAsset(c, path);
    sb.AppendLine("写出 " + path + "  keep=" + v.keep + "  压缩通道 " + n + "  loop=" + c.isLooping);
}

AssetDatabase.SaveAssets();
AssetDatabase.Refresh();
File.WriteAllText("Tools/reports/q_bake_run2.txt", sb.ToString(), new UTF8Encoding(false));
return sb.ToString();
