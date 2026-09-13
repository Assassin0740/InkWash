// 重建 Player.controller（S2.1 修正版）。
//
// 本轮要解决的 6 个问题里，有 4 个是**动画选型**造成的：
//
//   问题1「走路仰着身子」+ 问题3「没有跑步动作」
//       上一版用 UAL2 的 Walk_Carry_Loop 当走路（那是**抱重物**的走法：躯干后仰抵住重心），
//       还拿它加速当跑步。UAL2 里根本没有正经走路循环，也没有任何跑步片段。
//       本版改用 UAL1（CC0）的 Walk_Loop（走路）/ Jog_Fwd_Loop（跑步），并新增 Run 状态。
//
//   问题2「攻击第一下姿势很怪」
//       上一版为了盖住「抱物」的上半身，加了一个 UpperBody 遮罩层，但那一层只有一个
//       Idle_FoldArms 状态、**没有任何连线** —— 也就是任何时候上半身都被强制抱臂待机。
//       攻击时基底层在打剑招、上半身却锁在抱臂姿势，于是出现拧麻花一样的怪姿势。
//       本版**整个删掉这一层**：既然走路换成正经走路，就不需要遮罩了。
//
//   问题4「右键动作很奇怪」
//       上一版 Dash 用的是 UAL2 Slide_Start（滑铲），还被压进 0.32s 播完，
//       看起来像抽搐。本版改用 UAL1 的 Roll（翻滚），并把冲刺时长对齐到片段本身。
//
// 攻击仍用 UAL2 的 Sword_Regular_A/B/C（剑招是它的强项）。
// 两套库同作者、同套「通用人形骨架」，Unity 走 Humanoid 重定向即可混用。
//
// ⚠ 本脚本会原地重建 Player.controller（保住 GUID，预制体引用不断）。
var sb = new System.Text.StringBuilder();

const string CtrlPath = "Assets/_Project/Animations/Player.controller";
const string FbxUAL2 = "Assets/ThirdParty/Quaternius/UniversalAnimationLibrary2/Unity/UAL2_Standard.fbx";
const string FbxUAL1 = "Assets/ThirdParty/Quaternius/UniversalAnimationLibrary/Unity/AnimationLibrary_Unity_Standard.fbx";
// Kevin Iglesias《Human Soldier Animations FREE》(Unity 商店免费资源) —— 跑步改用它的 Run01_Forward。
// 见下方 clipRun 处的说明：Quaternius 的跑/慢跑片段骨盆甩幅过大，观感是"扭腰"。
const string FbxKIRun = "Assets/ThirdParty/KevinIglesias/Animations/Male/Movement/Run/HumanM@Run01_Forward.fbx";

// 冲刺时长：必须与 PlayerController.dashDuration 一致（验收会核对）
const float DashDuration = 0.72f;
// 走路 <-> 跑步的切换阈值（带迟滞，避免在阈值附近来回抖）
const float RunEnterSpeed = 3.0f;
const float RunExitSpeed = 2.5f;
const float MoveEnterSpeed = 0.15f;

// ------------------------------------------------------------------
// 0. 载入两套库的片段（按 '|' 之后的名字建索引）
// ------------------------------------------------------------------
System.Action<string, System.Collections.Generic.Dictionary<string, UnityEngine.AnimationClip>> load =
    (path, dict) =>
{
    var assets = UnityEditor.AssetDatabase.LoadAllAssetsAtPath(path);
    if (assets == null || assets.Length == 0)
    {
        sb.AppendLine("  [!] 载入失败（可能还在导入）：" + path);
        return;
    }
    foreach (var o in assets)
    {
        var c = o as UnityEngine.AnimationClip;
        if (c == null || c.name.StartsWith("__preview__")) continue;
        string key = c.name;
        int bar = key.LastIndexOf('|');
        if (bar >= 0 && bar + 1 < key.Length) key = key.Substring(bar + 1);
        if (!dict.ContainsKey(key)) dict[key] = c;
    }
    sb.AppendLine("  载入 " + dict.Count + " 个片段 <- " + path);
    // 打印键名，便于排查"片段名对不上"（换素材时经常撞上）
    foreach (var k in dict.Keys) sb.AppendLine("      · " + k);
};

var ual2 = new System.Collections.Generic.Dictionary<string, UnityEngine.AnimationClip>();
var ual1 = new System.Collections.Generic.Dictionary<string, UnityEngine.AnimationClip>();
var ki = new System.Collections.Generic.Dictionary<string, UnityEngine.AnimationClip>();
load(FbxUAL2, ual2);
load(FbxUAL1, ual1);
load(FbxKIRun, ki);

