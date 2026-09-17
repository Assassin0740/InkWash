// a8_verify_enemy.cs —— 实测三个新敌人 prefab（尺寸 / 骨骼绑定 / 动画是否真动）
//
// 为什么必须实测：a7 只是"改了引用"，但换模型有两个经典静默失效：
//   1. **Humanoid 绑定失败**：Animator 挂在根、骨骼在 Visual/Model 下隔了几层容器，
//      avatar 绑不上 ⇒ 角色**保持 T-Pose 不动**，而且控制台一条报错都没有。
//   2. **尺寸不对**：KayKit 的模型根被删了（它 scale=1，是 KayKit 的基准），
//      新素材的原始单位与它不同 ⇒ 站姿高度可能差几倍。
//
// 本脚本对每个新 prefab：
//   · 实例化（扔到场地外 y=2000）
//   · 量顶点真值高度（★ 只用顶点真值；renderer.bounds 会随姿态变，不可信）
//   · 逐帧推进 Animator，量髋/手位移 ⇒ 判定"动画是否真的在动"
//   · 打印从根到 SkinnedMeshRenderer 的层级路径（检查中间容器）
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

var sb = new StringBuilder();
string root = Path.GetDirectoryName(Application.dataPath);

const string TmpDir = "Assets/_Project/Animations/_A8Tmp";
if (!AssetDatabase.IsValidFolder(TmpDir))
    AssetDatabase.CreateFolder("Assets/_Project/Animations", "_A8Tmp");

// 顶点真值包围盒（忽略 SkinnedMesh 的 bindpose 陷阱：直接遍历顶点 × localToWorld）
(float minY, float maxY, float width, float height) VertexBounds(GameObject go)
{
    float minY = float.MaxValue, maxY = float.MinValue;
    float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
    bool any = false;
    foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
    {
        var m = smr.sharedMesh;
        if (m == null) continue;
        var verts = m.vertices;
        var rootM = go.transform;   // 用**场景根**的逆矩阵，得到 go 自身空间坐标
        var toLocal = rootM.worldToLocalMatrix;
        for (int i = 0; i < verts.Length; i++)
        {
            var wp = smr.transform.localToWorldMatrix.MultiplyPoint3x4(verts[i]);
            var lp = toLocal.MultiplyPoint3x4(wp);
            minX = Mathf.Min(minX, lp.x); maxX = Mathf.Max(maxX, lp.x);
            minY = Mathf.Min(minY, lp.y); maxY = Mathf.Max(maxY, lp.y);
            minZ = Mathf.Min(minZ, lp.z); maxZ = Mathf.Max(maxZ, lp.z);
            any = true;
        }
    }
    if (!any) return (0, 0, 0, 0);
    return (minY, maxY, Mathf.Max(maxX - minX, maxZ - minZ), maxY - minY);
}

// 逐帧推进 controller 的某状态，量骨骼位移
(float hips, float hand) SampleState(Animator anim, string stateName, float seconds)
{
    if (anim == null || anim.runtimeAnimatorController == null) return (-1f, -1f);
    anim.Play(stateName, 0, 0f);
    anim.Update(0.033f);

    var hips = anim.GetBoneTransform(HumanBodyBones.Hips);
    var hand = anim.GetBoneTransform(HumanBodyBones.RightHand);
    if (hips == null) return (-1f, -1f);

    int steps = Mathf.Clamp((int)(seconds / 0.033f), 8, 150);
    float hp = 0f, hd = 0f;
    Vector3 prevP = Vector3.zero, prevH = Vector3.zero;
    bool got = false;

    for (int i = 0; i <= steps; i++)
    {
        anim.Play(stateName, 0, Mathf.Clamp01(i / (float)steps));
        anim.Update(0.033f);
        var p = hips.position;
        if (got)
        {
            hp += Vector3.Distance(p, prevP);
            if (hand != null) hd += Vector3.Distance(hand.position, prevH);
        }
        prevP = p;
        if (hand != null) prevH = hand.position;
        got = true;
    }
    return (hp, hd);
}

