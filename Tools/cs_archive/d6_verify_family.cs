using System;
using System.Text;
using UnityEngine;

// d6：验证「材质族」改动真的进了已加载的 Assembly（编译新鲜度）。
public class d6_probe : MonoBehaviour
{
    void Start()
    {
        var sb = new StringBuilder();
        sb.AppendLine("========== d6 材质族改动编译新鲜度 ==========");

        var t = Type.GetType("InkWash.UI.InkStylePanel, Assembly-CSharp");
        if (t == null)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                t = asm.GetType("InkWash.UI.InkStylePanel");
                if (t != null) { sb.AppendLine("找到于 Assembly: " + asm.GetName().Name); break; }
            }
        }
        if (t == null)
        {
            sb.AppendLine("✗ 找不到 InkStylePanel 类型");
            Debug.Log(sb.ToString());
            return;
        }
        sb.AppendLine("类型 = " + t.AssemblyQualifiedName);

        string[] need = { "FamilyCount", "EditingFamilyName", "EditFamily", "IndexOfFamily" };
        foreach (var n in need)
        {
            var p = t.GetProperty(n);
            var m = t.GetMethod(n);
            bool ok = (p != null) || (m != null);
            sb.AppendLine((ok ? "✓" : "✗") + " " + n + (p != null ? " (属性)" : (m != null ? " (方法)" : " 缺失")));
        }

        var oldSync = t.GetMethod("Sync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance,
                                  null, new Type[] { typeof(int), typeof(bool) }, null);
        sb.AppendLine((oldSync == null ? "✓" : "✗") + " 旧 Sync(int,bool) 已移除");

        var panels = UnityEngine.Object.FindObjectsOfType(t);
        sb.AppendLine("场上 InkStylePanel 数量 = " + panels.Length);
        foreach (var p in panels)
        {
            var fc = t.GetProperty("FamilyCount");
            var efn = t.GetProperty("EditingFamilyName");
            var rc = t.GetProperty("RegisteredCount");
            sb.AppendLine("  " + ((Component)p).gameObject.name
                + "  RegisteredCount=" + (rc != null ? rc.GetValue(p).ToString() : "?")
                + "  FamilyCount=" + (fc != null ? fc.GetValue(p).ToString() : "?")
                + "  Editing=" + (efn != null ? "[" + efn.GetValue(p) + "]" : "?"));
        }
        Debug.Log(sb.ToString());
    }
}

var go6 = new GameObject("D6_Probe");
go6.AddComponent<d6_probe>();
return "D6_STARTED";
