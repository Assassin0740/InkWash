// 探针：PlaytestHarness 到底在不在编译产物里
var sb = new System.Text.StringBuilder();

// 1) Unity 认不认这个文件
string p = "Assets/_Project/Scripts/Utils/PlaytestHarness.cs";
sb.AppendLine("文件被 Unity 识别: " + (UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(p) != null));
var guid = UnityEditor.AssetDatabase.AssetPathToGUID(p);
sb.AppendLine("GUID: " + (string.IsNullOrEmpty(guid) ? "(无 —— 说明尚未导入)" : guid));
sb.AppendLine();

// 2) Assembly-CSharp 里所有含 Playtest 的类型
sb.AppendLine("=== Assembly-CSharp 中含 'Playtest' 的类型 ===");
bool found = false;
foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies())
{
    if (a.GetName().Name != "Assembly-CSharp") continue;
    foreach (var t in a.GetTypes())
    {
        if (t.FullName.IndexOf("Playtest", System.StringComparison.OrdinalIgnoreCase) >= 0)
        { sb.AppendLine("   " + t.FullName); found = true; }
    }
}
if (!found) sb.AppendLine("   (无)");

sb.AppendLine();
sb.AppendLine("=== Assembly-CSharp 里 InkWash 命名空间下的全部类型 ===");
found = false;
foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies())
{
    if (a.GetName().Name != "Assembly-CSharp") continue;
    foreach (var t in a.GetTypes())
    {
        if (t.FullName != null && t.FullName.StartsWith("InkWash"))
        { sb.AppendLine("   " + t.FullName); found = true; }
    }
}
if (!found) sb.AppendLine("   (无)");

// 3) 有没有编译错误日志残留
sb.AppendLine();
sb.AppendLine("=== 脚本编译相关 ===");
sb.AppendLine("isCompiling=" + UnityEditor.EditorApplication.isCompiling);
sb.AppendLine("Assembly-CSharp 里的类型总数: ");
foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies())
{
    if (a.GetName().Name == "Assembly-CSharp")
    {
        try { sb.AppendLine("   " + a.GetTypes().Length); } catch (System.Exception e) { sb.AppendLine("   err " + e.Message); }
    }
}
return sb.ToString();
