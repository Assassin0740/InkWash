// 一次性诊断：QFramework 包解析状态 + UIRoot.prefab 组件实际加载情况
var sb = new System.Text.StringBuilder();

// ---- 1) 触发包解析（manifest 被外部修改后，需要显式 Resolve 才会重新拉取）----
UnityEditor.PackageManager.Client.Resolve();
sb.AppendLine("[1] 已发出 Client.Resolve() 请求");

// ---- 2) UIRoot.prefab 里的组件 ----
string path = "Assets/ThirdParty/QFramework/Toolkits/UIKit/Scripts/Resources/UIRoot.prefab";
var root = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);
if (root == null)
{
    sb.AppendLine("[2] 加载失败: " + path);
}
else
{
    sb.AppendLine("[2] UIRoot.prefab 组件清单:");
    foreach (var t in root.GetComponentsInChildren<Transform>(true))
    {
        sb.Append("    " + t.name + " -> ");
        var comps = t.GetComponents<Component>();
        for (int i = 0; i < comps.Length; i++)
        {
            sb.Append(comps[i] == null ? "<Missing>" : comps[i].GetType().Name);
            if (i < comps.Length - 1) sb.Append(", ");
        }
        sb.AppendLine();
    }
}

// ---- 3) UIKit / AudioKit 等程序集是否已可见 ----
sb.AppendLine("[3] 类型可见性:");
sb.AppendLine("    QFramework.AudioKit: " + (System.Type.GetType("QFramework.AudioKit, AudioKit") != null));
sb.AppendLine("    QFramework.AudioKitSettingsModel: " + (System.Type.GetType("QFramework.AudioKitSettingsModel, AudioKit") != null));

return sb.ToString();
