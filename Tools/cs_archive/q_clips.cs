// q_clips.cs —— 全项目动画片段清单（按包分组，过滤 idle/sword/run/walk/guard/stance）
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

var sb = new StringBuilder();
var rx = new Regex("idle|sword|run|walk|guard|stance|sprint|jog|stand|breath|block", RegexOptions.IgnoreCase);

var byPack = new System.Collections.Generic.SortedDictionary<string, System.Collections.Generic.List<string>>();

string[] roots = { "Assets/ThirdParty", "Assets/Char_Feng", "Assets/_Project", "Assets/W_Sword" };
var seen = new System.Collections.Generic.HashSet<string>();

foreach (var root in roots)
{
    if (!AssetDatabase.IsValidFolder(root)) continue;
    foreach (var guid in AssetDatabase.FindAssets("t:AnimationClip", new[] { root }))
    {
        string p = AssetDatabase.GUIDToAssetPath(guid);
        if (!seen.Add(p)) continue;
        foreach (var o in AssetDatabase.LoadAllAssetsAtPath(p))
        {
            var c = o as AnimationClip;
            if (c == null || c.name.StartsWith("__preview__")) continue;
            if (!rx.IsMatch(c.name)) continue;

            string pack;
            var parts = p.Split('/');
            pack = parts.Length >= 3 && parts[1] == "ThirdParty" ? "ThirdParty/" + parts[2] : (parts.Length >= 2 ? parts[0] + "/" + parts[1] : p);
            if (!byPack.ContainsKey(pack)) byPack[pack] = new System.Collections.Generic.List<string>();
            byPack[pack].Add(string.Format("  {0,-42} len={1:F3} loop={2,-5} {3}", c.name, c.length, c.isLooping, Path.GetFileName(p)));
        }
    }
}

foreach (var kv in byPack)
{
    sb.AppendLine("########## " + kv.Key + "   (" + kv.Value.Count + " 条)");
    foreach (var line in kv.Value.OrderBy(x => x)) sb.AppendLine(line);
    sb.AppendLine();
}

File.WriteAllText("Tools/reports/q_clips.txt", sb.ToString(), new UTF8Encoding(false));
return sb.ToString();
