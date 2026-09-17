// 把「跑步片段参考速度」写进预制体与场景实例并落盘。
//
// 背景：PlayerController 上这些字段是**序列化**的，脚本里改默认值不影响已经存好的
// 预制体 / 场景。改了默认值却忘了同步预制体，运行时依然是旧值——本项目已经踩过
// 一次（runSpeed 脚本默认 5.6、预制体 4.2，谁也说不清哪个生效）。
//
// 只在编辑器模式跑（Play 模式下写资产会被 WriteGuard 拦，且资产改动容易丢）。
const float RunSpeed = 4.2f;
const float RunRefSpeed = 4.49f;
const float WalkSpeed = 2.2f;
const float WalkRefSpeed = 1.55f;

var sb = new System.Text.StringBuilder();
if (UnityEngine.Application.isPlaying) { sb.AppendLine("[!] 正在 Play，请先停止 Play 再跑本脚本"); return sb.ToString(); }

int touched = 0;
System.Action<InkWash.Player.PlayerController, string> apply = (pc, tag) =>
{
    if (pc == null) return;
    pc.runSpeed = RunSpeed;
    pc.runRefSpeed = RunRefSpeed;
    pc.walkSpeed = WalkSpeed;
    pc.walkRefSpeed = WalkRefSpeed;
    UnityEditor.EditorUtility.SetDirty(pc);
    touched++;
    sb.AppendLine("已写入 " + tag + "  →  runSpeed=" + pc.runSpeed.ToString("F2")
        + " runRefSpeed=" + pc.runRefSpeed.ToString("F2"));
};

// ---- 1. 预制体 ----
foreach (var g in UnityEditor.AssetDatabase.FindAssets("t:Prefab"))
{
    var path = UnityEditor.AssetDatabase.GUIDToAssetPath(g);
    var go = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(path);
    if (go == null) continue;
    var pc = go.GetComponent<InkWash.Player.PlayerController>();
    if (pc != null) apply(pc, "预制体 " + path);
}

// ---- 2. 打开的场景里的实例 ----
bool sceneTouched = false;
foreach (var pc in UnityEngine.Object.FindObjectsOfType<InkWash.Player.PlayerController>(true))
{
    apply(pc, "场景实例 " + pc.gameObject.name);
    sceneTouched = true;
}

UnityEditor.AssetDatabase.SaveAssets();

if (sceneTouched)
{
    var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
    UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
    UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes();
    sb.AppendLine("已保存场景: " + scene.name);
}
else sb.AppendLine("（打开的场景里没有 PlayerController，只更新了预制体）");

sb.AppendLine("合计改动 " + touched + " 处");
return sb.ToString();
