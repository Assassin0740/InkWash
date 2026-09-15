// q_bake_run.cs —— 烘焙「持剑跑」变体：保留 KI Run01 的腿/躯干/左臂，改造右臂
//
// 为什么需要：实测现行 KI Run01_Forward 一个循环里剑尖走 7.611 m、剑轴在 20°~170° 之间扫
// （几乎把剑抡了一圈）。角色穿长袍、腿被袍子遮住，观众看到的主要是上身和剑 ⇒ 剑乱甩就是「廉价感」主因。
// 源头是右臂摆幅（肩 242° / 肘 305°）。腿的比例本身是对的（单步 1.25 m、步频 3.33/s，
// 换成 UAL1 的跑会变成单步 2.1~2.5 m 的大跨步，且参考速度 5.5~6.3 m/s 会让片段被拖成慢动作）。
//
// 手法：humanoid 片段的曲线就是一组具名「肌肉通道」（PropertyName = "Right Arm Down-Up" 等）。
// 按通道筛选就能只换右臂。
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

string ki = "Assets/ThirdParty/KevinIglesias/Animations/Male/Movement/Run/HumanM@Run01_Forward.fbx";
string ual1 = "Assets/ThirdParty/Quaternius/UniversalAnimationLibrary/Unity/AnimationLibrary_Unity_Standard.fbx";
var src = Clip(ki, null);
var refIdle = Clip(ual1, "Rig|Idle_Loop");
var refSwordIdle = Clip(ual1, "Rig|Sword_Idle");
sb.AppendLine("src=" + src.name + "  refIdle=" + refIdle.name + "  refSwordIdle=" + refSwordIdle.name);

// 右臂通道（注意别误伤 "Right Upper Leg"）
bool IsRightArm(string p) =>
    p.StartsWith("Right Sh") || p.StartsWith("Right Arm") || p.StartsWith("Right Forearm")
    || p.StartsWith("Right Hand") || p.StartsWith("RightHand");

var binds = AnimationUtility.GetCurveBindings(src);
int nRight = binds.Count(b => IsRightArm(b.propertyName));
sb.AppendLine("右臂通道数 = " + nRight + " / 总 " + binds.Length);
foreach (var b in binds.Where(b => IsRightArm(b.propertyName)).OrderBy(b => b.propertyName))
    sb.AppendLine("   " + b.propertyName);
sb.AppendLine();

// 变体表：useRef = 右臂整段取自哪个片段（null = 用 KI 自己的右臂）；keep = 原摆幅保留比例；三个手部肌肉偏移
var variants = new (string name, AnimationClip useRef, float keep, float dDu, float dIo, float dTw, float refT)[]
{
    ("Run01_Ctrl",        null,        1.00f, 0f,     0f,     0f,    0.25f),
    ("Run01_Still",       null,        0.00f, 0f,     0f,     0f,    0.25f),
    ("Run01_Still_Hd",    null,        0.00f, -0.30f, 0f,     0f,    0.25f),
    ("Run01_Carry",       refIdle,     0f,    0f,     0f,     0f,    0.50f),
    ("Run01_Carry_Io",    refIdle,     0f,    0f,     -0.30f, 0f,    0.50f),
    ("Run01_Carry_Du",    refIdle,     0f,    -0.30f, 0f,     0f,    0.50f),
    ("Run01_SwCarry",     refSwordIdle,0f,    0f,     0f,     0f,    0.35f),
};

foreach (var v in variants)
{
    var c = Object.Instantiate(src);
    c.name = v.name;

    foreach (var b in AnimationUtility.GetCurveBindings(c))
    {
        if (!IsRightArm(b.propertyName)) continue;
        var curve = AnimationUtility.GetEditorCurve(c, b);
        if (curve == null) continue;

        float delta = b.propertyName == "Right Hand Down-Up" ? v.dDu
                    : b.propertyName == "Right Hand In-Out" ? v.dIo
                    : b.propertyName == "Right Hand Twist In-Out" ? v.dTw : 0f;

        var rc = v.useRef != null ? AnimationUtility.GetEditorCurve(v.useRef, b) : null;
        if (rc != null && rc.length > 0)
        {
            // 整段换成参考片段的某个时刻（常数）
            float val = rc.Evaluate(v.refT * v.useRef.length) + delta;
            var nc = new AnimationCurve(new Keyframe(0f, val), new Keyframe(c.length, val));
            AnimationUtility.SetEditorCurve(c, b, nc);
        }
        else
        {
            // 平均 + 按 keep 压缩原摆幅（keep=0 即彻底静止），再叠手部偏移
            float mean = curve.keys.Average(k => k.value);
            var keys = curve.keys;
            for (int i = 0; i < keys.Length; i++)
                keys[i].value = mean + v.keep * (keys[i].value - mean) + delta;
            curve.keys = keys;
            AnimationUtility.SetEditorCurve(c, b, curve);
        }
    }

    // 循环
    var settings = AnimationUtility.GetAnimationClipSettings(c);
    settings.loopTime = true;
    AnimationUtility.SetAnimationClipSettings(c, settings);

    string p = outDir + "/" + v.name + ".anim";
    AssetDatabase.DeleteAsset(p);
    AssetDatabase.CreateAsset(c, p);
    sb.AppendLine("写出 " + p + "  len=" + c.length.ToString("F3") + " loop=" + c.isLooping);
}

AssetDatabase.SaveAssets();
AssetDatabase.Refresh();
File.WriteAllText("Tools/reports/q_bake_run.txt", sb.ToString(), new UTF8Encoding(false));
return sb.ToString();
