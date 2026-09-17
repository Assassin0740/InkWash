// e_import.cs —— 把敌人要用的 KayKit FBX 统一成 Humanoid（编辑态，会改导入设置并重导入）
//   为什么值得改：实测 KayKit 全系列骨架共用 24 个同名骨骼（hips/chest/upperarm.l/…），
//   Rig_Medium 与 Rig_Large 只差缩放 ⇒ humanoid 重定向质量极高，且能复用整套 130+ 片段。
//   踩坑提醒：团结引擎的枚举是 ModelImporterAnimationType.Human（**不是** Humanoid）。
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

IEnumerator Body()
{
    var sb = new StringBuilder();
    string projRoot = Path.GetDirectoryName(Application.dataPath);
    yield return null; yield return null;

    // ---- ① 骷髅模型：Human + 从本模型生成 Avatar ----
    string[] chars = { "Necromancer", "Skeleton_Golem", "Skeleton_Mage", "Skeleton_Minion", "Skeleton_Rogue", "Skeleton_Warrior" };
    sb.AppendLine("========== ① 骷髅模型 FBX ==========");
    foreach (var n in chars)
    {
        string p = "Assets/ThirdParty/KayKit/Skeletons/Characters/" + n + ".fbx";
        var imp = AssetImporter.GetAtPath(p) as ModelImporter;
        if (imp == null) { sb.AppendLine("  [ERR] 无 importer: " + p); continue; }
        string before = imp.animationType + "/" + imp.avatarSetup;
        if (imp.animationType != ModelImporterAnimationType.Human)
        {
            imp.animationType = ModelImporterAnimationType.Human;
            imp.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            imp.SaveAndReimport();
        }
        var imp2 = AssetImporter.GetAtPath(p) as ModelImporter;
        var av = AssetDatabase.LoadAllAssetsAtPath(p).OfType<Avatar>().FirstOrDefault();
        sb.AppendLine(string.Format("  {0,-18} {1}  →  {2}/{3}   Avatar={4} human={5} valid={6}",
            n, before, imp2.animationType, imp2.avatarSetup,
            av != null ? av.name : "(无)", av != null ? av.isHuman.ToString() : "-", av != null ? av.isValid.ToString() : "-"));
    }

    // ---- ② 动画 FBX：统一 Humanoid ----
    var anim = new (string, string)[]
    {
        ("Assets/ThirdParty/KayKit/Animations/Rig_Medium/Rig_Medium_CombatRanged.fbx", "墨偶远程施法"),
        ("Assets/ThirdParty/KayKit/Animations/Rig_Medium/Rig_Medium_Special.fbx",      "骷髅专属：起身/死亡/待机"),
        ("Assets/ThirdParty/KayKit/Animations/Rig_Large/Rig_Large_CombatMelee.fbx",    "墨魇近战"),
        ("Assets/ThirdParty/KayKit/Animations/Rig_Large/Rig_Large_General.fbx",        "墨魇待机/受击/死亡"),
        ("Assets/ThirdParty/KayKit/Animations/Rig_Large/Rig_Large_MovementBasic.fbx",  "墨魇移动"),
    };
    sb.AppendLine();
    sb.AppendLine("========== ② 动画 FBX ==========");
    foreach (var (p, why) in anim)
    {
        var imp = AssetImporter.GetAtPath(p) as ModelImporter;
        if (imp == null) { sb.AppendLine("  [ERR] 无 importer: " + p); continue; }
        string before = imp.animationType.ToString();
        if (imp.animationType != ModelImporterAnimationType.Human)
        {
            imp.animationType = ModelImporterAnimationType.Human;
            imp.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            imp.SaveAndReimport();
        }
        var av = AssetDatabase.LoadAllAssetsAtPath(p).OfType<Avatar>().FirstOrDefault();
        int humanClips = AssetDatabase.LoadAllAssetsAtPath(p).OfType<AnimationClip>()
            .Count(c => !c.name.StartsWith("__preview__") && c.isHumanMotion);
        sb.AppendLine(string.Format("  {0,-34} {1,-8} → {2,-6} ({3})  Avatar={4}  human片段={5}",
            Path.GetFileNameWithoutExtension(p), before,
            (AssetImporter.GetAtPath(p) as ModelImporter).animationType, why,
            av != null ? av.name : "(无)", humanClips));
    }

    AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);

    // ---- ③ 复核：敌人要用的片段是否都成了 humanoid（按 FBX 逐文件查，避免同名片段串味）----
    sb.AppendLine();
    sb.AppendLine("========== ③ 敌人动画清单复核（isHumanMotion 必须为 True）==========");
    var want = new (string, string[])[]
    {
        ("Assets/ThirdParty/KayKit/Animations/Rig_Medium/Rig_Medium_CombatMelee.fbx",
            new[]{ "Melee_1H_Attack_Slice_Diagonal", "Melee_1H_Attack_Slice_Horizontal", "Melee_1H_Attack_Chop", "Melee_1H_Attack_Stab" }),
        ("Assets/ThirdParty/KayKit/Animations/Rig_Medium/Rig_Medium_CombatRanged.fbx",
            new[]{ "Ranged_Magic_Spellcasting", "Ranged_Magic_Shoot", "Ranged_Magic_Raise", "Ranged_Magic_Spellcasting_Long" }),
        ("Assets/ThirdParty/KayKit/Animations/Rig_Medium/Rig_Medium_General.fbx",
            new[]{ "Idle_A", "Idle_B", "Hit_A", "Hit_B", "Death_A", "Death_B", "Throw" }),
        ("Assets/ThirdParty/KayKit/Animations/Rig_Medium/Rig_Medium_MovementBasic.fbx",
            new[]{ "Walking_A", "Walking_B", "Running_A", "Running_B" }),
        ("Assets/ThirdParty/KayKit/Animations/Rig_Medium/Rig_Medium_Special.fbx",
            new[]{ "Skeletons_Idle", "Skeletons_Death", "Skeletons_Spawn_Ground", "Skeletons_Awaken_Standing", "Skeletons_Taunt" }),
        ("Assets/ThirdParty/KayKit/Animations/Rig_Large/Rig_Large_CombatMelee.fbx",
            new[]{ "Melee_1H_Slash", "Melee_1H_Stab", "Melee_2H_Attack", "Melee_2H_Slam" }),
        ("Assets/ThirdParty/KayKit/Animations/Rig_Large/Rig_Large_General.fbx",
            new[]{ "Idle_A", "Idle_B", "Hit_A", "Death_A" }),
        ("Assets/ThirdParty/KayKit/Animations/Rig_Large/Rig_Large_MovementBasic.fbx",
            new[]{ "Walking_A", "Running_A" }),
    };
    foreach (var (p, clips) in want)
    {
        sb.AppendLine("--- " + Path.GetFileNameWithoutExtension(p));
        var dict = AssetDatabase.LoadAllAssetsAtPath(p).OfType<AnimationClip>()
            .Where(c => !c.name.StartsWith("__preview__")).ToDictionary(c => c.name, c => c);
        foreach (var cn in clips)
        {
            AnimationClip c;
            if (!dict.TryGetValue(cn, out c)) { sb.AppendLine("    [缺失] " + cn); continue; }
            sb.AppendLine(string.Format("    {0,-32} len={1,6:F3}  human={2,-5}  loop={3}",
                cn, c.length, c.isHumanMotion, c.isLooping));
        }
    }

    File.WriteAllText(Path.Combine(projRoot, "Tools/reports/e_import.txt"), sb.ToString());
    Debug.Log("[e_import] done");
    yield return null;
}

return Body();
