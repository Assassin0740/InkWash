var sb = new System.Text.StringBuilder();
sb.AppendLine("isPlaying   = " + UnityEditor.EditorApplication.isPlaying);
sb.AppendLine("isCompiling = " + UnityEditor.EditorApplication.isCompiling);
sb.AppendLine("isUpdating  = " + UnityEditor.EditorApplication.isUpdating);
// 触发一次资源刷新，让改过的 ActionShowcase.cs 进入编译队列
UnityEditor.AssetDatabase.Refresh(UnityEditor.ImportAssetOptions.ForceUpdate);
sb.AppendLine("refresh 之后 isCompiling = " + UnityEditor.EditorApplication.isCompiling);
return sb.ToString();
