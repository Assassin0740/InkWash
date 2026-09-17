// q_foot1.cs —— 编辑态取证：「跑步脚 90° 旋转」+「跑步高出来一格」根因定位
//
// 上下文：用户反馈 ①持剑待机两腿大八字（已定 Idle_Carry_A）②跑步脚像被拧了 90° ③跑步整体浮空。
// 已排除：FootIK 只写模型容器 localPosition、不碰骨骼旋转（grep 零命中）；
//         全项目骨骼旋转改写只有 WeaponHandPose(手骨) 与 SwordVfx(世界朝向)。
// 因此怀疑源片段本身：KI 的 Run01_Forward 在脚踝「Twist In-Out」等通道上带了巨大偏置。
//
// 本脚本不依赖 Play，直接读曲线，因此不可能被 Animator 姿势缓存骗（SampleAnimation 的坑）。
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

string kiRun = "Assets/ThirdParty/KevinIglesias/Animations/Male/Movement/Run/HumanM@Run01_Forward.fbx";
string kiWalk = "Assets/ThirdParty/KevinIglesias/Animations/Male/Movement/Walk/HumanM@Walk01_Forward.fbx";
string ual1 = "Assets/ThirdParty/Quaternius/UniversalAnimationLibrary/Unity/AnimationLibrary_Unity_Standard.fbx";
string ual2 = "Assets/ThirdParty/Quaternius/UniversalAnimationLibrary2/Unity/UAL2_Standard.fbx";
string fengFbx = "Assets/Char_Feng/Fbx/Feng.fbx";

// ---------- ① 各包的移动类片段清单 ----------
sb.AppendLine("=== ① 各包可用「移动」片段 ===");
foreach (var kv in new (string label, string path)[]
    { ("KI-Run", kiRun), ("KI-Walk", kiWalk), ("UAL1", ual1), ("UAL2", ual2) })
{
    var cs = Clips(kv.path);
    sb.AppendLine("  [" + kv.label + "] 共 " + cs.Count + " 个片段，移动类：");
    var mv = cs.Where(c =>
    {
        string n = c.name.ToLower();
        return n.Contains("run") || n.Contains("sprint") || n.Contains("jog") || n.Contains("walk")
            || n.Contains("strafe") || n.Contains("dash");
    }).OrderBy(c => c.name).ToList();
    if (mv.Count == 0) sb.AppendLine("      <无>");
    foreach (var c in mv) sb.AppendLine("      " + c.name + "  len=" + c.length.ToString("F3"));
}
sb.AppendLine();

// ---------- ② 脚/腿肌肉通道范围对比 ----------
string[] footTokens = { "Foot", "Toes", "Lower Leg", "Upper Leg" };
bool IsLegChan(string p)
{
    for (int i = 0; i < footTokens.Length; i++) if (p.Contains(footTokens[i])) return true;
    return false;
}

void DumpRanges(AnimationClip c, string label)
{
    if (c == null) { sb.AppendLine("  <缺> " + label); return; }
    var bs = AnimationUtility.GetCurveBindings(c).Where(b => IsLegChan(b.propertyName))
        .OrderBy(b => b.propertyName).ToList();
    sb.AppendLine("  ── " + label + "   脚/腿通道 " + bs.Count + " 条   len=" + c.length.ToString("F3"));
    foreach (var b in bs)
    {
        var cu = AnimationUtility.GetEditorCurve(c, b);
        if (cu == null || cu.keys.Length == 0) continue;
        float mn = cu.keys.Min(k => k.value), mx = cu.keys.Max(k => k.value);
        float mean = cu.keys.Average(k => k.value);
        sb.AppendLine(string.Format("      {0,-36} keys={1,3}  [{2,7:F3},{3,7:F3}]  mean{4,7:F3}  跨度{5,6:F3}",
            b.propertyName, cu.keys.Length, mn, mx, mean, mx - mn));
    }
}

sb.AppendLine("=== ② 脚/腿肌肉通道范围（humanoid 肌肉值）===");
var ual1Idle = Clips(ual1).FirstOrDefault(c => c.name == "Rig|Idle_Loop");
var ual1SwordIdle = Clips(ual1).FirstOrDefault(c => c.name == "Rig|Sword_Idle");
var kiRunClip = Clips(kiRun).FirstOrDefault(c => c.name.ToLower().Contains("run"));
var kiWalkClip = Clips(kiWalk).FirstOrDefault(c => c.name.ToLower().Contains("walk"));

