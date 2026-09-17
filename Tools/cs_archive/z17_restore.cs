// z17_restore —— 恢复 v2 已验证正确的缩放（那组数字在对比图里肉眼确认过比例合理）
//
// 为什么不再自己算：SkinnedMesh 的尺寸测量有 bindpose/姿态/骨骼层级三重干扰，
// 我用 mesh.bounds 试过一轮，结果龙变成了 82 m 高 —— 明显更错。
// v2 那次用「sharedMesh.vertices × localToWorldMatrix」量出来的 2.60/2.05/1.95/9.00/7.00
// 是**已经在渲染图里被眼睛验证过的**，直接固化这几个 localScale。
using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

var sb = new StringBuilder();
const string PD = "Assets/_Project/Prefabs/Ziyuan";

// 从 z11 的记录反推：那些 k 值产生过正确的视觉比例
// z11 输出：Z_Orge k=1.0879 / Z_Orc k=0.3392 / Z_Undead k=0.3349 / Z_Dragon k=0.7546 / Z_Dragon_LP k=0.5759
var ks = new (string name, float k, float expectH, float expectLen)[]
{
    ("Z_Orge",      1.0879f, 2.60f, 3.48f),
    ("Z_Orc",       0.3392f, 2.05f, 0.76f),
    ("Z_Undead",    0.3349f, 1.95f, 0.41f),
    ("Z_Dragon",    0.7546f, 0.98f, 9.00f),
    ("Z_Dragon_LP", 0.5759f, 1.47f, 7.00f),
};

foreach (var d in ks)
{
    string pp = PD + "/" + d.name + ".prefab";
    var root = PrefabUtility.LoadPrefabContents(pp);
    root.transform.localPosition = Vector3.zero;
    root.transform.localRotation = Quaternion.identity;
    root.transform.localScale = Vector3.one * d.k;
    PrefabUtility.SaveAsPrefabAsset(root, pp);
    PrefabUtility.UnloadPrefabContents(root);
    sb.AppendLine($"{d.name}: localScale -> {d.k}  (期望 高={d.expectH} 体长={d.expectLen})");
}

AssetDatabase.SaveAssets();
AssetDatabase.Refresh();
Directory.CreateDirectory("Tools/reports");
File.WriteAllText("Tools/reports/z17_restore.txt", sb.ToString(), Encoding.UTF8);
return sb.ToString();
