// z13a_prepare —— 重新预置：删掉 Icosphere 残留，只留真正的角色网格
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

var sb = new StringBuilder();
const string PD = "Assets/_Project/Prefabs/Ziyuan";

// 1) 先清理 prefab 内部的垃圾网格（Icosphere）
string[] pnames = { "Z_Orge", "Z_Orc", "Z_Undead", "Z_Dragon", "Z_Dragon_LP" };
foreach (var n in pnames)
{
    string pp = PD + "/" + n + ".prefab";
    var root = PrefabUtility.LoadPrefabContents(pp);
    var junk = new System.Collections.Generic.List<GameObject>();
    void Scan(Transform t)
    {
        for (int i = 0; i < t.childCount; i++)
        {
            var c = t.GetChild(i);
            string cn = c.name.ToLower();
            if (cn.StartsWith("icosphere") || cn.StartsWith("sphere")) junk.Add(c.gameObject);
            else Scan(c);
        }
    }
    Scan(root.transform);
    foreach (var j in junk)
    {
        sb.AppendLine($"  [{n}] 删除垃圾 {j.name}");
        UnityEngine.Object.DestroyImmediate(j);
    }
    PrefabUtility.SaveAsPrefabAsset(root, pp);
    PrefabUtility.UnloadPrefabContents(root);
}

// 2) 删旧 Z_Rig，重建
var old = GameObject.Find("Z_Rig");
if (old != null) UnityEngine.Object.DestroyImmediate(old);
var rig = new GameObject("Z_Rig");
rig.transform.position = new Vector3(0f, 0.05f, 15f);

foreach (var n in pnames)
{
    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PD + "/" + n + ".prefab");
    if (prefab == null) { sb.AppendLine($"!! {n} 加载失败"); continue; }
    var go = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
    go.name = n;
    go.transform.SetParent(rig.transform, false);
    go.transform.localPosition = Vector3.zero;
    go.transform.localRotation = Quaternion.identity;
    sb.AppendLine($"预置 {n}");
}

var sc = EditorSceneManager.GetActiveScene();
EditorSceneManager.MarkSceneDirty(sc);
EditorSceneManager.SaveScene(sc);
sb.AppendLine("场景已保存 " + sc.path);

AssetDatabase.SaveAssets();
Directory.CreateDirectory("Tools/reports");
File.WriteAllText("Tools/reports/z13a_prepare.txt", sb.ToString(), Encoding.UTF8);
return sb.ToString();
