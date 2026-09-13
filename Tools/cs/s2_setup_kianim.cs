// 把 Kevin Iglesias（Human Soldier Animations FREE）的三个片段设为 Humanoid
// 并**从本模型生成 Avatar**，同时打开 loopTime。
//
// 为什么要显式改 avatarSetup：
//   从 .unitypackage 里提取出来的 .meta 是作者导出时的状态 —— avatarSetup=CopyFromOther
//   且 sourceAvatar=null（它原本指向包里另一个共享 Avatar，而我们没提取那个文件）。
//   这样 Unity 不会生成本地 Avatar，人形片段就只能靠默认肌肉区间解释，姿势可能走样。
//   改成 CreateFromThisModel 后 Unity 会用它自己的骨架生成 Avatar，重定向最稳。
//
// 注意：团结引擎的枚举是 Human，不是国际版的 Humanoid（沿用 setup_ubc_humanoid.cs 的写法）。
var targets = new string[] {
    "Assets/ThirdParty/KevinIglesias/Animations/Male/Movement/Run/HumanM@Run01_Forward.fbx",
    "Assets/ThirdParty/KevinIglesias/Animations/Male/Movement/Walk/HumanM@Walk01_Forward.fbx",
    "Assets/ThirdParty/KevinIglesias/Animations/Male/Idles/HumanM@Idle01.fbx",
};

var sb = new System.Text.StringBuilder();
int ok = 0, fail = 0;

foreach (var p in targets)
{
    string fn = System.IO.Path.GetFileName(p);
    var mi = UnityEditor.AssetImporter.GetAtPath(p) as UnityEditor.ModelImporter;
    if (mi == null) { sb.AppendLine("✗ " + fn + "  找不到 ModelImporter"); fail++; continue; }

    try
    {
        mi.animationType = UnityEditor.ModelImporterAnimationType.Human;
        mi.avatarSetup = UnityEditor.ModelImporterAvatarSetup.CreateFromThisModel;
        mi.importAnimation = true;

        // 循环片段打开 loopTime（跑步/走路/待机都要循环）
        var defs = mi.defaultClipAnimations;
        int looped = 0;
        foreach (var d in defs)
        {
            if (!d.loopTime) { d.loopTime = true; looped++; }
        }
        if (looped > 0) mi.clipAnimations = defs;

        mi.SaveAndReimport();

        string human = "无 Avatar";
        int bones = 0;
        float len = 0f;
        foreach (var a in UnityEditor.AssetDatabase.LoadAllAssetsAtPath(p))
        {
            var av = a as UnityEngine.Avatar;
            if (av != null)
            {
                human = av.isHuman ? "isHuman=TRUE" : "isHuman=FALSE(映射失败)";
                bones = av.humanDescription.skeleton != null ? av.humanDescription.skeleton.Length : 0;
            }
            var c = a as UnityEngine.AnimationClip;
            if (c != null && !c.name.StartsWith("__preview__")) len = c.length;
        }
        if (human.Contains("TRUE")) ok++; else fail++;
        sb.AppendLine("✓ " + fn + "  →  " + human + "  骨骼=" + bones + "  时长=" + len.ToString("F3") + "s");
    }
    catch (System.Exception e)
    {
        sb.AppendLine("✗ " + fn + "  异常: " + e.Message);
        fail++;
    }
}

UnityEditor.AssetDatabase.Refresh();
sb.AppendLine();
sb.AppendLine("完成：成功 " + ok + " / 失败 " + fail);
return sb.ToString();
