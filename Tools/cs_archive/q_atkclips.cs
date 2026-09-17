using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

// 清点工程内所有「攻击 / 挥砍」类 Humanoid 片段，供替换三段攻击用
var sb = new StringBuilder();

string[] keys = { "attack", "slash", "swing", "chop", "thrust", "stab", "slice",
                  "sword", "melee", "punch", "kick", "hit", "combo", "block", "parry" };

var guids = AssetDatabase.FindAssets("t:AnimationClip");
var hits = new SortedDictionary<string, List<string>>();
int total = 0, human = 0;

foreach (var g in guids)
{
    string path = AssetDatabase.GUIDToAssetPath(g);
    if (!path.StartsWith("Assets/")) continue;

    var assets = AssetDatabase.LoadAllAssetsAtPath(path);
    foreach (var obj in assets)
    {
        if (!(obj is AnimationClip c)) continue;
        if (c.name.StartsWith("__preview__")) continue;
        if (c.name.StartsWith("mine")) continue;
        total++;
        if (!c.isHumanMotion) continue;
        human++;

        string low = c.name.ToLowerInvariant();
        bool match = false;
        foreach (var k in keys) if (low.Contains(k)) { match = true; break; }
        if (!match) continue;

        string src;
        string a = path.StartsWith("Assets/ThirdParty/") ? path.Substring("Assets/ThirdParty/".Length) : null;
        if (a != null)
        {
            int s2 = a.IndexOf("/");
            src = a.Substring(0, s2 > 0 ? s2 : a.Length);
        }
        else if (path.StartsWith("Assets/_Project/")) src = "_Project";
        else src = path.Substring("Assets/".Length).Split('/')[0];

        if (!hits.TryGetValue(src, out var list)) { list = new List<string>(); hits[src] = list; }
        list.Add(c.name + "  [" + c.length.ToString("F2") + "s]  ← " + System.IO.Path.GetFileName(path));
    }
}

sb.AppendLine("工程内 AnimationClip 总数 = " + total + "，其中 Humanoid = " + human);
sb.AppendLine("名字含攻击关键词的 Humanoid 片段：");
sb.AppendLine();
int n2 = 0;
foreach (var kv in hits)
{
    kv.Value.Sort();
    n2 += kv.Value.Count;
    sb.AppendLine("════ " + kv.Key + "  (" + kv.Value.Count + " 个) ════");
    foreach (var n in kv.Value) sb.AppendLine("   " + n);
    sb.AppendLine();
}
sb.AppendLine("合计 " + n2 + " 个候选");

Debug.Log("[q_atkclips] 完成");
return sb.ToString();
