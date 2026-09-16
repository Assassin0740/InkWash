// a13_fix_prefab.cs —— 修好三个新敌人 prefab（终版）
//
// ============ 这一版为什么是对的（a9/a10/a11 三轮全错在哪）============
//
// a12 的 dump 揭开了真相，两个独立 bug 叠在一起：
//
//   【bug 1】a7 只删了 `Visual` 下名为 `Rig_Medium` / `Rig_Large` 的模型根，
//            但**模板里的 KayKit 怪其实是「一个骨架根 + 若干挂在 Visual 下的
//            SkinnedMesh 兄弟节点」**（Skeleton_Golem_* / Skeleton_Minion_* / Skeleton_Mage_*）。
//            这些兄弟节点**不叫 Rig_*** ⇒ 一个都没删掉。
//            它们由**已被删掉的旧骨骼**驱动 ⇒ 蒙皮退化为"停在 bindpose 原位"
//            ⇒ 顶点大量堆在原点附近或远处，把包围盒撑到离谱的值。
//            （实测 MoGuai 的 KayKit 残留把 Y 拉到 -3.842，而真正的模型在 [~0, 1.75]）
//
//   【bug 2】a9/a10/a11 里我写的是 `holder = visual.GetChild(0)`。
//            由于 bug 1 的残留节点就排在 `Model` **前面**（a12 dump 可证），
//            `GetChild(0)` 命中的是**第一个 KayKit 残留节点**而不是 `Model` 容器。
//            ⇒ 我改的是 KayKit 残留的 localScale。而它是蒙皮网格，
//              位置/大小由（已删的）骨骼决定 ⇒ **改它 localScale 完全不改变顶点世界位置**
//            ⇒ 这就解释了"连续三轮改 scale 读数一字不差"。
//            （a11 换成 BakeMesh 也没变，因为口径换了但对象还是错的）
//
// ============ 本版的正确口径 ============
//   1. 按**名字**找 `Visual/Model`（绝不 GetChild(index)）
//   2. 删掉 `Visual` 下所有非 `Model` 的子节点（KayKit 残留），删完要报数复核
//   3. 量顶点用 `BakeMesh`（蒙皮网格的唯一真值；直接读 sharedMesh.vertices 是 bindpose）
//      量的是 **holder 局部空间**（= 模型相对容器），因为要解的就是容器的 scale
//   4. 高度用 Y 的 [p1, p99]，脚底 = p1（绝对最低点对游离几何毫无抵抗力）
//   5. k = targetH / bodyH 写进 holder.localScale
//   6. holder.localPosition.y 把 p1 抬到 0
//   7. 复核：这次复核必须**在 Visual 的父坐标系下**，
//      并且要断言"改完读数是变的"（防止再出现不变量幻觉）
//
// 注意：`Model` 容器在 a7 里被建成了 `Visual` 的子对象，localScale=1。
//   模型自身还有一层 `Object_5`(1.6457) / `Object_4`(0.0029) / `GLTF_created_0`(0.0360)
//   —— 这些是**源 FBX 自带的**，保留不动，缩放统一加在 `Model` 上。
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

var sb = new StringBuilder();
string root = Path.GetDirectoryName(Application.dataPath);
const string EnemyDir = "Assets/_Project/Prefabs/Enemies";

// (prefab 名, 目标身高 m, 说明)
var jobs = new[]
{
    ("Z_Enemy_MoShan", 2.80f, "山怪"),
    ("Z_Enemy_MoGuai", 2.10f, "兽人"),
    ("Z_Enemy_MoGu",   1.90f, "不死兵"),
};

