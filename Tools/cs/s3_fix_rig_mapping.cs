// 把 KayKit 的角色 FBX 与全部动画 FBX 统一成同一份显式 Humanoid 映射。
//
// 为什么必须显式指定：
//   实测 Unity 自动映射对同一套 Rig_Medium 骨架给出了**不一致**的结果 ——
//   动画 FBX 把 Hips 映射到 `hips`，而角色 FBX 映射到 `root`（`hips` 的父级）。
//   Hips 轴心错一层，骨盆位移就会整体偏移；且两侧映射不一致时，
//   Humanoid 重定向不再是恒等变换，会出现浮空/陷地/姿势错位。
//   另外自动映射把 LeftHand 映射到 `wrist.l`，漏掉真正的 `hand.l`，
//   而武器挂点 `handslot.l` 就挂在 `hand.l` 下面，握着东西会歪。
using UnityEditor;
using UnityEngine;

var sb = new System.Text.StringBuilder();

// 目标映射：KayKit 的 Rig_Medium 骨架（无 neck / shoulder 骨）
var want = new System.Collections.Generic.Dictionary<string, string>
{
    { "Hips",        "hips"       },
    { "Spine",       "spine"      },
    { "Chest",       "chest"      },
    { "Head",        "head"       },
    { "LeftUpperArm","upperarm.l" }, { "RightUpperArm","upperarm.r" },
    { "LeftLowerArm","lowerarm.l" }, { "RightLowerArm","lowerarm.r" },
    { "LeftHand",    "wrist.l"    }, { "RightHand",    "wrist.r"    },
    { "LeftUpperLeg","upperleg.l" }, { "RightUpperLeg","upperleg.r" },
    { "LeftLowerLeg","lowerleg.l" }, { "RightLowerLeg","lowerleg.r" },
    { "LeftFoot",    "foot.l"     }, { "RightFoot",    "foot.r"     },
    { "LeftToes",    "toes.l"     }, { "RightToes",    "toes.r"     },
};

string[] files = {
    // 角色
    "Assets/ThirdParty/KayKit/Adventurers/Characters/Rogue_Hooded.fbx",
    // 动画
    "Assets/ThirdParty/KayKit/Animations/Rig_Medium/Rig_Medium_General.fbx",
    "Assets/ThirdParty/KayKit/Animations/Rig_Medium/Rig_Medium_MovementBasic.fbx",
    "Assets/ThirdParty/KayKit/Animations/Rig_Medium/Rig_Medium_MovementAdvanced.fbx",
    "Assets/ThirdParty/KayKit/Animations/Rig_Medium/Rig_Medium_CombatMelee.fbx",
};

foreach (var p in files)
{
    var imp = AssetImporter.GetAtPath(p) as ModelImporter;
    if (imp == null) { sb.AppendLine("[X] 无 importer: " + p); continue; }

    imp.animationType = ModelImporterAnimationType.Human;
    imp.importAnimation = true;
    imp.SaveAndReimport();

    // reimport 后引用失效，重新取
    imp = AssetImporter.GetAtPath(p) as ModelImporter;
    var hd = imp.humanDescription;

    // 用目标映射覆盖自动结果
    var list = new System.Collections.Generic.List<HumanBone>(hd.human);
    int patched = 0;
    for (int i = 0; i < list.Count; i++)
    {
        string bn;
        if (want.TryGetValue(list[i].humanName, out bn))
        {
            if (list[i].boneName != bn) { var hb = list[i]; hb.boneName = bn; list[i] = hb; patched++; }
        }
    }
    hd.human = list.ToArray();
    imp.humanDescription = hd;
    imp.SaveAndReimport();

    imp = AssetImporter.GetAtPath(p) as ModelImporter;
    var hd2 = imp.humanDescription;
    var objs = AssetDatabase.LoadAllAssetsAtPath(p);
    string avInfo = "(无 Avatar)";
    foreach (var o in objs)
    {
        var av = o as Avatar;
        if (av != null) avInfo = string.Format("isHuman={0} isValid={1}", av.isHuman, av.isValid);
    }
    // 复核 Hips 是否真的落到 hips
    string hipsBone = "(未映射)";
    foreach (var h in hd2.human) if (h.humanName == "Hips") hipsBone = h.boneName;
    sb.AppendLine(string.Format("{0,-28} 修正 {1} 项  Hips->{2}  {3}",
        System.IO.Path.GetFileNameWithoutExtension(p), patched, hipsBone, avInfo));
}
return sb.ToString();
