// v41f_fix.cs —— 编辑器态：①Z_Orc.controller 的 Idle 状态 motion 置为 ANM_IDLE（现在是 Roar/空）
// ②打印 orc 全部 clip 的 fileID（备用）③打印 MoGuai prefab 里 Box001 轴的节点结构
using System.Collections;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;

IEnumerator Body()
{
    var sb = new System.Text.StringBuilder();

    // ① controller Idle 状态修 motion
    var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(
        "Assets/_Project/Animations/Enemies/Z_Orc.controller");
    if (ctrl == null) { sb.AppendLine("FAIL controller"); Debug.Log("[v41f]\n" + sb); yield break; }
    var clips = AssetDatabase.LoadAllAssetsAtPath("Assets/ThirdParty/Ziyuan/_fbx/low-poly_orc.fbx");
    AnimationClip idleClip = null;
    foreach (var c in clips)
    {
        var ac = c as AnimationClip;
        if (ac != null && ac.name.Contains("ANM_IDLE") && !ac.name.Contains("preview")) idleClip = ac;
    }
    sb.AppendLine("idleClip = " + (idleClip != null ? idleClip.name : "NULL"));

    foreach (var layer in ctrl.layers)
    {
        foreach (var st in layer.stateMachine.states)
        {
            if (st.state.name == "Idle")
            {
                sb.AppendLine("Idle 现有 motion = " + (st.state.motion != null ? st.state.motion.name : "NULL"));
                st.state.motion = idleClip;
                sb.AppendLine("Idle motion → " + idleClip.name);
            }
        }
    }
    EditorUtility.SetDirty(ctrl);
    AssetDatabase.SaveAssets();

    // ② clip fileID 备查
    foreach (var c in clips)
    {
        var ac = c as AnimationClip;
        if (ac == null || ac.name.Contains("preview")) continue;
        long fid = (long)Unsupported.GetLocalIdentifierInFileForPersistentObject(ac);
        sb.AppendLine("fileID " + fid + " = " + ac.name);
    }

    // ③ MoGuai 的 Box001 轴节点
    var pre = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Enemies/Z_Enemy_MoGuai.prefab");
    foreach (var t in pre.GetComponentsInChildren<Transform>(true))
    {
        if (t.name.Contains("Box001") || t.name == "Model" || t.name.Contains("Hand"))
            sb.AppendLine("node: '" + t.name + "'  parent='" + (t.parent ? t.parent.name : "NULL") + "'");
    }
    Debug.Log("[v41f]\n" + sb.ToString());
    yield break;
}

return Body();
