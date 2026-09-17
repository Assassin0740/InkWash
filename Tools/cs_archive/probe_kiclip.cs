// 核对新导入的 Kevin Iglesias 片段：是不是 Humanoid、有没有 Avatar、时长/帧率/循环。
var sb = new System.Text.StringBuilder();

string[] paths = {
    "Assets/ThirdParty/KevinIglesias/Animations/Male/Movement/Run/HumanM@Run01_Forward.fbx",
    "Assets/ThirdParty/KevinIglesias/Animations/Male/Movement/Walk/HumanM@Walk01_Forward.fbx",
    "Assets/ThirdParty/KevinIglesias/Animations/Male/Idles/HumanM@Idle01.fbx",
};

foreach (var p in paths)
{
    sb.AppendLine("===== " + p + " =====");

    var imp = UnityEditor.AssetImporter.GetAtPath(p) as UnityEditor.ModelImporter;
    if (imp == null)
    {
        var any = UnityEditor.AssetImporter.GetAtPath(p);
        sb.AppendLine("  [!] importer=" + (any == null ? "null（没导入？）" : any.GetType().Name));
        sb.AppendLine();
        continue;
    }

    sb.AppendLine("  animationType = " + imp.animationType
        + "   avatarSetup = " + imp.avatarSetup
        + "   sourceAvatar = " + (imp.sourceAvatar == null ? "null" : imp.sourceAvatar.name));

    var objs = UnityEditor.AssetDatabase.LoadAllAssetsAtPath(p);
    int clips = 0, avatars = 0;
    foreach (var o in objs)
    {
        var c = o as UnityEngine.AnimationClip;
        if (c != null && !c.name.StartsWith("__preview__"))
        {
            clips++;
            sb.AppendLine(string.Format("  clip  name={0,-28} len={1:F3}s fps={2:F1} human={3} loop={4}",
                c.name, c.length, c.frameRate, c.isHumanMotion, c.isLooping));
            var b = UnityEditor.AnimationUtility.GetCurveBindings(c);
            sb.AppendLine("        曲线数=" + b.Length);
        }
        var a = o as UnityEngine.Avatar;
        if (a != null) { avatars++; sb.AppendLine("  avatar=" + a.name + "  isValid=" + a.isValid + "  human=" + a.isHuman); }
    }
    sb.AppendLine("  clip 数=" + clips + "  avatar 数=" + avatars);
    sb.AppendLine();
}

return sb.ToString();
