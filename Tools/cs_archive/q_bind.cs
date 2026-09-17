// q_bind.cs —— 探明 humanoid 动画片段的曲线通道名（决定「换部分骨骼」能不能靠通道筛选实现）
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

var sb = new StringBuilder();

AnimationClip Clip(string p, string n)
{
    foreach (var o in AssetDatabase.LoadAllAssetsAtPath(p))
    {
        var c = o as AnimationClip;
        if (c != null && !c.name.StartsWith("__preview__") && (n == null || c.name == n)) return c;
    }
    return null;
}

string ki = "Assets/ThirdParty/KevinIglesias/Animations/Male/Movement/Run/HumanM@Run01_Forward.fbx";
string ual1 = "Assets/ThirdParty/Quaternius/UniversalAnimationLibrary/Unity/AnimationLibrary_Unity_Standard.fbx";

var run = Clip(ki, null);
var idle = Clip(ual1, "Rig|Idle_Loop");
var swIdle = Clip(ual1, "Rig|Sword_Idle");

sb.AppendLine("run    = " + (run != null ? run.name + "  len=" + run.length : "<null>"));
sb.AppendLine("idle   = " + (idle != null ? idle.name + "  len=" + idle.length : "<null>"));
sb.AppendLine("swIdle = " + (swIdle != null ? swIdle.name + "  len=" + swIdle.length : "<null>"));
sb.AppendLine("run.legacy = " + (run != null ? run.legacy.ToString() : "?") + "   run.humanMotion = " + (run != null ? run.humanMotion.ToString() : "?"));
sb.AppendLine();

foreach (var (tag, clip) in new[] { ("RUN", run), ("IDLE", idle), ("SWORD_IDLE", swIdle) })
{
    if (clip == null) continue;
    var binds = AnimationUtility.GetCurveBindings(clip);
    sb.AppendLine("===== " + tag + " 曲线绑定数 = " + binds.Length + " =====");
    var groups = new SortedDictionary<string, int>();
    var examples = new SortedDictionary<string, string>();
    foreach (var b in binds)
    {
        string prop = b.propertyName;
        string key = prop.Contains(" ") ? prop.Substring(0, prop.IndexOf(' ')) : (prop.Contains(".") ? prop.Substring(0, prop.IndexOf('.')) : prop);
        if (!groups.ContainsKey(key)) { groups[key] = 0; examples[key] = prop; }
        groups[key]++;
    }
    foreach (var kv in groups) sb.AppendLine("   " + Pad(kv.Key, 22) + " × " + kv.Value + "   例:" + examples[kv.Key]);
    sb.AppendLine();
}

File.WriteAllText("Tools/reports/q_bind.txt", sb.ToString(), new UTF8Encoding(false));
return sb.ToString();

static string Pad(string s, int n) { return s.Length >= n ? s.Substring(0, n) : s + new string(' ', n - s.Length); }
