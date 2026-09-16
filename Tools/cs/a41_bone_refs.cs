// a41_bone_refs.cs —— 龙蒙皮塌陷的定位：bones 数组为什么是 null
//
// a40 决定性证据：
//   · Object_281: bones=273  非null=0  rootBone=NULL
//   · 换 URP/Unlit 品红 → 仍然全空（⇒ 与 shader 无关）
//   · 用 MeshRenderer 静态挂同一条 mesh → **正常渲染出图**（⇒ 网格本身健康）
//   ⇒ 结论：**skin 的 bones 引用在某个环节丢了**，蒙皮把所有顶点塌到原点。
//
// 本脚本比对四个载体，找出"在哪一步丢的"：
//   ① 源 FBX 资产（chinese_dragon.fbx）的 Mesh：它自带 Skin 数据吗？
//      → 看 `Mesh.bindposes.Length`（>0 说明 FBX 里带蒙皮）
//   ② Z_Dragon.prefab（LoadPrefabContents）：bones 是否非 null
//   ③ Z_Dragon.prefab 的**文本**里 m_Bones 有几条、rootBone 指向谁
//   ④ Z_Enemy_MoLong.prefab（我建的）：bones 是否非 null
//      —— 并检查我克隆搬入时是否**只搬了 GameObject 而丢了 SMR 的 bone 引用**
//
// ★ 这就是硬规矩 18 的另一面：我之前在 `InstantiatePrefab` 实例上读到 null，
//   以为是"读法陷阱"；a40/a41 证明**是真的丢了**。
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

var sb = new StringBuilder();
string root = Path.GetDirectoryName(Application.dataPath);
sb.AppendLine("========== a41 龙的 bones 引用在哪一步丢的 ==========");

// ---------- ① 源 FBX 的 Mesh ----------
sb.AppendLine();
sb.AppendLine("---- ① 源 FBX 资产 ----");
string fbx = "Assets/ThirdParty/Ziyuan/_fbx/chinese_dragon.fbx";
var fbxAssets = AssetDatabase.LoadAllAssetsAtPath(fbx);
var meshes = new List<Mesh>();
foreach (var a in fbxAssets) if (a is Mesh m) meshes.Add(m);
sb.AppendLine("  FBX 内 Mesh 数 = " + meshes.Count);
foreach (var m in meshes)
{
    sb.AppendLine("  " + m.name + "  vertexCount=" + m.vertexCount + "  bindposes=" + m.bindposes.Length);
}
var objs = new List<GameObject>();
foreach (var a in fbxAssets) if (a is GameObject g) objs.Add(g);
sb.AppendLine("  FBX 内 GameObject 数 = " + objs.Count);
foreach (var g in objs)
{
    var s = g.GetComponentsInChildren<SkinnedMeshRenderer>(true);
    if (s.Length == 0) continue;
    foreach (var smr in s)
    {
        int nn = 0, nu = 0;
        if (smr.bones != null) foreach (var b in smr.bones) { if (b == null) nu++; else nn++; }
        sb.AppendLine("  [" + g.name + "] SMR " + smr.name
                      + " bones=" + (smr.bones == null ? -1 : smr.bones.Length)
                      + " 非null=" + nn + " null=" + nu
                      + " rootBone=" + (smr.rootBone != null ? smr.rootBone.name : "NULL"));
    }
}

// ---------- ② Z_Dragon.prefab（源 prefab）----------
sb.AppendLine();
sb.AppendLine("---- ② Z_Dragon.prefab（LoadPrefabContents）----");
string srcPath = "Assets/_Project/Prefabs/Ziyuan/Z_Dragon.prefab";
var contents = PrefabUtility.LoadPrefabContents(srcPath);
try
{
    foreach (var smr in contents.GetComponentsInChildren<SkinnedMeshRenderer>(true))
    {
        int nn = 0, nu = 0;
        if (smr.bones != null) foreach (var b in smr.bones) { if (b == null) nu++; else nn++; }
        sb.AppendLine("  " + smr.name + " bones=" + (smr.bones == null ? -1 : smr.bones.Length)
                      + " 非null=" + nn + " null=" + nu
                      + " rootBone=" + (smr.rootBone != null ? smr.rootBone.name : "NULL")
                      + " mesh.bindposes=" + (smr.sharedMesh != null ? smr.sharedMesh.bindposes.Length : -1));
    }
    sb.AppendLine("  层级（前 3 层）：");
    void Dump(Transform t, int d)
    {
        sb.AppendLine(new string(' ', 4 + d * 2) + t.name);
        if (d >= 2) return;
        for (int i = 0; i < t.childCount; i++) Dump(t.GetChild(i), d + 1);
    }
    Dump(contents.transform, 0);
}
finally { PrefabUtility.UnloadPrefabContents(contents); }

