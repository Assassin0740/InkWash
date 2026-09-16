// 打开 Showcase 场景并进 Play，验证不报错
using UnityEditor.SceneManagement;
var sc = EditorSceneManager.OpenScene("Assets/_Project/Scenes/Showcase.unity", OpenSceneMode.Single);
UnityEditor.EditorApplication.isPlaying = true;
return "opened " + sc.name + " roots=" + sc.rootCount;
