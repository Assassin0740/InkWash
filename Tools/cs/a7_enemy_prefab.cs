// a7_enemy_prefab.cs —— 建三个新敌人的战斗 prefab（替换 KayKit 怪）
//
// 核心策略：**以现有敌人 prefab 为模板，只换 Visual 子树与 Animator 的 avatar/controller，
//   其余一切（EnemyBase 子类、所有行为参数、NavMeshAgent、CapsuleCollider、Hitbox、
//   InkMaterialSwap、Muzzle）原样保留。**
//
// 为什么这么做（而不是"从 Z_xx prefab 反着加组件"）：
//   1. 行为参数是**已验收过**的（S3 的 22 项断言、血量/速度/攻击时口的数值平衡）。
//      换骨架时若顺带把参数也重写一遍，等于把老验收全作废，而这是**换模型不该付出的代价**。
//   2. 组件的序列化字段极多（EnemyBase 有 40+ 个），手工重建必然漏。
//      本项目的硬规矩是"改 C# 默认值不生效，C# 与 prefab 序列化值都要改"——
//      反过来说，**保留 prefab 的序列化值**就是最省事且最不会出错的做法。
//
// 唯一要动的是"模型层"：
//   · Visual 子对象 → 换成 Ziyuan 模型实例（保留 Rig_* 根，保证骨架上移路径不变）
//   · Animator.avatar → 换成新骨架的 avatar
//   · Animator.runtimeAnimatorController → 换成 a6 建的新 controller
//   · InkMaterialSwap.inkMaterials → 换成新的水墨材质
//   · 容器 localScale / localPosition.y → 按新模型尺寸重定（换模型必做）
//
// 输出：Assets/_Project/Prefabs/Enemies/ 下
//   Z_Enemy_MoShan.prefab（山怪，Elite 模板 → 重击型精英）
//   Z_Enemy_MoGuai.prefab（兽人，Melee 模板 → 近战杂兵）
//   Z_Enemy_MoGu.prefab  （不死兵，Ranged 模板 → 远程）
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

var sb = new StringBuilder();
string root = Path.GetDirectoryName(Application.dataPath);

const string EnemyDir = "Assets/_Project/Prefabs/Enemies";

// (新 prefab 名, 模板 prefab, Ziyuan 源 prefab, controller 名, 水墨材质名)
var jobs = new[]
{
    ("Z_Enemy_MoShan",  "Enemy_MoYan", "Z_Orge",   "Z_Orge.controller",   "M_Ink_Enemy_MoShan"),
    ("Z_Enemy_MoGuai",  "Enemy_MoTu",  "Z_Orc",    "Z_Orc.controller",    "M_Ink_Enemy_MoGuai"),
    ("Z_Enemy_MoGu",    "Enemy_MoOu",  "Z_Undead", "Z_Undead.controller", "M_Ink_Enemy_MoGu"),
};

sb.AppendLine("========== 以现有敌人为模板，换模型层 ==========");

