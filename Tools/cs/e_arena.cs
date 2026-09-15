// e_arena.cs —— 量竞技场实际可用范围（墙体内侧 / 地面尺寸 / 柱子位置），
// 用来给 WaveSpawner 定 spawnCenter / spawnRadius，避免瞎猜。
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;

IEnumerator Body()
{
    var sb = new StringBuilder();
    string projRoot = Path.GetDirectoryName(Application.dataPath);
    var env = GameObject.Find("Environment");
    sb.AppendLine("===== 竞技场测量 =====");
    if (env == null) { sb.AppendLine("找不到 Environment"); }
    else
    {
        foreach (Transform c in env.transform)
        {
            var r = c.GetComponent<Renderer>();
            var col = c.GetComponent<Collider>();
            var b = r != null ? r.bounds : (col != null ? col.bounds : new Bounds());
            sb.AppendLine(string.Format("{0,-12} center={1}  size={2}  pos={3}  scale={4}",
                c.name, F(b.center), F(b.size), F(c.position), F(c.localScale)));
            if (c.name == "Pillars")
                foreach (Transform p in c)
                {
                    var pr = p.GetComponent<Renderer>();
                    sb.AppendLine("    " + p.name + " pos=" + F(p.position) + " size=" + (pr != null ? F(pr.bounds.size) : "-"));
                }
        }
    }

    // 用射线探墙内侧：从中心向四个方向打，看撞到谁
    sb.AppendLine();
    sb.AppendLine("---- 从原点向四方射线（找墙内表面）----");
    foreach (var dir in new Vector3[] { Vector3.forward, Vector3.back, Vector3.right, Vector3.left })
    {
        RaycastHit h;
        if (Physics.Raycast(new Vector3(0f, 1f, 0f), dir, out h, 200f, ~0, QueryTriggerInteraction.Ignore))
            sb.AppendLine("  " + dir + " → " + h.collider.name + "  距离 " + h.distance.ToString("F2")
                          + "  命中点 " + F(h.point));
        else sb.AppendLine("  " + dir + " → 没打到");
    }

    // 导航网格的实际包围盒（敌人能站的区域）
    sb.AppendLine();
    var tri = UnityEngine.AI.NavMesh.CalculateTriangulation();
    if (tri.vertices.Length > 0)
    {
        var mn = tri.vertices[0]; var mx = tri.vertices[0];
        foreach (var v in tri.vertices) { mn = Vector3.Min(mn, v); mx = Vector3.Max(mx, v); }
        sb.AppendLine("导航网格 AABB: min=" + F(mn) + "  max=" + F(mx));
        sb.AppendLine("导航网格中位:" + F((mn + mx) * 0.5f) + "  尺寸 " + F(mx - mn));
        // 从中心向四方采样，看导航网格到哪结束
        sb.AppendLine("---- 沿轴向采样导航网格边界 ----");
        foreach (var dir in new Vector3[] { Vector3.forward, Vector3.back, Vector3.right, Vector3.left })
        {
            float last = 0f;
            for (float d = 0f; d <= 30f; d += 0.25f)
            {
                UnityEngine.AI.NavMeshHit nh;
                if (UnityEngine.AI.NavMesh.SamplePosition(new Vector3(0f, 0.5f, 0f) + dir * d, out nh, 0.4f, UnityEngine.AI.NavMesh.AllAreas))
                    last = d; else break;
            }
            sb.AppendLine("  " + dir + " 方向可站立到 " + last.ToString("F2") + " m");
        }
    }
    else sb.AppendLine("导航网格为空");

    File.WriteAllText(Path.Combine(projRoot, "Tools/reports/e_arena.txt"), sb.ToString());
    Debug.Log("[e_arena] done");
    yield return null;
}

string F(Vector3 v) => "(" + v.x.ToString("F2") + ", " + v.y.ToString("F2") + ", " + v.z.ToString("F2") + ")";

return Body();