sb.AppendLine("========== 三个新敌人 prefab 实测 ==========");
sb.AppendLine("（目标高度：山怪 2.6~3.0 / 兽人 2.0~2.2 / 不死兵 1.8~2.0 m）");
sb.AppendLine();

foreach (var (nm, targetH) in new[]
{
    ("Z_Enemy_MoShan", 2.80f),
    ("Z_Enemy_MoGuai", 2.10f),
    ("Z_Enemy_MoGu",   1.90f),
})
{
    string path = "Assets/_Project/Prefabs/Enemies/" + nm + ".prefab";
    sb.AppendLine("--- " + nm + "（目标 " + targetH + " m）---");
    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
    if (prefab == null) { sb.AppendLine("  ★ 读不到"); continue; }

    var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
    go.transform.position = new Vector3(0f, 2000f, 0f);
    go.transform.rotation = Quaternion.identity;

    var anim = go.GetComponentInChildren<Animator>(true);

    // 1) 层级路径（检查中间容器）
    var smrs = go.GetComponentsInChildren<SkinnedMeshRenderer>(true);
    sb.AppendLine("  SkinnedMeshRenderer " + smrs.Length + " 个");
    if (smrs.Length > 0)
    {
        string p = "";
        var t = smrs[0].transform;
        var chain = new List<string>();
        while (t != null) { chain.Insert(0, t.name); t = t.parent; }
        p = string.Join(" / ", chain.ToArray());
        sb.AppendLine("  路径: " + p);
    }

    // 2) 顶点真值尺寸
    var (minY, maxY, w, h) = VertexBounds(go);
    sb.AppendLine("  顶点真值: 高=" + h.ToString("F3") + " m  宽/深=" + w.ToString("F3")
                  + "  Y范围=[" + minY.ToString("F3") + "," + maxY.ToString("F3") + "]");
    float k = h > 0.001f ? targetH / h : 0f;
    sb.AppendLine("  → 若按目标高度归一，容器 localScale 应为 " + k.ToString("F4"));

    // 3) Animator 绑定 + 各状态动不动
    if (anim == null) sb.AppendLine("  ★ 无 Animator");
    else
    {
        sb.AppendLine("  avatar=" + (anim.avatar != null ? anim.avatar.name : "null")
                      + " isHuman=" + (anim.avatar != null && anim.avatar.isHuman));
        sb.AppendLine("  controller=" + (anim.runtimeAnimatorController != null ? anim.runtimeAnimatorController.name : "null"));
        var hipsT = anim.GetBoneTransform(HumanBodyBones.Hips);
        sb.AppendLine("  GetBoneTransform(Hips) = " + (hipsT != null ? hipsT.name + " (路径含 Visual? " + IsUnder(hipsT, "Visual") + ")" : "★ null（绑定失败）"));

        foreach (var st in new[] { "Idle", "Run", "Attack", "Hit", "Dead" })
        {
            var (hp, hd) = SampleState(anim, st, 1.2f);
            string v = hp < 0 ? "★ 取不到骨骼"
                : (hp + hd < 0.05f ? "★★ 静止（该状态无动作）" : "✓ 有动作");
            sb.AppendLine("    [" + st.PadRight(7) + "] 髋=" + hp.ToString("F3").PadLeft(7)
                          + " 手=" + hd.ToString("F3").PadLeft(7) + "  " + v);
        }
    }

    UnityEngine.Object.DestroyImmediate(go);
    sb.AppendLine();
}

AssetDatabase.DeleteAsset(TmpDir);

File.WriteAllText(Path.Combine(root, "Tools/reports/a8_verify_enemy.txt"), sb.ToString());
Debug.Log("[a8]\n" + sb.ToString());

bool IsUnder(Transform t, string name)
{
    var cur = t;
    while (cur != null) { if (cur.name == name) return true; cur = cur.parent; }
    return false;
}
