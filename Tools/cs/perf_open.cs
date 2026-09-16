// perf_open.cs —— 切到主游戏场景并进 Play（性能基线前置）
//
// ★ 为什么必须先查 isDirty 再开场景：
//   EditorSceneManager.OpenScene 遇到**未保存的改动**会弹一个模态对话框，
//   而模态对话框会把 bridge 的响应卡住 —— 探针从此永远等不到返回。
//   本项目上一台机器就吃过"Unity 被弹窗挡住，bridge 完全无响应"的亏。
//   所以这里 dirty 就直接放弃（不弹窗、不代用户做决定），把话说清楚。
//
// ★ 为什么要显式开场景：性能基线必须在**主游戏场景**（有 WaveSpawner /
//   RunManager 的那个）里跑；编辑器里当前开着的是 Showcase 演示场，
//   里面没有波次生成器，量出来的"战斗负载"是假的。
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

var sb = new System.Text.StringBuilder();
sb.AppendLine("===== perf_open 切换场景 =====");

var cur = EditorSceneManager.GetActiveScene();
sb.AppendLine("当前场景: " + cur.name + "  path=" + cur.path);
sb.AppendLine("dirty = " + cur.isDirty + "　isPlaying = " + EditorApplication.isPlaying);

if (EditorApplication.isPlaying)
{
    sb.AppendLine("结果: 已处于 Play 模式 —— 不做任何事（请不要在 Play 中切场景）");
    return sb.ToString();
}

if (cur.isDirty)
{
    sb.AppendLine("结果: 当前场景**有未保存的改动** ⇒ 主动放弃。");
    sb.AppendLine("原因: OpenScene 会弹模态对话框，模态框会卡死 bridge 通道。");
    sb.AppendLine("处置: 请先在 Unity 里手动保存或撤销当前场景的改动，再重跑本探针。");
    return sb.ToString();
}

const string target = "Assets/_Project/Scenes/Main.unity";
if (!File.Exists(Path.GetFullPath(Path.Combine(Application.dataPath, "..", target))))
{
    sb.AppendLine("结果: 找不到 " + target);
    return sb.ToString();
}

var sc = EditorSceneManager.OpenScene(target, OpenSceneMode.Single);
sb.AppendLine("已切到: " + sc.name + "  roots=" + sc.rootCount);

int spawner = 0, runmgr = 0, player = 0, cam = 0;
foreach (var go in sc.GetRootGameObjects())
{
    if (go.GetComponentInChildren<InkWash.Enemies.WaveSpawner>(true) != null) spawner++;
    if (go.GetComponentInChildren<InkWash.Roguelike.RunManager>(true) != null) runmgr++;
    if (go.GetComponentInChildren<InkWash.Player.PlayerController>(true) != null) player++;
    if (go.GetComponentInChildren<Camera>(true) != null) cam++;
}
sb.AppendLine("关键对象: WaveSpawner×" + spawner + "  RunManager×" + runmgr
              + "  Player×" + player + "  Camera×" + cam);

if (spawner == 0 || player == 0)
{
    sb.AppendLine("结果: 场景关键对象不全 ⇒ **不进 Play**（否则基线的负载描述是假的）");
    return sb.ToString();
}

EditorApplication.isPlaying = true;
sb.AppendLine("结果: 已请求进入 Play 模式（下一条探针跑基线）");
return sb.ToString();
