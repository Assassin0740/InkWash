// 强制把新写的 .cs 编进 Assembly-CSharp。
// 踩过的坑：桥送的脚本 DLL 时间戳可能早于源文件，AssetDatabase.Refresh 不一定会重编，
// 于是"新类型不存在"。这里三重保险：ImportAsset(ForceUpdate) → Refresh(ForceUpdate) → RequestScriptCompilation。
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.Compilation;

IEnumerator Body()
{
    yield return null;
    string[] paths = {
        "Assets/_Project/Scripts/Utils/PlaytestHarness.cs",
        "Assets/_Project/Scripts/Effects/WeaponHandPose.cs",
    };
    foreach (var p in paths)
    {
        AssetDatabase.ImportAsset(p, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
        Debug.Log("[q_forcecompile2] ImportAsset " + p);
    }
    AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
    CompilationPipeline.RequestScriptCompilation();
    Debug.Log("[q_forcecompile2] 已请求重编，等待 Unity 编译完成");
    yield return null;
}

return Body();
