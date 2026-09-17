// 诊断：CombatMelee.fbx 现在到底导出了哪些剪辑名（改过 clipAnimations 之后可能变样）
using UnityEditor;
using UnityEngine;

var sb = new System.Text.StringBuilder();
string p = "Assets/ThirdParty/KayKit/Animations/Rig_Medium/Rig_Medium_CombatMelee.fbx";

var imp = AssetImporter.GetAtPath(p) as ModelImporter;
sb.AppendLine("clipAnimations(显式) 数量 = " + imp.clipAnimations.Length);
sb.AppendLine("defaultClipAnimations 数量 = " + imp.defaultClipAnimations.Length);
sb.AppendLine();
sb.AppendLine("=== 导入后的 AnimationClip（LoadAllAssetsAtPath）===");
var objs = AssetDatabase.LoadAllAssetsAtPath(p);
int n = 0;
foreach (var o in objs)
{
    var c = o as AnimationClip;
    if (c == null) continue;
    if (c.name.StartsWith("__preview__")) continue;
    sb.AppendLine(string.Format("   [{0}] {1}  {2:F2}s", c.name.Length, c.name, c.length));
    n++;
}
sb.AppendLine("合计 " + n);
sb.AppendLine();
sb.AppendLine("=== imp.clipAnimations 里的名字（带括号看是否有空格）===");
foreach (var d in imp.clipAnimations)
    sb.AppendLine(string.Format("   [{0}] '{1}'  frames {2}..{3}", d.name.Length, d.name, d.firstFrame, d.lastFrame));
return sb.ToString();
