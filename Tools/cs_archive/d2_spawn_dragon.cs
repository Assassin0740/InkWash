// d2_spawn_dragon.cs —— 生成一条墨龙（好让 d1 探针有东西可查）
//
// 为什么不直接跑 d1：d1 是逐帧探针，需要场上有龙。
// 这里先停掉 WaveSpawner（它有自己的节奏），手工 Instantiate 一条 Z_Enemy_MoLong 在玩家前。
using System.IO;
using System.Text;
using InkWash.Enemies;
using UnityEngine;

var root = Path.GetDirectoryName(Application.dataPath);
var sb = new StringBuilder();
sb.AppendLine("========== d2 生成墨龙供取证 ==========");

// 清掉一切 A*/D* 临时物
foreach (var g in UnityEngine.Object.FindObjectsOfType<GameObject>())
    if (g != null && (g.name.StartsWith("A") && g.name.Length > 1 && char.IsDigit(g.name[1])
                   || g.name.StartsWith("D2_")))
        UnityEngine.Object.DestroyImmediate(g);

var ws = UnityEngine.Object.FindObjectOfType<WaveSpawner>();
if (ws != null) { ws.enabled = false; sb.AppendLine("已停用 WaveSpawner"); }

// 找玩家
GameObject player = null;
foreach (var g in UnityEngine.Object.FindObjectsOfType<GameObject>())
    if (g.CompareTag("Player")) { player = g; break; }
Vector3 ppos = player != null ? player.transform.position : Vector3.zero;
sb.AppendLine("玩家位置 = " + ppos.ToString("F2"));

// 从 WaveSpawner 的波次表里取龙 prefab（不靠字符串找资产）
GameObject dragonPrefab = null;
if (ws != null && ws.waves != null)
{
    foreach (var w in ws.waves)
    {
        if (w.entries == null) continue;
        foreach (var e in w.entries)
        {
            if (e == null || e.prefab == null) continue;
            if (e.prefab.name.Contains("MoLong")) { dragonPrefab = e.prefab; break; }
        }
        if (dragonPrefab != null) break;
    }
}
sb.AppendLine("龙 prefab = " + (dragonPrefab == null ? "<未找到>" : dragonPrefab.name));

if (dragonPrefab != null)
{
    Vector3 pos = ppos + new Vector3(0f, 0.05f, 7f);
    var go = UnityEngine.Object.Instantiate(dragonPrefab, pos, Quaternion.Euler(0, 180, 0));
    go.name = "D2_MoLong";

    // 和 WaveSpawner 同款：生成后补一次水墨化
    InkWash.Rendering.InkMaterialForcer.ForceInk(go, go.name, true);

    sb.AppendLine("已生成 " + go.name + " @ " + pos.ToString("F2"));
    var d = go.GetComponent<EnemyDragon>();
    if (d == null) d = go.GetComponentInChildren<EnemyDragon>();
    sb.AppendLine("EnemyDragon 组件 = " + (d == null ? "<缺失!>" : "OK"));
}

File.WriteAllText(Path.Combine(root, "Tools/reports/d2_spawn_dragon.txt"), sb.ToString());
Debug.Log("[d2]\n" + sb.ToString());
