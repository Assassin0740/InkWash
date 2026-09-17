// 把 Play 现场恢复到「干净可用」状态。
//
// 背景：诊断脚本会在运行时临时改状态机 / 关控制器 / 把角色搬到半空。一旦脚本中途抛异常，
// 这些改动就留在现场了（Play 模式下改的是内存里的资产，不 SaveAssets，退出 Play 才回滚）。
// 跑任何诊断之前先跑这个，保证起点一致。
//
// 做四件事：
//   1. 控制器：Run 片段钉回 KI 的 Run01_Forward，恢复 speedParameterActive
//   2. 角色：重新落到地面（从当前位置向下打射线），恢复 PlayerController 与相机 rig
//   3. 动画：anim.speed = 1，清掉注入输入
//   4. 汇报结果

var sb = new System.Text.StringBuilder();
const string FbxKI = "Assets/ThirdParty/KevinIglesias/Animations/Male/Movement/Run/HumanM@Run01_Forward.fbx";
const string CtrlPath = "Assets/_Project/Animations/Player.controller";

// ---- 1. 控制器 ----
var ac = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(CtrlPath);
if (ac != null)
{
    UnityEditor.Animations.AnimatorState stRun = null;
    foreach (var s in ac.layers[0].stateMachine.states) if (s.state.name == "Run") stRun = s.state;
    UnityEngine.AnimationClip ki = null;
    foreach (var o in UnityEditor.AssetDatabase.LoadAllAssetsAtPath(FbxKI))
    {
        var c = o as UnityEngine.AnimationClip;
        if (c != null && !c.name.StartsWith("__preview__")) { ki = c; break; }
    }
    if (stRun != null && ki != null)
    {
        stRun.motion = ki;
        stRun.speedParameterActive = true;
        stRun.speedParameter = "MotionSpeed";
        sb.AppendLine("控制器: Run 已钉回 " + ki.name);
    }
    else sb.AppendLine("[!] 控制器/片段没找到");
}
else sb.AppendLine("[!] 找不到 " + CtrlPath);

if (!UnityEngine.Application.isPlaying) { sb.AppendLine("当前不在 Play 模式，仅完成控制器部分"); return sb.ToString(); }

// ---- 2. 现场 ----
var ctl = UnityEngine.Object.FindObjectOfType<InkWash.Player.PlayerController>();
if (ctl == null) { sb.AppendLine("[!] 场景里没有 PlayerController"); return sb.ToString(); }
ctl.enabled = true;
ctl.EndInputOverride();
ctl.SetInjectedMove(UnityEngine.Vector2.zero, false);

var root = ctl.transform;
var cam = UnityEngine.Camera.main;
if (cam != null)
{
    var rig = cam.GetComponent("ThirdPersonCamera") as UnityEngine.MonoBehaviour;
    if (rig != null) { rig.enabled = true; sb.AppendLine("相机 rig 已启用"); }
    cam.fieldOfView = 60f;
    cam.targetTexture = null;
}

// 落回地面：从当前位置上方往下打
var p = root.position;
UnityEngine.RaycastHit hit;
if (UnityEngine.Physics.Raycast(p + UnityEngine.Vector3.up * 60f, UnityEngine.Vector3.down, out hit, 200f))
{
    root.position = hit.point + UnityEngine.Vector3.up * 0.05f;
    sb.AppendLine(string.Format("落回地面: {0} -> {1}", p.ToString("F1"), root.position.ToString("F1")));
}
else sb.AppendLine("[!] 向下射线没打到地面，位置保持不变");

// ---- 3. 动画 ----
var anim = ctl.animator != null ? ctl.animator : ctl.GetComponentInChildren<UnityEngine.Animator>();
if (anim != null)
{
    anim.speed = 1f;
    anim.SetFloat("Speed", 0f);
    anim.SetFloat("MotionSpeed", 1f);
    anim.SetBool("Grounded", true);
    sb.AppendLine("animator: speed=1, Speed=0, MotionSpeed=1");
}
else sb.AppendLine("[!] 没找到 Animator");

return sb.ToString();
