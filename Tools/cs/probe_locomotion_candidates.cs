// 探针（修正版）：FBX 里的子片段不是独立 AnimationClip 资产，必须用 LoadAllAssetsAtPath 拿。
// 目标：判定工程内到底有没有能用的「走路 / 跑步」循环动画。
var sb = new System.Text.StringBuilder();

string[] roots = new string[]
{
    "Assets/ThirdParty/Quaternius/UniversalAnimationLibrary2",
    "Assets/ThirdParty/Quaternius/UniversalBaseCharacters",
    "Assets/ThirdParty/Quaternius/UltimateMonsters",
    "Assets/ThirdParty/Quaternius/Bestiary-DungeonMonsters",
    "Assets/ThirdParty/Quaternius/ModularCharacterOutfits-Fantasy",
};

int fileCount = 0, clipCount = 0;
var locoHits = new System.Collections.Generic.List<string>();
var nameBag = new System.Collections.Generic.SortedSet<string>();

foreach (string root in roots)
{
    if (!System.IO.Directory.Exists(root)) { sb.AppendLine("[缺] " + root); continue; }

    // 枚举该目录下所有可能承载动画的文件
    var files = new System.Collections.Generic.List<string>();
    foreach (string pat in new string[] { "*.fbx", "*.FBX", "*.anim", "*.glb", "*.gltf", "*.dae", "*.blend" })
        files.AddRange(System.IO.Directory.GetFiles(root, pat, System.IO.SearchOption.AllDirectories));

    sb.AppendLine();
    sb.AppendLine("### " + root + "   文件 " + files.Count + " 个");

    foreach (string f in files)
    {
        string p = f.Replace('\\', '/');
        if (p.StartsWith(System.IO.Directory.GetCurrentDirectory().Replace('\\', '/')))
            p = p.Substring(System.IO.Directory.GetCurrentDirectory().Length + 1);
        fileCount++;

        UnityEngine.Object[] all;
        try { all = UnityEditor.AssetDatabase.LoadAllAssetsAtPath(p); }
        catch { continue; }

        var clipNames = new System.Collections.Generic.List<string>();
        foreach (var o in all)
        {
            var clip = o as UnityEngine.AnimationClip;
            if (clip == null) continue;
            if (clip.name.StartsWith("__preview__")) continue;   // 预览副本，跳过
            clipCount++;
            clipNames.Add(clip.name);
            nameBag.Add(clip.name);

            string low = clip.name.ToLowerInvariant();
            if (low.Contains("walk") || low.Contains("run") || low.Contains("jog")
                || low.Contains("locomot") || low.Contains("stride") || low.Contains("step"))
            {
                var bindings = UnityEditor.AnimationUtility.GetCurveBindings(clip);
                locoHits.Add(string.Format(
                    "  {0,-40} {1,5:F2}s loop={2,-1} human={3,-1} 绑定数={4,-4}  {5}",
                    clip.name, clip.length, clip.isLooping ? "Y" : "n",
                    clip.humanMotion ? "Y" : "n", bindings.Length, p));
            }
        }
        if (clipNames.Count > 0)
            sb.AppendLine("   " + System.IO.Path.GetFileName(p) + "  ->  " + clipNames.Count + " 片段: "
                + string.Join(", ", clipNames.ToArray()));
    }
}

sb.AppendLine();
sb.AppendLine("=== 汇总 ===");
sb.AppendLine("动画文件 " + fileCount + " 个，真实片段 " + clipCount + " 条（已排除 __preview__）");

sb.AppendLine();
sb.AppendLine("=== 名字含 walk / run / jog / locomot / stride / step 的片段 ===");
locoHits.Sort();
if (locoHits.Count == 0) sb.AppendLine("  (无 —— 工程内确实没有可用的走路/跑步循环)");
foreach (var h in locoHits) sb.AppendLine(h);

sb.AppendLine();
sb.AppendLine("=== 全部片段名（去重排序，看有无遗漏）===");
int i = 0;
foreach (var n in nameBag) { sb.AppendLine("  " + n); i++; }
sb.AppendLine("  共 " + i + " 个唯一片段名");

return sb.ToString();
