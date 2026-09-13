// 把 Player.prefab 里的角色模型从 Quaternius Superhero 换成 KayKit Rogue_Hooded，
// 并把根 Animator 的 Avatar 一起换掉。
//
// 原结构：Player(根，挂 Animator/CharacterController/脚本) + 子节点 Visual（Quaternius FBX 的预制体实例）
// 新结构：同上，只把 Visual 换成 KayKit 的 Rogue_Hooded FBX 实例
//
// 用 PrefabUtility 走「加载-改-存」流程，保住预制体本身与场景实例的引用。
using UnityEditor;
using UnityEngine;

var sb = new System.Text.StringBuilder();

const string PrefabPath = "Assets/_Project/Prefabs/Player/Player.prefab";
const string CharFbx = "Assets/ThirdParty/KayKit/Adventurers/Characters/Rogue_Hooded.fbx";
const int PlayerLayer = 8;

var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(CharFbx);
if (fbx == null) return "[ERR] 加载不到 " + CharFbx;

Avatar avatar = null;
foreach (var o in AssetDatabase.LoadAllAssetsAtPath(CharFbx))
{
    var av = o as Avatar;
    if (av != null) avatar = av;
}
if (avatar == null) return "[ERR] " + CharFbx + " 里没有 Avatar（rig 是否已设成 Humanoid？）";
sb.AppendLine("新 Avatar = " + avatar.name + "  isHuman=" + avatar.isHuman + " isValid=" + avatar.isValid);

var root = PrefabUtility.LoadPrefabContents(PrefabPath);
if (root == null) return "[ERR] 打不开 " + PrefabPath;

// --- 记录旧 Visual ---
Transform oldVisual = root.transform.Find("Visual");
string oldInfo = oldVisual == null ? "(无)" : (PrefabUtility.GetCorrespondingObjectFromSource(oldVisual.gameObject) != null
    ? AssetDatabase.GetAssetPath(PrefabUtility.GetCorrespondingObjectFromSource(oldVisual.gameObject)) : oldVisual.name);
sb.AppendLine("旧 Visual = " + oldInfo);

// --- 换模型 ---
if (oldVisual != null) Object.DestroyImmediate(oldVisual.gameObject);

var inst = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
inst.name = "Visual";
inst.transform.SetParent(root.transform, false);

// 模型自带的 Animator 要去掉（动画由根节点那台 Animator 统一驱动，与旧结构一致）
var modelAnim = inst.GetComponent<Animator>();
if (modelAnim != null) { Object.DestroyImmediate(modelAnim, true); sb.AppendLine("已移除模型自带 Animator"); }
foreach (var a in inst.GetComponentsInChildren<Animator>(true)) { if (a != null) Object.DestroyImmediate(a, true); }

// 层设为 Player
System.Action<Transform> setLayer = null;
setLayer = t => { t.gameObject.layer = PlayerLayer; foreach (Transform c in t) setLayer(c); };
setLayer(inst.transform);

// --- 落地对齐：把脚底压到 y=0 ---
float minY = float.MaxValue, maxY = float.MinValue;
foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
{
    if (r is TrailRenderer) continue;
    minY = Mathf.Min(minY, r.bounds.min.y);
    maxY = Mathf.Max(maxY, r.bounds.max.y);
}
sb.AppendLine(string.Format("模型包围盒（世界）：y {0:F3} .. {1:F3}  高 {2:F3}", minY, maxY, maxY - minY));
inst.transform.localPosition = new Vector3(0f, -minY, 0f);
sb.AppendLine(string.Format("Visual localPosition 设为 (0, {0:F4}, 0)", -minY));

// --- 换 Avatar ---
var rootAnimator = root.GetComponent<Animator>();
if (rootAnimator == null) return "[ERR] 根节点没有 Animator";
string oldAvatar = rootAnimator.avatar == null ? "(无)" : rootAnimator.avatar.name;
rootAnimator.avatar = avatar;
sb.AppendLine("根 Animator Avatar：" + oldAvatar + " -> " + avatar.name);

PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
PrefabUtility.UnloadPrefabContents(root);

AssetDatabase.SaveAssets();
AssetDatabase.Refresh();
sb.AppendLine("已保存 " + PrefabPath);

// --- 复核：重新打开看一遍 ---
var check = PrefabUtility.LoadPrefabContents(PrefabPath);
var v = check.transform.Find("Visual");
sb.AppendLine("复核：Visual = " + (v == null ? "(缺失!)" : v.name + " layer=" + v.gameObject.layer
    + " localPos=" + v.localPosition.ToString("F3")
    + " 来源=" + AssetDatabase.GetAssetPath(PrefabUtility.GetCorrespondingObjectFromSource(v.gameObject))));
var ca = check.GetComponent<Animator>();
sb.AppendLine("复核：根 Animator avatar = " + (ca.avatar == null ? "(无)" : ca.avatar.name)
    + "  controller = " + (ca.runtimeAnimatorController == null ? "(无)" : ca.runtimeAnimatorController.name));
sb.AppendLine("复核：SkinnedMeshRenderer 数 = " + check.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length);
PrefabUtility.UnloadPrefabContents(check);

return sb.ToString();
