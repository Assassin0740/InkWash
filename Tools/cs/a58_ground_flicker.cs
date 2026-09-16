// a58_ground_flicker.cs —— 地面闪烁诊断（z-fighting）
//
// 症状：用户报"地面一直在闪烁，不知道是模型重叠了还是材质问题"。
//
// 两个候选根因，一次查清：
//   ① **我的测试地板叠层** —— 验收脚本一路建了 A49/A52/A54/A56/A57_Ground，
//      全是同一个位置 (0,-0.6,0) 和同样的 120~180 缩放，多层共面 ⇒ 典型 z-fighting。
//      这些是**临时物**，本该每次清掉，但名字前缀不同（A49_/A52_/...），清理时漏了。
//   ② **场景原有地面自身的重叠** —— Ground / Arena / 高台 / 瓦檐 之间是否共面。
//
// 判据：把所有渲染器的世界包围盒按"是否与地面同高"分组，列表里凡是
//       y 范围互相重叠、又都在同一 XZ 区域的大平面，就是闪烁源。
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

var root = Path.GetDirectoryName(Application.dataPath);
var sb = new StringBuilder();
sb.AppendLine("========== a58 地面闪烁诊断 ==========");
sb.AppendLine();

// ---- ① 找出所有"水平大平面"（薄、宽、近地） ----
var flats = new List<Renderer>();
foreach (var r in UnityEngine.Object.FindObjectsOfType<Renderer>())
{
    if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
    var b = r.bounds;
    bool thin = b.size.y < 1.5f;                    // 薄
    bool wide = b.size.x > 5f && b.size.z > 5f;      // 宽
    bool nearGround = b.min.y < 2.0f;                // 贴地
    if (thin && wide && nearGround) flats.Add(r);
}

sb.AppendLine("---- 候选「水平大平面」共 " + flats.Count + " 个 ----");
foreach (var r in flats.OrderBy(x => x.bounds.min.y))
{
    var b = r.bounds;
    sb.AppendLine(string.Format("  {0,-28} y[{1,7:F3}, {2,7:F3}]  size=({3,6:F2},{4,6:F2},{5,6:F2})  xz中心=({6,7:F2},{7,7:F2})  材质={8}",
        Hier(r.transform), b.min.y, b.max.y, b.size.x, b.size.y, b.size.z,
        b.center.x, b.center.z,
        r.sharedMaterial != null ? r.sharedMaterial.name : "无"));
}
sb.AppendLine();

// ---- ② 两两比对：共面且 XZ 重叠 = 闪烁源 ----
sb.AppendLine("---- 共面对（|y 顶面差| < 0.02 m 且 XZ 重叠 > 50%） ----");
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
        float overlap = (ox * oz) / Mathf.Min(a.size.x * a.size.z, c.size.x * c.size.z);
        if (overlap < 0.5f) continue;

        pairs++;
        sb.AppendLine(string.Format("  ★ {0}  ⇄  {1}", Hier(flats[i].transform), Hier(flats[j].transform)));
        sb.AppendLine(string.Format("        顶面 y 差 = {0:F4} m   XZ 重叠 = {1:F0}%",
            dz, overlap * 100f));
    }
sb.AppendLine("  共 " + pairs + " 对");
sb.AppendLine();

// ---- ③ A5x_Ground 残留统计 ----
sb.AppendLine("---- 我的测试地板残留（A*_Ground） ----");
int mine = 0;
foreach (var r in flats)
{
    string n = r.gameObject.name;
    if (n.StartsWith("A49_") || n.StartsWith("A52_") || n.StartsWith("A54_") || n.StartsWith("A56_") || n.StartsWith("A57_"))
    {
        mine++;
        sb.AppendLine("  ★ " + Hier(r.transform) + "  y[" + r.bounds.min.y.ToString("F3") + ", " + r.bounds.max.y.ToString("F3") + "]"
                      + "  scale=" + r.transform.lossyScale.ToString("F2"));
    }
}
sb.AppendLine("  共 " + mine + " 个测试地板残留");
sb.AppendLine();

// ---- ④ 场景层级里的 Ground 归属 ----
sb.AppendLine("---- 名字含 ground/floor/arena 的对象（含层级） ----");
foreach (var g in UnityEngine.Object.FindObjectsOfType<GameObject>())
{
    string n = g.name.ToLower();
    if (n.Contains("ground") || n.Contains("floor") || n.Contains("arena"))
    {
        var r = g.GetComponent<Renderer>();
        sb.AppendLine("  " + Hier(g.transform)
                      + (r != null ? "  y[" + r.bounds.min.y.ToString("F3") + ", " + r.bounds.max.y.ToString("F3") + "]" : "  (无渲染器)"));
    }
}

File.WriteAllText(Path.Combine(root, "Tools/reports/a58_ground_flicker.txt"), sb.ToString());
Debug.Log("[a58]\n" + sb.ToString());

string Hier(Transform t)
{
    var s = t.name; var p = t.parent; int g2 = 0;
    while (p != null && g2++ < 12) { s = p.name + "/" + s; p = p.parent; }
    return s;
}
