// ws_fix.cs —— 修掉 Main.unity 里残留的 WaveSpawner.enabled=false
//
// 依据（均已实测）：
//   · 编辑器态读到的序列化真值就是 false，且场景 isDirty=false、磁盘与 git 一致
//     ⇒ 不是"残留内存没存"，是**这个值真的被提交进仓库了**
//   · `41f1c9a` 动了 Main.unity（Bin 136192 → 135420）
//   · 生产代码里没有任何一处写 `spawner.enabled`（全仓 grep 为空）
//     ⇒ 一旦为 false，永远不会有人把它打开
//
// 只改这一处。`RunManager.autoStartRun=false` **不动** —— 那是设计：
// `DrawMainMenu()` 里有「开始一局」按钮，S5 验收也断言了主菜单态。
//
// 顺带说明：EditorSettings.serializationMode 已是 2（ForceText），但本场景是早期
// 存下的二进制格式，所以这次保存会把它转成文本 YAML。这正是本次事故"在 165 个文件
// 的提交里完全看不出来"的原因，转文本本身就是修复的一部分。
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

var sb = new StringBuilder();
sb.AppendLine("===== ws_fix：修 Main 场景的 WaveSpawner 调试态残留 =====");
sb.AppendLine("isPlaying = " + EditorApplication.isPlaying);

if (EditorApplication.isPlaying)
{
    sb.AppendLine("结果: 当前在 Play 模式 ⇒ 拒绝改资产（Play 里的改动写不进磁盘）");
    return sb.ToString();
}

var sc = EditorSceneManager.GetActiveScene();
var sp = UnityEngine.Object.FindObjectOfType<InkWash.Enemies.WaveSpawner>(true);
var rm = UnityEngine.Object.FindObjectOfType<InkWash.Roguelike.RunManager>(true);
if (sp == null)
{
    sb.AppendLine("结果: 场景 " + sc.name + " 里找不到 WaveSpawner");
    return sb.ToString();
}

sb.AppendLine("场景 = " + sc.name + "　isDirty = " + sc.isDirty);
sb.AppendLine("修复前: WaveSpawner.enabled = " + sp.enabled);

bool changed = false;
if (!sp.enabled)
{
    sp.enabled = true;
    changed = true;
}
sb.AppendLine("修复后: WaveSpawner.enabled = " + sp.enabled + "　（本次改动 = " + changed + "）");
sb.AppendLine("未改动: RunManager.autoStartRun = " + (rm == null ? "?" : rm.autoStartRun.ToString())
              + "　← 设计如此（主菜单「开始一局」按钮驱动）");

if (!changed)
{
    sb.AppendLine("结果: 无需保存");
    return sb.ToString();
}

EditorSceneManager.MarkSceneDirty(sc);
bool ok = EditorSceneManager.SaveScene(sc);
sb.AppendLine("保存场景 = " + ok);
sb.AppendLine("结果: " + (ok ? "已写盘（若原为二进制格式，本次已转为文本 YAML）" : "★ 保存失败"));
return sb.ToString();
