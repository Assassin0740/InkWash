// e_probe2.cs —— 敌人素材深挖（编辑态，只读）：能不能零代价用上 KayKit 动画库？
//   核心问题：Skeleton_*.fbx 的骨架节点名，与 Rig_Medium_*/Rig_Large_* 动画 FBX 的骨架节点名是否一致？
//   次要问题：为什么有些动画 FBX 的 isHumanMotion=False（导入设置不一致）？
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

IEnumerator Body()
{
    var sb = new StringBuilder();
    string projRoot = Path.GetDirectoryName(Application.dataPath);
    yield return null; yield return null;

    // AnimationClip.isHumanMotion 只说明"该片段按 humanoid 解析了"；根因在 importer。
    string[] animFiles = {
        "Rig_Medium_CombatMelee", "Rig_Medium_CombatRanged", "Rig_Medium_General",
        "Rig_Medium_MovementBasic", "Rig_Medium_MovementAdvanced", "Rig_Medium_Special",
        "Rig_Large_CombatMelee", "Rig_Large_General", "Rig_Large_MovementBasic",
    };
    sb.AppendLine("========== ① 动画 FBX 的导入设置 ==========");
    foreach (var n in animFiles)
    {
        string p = "Assets/ThirdParty/KayKit/Animations/" +
                   (n.StartsWith("Rig_Large") ? "Rig_Large/" : "Rig_Medium/") + n + ".fbx";
        var imp = AssetImporter.GetAtPath(p) as ModelImporter;
        if (imp == null) { sb.AppendLine("--- " + n + "  (无 ModelImporter)"); continue; }
        var av = AssetDatabase.LoadAllAssetsAtPath(p).OfType<Avatar>().FirstOrDefault();
        sb.AppendLine(string.Format("--- {0,-28} animType={1,-8} avatarSetup={2,-22} srcAvatar={3}",
            n, imp.animationType, imp.avatarSetup, imp.sourceAvatar != null ? imp.sourceAvatar.name : "(null)"));
        sb.AppendLine("     导入内 Avatar = " + (av != null ? av.name + " human=" + av.isHuman + " valid=" + av.isValid : "(无)"));
        sb.AppendLine("     optimizeGameObjects=" + imp.optimizeGameObjects + "  resample=" + imp.resampleCurves +
                      "  importAnimation=" + imp.importAnimation + "  fps=" + imp.animationCompression);
    }

    sb.AppendLine();
    sb.AppendLine("========== ② 骨架节点名：动画 FBX vs 骷髅模型 ==========");
    var names = new Dictionary<string, List<string>>();
    string[] probe = {
        "Assets/ThirdParty/KayKit/Animations/Rig_Medium/Rig_Medium_General.fbx",
        "Assets/ThirdParty/KayKit/Animations/Rig_Large/Rig_Large_General.fbx",
        "Assets/ThirdParty/KayKit/Skeletons/Characters/Skeleton_Minion.fbx",
        "Assets/ThirdParty/KayKit/Skeletons/Characters/Skeleton_Mage.fbx",
        "Assets/ThirdParty/KayKit/Skeletons/Characters/Skeleton_Golem.fbx",
    };
    foreach (var p in probe)
    {
        var go = AssetDatabase.LoadAssetAtPath<GameObject>(p);
        if (go == null) { sb.AppendLine("--- " + p + " (加载失败)"); continue; }
        // 只取骨骼（有 Animator 的层级里，Transform 名为骨架节点）
        var list = go.GetComponentsInChildren<Transform>(true).Select(t => t.name).Distinct().OrderBy(x => x).ToList();
        names[Path.GetFileNameWithoutExtension(p)] = list;
        sb.AppendLine(string.Format("--- {0,-24} 唯一节点名 {1} 个", Path.GetFileNameWithoutExtension(p), list.Count));
        sb.AppendLine("     " + string.Join(", ", list.Take(40)) + (list.Count > 40 ? " ..." : ""));
    }
    sb.AppendLine();
    sb.AppendLine("--- 交集/差集（判断能否零重定向）---");
    foreach (var a in names.Keys.Where(k => k.StartsWith("Rig_")).ToList())
        foreach (var b in names.Keys.Where(k => k.StartsWith("Skeleton_")).ToList())
        {
            var inter = names[a].Intersect(names[b]).Count();
            var onlyA = names[a].Except(names[b]).ToList();
            var onlyB = names[b].Except(names[a]).ToList();
            sb.AppendLine(string.Format("  {0} ∩ {1} = {2}   仅在动画={3}   仅在模型={4}",
                a, b, inter, onlyA.Count, onlyB.Count));
            if (onlyA.Count > 0) sb.AppendLine("      仅在动画: " + string.Join(", ", onlyA.Take(20)));
            if (onlyB.Count > 0) sb.AppendLine("      仅在模型: " + string.Join(", ", onlyB.Take(20)));
        }

    sb.AppendLine();
    sb.AppendLine("========== ③ 骷髅材质与贴图 ==========");
    foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { "Assets/ThirdParty/KayKit" }))
    {
        string p = AssetDatabase.GUIDToAssetPath(guid);
        var m = AssetDatabase.LoadAssetAtPath<Material>(p);
        var tex = m != null ? m.GetTexture("_BaseMap") : null;
        sb.AppendLine(string.Format("  {0,-34} shader={1,-40} baseMap={2}",
            m.name, m.shader.name, tex != null ? tex.name : "(null)"));
    }

    sb.AppendLine();
    sb.AppendLine("========== ④ 骷髅模型的网格部件构成 ==========");
    foreach (var f in new[] { "Skeleton_Minion", "Skeleton_Mage", "Skeleton_Warrior", "Skeleton_Golem", "Necromancer" })
    {
        var go = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ThirdParty/KayKit/Skeletons/Characters/" + f + ".fbx");
        if (go == null) continue;
        var smrs = go.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        int v = 0, t = 0;
        foreach (var s in smrs) if (s.sharedMesh != null) { v += s.sharedMesh.vertexCount; t += s.sharedMesh.triangles.Length / 3; }
        sb.AppendLine(string.Format("  {0,-18} 部件数={1}  顶点合计={2}  三角合计={3}  根={4}",
            f, smrs.Length, v, t, go.transform.name));
        sb.AppendLine("     部件: " + string.Join(", ", smrs.Select(s => s.name)));
    }

    sb.AppendLine();
    sb.AppendLine("========== ⑤ 现有 Prefab / 战斗相关现状 ==========");
    var pcur = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Player/Player.prefab");
    if (pcur != null)
    {
        sb.AppendLine("  Player.prefab 顶层组件: " + string.Join(", ",
            pcur.GetComponents<Component>().Where(c => c != null).Select(c => c.GetType().Name)));
        var anim = pcur.GetComponent<Animator>();
        if (anim != null) sb.AppendLine("  Animator.avatar = " + (anim.avatar != null ? anim.avatar.name : "(null)") +
                                       "  controller = " + (anim.runtimeAnimatorController != null ? anim.runtimeAnimatorController.name : "(null)"));
    }
    sb.AppendLine("  含 'Hitbox' 的脚本: " + (System.IO.Directory.GetFiles(Application.dataPath, "*.cs", SearchOption.AllDirectories)
        .Count(f => File.ReadAllText(f).Contains("Hitbox"))));

    File.WriteAllText(Path.Combine(projRoot, "Tools/reports/e_probe2.txt"), sb.ToString());
    Debug.Log("[e_probe2] done");
    yield return null;
}

return Body();
