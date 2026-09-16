// a18_dragon_bones.cs —— 把龙骨按**空间位置**分类，找出 嘴/尾/翼/颈
//
// 为什么不能按名字找：龙的骨骼名是 `drgon_03` / `drgon_0204` 这种**无意义编号**
//   （Blender 转 glTF 时丢了语义名），按名字 grep 只会得到一个空手而归的循环，
//   而且**不会报错** —— 这正是本项目硬规矩 1 要防的"静默失效"。
//   ⇒ 唯一的可靠办法：**按 bind pose 的空间位置**判断。
//
// 判据（龙的原生姿态是水平爬行的长条，长轴 = Z）：
//   · 沿 Z 分位把骨骼分三段：前段（头方向）= 头/颈/嘴；后段 = 尾；中段 = 躯干
//   · 沿 |X| 分位：远离中线的 = 翼/腿
//   · 沿 Y 分位：最高的 = 翼尖/背鳍
//
// 输出每个候选骨骼的名字 + bind pose 局部位置 + 到根的距离，供后续挑选。
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

var sb = new StringBuilder();
string root = Path.GetDirectoryName(Application.dataPath);

var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Ziyuan/Z_Dragon.prefab");
if (prefab == null) { Debug.LogError("[a18] 读不到 Z_Dragon"); return; }

var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
go.transform.position = new Vector3(0f, 6000f, 0f);
go.transform.rotation = Quaternion.identity;

// ---- 1) 收集全部骨骼（Transform），记录**世界**（= 实例局部，因为根在原点、无旋转）位置 ----
var bones = new List<Transform>();
CollectBones(go.transform, bones);

sb.AppendLine("========== 龙骨盘点（按空间位置分类）==========");
sb.AppendLine("骨骼总数 = " + bones.Count);
sb.AppendLine();

// 模型整体包围盒（顶点真值）用于归一
var (bMin, bMax) = VertexBounds(go);
sb.AppendLine("顶点包围盒: X[" + bMin.x.ToString("F3") + "," + bMax.x.ToString("F3") + "]"
              + "  Y[" + bMin.y.ToString("F3") + "," + bMax.y.ToString("F3") + "]"
              + "  Z[" + bMin.z.ToString("F3") + "," + bMax.z.ToString("F3") + "]");
sb.AppendLine("（相对角色根；长轴=" + LongAxis(bMin, bMax) + "）");
sb.AppendLine();

// ---- 2) 全部骨骼列表（按 Z 排序 = 前后顺序）----
sb.AppendLine("---- 全部骨骼（按 Z 从后到前排序）----");
sb.AppendLine("  [idx] 名字                      局部位置(X,Y,Z)               到根距离  子数");
var sorted = new List<Transform>(bones);
sorted.Sort((a, b) => a.position.z.CompareTo(b.position.z));
for (int i = 0; i < sorted.Count; i++)
{
    var t = sorted[i];
    var p = t.position - go.transform.position;
    sb.AppendLine("  [" + i.ToString().PadLeft(3) + "] " + t.name.PadRight(26)
                  + " (" + p.x.ToString("F3").PadLeft(7) + "," + p.y.ToString("F3").PadLeft(7) + "," + p.z.ToString("F3").PadLeft(7) + ")"
                  + "  " + p.magnitude.ToString("F3").PadLeft(7)
                  + "  " + t.childCount);
}

// ---- 3) 自动分类候选 ----
sb.AppendLine();
sb.AppendLine("---- 自动分类候选 ----");
string longAx = LongAxis(bMin, bMax);
float zMin = bMin.z, zMax = bMax.z;
float zLen = Mathf.Max(0.001f, zMax - zMin);

var heads = new List<Transform>();
var tails = new List<Transform>();
var wings = new List<Transform>();
foreach (var t in sorted)
{
    var p = t.position - go.transform.position;
    float zn = (p.z - zMin) / zLen;          // 0=后 1=前
    if (longAx == "X") zn = (p.x - bMin.x) / zLen;
    if (zn > 0.72f) heads.Add(t);
    else if (zn < 0.25f) tails.Add(t);
    if (Mathf.Abs(p.x) > 0.6f) wings.Add(t);
}

sb.AppendLine("前段（头/颈/嘴，Z 归一 > 0.72）共 " + heads.Count + " 根：");
foreach (var t in heads)
{
    var p = t.position - go.transform.position;
    sb.AppendLine("    " + t.name.PadRight(26) + " (" + p.x.ToString("F3") + ", " + p.y.ToString("F3") + ", " + p.z.ToString("F3") + ")");
}
sb.AppendLine("后段（尾，Z 归一 < 0.25）共 " + tails.Count + " 根：");
foreach (var t in tails)
{
    var p = t.position - go.transform.position;
    sb.AppendLine("    " + t.name.PadRight(26) + " (" + p.x.ToString("F3") + ", " + p.y.ToString("F3") + ", " + p.z.ToString("F3") + ")");
}
sb.AppendLine("偏离中线（|X| > 0.6，翼/腿）共 " + wings.Count + " 根：");
foreach (var t in wings)
{
    var p = t.position - go.transform.position;
    sb.AppendLine("    " + t.name.PadRight(26) + " (" + p.x.ToString("F3") + ", " + p.y.ToString("F3") + ", " + p.z.ToString("F3") + ")");
}

UnityEngine.Object.DestroyImmediate(go);

File.WriteAllText(Path.Combine(root, "Tools/reports/a18_dragon_bones.txt"), sb.ToString());
Debug.Log("[a18]\n" + sb.ToString());

void CollectBones(Transform t, List<Transform> acc)
{
    // 排除 Mesh/SkinnedMeshRenderer 节点本身（它们不是骨骼）
    bool isMesh = t.GetComponent<SkinnedMeshRenderer>() != null || t.GetComponent<MeshFilter>() != null;
    if (!isMesh) acc.Add(t);
    for (int i = 0; i < t.childCount; i++) CollectBones(t.GetChild(i), acc);
}

(Vector3 min, Vector3 max) VertexBounds(GameObject go)
{
    var mn = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
    var mx = new Vector3(float.MinValue, float.MinValue, float.MinValue);
    var rootInv = go.transform.worldToLocalMatrix;
    foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
    {
        if (smr.sharedMesh == null) continue;
        var baked = new Mesh();
        smr.BakeMesh(baked, true);
        foreach (var v in baked.vertices)
        {
            var p = rootInv.MultiplyPoint3x4(smr.transform.localToWorldMatrix.MultiplyPoint3x4(v));
            mn = Vector3.Min(mn, p); mx = Vector3.Max(mx, p);
        }
        UnityEngine.Object.DestroyImmediate(baked);
    }
    foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
    {
        if (mf.GetComponent<SkinnedMeshRenderer>() != null || mf.sharedMesh == null) continue;
        foreach (var v in mf.sharedMesh.vertices)
        {
            var p = rootInv.MultiplyPoint3x4(mf.transform.localToWorldMatrix.MultiplyPoint3x4(v));
            mn = Vector3.Min(mn, p); mx = Vector3.Max(mx, p);
        }
    }
    return (mn, mx);
}

string LongAxis(Vector3 mn, Vector3 mx)
{
    float dx = mx.x - mn.x, dy = mx.y - mn.y, dz = mx.z - mn.z;
    if (dz >= dx && dz >= dy) return "Z";
    if (dx >= dy) return "X";
    return "Y";
}
