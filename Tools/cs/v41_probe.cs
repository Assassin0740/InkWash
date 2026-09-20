// v41_probe.cs —— 怪物攻击动画诊断：FBX clip 清单 + fileID 匹配 + 运行时实拍
using System.Collections;
using UnityEngine;

IEnumerator Body()
{
    var sb = new System.Text.StringBuilder();
    // ① FBX 内的 AnimationClip 清单
    var clips = UnityEditor.AssetDatabase.LoadAllAssetsAtPath("Assets/ThirdParty/Ziyuan/_fbx/low-poly_orc.fbx");
    sb.AppendLine("== low-poly_orc.fbx 资产清单 ==");
    foreach (var c in clips)
    {
        var ac = c as AnimationClip;
        if (ac != null)
            sb.AppendLine("clip: " + ac.name + "  len=" + ac.length.ToString("F2") + "s");
    }
    // ② MoGuai prefab 上的 animator/controller 引用
    var pre = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
        "Assets/_Project/Prefabs/Enemies/Z_Enemy_MoGuai.prefab");
    var anim = pre != null ? pre.GetComponentInChildren<Animator>() : null;
    sb.AppendLine("prefab Animator = " + (anim != null)
        + " controller = " + (anim != null && anim.runtimeAnimatorController != null ? anim.runtimeAnimatorController.name : "NULL")
        + " avatar = " + (anim != null && anim.avatar != null ? anim.avatar.name : "NULL"));
    Debug.Log("[v41probe]\n" + sb.ToString());
    yield break;
}

return Body();
