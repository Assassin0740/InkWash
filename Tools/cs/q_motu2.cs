// 只读探针：dump 墨徒 9 个 SMR 的本地包围盒 + 骨骼绑定明细
// 目的：找出哪块网格包含武器、哪块绑到了不动的骨骼
var sb = new System.Text.StringBuilder();

string P = "Assets/_Project/Prefabs/Enemies/Enemy_MoTu.prefab";
var root = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(P);
if (root == null) return "prefab NULL";

var smrs = root.GetComponentsInChildren<UnityEngine.SkinnedMeshRenderer>(true);
sb.AppendLine("SMR 总数 = " + smrs.Length);
sb.AppendLine();

foreach (var s in smrs)
{
    var b = s.localBounds;
    var mesh = s.sharedMesh;
    sb.AppendLine("── " + s.name);
    sb.AppendLine("   mesh      = " + (mesh == null ? "NULL" : mesh.name + "  v=" + mesh.vertexCount + "  subMesh=" + mesh.subMeshCount));
    sb.AppendLine("   localBounds center=" + b.center.ToString("F3") + "  size=" + b.size.ToString("F3"));
    sb.AppendLine("   rootBone  = " + (s.rootBone == null ? "NULL" : s.rootBone.name));
    sb.AppendLine("   updateWhenOffscreen = " + s.updateWhenOffscreen);
    sb.Append("   bones(" + s.bones.Length + ") = ");
    for (int i = 0; i < s.bones.Length; i++)
    {
        sb.Append(s.bones[i] == null ? "NULL" : s.bones[i].name);
        if (i < s.bones.Length - 1) sb.Append(", ");
    }
    sb.AppendLine();

    // bindpose 数 vs 骨数
    if (mesh != null)
        sb.AppendLine("   bindposes = " + mesh.bindposeCount);
    sb.AppendLine();
}

System.IO.File.WriteAllText("Tools/reports/q_motu2.txt", sb.ToString());
return "q_motu2 done: smr=" + smrs.Length;
