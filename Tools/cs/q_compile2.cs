// q_compile2.cs —— 编辑态强制重编译（顶层语句版，不需要 --runtime）
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

var sb = new StringBuilder();
string[] paths =
{
    "Assets/_Project/Scripts/Player/CombatStance.cs",
    "Assets/_Project/Scripts/Player/FootIK.cs",
};
foreach (var p in paths)
{
    AssetDatabase.ImportAsset(p, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
    sb.AppendLine("ImportAsset " + p);
}
AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
CompilationPipeline.RequestScriptCompilation();
sb.AppendLine("已请求重编译；isCompiling=" + EditorApplication.isCompiling
    + "  scriptCompilationFailed=" + EditorUtility.scriptCompilationFailed);
File.WriteAllText("Tools/reports/q_compile2.txt", sb.ToString());
return sb.ToString();
