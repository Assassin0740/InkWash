// 探针：列出 Assembly-CSharp 的真实类型 + 查找 asmdef + 全程序集搜索
var sb = new System.Text.StringBuilder();

sb.AppendLine("=== 全部已加载程序集里含 'PlaytestHarness' 的类型 ===");
bool found = false;
foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies())
{
    System.Type[] types;
    try { types = a.GetTypes(); } catch { continue; }
    foreach (var t in types)
    {
        if (t.FullName != null && t.FullName.Contains("PlaytestHarness"))
        { sb.AppendLine("   [" + a.GetName().Name + "] " + t.FullName); found = true; }
    }
}
if (!found) sb.AppendLine("   (任何程序集里都没有)");

sb.AppendLine();
sb.AppendLine("=== Assembly-CSharp 的全部类型 ===");
foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies())
{
    if (a.GetName().Name != "Assembly-CSharp") continue;
    System.Type[] types;
    try { types = a.GetTypes(); } catch (System.Exception e) { sb.AppendLine("   GetTypes 异常: " + e.Message); continue; }
    sb.AppendLine("   共 " + types.Length + " 个：");
    foreach (var t in types) sb.AppendLine("      " + t.FullName);
}

sb.AppendLine();
sb.AppendLine("=== 所有 Assembly-CSharp* 程序集 ===");
foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies())
{
    string n = a.GetName().Name;
    if (n.StartsWith("Assembly-CSharp"))
    {
        int c = -1;
        try { c = a.GetTypes().Length; } catch { }
        sb.AppendLine("   " + n + "  类型数=" + c + "  位置=" + (a.Location ?? "(null)"));
    }
}

sb.AppendLine();
sb.AppendLine("=== Assets 下的 asmdef ===");
var gs = UnityEditor.AssetDatabase.FindAssets("t:AssemblyDefinitionAsset");
if (gs.Length == 0) sb.AppendLine("   (无)");
foreach (var g in gs) sb.AppendLine("   " + UnityEditor.AssetDatabase.GUIDToAssetPath(g));

sb.AppendLine();
sb.AppendLine("=== PlaytestHarness.cs 的导入器信息 ===");
string p = "Assets/_Project/Scripts/Utils/PlaytestHarness.cs";
var imp = UnityEditor.AssetImporter.GetAtPath(p);
sb.AppendLine("   importer: " + (imp == null ? "null!" : imp.GetType().Name));
sb.AppendLine("   assetPath: " + UnityEditor.AssetDatabase.GetAssetPath(imp));
var ma = UnityEditor.AssetDatabase.GetMainAssetTypeAtPath(p);
sb.AppendLine("   主资产类型: " + (ma == null ? "null!" : ma.FullName));

return sb.ToString();
