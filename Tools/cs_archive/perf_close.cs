// perf_close.cs —— 收尾：退出 Play 并切回 Showcase 场景
//
// 一个文件跑两次即可（第一次退 Play，第二次切场景）：
// 退出 Play 会触发域重载，没法在同一次调用里接着切场景。
//
// ★ 与 perf_open.cs 同一条纪律：切场景前查 isDirty，脏场景直接放弃 ——
//   OpenScene 的模态弹窗会把 bridge 卡死。
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

var sb = new System.Text.StringBuilder();
sb.AppendLine("===== perf_close 收尾 =====");

if (EditorApplication.isPlaying)
{
    EditorApplication.isPlaying = false;
    sb.AppendLine("isPlaying=True ⇒ 已请求退出 Play 模式。请再跑一次本脚本以切回 Showcase。");
    return sb.ToString();
}

var cur = EditorSceneManager.GetActiveScene();
sb.AppendLine("当前场景 " + cur.name + "　dirty=" + cur.isDirty);
if (cur.isDirty)
{
    sb.AppendLine("结果: 当前场景有未保存改动 ⇒ 放弃切场景（避免模态弹窗卡死 bridge）");
    return sb.ToString();
}

const string target = "Assets/_Project/Scenes/Showcase.unity";
var sc = EditorSceneManager.OpenScene(target, OpenSceneMode.Single);
sb.AppendLine("结果: 已切回 " + sc.name + "　roots=" + sc.rootCount);
return sb.ToString();
