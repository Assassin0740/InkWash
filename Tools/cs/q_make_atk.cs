using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// 把 UAL2 的「斩击 A/B」+「收招 A_Rec/B_Rec」拼成三段完整攻击片段，落到 _Project/Animations/Attacks/
// 做法：复制一个已有的 Humanoid .anim 作模板（保留 m_AnimationType），清空曲线后写回合并曲线
string ual2 = "Assets/ThirdParty/Quaternius/UniversalAnimationLibrary2/Unity/UAL2_Standard.fbx";
string template = "Assets/Char_Feng/Animation/Idle.anim";
string outDir = "Assets/_Project/Animations/Attacks";

var sb = new System.Text.StringBuilder();

if (!AssetDatabase.IsValidFolder(outDir))
{
    if (!AssetDatabase.IsValidFolder("Assets/_Project/Animations"))
        AssetDatabase.CreateFolder("Assets/_Project", "Animations");
    AssetDatabase.CreateFolder("Assets/_Project/Animations", "Attacks");
}

AnimationClip Src(string name)
{
    foreach (var o in AssetDatabase.LoadAllAssetsAtPath(ual2))
        if (o is AnimationClip c && !c.name.StartsWith("__preview__") && c.name == name) return c;
    return null;
}

AnimationClip Merge(AnimationClip a, AnimationClip b, string fileName, string label)
{
    string dst = outDir + "/" + fileName + ".anim";
    if (AssetDatabase.LoadAssetAtPath<AnimationClip>(dst) != null) AssetDatabase.DeleteAsset(dst);
    if (!AssetDatabase.CopyAsset(template, dst)) { sb.AppendLine("!! 复制模板失败"); return null; }

    var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(dst);
    if (clip == null) { sb.AppendLine("!! 加载失败 " + dst); return null; }

    // 1. 清空模板自带的曲线
    foreach (var bd in AnimationUtility.GetCurveBindings(clip)) AnimationUtility.SetEditorCurve(clip, bd, null);
    foreach (var bd in AnimationUtility.GetObjectReferenceCurveBindings(clip)) AnimationUtility.SetObjectReferenceCurve(clip, bd, null);

    // 2. b 的所有曲线整体后移 a.length
    float off = a.length;
    var bindSet = new List<EditorCurveBinding>();
    void Collect(AnimationClip c)
    {
        foreach (var bd in AnimationUtility.GetCurveBindings(c))
        {
            bool has = false;
            foreach (var e in bindSet) if (e.path == bd.path && e.type == bd.type && e.propertyName == bd.propertyName) { has = true; break; }
            if (!has) bindSet.Add(bd);
        }
    }
    Collect(a); Collect(b);

    int wrote = 0;
    foreach (var bd in bindSet)
    {
        var ca = AnimationUtility.GetEditorCurve(a, bd);
        var cb = AnimationUtility.GetEditorCurve(b, bd);
        if (ca == null && cb == null) continue;

        var keys = new List<Keyframe>();
        if (ca != null) foreach (var k in ca.keys) keys.Add(k);
        if (cb != null) foreach (var k in cb.keys) keys.Add(new Keyframe(k.time + off, k.value, k.inTangent, k.outTangent));
        if (keys.Count == 0) continue;

        AnimationUtility.SetEditorCurve(clip, bd, new AnimationCurve(keys.ToArray()));
        wrote++;
    }

    clip.frameRate = 60f;
    EditorUtility.SetDirty(clip);
    AssetDatabase.SaveAssets();
    AssetDatabase.ImportAsset(dst, ImportAssetOptions.ForceUpdate);

    var chk = AssetDatabase.LoadAssetAtPath<AnimationClip>(dst);
    sb.AppendLine(label.PadRight(22) + " → " + dst);
    sb.AppendLine("   A=" + a.name + " (" + a.length.ToString("F2") + "s)  +  B=" + b.name + " (" + b.length.ToString("F2") + "s)");
    sb.AppendLine("   产物: 时长=" + chk.length.ToString("F3") + "s  曲线=" + wrote + "  帧率=" + chk.frameRate
                  + "  isHumanMotion=" + chk.isHumanMotion + "  legacy=" + chk.legacy);
    return chk;
}

var aA = Src("Armature|Sword_Regular_A");
var aR = Src("Armature|Sword_Regular_A_Rec");
var bA = Src("Armature|Sword_Regular_B");
var bR = Src("Armature|Sword_Regular_B_Rec");
var cC = Src("Armature|Sword_Regular_C");

sb.AppendLine("源片段：");
sb.AppendLine("  A=" + (aA != null ? aA.name + " " + aA.length.ToString("F2") + "s" : "缺")
    + "  A_Rec=" + (aR != null ? aR.name + " " + aR.length.ToString("F2") + "s" : "缺")
    + "  B=" + (bA != null ? bA.name + " " + bA.length.ToString("F2") + "s" : "缺")
    + "  B_Rec=" + (bR != null ? bR.name + " " + bR.length.ToString("F2") + "s" : "缺")
    + "  C=" + (cC != null ? cC.name + " " + cC.length.ToString("F2") + "s" : "缺"));
sb.AppendLine();

if (aA != null && aR != null) Merge(aA, aR, "Atk1_Slash", "Atk1 = A + A_Rec");
if (bA != null && bR != null) Merge(bA, bR, "Atk2_Rise", "Atk2 = B + B_Rec");
// C 本身 2.0s 已是完整动作，直接复制成资产方便控制器引用
if (cC != null)
{
    string dst = outDir + "/Atk3_Heavy.anim";
    if (AssetDatabase.LoadAssetAtPath<AnimationClip>(dst) != null) AssetDatabase.DeleteAsset(dst);
    if (AssetDatabase.CopyAsset(template, dst))
    {
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(dst);
        foreach (var bd in AnimationUtility.GetCurveBindings(clip)) AnimationUtility.SetEditorCurve(clip, bd, null);
        foreach (var bd in AnimationUtility.GetObjectReferenceCurveBindings(clip)) AnimationUtility.SetObjectReferenceCurve(clip, bd, null);
        int w = 0;
        foreach (var bd in AnimationUtility.GetCurveBindings(cC))
        {
            var cc = AnimationUtility.GetEditorCurve(cC, bd);
            if (cc == null) continue;
            AnimationUtility.SetEditorCurve(clip, bd, cc); w++;
        }
        clip.frameRate = 60f;
        EditorUtility.SetDirty(clip);
        AssetDatabase.SaveAssets();
        AssetDatabase.ImportAsset(dst, ImportAssetOptions.ForceUpdate);
        var chk = AssetDatabase.LoadAssetAtPath<AnimationClip>(dst);
        sb.AppendLine("Atk3 = C(原样)".PadRight(22) + " → " + dst);
        sb.AppendLine("   产物: 时长=" + chk.length.ToString("F3") + "s  曲线=" + w + "  isHumanMotion=" + chk.isHumanMotion);
    }
}

AssetDatabase.Refresh();
Debug.Log("[q_make_atk] 完成");
return sb.ToString();
