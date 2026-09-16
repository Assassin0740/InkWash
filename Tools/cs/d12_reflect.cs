using System;
using System.Reflection;
using System.Text;
using UnityEngine;

var sb = new StringBuilder();
sb.AppendLine("========== d12 EnemyDragon 成员反射检查 ==========");
Type t = null;
foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
{
    t = asm.GetType("InkWash.Enemies.EnemyDragon");
    if (t != null) { sb.AppendLine("Assembly = " + asm.GetName().Name); break; }
}
if (t == null) { sb.AppendLine("✗ 找不到类型"); Debug.Log(sb.ToString()); return "D12_NO_TYPE"; }

sb.AppendLine("");
sb.AppendLine("── 属性 ──");
foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
    sb.AppendLine("  " + p.PropertyType.Name + " " + p.Name);

sb.AppendLine("");
sb.AppendLine("── 关键字段 ──");
foreach (var n in new[] { "aerialLoop", "diveTellDuration", "circleCooldownMin", "phase2AtRatio", "_airFrames", "_dive" })
{
    var f = t.GetField(n, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
    sb.AppendLine("  " + (f != null ? "✓" : "✗") + " " + n);
}

sb.AppendLine("");
sb.AppendLine("── 方法 ──");
foreach (var n in new[] { "TickChase", "TickCircling", "DoDiveBite", "DoDiveTravel", "ComputeDiveTarget" })
{
    var m = t.GetMethod(n, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
    sb.AppendLine("  " + (m != null ? "✓" : "✗") + " " + n);
}
Debug.Log(sb.ToString());
return "D12_DONE";
