// 重建 Player.controller —— 换用 KayKit（CC0）片段。
//
// 与此前 s2_build_animator.cs 的区别：
//   片段全部来自 Assets/ThirdParty/KayKit/，模型与动画是**同一套 Rig_Medium 骨架**，
//   Humanoid 重定向是恒等变换，不存在"给另一副骨架做的动作"那种偏差。
//
// 状态机骨架（状态名 / 参数 / 连线结构）**刻意保持与旧版一致** ——
//   PlayerController.cs 里用 st.IsName("Atk1Rec") 之类硬编码读状态，
//   FootIK 的 stateOffsets 也按状态名索引。换名字要动 C#，没必要。
//
// 唯一的结构性补充：KayKit 的单手攻击是一个**自包含片段**（起手+挥砍+收招都在里面），
//   不像 UAL2 那样 A / A_Rec 成对。所以从攻击片段尾部切出 "_Rec" 子片段当后摇，
//   既保留原有的"后摇可取消"节奏，又不会把整段挥砍重播一遍。
using UnityEditor;
using UnityEngine;

var sb = new System.Text.StringBuilder();

const string CtrlPath = "Assets/_Project/Animations/Player.controller";
const string FbxMelee = "Assets/ThirdParty/KayKit/Animations/Rig_Medium/Rig_Medium_CombatMelee.fbx";
const string FbxMove = "Assets/ThirdParty/KayKit/Animations/Rig_Medium/Rig_Medium_MovementBasic.fbx";
const string FbxAdv = "Assets/ThirdParty/KayKit/Animations/Rig_Medium/Rig_Medium_MovementAdvanced.fbx";
const string FbxGen = "Assets/ThirdParty/KayKit/Animations/Rig_Medium/Rig_Medium_General.fbx";
const string FbxChar = "Assets/ThirdParty/KayKit/Adventurers/Characters/Rogue_Hooded.fbx";

// 冲刺时长必须与 PlayerController.dashDuration 一致（验收会核对）
const float DashDuration = 0.40f;
const float MoveEnterSpeed = 0.35f;
const float RunEnterSpeed = 3.0f;
const float RunExitSpeed = 2.5f;

System.Func<string, string, UnityEngine.AnimationClip> pick =
    (fbx, name) =>
{
    var objs = UnityEditor.AssetDatabase.LoadAllAssetsAtPath(fbx);
    foreach (var o in objs)
    {
        var c = o as UnityEngine.AnimationClip;
        if (c != null && c.name == name) return c;
    }
    return null;
};

// ------------------------------------------------------------------
// 1. 循环片段开 loopTime
//    KayKit 的 FBX 里所有片段 loopTime 都是关的（实测），
//    不打开的话走路/跑步/待机播一遍就停在最后一帧。
// ------------------------------------------------------------------
System.Action<string, string[]> setLoop =
    (fbx, names) =>
{
    var imp = UnityEditor.AssetImporter.GetAtPath(fbx) as UnityEditor.ModelImporter;
    if (imp == null) { sb.AppendLine("[X] 无 importer: " + fbx); return; }
    var defs = imp.defaultClipAnimations;
    int n = 0;
    foreach (var d in defs)
        foreach (var nm in names)
            if (d.name == nm && !d.loopTime) { d.loopTime = true; n++; }
    if (n > 0) { imp.clipAnimations = defs; imp.SaveAndReimport(); }
    sb.AppendLine(string.Format("  {0,-26} 打开 loopTime: {1} 个", System.IO.Path.GetFileNameWithoutExtension(fbx), n));
};

sb.AppendLine("=== 1. 循环片段 ===");
setLoop(FbxMove, new[] { "Walking_A", "Walking_B", "Walking_C", "Running_A", "Running_B", "Jump_Idle" });
setLoop(FbxGen, new[] { "Idle_A", "Idle_B" });
// 注意：CombatMelee 的循环片段在步骤 2 里一并设置 —— 步骤 2 会把显式剪辑列表整体复位重建，
// 在这里设会被抹掉。

