// q_make_loop.cs —— ① 列出控制器全部状态的 motion 引用 ② 给 Feng Idle 生成"循环版"副本
// 为什么要副本：/Assets/Char_Feng/ 不入库（授权原因，靠 install_feng.py 复原），
// 直接改它的 .anim 既不进 git、也会被复原脚本覆盖。副本放在我们自己的 Animations/ 下。
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;

var sb = new System.Text.StringBuilder();

// ---------- ① 控制器全部状态 ----------
sb.AppendLine("================ 控制器状态清单 ================");
var ctrlPath = "Assets/_Project/Animations/Player.controller";
var ac = AssetDatabase.LoadAssetAtPath<AnimatorController>(ctrlPath);
if (ac == null) { sb.AppendLine("找不到 " + ctrlPath); return sb.ToString(); }

foreach (var layer in ac.layers)
{
    sb.AppendLine("---- Layer: " + layer.name + " ----");
    foreach (var cs in layer.stateMachine.states)
    {
        var st = cs.state;
        string p = st.motion != null ? AssetDatabase.GetAssetPath(st.motion) : "<null>";
        string extra = "";
        var clip = st.motion as AnimationClip;
        if (clip != null) extra = "   len=" + clip.length.ToString("F3") + " loop=" + clip.isLooping;
        sb.AppendLine("  " + st.name.PadRight(12) + " speed=" + st.speed.ToString("F2")
            + "  speedParam=" + (st.speedParameterActive ? st.speedParameter : "-").PadRight(12)
            + "  → " + p + extra);
    }
}

// ---------- ② Feng Idle 循环副本 ----------
sb.AppendLine();
sb.AppendLine("================ 生成 Feng Idle 循环副本 ================");
string srcPath = "Assets/Char_Feng/Animation/Idle.anim";
string dstPath = "Assets/_Project/Animations/Feng_Idle_Loop.anim";

var src = AssetDatabase.LoadAssetAtPath<AnimationClip>(srcPath);
if (src == null)
{
    sb.AppendLine("找不到源片段 " + srcPath);
}
else
{
    var st0 = AnimationUtility.GetAnimationClipSettings(src);
    sb.AppendLine("源: " + srcPath + "  length=" + src.length.ToString("F3")
        + "  loopTime=" + st0.loopTime + "  isLooping=" + src.isLooping);

    var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(dstPath);
    if (existing != null)
    {
        sb.AppendLine("副本已存在，跳过创建：" + dstPath
            + "  loopTime=" + AnimationUtility.GetAnimationClipSettings(existing).loopTime);
    }
    else
    {
        var copy = UnityEngine.Object.Instantiate(src);
        copy.name = "Feng_Idle_Loop";

        var st = AnimationUtility.GetAnimationClipSettings(copy);
        st.loopTime = true;
        st.loopBlend = false;
        AnimationUtility.SetAnimationClipSettings(copy, st);

        AssetDatabase.CreateAsset(copy, dstPath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        var chk = AssetDatabase.LoadAssetAtPath<AnimationClip>(dstPath);
        if (chk == null) sb.AppendLine("★ 副本创建失败");
        else
        {
            var stc = AnimationUtility.GetAnimationClipSettings(chk);
            sb.AppendLine("副本: " + dstPath);
            sb.AppendLine("  length=" + chk.length.ToString("F3")
                + "  loopTime=" + stc.loopTime + "  isLooping=" + chk.isLooping);
            sb.AppendLine("  FloatCurve 绑定数=" + AnimationUtility.GetCurveBindings(chk).Length);
            sb.AppendLine("  （源 " + AnimationUtility.GetCurveBindings(src).Length + " 条曲线，两者应一致）");
        }
    }
}

return sb.ToString();
