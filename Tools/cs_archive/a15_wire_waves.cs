// a15_wire_waves.cs —— 把场景里的 WaveSpawner 波次配置换到新敌人 prefab
//
// 目标（用户明确选择「替换现有 KayKit 怪」）：
//   Wave[0] 第一波·墨徒        → Z_Enemy_MoGuai（兽人，近战）x3
//   Wave[1] 第二波·墨徒+墨偶   → Z_Enemy_MoGuai x2 + Z_Enemy_MoGu（不死兵，远程）x2
//   Wave[2] 第三波·墨魇        → Z_Enemy_MoGuai x3 + Z_Enemy_MoShan（山怪，精英）x1
//
// ★ 关键前提：**改的是场景实例的序列化数组，不是 prefab**。
//   WaveSpawner 挂在场景对象 `Enemies` 上（a1_survey 已确认），
//   它的 waves 数组是场景里的序列化数据 ⇒ 必须改场景并标脏存盘。
//
// ★ 同时要**幂等**：本脚本可能被重复执行（调试期常态）。
//   做法 = 每次先记录改前的 prefab 名，改完报对照表，不依赖"当前是什么"。
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

var sb = new StringBuilder();
string root = Path.GetDirectoryName(Application.dataPath);
const string EnemyDir = "Assets/_Project/Prefabs/Enemies";

// 新 prefab（替换后）
var P_MoShan = AssetDatabase.LoadAssetAtPath<GameObject>(EnemyDir + "/Z_Enemy_MoShan.prefab");
var P_MoGuai = AssetDatabase.LoadAssetAtPath<GameObject>(EnemyDir + "/Z_Enemy_MoGuai.prefab");
var P_MoGu   = AssetDatabase.LoadAssetAtPath<GameObject>(EnemyDir + "/Z_Enemy_MoGu.prefab");

var missing = new List<string>();
if (P_MoShan == null) missing.Add("Z_Enemy_MoShan");
if (P_MoGuai == null) missing.Add("Z_Enemy_MoGuai");
if (P_MoGu   == null) missing.Add("Z_Enemy_MoGu");
if (missing.Count > 0)
{
    sb.AppendLine("★ 读不到新 prefab: " + string.Join(", ", missing.ToArray()));
    File.WriteAllText(Path.Combine(root, "Tools/reports/a15_wire_waves.txt"), sb.ToString());
    Debug.LogError("[a15]\n" + sb.ToString());
    return;
}

var scene = EditorSceneManager.GetActiveScene();
sb.AppendLine("========== a15 把波次换到新敌人 ==========");
sb.AppendLine("场景 = " + scene.name + "（path=" + scene.path + "）");
sb.AppendLine();

var spawner = UnityEngine.Object.FindObjectOfType<InkWash.Enemies.WaveSpawner>();
if (spawner == null)
{
    sb.AppendLine("★ 场景里找不到 WaveSpawner");
    File.WriteAllText(Path.Combine(root, "Tools/reports/a15_wire_waves.txt"), sb.ToString());
    Debug.LogError("[a15]\n" + sb.ToString());
    return;
}
sb.AppendLine("WaveSpawner 在对象: " + spawner.gameObject.name
              + "  autoStart=" + spawner.autoStart
              + "  interval=" + spawner.spawnInterval
              + "  center=" + spawner.spawnCenter + "  r=" + spawner.spawnRadius
              + "  minDist=" + spawner.minDistanceToPlayer);
sb.AppendLine();

// ---- 改前快照 ----
sb.AppendLine("---- 改前 ----");
DumpWaves(spawner, sb);

// ---- 重建 waves ----
var waves = new InkWash.Enemies.Wave[3];

waves[0] = new InkWash.Enemies.Wave
{
    label = "第一波·墨兵",
    delayBefore = 1.5f,
    entries = new[]
    {
        new InkWash.Enemies.EnemySpawnEntry { prefab = P_MoGuai, count = 3 },
    },
};

waves[1] = new InkWash.Enemies.Wave
{
    label = "第二波·墨兵+墨卒",
    delayBefore = 2.0f,
    entries = new[]
    {
        new InkWash.Enemies.EnemySpawnEntry { prefab = P_MoGuai, count = 2 },
        new InkWash.Enemies.EnemySpawnEntry { prefab = P_MoGu,   count = 2 },
    },
};

waves[2] = new InkWash.Enemies.Wave
{
    label = "第三波·墨魇",
    delayBefore = 2.0f,
    entries = new[]
    {
        new InkWash.Enemies.EnemySpawnEntry { prefab = P_MoGuai,  count = 3 },
        new InkWash.Enemies.EnemySpawnEntry { prefab = P_MoShan, count = 1 },
    },
};

spawner.waves = waves;
EditorUtility.SetDirty(spawner);
EditorSceneManager.MarkSceneDirty(scene);
EditorSceneManager.SaveScene(scene);

sb.AppendLine();
sb.AppendLine("---- 改后 ----");
DumpWaves(spawner, sb);

// ---- 复核：重新从磁盘读场景，确认真的落盘了 ----
sb.AppendLine();
/* 复核：SaveScene 之后再 LoadScene 会破坏当前编辑态，这里改用"重新 FindObjectOfType + 读序列化值"。
   真正的落盘证据是 .unity 文件里的 GUID —— 用 yaml 文本扫一遍最直接。 */
string unityPath = scene.path;
if (!string.IsNullOrEmpty(unityPath) && File.Exists(unityPath))
{
    string yaml = File.ReadAllText(unityPath);
    sb.AppendLine("---- 复核：场景文件里能找到的敌人 prefab GUID ----");
    foreach (var nm in new[] { "Z_Enemy_MoShan", "Z_Enemy_MoGuai", "Z_Enemy_MoGu", "Enemy_MoTu", "Enemy_MoOu", "Enemy_MoYan" })
    {
        string p = EnemyDir + "/" + nm + ".prefab";
        string guid = AssetDatabase.AssetPathToGUID(p);
        bool found = !string.IsNullOrEmpty(guid) && yaml.Contains(guid);
        sb.AppendLine("  " + nm.PadRight(18) + " guid=" + guid + "  在场景文件里=" + (found ? "✓" : "—"));
    }
}

AssetDatabase.SaveAssets();

File.WriteAllText(Path.Combine(root, "Tools/reports/a15_wire_waves.txt"), sb.ToString());
Debug.Log("[a15]\n" + sb.ToString());

void DumpWaves(InkWash.Enemies.WaveSpawner sp, StringBuilder s)
{
    if (sp.waves == null) { s.AppendLine("  (waves 为 null)"); return; }
    for (int i = 0; i < sp.waves.Length; i++)
    {
        var w = sp.waves[i];
        if (w == null) { s.AppendLine("  [" + i + "] ★ null"); continue; }
        var parts = new List<string>();
        if (w.entries != null)
            foreach (var e in w.entries)
                parts.Add((e == null || e.prefab == null ? "★null" : e.prefab.name) + " x" + (e == null ? 0 : e.count));
        s.AppendLine("  [" + i + "] '" + w.label + "' delay=" + w.delayBefore
                     + "  → " + string.Join(" + ", parts.ToArray()));
    }
}
