// 只读：dump prefab 完整层级 + 每个 Renderer 的骨骼/网格/材质状态；并 dump 上游 FBX 源
// 目的：判定「骨架引用全 null」是 FBX 源坏了，还是 prefab 的覆盖项把它置空了
using System.Text;

var sb = new StringBuilder();

sb.AppendLine("Unity " + Application.unityVersion + "   isPlaying=" + UnityEditor.EditorApplication.isPlaying);
sb.AppendLine();

string[] prefabs = {
    "Assets/_Project/Prefabs/Enemies/Z_Enemy_MoShan.prefab",
    "Assets/_Project/Prefabs/Enemies/Z_Enemy_MoGuai.prefab",
    "Assets/_Project/Prefabs/Enemies/Z_Enemy_MoGu.prefab",
};
string[] fbx = {
    "Assets/ThirdParty/KayKit/Skeletons/Characters/Skeleton_Golem.fbx",
    "Assets/ThirdParty/Ziyuan/_fbx/mountain_orge.fbx",
    "Assets/ThirdParty/Ziyuan/_fbx/cursed_undead_soldier_rig.fbx",
    "Assets/ThirdParty/Ziyuan/_fbx/low-poly_orc.fbx",
};

void DumpTree(Transform t, string pad, StringBuilder o)
{
    var comps = new StringBuilder();
    foreach (var c in t.GetComponents<Component>())
    {
        if (c == null) { comps.Append("[MISSING] "); continue; }
        var n = c.GetType().Name;
        if (c is SkinnedMeshRenderer smr)
        {
            int nonNull = 0;
            if (smr.bones != null) foreach (var b in smr.bones) if (b != null) nonNull++;
            n += "(bones=" + (smr.bones == null ? -1 : smr.bones.Length) + " nonNull=" + nonNull
               + " root=" + (smr.rootBone == null ? "null" : smr.rootBone.name)
               + " mesh=" + (smr.sharedMesh == null ? "null" : smr.sharedMesh.name + "#" + smr.sharedMesh.vertexCount)
               + " mat=" + (smr.sharedMaterial == null ? "null" : smr.sharedMaterial.name)
               + " updOff=" + smr.updateWhenOffscreen + ") ";
        }
        else if (c is MeshRenderer mr)
        {
            var mf = t.GetComponent<MeshFilter>();
            n += "(mesh=" + (mf == null || mf.sharedMesh == null ? "null" : mf.sharedMesh.name + "#" + mf.sharedMesh.vertexCount)
               + " mat=" + (mr.sharedMaterial == null ? "null" : mr.sharedMaterial.name) + ") ";
        }
        comps.Append(n + " ");
    }
    o.AppendLine(pad + t.name + "  " + comps);
    for (int i = 0; i < t.childCount; i++) DumpTree(t.GetChild(i), pad + "   ", o);
}

foreach (var p in prefabs)
{
    sb.AppendLine("============================== " + p);
    var go = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(p);
    if (go == null) { sb.AppendLine("  加载失败"); sb.AppendLine(); continue; }
    sb.AppendLine("  根 " + go.name + "  prefabType=" + UnityEditor.PrefabUtility.GetPrefabAssetType(go)
                  + "  childCount=" + go.transform.childCount);
    for (int i = 0; i < go.transform.childCount; i++) DumpTree(go.transform.GetChild(i), "  ", sb);
    sb.AppendLine();
}

foreach (var p in fbx)
{
    sb.AppendLine("============================== [SOURCE FBX] " + p);
    var go = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(p);
    if (go == null) { sb.AppendLine("  加载失败"); sb.AppendLine(); continue; }
    sb.AppendLine("  根 " + go.name + "  childCount=" + go.transform.childCount);
    for (int i = 0; i < go.transform.childCount; i++) DumpTree(go.transform.GetChild(i), "  ", sb);
    sb.AppendLine();
}

System.IO.File.WriteAllText("Tools/reports/bf_tree.txt", sb.ToString(), new UTF8Encoding(false));
return "written Tools/reports/bf_tree.txt  chars=" + sb.Length;
