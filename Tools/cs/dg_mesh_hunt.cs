// dg_mesh_hunt.cs —— 定位「Z_Dragon.prefab 引用的那个 mesh」到底属于哪个文件
//
// 已确认：
//   Z_Dragon.prefab 的 SMR:  m_Mesh: {fileID: -3325053396650211315, guid: <chinese_dragon.fbx>, type: 3}
//   同一个 guid 的材质(-1725437989455171953) 能解析 ⇒ guid 与 type 都对，只有 mesh 的 localId 对不上。
//   chinese_dragon.fbx 里只有 2 个 Mesh：Object_281(7161509005421682173) / Object_282(7238157132297919992)。
//   而另一台机器 Play 里读到 sharedMesh.name == "dragon" ⇒ "dragon" 不是这个 fbx 的网格名。
//
// 本探针回答三件事：
//   (a) 全工程有没有 Mesh 的 localId == -3325053396650211315（在哪个文件）
//   (b) 全工程有没有 Mesh 名字就叫 dragon（在哪个文件）—— 与另一台机器的观测对账
//   (c) chinese_dragon.fbx 这个 fbx 及其"工作正常的对照"（Skeleton_Golem.fbx）的
//       ModelImporter 关键设置差异（fileIdsGeneration / internalIDToNameTable / nodeNameCollisionStrategy）
//
// 只读。
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

const long Target = -3325053396650211315L;
const string DragonFbx = "Assets/ThirdParty/Ziyuan/_fbx/chinese_dragon.fbx";
const string MagicFbx = "Assets/ThirdParty/KayKit/Skeletons/Characters/Skeleton_Golem.fbx";

var sb = new StringBuilder();
var idHits = new List<string>();
var nameHits = new List<string>();
int modelCount = 0, meshTotal = 0;

sb.AppendLine("===== dg_mesh_hunt =====");
sb.AppendLine("目标 localId = " + Target);
sb.AppendLine();

string[] guids = AssetDatabase.FindAssets("t:Model");
sb.AppendLine("工程内 Model 资产 " + guids.Length + " 个，开始逐个枚举 Mesh 子资产…");

foreach (string g in guids)
{
    string p = AssetDatabase.GUIDToAssetPath(g);
    if (string.IsNullOrEmpty(p)) continue;
    modelCount++;
    UnityEngine.Object[] subs;
    try { subs = AssetDatabase.LoadAllAssetsAtPath(p); }
    catch { continue; }
    foreach (var o in subs)
    {
        Mesh m = o as Mesh;
        if (m == null) continue;
        meshTotal++;
        long lid = 0;
        string gg = "";
        bool ok = AssetDatabase.TryGetGUIDAndLocalFileIdentifier(m, out gg, out lid);
        string line = (ok ? lid.ToString() : "?").PadRight(22) + " " + p + "  sub=" + m.name
                      + "  verts=" + m.vertexCount;
        if (ok && lid == Target) idHits.Add("★ID命中★ " + line);
        if (m.name == "dragon") nameHits.Add("★名为dragon★ " + line);
    }
}

sb.AppendLine("  扫描 Model " + modelCount + " 个，Mesh 子资产 " + meshTotal + " 个");
sb.AppendLine();
sb.AppendLine("[a] localId == " + Target + " 的 Mesh：");
if (idHits.Count == 0) sb.AppendLine("      ★ 一个都没有 ⇒ 该引用在本工程内**无处可解析**（悬空）");
foreach (var s in idHits) sb.AppendLine("      " + s);
sb.AppendLine();
sb.AppendLine("[b] 名字为 dragon 的 Mesh：");
if (nameHits.Count == 0) sb.AppendLine("      没有");
foreach (var s in nameHits) sb.AppendLine("      " + s);
sb.AppendLine();

// ---------- [c] 对照两个模型的 .meta 原文（fileIdsGeneration / internalIDToNameTable 等）----------
// 为什么读原文：fileIdsGeneration 等并非稳定的公开 API，读 .meta 文本才是真值。
string[] watchKeys =
{
    "fileIdsGeneration", "nodeNameCollisionStrategy", "internalIDToNameTable",
    "importAnimation", "optimizeGameObjects", "materialImportMode", "bakeAxisConversion",
    "preserveHierarchy", "sortHierarchyByName", "useFileUnits", "useFileScale"
};
Action<string> dumpMeta = (assetPath) =>
{
    sb.AppendLine("--- " + assetPath);
    string metaPath = assetPath + ".meta";
    if (!File.Exists(assetPath)) { sb.AppendLine("      ★ 资产文件不存在"); return; }
    if (!File.Exists(metaPath)) { sb.AppendLine("      ★ .meta 不存在"); return; }
    string[] lines = File.ReadAllLines(metaPath);
    foreach (string ln in lines)
    {
        string t = ln.Trim();
        foreach (string k in watchKeys)
            if (t.StartsWith(k))
                sb.AppendLine("      " + t);
    }
    sb.AppendLine("      依赖哈希 = " + AssetDatabase.GetAssetDependencyHash(assetPath).ToString());
    sb.AppendLine("      资产 guid = " + AssetDatabase.AssetPathToGUID(assetPath));
};
sb.AppendLine("[c] .meta 对照");
dumpMeta(DragonFbx);
dumpMeta(MagicFbx);

// ---------- [d] 列出 chinese_dragon.fbx 的 Mesh 细节（含 mesh.name 与 sub 名） ----------
sb.AppendLine();
sb.AppendLine("[d] " + DragonFbx + " 的 Mesh 明细");
if (File.Exists(DragonFbx))
{
    var subs = AssetDatabase.LoadAllAssetsAtPath(DragonFbx);
    foreach (var o in subs)
    {
        Mesh m = o as Mesh;
        if (m == null) continue;
        long lid = 0; string gg = "";
        AssetDatabase.TryGetGUIDAndLocalFileIdentifier(m, out gg, out lid);
        sb.AppendLine("      sub名=" + m.name + "  mesh.name=" + m.name
                      + "  localId=" + lid
                      + "  verts=" + m.vertexCount + "  bindposes=" + (m.bindposes == null ? 0 : m.bindposes.Length)
                      + "  subMesh=" + m.subMeshCount);
    }
    // 该 fbx 的顶层 GameObject 名
    GameObject root = AssetDatabase.LoadAssetAtPath<GameObject>(DragonFbx);
    sb.AppendLine("      fbx 根对象: " + (root == null ? "null" : root.name + "（子 " + root.transform.childCount + "）"));
}

// ---------- [e] Z_Dragon.prefab 的 SMR 再确认 ----------
sb.AppendLine();
sb.AppendLine("[e] Z_Dragon.prefab 的 SMR 当前读数");
var dg = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Ziyuan/Z_Dragon.prefab");
if (dg != null)
{
    var s = dg.GetComponentInChildren<SkinnedMeshRenderer>(true);
    if (s != null)
        sb.AppendLine("      mesh=" + (s.sharedMesh == null ? "null" : s.sharedMesh.name)
                      + "  bones=" + (s.bones == null ? 0 : s.bones.Length)
                      + "  rootBone=" + (s.rootBone == null ? "null" : s.rootBone.name));
}

string outDir = Path.Combine(Directory.GetCurrentDirectory(), "Tools", "reports");
Directory.CreateDirectory(outDir);
string outPath = Path.Combine(outDir, "dg_mesh_hunt.txt");
File.WriteAllText(outPath, sb.ToString(), new UTF8Encoding(false));
sb.AppendLine();
sb.AppendLine("报告: " + outPath);
return sb.ToString();
