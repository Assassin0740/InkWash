// q_compile3.cs —— 编辑态强制重编译（本轮改的是 WeaponHandPose.cs）
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

var sb = new StringBuilder();
string[] paths =
{
    "Assets/_Project/Scripts/Effects/WeaponHandPose.cs",
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
File.WriteAllText("Tools/reports/q_compile3.txt", sb.ToString());
return sb.ToString();
