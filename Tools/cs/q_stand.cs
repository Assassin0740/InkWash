// q_stand.cs —— 清点「垂手站立」类候选片段 + 当前控制器各状态
// 目的：非战斗待机需要「自然双手下垂」的片段，先看本地有什么。
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEditor;

var sb = new StringBuilder();
sb.AppendLine("========== 当前 Player.controller 各状态片段 ==========");
foreach (var guid in AssetDatabase.FindAssets("t:AnimatorController"))
{
    string path = AssetDatabase.GUIDToAssetPath(guid);
    if (!path.Contains("Player.controller")) continue;
    var ac = AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(path);
    if (ac == null) continue;
    sb.AppendLine("控制器: " + path);
    foreach (var layer in ac.layers)
    {
        foreach (var st in layer.stateMachine.states)
        {
            string mn = st.state.motion != null ? st.state.motion.name : "<null>";
            string src = "";
            var clips = AssetDatabase.GetDependencies(AssetDatabase.GetAssetPath(st.state.motion), false);
            foreach (var c in clips) { if (c.EndsWith(".fbx") || c.EndsWith(".anim")) { src = c; break; } }
            sb.AppendLine(string.Format("  {0,-22} → {1,-24} ← {2}", st.state.name, mn, src));
        }
    }
}

sb.AppendLine();
sb.AppendLine("========== 全部 Humanoid 片段（按名字分组）==========");
sb.AppendLine("--- A. 名字像「待机/站立/垂手」的 ---");
var all = new List<string>();
foreach (var guid in AssetDatabase.FindAssets("t:AnimationClip"))
{
    string p = AssetDatabase.GUIDToAssetPath(guid);
    all.Add(p);
}
foreach (var p in AssetDatabase.FindAssets("t:Model"))
{
    string path = AssetDatabase.GUIDToAssetPath(p);
    if (!path.EndsWith(".fbx") && !path.EndsWith(".FBX")) continue;
    all.Add(path);
}
all.Sort();

var seen = new HashSet<string>();
int total = 0;
foreach (var path in all)
{
    Object[] objs;
    try { objs = AssetDatabase.LoadAllAssetsAtPath(path); } catch { continue; }
    foreach (var o in objs)
    {
        var clip = o as AnimationClip;
        if (clip == null || clip.length <= 0.01f) continue;
        if (clip.name.StartsWith("__preview__")) continue;
        total++;
        string low = clip.name.ToLower();
        bool hit = low.Contains("idle") || low.Contains("stand") || low.Contains("relax")
                || low.Contains("wait") || low.Contains("breath") || low.Contains("unarmed")
                || low.Contains("normal") || low.Contains("base");
        if (!hit) continue;
        string key = clip.name + "|" + path;
        if (!seen.Add(key)) continue;
        sb.AppendLine(string.Format("  {0,-30} [{1,5:F2}s] ← {2}", clip.name, clip.length, path));
    }
}
sb.AppendLine("（工程内带时长的片段总数: " + total + "）");

System.IO.File.WriteAllText("Tools/reports/q_stand.txt", sb.ToString(), new UTF8Encoding(false));
Debug.Log("[q_stand] done");
return null;
