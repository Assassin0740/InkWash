// a47_reset_stage.cs —— 把试玩台从"掉落"状态救回来 + 读 WaveSpawner 序列化真值
//
// a46 证实：玩家 y = -628158，两条残留龙的 y 也在 -53 万 / -51 万。
// NavMesh 数据本身完好（235 顶点 / 95 三角、5 个采样点全命中）
// ⇒ 这是「运行中途掉落」的累积状态，不是导航数据问题。
//
// 这一轮动作：
//   ① 强制停 Play
//   ② 销毁所有测试残留（_Test / y 绝对值 > 100）
//   ③ 重开 Main 场景（从盘上读，清掉脏状态）
//   ④ 读 WaveSpawner 的序列化真值（spawnCenter / spawnRadius / waves 表）
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

var root = Path.GetDirectoryName(Application.dataPath);
var sb = new StringBuilder();
sb.AppendLine("========== a47 试玩台复位 ==========");

if (EditorApplication.isPlaying)
{
    sb.AppendLine("当前在 Play 中 ⇒ 停止");
    EditorApplication.isPlaying = false;
}
else sb.AppendLine("当前不在 Play");

int killed = 0;
foreach (var e in UnityEngine.Object.FindObjectsOfType<InkWash.Enemies.EnemyBase>())
{
    if (e == null) continue;
    if (e.name.Contains("_Test") || Mathf.Abs(e.transform.position.y) > 100f)
    {
        sb.AppendLine("  销毁残留: " + e.name + " y=" + e.transform.position.y.ToString("F1"));
        UnityEngine.Object.DestroyImmediate(e.gameObject);
        killed++;
    }
}
sb.AppendLine("销毁残留 = " + killed);

EditorSceneManager.OpenScene("Assets/_Project/Scenes/Main.unity", OpenSceneMode.Single);
var sc = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
sb.AppendLine("重开场景 = " + sc.name + "  isDirty=" + sc.isDirty);
sb.AppendLine();

// ---- 玩家 ----
GameObject player = null;
foreach (var g in UnityEngine.Object.FindObjectsOfType<GameObject>())
    if (g.CompareTag("Player")) { player = g; break; }
if (player != null)
    sb.AppendLine("玩家 = " + player.name + " pos=" + player.transform.position.ToString("F3"));
else sb.AppendLine("★ 找不到 Player");

var anchor = GameObject.Find("StageAnchor");
sb.AppendLine("StageAnchor = " + (anchor != null ? anchor.transform.position.ToString("F3") : "无"));
sb.AppendLine("场景内敌人数 = " + UnityEngine.Object.FindObjectsOfType<InkWash.Enemies.EnemyBase>().Length);
sb.AppendLine();

// ---- ★ WaveSpawner 序列化真值 ----
sb.AppendLine("---- WaveSpawner 序列化真值 ----");
var ws = UnityEngine.Object.FindObjectOfType<InkWash.Enemies.WaveSpawner>();
if (ws == null) sb.AppendLine("★ 找不到 WaveSpawner");
else
{
    sb.AppendLine("  对象 = " + ws.name);
    sb.AppendLine("  spawnCenter = " + ws.spawnCenter.ToString("F3"));
    sb.AppendLine("  spawnRadius = " + ws.spawnRadius.ToString("F2")
                  + "   minDistanceToPlayer = " + ws.minDistanceToPlayer.ToString("F2")
                  + "   spawnAttempts = " + ws.spawnAttempts);
    sb.AppendLine("  spawnPoints = " + (ws.spawnPoints == null ? "null" : ws.spawnPoints.Length + " 个"));
    sb.AppendLine("  autoStart = " + ws.autoStart + "   spawnInterval = " + ws.spawnInterval);
    sb.AppendLine("  波数 = " + (ws.waves == null ? 0 : ws.waves.Length));
    if (ws.waves != null)
        for (int i = 0; i < ws.waves.Length; i++)
        {
            var w = ws.waves[i];
            var one = new StringBuilder();
            one.Append("    [" + i + "] '" + w.label + "' delay=" + w.delayBefore + " → ");
            if (w.entries != null)
                foreach (var e in w.entries)
                    one.Append((e.prefab != null ? e.prefab.name : "★null") + " x" + e.count + "  ");
            sb.AppendLine(one.ToString());
        }
}
sb.AppendLine();

var tri = NavMesh.CalculateTriangulation();
sb.AppendLine("NavMesh: 顶点=" + tri.vertices.Length + " 三角=" + tri.indices.Length / 3);

AssetDatabase.SaveAssets();
File.WriteAllText(Path.Combine(root, "Tools/reports/a47_reset_stage.txt"), sb.ToString());
Debug.Log("[a47]\n" + sb.ToString());
