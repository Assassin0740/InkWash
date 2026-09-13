// 把 Run 状态的 FootIK 竖直基准偏移写进预制体与场景实例并落盘。
//
// 为什么必须单独有一个脚本：FootIK.stateOffsets 是**序列化**在预制体上的数组，
// 改了 s2_setup_combat.cs 里那行常量不会自动生效。换跑步片段导致 Run 的
// 「无 IK 最低」从 +0.155 变成 +0.049，偏移就必须从 -0.155 改到 -0.046，
// 否则姿态体检里「落脚状态脚不穿地」会判未通过（实测穿地 10.6cm）。
const float RunOffset = -0.046f;

var sb = new System.Text.StringBuilder();
if (UnityEngine.Application.isPlaying) { sb.AppendLine("[!] 正在 Play，请先停止 Play 再跑本脚本"); return sb.ToString(); }

int touched = 0;
System.Action<UnityEngine.GameObject, string> apply = (go, tag) =>
{
    var ik = go.GetComponent<InkWash.Player.FootIK>();
    if (ik == null) return;
    if (ik.stateOffsets == null) { sb.AppendLine("[!] " + tag + " stateOffsets 为空"); return; }
    bool found = false;
    for (int i = 0; i < ik.stateOffsets.Length; i++)
    {
        if (ik.stateOffsets[i].state == "Run")
        {
            float old = ik.stateOffsets[i].y;
            var copy = ik.stateOffsets;
            copy[i].y = RunOffset;
            ik.stateOffsets = copy;
            found = true;
            sb.AppendLine("已写入 " + tag + " → Run 偏移 " + old.ToString("F3") + " → " + RunOffset.ToString("F3"));
        }
    }
    if (!found) { sb.AppendLine("[!] " + tag + " 偏移表里没有 Run 条目"); return; }
    UnityEditor.EditorUtility.SetDirty(ik);
    touched++;
};

foreach (var g in UnityEditor.AssetDatabase.FindAssets("t:Prefab"))
{
    var path = UnityEditor.AssetDatabase.GUIDToAssetPath(g);
    var go = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(path);
    if (go == null) continue;
    if (go.GetComponent<InkWash.Player.FootIK>() != null) apply(go, "预制体 " + path);
}

bool sceneTouched = false;
foreach (var ik in UnityEngine.Object.FindObjectsOfType<InkWash.Player.FootIK>(true))
{
    apply(ik.gameObject, "场景实例 " + ik.gameObject.name);
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
sb.AppendLine("合计改动 " + touched + " 处");
return sb.ToString();