foreach (var (newName, tmplName, srcName, ctrlName, matName) in jobs)
{
    sb.AppendLine();
    sb.AppendLine("--- " + newName + "（模板 " + tmplName + " + 模型 " + srcName + "）---");

    var tmpl = PrefabUtility.LoadPrefabContents(EnemyDir + "/" + tmplName + ".prefab");
    var src = PrefabUtility.LoadPrefabContents("Assets/_Project/Prefabs/Ziyuan/" + srcName + ".prefab");
    var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(AssetDatabase.GUIDToAssetPath(
        AssetDatabase.FindAssets("t:AnimatorController " + Path.GetFileNameWithoutExtension(ctrlName))[0]));
    var mat = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/Materials/" + matName + ".mat");

    if (tmpl == null || src == null) { sb.AppendLine("  ★ 读不到模板或源模型"); continue; }
    sb.AppendLine("  controller = " + (ctrl != null ? ctrl.name : "★ null"));
    sb.AppendLine("  材质 = " + (mat != null ? mat.name : "★ null"));

    // ---- 1) 找模板里的 Visual 与旧模型根 ----
    var visual = tmpl.transform.Find("Visual");
    if (visual == null) { sb.AppendLine("  ★ 模板里没有 Visual 子对象"); continue; }

    // 记下旧模型根的 localScale / localPosition（新模型要重定这两个）
    Transform oldModelRoot = null;
    for (int i = 0; i < visual.childCount; i++)
    {
        var c = visual.GetChild(i);
        // 旧模型根是 Rig_Medium / Rig_Large 这类
        if (c.name.StartsWith("Rig_")) { oldModelRoot = c; break; }
    }
    sb.AppendLine("  旧模型根 = " + (oldModelRoot != null ? oldModelRoot.name : "★ 未找到")
                  + (oldModelRoot != null ? "  scale=" + oldModelRoot.localScale + " pos=" + oldModelRoot.localPosition : ""));

    // ---- 2) 找源模型（Ziyuan prefab）里真正的模型子树 ----
    // Ziyuan prefab 的结构是：Z_Orge / Object_33(SkinnedMesh) ...
    // 我们要把「骨骼根 + 所有 SkinnedMesh」整体搬过来。
    // 源 prefab 的根其实就是一个容器，直接把它的全部子对象搬过去最稳。
    var srcChildren = new List<Transform>();
    for (int i = 0; i < src.transform.childCount; i++) srcChildren.Add(src.transform.GetChild(i));
    sb.AppendLine("  源模型子对象 " + srcChildren.Count + " 个: " + string.Join(", ", srcChildren.ConvertAll(t => t.name).ToArray()));

    // 源模型的整体尺寸（决定容器缩放）
    var srcAnim = src.GetComponentInChildren<Animator>(true);
    string srcAvatarName = srcAnim != null && srcAnim.avatar != null ? srcAnim.avatar.name : null;

    // ---- 3) 删掉旧模型根，把新模型搬进来 ----
    if (oldModelRoot != null) UnityEngine.Object.DestroyImmediate(oldModelRoot.gameObject);

    var holder = new GameObject("Model");
    holder.transform.SetParent(visual, false);

    foreach (var ch in srcChildren)
    {
        var copy = UnityEngine.Object.Instantiate(ch.gameObject);
        copy.name = ch.name;
        copy.transform.SetParent(holder.transform, false);
        copy.transform.localPosition = ch.localPosition;
        copy.transform.localRotation = ch.localRotation;
        copy.transform.localScale = ch.localScale;
    }

    // ---- 4) 配 Animator ----
    var anim = tmpl.GetComponentInChildren<Animator>(true);
    if (anim == null) { sb.AppendLine("  ★ 模板里没有 Animator"); }
    else
    {
        // avatar 用源 prefab 上的（同一套骨架，由 z2_humanoid 配好过 Humanoid）
        var srcAvatar = srcAnim != null ? srcAnim.avatar : null;
        if (srcAvatar != null) anim.avatar = srcAvatar;
        if (ctrl != null) anim.runtimeAnimatorController = ctrl;
        sb.AppendLine("  Animator.avatar = " + (srcAvatar != null ? srcAvatar.name : "★ null")
                      + " (isHuman=" + (srcAvatar != null && srcAvatar.isHuman) + ")");
        sb.AppendLine("  Animator.controller = " + (ctrl != null ? ctrl.name : "★ null"));

        // Animator 必须挂在能覆盖到新骨骼的节点上。
        // 模板里 Animator 就挂在根对象上（与 NavMeshAgent 同级）—— 保持不变即可，
        // 因为 Unity 会向下搜索骨骼。
        sb.AppendLine("  Animator 所在节点 = " + anim.gameObject.name
                      + "（是否根=" + (anim.gameObject == tmpl) + "）");
    }

    // ---- 5) 配 InkMaterialSwap ----
    var sw = tmpl.GetComponentInChildren<InkMaterialSwap>(true);
    if (sw != null && mat != null)
    {
        sw.inkMaterials = new[] { mat };
        EditorUtility.SetDirty(sw);
        sb.AppendLine("  InkMaterialSwap.inkMaterials = [" + mat.name + "]");
    }

    // ---- 6) 把模型上的渲染器材质先设成水墨材质（编辑态就正确，不依赖运行时 ForceInk）----
    if (mat != null)
    {
        int rc = 0;
        foreach (var r in tmpl.GetComponentsInChildren<Renderer>(true))
        {
            var mats = r.sharedMaterials;
            bool touched = false;
            for (int i = 0; i < mats.Length; i++) { mats[i] = mat; touched = true; }
            if (touched) { r.sharedMaterials = mats; rc++; }
        }
        sb.AppendLine("  渲染器材质已设水墨 ×" + rc);
    }

    // ---- 7) 改名 + 存盘 ----
    tmpl.name = newName;
    string outPath = EnemyDir + "/" + newName + ".prefab";
    PrefabUtility.SaveAsPrefabAsset(tmpl, outPath);
    sb.AppendLine("  已存 " + outPath);

    PrefabUtility.UnloadPrefabContents(tmpl);
    PrefabUtility.UnloadPrefabContents(src);
}

AssetDatabase.SaveAssets();
AssetDatabase.Refresh();

File.WriteAllText(Path.Combine(root, "Tools/reports/a7_enemy_prefab.txt"), sb.ToString());
Debug.Log("[a7]\n" + sb.ToString());
