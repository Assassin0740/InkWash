// v42d: ①Dash 态 motion -> falling to roll（按 dashDuration=0.55s 配速）
// ②新增 Cast 态（sword and shield casting）+ Cast 触发器 + AnyState 进/出
using System.Collections;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;

IEnumerator Body()
{
    var sb = new System.Text.StringBuilder();
    System.Func<string, AnimationClip> loadClip = fbxPath =>
    {
        foreach (var a in AssetDatabase.LoadAllAssetsAtPath(fbxPath))
        {
            var c = a as AnimationClip;
            if (c != null && !c.name.Contains("preview")) return c;
        }
        return null;
    };

    var rollClip = loadClip("Assets/Mixamo/falling to roll.fbx");
    var castClip = loadClip("Assets/Mixamo/sword and shield casting.fbx");
    sb.AppendLine("[v42d] roll=" + (rollClip ? rollClip.name + " len=" + rollClip.length.ToString("F2") : "null")
        + "  cast=" + (castClip ? castClip.name + " len=" + castClip.length.ToString("F2") : "null"));

    var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>("Assets/_Project/Animations/Player.controller");
    if (ctrl == null) { sb.AppendLine("[v42d] controller NOT FOUND"); Debug.Log("[v42d]\n" + sb); yield break; }
    var sm = ctrl.layers[0].stateMachine;

    AnimatorState dashState = null, castState = null, idleState = null;
    foreach (var st in sm.states)
    {
        if (st.state.name == "Dash") dashState = st.state;
        if (st.state.name == "Cast") castState = st.state;
        if (st.state.name == "Idle") idleState = st.state;
    }

    // ① Dash -> 翻滚（配速：翻滚完整放完 ≈ 冲刺时长 0.55s）
    if (dashState != null && rollClip != null)
    {
        dashState.motion = rollClip;
        dashState.speed = rollClip.length / 0.55f;
        sb.AppendLine("[v42d] Dash -> roll, speed=" + dashState.speed.ToString("F2"));
    }

    // ② Cast 态
    if (castState == null && castClip != null)
    {
        castState = sm.AddState("Cast", new Vector3(320, 200, 0));
        castState.motion = castClip;
        castState.speed = castClip.length / 0.8f;   // 施法演出压到 0.8s（技能本身瞬发）

        bool hasParam = false;
        foreach (var p in ctrl.parameters) if (p.name == "Cast") { hasParam = true; break; }
        if (!hasParam) ctrl.AddParameter("Cast", AnimatorControllerParameterType.Trigger);

        var any = sm.AddAnyStateTransition(castState);
        any.duration = 0.05f;
        any.hasExitTime = false;
        any.AddCondition(AnimatorConditionMode.If, 0, "Cast");

        var back = castState.AddTransition(idleState);
        back.hasExitTime = true;
        back.exitTime = 0.95f;
        back.duration = 0.12f;
        sb.AppendLine("[v42d] Cast state created");
    }
    else sb.AppendLine("[v42d] Cast exists=" + (castState != null));

    EditorUtility.SetDirty(ctrl);
    AssetDatabase.SaveAssets();
    Debug.Log("[v42d]\n" + sb);
    yield break;
}

return Body();
