// 逐个蒙皮网格地量 —— 确认 Player/Visual 下 8 个网格里哪个是"角色本体"，
// 以及 Feng 的 1 个网格是否为纯角色。避免并集包围盒把武器/特效算进身高。
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

IEnumerator Body()
{
    var sb = new System.Text.StringBuilder();
    var m = new Mesh();

    sb.AppendLine("========== Player.prefab / Visual 逐个蒙皮网格 ==========");
    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Player/Player.prefab");
    var pl = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
    foreach (var mb in pl.GetComponentsInChildren<MonoBehaviour>(true)) if (mb != null) mb.enabled = false;
    pl.transform.position = Vector3.zero; pl.transform.rotation = Quaternion.identity; pl.transform.localScale = Vector3.one;
    Dump(pl, sb, m, "Player");
    // 再看 Visual 的父链与兄弟，确认没有别的渲染器
    var vis = pl.transform.Find("Visual");
    if (vis != null)
    {
        sb.AppendLine("  -- Visual 的父链 --");
        var t = vis.parent;
        while (t != null) { sb.AppendLine("     ^ " + t.name); t = t.parent; }
    }
    Object.DestroyImmediate(pl);

    sb.AppendLine("========== Feng.fbx 逐个蒙皮网格 ==========");
    var fbx = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Char_Feng/Fbx/Feng.fbx");
    var feng = Object.Instantiate(fbx);
    feng.transform.position = Vector3.zero; feng.transform.rotation = Quaternion.identity; feng.transform.localScale = Vector3.one;
    Dump(feng, sb, m, "Feng");
    Object.DestroyImmediate(feng);

    Object.DestroyImmediate(m);
    Debug.Log(sb.ToString());
    yield return null;
}

void Dump(GameObject root, System.Text.StringBuilder sb, Mesh m, string tag)
{
    var smrs = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
    var mrs = root.GetComponentsInChildren<MeshRenderer>(true);
    sb.AppendLine(string.Format("  [{0}] SkinnedMeshRenderer={1}  MeshRenderer={2}", tag, smrs.Length, mrs.Length));
    foreach (var s in smrs)
    {
        string path = Path(s.transform, root.transform);
        if (s.sharedMesh == null) { sb.AppendLine("    (无网格) " + path); continue; }
        s.BakeMesh(m, true);
        var l2w = s.transform.localToWorldMatrix;
        var v = m.vertices;
        float mn = float.MaxValue, mx = float.MinValue, mnx = float.MaxValue, mxx = float.MinValue;
        for (int i = 0; i < v.Length; i++)
        {
            var w = l2w.MultiplyPoint3x4(v[i]);
            if (w.y < mn) mn = w.y; if (w.y > mx) mx = w.y;
            if (w.x < mnx) mnx = w.x; if (w.x > mxx) mxx = w.x;
        }
        sb.AppendLine(string.Format("    {0,-34} 顶点{1,-6} 高 {2:F4}  (y {3:F4}..{4:F4})  宽 {5:F4}",
            path, v.Length, mx - mn, mn, mx, mxx - mnx));
    }
    foreach (var r in mrs)
    {
        var f = r.GetComponent<MeshFilter>();
        string path = Path(r.transform, root.transform);
        if (f == null || f.sharedMesh == null) continue;
        var b = f.sharedMesh.bounds; var l2w = r.transform.localToWorldMatrix;
        var c = l2w.MultiplyPoint3x4(b.center);
        sb.AppendLine(string.Format("    [MeshRenderer] {0,-24} 局部高 {1:F4} 世界中心 y {2:F4}", path, b.size.y, c.y));
    }
}

string Path(Transform t, Transform root)
{
    var s = t.name;
    while (t.parent != null && t.parent != root) { t = t.parent; s = t.name + "/" + s; }
    return s;
}

return Body();
