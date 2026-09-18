// q_comp2.cs —— 强制把改过的 .cs 真正编进 Assembly-CSharp。
//
// ★ 为什么需要它（09-18 实测踩到）：
//   只调 `AssetDatabase.Refresh(ForceUpdate)` **不一定重编**。Editor.log 里的判据是
//   `CompileScripts: 2.413ms` —— 只有 domain reload、没有真编译，
//   于是"改了字面量但运行时读到的还是旧值"（本次是 `diveSpiralAmp` 2.2 没变成 3.0）。
//   三重保险：ImportAsset(ForceUpdate|ForceSynchronousImport) → Refresh(ForceUpdate)
//             → **CompilationPipeline.RequestScriptCompilation()**。
//   判据仍然只能是 **源文件 mtime vs Assembly-CSharp.dll mtime**，且要看 `CompileScripts` 耗时。
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.Compilation;

IEnumerator Body()
{
    yield return null;

    string[] paths = {
        "Assets/_Project/Scripts/Enemies/EnemyDragon.cs",
    };

    foreach (var p in paths)
    {
        AssetDatabase.ImportAsset(p, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
        Debug.Log("[q_comp2] ImportAsset " + p);
    }

    AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
    CompilationPipeline.RequestScriptCompilation();
    Debug.Log("[q_comp2] 已请求重编（RequestScriptCompilation）");

    yield return null;
}

return Body();
