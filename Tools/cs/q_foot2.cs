// q_foot2.cs —— 编辑态：① 搞清 RightFootQ/LeftFootT 之类通道的真实身份 ② 评估 UAL1 自带跑步片段
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

var sb = new StringBuilder();

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

// ---------- ① RightFootQ 这类通道的真实身份 ----------
sb.AppendLine("=== ① 非肌肉类通道的 binding 三元组（path / type / propertyName）===");
string kiRun = "Assets/ThirdParty/KevinIglesias/Animations/Male/Movement/Run/HumanM@Run01_Forward.fbx";
var runClip = Clips(kiRun).FirstOrDefault();
var seen = new HashSet<string>();
int shown = 0;
foreach (var b in AnimationUtility.GetCurveBindings(runClip))
{
    string key = b.propertyName;
    bool odd = key.Contains("Q.") || key.Contains("T.") || key.Contains("m_") || key == "";
    bool muscleLike = key.StartsWith("Left ") || key.StartsWith("Right ") || key.StartsWith("Spine")
        || key.StartsWith("Chest") || key.StartsWith("Neck") || key.StartsWith("Head") || key.StartsWith("Root");
    if (muscleLike || !odd) continue;
    if (!seen.Add(key)) continue;
    if (shown++ > 24) break;
    sb.AppendLine(string.Format("   path=\"{0}\"  type={1}  prop=\"{2}\"",
        b.path, b.type != null ? b.type.Name : "<?>", b.propertyName));
}
sb.AppendLine("   （共 " + AnimationUtility.GetCurveBindings(runClip).Length + " 条曲线）");
sb.AppendLine();

// ---------- ② UAL1 自带跑步片段 vs KI ----------
string ual1 = "Assets/ThirdParty/Quaternius/UniversalAnimationLibrary/Unity/AnimationLibrary_Unity_Standard.fbx";
string ual2 = "Assets/ThirdParty/Quaternius/UniversalAnimationLibrary2/Unity/UAL2_Standard.fbx";

string[] legTok = { "Foot", "Toes", "Lower Leg", "Upper Leg" };
bool IsLeg(string p) { for (int i = 0; i < legTok.Length; i++) if (p.Contains(legTok[i])) return true; return false; }

void DumpFeet(AnimationClip c, string label)
{
    if (c == null) { sb.AppendLine("  <缺> " + label); return; }
    sb.AppendLine("  ── " + label + "   len=" + c.length.ToString("F3"));
    foreach (var b in AnimationUtility.GetCurveBindings(c).Where(b => IsLeg(b.propertyName))
        .Where(b => b.propertyName.Contains("Foot") || b.propertyName.Contains("Toes"))
        .OrderBy(b => b.propertyName))
    {
        var cu = AnimationUtility.GetEditorCurve(c, b);
        if (cu == null || cu.keys.Length == 0) continue;
        float mn = cu.keys.Min(k => k.value), mx = cu.keys.Max(k => k.value);
        sb.AppendLine(string.Format("      {0,-34} keys={1,3}  [{2,7:F3},{3,7:F3}]  mean{4,7:F3}  跨度{5,6:F3}",
            b.propertyName, cu.keys.Length, mn, mx, cu.keys.Average(k => k.value), mx - mn));
    }
}

sb.AppendLine("=== ② 脚部通道：UAL1/UAL2 候选跑步 vs KI（只看脚/脚趾）===");
var ual1Clips = Clips(ual1);
DumpFeet(ual1Clips.FirstOrDefault(c => c.name == "Rig|Jog_Fwd_Loop"), "UAL1 Rig|Jog_Fwd_Loop");
DumpFeet(ual1Clips.FirstOrDefault(c => c.name == "Rig|Sprint_Loop"), "UAL1 Rig|Sprint_Loop");
DumpFeet(ual1Clips.FirstOrDefault(c => c.name == "Rig|Walk_Loop"), "UAL1 Rig|Walk_Loop（已知正常，对照）");
DumpFeet(ual1Clips.FirstOrDefault(c => c.name == "Rig|Idle_Loop"), "UAL1 Rig|Idle_Loop（已知正常，对照）");
var ual2Clips = Clips(ual2);
DumpFeet(ual2Clips.FirstOrDefault(c => c.name == "Armature|Sword_Dash"), "UAL2 Armature|Sword_Dash");
DumpFeet(ual2Clips.FirstOrDefault(c => c.name == "Armature|Walk_Carry_Loop"), "UAL2 Armature|Walk_Carry_Loop");
DumpFeet(AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/_Project/Animations/Baked/Run01_Carry.anim"), "现用 Baked/Run01_Carry");
sb.AppendLine();

// ---------- ③ 步幅/步频估算（用 Hips 位移随时间的极值跨度近似）----------
sb.AppendLine("=== ③ 片段整体活动量（用于估步频；只看 Upper Leg Front-Back 的过零次数）===");
void Cadence(AnimationClip c, string label)
{
    if (c == null) return;
    var b = AnimationUtility.GetCurveBindings(c).FirstOrDefault(x => x.propertyName == "Left Upper Leg Front-Back");
    if (b.propertyName == null) { sb.AppendLine("  " + label + " <无该通道>"); return; }
    var cu = AnimationUtility.GetEditorCurve(c, b);
    float mn = cu.keys.Min(k => k.value), mx = cu.keys.Max(k => k.value);
    float mid = (mn + mx) * 0.5f;
    int cross = 0;
    for (int i = 1; i < cu.keys.Length; i++)
        if ((cu.keys[i - 1].value - mid) * (cu.keys[i].value - mid) < 0f) cross++;
    sb.AppendLine(string.Format("  {0,-32} len={1:F3}s  左大腿摆动[{2:F2},{3:F2}]  过中值 {4} 次  周期≈{5:F3}s",
        label, c.length, mn, mx, cross, cross > 0 ? c.length / (cross * 0.5f) : 0f));
}
Cadence(runClip, "KI Run01_Forward");
Cadence(ual1Clips.FirstOrDefault(c => c.name == "Rig|Jog_Fwd_Loop"), "UAL1 Rig|Jog_Fwd_Loop");
Cadence(ual1Clips.FirstOrDefault(c => c.name == "Rig|Sprint_Loop"), "UAL1 Rig|Sprint_Loop");
Cadence(ual1Clips.FirstOrDefault(c => c.name == "Rig|Walk_Loop"), "UAL1 Rig|Walk_Loop");
Cadence(ual1Clips.FirstOrDefault(c => c.name == "Rig|Idle_Loop"), "UAL1 Rig|Idle_Loop");

System.IO.File.WriteAllText(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Application.dataPath), "Tools/reports/q_foot2.txt"),
    sb.ToString(), new UTF8Encoding(false));
return sb.ToString();
