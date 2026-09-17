// a59_clean_flats.cs —— 删掉所有测试用的共面地板，消除地面闪烁
//
// a58 定位（用户报"地面一直在闪烁"）：
//   ★ A48_Ground  ⇄  Environment/Ground
//         顶面 y 差 = 0.0000 m   XZ 重叠 = 100%   ← 教科书级 z-fighting
//
// A48_Ground 是我在 a48（盘旋升空验证）里建的保底地板（80×80，顶面 y=0），
// 与场景自带的 Environment/Ground（44×44，顶面 y=0）**完全共面**。
// 后续几次清理脚本只匹配了 `A49_ / A52_ / A54_ / A56_ / A57_` 前缀，
// **漏了 `A48_`** ⇒ 它一直留在场景里闪。
//
// 本脚本：
//   ① 删除所有 A*_Ground（我用过的临时地板，一律不留）
//   ② 重扫共面对，确认闪烁源归零
//   ③ 顺手把之前几轮遗留的临时相机焦点对象也清掉
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

var root = Path.GetDirectoryName(Application.dataPath);
var sb = new StringBuilder();
sb.AppendLine("========== a59 清理共面地板 ==========");
sb.AppendLine();

// ---- ① 删除所有临时地板 ----
var toKill = new List<GameObject>();
foreach (var g in UnityEngine.Object.FindObjectsOfType<GameObject>())
{
    if (g == null) continue;
    string n = g.name;
    // 我建过的临时物命名规律：A<数字>_ 开头
    if (System.Text.RegularExpressions.Regex.IsMatch(n, @"^A\d+_"))
        toKill.Add(g);
}
sb.AppendLine("待删除的临时物（A<数字>_ 前缀）共 " + toKill.Count + " 个：");
foreach (var g in toKill)
{
    var r = g.GetComponent<Renderer>();
    sb.AppendLine("  " + Hier(g.transform)
                  + (r != null ? "  y[" + r.bounds.min.y.ToString("F3") + ", " + r.bounds.max.y.ToString("F3") + "]" : ""));
}
foreach (var g in toKill) UnityEngine.Object.DestroyImmediate(g);
sb.AppendLine();

// ---- ② 重扫共面对 ----
sb.AppendLine("---- 清理后重扫「水平大平面」 ----");
var flats = new List<Renderer>();
foreach (var r in UnityEngine.Object.FindObjectsOfType<Renderer>())
{
    if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
    var b = r.bounds;
    if (b.size.y < 1.5f && b.size.x > 5f && b.size.z > 5f && b.min.y < 2.0f) flats.Add(r);
}
foreach (var r in flats.OrderBy(x => x.bounds.min.y))
{
    var b = r.bounds;
    sb.AppendLine(string.Format("  {0,-26} y[{1,7:F3}, {2,7:F3}]  size=({3,6:F2},{4,6:F2},{5,6:F2})  材质={6}",
        Hier(r.transform), b.min.y, b.max.y, b.size.x, b.size.y, b.size.z,
        r.sharedMaterial != null ? r.sharedMaterial.name : "无"));
}
sb.AppendLine();

sb.AppendLine("---- 共面对（顶面差 < 0.02 m 且 XZ 重叠 > 50%） ----");
int pairs = 0;
for (int i = 0; i < flats.Count; i++)
    for (int j = i + 1; j < flats.Count; j++)
    {
        var a = flats[i].bounds; var c = flats[j].bounds;
        float dz = Mathf.Abs(a.max.y - c.max.y);
        if (dz > 0.02f) continue;
        float ox = Mathf.Min(a.max.x, c.max.x) - Mathf.Max(a.min.x, c.min.x);
        float oz = Mathf.Min(a.max.z, c.max.z) - Mathf.Max(a.min.z, c.min.z);
        if (ox <= 0f || oz <= 0f) continue;
        float ov = (ox * oz) / Mathf.Min(a.size.x * a.size.z, c.size.x * c.size.z);
        if (ov < 0.5f) continue;
        pairs++;
        sb.AppendLine("  ★ " + Hier(flats[i].transform) + "  ⇄  " + Hier(flats[j].transform)
                      + "   顶面差=" + dz.ToString("F4") + " m  重叠=" + (ov * 100f).ToString("F0") + "%");
    }
sb.AppendLine("  ⇒ 共 " + pairs + " 对" + (pairs == 0 ? "   ✓ 闪烁源已清除" : "   ★ 仍有共面"));
sb.AppendLine();

// ---- ③ 保存场景 ----
UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
    UnityEngine.SceneManagement.SceneManager.GetActiveScene());
UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes();
sb.AppendLine("场景已保存");

File.WriteAllText(Path.Combine(root, "Tools/reports/a59_clean_flats.txt"), sb.ToString());
Debug.Log("[a59]\n" + sb.ToString());

string Hier(Transform t)
{
    var s = t.name; var p = t.parent; int g2 = 0;
    while (p != null && g2++ < 12) { s = p.name + "/" + s; p = p.parent; }
    return s;
}
