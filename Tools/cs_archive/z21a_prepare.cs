// z21a_prepare —— 编辑器侧：额外预置一个「原 PBR 材质」的 Orge 实例，供 A/B 对照
//
// 为什么需要：Play 模式不能 AssetDatabase ⇒ 没法临时取 FBX 原始材质。
// 所以在这里把原始材质先取出来，"烘"进一个独立 prefab（Z_Orge_PBR）。
using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

var sb = new StringBuilder();
const string FBX = "Assets/ThirdParty/Ziyuan/_fbx/mountain_orge.fbx";
string pbrPrefabPath = "Assets/_Project/Prefabs/Ziyuan/Z_Orge_PBR.prefab";

// 1) 从 FBX 实例上收集原始材质
var fbxGo = AssetDatabase.LoadAssetAtPath<GameObject>(FBX);
if (fbxGo == null) { sb.AppendLine("!! FBX 加载失败"); return "FAIL"; }

var tmp = UnityEngine.Object.Instantiate(fbxGo) as GameObject;
var srcMats = new List<Material>();
foreach (var r in tmp.GetComponentsInChildren<Renderer>())
    foreach (var m in r.sharedMaterials)
        if (m != null && !srcMats.Contains(m)) srcMats.Add(m);

sb.AppendLine($"从 FBX 收集原始材质 {srcMats.Count} 种:");
foreach (var m in srcMats) sb.AppendLine($"   {m.name}  shader={m.shader.name}");

// 2) 把 tmp 存成独立 prefab（保留原材质与缩放）
//    注意要用 z10/z18 那套归一化后的缩放，保持与水墨版同尺寸
tmp.name = "Z_Orge_PBR";
tmp.transform.localScale = Vector3.one * 1.0879f;   // 与 Z_Orge 一致
PrefabUtility.SaveAsPrefabAsset(tmp, pbrPrefabPath);
sb.AppendLine($"已存 {pbrPrefabPath}  scale={tmp.transform.localScale}");
UnityEngine.Object.DestroyImmediate(tmp);

// 3) 重建 Z_Rig（含 PBR 对照，放在稍远处）
var old = GameObject.Find("Z_Rig");
if (old != null) UnityEngine.Object.DestroyImmediate(old);
var oldP = GameObject.Find("Z_Orge_PBR");
if (oldP != null) UnityEngine.Object.DestroyImmediate(oldP);

var rig = new GameObject("Z_Rig");
rig.transform.position = new Vector3(0f, 0.05f, 0f);

string[] names = { "Z_Orge", "Z_Orc", "Z_Undead", "Z_Dragon", "Z_Dragon_LP" };
foreach (var n in names)
{
    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Ziyuan/" + n + ".prefab");
    if (prefab == null) { sb.AppendLine($"!! {n} 失败"); continue; }
    var go = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
    go.name = n;
    go.transform.SetParent(rig.transform, false);
    go.transform.localPosition = Vector3.zero;
    go.transform.localRotation = Quaternion.identity;
    sb.AppendLine($"预置 {n}");
}

// PBR 对照（独立放，不挂 Z_Rig 下）
var pbrPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(pbrPrefabPath);
if (pbrPrefab != null)
{
    var pg = PrefabUtility.InstantiatePrefab(pbrPrefab) as GameObject;
    pg.name = "Z_Orge_PBR";
    pg.transform.position = new Vector3(20f, 0.05f, 0f);
    sb.AppendLine("预置 Z_Orge_PBR");
}

var sc = EditorSceneManager.GetActiveScene();
EditorSceneManager.MarkSceneDirty(sc);
EditorSceneManager.SaveScene(sc);
sb.AppendLine("场景已保存 " + sc.path);

AssetDatabase.SaveAssets();
Directory.CreateDirectory("Tools/reports");
File.WriteAllText("Tools/reports/z21a_prepare.txt", sb.ToString(), Encoding.UTF8);
return sb.ToString();
