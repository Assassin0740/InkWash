// z12a_prepare —— 编辑器侧：把 5 个 Ziyuan prefab 实例预置到当前场景的 Z_Rig 容器下
// 跑完不进 Play；之后跑 z12_sheet.cs --runtime 就位拍照
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

var sb = new StringBuilder();
const string PD = "Assets/_Project/Prefabs/Ziyuan";

// 清掉旧的
var old = GameObject.Find("Z_Rig");
if (old != null) { UnityEngine.Object.DestroyImmediate(old); sb.AppendLine("删掉旧 Z_Rig"); }

var rig = new GameObject("Z_Rig");
rig.transform.position = new Vector3(0f, 0.05f, 15f);

string[] names = { "Z_Orge", "Z_Orc", "Z_Undead", "Z_Dragon", "Z_Dragon_LP" };
foreach (var n in names)
{
    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PD + "/" + n + ".prefab");
    if (prefab == null) { sb.AppendLine($"!! 加载失败 {n}"); continue; }

    var go = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
    go.name = n;
    go.transform.SetParent(rig.transform, false);
    go.transform.localPosition = Vector3.zero;
    go.transform.localRotation = Quaternion.identity;

    // 每个挂一个 InkMaterialSwap，确保 ForceAll 认识它
    var sw = go.GetComponent<InkWash.UI.InkMaterialSwap>();
    sb.AppendLine($"预置 {n}  swap={(sw == null ? "无" : "有")}");
}

// 自动存场景，Play 才会看到
try
{
    var sc = EditorSceneManager.GetActiveScene();
    EditorSceneManager.MarkSceneDirty(sc);
    EditorSceneManager.SaveScene(sc);
    sb.AppendLine("场景已保存: " + sc.path);
}
catch (System.Exception e) { sb.AppendLine("保存场景失败: " + e.Message); }

AssetDatabase.SaveAssets();

Directory.CreateDirectory("Tools/reports");
File.WriteAllText("Tools/reports/z12a_prepare.txt", sb.ToString(), Encoding.UTF8);
return sb.ToString();