// ---------- ③ Z_Enemy_MoLong.prefab（我建的）----------
sb.AppendLine();
sb.AppendLine("---- ③ Z_Enemy_MoLong.prefab（我建的）----");
var c2 = PrefabUtility.LoadPrefabContents("Assets/_Project/Prefabs/Enemies/Z_Enemy_MoLong.prefab");
try
{
    foreach (var smr in c2.GetComponentsInChildren<SkinnedMeshRenderer>(true))
    {
        int nn = 0, nu = 0;
        if (smr.bones != null) foreach (var b in smr.bones) { if (b == null) nu++; else nn++; }
        sb.AppendLine("  " + smr.name + " bones=" + (smr.bones == null ? -1 : smr.bones.Length)
                      + " 非null=" + nn + " null=" + nu
                      + " rootBone=" + (smr.rootBone != null ? smr.rootBone.name : "NULL")
                      + " mesh.bindposes=" + (smr.sharedMesh != null ? smr.sharedMesh.bindposes.Length : -1));
    }
    sb.AppendLine("  层级（前 3 层）：");
    void Dump2(Transform t, int d)
    {
        var smr = t.GetComponent<SkinnedMeshRenderer>();
        string extra = smr != null
            ? "   [SMR bones=" + (smr.bones == null ? -1 : smr.bones.Length)
              + " root=" + (smr.rootBone != null ? smr.rootBone.name : "NULL") + "]"
            : "";
        sb.AppendLine(new string(' ', 4 + d * 2) + t.name + extra);
        if (d >= 2) return;
        for (int i = 0; i < t.childCount; i++) Dump2(t.GetChild(i), d + 1);
    }
    Dump2(c2.transform, 0);
}
finally { PrefabUtility.UnloadPrefabContents(c2); }

// ---------- ④ 文本层面对照：两份 prefab 的 m_Bones 条数 ----------
sb.AppendLine();
sb.AppendLine("---- ④ 文本层面对照 ----");
foreach (var p in new[] { srcPath, "Assets/_Project/Prefabs/Enemies/Z_Enemy_MoLong.prefab" })
{
    string full = Path.Combine(root, p);
    if (!File.Exists(full)) { sb.AppendLine("  " + p + " 不存在"); continue; }
    string txt = File.ReadAllText(full);
    int blocks = 0, boneRefs = 0, rootNone = 0;
    int idx = 0;
    while ((idx = txt.IndexOf("--- !u!137 &", idx)) >= 0)
    {
        blocks++;
        int end = txt.IndexOf("--- !u!", idx + 10);
        string blk = end > 0 ? txt.Substring(idx, end - idx) : txt.Substring(idx);
        int b0 = blk.IndexOf("m_Bones:");
        if (b0 >= 0)
        {
            int b1 = blk.IndexOf("m_RootBone", b0);
            if (b1 < 0) b1 = blk.Length;
            boneRefs += System.Text.RegularExpressions.Regex.Matches(blk.Substring(b0, b1 - b0), "fileID: \\d+").Count;
        }
        if (blk.Contains("m_RootBone: {fileID: 0}")) rootNone++;
        idx = end > 0 ? end : txt.Length;
    }
    sb.AppendLine("  " + p);
    sb.AppendLine("    SkinnedMeshRenderer 块 = " + blocks + "   m_Bones 引用总数 = " + boneRefs
                  + "   rootBone=0 的块 = " + rootNone);
}

File.WriteAllText(Path.Combine(root, "Tools/reports/a41_bone_refs.txt"), sb.ToString());
Debug.Log("[a41]\n" + sb.ToString());
