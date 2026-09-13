// 强制重新编译脚本：先用 ImportAsset(ForceUpdate) 把文件标记为变更，再请求编译。
// 触发域重载后 TCP 连接会断，客户端可能收不到响应 —— 属预期，随后重新探测即可。
var sb = new System.Text.StringBuilder();
string[] paths = new string[]
{
    // 本轮新建 / 改写的自有脚本
    "Assets/_Project/Scripts/Player/PlayerController.cs",
    "Assets/_Project/Scripts/Camera/ThirdPersonCamera.cs",
    "Assets/_Project/Scripts/Effects/SwordVfx.cs",
    "Assets/_Project/Scripts/Utils/PlaytestHarness.cs",
    "Assets/_Project/Scripts/Core/GameAudio.cs",
    "Assets/_Project/Scripts/Core/AudioDirector.cs",
    "Assets/_Project/Scripts/Core/AudioKitBootstrap.cs",
    "Assets/_Project/Scripts/Player/FootIK.cs",
    // 修正过 YAML 的第三方预制体，需要强制重导入才会重新解析
    "Assets/ThirdParty/QFramework/Toolkits/UIKit/Scripts/Resources/UIRoot.prefab",
    // 自研水墨刀光 Shader
    "Assets/_Project/Shaders/InkSlash.shader",
    // 新引入的 UAL1 动画库（Humanoid 重定向）
    "Assets/ThirdParty/Quaternius/UniversalAnimationLibrary/Unity/AnimationLibrary_Unity_Standard.fbx",
};

sb.AppendLine("导入前 isCompiling = " + UnityEditor.EditorApplication.isCompiling);
foreach (string p in paths)
{
    if (UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(p) == null
        && !System.IO.File.Exists(p))
    {
        sb.AppendLine("[跳过] 不存在: " + p);
        continue;
    }
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
