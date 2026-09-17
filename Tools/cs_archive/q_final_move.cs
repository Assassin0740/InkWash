// q_final_move.cs —— 走路收口：
//   ① 生成 Feng Walk 的循环副本（源片段 m_LoopTime=0，播一遍就停在末帧）
//   ② 控制器 Walk 换回该副本（KI Walk01_Forward 幅度像跨步/跑步，实测最低点跨度 0.327 = Feng 的两倍）
//   ③ 攻击收招段提速（收招占一轮连击 60%+，是「廉价」的直接来源）
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;

var sb = new System.Text.StringBuilder();

// ---------- ① Feng Walk 循环副本 ----------
string srcPath = "Assets/Char_Feng/Animation/Walk.anim";
string dstPath = "Assets/_Project/Animations/Feng_Walk_Loop.anim";

var src = AssetDatabase.LoadAssetAtPath<AnimationClip>(srcPath);
if (src == null) { Debug.LogError("找不到 " + srcPath); return sb.ToString(); }

if (AssetDatabase.LoadAssetAtPath<AnimationClip>(dstPath) == null)
{
    var copy = UnityEngine.Object.Instantiate(src);
    copy.name = "Feng_Walk_Loop";
    var st = AnimationUtility.GetAnimationClipSettings(copy);
    st.loopTime = true;
    st.loopBlend = false;
    AnimationUtility.SetAnimationClipSettings(copy, st);
    AssetDatabase.CreateAsset(copy, dstPath);
    AssetDatabase.SaveAssets();
    AssetDatabase.Refresh();
    sb.AppendLine("① 生成循环副本 " + dstPath);
}
else sb.AppendLine("① 循环副本已存在，跳过");

var walkLoop = AssetDatabase.LoadAssetAtPath<AnimationClip>(dstPath);
if (walkLoop == null) { Debug.LogError("副本创建失败"); return sb.ToString(); }
sb.AppendLine("   源 loopTime=" + AnimationUtility.GetAnimationClipSettings(src).loopTime
    + "  → 副本 loopTime=" + AnimationUtility.GetAnimationClipSettings(walkLoop).loopTime
    + "  len=" + walkLoop.length.ToString("F3"));

// ---------- ②③ 控制器 ----------
var ac = AssetDatabase.LoadAssetAtPath<AnimatorController>("Assets/_Project/Animations/Player.controller");
if (ac == null) { Debug.LogError("找不到控制器"); return sb.ToString(); }

sb.AppendLine();
sb.AppendLine("②③ 控制器改动：");
foreach (var cs in ac.layers[0].stateMachine.states)
{
    if (cs.state.name == "Walk")
    {
        cs.state.motion = walkLoop;
        sb.AppendLine("   Walk     → " + walkLoop.name);
    }
    else if (cs.state.name == "Atk1Rec" || cs.state.name == "Atk2Rec")
    {
        float before = cs.state.speed;
        cs.state.speed = 1.70f;
        sb.AppendLine("   " + cs.state.name.PadRight(8) + " speed " + before.ToString("F2") + " → 1.70");
    }
}

EditorUtility.SetDirty(ac);
AssetDatabase.SaveAssets();
AssetDatabase.Refresh();

// ---------- 复核 ----------
sb.AppendLine();
sb.AppendLine("---- 复核（重新读盘）----");
var re = AssetDatabase.LoadAssetAtPath<AnimatorController>("Assets/_Project/Animations/Player.controller");
foreach (var cs in re.layers[0].stateMachine.states)
{
    var c = cs.state.motion as AnimationClip;
    sb.AppendLine("  " + cs.state.name.PadRight(10) + " speed=" + cs.state.speed.ToString("F2")
        + "  " + (c != null ? c.name.PadRight(26) + " loop=" + c.isLooping : "<null>"));
}
return sb.ToString();
