// q_bake_check.cs —— 编辑态直接读曲线值，判定「按肌肉通道替换」到底有没有写进去
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

var sb = new StringBuilder();

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
var baseIdle = Clip(ual1, "Rig|Idle_Loop");
var swIdle = Clip(ual1, "Rig|Sword_Idle");
var baked = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/_Project/Animations/Baked/Idle_Carry_A.anim");
var bakedAT = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/_Project/Animations/Baked/Idle_Carry_AT.anim");

if (baseIdle == null || swIdle == null || baked == null) { return "★ 有引用缺失 base=" + (baseIdle != null) + " sw=" + (swIdle != null) + " baked=" + (baked != null); }

sb.AppendLine("base  = " + baseIdle.name + "  len=" + baseIdle.length.ToString("F3"));
sb.AppendLine("ref   = " + swIdle.name + "  len=" + swIdle.length.ToString("F3"));
sb.AppendLine("baked = " + baked.name + "  len=" + baked.length.ToString("F3"));
sb.AppendLine();

// 1) 两边通道名集合对比
var bBase = AnimationUtility.GetCurveBindings(baseIdle);
var bRef = AnimationUtility.GetCurveBindings(swIdle);
sb.AppendLine("base 通道数 = " + bBase.Length + "   ref 通道数 = " + bRef.Length);
var setBase = bBase.Select(b => b.propertyName + "|" + b.path + "|" + b.type.Name).ToHashSet();
var setRef = bRef.Select(b => b.propertyName + "|" + b.path + "|" + b.type.Name).ToHashSet();
sb.AppendLine("base 有而 ref 没有的通道：" + string.Join(", ", setBase.Except(setRef).Take(10)));
sb.AppendLine("ref 有而 base 没有的通道：" + string.Join(", ", setRef.Except(setBase).Take(10)));
sb.AppendLine();

// 2) 右臂 9 条通道逐个打印：base@t、ref@t、baked（常数）、用 ref 的 binding 去取
sb.AppendLine("=== 右臂通道值对比 ===");
sb.AppendLine("通道名                          base@1.0    ref@0.583   bakedA常数  用refBinding取值");
var refBinds = bRef.ToDictionary(b => b.propertyName, b => b);
foreach (var b in bBase.Where(x =>
        x.propertyName.StartsWith("Right Sh") || x.propertyName.StartsWith("Right Arm")
        || x.propertyName.StartsWith("Right Forearm") || x.propertyName.StartsWith("Right Hand"))
    .OrderBy(x => x.propertyName))
{
    var cb = AnimationUtility.GetEditorCurve(baseIdle, b);
    var cr = AnimationUtility.GetEditorCurve(swIdle, b);
    var cbk = AnimationUtility.GetEditorCurve(baked, b);
    float vb = cb != null ? cb.Evaluate(1.0f) : float.NaN;
    float vr = cr != null ? cr.Evaluate(0.583f) : float.NaN;
    float vk = cbk != null ? cbk.Evaluate(0.5f) : float.NaN;
    string alt = "-";
    if (refBinds.TryGetValue(b.propertyName, out var rb))
    {
        var calt = AnimationUtility.GetEditorCurve(swIdle, rb);
        alt = calt != null ? calt.Evaluate(0.583f).ToString("F4") + " (keys=" + calt.length + ")" : "null";
    }
    sb.AppendLine(string.Format("{0,-30} {1,9:F4}  {2,9:F4}  {3,10:F4}   {4}",
        b.propertyName, vb, vr, vk, alt));
}

sb.AppendLine();
sb.AppendLine("=== baked 片段与 base 的曲线差异（key 数 / 常数？）===");
foreach (var b in bBase.Where(x => x.propertyName.StartsWith("Right Arm Down-Up")))
{
    var cb = AnimationUtility.GetEditorCurve(baseIdle, b);
    var ck = AnimationUtility.GetEditorCurve(baked, b);
    sb.AppendLine(b.propertyName + "  base keys=" + (cb != null ? cb.keys.Length : -1) + "  baked keys=" + (ck != null ? ck.keys.Length : -1));
    if (ck != null) foreach (var k in ck.keys) sb.AppendLine("    baked key t=" + k.time.ToString("F3") + " v=" + k.value.ToString("F5"));
    if (cb != null) foreach (var k in cb.keys.Take(4)) sb.AppendLine("    base  key t=" + k.time.ToString("F3") + " v=" + k.value.ToString("F5"));
}

sb.AppendLine();
sb.AppendLine("=== Idle_Carry_AT 的 Torso 通道是否变成了常数 ===");
foreach (var b in bBase.Where(x => x.propertyName.StartsWith("Spine") || x.propertyName.StartsWith("Chest")).Take(4))
{
    var ck = AnimationUtility.GetEditorCurve(bakedAT != null ? bakedAT : baked, b);
    sb.AppendLine(b.propertyName + "  AT keys=" + (ck != null ? ck.keys.Length : -1)
        + (ck != null && ck.keys.Length == 2 ? "  v=" + ck.keys[0].value.ToString("F4") : ""));
}

File.WriteAllText("Tools/reports/q_bake_check.txt", sb.ToString(), new UTF8Encoding(false));
return sb.ToString();
