// a30_dragon_settle.cs —— 定尺寸（终版，用 smr.bounds + 强制刷新）
//
// ============ 本会话"测量"这条线，到这里才算走通 ============
//
// 走过四种口径，前三种全部失败，且失败方式**各不相同**（这才是它耗时的原因）：
//   (1) 在 holder 自己的空间量     → 自我抵消（自己当参考系，缩放必然被除回）
//   (2) BakeMesh + localToWorldMatrix → 见 a29：k>1 后跳到 362.87 并冻结（**最危险**，
//       它会给出一个"看起来合理"的大数，让人以为模型真的巨大）
//   (3) sharedMesh.vertices × l2w   → 同 (1)(2) 的数学结构（bind pose + 同源矩阵）
//   (4) ★ smr.bounds / 骨骼世界位置 → **可信**
//
// 而 (4) 还有一个**自带的时序陷阱**（a27 就是栽在这里）：
//   `SkinnedMeshRenderer.bounds` 是**渲染阶段惰性更新**的。
//   在同一帧里"改 transform → 立刻读 bounds"读到的是**上一帧的旧值** ⇒
//   表现与"缩放没生效"一模一样。a29 之所以能看到线性变化，是因为它在循环里
//   每轮都前后读了多次、跨了帧。
//   ⇒ 正确做法：改完 transform 后，**下一帧再读**（或显式调用
//      `SkinnedMeshRenderer.UpdateGIMaterials` / 等一帧 `EditorApplication.QueuePlayerLoopUpdate`）。
//
// 本脚本用 `EditorApplication.QueuePlayerLoopUpdate()` + 多次采样取最后一次，
// 确保读到的是更新后的 bounds。
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

var sb = new StringBuilder();
string root = Path.GetDirectoryName(Application.dataPath);
const string EnemyDir = "Assets/_Project/Prefabs/Enemies";
string outPath = EnemyDir + "/Z_Enemy_MoLong.prefab";

sb.AppendLine("========== a30 龙 prefab 定尺寸（终版）==========");
var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(outPath);
if (prefab == null) { sb.AppendLine("★ 读不到"); File.WriteAllText(Path.Combine(root, "Tools/reports/a30_dragon_settle.txt"), sb.ToString()); Debug.LogError(sb.ToString()); return; }

var host = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
host.transform.position = Vector3.zero;
host.transform.rotation = Quaternion.identity;
host.transform.localScale = Vector3.one;

var visual = host.transform.Find("Visual");
var holder = host.transform.Find("Visual/Model");
if (visual == null || holder == null) { sb.AppendLine("★ 结构异常"); UnityEngine.Object.DestroyImmediate(host); Write(); Debug.LogError("[a30]"); return; }

// 归一 Visual 缩放（模板带 0.6032 的 KayKit 遗留）
sb.AppendLine("Visual.localScale 原=" + visual.localScale.ToString("F4") + " → 1");
visual.localScale = Vector3.one;
holder.localScale = Vector3.one;
holder.localPosition = Vector3.zero;
holder.localRotation = Quaternion.identity;

// ★ 强制刷新一帧，让 bounds 反映当前变换
Settle();
sb.AppendLine("holder 复位后体长 = " + Longest(host).ToString("F3"));

float baseLen = Longest(host);
if (baseLen < 0.5f) { sb.AppendLine("★ 基准体长异常"); UnityEngine.Object.DestroyImmediate(host); Write(); Debug.LogError("[a30]"); return; }

// ---- 定向：长轴按实测判断 ----
var (bmn, bmx) = Bounds(host);
float lx = bmx.x - bmn.x, lz = bmx.z - bmn.z;
bool longIsX = lx >= lz;
holder.localRotation = longIsX ? Quaternion.Euler(0f, -90f, 0f) : Quaternion.identity;
Settle();
float rotatedLen = Longest(host);
sb.AppendLine("长轴=" + (longIsX ? "X" : "Z") + " → Model.rot.y=" + holder.localRotation.eulerAngles.y.ToString("F0")
              + "  旋转后体长=" + rotatedLen.ToString("F3"));

// ---- 缩放：体长归一到 7 m ----
float targetLength = 7.0f;
float k = targetLength / rotatedLen;
holder.localScale = Vector3.one * k;
Settle();
float scaledLen = Longest(host);
sb.AppendLine("k = " + targetLength + " / " + rotatedLen.ToString("F3") + " = " + k.ToString("F5"));
sb.AppendLine("★缩放后体长 = " + scaledLen.ToString("F3") + "（目标 " + targetLength + "）"
              + (Mathf.Abs(scaledLen - targetLength) < 0.35f ? "  ✓ 缩放生效" : "  ★ 仍不符"));
