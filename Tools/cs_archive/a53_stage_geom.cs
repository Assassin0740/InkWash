// a53_stage_geom.cs —— 量场地几何：高台多高、NavMesh 有几个高度层
//
// 起因：a52 第二版录像里「场上剩 1 只怪，玩家原地挥空 70 秒」。
// 截图证据：敌人站在右上角的**高台顶面**上，玩家在地面够不到。
//
// 这不是脚本 bug，是 WaveSpawner 的生成点缺陷：
//   spawnRadius = 7.5 m，而 Arena 高台是 ±5 m ⇒ 圆上必然有点落在台面上，
//   `SamplePosition` 会把它们吸到台面。`HasPathTo` 也判"有路"（导航网格连通），
//   于是完全静默。
//
// 本脚本先量清楚：台面相对地面高多少？NavMesh 有几个高度层？
// 有了这个数才能定 `maxSpawnHeightDelta` 到底该取多少。
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

var root = Path.GetDirectoryName(Application.dataPath);
var sb = new StringBuilder();
sb.AppendLine("========== a53 场地几何 ==========");
sb.AppendLine();

// ---- NavMesh 高度分布 ----
var tri = NavMesh.CalculateTriangulation();
sb.AppendLine("NavMesh 顶点 = " + tri.vertices.Length + "  三角 = " + tri.indices.Length / 3);

// 把顶点按 y 聚成层（0.15 m 一档）
var hist = new SortedDictionary<int, int>();
float ymin = float.MaxValue, ymax = float.MinValue;
foreach (var v in tri.vertices)
{
    int bucket = Mathf.RoundToInt(v.y / 0.15f);
    hist[bucket] = (hist.TryGetValue(bucket, out var c) ? c : 0) + 1;
    ymin = Mathf.Min(ymin, v.y);
    ymax = Mathf.Max(ymax, v.y);
}
sb.AppendLine("NavMesh y 范围 [" + ymin.ToString("F3") + ", " + ymax.ToString("F3") + "]");
sb.AppendLine();
sb.AppendLine("---- y 分层直方图（0.15 m 一档） ----");
foreach (var kv in hist)
    sb.AppendLine("  y≈" + (kv.Key * 0.15f).ToString("F2").PadLeft(6) + "  : " + kv.Value + " 个顶点");
sb.AppendLine();

// ---- 扫一条射线看台面 ----
sb.AppendLine("---- 从各点向下打射线（找地表高度） ----");
foreach (var p in new[]
{
    new Vector3(0f, 20f, 0f), new Vector3(0f, 20f, -3f),
    new Vector3(4.5f, 20f, 0f), new Vector3(5.5f, 20f, 0f),
    new Vector3(7f, 20f, 0f), new Vector3(0f, 20f, 7f),
    new Vector3(9f, 20f, 0f), new Vector3(-7.5f, 20f, 0f),
})
{
    RaycastHit hit;
    if (Physics.Raycast(p, Vector3.down, out hit, 60f))
        sb.AppendLine("  " + p.ToString("F1") + " → 命中 " + hit.collider.name
                      + "  y=" + hit.point.y.ToString("F3")
                      + "  距离=" + hit.distance.ToString("F2"));
    else sb.AppendLine("  " + p.ToString("F1") + " → 未命中");
}
sb.AppendLine();

// ---- NavMesh 采样：看同一 XZ 有几个 y ----
sb.AppendLine("---- NavMesh 采样（同 XZ 可能命中多层，取最近） ----");
foreach (var xz in new[]
{
    new Vector3(0f, 0.1f, 0f), new Vector3(4.8f, 0.1f, 0f), new Vector3(5.2f, 0.1f, 0f),
    new Vector3(6.5f, 0.1f, 0f), new Vector3(0f, 0.1f, 6.5f), new Vector3(0f, 0.1f, -6.5f),
})
{
    sb.Append("  " + xz.ToString("F1") + " :");
    for (int i = 0; i < 4; i++)
    {
        NavMeshHit h;
        // 从高处往下采样，逐层剔除以探测多层
        Vector3 probe = new Vector3(xz.x, 6f - i * 1.5f, xz.z);
        if (NavMesh.SamplePosition(probe, out h, 1.0f, NavMesh.AllAreas))
            sb.Append("  [y=" + h.position.y.ToString("F2") + "]");
        else sb.Append("  [miss]");
    }
    sb.AppendLine();
}
sb.AppendLine();

// ---- 场景里 Arena 的包围盒 ----
sb.AppendLine("---- 场景内 Arena / 高台对象 ----");
foreach (var g in UnityEngine.Object.FindObjectsOfType<GameObject>())
{
    if (!g.activeInHierarchy) continue;
    string n = g.name.ToLower();
    if (n.Contains("arena") || n.Contains("platform") || n.Contains("stage") || n.Contains("gate"))
    {
        var r = g.GetComponent<Renderer>();
        string b = "";
        if (r != null)
        {
            var bb = r.bounds;
            b = "  bounds center=" + bb.center.ToString("F2") + " size=" + bb.size.ToString("F2");
        }
        sb.AppendLine("  " + Path.GetFileName(g.name) + "  pos=" + g.transform.position.ToString("F2") + b);
    }
}

File.WriteAllText(Path.Combine(root, "Tools/reports/a53_stage_geom.txt"), sb.ToString());
Debug.Log("[a53]\n" + sb.ToString());
