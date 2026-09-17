// q_swap_move2.cs —— ① Walk 换成 KI Walk01_Forward（原生 1.80 m/s，循环）② Idle 换成 Feng Idle 循环副本
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;

var sb = new System.Text.StringBuilder();
string ctrlPath = "Assets/_Project/Animations/Player.controller";
var ac = AssetDatabase.LoadAssetAtPath<AnimatorController>(ctrlPath);
if (ac == null) { Debug.LogError("找不到控制器"); return "找不到 " + ctrlPath; }

// ---------- 取目标片段 ----------
string kiWalkPath = "Assets/ThirdParty/KevinIglesias/Animations/Male/Movement/Walk/HumanM@Walk01_Forward.fbx";
var kiClips = AssetDatabase.LoadAllAssetsAtPath(kiWalkPath)
    .OfType<AnimationClip>()
    .Where(c => !c.name.StartsWith("__preview__"))
    .ToList();
sb.AppendLine("KI Walk fbx 里的片段: " + string.Join(", ", kiClips.Select(c => c.name + "(" + c.length.ToString("F3") + "s,loop=" + c.isLooping + ")").ToArray()));
var walkClip = kiClips.FirstOrDefault(c => c.name.ToLower().Contains("walk"));
if (walkClip == null) { Debug.LogError("KI fbx 里找不到 walk 片段"); return sb.ToString(); }

var idleLoopPath = "Assets/_Project/Animations/Feng_Idle_Loop.anim";
var idleLoop = AssetDatabase.LoadAssetAtPath<AnimationClip>(idleLoopPath);
if (idleLoop == null) { Debug.LogError("找不到 " + idleLoopPath); return sb.ToString(); }

// ---------- 替换 ----------
var sm = ac.layers[0].stateMachine;
var done = new System.Collections.Generic.List<string>();
foreach (var cs in sm.states)
{
    if (cs.state.name == "Walk")
    {
        cs.state.motion = walkClip;
        done.Add("Walk → " + walkClip.name);
    }
    else if (cs.state.name == "Idle")
    {
        cs.state.motion = idleLoop;
        done.Add("Idle → " + idleLoop.name);
    }
}

EditorUtility.SetDirty(ac);
AssetDatabase.SaveAssets();
AssetDatabase.Refresh();

sb.AppendLine();
sb.AppendLine("已替换:");
foreach (var d in done) sb.AppendLine("  " + d);

// ---------- 复核（重新从磁盘读） ----------
sb.AppendLine();
sb.AppendLine("---- 复核（重新读盘）----");
var re = AssetDatabase.LoadAssetAtPath<AnimatorController>(ctrlPath);
foreach (var cs in re.layers[0].stateMachine.states)
{
    var c = cs.state.motion as AnimationClip;
    sb.AppendLine("  " + cs.state.name.PadRight(10)
        + (c != null ? c.name.PadRight(24) + " len=" + c.length.ToString("F3") + " loop=" + c.isLooping
                     : (cs.state.motion != null ? cs.state.motion.name : "<null>")));
}
return sb.ToString();
