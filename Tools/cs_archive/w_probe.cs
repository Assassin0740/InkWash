// W_Sword（F3 古代宝剑）素材探针 —— 接进角色手骨之前，先把"它是谁、多长、朝哪、什么材质"量清楚。
//
// 要回答的问题：
//   1. 网格的**长轴**是哪一轴（剑身沿 X / Y / Z？）—— 决定挂到右手骨后的 localRotation
//   2. **原点在哪**：剑柄末端 / 剑柄中点 / 几何中心？—— 决定 bladeLocalOffset
//   3. 原生长度、宽、厚 —— 决定缩放倍率（要缩到世界 1.05 m 左右）
//   4. 有几个 submesh、生成的材质用的什么着色器（URP 下内置 Standard 必粉红）
//   5. 网格有没有 normals / tangents / UV —— 缺了就得重导或重算
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

IEnumerator Body()
{
    var sb = new System.Text.StringBuilder();
    string projRoot = System.IO.Path.GetDirectoryName(Application.dataPath);
    System.Action flush = () => System.IO.File.WriteAllText(
        System.IO.Path.Combine(projRoot, "Tools/reports/w_probe.txt"), sb.ToString());

    const string FBX = "Assets/W_Sword/W_Sword.FBX";

    sb.AppendLine("========== W_Sword（F3 古代宝剑）素材探针 ==========");
    sb.AppendLine("资产路径：" + FBX);
    sb.AppendLine();

    // ---------- 一、FBX 的全部子资产 ----------
    sb.AppendLine("=== 一、FBX 子资产清单 ===");
    var all = AssetDatabase.LoadAllAssetsAtPath(FBX);
    if (all == null || all.Length == 0) { sb.AppendLine("[ERR] 加载不到子资产"); flush(); yield break; }
    foreach (var o in all)
    {
        string extra = "";
        if (o is Mesh mm)
            extra = string.Format("  顶点{0} 三角{1} subMesh{2}", mm.vertexCount,
                mm.triangles.Length / 3, mm.subMeshCount);
        else if (o is Material mat)
            extra = string.Format("  shader={0}  路径={1}",
                mat.shader != null ? mat.shader.name : "null",
                mat.shader != null ? AssetDatabase.GetAssetPath(mat.shader) : "-");
        else if (o is GameObject g)
            extra = "  子节点数=" + g.transform.childCount;
        sb.AppendLine(string.Format("  [{0,-16}] {1}{2}", o.GetType().Name, o.name, extra));
    }
    sb.AppendLine();

    // ---------- 二、网格几何 ----------
    sb.AppendLine("=== 二、网格几何（局部空间，未旋转未缩放）===");
    var meshes = new List<Mesh>();
    foreach (var o in all) if (o is Mesh m2) meshes.Add(m2);

    if (meshes.Count == 0) { sb.AppendLine("[ERR] 没有 Mesh 子资产"); flush(); yield break; }

    foreach (var mesh in meshes)
    {
        var b = mesh.bounds;
        var v = mesh.vertices;
        sb.AppendLine(string.Format("网格 \"{0}\"：顶点 {1} / 三角 {2} / subMesh {3}",
            mesh.name, mesh.vertexCount, mesh.triangles.Length / 3, mesh.subMeshCount));
        sb.AppendLine(string.Format("  bounds.center = ({0:F4}, {1:F4}, {2:F4})", b.center.x, b.center.y, b.center.z));
        sb.AppendLine(string.Format("  bounds.size   = ({0:F4}, {1:F4}, {2:F4})", b.size.x, b.size.y, b.size.z));
        sb.AppendLine(string.Format("  bounds.min    = ({0:F4}, {1:F4}, {2:F4})", b.min.x, b.min.y, b.min.z));
        sb.AppendLine(string.Format("  bounds.max    = ({0:F4}, {1:F4}, {2:F4})", b.max.x, b.max.y, b.max.z));
        sb.AppendLine(string.Format("  normals={0}  tangents={1}  uv={2}  uv2={3}  colors={4}  bones={5}",
            mesh.normals.Length, mesh.tangents.Length, mesh.uv.Length, mesh.uv2.Length,
            mesh.colors.Length, mesh.bindposes.Length));

        // 长轴判定
        float dmax = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
        string axis = (dmax == b.size.x) ? "X" : (dmax == b.size.y ? "Y" : "Z");
        sb.AppendLine(string.Format("  → 长轴 = {0}（{1:F4}）  次轴 X={2:F4} Y={3:F4} Z={4:F4}",
            axis, dmax, b.size.x, b.size.y, b.size.z));

        // 沿长轴切片扫描：看截面（宽/厚）随长轴怎么变 —— 用来判断哪端是剑尖、哪端是柄
        int SEG = 12;
        float lo = 0f, hi = 0f;
        if (axis == "X") { lo = b.min.x; hi = b.max.x; }
        else if (axis == "Y") { lo = b.min.y; hi = b.max.y; }
        else { lo = b.min.z; hi = b.max.z; }

        sb.AppendLine(string.Format("  沿 {0} 轴 {1} 段剖面（看哪端收尖 = 剑尖）：", axis, SEG));
        for (int s = 0; s < SEG; s++)
        {
            float t0 = lo + (hi - lo) * (s / (float)SEG);
            float t1 = lo + (hi - lo) * ((s + 1) / (float)SEG);
            // 该段内：另两轴的跨度 + 顶点数
            float a0 = float.MaxValue, a1 = float.MinValue, c0 = float.MaxValue, c1 = float.MinValue;
            int cnt = 0;
            for (int i = 0; i < v.Length; i++)
            {
                float av = 0f, cv = 0f, lv = 0f;
                if (axis == "X") { lv = v[i].x; av = v[i].y; cv = v[i].z; }
                else if (axis == "Y") { lv = v[i].y; av = v[i].x; cv = v[i].z; }
                else { lv = v[i].z; av = v[i].x; cv = v[i].y; }
                if (lv < t0 || lv >= t1) continue;
                cnt++;
                if (av < a0) a0 = av; if (av > a1) a1 = av;
                if (cv < c0) c0 = cv; if (cv > c1) c1 = cv;
            }
            string bar = cnt == 0 ? "(空)" : string.Format("顶点{0,-6} 侧向跨 {1:F4}  另一向跨 {2:F4}",
                cnt, cnt == 0 ? 0f : a1 - a0, cnt == 0 ? 0f : c1 - c0);
            sb.AppendLine(string.Format("    [{0:F4} .. {1:F4}]  {2}", t0, t1, bar));
        }
        sb.AppendLine();
    }

    // ---------- 三、实例化后的实际朝向 ----------
    sb.AppendLine("=== 三、实例化后的世界朝向（root 无旋转时）===");
    var fbxAsset = AssetDatabase.LoadAssetAtPath<GameObject>(FBX);
    if (fbxAsset == null) { sb.AppendLine("[ERR] 加载不到 FBX GameObject"); flush(); yield break; }

    var inst = (GameObject)PrefabUtility.InstantiatePrefab(fbxAsset);
    inst.transform.position = Vector3.zero;
    inst.transform.rotation = Quaternion.identity;
    inst.transform.localScale = Vector3.one;

    sb.AppendLine("节点层级：");
    DumpTree(inst.transform, inst.transform, sb, 1);

    var rends = inst.GetComponentsInChildren<Renderer>(true);
    sb.AppendLine(string.Format("渲染器数量 = {0}", rends.Length));
    foreach (var r in rends)
    {
        string path = RelPath(r.transform, inst.transform);
        sb.AppendLine(string.Format("  {0}  ({1})", path, r.GetType().Name));
        sb.AppendLine(string.Format("    sharedMaterial = {0}",
            r.sharedMaterial != null ? r.sharedMaterial.name : "null"));
        if (r.sharedMaterial != null)
            sb.AppendLine(string.Format("    shader         = {0}",
                r.sharedMaterial.shader != null ? r.sharedMaterial.shader.name : "null"));
        sb.AppendLine(string.Format("    bounds(世界)   size={0:F4} center={1:F4}",
            r.bounds.size.ToString("F4"), r.bounds.center.ToString("F4")));
        sb.AppendLine(string.Format("    lossyScale     = {0:F4}   localPos = {1:F4}  localRot = {2}",
            r.transform.lossyScale.y, r.transform.localPosition.ToString("F4"),
            r.transform.localEulerAngles.ToString("F2")));
    }

    // 世界包围盒（把子节点的变换都算进去）
    var allRend = inst.GetComponentsInChildren<Renderer>(true);
    if (allRend.Length > 0)
    {
        var wb = allRend[0].bounds;
        for (int i = 1; i < allRend.Length; i++) wb.Encapsulate(allRend[i].bounds);
        sb.AppendLine(string.Format("整体世界包围盒 size = {0:F4}  center = {1:F4}", wb.size.ToString("F4"), wb.center.ToString("F4")));
        float dmax = Mathf.Max(wb.size.x, Mathf.Max(wb.size.y, wb.size.z));
        sb.AppendLine(string.Format("→ 实例后长轴 = {0}  原生长度 = {1:F4} m",
            dmax == wb.size.x ? "X" : (dmax == wb.size.y ? "Y" : "Z"), dmax));
        sb.AppendLine(string.Format("→ 缩到世界 1.05 m 需要 scale = {0:F4}", 1.05f / dmax));
    }

    Object.DestroyImmediate(inst);
    flush();
}

void DumpTree(Transform t, Transform root, System.Text.StringBuilder sb, int depth)
{
    var r = t.GetComponent<Renderer>();
    var mf = t.GetComponent<MeshFilter>();
    string tag = "";
    if (r != null) tag += " [Renderer]";
    if (mf != null && mf.sharedMesh != null) tag += string.Format(" [Mesh v={0} sub={1}]", mf.sharedMesh.vertexCount, mf.sharedMesh.subMeshCount);
    sb.AppendLine(string.Format("{0}{1}{2}  localPos={3} localScale={4}",
        new string(' ', depth * 2), t.name, tag,
        t.localPosition.ToString("F4"), t.localScale.ToString("F4")));
    for (int i = 0; i < t.childCount; i++) DumpTree(t.GetChild(i), root, sb, depth + 1);
}

string RelPath(Transform t, Transform root)
{
    var s = t.name;
    while (t.parent != null && t.parent != root) { t = t.parent; s = t.name + "/" + s; }
    return s;
}

return Body();
