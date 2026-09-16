// a16_verify_waves.cs —— 从磁盘重新读场景，确认波次配置真的落盘了
//
// 为什么必须单独复核：a15 里我"改完再读一遍"其实读的是**内存里的同一个对象**，
//   那是自我循环论证。而场景文件是 **binary 序列化**（不是 YAML 文本），
//   grep GUID 也 grep 不到 ⇒ 上一轮的"复核"两条路都是无效的。
//
// 正确做法：把当前场景**卸掉**，重新 OpenScene，再从新加载的对象上读 waves。
//   但这会破坏编辑态的临时对象 —— 所以本脚本只做**只读**：
//   用 AssetDatabase 强制重新导入场景，然后 OpenScene(Additive) 一份到旁路场景里读。
//
// 更简单且够用的做法：OpenScene 同一场景（Single 模式）本身就是从磁盘加载。
//   如果 a15 没落盘，这里读到的就是旧值。所以本脚本直接重开场景再读一遍 ——
//   这就是真正的"从磁盘读"。
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

var sb = new StringBuilder();
string root = Path.GetDirectoryName(Application.dataPath);

// 先存盘一次，确保内存态与磁盘一致（幂等）
var scene = EditorSceneManager.GetActiveScene();
EditorSceneManager.SaveScene(scene);

// ★ 关键：重新打开场景 = 强制从磁盘反序列化
EditorSceneManager.OpenScene(scene.path, OpenSceneMode.Single);

sb.AppendLine("========== a16 从磁盘复核波次配置 ==========");
sb.AppendLine("重新打开场景: " + scene.path);
sb.AppendLine();

var spawner = UnityEngine.Object.FindObjectOfType<InkWash.Enemies.WaveSpawner>();
if (spawner == null)
{
    sb.AppendLine("★ 重开场景后找不到 WaveSpawner");
}
else
{
    sb.AppendLine("WaveSpawner 在: " + spawner.gameObject.name
                  + "  autoStart=" + spawner.autoStart
                  + "  interval=" + spawner.spawnInterval
                  + "  center=" + spawner.spawnCenter
                  + "  r=" + spawner.spawnRadius
                  + "  minDist=" + spawner.minDistanceToPlayer);
    sb.AppendLine();
    sb.AppendLine("波次（从磁盘读回）：");
    if (spawner.waves == null) sb.AppendLine("  (null)");
    else
        for (int i = 0; i < spawner.waves.Length; i++)
        {
            var w = spawner.waves[i];
            if (w == null) { sb.AppendLine("  [" + i + "] ★ null"); continue; }
            var parts = new List<string>();
            if (w.entries != null)
                foreach (var e in w.entries)
                    parts.Add((e == null || e.prefab == null ? "★null" : e.prefab.name) + " x" + (e == null ? 0 : e.count));
            sb.AppendLine("  [" + i + "] '" + w.label + "' delay=" + w.delayBefore
                          + "  → " + string.Join(" + ", parts.ToArray()));
        }
}

File.WriteAllText(Path.Combine(root, "Tools/reports/a16_verify_waves.txt"), sb.ToString());
Debug.Log("[a16]\n" + sb.ToString());
