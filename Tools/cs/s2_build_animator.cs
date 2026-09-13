// 重建 Player.controller：双图层 + 剑系攻击状态 + 步伐同步。
//
// 设计要点（对应问题 1/2/5）：
//  - 问题1「抱空气」：UAL2 没有普通走路循环，只能用 Walk_Carry_Loop（抱物走路的腿）。
//    解法是加一层 UpperBody 用 AvatarMask 把上半身覆盖为 Idle_FoldArms_Loop —— 腿走路的、上半身是站姿。
//  - 问题5「脚跟不上」：给 Move 状态启用 speedParameter = MotionSpeed，
//    播放速度由代码按「实际速度 / 参考速度」推算，使步频与位移匹配。
//  - 问题2「后摇长」：攻击拆成 挥砍(短) + 后摇(Rec) 两段，
//    后摇段从 30% 处就开放取消窗口（可接下一段连击 / 冲刺 / 移动）。
//
// ⚠ 本脚本会删除并重建 Player.controller，运行前请确认没有手工改动需要保留。
var sb = new System.Text.StringBuilder();

const string CtrlPath = "Assets/_Project/Animations/Player.controller";
const string Fbx = "Assets/ThirdParty/Quaternius/UniversalAnimationLibrary2/Unity/UAL2_Standard.fbx";
const string MaskPath = "Assets/_Project/Animations/Mask_UpperBody.mask";

// ------------------------------------------------------------------
// 0. 取片段
// ------------------------------------------------------------------
var clips = new System.Collections.Generic.Dictionary<string, UnityEngine.AnimationClip>();
foreach (var o in UnityEditor.AssetDatabase.LoadAllAssetsAtPath(Fbx))
{
    var c = o as UnityEngine.AnimationClip;
    if (c == null || c.name.StartsWith("__preview__")) continue;
    if (!clips.ContainsKey(c.name)) clips[c.name] = c;
}

System.Func<string, UnityEngine.AnimationClip> C = name =>
{
    UnityEngine.AnimationClip c;
    if (clips.TryGetValue(name, out c)) return c;
    sb.AppendLine("  [!] 缺片段: " + name);
    return null;
};

var clipIdle = C("Armature|Idle_FoldArms_Loop");
var clipWalk = C("Armature|Walk_Carry_Loop");
var clipDash = C("Armature|Slide_Start");
var clipAtkA = C("Armature|Sword_Regular_A");
var clipAtkARec = C("Armature|Sword_Regular_A_Rec");
var clipAtkB = C("Armature|Sword_Regular_B");
var clipAtkBRec = C("Armature|Sword_Regular_B_Rec");
var clipAtkC = C("Armature|Sword_Regular_C");

if (clipIdle == null || clipWalk == null || clipAtkA == null)
{
    sb.AppendLine("关键片段缺失，中止");
    return sb.ToString();
}
sb.AppendLine("片段就位：Idle/Walk/Dash(" + (clipDash != null ? clipDash.name : "无") + ")/AtkA/AtkARec/AtkB/AtkBRec/AtkC");

// ------------------------------------------------------------------
// 1. 上半身遮罩：开启 躯干/头/双臂/手指，关闭 双腿/双脚/根
// ------------------------------------------------------------------
var mask = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.AvatarMask>(MaskPath);
if (mask == null)
{
    mask = new UnityEngine.AvatarMask();
    UnityEditor.AssetDatabase.CreateAsset(mask, MaskPath);
}
for (int i = 0; i < (int)UnityEngine.AvatarMaskBodyPart.LastBodyPart; i++)
    mask.SetHumanoidBodyPartActive((UnityEngine.AvatarMaskBodyPart)i, false);