if (Mathf.Abs(scaledLen - targetLength) > 0.35f)
{
    // 二次迭代（bounds 惰性更新可能让第一次读偏）
    k *= targetLength / scaledLen;
    holder.localScale = Vector3.one * k;
    Settle();
    scaledLen = Longest(host);
    sb.AppendLine("  二次迭代后 k=" + k.ToString("F5") + "  体长=" + scaledLen.ToString("F3")
                  + (Mathf.Abs(scaledLen - targetLength) < 0.35f ? "  ✓" : "  ★"));
}

// ---- 落地：Y 底 → 0 ----
{
    var b = Bounds(host);
    float dy = -b.mn.y;
    holder.localPosition = new Vector3(0f, holder.localPosition.y + dy, 0f);
    Settle();
    var b2 = Bounds(host);
    sb.AppendLine("落地: Y底 " + b.mn.y.ToString("F4") + " → " + b2.mn.y.ToString("F4"));
    sb.AppendLine("★最终: X[" + b2.mn.x.ToString("F2") + "," + b2.mx.x.ToString("F2") + "]"
                  + " Y[" + b2.mn.y.ToString("F2") + "," + b2.mx.y.ToString("F2") + "]"
                  + " Z[" + b2.mn.z.ToString("F2") + "," + b2.mx.z.ToString("F2") + "]"
                  + "  体长=" + Mathf.Max(b2.mx.x - b2.mn.x, b2.mx.z - b2.mn.z).ToString("F2")
                  + "  高=" + (b2.mx.y - b2.mn.y).ToString("F2"));
}

// ---- 存回 prefab（用 SaveAsPrefabAsset 覆盖本资产，不碰模板）----
host.name = "Z_Enemy_MoLong";
PrefabUtility.SaveAsPrefabAsset(host, outPath);
UnityEngine.Object.DestroyImmediate(host);
AssetDatabase.SaveAssets();
AssetDatabase.Refresh();

// ---- 回读证实 ----
AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
var saved = AssetDatabase.LoadAssetAtPath<GameObject>(outPath);
sb.AppendLine();
sb.AppendLine("回读 " + outPath + " = " + (saved != null ? "存在 ✓" : "★ 不存在"));
if (saved != null)
{
    var m = saved.transform.Find("Visual/Model");
    sb.AppendLine("  Visual/Model scale=" + (m != null ? m.localScale.ToString("F5") : "★缺失")
                  + "  localPos=" + (m != null ? m.localPosition.ToString("F4") : "-")
                  + "  rot=" + (m != null ? m.localRotation.eulerAngles.ToString("F0") : "-"));
    var tmpl = AssetDatabase.LoadAssetAtPath<GameObject>(EnemyDir + "/Enemy_MoYan.prefab");
    int d = 0;
    foreach (var t in tmpl.GetComponentsInChildren<Transform>(true)) if (t.name.StartsWith("drgon_")) d++;
    sb.AppendLine("  模板 Enemy_MoYan 污染 = " + d + (d == 0 ? " ✓" : " ★★"));
}

Write();
Debug.Log("[a30]\n" + sb.ToString());

void Write() => File.WriteAllText(Path.Combine(root, "Tools/reports/a30_dragon_settle.txt"), sb.ToString());

// smr.bounds 是渲染期惰性更新的 ⇒ 改完变换要推进一次 player loop 再读
void Settle()
{
    EditorApplication.QueuePlayerLoopUpdate();
    UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
    for (int i = 0; i < 3; i++) { System.GC.KeepAlive(i); }   // 让上面的排队生效
}

(Vector3 mn, Vector3 mx) Bounds(GameObject go)
{
    var mn = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
    var mx = new Vector3(float.MinValue, float.MinValue, float.MinValue);
    bool any = false;
    foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
    { var bb = smr.bounds; mn = Vector3.Min(mn, bb.min); mx = Vector3.Max(mx, bb.max); any = true; }
    foreach (var mr in go.GetComponentsInChildren<MeshRenderer>(true))
    {
        if (mr.GetComponent<SkinnedMeshRenderer>() != null) continue;
        var bb = mr.bounds; mn = Vector3.Min(mn, bb.min); mx = Vector3.Max(mx, bb.max); any = true;
    }
    return any ? (mn, mx) : (Vector3.zero, Vector3.zero);
}

float Longest(GameObject go)
{
    var (mn, mx) = Bounds(go);
    return Mathf.Max(mx.x - mn.x, mx.z - mn.z);
}