System.Func<System.Collections.Generic.Dictionary<string, UnityEngine.AnimationClip>, string,
    UnityEngine.AnimationClip> pick = (dict, name) =>
{
    UnityEngine.AnimationClip c;
    if (dict.TryGetValue(name, out c)) return c;
    sb.AppendLine("  [!] 缺片段: " + name);
    return null;
};

var clipIdle = pick(ual1, "Sword_Idle");          // 持剑待机（比抱臂待机贴合战斗角色）
var clipWalk = pick(ual1, "Walk_Loop");           // 正经走路
// 跑步：改用 Kevin Iglesias《Human Soldier Animations FREE》的 HumanM@Run01_Forward。
//
// 为什么不继续用 Quaternius 的跑片段（Jog_Fwd_Loop / Sprint_Loop）：
//   用 Tools/cs/s2_probe_runtwist.cs 量化过「躯干扭转角 = 肩线偏航 - 髋线偏航」，
//   同一条件下（runSpeed 4.2、同一角色、同一 Avatar）：
//       Walk_Loop        髋 9.5°  肩 5.6°  扭转 15.1°   ← 正常
//       Sprint_Loop      髋 42.7° 肩 18.4° 扭转 61.1°   ← 骨盆甩得离谱
//       Jog_Fwd_Loop     髋 55.3° 肩 30.6° 扭转 85.9°   ← 更离谱
//   并且 s2_probe_muscle.cs 读原始肌肉曲线证实这是**烘焙在片段里**的
//   （UpperChest Twist 峰峰：Walk 0.377 / Jog 3.236 / Sprint 2.382），
//   不是重定向放出来的 —— 走路干净、跑步拧麻花，所以只能换片段。
//   真人跑步躯干扭转一般 ≤20°，Quaternius 那两段是它的 3~4 倍，观感就是"扭腰"。
//
// 授权：Kevin Iglesias 的 Human Soldier Animations FREE 是 Unity 商店免费资源，
//       本机 Asset Store 缓存里已有（Human Soldier Animations FREE.unitypackage），
//       按 Unity 商店标准 EULA 可用于本项目。
// 片段名注意：FBX 文件名是 "HumanM@Run01_Forward"，但 Unity 把 "@" 前那截
// 当成**模型名前缀**剥掉了，实际片段名只有 "Run01_Forward"（实测踩过）。
var clipRun = pick(ki, "Run01_Forward");   // 跑步
if (clipRun == null && ki.Count > 0)
{
    // 兜底：换素材时名字对不上是常事，这个 FBX 只含一个片段就直接拿它。
    foreach (var kv in ki) { clipRun = kv.Value; break; }
    sb.AppendLine("  [i] 按名字没命中，改用包内唯一片段: " + clipRun.name);
}
var clipDash = pick(ual1, "Roll");                // 翻滚（非 _RM 版本）
var clipAtkA = pick(ual2, "Sword_Regular_A");
var clipAtkARec = pick(ual2, "Sword_Regular_A_Rec");
var clipAtkB = pick(ual2, "Sword_Regular_B");
var clipAtkBRec = pick(ual2, "Sword_Regular_B_Rec");
var clipAtkC = pick(ual2, "Sword_Regular_C");

if (clipIdle == null || clipWalk == null || clipRun == null || clipAtkA == null)
{
    sb.AppendLine("关键片段缺失，中止（不修改现有控制器）");
    return sb.ToString();
}

// ------------------------------------------------------------------
// 1. 让 UAL1 的循环片段真的循环
//    UAL1 的 FBX 里所有片段的 loopTime 都是关的（实测 isLooping=False），
//    不打开的话走路会走一步就停在最后一帧。
// ------------------------------------------------------------------
var imp1 = UnityEditor.AssetImporter.GetAtPath(FbxUAL1) as UnityEditor.ModelImporter;
if (imp1 != null)
{
    var defs = imp1.defaultClipAnimations;
    int looped = 0;
    foreach (var d in defs)
    {
        bool want = d.name.EndsWith("_Loop");
        if (want && !d.loopTime) { d.loopTime = true; looped++; }
    }
    if (looped > 0)
    {
        imp1.clipAnimations = defs;
        imp1.SaveAndReimport();
        sb.AppendLine("已为 " + looped + " 个 UAL1 循环片段打开 loopTime 并重新导入");
    }
    else sb.AppendLine("UAL1 循环片段的 loopTime 已是开着的（或无需修改）");
}

// ------------------------------------------------------------------
// 2. 原地重建控制器（保 GUID）
// ------------------------------------------------------------------
var ac = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(CtrlPath);
if (ac == null) return "[ERR] 找不到 " + CtrlPath;