// 需要上半身来盖住「抱物」姿态的部件
mask.SetHumanoidBodyPartActive(UnityEngine.AvatarMaskBodyPart.Body, true);        // 髋/脊柱/胸/颈/肩
mask.SetHumanoidBodyPartActive(UnityEngine.AvatarMaskBodyPart.Head, true);
mask.SetHumanoidBodyPartActive(UnityEngine.AvatarMaskBodyPart.LeftArm, true);
mask.SetHumanoidBodyPartActive(UnityEngine.AvatarMaskBodyPart.RightArm, true);
mask.SetHumanoidBodyPartActive(UnityEngine.AvatarMaskBodyPart.LeftFingers, true);
mask.SetHumanoidBodyPartActive(UnityEngine.AvatarMaskBodyPart.RightFingers, true);
// 关闭：Root / LeftLeg / RightLeg / LeftFootIK / RightFootIK —— 腿留给下层走路用
UnityEditor.EditorUtility.SetDirty(mask);
sb.AppendLine("上半身遮罩: " + MaskPath + "（开 Body/Head/双臂/手指，关 双腿/双脚/Root）");

// ------------------------------------------------------------------
// 2. 建控制器（原地重建，保住 GUID —— 删了重建会让 Player 预制体的引用断掉）
// ------------------------------------------------------------------
var ac = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(CtrlPath);
if (ac == null)
{
    ac = UnityEditor.Animations.AnimatorController.CreateAnimatorControllerAtPath(CtrlPath);
    sb.AppendLine("新建 Player.controller");
}
else
{
    string oldGuid = UnityEditor.AssetDatabase.AssetPathToGUID(CtrlPath);

    // 清空转移与状态（顺序：先转移后状态，否则 RemoveState 可能失败）
    for (int li = 0; li < ac.layers.Length; li++)
    {
        var smOld = ac.layers[li].stateMachine;
        foreach (var cs in smOld.states)
            foreach (var t in cs.state.transitions)
                cs.state.RemoveTransition(t);
        foreach (var t in smOld.anyStateTransitions)
            smOld.RemoveAnyStateTransition(t);
        foreach (var t in smOld.entryTransitions)
            smOld.RemoveEntryTransition(t);
        foreach (var cs in smOld.states)
            smOld.RemoveState(cs.state);
    }
    // 删掉第 1 层及以后
    for (int i = ac.layers.Length - 1; i >= 1; i--) ac.RemoveLayer(i);
    // 清空参数
    foreach (var p in ac.parameters) ac.RemoveParameter(p);

    sb.AppendLine("原地重建 Player.controller，GUID 保持 " + oldGuid);
}

// ---- 参数 ----
ac.AddParameter("Speed", UnityEngine.AnimatorControllerParameterType.Float);
ac.AddParameter("MotionSpeed", UnityEngine.AnimatorControllerParameterType.Float);
ac.AddParameter("Grounded", UnityEngine.AnimatorControllerParameterType.Bool);
ac.AddParameter("Dash", UnityEngine.AnimatorControllerParameterType.Trigger);
ac.AddParameter("Attack1", UnityEngine.AnimatorControllerParameterType.Trigger);
ac.AddParameter("Attack2", UnityEngine.AnimatorControllerParameterType.Trigger);
ac.AddParameter("Attack3", UnityEngine.AnimatorControllerParameterType.Trigger);

// ---- 第 0 层：全身动作 ----
var sm = ac.layers[0].stateMachine;

System.Func<string, UnityEngine.AnimationClip, float, UnityEditor.Animations.AnimatorState> add =
    (name, clip, speed) =>
{
    var st = sm.AddState(name);
    st.motion = clip;
    st.speed = speed;
    st.writeDefaultValues = true;
    return st;
};

var stIdle = add("Idle", clipIdle, 1f);
var stMove = add("Move", clipWalk, 1f);
// 步伐同步：播放速度 = MotionSpeed（由 PlayerController 按实际速度推算）
stMove.speedParameterActive = true;
stMove.speedParameter = "MotionSpeed";
var stDash = add("Dash", clipDash != null ? clipDash : clipIdle, 2.6f);
var stA1 = add("Atk1", clipAtkA, 1f);
var stA1R = add("Atk1Rec", clipAtkARec != null ? clipAtkARec : clipAtkA, 2.8f);
var stA2 = add("Atk2", clipAtkB != null ? clipAtkB : clipAtkA, 1f);
var stA2R = add("Atk2Rec", clipAtkBRec != null ? clipAtkBRec : clipAtkA, 2.8f);
var stA3 = add("Atk3", clipAtkC != null ? clipAtkC : clipAtkA, 1.4f);

