// a35_why_invisible.cs —— 龙为什么一个像素都不显示？
//
// a34 渲染五张图，全部只有参照物，龙**完全不可见** —— 但 `smr.bounds` 报出
//   X[-0.06,2.93] Y[0,6.80] Z[-4.57,2.43] 这么大一块。**有包围盒却画不出来**，
//   这是一个必须查到底的矛盾（本项目硬规矩 3：只差一项未通过 → 先怀疑度量本身，做对照实验）。
//
// 可能原因（逐项排查，全部用数据回答）：
//   ① 渲染器被禁用（enabled=false）
//   ② 材质 shader 的 Pass 被剔除 / 材质坏掉（比如水墨 shader 需要关键词未开）
//   ③ 顶点全被蒙皮到无穷远（骨骼矩阵 NaN）
//   ④ 网格子网格数为 0 / 索引损坏
//   ⑤ Layer 不在相机 cullingMask 内  ← a34 的相机是默认 cullingMask，应当包含一切
//   ⑥ 顶点坐标本身就是 NaN/Inf
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

var sb = new StringBuilder();
string root = Path.GetDirectoryName(Application.dataPath);

sb.AppendLine("========== a35 龙不可见原因排查 ==========");

// ★ 用源 prefab 的内容（a31/a32 已证：实例上读 bones 会出假象）
var go = PrefabUtility.LoadPrefabContents("Assets/_Project/Prefabs/Enemies/Z_Enemy_MoLong.prefab");
if (go == null) { sb.AppendLine("★ 读不到"); File.WriteAllText(Path.Combine(root, "Tools/reports/a35_why_invisible.txt"), sb.ToString()); Debug.LogError(sb.ToString()); return; }

sb.AppendLine();
sb.AppendLine("---- ① 渲染器状态 ----");
foreach (var r in go.GetComponentsInChildren<Renderer>(true))
{
    sb.AppendLine("  " + PathOf(r.transform));
    sb.AppendLine("     type=" + r.GetType().Name + "  enabled=" + r.enabled
                  + "  go.activeInHierarchy=" + r.gameObject.activeInHierarchy
                  + "  layer=" + r.gameObject.layer + "(" + LayerMask.LayerToName(r.gameObject.layer) + ")");
    sb.AppendLine("     materials=" + r.sharedMaterials.Length);
    for (int i = 0; i < r.sharedMaterials.Length; i++)
    {
        var m = r.sharedMaterials[i];
        if (m == null) { sb.AppendLine("       [" + i + "] ★ null"); continue; }
        sb.AppendLine("       [" + i + "] " + m.name + "  shader=" + (m.shader != null ? m.shader.name : "★ null")
                      + "  renderQueue=" + m.renderQueue);
        if (m.shader != null)
        {
            string errs;
            var msgs = ShaderUtil.GetShaderMessages(m.shader);
            errs = "";
            foreach (var msg in msgs) errs += msg.severity + ":" + msg.message + "; ";
            sb.AppendLine("           ShaderUtil 消息: " + (msgs.Length == 0 ? "（无）" : errs));
        }
    }
    // bounds
    sb.AppendLine("     bounds center=" + r.bounds.center.ToString("F3") + " size=" + r.bounds.size.ToString("F3"));
}

sb.AppendLine();
sb.AppendLine("---- ② 网格数据健康度 ----");
foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
{
    var m = smr.sharedMesh;
    sb.AppendLine("  " + smr.name + "  mesh=" + (m != null ? m.name : "★null"));
    if (m == null) continue;
    sb.AppendLine("     顶点=" + m.vertexCount + "  子网格=" + m.subMeshCount
                  + "  indexFormat=" + m.indexFormat);
    m.RecalculateBounds();
    sb.AppendLine("     mesh.bounds=" + m.bounds.center.ToString("F3") + " size=" + m.bounds.size.ToString("F3"));

    // 顶点非法值检查
    var vs = m.vertices;
    int nan = 0, inf = 0;
    var mn = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
    var mx = new Vector3(float.MinValue, float.MinValue, float.MinValue);
    foreach (var v in vs)
    {
        if (float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z)) { nan++; continue; }
        if (float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z)) { inf++; continue; }
        mn = Vector3.Min(mn, v); mx = Vector3.Max(mx, v);
    }
    sb.AppendLine("     bind pose 顶点范围: [" + mn.ToString("F2") + "] ~ [" + mx.ToString("F2") + "]"
                  + "  NaN=" + nan + " Inf=" + inf);

    // 蒙皮后的真值（BakeMesh 到世界）
    var baked = new Mesh();
    smr.BakeMesh(baked, true);
    var bmn = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
    var bmx = new Vector3(float.MinValue, float.MinValue, float.MinValue);
    int bnan = 0;
    foreach (var v in baked.vertices)
    {
        if (float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z)) { bnan++; continue; }
        bmn = Vector3.Min(bmn, v); bmx = Vector3.Max(bmx, v);
    }
    sb.AppendLine("     BakeMesh(渲染器局部) 范围: [" + bmn.ToString("F2") + "] ~ [" + bmx.ToString("F2") + "]  NaN=" + bnan);
    UnityEngine.Object.DestroyImmediate(baked);

    // 骨骼矩阵是否有 NaN
    int badBones = 0;
    if (smr.bones != null)
        foreach (var b in smr.bones)
        {
            if (b == null) { badBones++; continue; }
            var p = b.position;
            if (float.IsNaN(p.x) || float.IsNaN(p.y) || float.IsNaN(p.z)) badBones++;
        }
    sb.AppendLine("     骨骼（长度 " + (smr.bones == null ? -1 : smr.bones.Length) + "）异常数=" + badBones);
}

sb.AppendLine();
sb.AppendLine("---- ③ 相机可见性模拟（用 Unity 自己的 CullingResults 不现实，这里只报关键项）----");
sb.AppendLine("  默认相机 cullingMask = 一切（-1），所以 layer 不是原因（除非被改）");

PrefabUtility.UnloadPrefabContents(go);

File.WriteAllText(Path.Combine(root, "Tools/reports/a35_why_invisible.txt"), sb.ToString());
Debug.Log("[a35]\n" + sb.ToString());

string PathOf(Transform t)
{
    var l = new List<string>();
    var c = t;
    while (c != null) { l.Insert(0, c.name); c = c.parent; }
    return string.Join("/", l.ToArray());
}