// ---- 顶点真值：BakeMesh 到世界，再乘 space 的逆矩阵 ----
List<Vector3> VertsIn(GameObject go, Transform space)
{
    var all = new List<Vector3>();
    var toLocal = space.worldToLocalMatrix;
    foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
    {
        if (smr.sharedMesh == null) continue;
        var baked = new Mesh();
        smr.BakeMesh(baked, true);          // ★ 蒙皮后真值（不能用 sharedMesh.vertices）
        var verts = baked.vertices;
        var l2w = smr.transform.localToWorldMatrix;
        for (int i = 0; i < verts.Length; i++)
            all.Add(toLocal.MultiplyPoint3x4(l2w.MultiplyPoint3x4(verts[i])));
        UnityEngine.Object.DestroyImmediate(baked);
    }
    // 顺带把非蒙皮的 MeshRenderer（兽人 Box001 之类）也算上，它们同样是"看得见的东西"
    foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
    {
        var smr = mf.GetComponent<SkinnedMeshRenderer>();
        if (smr != null) continue;
        if (mf.sharedMesh == null) continue;
        var verts = mf.sharedMesh.vertices;
        var l2w = mf.transform.localToWorldMatrix;
        for (int i = 0; i < verts.Length; i++)
            all.Add(toLocal.MultiplyPoint3x4(l2w.MultiplyPoint3x4(verts[i])));
    }
    return all;
}

void YStats(List<Vector3> vs, out float lo, out float hi, out float p1, out float p99)
{
    lo = hi = p1 = p99 = 0f;
    if (vs.Count == 0) return;
    var ys = new List<float>(vs.Count);
    foreach (var v in vs) ys.Add(v.y);
    ys.Sort();
    lo = ys[0]; hi = ys[ys.Count - 1];
    p1 = ys[Mathf.Clamp((int)(ys.Count * 0.01f), 0, ys.Count - 1)];
    p99 = ys[Mathf.Clamp((int)(ys.Count * 0.99f), 0, ys.Count - 1)];
}

sb.AppendLine("========== a13 修 prefab（删 KayKit 残留 + 按 Model 容器重定尺寸）==========");

