// q_clips2.cs —— 枚举 UAL1 / UAL2 全部片段名，找现成的「持剑移动」片段
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

var sb = new StringBuilder();

void Dump(string path, string label)
{
    var names = new List<string>();
    foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path))
    {
        var c = o as AnimationClip;
        if (c != null && !c.name.StartsWith("__preview__")) names.Add(c.name + " (" + c.length.ToString("F3") + "s)");
    }
    names.Sort();
    sb.AppendLine("=== " + label + "  共 " + names.Count + " 个 ===");
    for (int i = 0; i < names.Count; i += 3)
    {
        var line = new StringBuilder("  ");
        for (int k = 0; k < 3 && i + k < names.Count; k++) line.Append(names[i + k].PadRight(42));
        sb.AppendLine(line.ToString());
    }
    // 只挑含 Sword / Carry / Run / Jog / Sprint 的
    sb.AppendLine("  ── 含 Sword/Run/Jog/Sprint/Carry 的：");
    foreach (var n in names)
    {
        string l = n.ToLower();
        if (l.Contains("sword") || l.Contains("run") || l.Contains("jog") || l.Contains("sprint") || l.Contains("carry"))
            sb.AppendLine("      " + n);
    }
    sb.AppendLine();
}

Dump("Assets/ThirdParty/Quaternius/UniversalAnimationLibrary/Unity/AnimationLibrary_Unity_Standard.fbx", "UAL1");
Dump("Assets/ThirdParty/Quaternius/UniversalAnimationLibrary2/Unity/UAL2_Standard.fbx", "UAL2");

System.IO.File.WriteAllText(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Application.dataPath), "Tools/reports/q_clips2.txt"),
    sb.ToString(), new UTF8Encoding(false));
return sb.ToString();