sm.defaultState = stIdle;

// ---- 连线 ----
System.Func<UnityEditor.Animations.AnimatorState, UnityEditor.Animations.AnimatorState,
    bool, float, float, UnityEditor.Animations.AnimatorStateTransition> link =
    (from, to, exitTime, exitAt, dur) =>
{
    var t = from.AddTransition(to);
    t.hasExitTime = exitTime;
    t.exitTime = exitAt;
    t.duration = dur;
    t.hasFixedDuration = true;
    return t;
};

System.Action<UnityEditor.Animations.AnimatorStateTransition, string, UnityEditor.Animations.AnimatorConditionMode, float> cond =
    (t, p, mode, v) => { t.AddCondition(mode, v, p); };

// 待机 <-> 移动
var t1 = link(stIdle, stMove, false, 0f, 0.15f); cond(t1, "Speed", UnityEditor.Animations.AnimatorConditionMode.Greater, 0.15f);
var t2 = link(stMove, stIdle, false, 0f, 0.20f); cond(t2, "Speed", UnityEditor.Animations.AnimatorConditionMode.Less, 0.15f);

// 冲刺
var t3 = link(stIdle, stDash, false, 0f, 0.08f); cond(t3, "Dash", UnityEditor.Animations.AnimatorConditionMode.If, 0f);
var t4 = link(stMove, stDash, false, 0f, 0.08f); cond(t4, "Dash", UnityEditor.Animations.AnimatorConditionMode.If, 0f);
var t5 = link(stDash, stIdle, true, 0.90f, 0.16f);

// 起手攻击（从待机 / 移动 / 冲刺 都能接）
var t6 = link(stIdle, stA1, false, 0f, 0.06f); cond(t6, "Attack1", UnityEditor.Animations.AnimatorConditionMode.If, 0f);
var t7 = link(stMove, stA1, false, 0f, 0.06f); cond(t7, "Attack1", UnityEditor.Animations.AnimatorConditionMode.If, 0f);
var t8 = link(stDash, stA1, true, 0.55f, 0.08f); cond(t8, "Attack1", UnityEditor.Animations.AnimatorConditionMode.If, 0f);

// 第 1 段 -> 后摇
var t9 = link(stA1, stA1R, true, 0.85f, 0.08f);
// 后摇：开放取消窗口（30% 起可接下一段 / 冲刺；55% 起可接移动；85% 自然回待机）
var t10 = link(stA1R, stA2, true, 0.30f, 0.06f); cond(t10, "Attack2", UnityEditor.Animations.AnimatorConditionMode.If, 0f);
var t11 = link(stA1R, stIdle, true, 0.85f, 0.16f);
var t12 = link(stA1R, stMove, true, 0.55f, 0.16f); cond(t12, "Speed", UnityEditor.Animations.AnimatorConditionMode.Greater, 0.15f);
var t13 = link(stA1R, stDash, true, 0.30f, 0.08f); cond(t13, "Dash", UnityEditor.Animations.AnimatorConditionMode.If, 0f);

// 第 2 段
var t14 = link(stA2, stA2R, true, 0.85f, 0.08f);
var t15 = link(stA2R, stA3, true, 0.30f, 0.06f); cond(t15, "Attack3", UnityEditor.Animations.AnimatorConditionMode.If, 0f);
var t16 = link(stA2R, stIdle, true, 0.85f, 0.16f);
var t17 = link(stA2R, stMove, true, 0.55f, 0.16f); cond(t17, "Speed", UnityEditor.Animations.AnimatorConditionMode.Greater, 0.15f);
var t18 = link(stA2R, stDash, true, 0.30f, 0.08f); cond(t18, "Dash", UnityEditor.Animations.AnimatorConditionMode.If, 0f);

