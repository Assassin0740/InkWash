// chk_enemy_vis：验证 6 个敌人 prefab 的可见性（SMR 是否有网格 + 材质），即 ④ 的收口判据。
using System.Text;
using UnityEditor;
using UnityEngine;

var sb = new StringBuilder();
string[] names =
{
    "Enemy_MoYan", "Enemy_MoTu", "Enemy_MoOu",
    "Z_Enemy_MoGu", "Z_Enemy_MoGuai", "Z_Enemy_MoShan",
    "Z_Enemy_MoLong"
};

int good = 0, bad = 0;
foreach (var n in names)
{
    var p = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Enemies/" + n + ".prefab");
    if (p == null) { sb.AppendLine("[VIS] " + n + "  —— prefab 不存在"); bad++; continue; }

    var smrs = p.GetComponentsInChildren<SkinnedMeshRenderer>(true);
    int withMesh = 0; var meshes = new StringBuilder();
    foreach (var s in smrs)
    {
        if (s.sharedMesh != null)
        {
            withMesh++;
            if (meshes.Length < 90)
                meshes.Append(s.sharedMesh.name).Append("(").Append(s.sharedMesh.vertexCount).Append("v) ");
        }
    }
    bool ok = withMesh > 0;
    if (ok) good++; else bad++;
    sb.AppendLine("[VIS] " + (ok ? "✅" : "❌") + " " + n
                  + "   SMR=" + smrs.Length + "  有网格=" + withMesh
                  + "   材质=" + (smrs.Length > 0 && smrs[0].sharedMaterial != null ? smrs[0].sharedMaterial.name : "null")
                  + "   " + meshes.ToString().Trim());
}

sb.AppendLine("[VIS] 汇总: 可见 " + good + " / 不可见 " + bad + " （共 " + names.Length + "）");
sb.AppendLine("[VIS] 判据: 每个敌人至少 1 个 SMR 的 sharedMesh != null ⇒ 能渲染");
Debug.Log(sb.ToString());
return sb.ToString();
