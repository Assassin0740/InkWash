// chk_enemy_vis：验证 6 个敌人 prefab 的可见性（收口判据 ④）
//
// 判据（三条，全部必须通过）：
//   ① 至少 1 个 SMR 且 sharedMesh != null          —— 有东西可画
//   ② SMR 的 bones 数组【逐元素】非 null 数 > 0      —— 蒙皮有效，三角形才会被光栅化
//   ③ 材质槽无空槽                                  —— 材质没丢
// ② 是 2026-09-17 补的硬判据：MoShan/MoGuai 曾经 14/14、10/10 网格齐全、材质齐全，
//   但 m_Bones 逐元素全为 null ⇒ 蒙皮退化 ⇒ 实渲 0 像素，而旧判据（只看 sharedMesh）判它们 ✅。
//   注意只数 bones.Length 会漏 —— 长度当时是正常的（26 / 97 / 80）。
// 详见 Docs/敌人0像素-诊断记录.md。
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
    var mrs = p.GetComponentsInChildren<MeshRenderer>(true);

    int withMesh = 0, boneLenAll = 0, boneNonNull = 0, emptyMat = 0, boneStarved = 0;
    var meshes = new StringBuilder();
    foreach (var s in smrs)
    {
        if (s.sharedMesh != null)
        {
            withMesh++;
            if (meshes.Length < 80)
                meshes.Append(s.sharedMesh.name).Append("(").Append(s.sharedMesh.vertexCount).Append("v) ");
        }
        if (s.sharedMaterial == null) emptyMat++;

        int len = s.bones == null ? 0 : s.bones.Length;
        int nn = 0;
        if (s.bones != null) foreach (var b in s.bones) if (b != null) nn++;
        boneLenAll += len;
        boneNonNull += nn;
        if (len > 0 && nn == 0) boneStarved++;   // ★ 致命：数组在、元素全 null
    }

    bool ok = withMesh > 0 && boneStarved == 0 && boneNonNull > 0 && emptyMat == 0;
    if (ok) good++; else bad++;
    sb.AppendLine("[VIS] " + (ok ? "✅" : "❌") + " " + n
                  + "   SMR=" + smrs.Length + " (+MR=" + mrs.Length + ")"
                  + "   有网格=" + withMesh
                  + "   骨骼=" + boneNonNull + "/" + boneLenAll
                  + (boneStarved > 0 ? "  **骨骼全 null 的 SMR=" + boneStarved + " ⇒ 必为 0 像素**" : "")
                  + "   空材质槽=" + emptyMat
                  + "   材质=" + (smrs.Length > 0 && smrs[0].sharedMaterial != null ? smrs[0].sharedMaterial.name : "null")
                  + "   " + meshes.ToString().Trim());
}

sb.AppendLine("[VIS] 汇总: 通过 " + good + " / 失败 " + bad + " （共 " + names.Length + "）");
sb.AppendLine("[VIS] 判据: ① sharedMesh != null  ② SMR 的 bones 逐元素非 null 数 > 0  ③ 材质槽无空槽");
sb.AppendLine("[VIS] 注: 本脚本是静态判据（编辑模式可跑）。真正「看得见」的判据是实渲像素，");
sb.AppendLine("[VIS]     做法见 Tools/cs/dg_sc_verify.cs（测试场景 + 演示场相机 + 同 tick 差分帧）。");
Debug.Log(sb.ToString());
return sb.ToString();