// 第 3 段（收招）：45% 起可闪避取消，70% 起可接移动，否则回待机
var t19 = link(stA3, stIdle, true, 0.72f, 0.20f);
var t20 = link(stA3, stMove, true, 0.70f, 0.20f); cond(t20, "Speed", UnityEditor.Animations.AnimatorConditionMode.Greater, 0.15f);
var t21 = link(stA3, stDash, true, 0.45f, 0.10f); cond(t21, "Dash", UnityEditor.Animations.AnimatorConditionMode.If, 0f);

// ---- 第 1 层：上半身覆盖（消除「抱空气」）----
// 注意：AnimatorController.layers 是「结构体数组的副本」，改完必须写回，否则不生效。
ac.AddLayer("UpperBody");
var layersBuf = ac.layers;
int upperIdx = layersBuf.Length - 1;

var upper = layersBuf[upperIdx];
upper.name = "UpperBody";
upper.avatarMask = mask;
upper.defaultWeight = 1f;
upper.iKPass = false;
// blendingMode 不显式设置：新增层默认即 Override，避免枚举命名空间踩坑；稍后复核打印确认。
layersBuf[upperIdx] = upper;

// 基础层权重务必为 1（旧控制器被误设为 0）
var baseLayer = layersBuf[0];
baseLayer.avatarMask = null;
baseLayer.defaultWeight = 1f;
layersBuf[0] = baseLayer;

ac.layers = layersBuf;   // 写回

var smU = ac.layers[upperIdx].stateMachine;
var stUpper = smU.AddState("UpperIdle");
stUpper.motion = clipIdle;
stUpper.speed = 1f;
smU.defaultState = stUpper;
sb.AppendLine("已建第 1 层 UpperBody（遮罩覆盖上半身）");

UnityEditor.EditorUtility.SetDirty(ac);
UnityEditor.AssetDatabase.SaveAssets();

// ------------------------------------------------------------------
// 3. 复核
// ------------------------------------------------------------------
sb.AppendLine();
sb.AppendLine("=== 复核 Player.controller ===");
sb.AppendLine("控制层数: " + ac.layers.Length);
foreach (var L in ac.layers)
    sb.AppendLine("  " + L.name + "  权重=" + L.defaultWeight + "  遮罩=" + (L.avatarMask == null ? "(无)" : L.avatarMask.name)
        + "  模式=" + L.blendingMode);
sb.AppendLine("参数: " + string.Join(", ", System.Array.ConvertAll(ac.parameters, p => p.name + ":" + p.type)));
sb.AppendLine("状态与时长（秒）:");
foreach (var s in ac.layers[0].stateMachine.states)
{
    float len = 0f;
    if (s.state.motion is UnityEngine.AnimationClip c) len = c.length / Mathf.Max(s.state.speed, 0.001f);
    sb.AppendLine(string.Format("  {0,-9} 片段={1,-32} 速度倍率={2,-5} 实际时长≈{3:F2}s",
        s.state.name,
        (s.state.motion == null ? "(无)" : s.state.motion.name),
        s.state.speedParameterActive ? ("参数:" + s.state.speedParameter) : s.state.speed.ToString("F2"),
        s.state.speedParameterActive ? 0f : len));
}
sb.AppendLine("连线数: " + ac.layers[0].stateMachine.states.Length + " 状态 / 见下方明细");
foreach (var s in ac.layers[0].stateMachine.states)
    foreach (var t in s.state.transitions)
    {
        var cs = new System.Collections.Generic.List<string>();
        foreach (var c in t.conditions) cs.Add(c.parameter + " " + c.mode + " " + c.threshold);
        sb.AppendLine("   " + s.state.name + " -> " + t.destinationState.name
            + "  exitTime=" + (t.hasExitTime ? t.exitTime.ToString("F2") : "无")
            + "  混合=" + t.duration + "  [" + string.Join(";", cs.ToArray()) + "]");
    }

return sb.ToString();
