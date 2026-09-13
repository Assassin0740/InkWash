// 强制重新编译脚本：先用 ImportAsset(ForceUpdate) 把文件标记为变更，再请求编译。
// 触发域重载后 TCP 连接会断，客户端可能收不到响应 —— 属预期，随后重新探测即可。
var sb = new System.Text.StringBuilder();
string[] paths = new string[]
{
    "Assets/_Project/Scripts/Player/PlayerController.cs",
    "Assets/_Project/Scripts/Utils/PlaytestHarness.cs",
};

sb.AppendLine("导入前 isCompiling = " + UnityEditor.EditorApplication.isCompiling);
foreach (string p in paths)
{
    UnityEditor.AssetDatabase.ImportAsset(p,
        UnityEditor.ImportAssetOptions.ForceUpdate
        | UnityEditor.ImportAssetOptions.ForceSynchronousImport);
    sb.AppendLine("已强制导入: " + p);
}

UnityEditor.AssetDatabase.Refresh(UnityEditor.ImportAssetOptions.ForceUpdate
    | UnityEditor.ImportAssetOptions.ForceSynchronousImport);
sb.AppendLine("已强制 Refresh");

UnityEditor.Compilation.CompilationPipeline.RequestScriptCompilation();
sb.AppendLine("已请求脚本编译（随后会域重载）");

return sb.ToString();