// ------------------------------------------------------------------
// 2. 从攻击片段尾部切出后摇子片段
//    命名 Slice_Diagonal_Rec / Slice_Horizontal_Rec，供 Atk1Rec / Atk2Rec 使用。
// ------------------------------------------------------------------
sb.AppendLine();
sb.AppendLine("=== 2. 后摇子片段 ===");
{
    var imp = UnityEditor.AssetImporter.GetAtPath(FbxMelee) as UnityEditor.ModelImporter;

    // 先复位：上一版脚本踩过坑 —— ModelImporterClipAnimation 是**引用类型**，
    // `var nd = d` 只是别名，改 nd.name 会把原始条目一起改名（Slice_Diagonal 消失、
    // 并多出一份重复的 _Rec）。这里先把显式列表清空，回到 FBX 自带的 22 个默认片段。
    var defaults = imp.defaultClipAnimations;
    imp.clipAnimations = new UnityEditor.ModelImporterClipAnimation[0];
    imp.SaveAndReimport();
    imp = UnityEditor.AssetImporter.GetAtPath(FbxMelee) as UnityEditor.ModelImporter;
    defaults = imp.defaultClipAnimations;
    sb.AppendLine("  复位后默认片段数 = " + defaults.Length);

    // 反射克隆：ModelImporterClipAnimation 没有拷贝构造，逐个属性复制
    System.Func<UnityEditor.ModelImporterClipAnimation, UnityEditor.ModelImporterClipAnimation> clone =
        src =>
    {
        var t = typeof(UnityEditor.ModelImporterClipAnimation);
        var dst = new UnityEditor.ModelImporterClipAnimation();
        foreach (var pi in t.GetProperties())
            if (pi.CanRead && pi.CanWrite && pi.GetIndexParameters().Length == 0)
                pi.SetValue(dst, pi.GetValue(src));
        return dst;
    };

    var list = new System.Collections.Generic.List<UnityEditor.ModelImporterClipAnimation>();
    foreach (var d in defaults) list.Add(clone(d));

    System.Action<string, string> makeRec = (srcName, recName) =>
    {
        foreach (var d in defaults)
        {
            if (d.name != srcName) continue;
            bool exists = false;
            foreach (var e in list) if (e.name == recName) exists = true;
            if (exists) { sb.AppendLine("  " + recName + " 已存在，跳过"); return; }
            var nd = clone(d);
            float total = d.lastFrame - d.firstFrame;
            nd.name = recName;
            nd.firstFrame = d.firstFrame + total * 0.70f;
            nd.lastFrame = d.lastFrame;
            nd.loopTime = false;
            list.Add(nd);
            sb.AppendLine(string.Format("  {0}  <- {1}  frames {2:F0}..{3:F0}  ({4:F2}s)",
                recName, srcName, nd.firstFrame, nd.lastFrame, (nd.lastFrame - nd.firstFrame) / 30f));
            return;
        }
        sb.AppendLine("  [!] 找不到源片段 " + srcName);
    };
    makeRec("Melee_1H_Attack_Slice_Diagonal", "Slice_Diagonal_Rec");
    makeRec("Melee_1H_Attack_Slice_Horizontal", "Slice_Horizontal_Rec");

    // 循环片段：待机 / 格挡这类要保持循环（FBX 默认全是关的）
    // 注意：KayKit 的单手近战**没有** Melee_1H_Idle，只有 2H / Unarmed 两个 idle，
    // 所以待机走 General 的 Idle_A（见下方 clipIdle 的说明）。
    string[] loops = { "Melee_2H_Idle", "Melee_Unarmed_Idle", "Melee_Blocking" };
    int looped = 0;
    foreach (var d in list)
        foreach (var nm in loops)
            if (d.name == nm && !d.loopTime) { d.loopTime = true; looped++; }
    sb.AppendLine("  打开 loopTime: " + looped + " 个");

    imp = UnityEditor.AssetImporter.GetAtPath(FbxMelee) as UnityEditor.ModelImporter;
    imp.clipAnimations = list.ToArray();
    imp.SaveAndReimport();
}

