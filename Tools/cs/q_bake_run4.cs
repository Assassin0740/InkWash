// q_bake_run4.cs —— 修 KI Run01_Forward 的右脚数据（保留它的触地优点），并统一右臂为持剑姿势
//
// 根因链：
//   1) 曲线层面：Right Foot Twist In-Out = [-4.012, -0.876]，肌肉界限 ±30° ⇒ 实际 [-120°, -26°]（脚外翻 120°，超生理极限 4 倍）
//      Right Foot Up-Down = [-1.802, 2.122]，界限 ±50° ⇒ [-90°, +106°]（同样超限）
//      同片段左脚完全正常（Twist [0.016,0.279] ⇒ [0.5°,8.4°]），同作者 Walk01 右脚也正常（[-0.015,0.211]）
//   2) T-pose 层面：KI 的 B-foot.R rot w=0.1、B-toe.R w=0.0（接近 180° 的奇异四元数），
//      而 Feng 的 CC_Base_R_Foot w=0.8、ToeBase w=1.0 ⇒ 插值经过奇异点会「绕远路」，产生数值爆炸。
//      ⇒ 这是 avatar/源数据的固有特性，只能在曲线层面修。
//
// 修法：用**同片段左脚**（同一动作、同一时刻，左右腿应试对称）的统计特征重锚 + 压缩右脚：
//          newVal(t) = leftMean + (leftSpan/rightSpan) * (oldVal(t) - rightMean)
//       这样右脚的均值与摆幅都对到左脚量级，且保留波形形状（相位/形状不变）。
// 另：右臂统一接 Rig|Sword_Idle @0.35 —— 与持剑待机 Idle_Carry_A 同源，避免待机/跑步切换时手臂跳。
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
sb.AppendLine("refSwordIdle = " + refSwordIdle.name + "  len=" + refSwordIdle.length.ToString("F3"));
sb.AppendLine();

bool IsRightArm(string p) =>
    p.StartsWith("Right Sh") || p.StartsWith("Right Arm") || p.StartsWith("Right Forearm")
    || p.StartsWith("Right Hand") || p.StartsWith("RightHand");

// 右脚 → 左脚 的通道配对（一一对应，只改这 3 条；其余右腿通道实测与左脚一致，不动）
var pairs = new (string right, string left)[]
{
    ("Right Foot Twist In-Out",        "Left Foot Twist In-Out"),
    ("Right Foot Up-Down",             "Left Foot Up-Down"),
    ("Right Lower Leg Twist In-Out",   "Left Lower Leg Twist In-Out"),
};
// 肌肉界限（用于把 muscle 值换成真实角度，便于人读）
var limits = new Dictionary<string, float>
{
    { "Right Foot Twist In-Out", 30f }, { "Left Foot Twist In-Out", 30f },
    { "Right Foot Up-Down", 50f },      { "Left Foot Up-Down", 50f },
    { "Right Lower Leg Twist In-Out", 90f }, { "Left Lower Leg Twist In-Out", 90f },
};

var c = Object.Instantiate(src);
c.name = "Run03_KI_Carry";

// ---------- ① 先量左/右脚现状（未修改）----------
sb.AppendLine("=== ① 修改前：右脚 vs 左脚（同片段）===");
foreach (var pr in pairs)
{
    var br = AnimationUtility.GetCurveBindings(c).FirstOrDefault(b => b.propertyName == pr.right);
    var bl = AnimationUtility.GetCurveBindings(c).FirstOrDefault(b => b.propertyName == pr.left);
    var cr = br.propertyName != null ? AnimationUtility.GetEditorCurve(c, br) : null;
    var cl = bl.propertyName != null ? AnimationUtility.GetEditorCurve(c, bl) : null;
    if (cr == null || cl == null) { sb.AppendLine("  <缺通道> " + pr.right); continue; }
    float rmn = cr.keys.Min(k => k.value), rmx = cr.keys.Max(k => k.value), rmean = cr.keys.Average(k => k.value);
    float lmn = cl.keys.Min(k => k.value), lmx = cl.keys.Max(k => k.value), lmean = cl.keys.Average(k => k.value);
    float limp = limits.ContainsKey(pr.right) ? limits[pr.right] : 1f;
    float llim = limits.ContainsKey(pr.left) ? limits[pr.left] : 1f;
    sb.AppendLine(string.Format("  {0,-32} 右 [{1,7:F3},{2,7:F3}] span{3,6:F3} mean{4,7:F3} ⇒ 角度 [{5,7:F1}°,{6,7:F1}°]",
        pr.right.Replace("Right ", ""), rmn, rmx, rmx - rmn, rmean, rmn * limp, rmx * limp));
    sb.AppendLine(string.Format("  {0,-32} 左 [{1,7:F3},{2,7:F3}] span{3,6:F3} mean{4,7:F3} ⇒ 角度 [{5,7:F1}°,{6,7:F1}°]",
        pr.left.Replace("Left ", ""), lmn, lmx, lmx - lmn, lmean, lmn * llim, lmx * llim));
}

// ---------- ② 校准右脚 ----------
sb.AppendLine();
sb.AppendLine("=== ② 校准右脚（newVal = leftMean + (leftSpan/rightSpan)*(oldVal − rightMean)）===");
int fixedCount = 0;
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

    var keys = cr.keys;
    for (int i = 0; i < keys.Length; i++) keys[i].value = lmean + k * (keys[i].value - rmean);
    var nc = new AnimationCurve(keys);
    AnimationUtility.SetEditorCurve(c, br, nc);
    fixedCount++;
    sb.AppendLine(string.Format("  {0,-32} k={1:F4}  新 span={2:F3}（目标左脚 {3:F3}）",
        pr.right.Replace("Right ", ""), k, nc.keys.Max(x => x.value) - nc.keys.Min(x => x.value), lspan));
}

// ---------- ③ 右臂接持剑姿势（与 Idle_Carry_A 同源）----------
sb.AppendLine();
sb.AppendLine("=== ③ 右臂 ← Rig|Sword_Idle @ t=0.35 ===");
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

string path = outDir + "/Run03_KI_Carry.anim";
AssetDatabase.DeleteAsset(path);
AssetDatabase.CreateAsset(c, path);
sb.AppendLine("写出 " + path + "  len=" + c.length.ToString("F3") + " loop=" + c.isLooping);
sb.AppendLine();

// ---------- ④ 复核 ----------
sb.AppendLine("=== ④ 复核：修改后右脚 vs 左脚 ===");
var ck = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
foreach (var pr in pairs)
{
    foreach (var nm in new[] { pr.right, pr.left })
    {
        var b = AnimationUtility.GetCurveBindings(ck).FirstOrDefault(x => x.propertyName == nm);
        if (b.propertyName == null) continue;
        var cu = AnimationUtility.GetEditorCurve(ck, b);
        float mn = cu.keys.Min(x => x.value), mx = cu.keys.Max(x => x.value);
        float limp = limits.ContainsKey(nm) ? limits[nm] : 1f;
        sb.AppendLine(string.Format("  {0,-32} [{1,7:F3},{2,7:F3}] span{3,6:F3} ⇒ 角度 [{4,7:F1}°,{5,7:F1}°]",
            nm, mn, mx, mx - mn, mn * limp, mx * limp));
    }
}

AssetDatabase.SaveAssets();
AssetDatabase.Refresh();
File.WriteAllText("Tools/reports/q_bake_run4.txt", sb.ToString(), new UTF8Encoding(false));
return sb.ToString();
