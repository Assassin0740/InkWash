// dg_fix_mesh.cs —— 修 Z_Dragon.prefab 悬空的网格引用（只改这一个引用，不动骨架）
//
// 证据链（Tools/reports/dg_prefab_forensic.txt / dg_mesh_hunt.txt / dg_play_verify.txt）：
//   · SMR.m_Mesh -> chinese_dragon.fbx 的 localId -3325053396650211315，该 id 全工程零命中
//   · Play 实测非背景像素 0.00% ⇒ 龙完全隐形
//   · 重建前的两个 mesh id（7161509005421682173=Object_281 body / 7238157132297919992=Object_282）
//     至今仍有效，且重建前就是 2 个 SMR 用它们
//
// 本步只做最小修复：把现有 SMR 指到 Object_281，材质槽对齐它的 1 个 submesh。
//   ★ 刻意不改骨架、不增删物体、不动 MoLong —— 骨架（24 节脊柱）是重建的收益，要留。
//   ★ 用「场景实例 + ApplyPrefabInstance」而非 LoadPrefabContents：
//     后者会重序列化整个 prefab，可能洗掉 MoLong 里那些 stripped 引用。
//
// 风险点（本脚本会把实测值打出来）：
//   fbx 的 mesh bindposes = 273，而重建后的骨架只有 180 骨。
//   若蒙皮错位，说明必须补骨或改用别的网格 —— 那要靠渲染图判断，不靠猜。
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

var sb = new StringBuilder();
const string PrefabPath = "Assets/_Project/Prefabs/Ziyuan/Z_Dragon.prefab";
const string FbxPath = "Assets/ThirdParty/Ziyuan/_fbx/chinese_dragon.fbx";
const string InkMatPath = "Assets/_Project/Art/Materials/M_Ink_Boss_Dragon.mat";
const long BodyId = 7161509005421682173L;   // Mesh Object_281, 28082v
const long ExtraId = 7238157132297919992L;  // Mesh Object_282, 804v

sb.AppendLine("===== dg_fix_mesh =====");
sb.AppendLine("isPlaying=" + EditorApplication.isPlaying + "（必须是 False）");
var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
sb.AppendLine("activeScene=" + scene.name + "  isDirty=" + scene.isDirty);

// ---------- 找 mesh ----------
Mesh body = null, extra = null;
foreach (var o in AssetDatabase.LoadAllAssetsAtPath(FbxPath))
{
    Mesh m = o as Mesh;
    if (m == null) continue;
    string g = ""; long lid = 0;
    AssetDatabase.TryGetGUIDAndLocalFileIdentifier(m, out g, out lid);
    if (lid == BodyId) body = m;
    else if (lid == ExtraId) extra = m;
}
sb.AppendLine();
sb.AppendLine("[1] fbx 网格定位");
sb.AppendLine("    body (Object_281) = " + (body == null ? "★没找到" : body.name + "  " + body.vertexCount + "v  subMesh=" + body.subMeshCount + "  bindposes=" + body.bindposes.Length));
sb.AppendLine("    extra(Object_282) = " + (extra == null ? "★没找到" : extra.name + "  " + extra.vertexCount + "v  subMesh=" + extra.subMeshCount + "  bindposes=" + extra.bindposes.Length));

Material ink = AssetDatabase.LoadAssetAtPath<Material>(InkMatPath);
sb.AppendLine("    ink 材质 = " + (ink == null ? "★没找到" : ink.name + "  shader=" + (ink.shader == null ? "null" : ink.shader.name)));

if (body == null || ink == null) { sb.AppendLine("★ 前置条件不满足，中止（未改动任何东西）"); return sb.ToString(); }

// ---------- 改前读数 ----------
var pf = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
if (pf == null) { sb.AppendLine("★ prefab 加载失败"); return sb.ToString(); }
var before = pf.GetComponentInChildren<SkinnedMeshRenderer>(true);
sb.AppendLine();
sb.AppendLine("[2] 改动前");
sb.AppendLine("    mesh=" + (before.sharedMesh == null ? "null" : before.sharedMesh.name)
              + "  mats=" + before.sharedMaterials.Length
              + "  bones=" + (before.bones == null ? 0 : before.bones.Length)
              + "  rootBone=" + (before.rootBone == null ? "null" : before.rootBone.name));

