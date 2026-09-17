// ws_state.cs —— 编辑器态（非 Play）读场景里的序列化值，判定「谁改脏了谁」
//
// 为什么必须退出 Play 再读：
//   Play 模式下读到的 `enabled` 是**运行时实例**的值，它可能被任何探针改过，
//   推断不出"场景文件里存的是什么"。只有编辑器态读到的才是序列化真值。
//
// 要判的两件事：
//   1) WaveSpawner.enabled —— 场景里到底存的 true 还是 false
//   2) 场景 isDirty —— 变脏说明有东西写过它；配合 1) 就能区分
//      「配置本来如此」还是「上一轮探针留下的脏状态」
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

var sb = new StringBuilder();
sb.AppendLine("===== ws_state：编辑器态序列化真值 =====");
sb.AppendLine("isPlaying = " + EditorApplication.isPlaying);

var sc = EditorSceneManager.GetActiveScene();
sb.AppendLine("当前场景 = " + sc.name + "　path = " + sc.path);
sb.AppendLine("isDirty = " + sc.isDirty
              + (sc.isDirty ? "　★ 有未保存改动" : "　（干净）"));

var sps = UnityEngine.Object.FindObjectsOfType<InkWash.Enemies.WaveSpawner>(true);
sb.AppendLine("WaveSpawner 数量 = " + sps.Length);
foreach (var sp in sps)
{
    sb.AppendLine("  [" + sp.gameObject.name + "] enabled = " + sp.enabled
                  + "　activeSelf = " + sp.gameObject.activeSelf
                  + "　autoStart = " + sp.autoStart
                  + "　waves = " + (sp.waves == null ? "null" : sp.waves.Length.ToString()));
}

var rms = UnityEngine.Object.FindObjectsOfType<InkWash.Roguelike.RunManager>(true);
foreach (var rm in rms)
    sb.AppendLine("RunManager [" + rm.gameObject.name + "] enabled = " + rm.enabled
                  + "　autoStartRun = " + rm.autoStartRun);

var rcs = UnityEngine.Object.FindObjectsOfType<InkWash.Enemies.RoomController>(true);
foreach (var rc in rcs)
    sb.AppendLine("RoomController [" + rc.gameObject.name + "] enabled = " + rc.enabled
                  + "　spawner = " + (rc.spawner == null ? "null" : rc.spawner.gameObject.name));

// 场景文件的 git 状态（判定是否与仓库版本一致）
sb.AppendLine();
sb.AppendLine("（下一步用 git status 交叉核对 Main.unity 是否被改）");
return sb.ToString();
