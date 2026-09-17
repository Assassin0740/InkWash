// s6_forcecompile.cs —— 本轮（S6 打磨）改动文件的强制重编
//
// 坑（必须照抄这里的顺序）：桥送脚本时 Unity 的 DLL 时间戳可能早于源文件，
// 单靠 AssetDatabase.Refresh 不一定触发重编；而 Console「什么都没有」看起来像
// 「编译通过」，其实是**根本没编**。三重保险：ImportAsset(ForceUpdate)
// → Refresh(ForceUpdate) → RequestScriptCompilation，然后再等编译真正结束。
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEditor.Compilation;

IEnumerator Body()
{
    var sb = new StringBuilder();
    string[] paths = {
        // 本轮改动的 4 个脚本
        "Assets/_Project/Scripts/Camera/ThirdPersonCamera.cs",
        "Assets/_Project/Scripts/Roguelike/RunManager.cs",
        "Assets/_Project/Scripts/Player/PlayerController.cs",
        "Assets/_Project/Scripts/Effects/SwordVfx.cs",
        // 本轮改/加的 3 个 shader
        "Assets/_Project/Art/Shaders/InkEdge.shader",
        "Assets/_Project/Art/Shaders/InkSurface.shader",
        "Assets/_Project/Art/Shaders/InkSky.shader",
    };

    yield return null;

    foreach (var p in paths)
    {
        if (AssetDatabase.LoadAssetAtPath<Object>(p) == null) sb.AppendLine("[缺失] " + p);
        AssetDatabase.ImportAsset(p, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
    }
    AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
    CompilationPipeline.RequestScriptCompilation();
    sb.AppendLine("[forcecompile] 已请求重编");

    // 等编译真正结束（最多 60 s）
    float t = Time.realtimeSinceStartup;
    while (EditorApplication.isCompiling && Time.realtimeSinceStartup - t < 60f) yield return null;
    while (!EditorApplication.isCompiling && Time.realtimeSinceStartup - t < 1.5f) yield return null;

    sb.AppendLine("scriptCompilationFailed = " + EditorUtility.scriptCompilationFailed);
    sb.AppendLine("isCompiling = " + EditorApplication.isCompiling);

    // ★ 只报「失败标志」不够：编译失败时 Unity 不卸载上一次成功的 Assembly，
    //   类型照样查得到。所以必须再比对 **DLL 写入时间 vs 最新源文件时间**。
    string root = System.IO.Path.GetDirectoryName(Application.dataPath);
    string dll = System.IO.Path.Combine(root, "Library/ScriptAssemblies/Assembly-CSharp.dll");
    var newest = default(System.IO.FileInfo);
    foreach (var f in System.IO.Directory.GetFiles(
                 System.IO.Path.Combine(Application.dataPath, "_Project"), "*.cs",
                 System.IO.SearchOption.AllDirectories))
    {
        var fi = new System.IO.FileInfo(f);
        if (newest == null || fi.LastWriteTime > newest.LastWriteTime) newest = fi;
    }
    if (System.IO.File.Exists(dll) && newest != null)
    {
        var dt = System.IO.File.GetLastWriteTime(dll);
        sb.AppendLine("DLL   写入 = " + dt.ToString("HH:mm:ss"));
        sb.AppendLine("最新源   = " + newest.LastWriteTime.ToString("HH:mm:ss") + "   " + newest.Name);
        sb.AppendLine("判定     = " + (newest.LastWriteTime > dt
            ? "**过期（DLL 比源旧 ⇒ 类型可见性不可信）**" : "最新"));
    }

    sb.AppendLine("着色器：");
    foreach (var sn in new[] {
        "InkWash/InkCharacter", "InkWash/InkSurface", "InkWash/InkSky",
        "Hidden/InkWash/InkEdge", "Hidden/InkWash/InkPaper",
        "Hidden/InkWash/InkBloom", "InkWash/InkSplash" })
    {
        var sh = Shader.Find(sn);
        sb.AppendLine("  " + sn + " → " + (sh != null ? "OK  pass=" + sh.passCount
                                                  : "**找不到（没编译出来？）**"));
    }

    System.IO.File.WriteAllText(System.IO.Path.Combine(root, "Tools/reports/s6_compile.txt"), sb.ToString());
    Debug.Log("[s6_forcecompile] done  failed=" + EditorUtility.scriptCompilationFailed);
    yield return null;
}

return Body();
