// 探针：要用的片段帧范围 + 帧率（帧率取 AnimationClip.frameRate，ModelImporter 没有这属性）
using UnityEditor;
using UnityEngine;

var sb = new System.Text.StringBuilder();
string melee = "Assets/ThirdParty/KayKit/Animations/Rig_Medium/Rig_Medium_CombatMelee.fbx";
string move = "Assets/ThirdParty/KayKit/Animations/Rig_Medium/Rig_Medium_MovementBasic.fbx";
string gen = "Assets/ThirdParty/KayKit/Animations/Rig_Medium/Rig_Medium_General.fbx";
string adv = "Assets/ThirdParty/KayKit/Animations/Rig_Medium/Rig_Medium_MovementAdvanced.fbx";

string[][] sets = {
    new[] { melee, "Melee_1H_Attack_Slice_Diagonal" },
    new[] { melee, "Melee_1H_Attack_Slice_Horizontal" },
    new[] { melee, "Melee_1H_Attack_Stab" },
    new[] { melee, "Melee_1H_Attack_Chop" },
    new[] { melee, "Melee_1H_Idle" },
    new[] { move,  "Walking_A" },
    new[] { move,  "Running_A" },
    new[] { adv,   "Dodge_Forward" },
    new[] { gen,   "Idle_A" },
};

foreach (var pair in sets)
{
    var imp = AssetImporter.GetAtPath(pair[0]) as ModelImporter;
    if (imp == null) { sb.AppendLine("[X] " + pair[0]); continue; }
    foreach (var d in imp.defaultClipAnimations)
    {
        if (d.name != pair[1]) continue;
        var objs = AssetDatabase.LoadAllAssetsAtPath(pair[0]);
        float fps = 30f;
        foreach (var o in objs) { var c = o as AnimationClip; if (c != null && c.name == d.name) fps = c.frameRate; }
        sb.AppendLine(string.Format("{0,-34} frames {1,4}..{2,4}  fps={3}  帧数={4}  length={5:F2}s  loop={6}",
            d.name, d.firstFrame, d.lastFrame, fps, d.lastFrame - d.firstFrame,
            (d.lastFrame - d.firstFrame) / fps, d.loopTime));
    }
}
return sb.ToString();
