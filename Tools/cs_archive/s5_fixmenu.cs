// s5_fixmenu.cs —— 关掉 WaveSpawner 的自动开刷（编辑态，一次跑完）
//
// 背景（实测到的真缺陷）：场景里 RunManager 默认停在主菜单，但 WaveSpawner.autoStart = true
// 会在自己的 Start() 里直接 Begin() —— 于是**玩家还没点「开始一局」，怪已经在刷、并且能把玩家砍死**。
// 表现是 Console 里一条 `[RunManager] 拒绝非法迁移：MainMenu → GameOver`
// （不是报错，是迁移表正确兜住了"发生在开局之前的一次死亡"）。
// 一局的开始应当只有一个入口：RunManager.StartRun() 里显式调 spawner.Begin()。
//
// 本脚本**幂等**：已经是 false 就只打印一行。
using System.Collections;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using InkWash.Enemies;

IEnumerator Body()
{
    var sb = new StringBuilder();
    string projRoot = Path.GetDirectoryName(Application.dataPath);
    yield return null; yield return null;

    var scene = SceneManager.GetActiveScene();
    sb.AppendLine("场景 = " + scene.name);
    sb.AppendLine("===== S5 修复：主菜单里不该在刷怪 =====");

    var spawner = UnityEngine.Object.FindObjectOfType<WaveSpawner>();
    if (spawner == null)
    {
        sb.AppendLine("  ** 场景里找不到 WaveSpawner");
    }
    else
    {
        sb.AppendLine("  before: autoStart = " + spawner.autoStart);
        spawner.autoStart = false;
        EditorUtility.SetDirty(spawner);
        sb.AppendLine("  after : autoStart = " + spawner.autoStart
                      + "（一局的开始统一由 RunManager.StartRun() → spawner.Begin() 驱动）");
    }

    EditorSceneManager.MarkSceneDirty(scene);
    bool saved = EditorSceneManager.SaveScene(scene, scene.path);
    AssetDatabase.SaveAssets();
    sb.AppendLine("场景保存 = " + saved);

    File.WriteAllText(Path.Combine(projRoot, "Tools/reports/s5_fixmenu.txt"), sb.ToString());
    Debug.Log("[s5_fixmenu] done");
    yield return null;
}
return Body();