string oldGuid = UnityEditor.AssetDatabase.AssetPathToGUID(CtrlPath);

for (int li = 0; li < ac.layers.Length; li++)
{
    var smOld = ac.layers[li].stateMachine;
    foreach (var cs in smOld.states)
        foreach (var t in cs.state.transitions)
            cs.state.RemoveTransition(t);
    foreach (var t in smOld.anyStateTransitions) smOld.RemoveAnyStateTransition(t);
    foreach (var t in smOld.entryTransitions) smOld.RemoveEntryTransition(t);
    foreach (var cs in smOld.states) smOld.RemoveState(cs.state);
}
// 删掉第 1 层及以后 —— 本次重点：把 UpperBody 遮罩层彻底删掉
int removedLayers = 0;
for (int i = ac.layers.Length - 1; i >= 1; i--) { ac.RemoveLayer(i); removedLayers++; }
foreach (var p in ac.parameters) ac.RemoveParameter(p);
sb.AppendLine("原地重建 Player.controller（GUID " + oldGuid + "），删除附加层 " + removedLayers + " 个");

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
var stWalk = add("Walk", clipWalk, 1f);
stWalk.speedParameterActive = true;      // 步伐同步：播放速度 = MotionSpeed
stWalk.speedParameter = "MotionSpeed";
var stRun = add("Run", clipRun, 1f);
stRun.speedParameterActive = true;
stRun.speedParameter = "MotionSpeed";
var stDash = add("Dash", clipDash != null ? clipDash : clipIdle, 1f);
// 冲刺动画按「片段长度 / 冲刺时长」定速，播完正好等于冲刺结束 —— 不会再「挥着剑滑行」
// 注意：AnimatorState.motion 的静态类型是 Motion（没有 length），要用 AnimationClip 变量取
stDash.speed = clipDash != null ? Mathf.Max(0.01f, clipDash.length / DashDuration) : 1f;
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

System.Action<UnityEditor.Animations.AnimatorStateTransition, string,
    UnityEditor.Animations.AnimatorConditionMode, float> cond =
    (t, p, mode, v) => { t.AddCondition(mode, v, p); };

// —— 移动：Idle <-> Walk <-> Run（带迟滞）——
UnityEditor.Animations.AnimatorConditionMode GT = UnityEditor.Animations.AnimatorConditionMode.Greater;
UnityEditor.Animations.AnimatorConditionMode LT = UnityEditor.Animations.AnimatorConditionMode.Less;
UnityEditor.Animations.AnimatorConditionMode IF = UnityEditor.Animations.AnimatorConditionMode.If;

var tIdleWalk = link(stIdle, stWalk, false, 0f, 0.15f); cond(tIdleWalk, "Speed", GT, MoveEnterSpeed);
var tIdleRun = link(stIdle, stRun, false, 0f, 0.15f); cond(tIdleRun, "Speed", GT, RunEnterSpeed);
var tWalkIdle = link(stWalk, stIdle, false, 0f, 0.20f); cond(tWalkIdle, "Speed", LT, MoveEnterSpeed);
var tWalkRun = link(stWalk, stRun, false, 0f, 0.14f); cond(tWalkRun, "Speed", GT, RunEnterSpeed);
var tRunWalk = link(stRun, stWalk, false, 0f, 0.18f); cond(tRunWalk, "Speed", LT, RunExitSpeed);
var tRunIdle = link(stRun, stIdle, false, 0f, 0.22f); cond(tRunIdle, "Speed", LT, MoveEnterSpeed);

// —— 冲刺：Idle / Walk / Run 都能起 ——
var tDash1 = link(stIdle, stDash, false, 0f, 0.08f); cond(tDash1, "Dash", IF, 0f);
var tDash2 = link(stWalk, stDash, false, 0f, 0.08f); cond(tDash2, "Dash", IF, 0f);
var tDash3 = link(stRun, stDash, false, 0f, 0.08f); cond(tDash3, "Dash", IF, 0f);
var tDashOut = link(stDash, stIdle, true, 0.92f, 0.14f);

// —— 起手攻击（Idle / Walk / Run / 冲刺末段 都能接）——
System.Action<UnityEditor.Animations.AnimatorState, float> toAtk1 =
    (from, blend) => { var t = link(from, stA1, from == stDash, from == stDash ? 0.60f : 0f, blend); cond(t, "Attack1", IF, 0f); };
toAtk1(stIdle, 0.06f);
toAtk1(stWalk, 0.06f);
toAtk1(stRun, 0.06f);
toAtk1(stDash, 0.08f);

