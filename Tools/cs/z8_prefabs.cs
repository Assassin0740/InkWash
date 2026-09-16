// z8_prefabs —— 把 5 个 Ziyuan 素材做成 prefab（供 Play 模式对比图用）
// prefab 里就把材质换好（这样 Play 模式不需 AssetDatabase）
using System.IO;
using System.Text;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

var sb = new StringBuilder();
const string ROOT = "Assets/ThirdParty/Ziyuan/_fbx";
const string MATDIR = "Assets/_Project/Art/Materials";
string prefabDir = "Assets/_Project/Prefabs/Ziyuan";
Directory.CreateDirectory(prefabDir);

var defs = new (string name, string fbx, string mat)[]
{
    ("Z_Orge",      ROOT + "/mountain_orge.fbx",                     MATDIR + "/M_Ink_Enemy_MoShan.mat"),
    ("Z_Orc",       ROOT + "/low-poly_orc.fbx",                      MATDIR + "/M_Ink_Enemy_MoGuai.mat"),
    ("Z_Undead",    ROOT + "/cursed_undead_soldier_rig.fbx",         MATDIR + "/M_Ink_Enemy_MoGu.mat"),
    ("Z_Dragon",    ROOT + "/chinese_dragon.fbx",                    MATDIR + "/M_Ink_Boss_Dragon.mat"),
    ("Z_Dragon_LP", ROOT + "/lowpoly_textured_chinese_dragon.fbx",   MATDIR + "/M_Ink_Boss_Dragon.mat"),
};

foreach (var d in defs)
{
    var src = AssetDatabase.LoadAssetAtPath<GameObject>(d.fbx);
    if (src == null) { sb.AppendLine($"!! 加载失败 {d.fbx}"); continue; }

    var go = UnityEngine.Object.Instantiate(src) as GameObject;
    go.name = d.name;

    var mat = AssetDatabase.LoadAssetAtPath<Material>(d.mat);
    int n = 0;
    foreach (var r in go.GetComponentsInChildren<Renderer>())
    {
        var arr = new Material[r.sharedMaterials.Length == 0 ? 1 : r.sharedMaterials.Length];
        for (int i = 0; i < arr.Length; i++) arr[i] = mat;
        r.sharedMaterials = arr;
        n++;
    }

    // 量尺寸
    var rends = go.GetComponentsInChildren<Renderer>();
    Bounds b = new Bounds(); bool first = true;
    foreach (var r in rends) { if (first) { b = r.bounds; first = false; } else b.Encapsulate(r.bounds); }

    // 挂 InkMaterialSwap，供 InkMaterialForcer 与 InkStylePanel 识别
    var sw = go.GetComponent<InkWash.UI.InkMaterialSwap>();
    if (sw == null) sw = go.AddComponent<InkWash.UI.InkMaterialSwap>();
    sw.target = go.GetComponentInChildren<Renderer>();
    sw.inkMaterials = new Material[] { mat };

    string pp = prefabDir + "/" + d.name + ".prefab";
    PrefabUtility.SaveAsPrefabAsset(go, pp);
    string rootName = go.name;
    int rendCount = n;
    float bh = b.size.y, bw = b.size.x, bd = b.size.z;
    UnityEngine.Object.DestroyImmediate(go);

    sb.AppendLine($"{d.name}: 渲染器={rendCount} 高={bh:F2}m 宽={bw:F2}m 深={bd:F2}m -> {pp}");
    sb.AppendLine($"    骨架根={rootName} 材质={mat?.name}");
}

AssetDatabase.SaveAssets();
AssetDatabase.Refresh();

Directory.CreateDirectory("Tools/reports");
File.WriteAllText("Tools/reports/z8_prefabs.txt", sb.ToString(), Encoding.UTF8);
return sb.ToString();
