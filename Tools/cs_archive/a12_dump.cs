// a12_dump.cs —— 直接 dump 新敌人 prefab 的完整层级 + 每个节点的 localScale
//
// 为什么需要它：连续三次"改了 scale 但读数不变"，我不该再猜测量口径了
//   —— 该怀疑的是**我改的那个节点根本不是模型容器**。
//   a8 报的路径 'Z_Enemy_MoGu / Visual / Skeleton_Mage_ArmLeft' 里**没有 Model 这一层**，
//   而 a7 明明新建了 Model 容器 ⇒ 强烈暗示：holder = visual.GetChild(0) 取错了节点。
//
// 本脚本不做任何修改，只把真实层级摊开（含 localScale / localPosition），
// 让"改哪个节点才对"变成看一眼就知道的事。
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

var sb = new StringBuilder();
string root = Path.GetDirectoryName(Application.dataPath);

foreach (var nm in new[] { "Z_Enemy_MoShan", "Z_Enemy_MoGuai", "Z_Enemy_MoGu" })
{
    string path = "Assets/_Project/Prefabs/Enemies/" + nm + ".prefab";
    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
    sb.AppendLine("================ " + nm + " ================");
    if (prefab == null) { sb.AppendLine("★ 读不到"); continue; }

    var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
    go.transform.position = new Vector3(0f, 3000f, 0f);
    go.transform.rotation = Quaternion.identity;

    void Walk(Transform t, int depth)
    {
        if (depth > 7) { sb.AppendLine(new string(' ', depth * 2) + "…（更深省略）"); return; }
        string pad = new string(' ', depth * 2);
        var comps = new StringBuilder();
        foreach (var c in t.GetComponents<Component>())
        {
            if (c == null) { comps.Append("[Missing] "); continue; }
            comps.Append(c.GetType().Name);
            if (c is SkinnedMeshRenderer sm) comps.Append("[verts=" + (sm.sharedMesh != null ? sm.sharedMesh.vertexCount : 0) + "]");
            if (c is MeshRenderer) comps.Append("[MeshR]");
            if (c is Animator a) comps.Append("[av=" + (a.avatar != null ? a.avatar.name : "null") + "]");
            comps.Append(' ');
        }
        sb.AppendLine(pad + t.name
            + "  scale=" + t.localScale.ToString("F4")
            + "  posY=" + t.localPosition.y.ToString("F4")
            + "  <" + comps + ">");
        for (int i = 0; i < t.childCount; i++) Walk(t.GetChild(i), depth + 1);
    }

    Walk(go.transform, 0);
    sb.AppendLine();

    UnityEngine.Object.DestroyImmediate(go);
}

File.WriteAllText(Path.Combine(root, "Tools/reports/a12_dump.txt"), sb.ToString());
Debug.Log("[a12] 完成 " + sb.Length + " 字符");
