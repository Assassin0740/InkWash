// a17_dragon_survey.cs —— 龙的现状盘点（写程序驱动动作前的必做一步）
//
// 要回答的问题：
//   1. Z_Dragon / Z_Dragon_LP 的骨架长什么样？有没有嘴/尾/翼/颈这些**关键部位骨骼**？
//      （龙不能走 Humanoid —— summary 里已确认，所以只能靠程序驱动骨骼 Transform）
//   2. 它有多少顶点、多大尺寸、朝向哪个轴？（尺寸决定它能不能当 Boss 放进院子）
//   3. 有没有现成动画片段？
//   4. 场景里现有的 EnemyBase / EnemyRanged 是怎么组织的，龙能复用哪些？
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

var sb = new StringBuilder();
string root = Path.GetDirectoryName(Application.dataPath);

foreach (var nm in new[] { "Z_Dragon", "Z_Dragon_LP" })
{
    string path = "Assets/_Project/Prefabs/Ziyuan/" + nm + ".prefab";
    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
    sb.AppendLine("================ " + nm + " ================");
    if (prefab == null) { sb.AppendLine("★ 读不到 " + path); continue; }

    var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
    go.transform.position = new Vector3(0f, 5000f, 0f);
    go.transform.rotation = Quaternion.identity;

    // ---- 组件 ----
    sb.AppendLine("根组件: " + CompList(go));

    // ---- 顶点真值尺寸 ----
    var (minY, maxY, w, h, verts) = VertexBounds(go);
    sb.AppendLine("顶点 " + verts + " 个  高=" + h.ToString("F3") + " m  宽/深=" + w.ToString("F3")
                  + "  Y范围=[" + minY.ToString("F3") + "," + maxY.ToString("F3") + "]");

    // ---- 完整层级（骨骼名是重点）----
    sb.AppendLine();
    sb.AppendLine("层级（深度 ≤6）：");
    Walk(go.transform, 0, 6, sb);

    UnityEngine.Object.DestroyImmediate(go);
    sb.AppendLine();
}

// ---- 3) 源 FBX 的动画片段 ----
sb.AppendLine("================ 龙源 FBX 的片段 ================");
foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { "Assets/ThirdParty/Ziyuan" }))
{
    string p = AssetDatabase.GUIDToAssetPath(guid);
    string low = p.ToLower();
    if (!low.Contains("dragon")) continue;
    sb.AppendLine();
    sb.AppendLine("--- " + p + " ---");
    var clips = AssetDatabase.LoadAllAssetsAtPath(p);
    int n = 0;
    foreach (var o in clips)
    {
        if (o is AnimationClip c && !c.name.StartsWith("__preview__"))
        {
            sb.AppendLine("  [" + n++ + "] '" + c.name + "'  len=" + c.length.ToString("F3") + " s");
        }
    }
    if (n == 0) sb.AppendLine("  （无片段）");
}

// ---- 4) 玩家与敌方关键类的公共 API（决定龙能复用多少）----
sb.AppendLine();
sb.AppendLine("================ 可复用基建（读源码摘出公共成员）================");
foreach (var f in new[]
{
    "Assets/_Project/Scripts/Enemies/EnemyBase.cs",
    "Assets/_Project/Scripts/Enemies/EnemyRanged.cs",
    "Assets/_Project/Scripts/Enemies/InkProjectile.cs",
})
{
    if (!File.Exists(f)) { sb.AppendLine("★ 缺 " + f); continue; }
    sb.AppendLine();
    sb.AppendLine("--- " + Path.GetFileName(f) + " ---");
    foreach (var line in File.ReadAllLines(f))
    {
        string t = line.Trim();
        if (t.StartsWith("public ") || t.StartsWith("protected ") || t.StartsWith("[SerializeField]"))
        {
            if (t.StartsWith("//")) continue;
            sb.AppendLine("  " + t);
        }
    }
}

File.WriteAllText(Path.Combine(root, "Tools/reports/a17_dragon_survey.txt"), sb.ToString());
Debug.Log("[a17]\n" + sb.ToString());

string CompList(GameObject g)
{
    var l = new List<string>();
    foreach (var c in g.GetComponents<Component>())
        l.Add(c == null ? "[Missing]" : c.GetType().Name);
    return string.Join(", ", l.ToArray());
}

(float minY, float maxY, float w, float h, int verts) VertexBounds(GameObject go)
{
    float minY = float.MaxValue, maxY = float.MinValue;
    float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
    bool any = false; int cnt = 0;
    foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
    {
        if (smr.sharedMesh == null) continue;
        var baked = new Mesh();
        smr.BakeMesh(baked, true);
        var vs = baked.vertices;
        var l2w = smr.transform.localToWorldMatrix;
        for (int i = 0; i < vs.Length; i++)
        {
            var p = l2w.MultiplyPoint3x4(vs[i]);
            minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x);
            minY = Mathf.Min(minY, p.y); maxY = Mathf.Max(maxY, p.y);
            minZ = Mathf.Min(minZ, p.z); maxZ = Mathf.Max(maxZ, p.z);
            any = true;
        }
        cnt += vs.Length;
        UnityEngine.Object.DestroyImmediate(baked);
    }
    foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
    {
        if (mf.GetComponent<SkinnedMeshRenderer>() != null) continue;
        if (mf.sharedMesh == null) continue;
        var vs = mf.sharedMesh.vertices;
        var l2w = mf.transform.localToWorldMatrix;
        for (int i = 0; i < vs.Length; i++)
        {
            var p = l2w.MultiplyPoint3x4(vs[i]);
            minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x);
            minY = Mathf.Min(minY, p.y); maxY = Mathf.Max(maxY, p.y);
            minZ = Mathf.Min(minZ, p.z); maxZ = Mathf.Max(maxZ, p.z);
            any = true;
        }
        cnt += vs.Length;
    }
    if (!any) return (0, 0, 0, 0, 0);
    return (minY, maxY, Mathf.Max(maxX - minX, maxZ - minZ), maxY - minY, cnt);
}

void Walk(Transform t, int depth, int maxDepth, StringBuilder s)
{
    if (depth > maxDepth) return;
    string pad = new string(' ', depth * 2);
    var comps = new StringBuilder();
    foreach (var c in t.GetComponents<Component>())
    {
        if (c == null) { comps.Append("[Missing] "); continue; }
        comps.Append(c.GetType().Name);
        if (c is SkinnedMeshRenderer sm) comps.Append("[v=" + (sm.sharedMesh != null ? sm.sharedMesh.vertexCount : 0) + "]");
        if (c is MeshRenderer) comps.Append("[MeshR]");
        comps.Append(' ');
    }
    s.AppendLine(pad + t.name + "  " + comps);
    for (int i = 0; i < t.childCount; i++) Walk(t.GetChild(i), depth + 1, maxDepth, s);
}
