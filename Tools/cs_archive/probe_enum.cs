// 反射探针：查 ModelImporterAnimationType / ModelImporterAvatarSetup 的真实枚举值。
var sb = new System.Text.StringBuilder();

var t1 = typeof(UnityEditor.ModelImporterAnimationType);
sb.AppendLine("ModelImporterAnimationType = " + t1.FullName);
sb.AppendLine("  程序集: " + t1.Assembly.GetName().Name + " @ " + t1.Assembly.Location);
sb.AppendLine("  值: " + string.Join(", ", System.Enum.GetNames(t1)));
sb.AppendLine();

var t2 = typeof(UnityEditor.ModelImporterAvatarSetup);
sb.AppendLine("ModelImporterAvatarSetup = " + t2.FullName);
sb.AppendLine("  值: " + string.Join(", ", System.Enum.GetNames(t2)));
sb.AppendLine();

// 也确认这几个属性是否存在于当前引擎版本
var mi = typeof(UnityEditor.ModelImporter);
foreach (var name in new string[] { "animationType", "avatarSetup", "importAnimation", "sourceAvatar", "SaveAndReimport", "clipAnimations" })
{
    var mem = mi.GetMember(name);
    sb.AppendLine("ModelImporter." + name + " => " + (mem.Length > 0 ? mem[0].MemberType.ToString() : "不存在"));
}

return sb.ToString();
