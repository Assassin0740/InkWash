// 探针：量 UAL2 关键动作片段的真实时长与循环属性，用于安排连击 / 后摇 / 冲刺的节奏。
var sb = new System.Text.StringBuilder();
string p = "Assets/ThirdParty/Quaternius/UniversalAnimationLibrary2/Unity/UAL2_Standard.fbx";

string[] want = new string[]
{
    "Armature|Idle_FoldArms_Loop",
    "Armature|Walk_Carry_Loop",
    "Armature|Zombie_Walk_Fwd_Loop",
    "Armature|Sword_Regular_A",
    "Armature|Sword_Regular_A_Rec",
    "Armature|Sword_Regular_B",
    "Armature|Sword_Regular_B_Rec",
    "Armature|Sword_Regular_C",
    "Armature|Sword_Regular_Combo",
    "Armature|Sword_Heavy_Combo",
    "Armature|Sword_Dash",
    "Armature|Sword_Block",
    "Armature|Shield_Dash",
    "Armature|Slide_Start",
    "Armature|Slide_Loop",
    "Armature|Slide_Exit",
    "Armature|Melee_Hook",
    "Armature|Melee_Hook_Rec",
    "Armature|Hit_Knockback",
    "Armature|NinjaJump_Start",
    "Armature|NinjaJump_Land",
    "Armature|NinjaJump_Idle_Loop",
    "Armature|Shield_OneShot",
    "Armature|OverhandThrow",
};

var all = UnityEditor.AssetDatabase.LoadAllAssetsAtPath(p);
var map = new System.Collections.Generic.Dictionary<string, UnityEngine.AnimationClip>();
foreach (var o in all)
{
    var c = o as UnityEngine.AnimationClip;
    if (c == null) continue;
    if (!map.ContainsKey(c.name)) map[c.name] = c;
}

sb.AppendLine("=== 关键片段时长（FBX 内 30fps 烘焙）===");
foreach (string w in want)
{
    UnityEngine.AnimationClip c;
    if (!map.TryGetValue(w, out c)) { sb.AppendLine("  [缺] " + w); continue; }

    // 关键帧数 + 首末帧姿态差异（判断是否首尾一致，即能否无缝循环）
    var bindings = UnityEditor.AnimationUtility.GetCurveBindings(c);
    float maxDelta = 0f;
    foreach (var b in bindings)
    {
        var curve = UnityEditor.AnimationUtility.GetEditorCurve(c, b);
        if (curve == null || curve.length < 2) continue;
        float d = Mathf.Abs(curve.keys[0].value - curve.keys[curve.length - 1].value);
        if (d > maxDelta) maxDelta = d;
    }
    sb.AppendLine(string.Format("  {0,-34} {1,5:F2}s  循环={2,-3} 曲线数={3,-4} 首末姿态差={4:F4}",
        c.name, c.length, c.isLooping ? "是" : "否", bindings.Length, maxDelta));
}

// 顺便看看 Avatar 的 humanoid 描述是否可用（决定 AvatarMask 能不能用）
sb.AppendLine();
sb.AppendLine("=== Avatar 检查（决定能否用 Humanoid AvatarMask）===");
var fbx = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(p);
if (fbx != null)
{
    var anim = fbx.GetComponentInChildren<UnityEngine.Animator>();
    var av = anim != null ? anim.avatar : null;
    if (av == null) sb.AppendLine("  FBX 内未直接找到 Avatar");
    else
    {
        sb.AppendLine("  Avatar: " + av.name + "  isHuman=" + av.isHuman + "  isValid=" + av.isValid);
        if (av.isHuman)
            for (int i = 0; i < (int)UnityEngine.HumanBodyBones.LastBone; i++)
            {
                var b = anim.GetBoneTransform((UnityEngine.HumanBodyBones)i);
                if (b != null) sb.AppendLine("     " + (UnityEngine.HumanBodyBones)i + " -> " + b.name);
            }
    }
}

return sb.ToString();