// ---------- 场景实例 + ApplyPrefabInstance（最小 diff）----------
var inst = PrefabUtility.InstantiatePrefab(pf) as GameObject;
if (inst == null) { sb.AppendLine("★ 实例化失败"); return sb.ToString(); }
inst.name = "dg_fix_tmp";
inst.transform.position = new Vector3(0f, 500f, 0f);   // 挪远，避免与场景物体互相干扰

var smr = inst.GetComponentInChildren<SkinnedMeshRenderer>(true);
smr.sharedMesh = body;
smr.sharedMaterials = new Material[] { ink };
sb.AppendLine();
sb.AppendLine("[3] 已在场景实例上改：mesh=Object_281, mats=[M_Ink_Boss_Dragon]");
sb.AppendLine("    蒙皮一致性: bones=" + (smr.bones == null ? 0 : smr.bones.Length)
              + "  mesh.bindposes=" + body.bindposes.Length
              + (smr.bones != null && smr.bones.Length != body.bindposes.Length
                 ? "   ⚠ 数量不等（180 vs 273）⇒ 若渲染错位，须另想办法" : "   ✓ 数量相等"));

PrefabUtility.ApplyPrefabInstance(inst, InteractionMode.AutomatedAction);
UnityEngine.Object.DestroyImmediate(inst);
sb.AppendLine("    已 ApplyPrefabInstance 写回 prefab，临时实例已销毁");

AssetDatabase.SaveAssets();
AssetDatabase.Refresh();

// ---------- 改后复核 ----------
var after = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
var a2 = after == null ? null : after.GetComponentInChildren<SkinnedMeshRenderer>(true);
sb.AppendLine();
sb.AppendLine("[4] 写回后复核（重新从磁盘加载）");
if (a2 == null) sb.AppendLine("    ★ 找不到 SMR —— 改坏了，请用 Tools/backup/ 的备份回滚");
else
{
    string mats = "";
    for (int i = 0; i < a2.sharedMaterials.Length; i++)
        mats += (i > 0 ? "," : "") + (a2.sharedMaterials[i] == null ? "null" : a2.sharedMaterials[i].name);
    sb.AppendLine("    mesh=" + (a2.sharedMesh == null ? "★null（没修好）" : a2.sharedMesh.name + "(" + a2.sharedMesh.vertexCount + "v)")
                  + "  mats=" + a2.sharedMaterials.Length + "[" + mats + "]"
                  + "  bones=" + (a2.bones == null ? 0 : a2.bones.Length)
                  + "  rootBone=" + (a2.rootBone == null ? "null" : a2.rootBone.name));
}

// ---------- MoLong 是否被牵连 ----------
var ml = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Enemies/Z_Enemy_MoLong.prefab");
var m2 = ml == null ? null : ml.GetComponentInChildren<SkinnedMeshRenderer>(true);
sb.AppendLine();
sb.AppendLine("[5] MoLong 牵连检查");
sb.AppendLine("    MoLong SMR = " + (ml == null ? "★prefab 没了" : ml.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length.ToString())
              + "  mesh=" + (m2 == null ? "null" : (m2.sharedMesh == null ? "★null" : m2.sharedMesh.name))
              + "  bones=" + (m2 == null || m2.bones == null ? 0 : m2.bones.Length));
sb.AppendLine("    场景 isDirty=" + UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty + "（不保存场景）");

string s = sb.ToString();
Debug.Log(s);
try
{
    string dir = Path.Combine(Directory.GetCurrentDirectory(), "Tools", "reports");
    Directory.CreateDirectory(dir);
    File.WriteAllText(Path.Combine(dir, "dg_fix_mesh.txt"), s, new UTF8Encoding(false));
}
catch { }
return s;