// —— 第 1 段 -> 后摇；后摇开取消窗口 ——
link(stA1, stA1R, true, 0.85f, 0.08f);
var t10 = link(stA1R, stA2, true, 0.30f, 0.06f); cond(t10, "Attack2", IF, 0f);
link(stA1R, stIdle, true, 0.85f, 0.16f);
{ var t = link(stA1R, stWalk, true, 0.55f, 0.16f); cond(t, "Speed", GT, MoveEnterSpeed); }
{ var t = link(stA1R, stRun, true, 0.55f, 0.16f); cond(t, "Speed", GT, RunEnterSpeed); }
{ var t = link(stA1R, stDash, true, 0.30f, 0.08f); cond(t, "Dash", IF, 0f); }

// —— 第 2 段 ——
link(stA2, stA2R, true, 0.85f, 0.08f);
var t15 = link(stA2R, stA3, true, 0.30f, 0.06f); cond(t15, "Attack3", IF, 0f);
link(stA2R, stIdle, true, 0.85f, 0.16f);
{ var t = link(stA2R, stWalk, true, 0.55f, 0.16f); cond(t, "Speed", GT, MoveEnterSpeed); }
{ var t = link(stA2R, stRun, true, 0.55f, 0.16f); cond(t, "Speed", GT, RunEnterSpeed); }
{ var t = link(stA2R, stDash, true, 0.30f, 0.08f); cond(t, "Dash", IF, 0f); }

// —— 第 3 段（收招）——
link(stA3, stIdle, true, 0.72f, 0.20f);
{ var t = link(stA3, stWalk, true, 0.70f, 0.20f); cond(t, "Speed", GT, MoveEnterSpeed); }
{ var t = link(stA3, stRun, true, 0.70f, 0.20f); cond(t, "Speed", GT, RunEnterSpeed); }
{ var t = link(stA3, stDash, true, 0.45f, 0.10f); cond(t, "Dash", IF, 0f); }

// ---- 第 0 层开 IK Pass（FootIK 组件靠它拿到 OnAnimatorIK 回调）----
var layersBuf = ac.layers;
var baseLayer = layersBuf[0];
baseLayer.avatarMask = null;
baseLayer.defaultWeight = 1f;
baseLayer.iKPass = true;
layersBuf[0] = baseLayer;
ac.layers = layersBuf;

UnityEditor.EditorUtility.SetDirty(ac);
UnityEditor.AssetDatabase.SaveAssets();

// ------------------------------------------------------------------
// 3. 复核
// ------------------------------------------------------------------
sb.AppendLine();
sb.AppendLine("=== 复核 Player.controller ===");
sb.AppendLine("图层数: " + ac.layers.Length + "（本次设计只需要 1 层全身动作，遮罩层已删除）");
foreach (var L in ac.layers)
    sb.AppendLine("  " + L.name + "  权重=" + L.defaultWeight
        + "  遮罩=" + (L.avatarMask == null ? "(无)" : L.avatarMask.name)
        + "  模式=" + L.blendingMode + "  IKPass=" + L.iKPass);
sb.AppendLine("参数: " + string.Join(", ", System.Array.ConvertAll(ac.parameters, p => p.name + ":" + p.type)));
sb.AppendLine();
sb.AppendLine("状态（片段 / 速度 / 实际时长）:");
foreach (var s in ac.layers[0].stateMachine.states)
{
    var m = s.state.motion as UnityEngine.AnimationClip;
    float spd = s.state.speedParameterActive ? -1f : s.state.speed;
    float len = m == null ? 0f : (s.state.speedParameterActive ? 0f : m.length / Mathf.Max(spd, 0.001f));
    sb.AppendLine(string.Format("  {0,-9} 片段={1,-34} 速度={2,-16} 时长≈{3}",
        s.state.name, m == null ? "(无)" : m.name,
        s.state.speedParameterActive ? ("参数:" + s.state.speedParameter) : spd.ToString("F2"),
        s.state.speedParameterActive ? "由 MotionSpeed 决定" : (len.ToString("F2") + "s")));
}
sb.AppendLine();
sb.AppendLine("连线:");
foreach (var s in ac.layers[0].stateMachine.states)
    foreach (var t in s.state.transitions)
    {
        var cs = new System.Collections.Generic.List<string>();
        foreach (var c in t.conditions) cs.Add(c.parameter + " " + c.mode + " " + c.threshold);
        sb.AppendLine("  " + s.state.name + " -> " + t.destinationState.name
            + "  exitTime=" + (t.hasExitTime ? t.exitTime.ToString("F2") : "无")
            + "  [" + string.Join(";", cs.ToArray()) + "]");
    }

return sb.ToString();
