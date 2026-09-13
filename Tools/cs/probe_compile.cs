// 探针：确认脚本编译完成，且 PlayerController / PlaytestHarness 两个类型都可用。
var sb = new System.Text.StringBuilder();
sb.AppendLine("isCompiling     = " + UnityEditor.EditorApplication.isCompiling);
sb.AppendLine("isUpdating      = " + UnityEditor.EditorApplication.isUpdating);

string[] wanted = new string[]
{
    "InkWash.Player.PlayerController, Assembly-CSharp",
    "InkWash.Utils.PlaytestHarness, Assembly-CSharp",
};
foreach (string w in wanted)
{
    var t = System.Type.GetType(w);
    if (t == null) { sb.AppendLine("[缺失] " + w); continue; }
    sb.AppendLine("[就绪] " + t.FullName + "  (程序集 " + t.Assembly.GetName().Name + ")");
}

// PlayerController 的公开成员（确认 _actualVelocity / IsGrounded / 注入入口都在）
var pc = System.Type.GetType("InkWash.Player.PlayerController, Assembly-CSharp");
if (pc != null)
{
    foreach (var p in pc.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
        sb.AppendLine("   prop: " + p.Name + " : " + p.PropertyType.Name);
    foreach (var m in pc.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly))
        sb.AppendLine("   method: " + m.Name);
}

sb.AppendLine("已加载程序集数量: " + System.AppDomain.CurrentDomain.GetAssemblies().Length);
return sb.ToString();
