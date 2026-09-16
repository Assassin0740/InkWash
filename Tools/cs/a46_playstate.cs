// a46_playstate.cs —— 先确认试玩台的起始状态是否健康
//
// a44 报出 spawn=(12.29, -538886.30, 32.82) —— 玩家自己在 -538886 m 深处。
// 这说明**在我碰龙之前，试玩台就已经不对了**：
// 要么场景没重置，要么 NavMesh 没烘（导致玩家/敌人一起掉落）。
//
// 验收纪律：**先证明台子是好的，再证明龙能打**。否则读数全是假的。
using System.Collections.Generic;
using System.IO;
using System.Text;
using InkWash.Enemies;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

var root = Path.GetDirectoryName(Application.dataPath);
var sb = new StringBuilder();
var sc = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
sb.AppendLine("========== a46 试玩台状态体检 ==========");
sb.AppendLine("场景 = " + sc.name + " (isLoaded=" + sc.isLoaded + ", isDirty=" + sc.isDirty + ")");
sb.AppendLine("Time.timeScale = " + Time.timeScale + "   Time.time = " + Time.time.ToString("F2"));
sb.AppendLine();

// 玩家
GameObject player = null;
foreach (var g in UnityEngine.Object.FindObjectsOfType<GameObject>())
    if (g.CompareTag("Player")) { player = g; break; }
if (player != null)
{
    sb.AppendLine("玩家 = " + player.name + "  pos=" + player.transform.position.ToString("F3")
                  + "  activeInHierarchy=" + player.activeInHierarchy);
    var cc = player.GetComponent<CharacterController>();
    if (cc != null) sb.AppendLine("  CharacterController enabled=" + cc.enabled + " isGrounded=" + cc.isGrounded);
    var rb = player.GetComponent<Rigidbody>();
    if (rb != null) sb.AppendLine("  Rigidbody useGravity=" + rb.useGravity + " isKinematic=" + rb.isKinematic
                                  + " velocity=" + rb.velocity.ToString("F3"));
}
else sb.AppendLine("★ 找不到 Player");

sb.AppendLine();
sb.AppendLine("---- 所有敌人实例 ----");
int cnt = 0;
foreach (var e in UnityEngine.Object.FindObjectsOfType<EnemyBase>())
{
    var ag = e.GetComponent<NavMeshAgent>();
    sb.AppendLine("  " + e.name + " pos=" + e.transform.position.ToString("F2")
                  + " hp=" + e.Health.ToString("F1") + "/" + e.maxHealth
                  + " agent=" + (ag == null ? "无" : (ag.enabled ? (ag.isOnNavMesh ? "onNavMesh" : "★offNavMesh") : "disabled")));
    cnt++;
}
sb.AppendLine("  共 " + cnt + " 个");

sb.AppendLine();
sb.AppendLine("---- NavMesh 数据 ----");
{
    var tri = NavMesh.CalculateTriangulation();
    sb.AppendLine("  顶点=" + tri.vertices.Length + " 三角=" + tri.indices.Length / 3);
    if (tri.vertices.Length > 0)
    {
        var mn = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
        var mx = new Vector3(float.MinValue, float.MinValue, float.MinValue);
        foreach (var v in tri.vertices) { mn = Vector3.Min(mn, v); mx = Vector3.Max(mx, v); }
        sb.AppendLine("  NavMesh 范围 [" + mn.ToString("F2") + "] ~ [" + mx.ToString("F2") + "]");
    }
    else sb.AppendLine("  ★ NavMesh 为空！没有烘导航网格 ⇒ 所有 NavMeshAgent 都建不起来");
}

sb.AppendLine();
sb.AppendLine("---- 从几个关键点采样 NavMesh ----");
foreach (var p in new[]
{
    new Vector3(0f, 0.05f, -8f),
    new Vector3(0f, 0.5f, 0f),
    new Vector3(0f, 0.3f, 6f),
    new Vector3(5f, 0.3f, 5f),
    new Vector3(20f, 0.5f, 0f),
})
{
    NavMeshHit h;
    bool ok = NavMesh.SamplePosition(p, out h, 12f, NavMesh.AllAreas);
    sb.AppendLine("  " + p.ToString("F1") + " → " + (ok ? "命中 " + h.position.ToString("F2") : "★ 12 m 内无 NavMesh"));
}

sb.AppendLine();
sb.AppendLine("---- StageAnchor / PlaytestHarness ----");
var anchor = GameObject.Find("StageAnchor");
sb.AppendLine("  StageAnchor = " + (anchor != null ? anchor.transform.position.ToString("F3") : "无"));

File.WriteAllText(Path.Combine(root, "Tools/reports/a46_playstate.txt"), sb.ToString());
Debug.Log("[a46]\n" + sb.ToString());