// ------------------------------------------------------------------
// 3. 取片段
// ------------------------------------------------------------------
var clipIdle = pick(FbxGen, "Idle_A");
var clipWalk = pick(FbxMove, "Walking_A");
var clipRun = pick(FbxMove, "Running_A");
var clipDash = pick(FbxAdv, "Dodge_Forward");
var clipAtkA = pick(FbxMelee, "Melee_1H_Attack_Slice_Diagonal");
var clipAtkARec = pick(FbxMelee, "Slice_Diagonal_Rec");
var clipAtkB = pick(FbxMelee, "Melee_1H_Attack_Slice_Horizontal");
var clipAtkBRec = pick(FbxMelee, "Slice_Horizontal_Rec");
var clipAtkC = pick(FbxMelee, "Melee_1H_Attack_Stab");

sb.AppendLine();
sb.AppendLine("=== 3. 片段取用 ===");
System.Action<string, UnityEngine.AnimationClip> report =
    (label, c) => sb.AppendLine(string.Format("  {0,-10} {1,-36} {2}",
        label, c == null ? "(缺失)" : c.name, c == null ? "" : c.length.ToString("F2") + "s"));

report("Idle", clipIdle); report("Walk", clipWalk); report("Run", clipRun); report("Dash", clipDash);
report("Atk1", clipAtkA); report("Atk1Rec", clipAtkARec);
report("Atk2", clipAtkB); report("Atk2Rec", clipAtkBRec); report("Atk3", clipAtkC);

if (clipIdle == null || clipWalk == null || clipRun == null || clipAtkA == null)
{
    sb.AppendLine("关键片段缺失，中止（不修改现有控制器）");
    return sb.ToString();
}

// ------------------------------------------------------------------
// 4. 原地重建控制器（保 GUID，预制体引用不断）
// ------------------------------------------------------------------
var ac = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(CtrlPath);
if (ac == null) return "[ERR] 找不到 " + CtrlPath;
string oldGuid = UnityEditor.AssetDatabase.AssetPathToGUID(CtrlPath);

for (int li = 0; li < ac.layers.Length; li++)
{
    var smOld = ac.layers[li].stateMachine;
    foreach (var cs in smOld.states)
        foreach (var t in cs.state.transitions) cs.state.RemoveTransition(t);
    foreach (var t in smOld.anyStateTransitions) smOld.RemoveAnyStateTransition(t);
    foreach (var t in smOld.entryTransitions) smOld.RemoveEntryTransition(t);
    foreach (var cs in smOld.states) smOld.RemoveState(cs.state);
}
int removedLayers = 0;
for (int i = ac.layers.Length - 1; i >= 1; i--) { ac.RemoveLayer(i); removedLayers++; }
foreach (var p in ac.parameters) ac.RemoveParameter(p);
sb.AppendLine();
sb.AppendLine("=== 4. 重建控制器 ===（GUID " + oldGuid + "，删除附加层 " + removedLayers + " 个）");

