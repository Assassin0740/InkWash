// molong_smr_probe.cs —— 编辑模式读「嵌套 prefab 的 SkinnedMeshRenderer」四种方式对照
//
// 要回答的问题：
//   Z_Enemy_MoLong.prefab 在编辑模式读出来 mesh=null / 材质=null，
//   而同一个 prefab 在 Play 里 mesh=dragon / bones=180。
//   这是「读取方式的假阴性」，还是「导入产物陈旧」？
//
// 为什么必须分四种读法：
//   Z_Enemy_MoLong.prefab 文本里 2 个 SMR 块**全是 stripped 占位**（字段上就没有
//   m_Mesh / m_Materials / m_Bones），数据全在源 prefab 里：
//     其一 → Z_Dragon.prefab#6533441441231540133（inline 完整 SMR，182 字段）
//     其二 → Enemy_MoYan.prefab#6374850387060490837（stripped，再往上是 KayKit fbx）
//   所以「读得到 / 读不到」完全取决于 Unity 有没有把占位解析到源。
//   A/B 读资产对象本身、C/D 强制展开，四种一起看就能定性。
//
// 判据：
//   ① 若 A/B 为空而 C/D 有值              → 是读取方式问题，编辑模式要用 C 或 D
//   ② 若四种都为空                        → 不是读取方式，看 D 段「源SMR」能否解析
//   ③ 若四种都有值                        → 当前读数是好的，之前那次是导入未完成
//   ④ 「源SMR」列若为空而资产在盘上完整   → 导入产物陈旧（Library 落后于资产）
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

var sb = new StringBuilder();
sb.AppendLine("===== molong_smr_probe =====");
sb.AppendLine("isPlaying = " + EditorApplication.isPlaying);

// ---------- D 段：导入新鲜度（Library 是否落后于资产） ----------
sb.AppendLine();
sb.AppendLine("[D] 导入新鲜度：磁盘 mtime 对比");
string db = Path.Combine(Directory.GetCurrentDirectory(), "Library", "ArtifactDB");
DateTime dbTime = DateTime.MinValue;
if (File.Exists(db))
{
    dbTime = File.GetLastWriteTimeUtc(db);
    sb.AppendLine("    Library/ArtifactDB          " + dbTime.ToString("MM-dd HH:mm:ss"));
}
else
{
    sb.AppendLine("    Library/ArtifactDB          不存在");
}

string[] watch =
{
    "Assets/_Project/Prefabs/Enemies/Z_Enemy_MoLong.prefab",
    "Assets/_Project/Prefabs/Ziyuan/Z_Dragon.prefab",
    "Assets/_Project/Prefabs/Enemies/Enemy_MoYan.prefab",
    "Assets/ThirdParty/Ziyuan/_fbx/chinese_dragon.fbx",
    "Assets/ThirdParty/KayKit/Skeletons/Characters/Skeleton_Golem.fbx",
    "Assets/_Project/Scripts/Enemies/EnemyDragon.cs"
};
foreach (string w in watch)
{
    if (!File.Exists(w))
    {
        sb.AppendLine("    " + w + "  -> 文件不存在");
        continue;
    }
    DateTime t = File.GetLastWriteTimeUtc(w);
    string flag = "";
    if (dbTime != DateTime.MinValue && t > dbTime) flag = "   ★ 晚于 ArtifactDB（可能未导入）";
    sb.AppendLine("    " + Path.GetFileName(w).PadRight(30) + " " + t.ToString("MM-dd HH:mm:ss") + flag);
}

// ---------- 描述函数 ----------
Func<Transform, string> pathOf = (t) =>
{
    var sbp = new StringBuilder(t.name);
    Transform cur = t.parent;
    while (cur != null)
    {
        sbp.Insert(0, cur.name + "/");
        cur = cur.parent;
    }
    return sbp.ToString();
};

