using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

// d8：清理编辑态场景里的探针残留（HoverTestDragon / D2_MoLong / D*_Probe 等）。
// 只在编辑态跑（--runtime 不加）。
var sb = new StringBuilder();
sb.AppendLine("========== d8 清场景残留 ==========");

var scene = SceneManager.GetActiveScene();
var roots = scene.GetRootGameObjects();
int removed = 0;
for (int i = roots.Length - 1; i >= 0; i--)
{
    var go = roots[i];
    if (go == null) continue;
    string n = go.name;
    bool kill = n == "HoverTestDragon" || n == "D2_MoLong"
             || n.StartsWith("D1Probe") || n.StartsWith("D1_")
             || n.StartsWith("D2_") || n.StartsWith("D3_") || n.StartsWith("D4_")
             || n.StartsWith("D5_") || n.StartsWith("D6_") || n.StartsWith("D7_") || n.StartsWith("D8_");
    if (!kill) continue;
    sb.AppendLine("  删除 " + n);
    UnityEngine.Object.DestroyImmediate(go);
    removed++;
}

// 顺带扫掉嵌套的探针（挂在别的父下）
foreach (var r in scene.GetRootGameObjects())
{
    if (r == null) continue;
    var all = r.GetComponentsInChildren<Transform>(true);
    foreach (var t in all)
    {
        if (t == null || t.gameObject == r) continue;
        string n = t.name;
        if (n.StartsWith("D1Probe") || n.StartsWith("D6_Probe") || n.StartsWith("D7_Probe"))
        {
            sb.AppendLine("  删除嵌套 " + n);
            UnityEngine.Object.DestroyImmediate(t.gameObject);
            removed++;
        }
    }
}

sb.AppendLine("共删除 " + removed + " 个");
if (removed > 0) UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
sb.AppendLine("场景已保存：" + scene.path);
Debug.Log(sb.ToString());
return "D8_DONE removed=" + removed;