foreach (var (nm, targetH, note) in jobs)
{
    string path = EnemyDir + "/" + nm + ".prefab";
    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
    sb.AppendLine();
    sb.AppendLine("================ " + nm + "（" + note + "，目标 " + targetH.ToString("F2") + " m）================");
    if (prefab == null) { sb.AppendLine("  ★ 读不到 " + path); continue; }

    var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
    go.transform.position = new Vector3(0f, 3000f, 0f);
    go.transform.rotation = Quaternion.identity;
    go.transform.localScale = Vector3.one;

    var visual = go.transform.Find("Visual");
    if (visual == null) { sb.AppendLine("  ★ 没有 Visual"); UnityEngine.Object.DestroyImmediate(go); continue; }

    // ============ 1) 删掉 Visual 下所有非 Model 的子节点（KayKit 残留）============
    var kept = new List<Transform>();
    var killed = new List<string>();
    for (int i = visual.childCount - 1; i >= 0; i--)
    {
        var c = visual.GetChild(i);
        if (c.name == "Model") continue;
        killed.Add(c.name + "(" + c.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length + "smr)");
        UnityEngine.Object.DestroyImmediate(c.gameObject);
    }
    sb.AppendLine("  [1] 删 Visual 下非 Model 子节点 ×" + killed.Count
                  + (killed.Count > 0 ? "  → " + string.Join(", ", killed.ToArray()) : ""));
    sb.AppendLine("      Visual 剩余子节点 " + visual.childCount + " 个");

    // ============ 2) 按名字找 Model 容器 ============
    var holder = visual.Find("Model");
    if (holder == null) { sb.AppendLine("  ★ 找不到 Visual/Model"); UnityEngine.Object.DestroyImmediate(go); continue; }
    sb.AppendLine("  [2] holder = " + PathOf(holder) + "  scale=" + holder.localScale.ToString("F4")
                  + " posY=" + holder.localPosition.y.ToString("F4"));

    // 复位
    holder.localScale = Vector3.one;
    holder.localPosition = Vector3.zero;

    // ============ 3) 量真值 ============
    var vs0 = VertsIn(go, holder);
    float lo0, hi0, p10, p990;
    YStats(vs0, out lo0, out hi0, out p10, out p990);
    sb.AppendLine("  [3] 复位后顶点 " + vs0.Count
                  + "  Y范围=[" + lo0.ToString("F3") + "," + hi0.ToString("F3") + "]"
                  + "  1~99分位=[" + p10.ToString("F3") + "," + p990.ToString("F3") + "]"
                  + "  分位高=" + (p990 - p10).ToString("F3"));

    float bodyH = p990 - p10;
    if (bodyH < 0.05f)
    {
        sb.AppendLine("  ★ 分位高异常 " + bodyH.ToString("F4") + " —— 拒绝按它归一，请人工检查");
        UnityEngine.Object.DestroyImmediate(go);
        continue;
    }

    // ============ 4) 缩放 ============
    float k = targetH / bodyH;
    holder.localScale = Vector3.one * k;
    var vs1 = VertsIn(go, holder);
    float lo1, hi1, p11, p991;
    YStats(vs1, out lo1, out hi1, out p11, out p991);
    sb.AppendLine("  [4] k = " + targetH.ToString("F2") + " / " + bodyH.ToString("F3")
                  + " = " + k.ToString("F4") + "  → 缩放后分位高 = " + (p991 - p11).ToString("F3")
                  + "（应 ≈ " + targetH.ToString("F2") + "，证明缩放确实生效）");

    // ============ 5) 落地 ============
    float dy = -p11;
    holder.localPosition = new Vector3(0f, dy, 0f);
    sb.AppendLine("  [5] 脚底(1分位)=" + p11.ToString("F3") + " ⇒ holder.localPosition.y = " + dy.ToString("F4"));

    // ============ 6) 复核：用 go 空间（= 角色根坐标系，决定"站在地上多高"）============
    var vsG = VertsIn(go, go.transform);
    float glo, ghi, gp1, gp99;
    YStats(vsG, out glo, out ghi, out gp1, out gp99);
    bool okH = Mathf.Abs(gp99 - gp1 - targetH) < 0.06f;
    bool okF = Mathf.Abs(gp1) < 0.01f;
    sb.AppendLine("  [6] ★复核(go 空间): Y范围=[" + glo.ToString("F3") + "," + ghi.ToString("F3")
                  + "]  分位高=" + (gp99 - gp1).ToString("F3")
                  + "  脚底偏离0 = " + gp1.ToString("F4"));
    sb.AppendLine("      身高 " + (okH ? "✓" : "★") + "  落地 " + (okF ? "✓" : "★"));

    // ============ 7) 顺带校正 Muzzle（远程怪用）与 CapsuleCollider ============
    // 高度变了 ⇒ 碰撞胶囊/枪口高度也得跟着走，否则打不中/兵不打人
    var cap = go.GetComponent<CapsuleCollider>();
    if (cap != null)
    {
        float oldH = cap.height, oldR = cap.radius, oldY = cap.center.y;
        cap.height = targetH * 0.92f;
        cap.radius = Mathf.Max(0.25f, targetH * 0.22f);
        cap.center = new Vector3(cap.center.x, targetH * 0.5f, cap.center.z);
        sb.AppendLine("  [7] CapsuleCollider: h " + oldH.ToString("F2") + "→" + cap.height.ToString("F2")
                      + "  r " + oldR.ToString("F2") + "→" + cap.radius.ToString("F2")
                      + "  centerY " + oldY.ToString("F2") + "→" + cap.center.y.ToString("F2"));
    }
    var muzzle = go.transform.Find("Muzzle");
    if (muzzle != null)
    {
        float oldY = muzzle.localPosition.y;
        muzzle.localPosition = new Vector3(muzzle.localPosition.x, targetH * 0.62f, muzzle.localPosition.z);
        sb.AppendLine("  [7] Muzzle.localPosition.y " + oldY.ToString("F3") + "→" + muzzle.localPosition.y.ToString("F3"));
    }

    // ============ 8) 存盘 ============
    PrefabUtility.ApplyPrefabInstance(go, InteractionMode.AutomatedAction);
    UnityEngine.Object.DestroyImmediate(go);
    sb.AppendLine("  [8] 已写回 " + path);
}

AssetDatabase.SaveAssets();
AssetDatabase.Refresh();

File.WriteAllText(Path.Combine(root, "Tools/reports/a13_fix_prefab.txt"), sb.ToString());
Debug.Log("[a13]\n" + sb.ToString());

string PathOf(Transform t)
{
    var chain = new List<string>();
    var cur = t;
    while (cur != null) { chain.Insert(0, cur.name); cur = cur.parent; }
    return string.Join("/", chain.ToArray());
}
