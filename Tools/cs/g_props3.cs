// g-11：按索引重排道具（重名导致两棵 Pine_1 叠在一起）+ 重命名为唯一名
using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEngine.SceneManagement;
using UnityEditor.SceneManagement;

var sb = new StringBuilder();
string projRoot = Path.GetDirectoryName(Application.dataPath);

Bounds WorldBounds(GameObject go)
{
    var rs = go.GetComponentsInChildren<Renderer>();
    if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
    var b = rs[0].bounds;
    for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
    return b;
}

IEnumerator Body()
{
    yield return null;
    var scene = SceneManager.GetActiveScene();
    if (scene.path != "Assets/_Project/Scenes/Main.unity") { Debug.LogError("[g_props3] 场景不对"); yield break; }

    var propRoot = GameObject.Find("Environment/Props");
    if (propRoot == null) { Debug.LogError("[g_props3] 缺 Props"); yield break; }

    // 按**索引**给最终位置（名字会重名，不能按名字匹配 —— 上一版就是栽在这里）
    var finalPos = new (Vector3 pos, float yaw, string label)[]
    {
        (new Vector3(13.0f, 0f, 13.0f),   15f,  "松·东南"),
        (new Vector3(-13.0f, 0f, 13.0f),  55f,  "松·东北"),
        (new Vector3(13.0f, 0f, -13.0f), 125f,  "松·西南"),
        (new Vector3(-13.0f, 0f, -13.0f), 215f, "松·西北"),
        (new Vector3(19.5f, 0f, 0f),      75f,  "松·东"),
        (new Vector3(-19.5f, 0f, 0f),    300f,  "松·西"),
        (new Vector3(15.5f, 0f, -12.0f),  20f,  "枯·西南"),
        (new Vector3(-15.5f, 0f, 12.0f), 155f,  "枯·东北"),
        (new Vector3(12.0f, 0f, 15.5f),  260f,  "虬·东南"),
        (new Vector3(-12.0f, 0f, -15.5f), 95f,  "虬·西北"),
        (new Vector3(19.0f, 0f, 8.5f),     0f,  "石1"),
        (new Vector3(19.2f, 0f, -7.5f),   65f,  "石2"),
        (new Vector3(-19.0f, 0f, 9.5f),  130f,  "石3"),
        (new Vector3(-19.2f, 0f, -8.5f), 200f,  "石4"),
        (new Vector3(8.5f, 0f, 19.0f),    15f,  "石5"),
        (new Vector3(-8.5f, 0f, -19.0f), 285f,  "石6"),
        (new Vector3(15.5f, 0f, 12.5f),    0f,  "灌1"),
        (new Vector3(-15.5f, 0f, -12.5f), 80f,  "灌2"),
        (new Vector3(12.5f, 0f, -16.5f), 170f,  "灌3"),
        (new Vector3(-12.5f, 0f, 16.5f), 250f,  "灌4"),
    };

    int n = propRoot.transform.childCount;
    sb.AppendLine("Props 子物体数 = " + n + "（期望 20）");
    sb.AppendLine();
    sb.AppendLine("=== 重排 ===");
    for (int i = 0; i < n && i < finalPos.Length; i++)
    {
        var c = propRoot.transform.GetChild(i);
        string oldName = c.name;
        c.name = string.Format("Prop{0:D2}_{1}_{2}", i, finalPos[i].label, oldName);
        c.position = finalPos[i].pos;
        c.rotation = Quaternion.Euler(0f, finalPos[i].yaw, 0f);
        var b = WorldBounds(c.gameObject);
        c.position += Vector3.up * (0f - b.min.y);
        var b2 = WorldBounds(c.gameObject);
        sb.AppendLine(string.Format("  [{0:D2}] {1,-28} pos={2} 高={3} 底Y={4}",
            i, c.name, finalPos[i].pos.ToString("F1"),
            b2.size.y.ToString("0.##"), b2.min.y.ToString("0.###")));
    }

    EditorSceneManager.MarkSceneDirty(scene);
    bool ok = EditorSceneManager.SaveScene(scene, scene.path);
    AssetDatabase.SaveAssets();
    sb.AppendLine();
    sb.AppendLine("场景保存 = " + ok);

    File.WriteAllText(Path.Combine(projRoot, "Tools/reports/g_props3.txt"), sb.ToString());
    Debug.Log("[g_props3] done\n" + sb.ToString());
    yield return null;
}

return Body();
