// a29_scale_truth.cs —— 定论：改祖先缩放，到底什么量会变？
//
// 迄今三种"顶点式"口径全部对祖先缩放免疫：
//   (1) BakeMesh + smr.transform.localToWorldMatrix
//   (2) smr.bounds
//   (3) sharedMesh.vertices × localToWorldMatrix
// 它们共同的数学结构：**被测量的量与该变换矩阵同源于同一套骨骼矩阵**
//   ⇒ 缩放同时进入"网格数据"和"我的矩阵"，相乘抵消。
//
// 本脚本做**受控实验**：把 holder.localScale 从 0.5 扫到 2.0，
// 同时记录四种量（含**骨骼世界位置**这个不受影响的参照），一次看清谁在变、谁不变。
//   · 骨骼世界位置：缩放祖先 → 骨骼世界坐标必然按 k 变 ⇒ 这是**真值**
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

var sb = new StringBuilder();
string root = Path.GetDirectoryName(Application.dataPath);

var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Enemies/Z_Enemy_MoLong.prefab");
sb.AppendLine("========== a29 缩放灵敏度受控实验 ==========");
if (prefab == null) { sb.AppendLine("★ 读不到 Z_Enemy_MoLong"); File.WriteAllText(Path.Combine(root, "Tools/reports/a29_scale_truth.txt"), sb.ToString()); Debug.Log(sb.ToString()); return; }

var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
go.transform.position = Vector3.zero;
go.transform.rotation = Quaternion.identity;
go.transform.localScale = Vector3.one;

var holder = go.transform.Find("Visual/Model");
if (holder == null) { sb.AppendLine("★ 没有 Visual/Model"); UnityEngine.Object.DestroyImmediate(go); File.WriteAllText(Path.Combine(root, "Tools/reports/a29_scale_truth.txt"), sb.ToString()); Debug.Log(sb.ToString()); return; }

sb.AppendLine("holder = " + holder.name + "  当前 localScale=" + holder.localScale.ToString("F4"));
sb.AppendLine("骨骼总数（去重）= " + CountBones(go));
sb.AppendLine();
sb.AppendLine(" k   | BakeMesh+l2w 体长 | smr.bounds 体长 | 骨骼世界范围 X | 骨骼世界范围 Y | 骨骼世界范围 Z");
sb.AppendLine("-----+------------------+-----------------+---------------+---------------+--------------");

foreach (float k in new[] { 0.5f, 1.0f, 1.53477f, 2.0f })
{
    holder.localScale = Vector3.one * k;

    // (1) BakeMesh + l2w
    float b1 = 0f;
    {
        var mn = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
        var mx = new Vector3(float.MinValue, float.MinValue, float.MinValue);
        foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (smr.sharedMesh == null) continue;
            var baked = new Mesh();
            smr.BakeMesh(baked, true);
            var l2w = smr.transform.localToWorldMatrix;
            foreach (var v in baked.vertices)
            { var p = l2w.MultiplyPoint3x4(v); mn = Vector3.Min(mn, p); mx = Vector3.Max(mx, p); }
            UnityEngine.Object.DestroyImmediate(baked);
        }
        b1 = Mathf.Max(mx.x - mn.x, mx.z - mn.z);
    }

    // (2) smr.bounds
    float b2 = 0f;
    {
        var mn = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
        var mx = new Vector3(float.MinValue, float.MinValue, float.MinValue);
        foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        { var bb = smr.bounds; mn = Vector3.Min(mn, bb.min); mx = Vector3.Max(mx, bb.max); }
        b2 = Mathf.Max(mx.x - mn.x, mx.z - mn.z);
    }

    // (3) 骨骼世界位置
    var bmn = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
    var bmx = new Vector3(float.MinValue, float.MinValue, float.MinValue);
    foreach (var t in DistinctBones(go))
    { var p = t.position; bmn = Vector3.Min(bmn, p); bmx = Vector3.Max(bmx, p); }

    sb.AppendLine(" " + k.ToString("F3").PadLeft(6) + " | "
        + b1.ToString("F4").PadLeft(16) + " | "
        + b2.ToString("F4").PadLeft(15) + " | "
        + (bmx.x - bmn.x).ToString("F4").PadLeft(13) + " | "
        + (bmx.y - bmn.y).ToString("F4").PadLeft(13) + " | "
        + (bmx.z - bmn.z).ToString("F4").PadLeft(13));
}

holder.localScale = Vector3.one;
UnityEngine.Object.DestroyImmediate(go);

sb.AppendLine();
sb.AppendLine("判读：");
sb.AppendLine("  · 若「骨骼世界范围」随 k 线性变化，而前两列不变 ⇒ 前两者对祖先缩放免疫，");
sb.AppendLine("    它们量的是「渲染器局部空间」的尺寸，**不能**用来验证祖先缩放。");
sb.AppendLine("  · 骨骼世界范围 = 唯一可信的『这个角色实际占多大』口径。");

File.WriteAllText(Path.Combine(root, "Tools/reports/a29_scale_truth.txt"), sb.ToString());
Debug.Log("[a29]\n" + sb.ToString());

int CountBones(GameObject g) { return DistinctBones(g).Count; }

List<Transform> DistinctBones(GameObject g)
{
    var set = new HashSet<Transform>();
    foreach (var smr in g.GetComponentsInChildren<SkinnedMeshRenderer>(true))
    {
        if (smr.bones == null) continue;
        foreach (var b in smr.bones) if (b != null) set.Add(b);
    }
    return new List<Transform>(set);
}
