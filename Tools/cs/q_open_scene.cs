// q_open_scene.cs —— 打开 Showcase 场景（**不进 Play**）。
//
// 为什么需要：域重载（RequestScriptReload）之后，编辑器当前打开的场景会变成默认/空场景，
// 演示场组件不存在 ⇒ 探针报「没找到「墨龙 + 盘旋」条目」。
// 探针自身用 `--runtime` 执行时会自动进 Play，所以这里只需把场景打开。
using UnityEditor.SceneManagement;

var sc = EditorSceneManager.OpenScene("Assets/_Project/Scenes/Showcase.unity", OpenSceneMode.Single);
return "OPENED " + sc.name + " roots=" + sc.rootCount;