DumpRanges(ual1Idle, "【基准】UAL1 Rig|Idle_Loop（自然站姿，用户认可）");
DumpRanges(ual1SwordIdle, "现用持剑待机 UAL1 Rig|Sword_Idle");
DumpRanges(kiRunClip, "现用跑步源 KI Run01_Forward");
DumpRanges(kiWalkClip, "KI Walk01_Forward（对照：同作者同骨架）");
DumpRanges(AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/_Project/Animations/Baked/Run01_Carry.anim"), "Baked/Run01_Carry（现用跑步片段）");
DumpRanges(AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/_Project/Animations/Baked/Run01_Ctrl.anim"), "Baked/Run01_Ctrl（跑步对照组）");
DumpRanges(AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/_Project/Animations/Baked/Idle_Carry_A.anim"), "Baked/Idle_Carry_A（本轮选用的持剑待机）");
sb.AppendLine();

// ---------- ③ Feng 与 KI 的 Humanoid 映射对比 ----------
void DumpHuman(Avatar av, string label)
{
    if (av == null) { sb.AppendLine("  <缺 avatar> " + label); return; }
    sb.AppendLine("  ── " + label + "   isHuman=" + av.isHuman);
    var hd = av.humanDescription;
    foreach (var hb in hd.human.OrderBy(h => h.humanName))
        if (hb.humanName.Contains("Foot") || hb.humanName.Contains("Toe") || hb.humanName.Contains("Leg")
            || hb.humanName.Contains("Hips"))
            sb.AppendLine(string.Format("      {0,-22} → bone \"{1}\"", hb.humanName, hb.boneName));
}

sb.AppendLine("=== ③ Humanoid 映射对比（脚/腿）===");
var fengAvatar = AssetDatabase.LoadAllAssetsAtPath(fengFbx).OfType<Avatar>().FirstOrDefault();
var kiAvatar = AssetDatabase.LoadAllAssetsAtPath(kiRun).OfType<Avatar>().FirstOrDefault();
var ual1Avatar = AssetDatabase.LoadAllAssetsAtPath(ual1).OfType<Avatar>().FirstOrDefault();
DumpHuman(fengAvatar, "Feng.fbx（主角，目标骨架）");
DumpHuman(kiAvatar, "KI HumanM@Run01_Forward.fbx（跑步源）");
DumpHuman(ual1Avatar, "UAL1（待机/走路源）");
sb.AppendLine();

// ---------- ④ T-pose 中脚骨的局部旋转（重定向的基准）----------
void DumpTpose(Avatar av, string label)
{
    if (av == null) return;
    var hd = av.humanDescription;
    sb.AppendLine("  ── " + label + " T-pose 骨架（脚/腿骨局部 pos/rot）");
    foreach (var s in hd.skeleton)
        if (s.name.ToLower().Contains("foot") || s.name.ToLower().Contains("toe")
            || s.name.ToLower().Contains("leg") || s.name.ToLower().Contains("ankle"))
            sb.AppendLine(string.Format("      {0,-28} pos({1,6:F3},{2,6:F3},{3,6:F3})  rot({4,6:F1},{5,6:F1},{6,6:F1},{7,6:F1})",
                s.name, s.position.x, s.position.y, s.position.z,
                s.rotation.x, s.rotation.y, s.rotation.z, s.rotation.w));
}

sb.AppendLine("=== ④ T-pose 中脚/腿骨定义 ===");
DumpTpose(fengAvatar, "Feng（目标）");
DumpTpose(kiAvatar, "KI（源）");
sb.AppendLine();

// ---------- ⑤ HumanTrait 肌肉上下限（判定「−4.0」是否越界）----------
sb.AppendLine("=== ⑤ HumanTrait 肌肉默认上下限（脚/腿）===");
for (int i = 0; i < HumanTrait.MuscleCount; i++)
{
    string n = HumanTrait.MuscleName[i];
    if (!IsLegChan(n)) continue;
    sb.AppendLine(string.Format("  {0,-36} min{1,7:F2}  max{2,7:F2}",
        n, HumanTrait.GetMuscleDefaultMin(i), HumanTrait.GetMuscleDefaultMax(i)));
}

System.IO.File.WriteAllText(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Application.dataPath), "Tools/reports/q_foot1.txt"),
    sb.ToString(), new UTF8Encoding(false));
return sb.ToString();
