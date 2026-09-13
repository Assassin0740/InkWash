// 复核三方库是否真的可用（上一条探针把程序集名写错了）
var sb = new System.Text.StringBuilder();

sb.AppendLine("已加载的程序集里含 PrimeTween / QFramework / Burst 的：");
foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies())
{
    var n = a.GetName().Name;
    if (n.IndexOf("PrimeTween", System.StringComparison.OrdinalIgnoreCase) >= 0
        || n.IndexOf("Burst", System.StringComparison.OrdinalIgnoreCase) >= 0
        || n == "AudioKit" || n == "QFramework" || n == "QFramework.CoreKit")
    {
        sb.AppendLine("    " + n);
    }
}

sb.AppendLine();
sb.AppendLine("按正确程序集名查类型：");
var probes = new (string label, string typeName)[]
{
    ("PrimeTween.Tween",               "PrimeTween.Tween, PrimeTween.Runtime"),
    ("PrimeTween.TweenSettings`1",     "PrimeTween.TweenSettings`1, PrimeTween.Runtime"),
    ("PrimeTween.Sequence",            "PrimeTween.Sequence, PrimeTween.Runtime"),
    ("QFramework.AudioKit",            "QFramework.AudioKit, AudioKit"),
    ("QFramework.AudioKitSettingsModel","QFramework.AudioKitSettingsModel, AudioKit"),
};
foreach (var p in probes)
{
    sb.AppendLine(string.Format("    {0,-34} {1}", p.label,
        System.Type.GetType(p.typeName) != null ? "OK" : "缺失"));
}
return sb.ToString();
