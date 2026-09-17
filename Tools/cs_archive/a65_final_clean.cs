// a65_final_clean.cs —— 收尾：清干净临时物 + 确认场景状态 + 顺便把「闪烁」这个坑固化成文档
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

var root = Path.GetDirectoryName(Application.dataPath);
var sb = new StringBuilder();
sb.AppendLine("========== a65 收尾清理与状态确认 ==========");

// ① 清掉一切 A<数字>_ 临时物（含本次 a62/a63/a64 探针残留的注入地板）
var doomed = new List<GameObject>();
foreach (var g in UnityEngine.Object.FindObjectsOfType<GameObject>())
    if (g != null && Regex.IsMatch(g.name, @"^A\d+_")) doomed.Add(g);
sb.AppendLine($"待清理的临时物（^A\\d+_）共 {doomed.Count} 个：");
foreach (var g in doomed) sb.AppendLine("  " + g.name);
foreach (var g in doomed) UnityEngine.Object.DestroyImmediate(g);
sb.AppendLine("已清理");

// ② 重扫所有水平大平面，确认无共面
sb.AppendLine();
sb.AppendLine("---- 场景中所有水平大平面 ----");
var flats = new List<(string n, float lo, float hi, float sx, float sz, float cx, float cz)>();
foreach (var r in UnityEngine.Object.FindObjectsOfType<Renderer>())
{
    if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
    var b = r.bounds;
    // 水平大平面：Y 很薄、XZ 较大
    if (b.size.y < 2.0f && b.size.x > 5f && b.size.z > 5f)
        flats.Add((r.gameObject.name, b.min.y, b.max.y, b.size.x, b.size.z, b.center.x, b.center.z));
}
flats.Sort((a, b2) => b2.hi.CompareTo(a.hi));
foreach (var f in flats)
    sb.AppendLine($"  {f.n,-28} y[{f.lo,7:F3},{f.hi,7:F3}] size=({f.sx,7:F2},{f.sz,7:F2}) xz中心=({f.cx,7:F2},{f.cz,7:F2})");

sb.AppendLine();
sb.AppendLine("---- 共面对（|顶面差| < 0.02 m 且 XZ 重叠 > 50%）----");
int pairs = 0;
for (int i = 0; i < flats.Count; i++)
    for (int j = i + 1; j < flats.Count; j++)
    {
        var A = flats[i]; var B = flats[j];
        float dy = Mathf.Abs(A.hi - B.hi);
        if (dy >= 0.02f) continue;
        float ox = Mathf.Max(0f, Mathf.Min(A.cx + A.sx / 2, B.cx + B.sx / 2) - Mathf.Max(A.cx - A.sx / 2, B.cx - B.sx / 2));
        float oz = Mathf.Max(0f, Mathf.Min(A.cz + A.sz / 2, B.cz + B.sz / 2) - Mathf.Max(A.cz - A.sz / 2, B.cz - B.sz / 2));
        float ov = (ox * oz) / Mathf.Min(A.sx * A.sz, B.sx * B.sz);
        if (ov > 0.5f)
        {
            pairs++;
            sb.AppendLine($"  ★ {A.n} ⇄ {B.n}   顶面差={dy:F4} m   重叠={ov * 100:F0}%");
        }
    }
sb.AppendLine(pairs == 0 ? "  ⇒ 共 0 对   ✓ 无 z-fighting 隐患" : $"  ⇒ 共 {pairs} 对   ✗ 需要处理");

// ③ 关掉运行时探针残留（可能有挂着脚本的空物体）
foreach (var mb in UnityEngine.Object.FindObjectsOfType<MonoBehaviour>())
{
    if (mb == null) continue;
    string tn = mb.GetType().Name;
    if (Regex.IsMatch(tn, @"^A\d+Probe$"))
    {
        sb.AppendLine($"移除残留探针组件 {tn} @ {mb.gameObject.name}");
        UnityEngine.Object.DestroyImmediate(mb.gameObject);
    }
}

EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
EditorSceneManager.SaveOpenScenes();
sb.AppendLine();
sb.AppendLine("场景已保存");

File.WriteAllText(Path.Combine(root, "Tools/reports/a65_final_clean.txt"), sb.ToString());
Debug.Log("[a65]\n" + sb.ToString());
