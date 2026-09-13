// S1-3：创建玩家 AnimatorController
// 说明：UAL2 是「交互动作库」，不含普通 Walk/Run 循环。
//       这里用 Walk_Carry_Loop 作占位；正式移动动画换 Mixamo 后，
//       只需把 Run 状态的 Motion 换掉，状态机结构与参数契约不变。
var sb = new System.Text.StringBuilder();

const string ClipFbx = "Assets/ThirdParty/Quaternius/UniversalAnimationLibrary2/Unity/UAL2_Standard.fbx";
const string CtrlDir = "Assets/_Project/Animations";
const string CtrlPath = "Assets/_Project/Animations/Player.controller";

if (!UnityEditor.AssetDatabase.IsValidFolder(CtrlDir))
    UnityEditor.AssetDatabase.CreateFolder("Assets/_Project", "Animations");

// ---------- 1) 取剪辑 ----------
System.Func<string, UnityEngine.AnimationClip> findClip = (wanted) =>
{
    foreach (var o in UnityEditor.AssetDatabase.LoadAllAssetsAtPath(ClipFbx))
    {
        var c = o as UnityEngine.AnimationClip;
        if (c != null && c.name == wanted) return c;
    }
    return null;
};

var clipIdle = findClip("Armature|Idle_FoldArms_Loop");
var clipRun = findClip("Armature|Walk_Carry_Loop");     // 占位：待 Mixamo 替换
var clipDash = findClip("Armature|Sword_Dash");

sb.AppendLine("剪辑解析：");
sb.AppendLine("   Idle = " + (clipIdle != null ? clipIdle.name + "  (" + clipIdle.length.ToString("F2") + "s, loop=" + clipIdle.isLooping + ")" : "缺失!"));
sb.AppendLine("   Run  = " + (clipRun != null ? clipRun.name + "  (" + clipRun.length.ToString("F2") + "s) [占位]" : "缺失!"));
sb.AppendLine("   Dash = " + (clipDash != null ? clipDash.name + "  (" + clipDash.length.ToString("F2") + "s)" : "缺失!"));
if (clipIdle == null || clipRun == null || clipDash == null) { sb.AppendLine("!! 有剪辑缺失，中止"); return sb.ToString(); }
sb.AppendLine();

// ---------- 2) 建控制器 ----------
UnityEditor.AssetDatabase.DeleteAsset(CtrlPath);
var ac = UnityEditor.Animations.AnimatorController.CreateAnimatorControllerAtPath(CtrlPath);

// 参数契约（必须与 PlayerController 里的 Animator.StringToHash 一致）
ac.AddParameter("Speed", UnityEditor.AnimatorControllerParameterType.Float);
ac.AddParameter("MotionSpeed", UnityEditor.AnimatorControllerParameterType.Float);
ac.AddParameter("Grounded", UnityEditor.AnimatorControllerParameterType.Bool);
ac.AddParameter("Dash", UnityEditor.AnimatorControllerParameterType.Trigger);
sb.AppendLine("参数: Speed / MotionSpeed / Grounded / Dash");
sb.AppendLine();

// ---------- 3) 状态 ----------
var sm = ac.layers[0].stateMachine;

var stIdle = sm.AddState("Idle");
stIdle.motion = clipIdle;
stIdle.writeDefaultValues = false;
stIdle.iKOnFeet = true;

var stRun = sm.AddState("Run");
stRun.motion = clipRun;
stRun.writeDefaultValues = false;
stRun.iKOnFeet = true;
stRun.speed = 1.25f;       // 让占位的走步看起来像小跑

var stDash = sm.AddState("Dash");
stDash.motion = clipDash;
stDash.writeDefaultValues = false;
stDash.iKOnFeet = true;

sm.defaultState = stIdle;
sb.AppendLine("状态: Idle / Run / Dash   (默认 = Idle)");

// ---------- 4) 过渡 ----------
System.Func<UnityEditor.Animations.AnimatorState, UnityEditor.Animations.AnimatorState,
    float, UnityEditor.Animations.AnimatorStateTransition> link =
    (from, to, dur) =>
    {
        var t = from.AddTransition(to);
        t.hasExitTime = false;
        t.duration = dur;
        t.hasFixedDuration = true;
        t.interruptionSource = UnityEditor.Animations.TransitionInterruptionSource.None;
        return t;
    };

// Idle <-> Run：用 Speed 阈值切换
var tIdleRun = link(stIdle, stRun, 0.14f);
tIdleRun.AddCondition(UnityEditor.Animations.AnimatorConditionMode.Greater, 0.15f, "Speed");

var tRunIdle = link(stRun, stIdle, 0.18f);
tRunIdle.AddCondition(UnityEditor.Animations.AnimatorConditionMode.Less, 0.15f, "Speed");

// 任意状态 -> Dash（Trigger）
var tAnyDash = sm.AddAnyStateTransition(stDash);
tAnyDash.hasExitTime = false;
tAnyDash.duration = 0.06f;
tAnyDash.hasFixedDuration = true;
tAnyDash.canTransitionToSelf = false;
tAnyDash.AddCondition(UnityEditor.Animations.AnimatorConditionMode.If, 0f, "Dash");

// Dash -> Idle（播完自然回）
var tDashIdle = link(stDash, stIdle, 0.12f);
tDashIdle.hasExitTime = true;
tDashIdle.exitTime = 0.92f;

sb.AppendLine("过渡: Idle→Run(Speed>0.15) / Run→Idle(Speed<0.15) / Any→Dash(Trigger) / Dash→Idle(exitTime 0.92)");

UnityEditor.EditorUtility.SetDirty(ac);
UnityEditor.AssetDatabase.SaveAssets();
UnityEditor.AssetDatabase.Refresh();

// ---------- 5) 复查 ----------
sb.AppendLine();
sb.AppendLine("=== 复查：控制器已落盘 ===");
var loaded = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(CtrlPath);
sb.AppendLine("   " + CtrlPath + "  ->  " + (loaded != null ? "OK" : "读取失败"));
if (loaded != null)
{
    sb.AppendLine("   layer: " + loaded.layers[0].name);
    sb.AppendLine("   parameter 数: " + loaded.parameters.Length);
    foreach (var p in loaded.parameters) sb.AppendLine("       " + p.name + " : " + p.type);
    sb.AppendLine("   state 数: " + loaded.layers[0].stateMachine.states.Length);
    foreach (var s in loaded.layers[0].stateMachine.states)
        sb.AppendLine("       " + s.state.name + "  motion=" + (s.state.motion != null ? s.state.motion.name : "null"));
}

return sb.ToString();
