// 方案 B 的真身：把 Ziyuan 敌人的 SkinnedMeshRenderer 骨骼引用接回 prefab 内的同名 Transform
//
// 背景（已实测）：
//   三个敌人的 prefab 本质上就是 Ziyuan FBX 的完整拷贝：
//     Z_Enemy_MoShan  <- mountain_orge.fbx           骨架 _rootJoint + mixamorig:*  26 根   14 SMR
//     Z_Enemy_MoGuai  <- low-poly_orc.fbx            骨架 _rootJoint + Bip001_01     97 根   10 SMR
//     Z_Enemy_MoGu     <- cursed_undead_soldier_rig.fbx 同左                        80 根    3 SMR
//   源 FBX 里这些 SMR 的 bones 全部有效（26/26、97/97、80/80），
//   但 prefab 里同一个 SMR 的 m_Bones 被写成了全 {fileID: 0}（MoShan 364 个、MoGuai 970 个、MoGu 240 个），
//   m_RootBone 也是 {fileID: 0} ⇒ 蒙皮退化 ⇒ 三角形不被光栅化 ⇒ 实渲 0 像素。
//
// 本脚本：按源 FBX 里【同名 SMR 的 bones 顺序】把 prefab 内【同名 Transform】接回去。
// 顺序必须照抄源 FBX —— 它必须与 mesh.bindposes 的顺序一致，不能自己排序。
//
// APPLY=false 为只读预演（不落盘）；确认匹配无误后改 true 再跑。
using System.Text;
using System.Collections.Generic;
using UnityEditor;

bool APPLY = true;

string[] jobs = {
    "Assets/ThirdParty/Ziyuan/_fbx/mountain_orge.fbx|Assets/_Project/Prefabs/Enemies/Z_Enemy_MoShan.prefab",
    "Assets/ThirdParty/Ziyuan/_fbx/low-poly_orc.fbx|Assets/_Project/Prefabs/Enemies/Z_Enemy_MoGuai.prefab",
    "Assets/ThirdParty/Ziyuan/_fbx/cursed_undead_soldier_rig.fbx|Assets/_Project/Prefabs/Enemies/Z_Enemy_MoGu.prefab",
};

var sb = new StringBuilder();
sb.AppendLine("=== bf_fix   APPLY=" + APPLY + "   isPlaying=" + EditorApplication.isPlaying);

Transform FindDeep(Transform t, string n)
{
    if (t == null) return null;
    if (t.name == n) return t;
    for (int i = 0; i < t.childCount; i++)
    {
        var r = FindDeep(t.GetChild(i), n);
        if (r != null) return r;
    }
    return null;
}

foreach (var job in jobs)
{
    var parts = job.Split('|');
    string srcPath = parts[0], pPath = parts[1];
    sb.AppendLine();
    sb.AppendLine("################ " + pPath.Substring(pPath.LastIndexOf('/') + 1) + "   <- " + srcPath.Substring(srcPath.LastIndexOf('/') + 1));

    var srcGo = AssetDatabase.LoadAssetAtPath<GameObject>(srcPath);
    if (srcGo == null) { sb.AppendLine("  !! 源 FBX 加载失败"); continue; }

    // 1) 从源 FBX 抄「同名 SMR 的骨骼名字序列」——这就是 mesh.bindposes 的顺序
    var wantBones = new Dictionary<string, string[]>();
    var wantRoot = new Dictionary<string, string>();
    foreach (var s in srcGo.GetComponentsInChildren<SkinnedMeshRenderer>(true))
    {
        var arr = new string[s.bones.Length];
        int nn = 0;
        for (int i = 0; i < s.bones.Length; i++)
        {
            arr[i] = s.bones[i] != null ? s.bones[i].name : null;
            if (arr[i] != null) nn++;
        }
        wantBones[s.gameObject.name] = arr;
        wantRoot[s.gameObject.name] = s.rootBone != null ? s.rootBone.name : null;
        sb.AppendLine("  源 " + s.gameObject.name.PadRight(12) + " bones=" + arr.Length + " 有效=" + nn + " root=" + wantRoot[s.gameObject.name]);
    }

    // 2) 打开 prefab 内容
    var contents = PrefabUtility.LoadPrefabContents(pPath);
    if (contents == null) { sb.AppendLine("  !! prefab 加载失败"); continue; }

    var rj = FindDeep(contents.transform, "_rootJoint");
    sb.AppendLine("  prefab 骨架根 _rootJoint = " + (rj != null ? ("找到，子树 Transform 数 " + rj.GetComponentsInChildren<Transform>(true).Length) : "**找不到**"));
    sb.AppendLine("  prefab SMR 总数 = " + contents.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length);

    int done = 0, missB = 0, missRoot = 0;
    foreach (var smr in contents.GetComponentsInChildren<SkinnedMeshRenderer>(true))
    {
        string key = smr.gameObject.name;
        if (!wantBones.ContainsKey(key)) continue;

        var want = wantBones[key];
        var got = new Transform[want.Length];
        int nn = 0;
        var missing = new List<string>();
        for (int i = 0; i < want.Length; i++)
        {
            if (want[i] == null) continue;
            // 优先在 _rootJoint 子树内找；找不到再全 prefab 找（骨架可能挂在别处）
            var t = rj != null ? FindDeep(rj, want[i]) : null;
            if (t == null) t = FindDeep(contents.transform, want[i]);
            got[i] = t;
            if (t != null) nn++; else { missB++; if (missing.Count < 4) missing.Add(want[i]); }
        }

        Transform newRoot = wantRoot[key] == null ? null : FindDeep(contents.transform, wantRoot[key]);
        if (wantRoot[key] != null && newRoot == null) missRoot++;

        sb.AppendLine("  " + key.PadRight(12)
                      + " 现 bones=" + smr.bones.Length + " 非null=" + (smr.bones == null ? -1 : System.Array.FindAll(smr.bones, b => b != null).Length)
                      + "  ->  目标 " + nn + "/" + want.Length
                      + "  root=" + (newRoot != null ? newRoot.name : "<缺失>")
                      + (missing.Count > 0 ? "  缺: " + string.Join(",", missing.ToArray()) : ""));

        if (APPLY)
        {
            smr.bones = got;
            smr.rootBone = newRoot;
            EditorUtility.SetDirty(smr);
        }
        done++;
    }
    sb.AppendLine("  ⇒ 处理 " + done + " 个 SMR；缺骨骼引用 " + missB + "；缺 rootBone " + missRoot);

    if (APPLY)
    {
        PrefabUtility.SaveAsPrefabAsset(contents, pPath);
        sb.AppendLine("  >>> 已写回 " + pPath);
    }
    PrefabUtility.UnloadPrefabContents(contents);
}

System.IO.File.WriteAllText("Tools/reports/bf_fix.txt", sb.ToString(), new UTF8Encoding(false));
return sb.ToString();