ac.AddParameter("Speed", UnityEngine.AnimatorControllerParameterType.Float);
ac.AddParameter("MotionSpeed", UnityEngine.AnimatorControllerParameterType.Float);
ac.AddParameter("Grounded", UnityEngine.AnimatorControllerParameterType.Bool);
ac.AddParameter("Dash", UnityEngine.AnimatorControllerParameterType.Trigger);
ac.AddParameter("Attack1", UnityEngine.AnimatorControllerParameterType.Trigger);
ac.AddParameter("Attack2", UnityEngine.AnimatorControllerParameterType.Trigger);
ac.AddParameter("Attack3", UnityEngine.AnimatorControllerParameterType.Trigger);

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
var stWalk = add("Walk", clipWalk, 1f);
stWalk.speedParameterActive = true; stWalk.speedParameter = "MotionSpeed";
var stRun = add("Run", clipRun, 1f);
stRun.speedParameterActive = true; stRun.speedParameter = "MotionSpeed";
var stDash = add("Dash", clipDash != null ? clipDash : clipIdle, 1f);
// 冲刺动画按「片段长度 / 冲刺时长」定速，播完正好等于冲刺结束
stDash.speed = clipDash != null ? Mathf.Max(0.01f, clipDash.length / DashDuration) : 1f;
var stA1 = add("Atk1", clipAtkA, 1f);
var stA1R = add("Atk1Rec", clipAtkARec != null ? clipAtkARec : clipAtkA, 1f);
var stA2 = add("Atk2", clipAtkB != null ? clipAtkB : clipAtkA, 1f);
var stA2R = add("Atk2Rec", clipAtkBRec != null ? clipAtkBRec : clipAtkA, 1f);
var stA3 = add("Atk3", clipAtkC != null ? clipAtkC : clipAtkA, 1f);

sm.defaultState = stIdle;

System.Func<UnityEditor.Animations.AnimatorState, UnityEditor.Animations.AnimatorState,
    bool, float, float, UnityEditor.Animations.AnimatorStateTransition> link =
    (from, to, exitTime, exitAt, dur) =>
{
    var t = from.AddTransition(to);
    t.hasExitTime = exitTime; t.exitTime = exitAt; t.duration = dur; t.hasFixedDuration = true;
    return t;
};
System.Action<UnityEditor.Animations.AnimatorStateTransition, string,
    UnityEditor.Animations.AnimatorConditionMode, float> cond =
    (t, p, mode, v) => { t.AddCondition(mode, v, p); };

var GT = UnityEditor.Animations.AnimatorConditionMode.Greater;
var LT = UnityEditor.Animations.AnimatorConditionMode.Less;
var IF = UnityEditor.Animations.AnimatorConditionMode.If;

// 移动：Idle <-> Walk <-> Run（带迟滞）
{ var t = link(stIdle, stWalk, false, 0f, 0.15f); cond(t, "Speed", GT, MoveEnterSpeed); }
{ var t = link(stIdle, stRun, false, 0f, 0.15f); cond(t, "Speed", GT, RunEnterSpeed); }
{ var t = link(stWalk, stIdle, false, 0f, 0.20f); cond(t, "Speed", LT, MoveEnterSpeed); }
{ var t = link(stWalk, stRun, false, 0f, 0.14f); cond(t, "Speed", GT, RunEnterSpeed); }
{ var t = link(stRun, stWalk, false, 0f, 0.18f); cond(t, "Speed", LT, RunExitSpeed); }
{ var t = link(stRun, stIdle, false, 0f, 0.22f); cond(t, "Speed", LT, MoveEnterSpeed); }

// 冲刺
{ var t = link(stIdle, stDash, false, 0f, 0.08f); cond(t, "Dash", IF, 0f); }
{ var t = link(stWalk, stDash, false, 0f, 0.08f); cond(t, "Dash", IF, 0f); }
{ var t = link(stRun, stDash, false, 0f, 0.08f); cond(t, "Dash", IF, 0f); }
link(stDash, stIdle, true, 0.92f, 0.14f);

// 起手攻击
System.Action<UnityEditor.Animations.AnimatorState, float> toAtk1 =
    (from, blend) => { var t = link(from, stA1, from == stDash, from == stDash ? 0.60f : 0f, blend); cond(t, "Attack1", IF, 0f); };
toAtk1(stIdle, 0.06f); toAtk1(stWalk, 0.06f); toAtk1(stRun, 0.06f); toAtk1(stDash, 0.08f);

