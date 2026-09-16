// a26_measure_probe.cs —— 搞清楚"改了祖先缩放，顶点式测量为何恒定不变"
//
// 假设（要证伪/证实）：`BakeMesh` + `smr.transform.localToWorldMatrix` 的组合
//   对**祖先缩放**免疫，因为：
//     · BakeMesh 输出的顶点是**渲染器局部空间**的蒙皮结果
//     · 祖先缩放会同时作用在骨骼（⇒ 蒙皮结果）**和** l2w 上 ⇒ 两次相乘相互抵消
//   ⇒ 于是读数恒定 —— 和 a13/a14 里那个"holder 空间自我抵消"是**同一个数学结构**，
//     只是这次藏得更深（藏在 BakeMesh 的语义里）。
//
// 本脚本用**三种互不相同的口径**量同一个对象，在同一帧内，并对照：
//   (1) BakeMesh + l2w           —— 我一直在用的（怀疑失效）
//   (2) smr.bounds（世界 AABB）  —— Unity 自己算的，不经过我
//   (3) 骨骼世界位置的范围        —— 完全绕开网格
// 如果 (1) 恒定而 (2)(3) 随缩放变化 ⇒ 假设成立，以后一律用 (2)/(3)。
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

var sb = new StringBuilder();
string root = Path.GetDirectoryName(Application.dataPath);

var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Enemies/Z_Enemy_MoLong.prefab");
sb.AppendLine("========== a26 三种口径对照 ==========");
if (prefab == null) { sb.AppendLine("★ 读不到"); File.WriteAllText(Path.Combine(root, "Tools/reports/a26_measure_probe.txt"), sb.ToString()); Debug.Log(sb.ToString()); return; }

var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
go.transform.position = Vector3.zero;
go.transform.rotation = Quaternion.identity;
go.transform.localScale = Vector3.one;

var holder = go.transform.Find("Visual/Model");
if (holder == null) { sb.AppendLine("★ 没有 Visual/Model"); UnityEngine.Object.DestroyImmediate(go); File.WriteAllText(Path.Combine(root, "Tools/reports/a26_measure_probe.txt"), sb.ToString()); Debug.Log(sb.ToString()); return; }

var visual = go.transform.Find("Visual");
sb.AppendLine("Visual.localScale = " + visual.localScale.ToString("F4"));
sb.AppendLine("Model.localScale  = " + holder.localScale.ToString("F4"));
sb.AppendLine("Model.localPos    = " + holder.localPosition.ToString("F4"));
sb.AppendLine();

// 让"祖先缩放"从 0.25x 变到 4x，看三种口径各自怎么动
foreach (float k in new[] { 0.25f, 0.5f, 1.0f, 2.0f, 4.0f })
{
    visual.localScale = Vector3.one * k;

    // (1) BakeMesh + l2w
    var mn1 = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
    var mx1 = new Vector3(float.MinValue, float.MinValue, float.MinValue);
    foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
    {
        if (smr.sharedMesh == null) continue;
        var baked = new Mesh();
        smr.BakeMesh(baked, true);
        var l2w = smr.transform.localToWorldMatrix;
        foreach (var v in baked.vertices)
        { var p = l2w.MultiplyPoint3x4(v); mn1 = Vector3.Min(mn1, p); mx1 = Vector3.Max(mx1, p); }
        UnityEngine.Object.DestroyImmediate(baked);
    }

    // (2) smr.bounds
    var mn2 = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
    var mx2 = new Vector3(float.MinValue, float.MinValue, float.MinValue);
    foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
    {
        var bb = smr.bounds;
        mn2 = Vector3.Min(mn2, bb.min); mx2 = Vector3.Max(mx2, bb.max);
    }

    // (3) 骨骼世界位置
    var mn3 = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
    var mx3 = new Vector3(float.MinValue, float.MinValue, float.MinValue);
    int nb = 0;
    foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
    {
        if (smr.bones == null) continue;
        foreach (var b in smr.bones)
        {
            if (b == null) continue;
            var p = b.position;
            mn3 = Vector3.Min(mn3, p); mx3 = Vector3.Max(mx3, p); nb++;
        }
    }

    sb.AppendLine("Visual.scale = " + k.ToString("F2") + "   骨骼数=" + nb);
    sb.AppendLine("  (1) BakeMesh+l2w   X=" + (mx1.x - mn1.x).ToString("F3") + " Y=" + (mx1.y - mn1.y).ToString("F3") + " Z=" + (mx1.z - mn1.z).ToString("F3"));
    sb.AppendLine("  (2) smr.bounds     X=" + (mx2.x - mn2.x).ToString("F3") + " Y=" + (mx2.y - mn2.y).ToString("F3") + " Z=" + (mx2.z - mn2.z).ToString("F3"));
    sb.AppendLine("  (3) 骨骼世界范围   X=" + (mx3.x - mn3.x).ToString("F3") + " Y=" + (mx3.y - mn3.y).ToString("F3") + " Z=" + (mx3.z - mn3.z).ToString("F3"));
    sb.AppendLine();
}

// 复位
visual.localScale = Vector3.one;
UnityEngine.Object.DestroyImmediate(go);

File.WriteAllText(Path.Combine(root, "Tools/reports/a26_measure_probe.txt"), sb.ToString());
Debug.Log("[a26]\n" + sb.ToString());
