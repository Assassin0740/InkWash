// a31_dragon_measure.cs —— 用「Transform 层级世界位置」量尺寸（绕开一切网格语义）
//
// ============ 为什么改用量骨骼 Transform ============
// a29 揭示：龙的 `SkinnedMeshRenderer.bones` 数组长度 273 但**元素全是 null**。
//   这有两种可能：
//     (a) glTF 导入时骨骼引用真的丢了 ⇒ 蒙皮不生效 ⇒ 模型是"死的 bind pose"
//     (b) 它用的是 rootBone-only 变体，bones 只是占位
//   无论哪种，**`smr.bounds` 与 BakeMesh 的语义都变得可疑**（a29 里 bounds 缩放正常、
//   BakeMesh 却跳到 362 —— 两者矛盾，正说明底层数据不自洽）。
//
//   而「**Transform 层级**」是唯一不依赖网格/蒙皮语义的量：
//   缩放祖先 ⇒ 所有后代 Transform 的世界坐标**必然**按 k 变化。
//   龙的 272 根骨骼就是 272 个 Transform，它们的空间张成就是"这条龙占多大"。
//
// 本脚本：
//   1. 报 bones 数组的真实状态（长度 / 非 null 数 / rootBone）
//   2. 用 Transform 世界位置量尺寸
//   3. 扫 k 证明该口径对缩放敏感（灵敏度自证）
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

var sb = new StringBuilder();
string root = Path.GetDirectoryName(Application.dataPath);

var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Enemies/Z_Enemy_MoLong.prefab");
sb.AppendLine("========== a31 用 Transform 层级量尺寸 ==========");
if (prefab == null) { sb.AppendLine("★ 读不到"); File.WriteAllText(Path.Combine(root, "Tools/reports/a31_dragon_measure.txt"), sb.ToString()); Debug.LogError(sb.ToString()); return; }

var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
go.transform.position = Vector3.zero;
go.transform.rotation = Quaternion.identity;
go.transform.localScale = Vector3.one;

var holder = go.transform.Find("Visual/Model");

// ---- 1) bones 数组真实状态 ----
sb.AppendLine("---- bones 数组状态 ----");
foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
{
    var bones = smr.bones;
    int nonNull = 0;
    if (bones != null) foreach (var b in bones) if (b != null) nonNull++;
    sb.AppendLine("  " + smr.name + "  bones.Length=" + (bones == null ? -1 : bones.Length)
                  + "  非null=" + nonNull
                  + "  rootBone=" + (smr.rootBone != null ? smr.rootBone.name : "null")
                  + "  sharedMesh=" + (smr.sharedMesh != null ? smr.sharedMesh.name : "null")
                  + "  顶点=" + (smr.sharedMesh != null ? smr.sharedMesh.vertexCount : 0));
}

// ---- 2) Transform 世界范围（全部后代 Transform）----
sb.AppendLine();
sb.AppendLine("---- Transform 世界范围（全部后代）----");
sb.AppendLine("  k        X范围            Y范围            Z范围            体长");
foreach (float k in new[] { 0.5f, 1.0f, 1.53477f, 2.0f })
{
    if (holder != null) holder.localScale = Vector3.one * k;
    var (mn, mx) = TransformBounds(go);
    float lx = mx.x - mn.x, ly = mx.y - mn.y, lz = mx.z - mn.z;
    sb.AppendLine("  " + k.ToString("F3").PadLeft(6)
        + "  [" + mn.x.ToString("F2").PadLeft(7) + "," + mx.x.ToString("F2").PadLeft(7) + "]"
        + "  [" + mn.y.ToString("F2").PadLeft(7) + "," + mx.y.ToString("F2").PadLeft(7) + "]"
        + "  [" + mn.z.ToString("F2").PadLeft(7) + "," + mx.z.ToString("F2").PadLeft(7) + "]"
        + "  " + Mathf.Max(lx, lz).ToString("F3").PadLeft(8));
}
if (holder != null) holder.localScale = Vector3.one;

// ---- 3) 当前 prefab 的实际配置 + 用 Transform 口径给出的最终尺寸 ----
sb.AppendLine();
sb.AppendLine("---- 当前 prefab 的实际配置 ----");
if (holder != null)
{
    sb.AppendLine("  Model.localScale = " + holder.localScale.ToString("F5"));
    sb.AppendLine("  Model.localPos   = " + holder.localPosition.ToString("F4"));
    sb.AppendLine("  Model.localRot   = " + holder.localRotation.eulerAngles.ToString("F1"));
}
var (fmn, fmx) = TransformBounds(go);
float flx = fmx.x - fmn.x, fly = fmx.y - fmn.y, flz = fmx.z - fmn.z;
sb.AppendLine("  ★Transform 口径尺寸: X=" + flx.ToString("F3") + " Y=" + fly.ToString("F3") + " Z=" + flz.ToString("F3")
              + "  体长=" + Mathf.Max(flx, flz).ToString("F3"));
sb.AppendLine("  ★Transform 口径 Y范围: [" + fmn.y.ToString("F3") + "," + fmx.y.ToString("F3") + "]");

UnityEngine.Object.DestroyImmediate(go);
File.WriteAllText(Path.Combine(root, "Tools/reports/a31_dragon_measure.txt"), sb.ToString());
Debug.Log("[a31]\n" + sb.ToString());

(Vector3 mn, Vector3 mx) TransformBounds(GameObject go)
{
    var mn = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
    var mx = new Vector3(float.MinValue, float.MinValue, float.MinValue);
    foreach (var t in go.GetComponentsInChildren<Transform>(true))
    {
        var p = t.position;
        mn = Vector3.Min(mn, p); mx = Vector3.Max(mx, p);
    }
    return (mn, mx);
}
