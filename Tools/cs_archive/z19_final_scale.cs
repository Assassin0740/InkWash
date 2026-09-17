// z19_final_scale —— 修正 Z_Undead 缩放（删碎片后尺寸变了）
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

var sb = new StringBuilder();
var inst = PrefabUtility.InstantiatePrefab(
    AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Ziyuan/Z_Undead.prefab")) as GameObject;
inst.transform.position = Vector3.zero;
inst.transform.rotation = Quaternion.identity;
inst.transform.localScale = Vector3.one * 0.4576f;
PrefabUtility.ApplyPrefabInstance(inst, InteractionMode.AutomatedAction);
string nm = inst.name;
UnityEngine.Object.DestroyImmediate(inst);

var re = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Ziyuan/Z_Undead.prefab");
sb.AppendLine($"Z_Undead localScale -> {re.transform.localScale}  (期望 0.4576)");

AssetDatabase.SaveAssets();
AssetDatabase.Refresh();
Directory.CreateDirectory("Tools/reports");
File.WriteAllText("Tools/reports/z19_final_scale.txt", sb.ToString(), Encoding.UTF8);
return sb.ToString();
