// q_bake_run5.cs —— 精修版：Twist 通道「统计对齐左脚」，Up-Down 通道「只压幅度、保留均值」
//
// 教训（来自 Run03_KI_Carry）：上一版把 Right Foot Up-Down 的均值也对齐到左脚（1.177→0.924，等于脚踝平均角变 −12.7°），
//   结果全循环最低点从 −0.142 恶化到 −0.241 —— 脚被整体压低，反而更穿地。
//   Up-Down 的左右均值差 0.253 可能只是**相位差**（跑步时左右腿差半周期），不该对齐。
//   而 Twist 的均值 −2.623（=−78.7°）本身就是「脚外翻」的错误，必须对齐。
//
// 因此：
//   Right Foot Twist In-Out       → 对齐左脚均值 + 压到左脚幅度
//   Right Lower Leg Twist In-Out  → 对齐左脚均值 + 压到左脚幅度
//   Right Foot Up-Down            → 只压到左脚幅度，均值保留（不改变脚的平均高度）
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

var sb = new StringBuilder();
const string outDir = "Assets/_Project/Animations/Baked";

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

string kiRun = "Assets/ThirdParty/KevinIglesias/Animations/Male/Movement/Run/HumanM@Run01_Forward.fbx";
string ual1 = "Assets/ThirdParty/Quaternius/UniversalAnimationLibrary/Unity/AnimationLibrary_Unity_Standard.fbx";
var src = Clips(kiRun).FirstOrDefault();
var refSwordIdle = Clips(ual1).FirstOrDefault(c => c.name == "Rig|Sword_Idle");
sb.AppendLine("src = " + src.name + "  len=" + src.length.ToString("F3"));
sb.AppendLine();

bool IsRightArm(string p) =>
    p.StartsWith("Right Sh") || p.StartsWith("Right Arm") || p.StartsWith("Right Forearm")
    || p.StartsWith("Right Hand") || p.StartsWith("RightHand");

// align = true → 均值也对齐左脚；false → 只压幅度，保留自身均值
var pairs = new (string right, string left, bool align, float limit)[]
{
    ("Right Foot Twist In-Out",      "Left Foot Twist In-Out",      true,  30f),
    ("Right Lower Leg Twist In-Out", "Left Lower Leg Twist In-Out", true,  90f),
    ("Right Foot Up-Down",           "Left Foot Up-Down",           false, 50f),
};

var c = Object.Instantiate(src);
c.name = "Run04_KI_Carry";

sb.AppendLine("=== 修改前（右 vs 左，角度 = muscle × limit）===");
foreach (var pr in pairs)
{
    var br = AnimationUtility.GetCurveBindings(c).FirstOrDefault(b => b.propertyName == pr.right);
    var bl = AnimationUtility.GetCurveBindings(c).FirstOrDefault(b => b.propertyName == pr.left);
    var cr = br.propertyName != null ? AnimationUtility.GetEditorCurve(c, br) : null;
    var cl = bl.propertyName != null ? AnimationUtility.GetEditorCurve(c, bl) : null;
    if (cr == null || cl == null) continue;
    sb.AppendLine(string.Format("  {0,-30} 右 [{1,7:F3},{2,7:F3}] ⇒ [{3,7:F1}°,{4,7:F1}°]   左 [{5,7:F3},{6,7:F3}] ⇒ [{7,7:F1}°,{8,7:F1}°]",
        pr.right.Replace("Right ", ""), cr.keys.Min(k => k.value), cr.keys.Max(k => k.value),
        cr.keys.Min(k => k.value) * pr.limit, cr.keys.Max(k => k.value) * pr.limit,
        cl.keys.Min(k => k.value), cl.keys.Max(k => k.value),
        cl.keys.Min(k => k.value) * pr.limit, cl.keys.Max(k => k.value) * pr.limit));
}

sb.AppendLine();
sb.AppendLine("=== 校准 ===");
int nFix = 0;
foreach (var pr in pairs)
{
    var br = AnimationUtility.GetCurveBindings(c).FirstOrDefault(b => b.propertyName == pr.right);
    var bl = AnimationUtility.GetCurveBindings(c).FirstOrDefault(b => b.propertyName == pr.left);
    if (br.propertyName == null || bl.propertyName == null) continue;
    var cr = AnimationUtility.GetEditorCurve(c, br);
    var cl = AnimationUtility.GetEditorCurve(c, bl);
    if (cr == null || cl == null || cr.keys.Length == 0 || cl.keys.Length == 0) continue;

    float rmean = cr.keys.Average(k => k.value);
    float rspan = cr.keys.Max(k => k.value) - cr.keys.Min(k => k.value);
    float lmean = cl.keys.Average(k => k.value);
    float lspan = cl.keys.Max(k => k.value) - cl.keys.Min(k => k.value);
    float k = rspan > 1e-6f ? lspan / rspan : 1f;
    float anchor = pr.align ? lmean : rmean;   // ★ 关键区别

    var keys = cr.keys;
    for (int i = 0; i < keys.Length; i++) keys[i].value = anchor + k * (keys[i].value - rmean);
    var nc = new AnimationCurve(keys);
    AnimationUtility.SetEditorCurve(c, br, nc);
    nFix++;
    sb.AppendLine(string.Format("  {0,-30} k={1:F4}  锚={2,8:F3}({3})  新 [{4,7:F3},{5,7:F3}] ⇒ [{6,7:F1}°,{7,7:F1}°]",
        pr.right.Replace("Right ", ""), k, anchor, pr.align ? "对齐左脚" : "保留自身",
        nc.keys.Min(x => x.value), nc.keys.Max(x => x.value),
        nc.keys.Min(x => x.value) * pr.limit, nc.keys.Max(x => x.value) * pr.limit));
}

sb.AppendLine();
sb.AppendLine("=== 右臂 ← Rig|Sword_Idle @ t=0.35 ===");
int armCount = 0;
foreach (var b in AnimationUtility.GetCurveBindings(c))
{
    if (!IsRightArm(b.propertyName)) continue;
    var rc = AnimationUtility.GetEditorCurve(refSwordIdle, b);
    if (rc == null || rc.keys.Length == 0) continue;
    float val = rc.Evaluate(0.35f * refSwordIdle.length);
    if (float.IsNaN(val) || float.IsInfinity(val)) continue;
    AnimationUtility.SetEditorCurve(c, b, new AnimationCurve(new Keyframe(0f, val), new Keyframe(c.length, val)));
    armCount++;
}
sb.AppendLine("  右臂替换 " + armCount + " 条");

var settings = AnimationUtility.GetAnimationClipSettings(c);
settings.loopTime = true;
AnimationUtility.SetAnimationClipSettings(c, settings);

string path = outDir + "/Run04_KI_Carry.anim";
AssetDatabase.DeleteAsset(path);
AssetDatabase.CreateAsset(c, path);
sb.AppendLine("写出 " + path + "  len=" + c.length.ToString("F3") + " loop=" + c.isLooping);

AssetDatabase.SaveAssets();
AssetDatabase.Refresh();
File.WriteAllText("Tools/reports/q_bake_run5.txt", sb.ToString(), new UTF8Encoding(false));
return sb.ToString();
