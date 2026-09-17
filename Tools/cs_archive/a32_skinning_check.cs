// a32_skinning_check.cs —— 龙的蒙皮到底有没有生效？（决定程序驱动骨骼是否有意义）
//
// a31 报：`smr.bones.Length=273 但非null=0，rootBone=null`
//   ⇒ 如果这是真的，那龙的网格是**死 bind pose**，我写再多"驱动脊骨 Transform"的代码
//      都**不会在画面上有任何变化** —— 而且是静默的（不报错、不警告）。
//   ⇒ 这直接决定 <see cref="InkWash.Enemies.EnemyDragon"/> 这套方案是否成立，
//      必须在写更多代码之前查清。
//
// 但 a31 是在**预制体实例**上读的。`smr.bones` 在 prefab 实例上可能因为引用重定向
//   而读到 null（编辑器脚本的已知坑）。所以要**三种载体对照**：
//     ① 源 prefab（LoadPrefabContents，无实例间接）
//     ② 场景实例（InstantiatePrefab）—— a31 用的就是这种
//     ③ 直接 AddComponent 裸对象
//   如果三者的非null数不同 ⇒ 是读法问题，不是资产问题。
//
//   ★ 最终判据不看 bones 数组，而是**几何真值**：
//     把骨骼 Transform 人为旋转一个角度，然后用 BakeMesh 看顶点是否跟着变。
//     变了 ⇒ 蒙皮生效（程序驱动可行）；不变 ⇒ 蒙皮失效（程序驱动无效，要换方案）。
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

var sb = new StringBuilder();
string root = Path.GetDirectoryName(Application.dataPath);

sb.AppendLine("========== a32 龙蒙皮有效性三重判定 ==========");

// ---------------- ① 源 prefab via LoadPrefabContents ----------------
sb.AppendLine();
sb.AppendLine("---- ① 源 prefab（LoadPrefabContents，无实例间接）----");
{
    var c = PrefabUtility.LoadPrefabContents("Assets/_Project/Prefabs/Ziyuan/Z_Dragon.prefab");
    if (c == null) sb.AppendLine("  ★ 读不到");
    else
    {
        Report(c, sb, "  ");
        PrefabUtility.UnloadPrefabContents(c);
    }
}

// ---------------- ② 场景实例 ----------------
sb.AppendLine();
sb.AppendLine("---- ② 场景实例（InstantiatePrefab）----");
var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Ziyuan/Z_Dragon.prefab");
GameObject inst = null;
if (prefab != null)
{
    inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
    inst.transform.position = Vector3.zero;
    Report(inst, sb, "  ");
}

// ---------------- ③ 蒙皮有效性：转骨骼看顶点是否动 ----------------
sb.AppendLine();
sb.AppendLine("---- ③ ★蒙皮有效性：人为转骨骼，看顶点跟不跟 ----");
if (inst != null)
{
    var smr = inst.GetComponentInChildren<SkinnedMeshRenderer>();
    if (smr == null) sb.AppendLine("  ★ 没有 SkinnedMeshRenderer");
    else
    {
        // 先取一次"基准"顶点（BakeMesh）
        var before = BakeVerts(smr);

        // 找一根足够靠下的骨骼（骨骼树里随机取一个非根的），给它转 45°
        Transform target = null;
        var all = inst.GetComponentsInChildren<Transform>(true);
        foreach (var t in all)
        {
            if (t == inst.transform) continue;
            if (t.name.StartsWith("drgon_") && t.childCount > 0) { target = t; break; }
        }
        if (target == null) sb.AppendLine("  ★ 找不到可转的骨骼");
        else
        {
            sb.AppendLine("  旋转目标骨骼: " + target.name + "  (世界位置 " + target.position.ToString("F2") + ")");
            var orig = target.localRotation;
            target.localRotation = orig * Quaternion.Euler(0f, 0f, 45f);

            var after = BakeVerts(smr);
            float maxD = 0f, sumD = 0f;
            int n = Mathf.Min(before.Count, after.Count);
            for (int i = 0; i < n; i++)
            {
                float d = Vector3.Distance(before[i], after[i]);
                maxD = Mathf.Max(maxD, d); sumD += d;
            }
            sb.AppendLine("  顶点位移: 最大=" + maxD.ToString("F5") + "  平均=" + (n > 0 ? (sumD / n).ToString("F5") : "-")
                          + "  （顶点数 " + n + "）");
            sb.AppendLine("  判定: " + (maxD > 0.001f ? "✓ 蒙皮生效 —— 程序驱动骨骼**会**反映到画面上"
                                                       : "★★ 蒙皮失效 —— 转骨骼**不动**，程序驱动此路不通"));

            // 再试一根靠末端的（更可能被权重覆盖）
            Transform far = null;
            foreach (var t in all)
                if (t != inst.transform && t.name.StartsWith("drgon_") && t.childCount == 0) { far = t; break; }
            if (far != null)
            {
                target.localRotation = orig;
                var o2 = far.localRotation;
                far.localRotation = o2 * Quaternion.Euler(45f, 0f, 0f);
                var after2 = BakeVerts(smr);
                float m2 = 0f;
                for (int i = 0; i < Mathf.Min(before.Count, after2.Count); i++)
                    m2 = Mathf.Max(m2, Vector3.Distance(before[i], after2[i]));
                sb.AppendLine("  再试末端骨骼 " + far.name + "（无子节点）: 最大位移=" + m2.ToString("F5")
                              + (m2 > 0.001f ? "  ✓ 蒙皮生效" : "  ★★ 仍不动"));
                far.localRotation = o2;
            }
        }
    }
    UnityEngine.Object.DestroyImmediate(inst);
}

File.WriteAllText(Path.Combine(root, "Tools/reports/a32_skinning_check.txt"), sb.ToString());
Debug.Log("[a32]\n" + sb.ToString());

void Report(GameObject g, StringBuilder s, string pad)
{
    var smrs = g.GetComponentsInChildren<SkinnedMeshRenderer>(true);
    s.AppendLine(pad + "SkinnedMeshRenderer " + smrs.Length + " 个");
    foreach (var smr in smrs)
    {
        int nn = 0;
        if (smr.bones != null) foreach (var b in smr.bones) if (b != null) nn++;
        s.AppendLine(pad + "  " + smr.name
                     + "  bones=" + (smr.bones == null ? -1 : smr.bones.Length)
                     + "  非null=" + nn
                     + "  rootBone=" + (smr.rootBone != null ? smr.rootBone.name : "null")
                     + "  顶点=" + (smr.sharedMesh != null ? smr.sharedMesh.vertexCount : 0));
    }
}

List<Vector3> BakeVerts(SkinnedMeshRenderer smr)
{
    var list = new List<Vector3>();
    var m = new Mesh();
    smr.BakeMesh(m, true);
    list.AddRange(m.vertices);
    UnityEngine.Object.DestroyImmediate(m);
    return list;
}
