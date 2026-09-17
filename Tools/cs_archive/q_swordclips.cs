using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

// 清点工程内所有 Animator 可用的 Humanoid 片段（按来源分组），供挑选 Idle 用
var sb = new StringBuilder();

var guids = AssetDatabase.FindAssets("t:AnimationClip");
var groups = new SortedDictionary<string, List<string>>();
int total = 0, human = 0;

foreach (var g in guids)
{
    string path = AssetDatabase.GUIDToAssetPath(g);
    // 只扫第三方动画包与自有动画目录，跳过 Library/Packages
    if (!path.StartsWith("Assets/")) continue;

    var clips = AssetDatabase.LoadAllAssetsAtPath(path);
    foreach (var obj in clips)
    {
        if (!(obj is AnimationClip c)) continue;
        if (c.name.StartsWith("__preview__")) continue;
        total++;
        if (!c.isHumanMotion) continue;
        human++;

        // 分来源
        string src;
        int t = path.IndexOf("/", "Assets/".Length);
        string a = path.StartsWith("Assets/ThirdParty/") ? path.Substring("Assets/ThirdParty/".Length) : null;
        if (a != null)
        {
            int s2 = a.IndexOf("/");
            src = "ThirdParty/" + (s2 > 0 ? a.Substring(0, s2) : a);
        }
        else if (path.StartsWith("Assets/_Project/")) src = "_Project";
        else src = path.Substring("Assets/".Length).Split('/')[0];

        if (!groups.TryGetValue(src, out var list)) { list = new List<string>(); groups[src] = list; }
        list.Add(c.name + "  [" + c.length.ToString("F2") + "s]  ← " + System.IO.Path.GetFileName(path));
    }
}

sb.AppendLine("工程内 AnimationClip 总数 = " + total + "，其中 Humanoid = " + human);
sb.AppendLine();
foreach (var kv in groups)
{
    kv.Value.Sort();
    sb.AppendLine("════ " + kv.Key + "  (" + kv.Value.Count + " 个 Humanoid) ════");
    foreach (var n in kv.Value) sb.AppendLine("   " + n);
    sb.AppendLine();
}

// 单独把名字里带 idle / stand / guard / relax 的挑出来
sb.AppendLine("════ 名字像「待机」的候选 ════");
foreach (var kv in groups)
    foreach (var n in kv.Value)
    {
        string low = n.ToLowerInvariant();
        if (low.Contains("sword") || low.Contains("guard") || low.Contains("combat")
            || low.Contains("hold") || low.Contains("weapon"))
            sb.AppendLine("   [" + kv.Key + "] " + n);
    }

Debug.Log("[q_swordclips] 完成");
return sb.ToString();