// 第 1 段 -> 后摇 -> 第 2 段
link(stA1, stA1R, true, 0.85f, 0.08f);
// 注意：接下一段**不能**再挂 hasExitTime。挂上去就有两个时间口径
// （状态机的 exitTime 0.30 与 C# 的 comboRecCancelStart[0] 0.30），
// 触发器到达时 exitTime 刚好被跨过 → 转移时有时无，表现是"有时候能连、有时候断"。
// 时间口径只留 C# 的取消窗口：窗口没开就不会置触发器，窗口一开立刻转移。
// 详见 Tools/cs/s3_patch_combo_transitions.cs。
{ var t = link(stA1R, stA2, false, 0f, 0.06f); cond(t, "Attack2", IF, 0f); }
link(stA1R, stIdle, true, 0.85f, 0.16f);
{ var t = link(stA1R, stWalk, true, 0.55f, 0.16f); cond(t, "Speed", GT, MoveEnterSpeed); }
{ var t = link(stA1R, stRun, true, 0.55f, 0.16f); cond(t, "Speed", GT, RunEnterSpeed); }
{ var t = link(stA1R, stDash, true, 0.30f, 0.08f); cond(t, "Dash", IF, 0f); }

// 第 2 段 -> 后摇 -> 第 3 段
link(stA2, stA2R, true, 0.85f, 0.08f);
{ var t = link(stA2R, stA3, false, 0f, 0.06f); cond(t, "Attack3", IF, 0f); }
link(stA2R, stIdle, true, 0.85f, 0.16f);
{ var t = link(stA2R, stWalk, true, 0.55f, 0.16f); cond(t, "Speed", GT, MoveEnterSpeed); }
{ var t = link(stA2R, stRun, true, 0.55f, 0.16f); cond(t, "Speed", GT, RunEnterSpeed); }
{ var t = link(stA2R, stDash, true, 0.30f, 0.08f); cond(t, "Dash", IF, 0f); }

// 第 3 段（收招）
link(stA3, stIdle, true, 0.72f, 0.20f);
{ var t = link(stA3, stWalk, true, 0.70f, 0.20f); cond(t, "Speed", GT, MoveEnterSpeed); }
{ var t = link(stA3, stRun, true, 0.70f, 0.20f); cond(t, "Speed", GT, RunEnterSpeed); }
{ var t = link(stA3, stDash, true, 0.45f, 0.10f); cond(t, "Dash", IF, 0f); }

// 第 0 层开 IK Pass（FootIK 靠它拿 OnAnimatorIK 回调）
var layersBuf = ac.layers;
var baseLayer = layersBuf[0];
baseLayer.avatarMask = null; baseLayer.defaultWeight = 1f; baseLayer.iKPass = true;
layersBuf[0] = baseLayer;
ac.layers = layersBuf;

UnityEditor.EditorUtility.SetDirty(ac);
UnityEditor.AssetDatabase.SaveAssets();

// ------------------------------------------------------------------
// 5. 复核
// ------------------------------------------------------------------
sb.AppendLine();
sb.AppendLine("=== 复核 Player.controller ===");
foreach (var L in ac.layers)
    sb.AppendLine("  " + L.name + " 权重=" + L.defaultWeight + " 遮罩=" + (L.avatarMask == null ? "(无)" : L.avatarMask.name)
        + " IKPass=" + L.iKPass);
foreach (var s in ac.layers[0].stateMachine.states)
{
    var m = s.state.motion as UnityEngine.AnimationClip;
    sb.AppendLine(string.Format("  {0,-9} 片段={1,-34} 速度={2} 时长≈{3}",
        s.state.name, m == null ? "(无)" : m.name,
        s.state.speedParameterActive ? ("参数:" + s.state.speedParameter) : s.state.speed.ToString("F2"),
        m == null ? "-" : (s.state.speedParameterActive ? "由 MotionSpeed 决定" : (m.length / Mathf.Max(s.state.speed, 0.001f)).ToString("F2") + "s")));
}
return sb.ToString();
