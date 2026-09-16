// a36_bindpose.cs —— 判定"蒙皮爆炸"是不是 bindpose 不匹配，并找一个能让它正常显示的缩放
//
// a35 的关键数据：
//   · 网格顶点（bind pose）范围  ±2932  → 厘米级
//   · BakeMesh（蒙皮后，渲染器局部）范围 ±100000 → **爆炸了 34 倍**
//   · 273 根骨全部被判"异常"（我自己写的判据：世界坐标 NaN 或 null）
//
//   ⇒ 假设：**bindpose 与当前骨骼变换不匹配**。这是 glTF/FBX 转换里最常见的坑：
//     网格顶点以一种单位烘好，而骨骼的局部变换以另一种单位（或带不同的根缩放）烘好，
//     蒙皮矩阵 = boneMatrix × inverseBindMatrix 两者单位不一致 ⇒ 顶点被乘到一个巨大值。
//
//   ★ 为什么"看起来"是好的：a34 的 AABB 来自 `smr.bounds`，
//     而 Unity 对 SkinnedMeshRenderer 的 bounds 会退化用 **mesh.bounds × lossyScale**
//     （不真正算蒙皮），所以它给出"7 m 的合理值"，与渲染用的蒙皮结果**完全脱节**。
//     这是本项目硬规矩 3 的又一例：**度量与真相脱节时，先怀疑度量**。
//
// 本脚本：
//   ① 打印每根骨的世界坐标范围（看骨骼本身在什么尺度）
//   ② 扫一遍候选的"根缩放/单位修正"，看哪个 k 能让 BakeMesh 的范围回到合理量级
//      （合理判据：蒙皮后包围盒的最大边 ≤ 20 倍 bind pose 的最大边）
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

var sb = new StringBuilder();
string root = Path.GetDirectoryName(Application.dataPath);

sb.AppendLine("========== a36 bindpose / 蒙皮爆炸诊断 ==========");

var go = PrefabUtility.LoadPrefabContents("Assets/_Project/Prefabs/Ziyuan/Z_Dragon.prefab");
if (go == null) { sb.AppendLine("★ 读不到"); File.WriteAllText(Path.Combine(root, "Tools/reports/a36_bindpose.txt"), sb.ToString()); Debug.LogError(sb.ToString()); return; }

sb.AppendLine();
sb.AppendLine("---- ① 骨骼世界坐标范围（源 prefab，未做任何缩放）----");
{
    var mn = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
    var mx = new Vector3(float.MinValue, float.MinValue, float.MinValue);
    int n = 0, nulls = 0;
    foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
    {
        if (smr.bones == null) continue;
        foreach (var b in smr.bones)
        {
            if (b == null) { nulls++; continue; }
            var p = b.position;
            mn = Vector3.Min(mn, p); mx = Vector3.Max(mx, p); n++;
        }
    }
    sb.AppendLine("  有效骨骼=" + n + "  null=" + nulls);
    sb.AppendLine("  范围 [" + mn.ToString("F2") + "] ~ [" + mx.ToString("F2") + "]"
                  + "  尺寸=" + (mx - mn).ToString("F2"));
}

