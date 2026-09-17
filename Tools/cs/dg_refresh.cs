using UnityEditor;
using UnityEngine;

// 让 Unity 重新导入两个被回退的 prefab（改资产后必须 refresh，否则读数还是旧的）
string[] paths =
{
    "Assets/_Project/Prefabs/Ziyuan/Z_Dragon.prefab",
    "Assets/_Project/Prefabs/Enemies/Z_Enemy_MoLong.prefab"
};
foreach (string p in paths)
{
    AssetDatabase.ImportAsset(p, ImportAssetOptions.ForceUpdate);
    var go = AssetDatabase.LoadAssetAtPath<GameObject>(p);
    int smr = go == null ? -1 : go.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length;
    int withMesh = 0;
    if (go != null)
        foreach (var s in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            if (s.sharedMesh != null) withMesh++;
    Debug.Log("[REFRESH] " + p + "  SMR=" + smr + "  有mesh=" + withMesh);
}
AssetDatabase.SaveAssets();
return "REFRESH_OK";
