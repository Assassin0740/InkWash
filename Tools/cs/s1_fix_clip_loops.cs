// S1-4：修正 UAL2 动画剪辑的循环设置
// 实测发现：Quaternius 的 FBX 未勾选 loopTime，尽管剪辑名带 "_Loop"。
// 不修的话待机/行走动画播一遍就停住 —— 这是"能跑通"与"跑得对"的分界。
var sb = new System.Text.StringBuilder();

const string Fbx = "Assets/ThirdParty/Quaternius/UniversalAnimationLibrary2/Unity/UAL2_Standard.fbx";

var imp = UnityEditor.AssetImporter.GetAtPath(Fbx) as UnityEditor.ModelImporter;
if (imp == null) { sb.AppendLine("!! 取不到 ModelImporter: " + Fbx); return sb.ToString(); }

// defaultClipAnimations 由文件解析而来；改完要赋给 clipAnimations 才会生效
var clips = imp.defaultClipAnimations;
sb.AppendLine("FBX 内剪辑数（导入器视角）: " + clips.Length);
sb.AppendLine();

int changed = 0;
sb.AppendLine("=== 逐条判定 ===");
foreach (var c in clips)
{
    bool shouldLoop = c.name.Contains("_Loop");
    string mark = "";
    if (c.loopTime != shouldLoop)
    {
        c.loopTime = shouldLoop;
        changed++;
        mark = "   <== 修正";
    }
    sb.AppendLine("   " + (shouldLoop ? "[循环]" : "[单次]") + " " + c.name + "  (loopTime=" + c.loopTime + ")" + mark);
}

if (changed == 0)
{
    sb.AppendLine();
    sb.AppendLine("无需修改（或已是正确状态）。");
}
else
{
    imp.clipAnimations = clips;
    UnityEditor.EditorUtility.SetDirty(imp);
    imp.SaveAndReimport();
    sb.AppendLine();
    sb.AppendLine("已修正 " + changed + " 条并触发重导入。");
}

UnityEditor.AssetDatabase.Refresh();
return sb.ToString();
