// z18_fixscale —— 用「场景实例 + ApplyPrefab」方式改缩放（比 LoadPrefabContents 可靠）
//
// 上一轮失败原因：LoadPrefabContents 改 root.transform.localScale 后，
// SaveAsPrefabAsset 没能写进 prefab（被 FBX 的 root override 覆盖）。
// 验证证据：Z_Orc.prefab 里 root 的 m_LocalScale = 1.0000001 而不是我设的 0.3392。
//
// 本脚本：每个 prefab 实例化到场景 → 设 scale → PrefabUtility.ApplyPrefabInstance → 删实例
using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

var sb = new StringBuilder();
const string PD = "Assets/_Project/Prefabs/Ziyuan";

var ks = new (string name, float k)[]
{
    ("Z_Orge",      1.0879f),
    ("Z_Orc",       0.3392f),
    ("Z_Undead",    0.3349f),
    ("Z_Dragon",    0.7546f),
    ("Z_Dragon_LP", 0.5759f),
};

// 先清掉场景里的旧 Z_Rig，免得干扰
var oldRig = GameObject.Find("Z_Rig");
if (oldRig != null) { UnityEngine.Object.DestroyImmediate(oldRig); sb.AppendLine("清掉旧 Z_Rig"); }

foreach (var d in ks)
{
    string pp = PD + "/" + d.name + ".prefab";
    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(pp);
    if (prefab == null) { sb.AppendLine($"!! {d.name} 加载失败"); continue; }

    var inst = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
    inst.name = "TMP_" + d.name;
    inst.transform.position = Vector3.zero;
    inst.transform.rotation = Quaternion.identity;
    inst.transform.localScale = Vector3.one * d.k;

    PrefabUtility.ApplyPrefabInstance(inst, InteractionMode.AutomatedAction);
    UnityEngine.Object.DestroyImmediate(inst);

    // 立刻回读 prefab 文件确认
    var reread = AssetDatabase.LoadAssetAtPath<GameObject>(pp);
    sb.AppendLine($"{d.name}: 设 k={d.k} -> prefab 根 localScale={reread.transform.localScale}");
}

AssetDatabase.SaveAssets();
AssetDatabase.Refresh();
EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

Directory.CreateDirectory("Tools/reports");
File.WriteAllText("Tools/reports/z18_fixscale.txt", sb.ToString(), Encoding.UTF8);
return sb.ToString();
