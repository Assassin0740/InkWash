// 配置 UAL1 的导入设置（Humanoid + 从本模型创建 Avatar），并列出全部片段。
//
// 为什么必须显式设 Humanoid：Unity 导入 FBX 默认是 Generic。UAL1 与 UAL2 是两套
// 不同的 FBX，只有两边都是 Humanoid，Unity 才会做 Humanoid 重定向，才能把 UAL1 的
// Walk/Run 播到本工程的角色上。
var sb = new System.Text.StringBuilder();
const string Fbx = "Assets/ThirdParty/Quaternius/UniversalAnimationLibrary/Unity/AnimationLibrary_Unity_Standard.fbx";

if (UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(Fbx) == null)
    return "[ERR] 找不到 " + Fbx + "（可能还在导入中，稍后重试）";

var imp = UnityEditor.AssetImporter.GetAtPath(Fbx) as UnityEditor.ModelImporter;
if (imp == null) return "[ERR] 取不到 ModelImporter";

sb.AppendLine("=== 修改前 ===");
sb.AppendLine("  animationType=" + imp.animationType + "  avatarSetup=" + imp.avatarSetup);

bool changed = false;
if (imp.animationType != UnityEditor.ModelImporterAnimationType.Human)
{ imp.animationType = UnityEditor.ModelImporterAnimationType.Human; changed = true; }
if (imp.avatarSetup != UnityEditor.ModelImporterAvatarSetup.CreateFromThisModel)
{ imp.avatarSetup = UnityEditor.ModelImporterAvatarSetup.CreateFromThisModel; changed = true; }

// 本工程用代码驱动动画，不需要循环烘焙以外的额外设置
if (imp.importAnimation != true) { imp.importAnimation = true; changed = true; }

if (changed)
{
    imp.SaveAndReimport();
    sb.AppendLine("已写入并重新导入");
}
else sb.AppendLine("无需修改");

imp = UnityEditor.AssetImporter.GetAtPath(Fbx) as UnityEditor.ModelImporter;
sb.AppendLine("=== 修改后 ===");
sb.AppendLine("  animationType=" + imp.animationType + "  avatarSetup=" + imp.avatarSetup);

var av = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Avatar>(Fbx);
if (av == null)
{
    var all = UnityEditor.AssetDatabase.LoadAllAssetsAtPath(Fbx);
    foreach (var o in all) if (o is UnityEngine.Avatar) { av = (UnityEngine.Avatar)o; break; }
}
if (av != null) sb.AppendLine("  Avatar=" + av.name + "  isHuman=" + av.isHuman + "  isValid=" + av.isValid);
else sb.AppendLine("  [!] 取不到 Avatar");

// ---- 片段清单 ----
var list = new System.Collections.Generic.List<string>();
foreach (var o in UnityEditor.AssetDatabase.LoadAllAssetsAtPath(Fbx))
{
    var c = o as UnityEngine.AnimationClip;
    if (c == null || c.name.StartsWith("__preview__")) continue;
    string n = c.name;
    if (n.StartsWith("Armature|")) n = n.Substring("Armature|".Length);
    list.Add(string.Format("{0,-40} {1,6:F2}s  循环={2}", n, c.length, c.isLooping));
}
list.Sort(System.StringComparer.OrdinalIgnoreCase);
sb.AppendLine();
sb.AppendLine("=== UAL1 片段（共 " + list.Count + "）===");
foreach (var s in list) sb.AppendLine("  " + s);

// ---- 关键字命中：走 / 跑 / 冲刺 / 翻滚 / 待机 / 剑 ----
sb.AppendLine();
sb.AppendLine("=== 关键片段是否齐备 ===");
string[] keys = { "Walk", "Run", "Jog", "Sprint", "Roll", "Dodge", "Idle", "Sword", "Attack", "Hit", "Death" };
var found = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<string>>();
foreach (var o in UnityEditor.AssetDatabase.LoadAllAssetsAtPath(Fbx))
{
    var c = o as UnityEngine.AnimationClip;
    if (c == null || c.name.StartsWith("__preview__")) continue;
    foreach (var k in keys)
    {
        if (c.name.IndexOf(k, System.StringComparison.OrdinalIgnoreCase) >= 0)
        {
            if (!found.ContainsKey(k)) found[k] = new System.Collections.Generic.List<string>();
            string sn = c.name;
            if (sn.StartsWith("Armature|")) sn = sn.Substring("Armature|".Length);
            found[k].Add(sn);
            break;
        }
    }
}
foreach (var k in keys)
    sb.AppendLine("  [" + k + "] " + (found.ContainsKey(k) ? string.Join(", ", found[k].ToArray()) : "(无)"));

return sb.ToString();