sb.AppendLine();
sb.AppendLine("---- ② 逐个 SkinnedMesh 看 bindpose 一致性 ----");
foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
{
    var m = smr.sharedMesh;
    if (m == null) continue;
    sb.AppendLine("  " + smr.name);
    var bp = m.bindposes;
    sb.AppendLine("    bindposes.Length=" + bp.Length + "  bones.Length=" + (smr.bones == null ? -1 : smr.bones.Length));
    // bindpose 的平移量级
    if (bp.Length > 0)
    {
        float maxT = 0f, maxS = 0f;
        foreach (var b in bp)
        {
            var t = b.GetColumn(3);
            maxT = Mathf.Max(maxT, t.magnitude);
            maxS = Mathf.Max(maxS, b.lossyScale.magnitude);
        }
        sb.AppendLine("    bindpose 平移最大量级=" + maxT.ToString("F2") + "  缩放最大量级=" + maxS.ToString("F6"));
    }
    // 骨骼局部缩放
    if (smr.bones != null && smr.bones.Length > 0 && smr.bones[0] != null)
        sb.AppendLine("    bones[0]=" + smr.bones[0].name + " lossyScale=" + smr.bones[0].lossyScale.ToString("F6"));
    sb.AppendLine("    smr.transform.lossyScale=" + smr.transform.lossyScale.ToString("F6"));
    sb.AppendLine("    rootBone=" + (smr.rootBone != null ? smr.rootBone.name + " lossyScale=" + smr.rootBone.lossyScale.ToString("F6") : "null"));

    // 蒙皮真值
    var baked = new Mesh();
    smr.BakeMesh(baked, true);
    var bmn = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
    var bmx = new Vector3(float.MinValue, float.MinValue, float.MinValue);
    foreach (var v in baked.vertices) { bmn = Vector3.Min(bmn, v); bmx = Vector3.Max(bmx, v); }
    float bakedMax = Mathf.Max(bmx.x - bmn.x, Mathf.Max(bmx.y - bmn.y, bmx.z - bmn.z));
    var vb = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
    var vx = new Vector3(float.MinValue, float.MinValue, float.MinValue);
    foreach (var v in m.vertices) { vb = Vector3.Min(vb, v); vx = Vector3.Max(vx, v); }
    float bindMax = Mathf.Max(vx.x - vb.x, Mathf.Max(vx.y - vb.y, vx.z - vb.z));
    sb.AppendLine("    bind pose 最大边=" + bindMax.ToString("F2")
                  + "  蒙皮后最大边=" + bakedMax.ToString("F2")
                  + "  比值=" + (bindMax > 0.001f ? (bakedMax / bindMax).ToString("F2") : "-")
                  + (bakedMax / Mathf.Max(0.001f, bindMax) > 20f ? "   ★★ 爆炸" : "   ✓ 正常量级"));
    UnityEngine.Object.DestroyImmediate(baked);
}

sb.AppendLine();
sb.AppendLine("---- ③ 候选口径：把 Model 容器缩到 1/k，看蒙皮是否回到正常量级 ----");
sb.AppendLine("   （若某个 k 让比值回到 ~1，说明问题是「网格与骨骼单位不一致」⇒ 用该 k 修正）");
{
    var smr = go.GetComponentInChildren<SkinnedMeshRenderer>();
    var m = smr.sharedMesh;
    var vb = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
    var vx = new Vector3(float.MinValue, float.MinValue, float.MinValue);
    foreach (var v in m.vertices) { vb = Vector3.Min(vb, v); vx = Vector3.Max(vx, v); }
    float bindMax = Mathf.Max(vx.x - vb.x, Mathf.Max(vx.y - vb.y, vx.z - vb.z));

    var modelT = smr.transform.parent;
    var origScale = modelT != null ? modelT.localScale : Vector3.one;
    sb.AppendLine("   Model 容器 = " + (modelT != null ? modelT.name : "null") + "  原 scale=" + origScale.ToString("F5"));
    sb.AppendLine("     k         蒙皮最大边        比值");
    foreach (float k in new[] { 1f, 0.1f, 0.01f, 0.001f, 0.0001f, 100f, 1000f })
    {
        if (modelT != null) modelT.localScale = Vector3.one * k;
        var baked = new Mesh();
        smr.BakeMesh(baked, true);
        var bmn = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
        var bmx = new Vector3(float.MinValue, float.MinValue, float.MinValue);
        foreach (var v in baked.vertices) { bmn = Vector3.Min(bmn, v); bmx = Vector3.Max(bmx, v); }
        float bm = Mathf.Max(bmx.x - bmn.x, Mathf.Max(bmx.y - bmn.y, bmx.z - bmn.z));
        sb.AppendLine("     " + k.ToString("F4").PadLeft(8) + "  " + bm.ToString("F2").PadLeft(14)
                      + "   " + (bindMax > 0.001f ? (bm / bindMax).ToString("F4") : "-"));
        UnityEngine.Object.DestroyImmediate(baked);
    }
    if (modelT != null) modelT.localScale = origScale;
}

PrefabUtility.UnloadPrefabContents(go);
File.WriteAllText(Path.Combine(root, "Tools/reports/a36_bindpose.txt"), sb.ToString());
Debug.Log("[a36]\n" + sb.ToString());
