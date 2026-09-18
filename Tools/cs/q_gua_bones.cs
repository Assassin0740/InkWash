// 只读探针：对比墨怪各 SMR 的 bones 数组 —— 找出斧子（Object_25）为何不跟随骨骼
var sb = new System.Text.StringBuilder();

string P = "Assets/_Project/Prefabs/Enemies/Z_Enemy_MoGuai.prefab";
var root = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(P);
if (root == null) return "NULL";

string PathOf(UnityEngine.Transform t)
{
    string s = t.name;
    var p = t.parent;
    while (p != null) { s = p.name + "/" + s; p = p.parent; }
    return s;
}

var smrs = root.GetComponentsInChildren<UnityEngine.SkinnedMeshRenderer>(true);
var byName = new System.Collections.Generic.Dictionary<string, UnityEngine.SkinnedMeshRenderer>();
foreach (var s in smrs) byName[s.name] = s;

sb.AppendLine("===== SMR 清单 =====");
foreach (var s in smrs)
    sb.AppendLine("  " + s.name.PadRight(24)
        + " bones=" + s.bones.Length
        + " bindposes=" + (s.sharedMesh == null ? -1 : s.sharedMesh.bindposeCount)
        + " rootBone=" + (s.rootBone == null ? "NULL" : PathOf(s.rootBone)));
sb.AppendLine();

// 找 Object_25 的父链与位置
UnityEngine.SkinnedMeshRenderer t25 = null;
foreach (var s in smrs) if (s.name == "Object_25") { t25 = s; break; }
if (t25 != null)
{
    sb.AppendLine("Object_25 节点路径 = " + PathOf(t25.transform));
    sb.AppendLine("  localPos=" + t25.transform.localPosition.ToString("F3")
        + "  localScale=" + t25.transform.localScale.ToString("F4"));
    // 它的骨骼里，被 Axe Dummy 影响？
    for (int i = 0; i < t25.bones.Length; i++)
    {
        if (t25.bones[i] == null) continue;
        if (t25.bones[i].name.IndexOf("Axe", System.StringComparison.OrdinalIgnoreCase) >= 0)
            sb.AppendLine("  骨骼[" + i + "] = " + PathOf(t25.bones[i]) + "  ← Axe 相关");
    }
    sb.AppendLine();
}

var refS = byName.ContainsKey("Object_7") ? byName["Object_7"] : null;
if (refS != null)
{
    sb.AppendLine("===== 以 Object_7（身体，正常跟随）为基准，逐元素对比 bones =====");
    foreach (var s in smrs)
    {
        if (s == refS) continue;
        int n = UnityEngine.Mathf.Min(s.bones.Length, refS.bones.Length);
        int diff = UnityEngine.Mathf.Abs(s.bones.Length - refS.bones.Length);
        var first = new System.Text.StringBuilder();
        for (int i = 0; i < n; i++)
        {
            string na = refS.bones[i] == null ? "<NULL>" : refS.bones[i].name;
            string nb = s.bones[i] == null ? "<NULL>" : s.bones[i].name;
            if (na != nb) { diff++; if (diff <= 5) first.Append("\n        [" + i + "] ref=" + na + "   本件=" + nb); }
        }
        sb.AppendLine("  " + s.name.PadRight(24) + " 不同元素=" + diff + first);
    }
    sb.AppendLine();
    sb.AppendLine("===== Object_7 的 bones 前 10 / 后 5 =====");
    for (int i = 0; i < UnityEngine.Mathf.Min(10, refS.bones.Length); i++)
        sb.AppendLine("  [" + i + "] " + PathOf(refS.bones[i]));
    for (int i = UnityEngine.Mathf.Max(0, refS.bones.Length - 5); i < refS.bones.Length; i++)
        sb.AppendLine("  [" + i + "] " + PathOf(refS.bones[i]));
}

System.IO.File.WriteAllText("Tools/reports/q_gua_bones.txt", sb.ToString());
return "q_gua_bones done";