Func<SkinnedMeshRenderer, string> desc = (s) =>
{
    var p = new List<string>();
    p.Add(pathOf(s.transform));
    p.Add("localMesh=" + (s.sharedMesh == null ? "null" : s.sharedMesh.name));
    int ml = 0;
    string mn = "";
    if (s.sharedMaterials != null)
    {
        ml = s.sharedMaterials.Length;
        for (int i = 0; i < s.sharedMaterials.Length; i++)
        {
            mn = mn + (i > 0 ? "," : "") + (s.sharedMaterials[i] == null ? "null" : s.sharedMaterials[i].name);
        }
    }
    p.Add("mats=" + ml + "[" + mn + "]");
    p.Add("bones=" + (s.bones == null ? "null" : s.bones.Length.ToString()));
    UnityEngine.Object srcObj = PrefabUtility.GetCorrespondingObjectFromSource(s);
    string srcTxt = "null";
    if (srcObj == null) srcTxt = "null";
    else if (srcObj == s) srcTxt = "自身";
    else
    {
        SkinnedMeshRenderer srcSmr = srcObj as SkinnedMeshRenderer;
        if (srcSmr == null) srcTxt = "源非SMR(" + srcObj.GetType().Name + ")";
        else if (srcSmr.sharedMesh == null) srcTxt = "有源但源mesh也是null";
        else srcTxt = "源mesh=" + srcSmr.sharedMesh.name + " 源bones=" + (srcSmr.bones == null ? "null" : srcSmr.bones.Length.ToString());
    }
    p.Add("源SMR:" + srcTxt);
    p.Add("isPrefabInst=" + PrefabUtility.IsPartOfPrefabInstance(s));
    p.Add("assetPath=" + AssetDatabase.GetAssetPath(s));
    return string.Join("  |  ", p.ToArray());
};

string[] assets =
{
    "Assets/_Project/Prefabs/Enemies/Z_Enemy_MoLong.prefab",
    "Assets/_Project/Prefabs/Enemies/Enemy_MoYan.prefab",
    "Assets/_Project/Prefabs/Ziyuan/Z_Dragon.prefab"
};

foreach (string path in assets)
{
    sb.AppendLine();
    sb.AppendLine("### " + path);

    GameObject go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
    sb.AppendLine("    LoadAssetAtPath        -> " + (go == null ? "null" : go.name + "  (子物体 " + go.transform.childCount + " 个)"));

    // ---- A：遍历全部（含 inactive）----
    if (go != null)
    {
        SkinnedMeshRenderer[] all = go.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        sb.AppendLine("    [A] GetComponentsInChildren(true) -> " + all.Length + " 个 SMR");
        for (int i = 0; i < all.Length; i++) sb.AppendLine("          " + desc(all[i]));

        SkinnedMeshRenderer[] onlyOn = go.GetComponentsInChildren<SkinnedMeshRenderer>(false);
        sb.AppendLine("    [A2] GetComponentsInChildren(false，不带 true) -> " + onlyOn.Length + " 个 SMR");

        // ---- B：单数 ----
        SkinnedMeshRenderer one = go.GetComponentInChildren<SkinnedMeshRenderer>(true);
        sb.AppendLine("    [B] GetComponentInChildren(true) -> " + (one == null ? "null" : desc(one)));
    }

    // ---- C：强制展开 ----
    GameObject contents = null;
    try
    {
        contents = PrefabUtility.LoadPrefabContents(path);
    }
    catch (Exception e)
    {
        sb.AppendLine("    [C] LoadPrefabContents 抛异常: " + e.GetType().Name + " " + e.Message);
    }
    if (contents != null)
    {
        SkinnedMeshRenderer[] allC = contents.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        sb.AppendLine("    [C] LoadPrefabContents -> " + allC.Length + " 个 SMR");
        for (int i = 0; i < allC.Length; i++) sb.AppendLine("          " + desc(allC[i]));
        PrefabUtility.UnloadPrefabContents(contents);
    }

    // ---- D2：实例化到当前场景再销毁（会短暂进场景，随即清理）----
    if (go != null)
    {
        GameObject inst = PrefabUtility.InstantiatePrefab(go) as GameObject;
        if (inst != null)
        {
            SkinnedMeshRenderer[] allD = inst.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            sb.AppendLine("    [D] InstantiatePrefab -> " + allD.Length + " 个 SMR");
            for (int i = 0; i < allD.Length; i++) sb.AppendLine("          " + desc(allD[i]));
            UnityEngine.Object.DestroyImmediate(inst);
            sb.AppendLine("        （临时实例已销毁；当前场景 isDirty="
                          + UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene().isDirty + "）");
        }
        else
        {
            sb.AppendLine("    [D] InstantiatePrefab -> null");
        }
    }
}

sb.AppendLine();
sb.AppendLine("===== 判据 =====");
sb.AppendLine("  ①②③④ 见脚本头部注释。核心一列是「源SMR」：它若解析不出来，");
sb.AppendLine("  而磁盘上 Z_Dragon.prefab 里 SMR#6533441441231540133 数据完整，就是导入陈旧。");
return sb.ToString();
