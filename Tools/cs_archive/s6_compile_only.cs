// s6_compile_only.cs —— 只请求一次重编并在下一帧就返回（不做长等待，避免触发桥的
// "C# is performing domain reloading" 守卫把整次调用判失败）。
using System.Collections;
using UnityEngine;
using UnityEditor;
using UnityEditor.Compilation;

IEnumerator Body()
{
    yield return null;
    AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
    CompilationPipeline.RequestScriptCompilation();
    Debug.Log("[s6_compile_only] 已请求重编；isCompiling=" + EditorApplication.isCompiling);
    yield return null;
}

return Body();
