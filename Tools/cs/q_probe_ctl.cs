// q_probe_ctl.cs —— 编辑态只读探查：控制器结构 + 待落地的三个现值
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

var sb = new StringBuilder();
string projRoot = Path.GetDirectoryName(Application.dataPath);

// ---------- ① 场景 Player 的控制器 ----------
sb.AppendLine("=== ① 场景 Player 的 Animator 控制器 ===");
var go = GameObject.Find("Player");
if (go != null)
{
    var anim = go.GetComponent<Animator>();
    var rc = anim != null ? anim.runtimeAnimatorController : null;
    sb.AppendLine("  runtimeAnimatorController = " + (rc != null ? rc.name + "  (" + rc.GetType().Name + ")" : "<null>"));
    if (rc != null) sb.AppendLine("  资产路径 = " + AssetDatabase.GetAssetPath(rc));
    var ovr = rc as AnimatorOverrideController;
    if (ovr != null)
    {
        sb.AppendLine("  base = " + (ovr.runtimeAnimatorController != null ? ovr.runtimeAnimatorController.name : "<null>")
            + "  路径 " + (ovr.runtimeAnimatorController != null ? AssetDatabase.GetAssetPath(ovr.runtimeAnimatorController) : "-"));
        var pairs = new List<KeyValuePair<AnimationClip, AnimationClip>>();
        ovr.GetOverrides(pairs);
        sb.AppendLine("  override 对 " + pairs.Count + " 个：");
        foreach (var kv in pairs)
            sb.AppendLine("     " + (kv.Key != null ? kv.Key.name : "<null>").PadRight(34) + " → " + (kv.Value != null ? kv.Value.name : "<null>"));
    }
    var ik0 = go.GetComponent<InkWash.Player.FootIK>();
    if (ik0 != null)
    {
        sb.AppendLine("  FootIK(运行时实例，仅供参照) stateOffsets:");
        if (ik0.stateOffsets != null)
            foreach (var o in ik0.stateOffsets) sb.AppendLine("     " + o.state.PadRight(22) + o.y.ToString("F4"));
    }
}
else sb.AppendLine("  场景里没有 Player（未打开 Game 场景？）");
sb.AppendLine();

// ---------- ② base AnimatorController 的状态与片段 ----------
sb.AppendLine("=== ② 项目内所有 AnimatorController 资产 ===");
foreach (var g in AssetDatabase.FindAssets("t:AnimatorController"))
{
    string p = AssetDatabase.GUIDToAssetPath(g);
    var ac = AssetDatabase.LoadAssetAtPath<AnimatorController>(p);
    if (ac == null) continue;
    sb.AppendLine("  " + p);
    foreach (var l in ac.layers)
    {
        sb.AppendLine("    层 \"" + l.name + "\"  状态 " + l.stateMachine.states.Length + " 个");
        foreach (var cs in l.stateMachine.states)
            sb.AppendLine("       " + cs.state.name.PadRight(22)
                + " motion=" + (cs.state.motion != null ? cs.state.motion.name + " (" + cs.state.motion.GetType().Name + ")" : "<null>")
                + "  speed=" + cs.state.speed.ToString("F2"));
    }
}
sb.AppendLine();

// ---------- ③ Player.prefab 的 CombatStance / FootIK 现值 ----------
sb.AppendLine("=== ③ Player.prefab 现值 ===");
const string PREFAB = "Assets/_Project/Prefabs/Player/Player.prefab";
if (File.Exists(PREFAB))
{
    var contents = PrefabUtility.LoadPrefabContents(PREFAB);
    var stance = contents.GetComponentInChildren<InkWash.Player.CombatStance>(true);
    if (stance != null)
        sb.AppendLine("  CombatStance.combatIdleClip = " + (stance.combatIdleClip != null ? stance.combatIdleClip.name : "<null>")
            + "  path=" + (stance.combatIdleClip != null ? AssetDatabase.GetAssetPath(stance.combatIdleClip) : "-"));
    var ik = contents.GetComponent<InkWash.Player.FootIK>();
    if (ik != null)
    {
        sb.AppendLine("  FootIK 根上，stateOffsets " + (ik.stateOffsets != null ? ik.stateOffsets.Length : 0) + " 条：");
        if (ik.stateOffsets != null)
            foreach (var o in ik.stateOffsets) sb.AppendLine("     " + o.state.PadRight(22) + o.y.ToString("F4"));
    }
    else sb.AppendLine("  [WARN] prefab 根上没有 FootIK（可能在子节点）");
    PrefabUtility.UnloadPrefabContents(contents);
}
else sb.AppendLine("  [ERR] 找不到 " + PREFAB);
sb.AppendLine();

// ---------- ④ Baked 目录现存片段 ----------
sb.AppendLine("=== ④ Assets/_Project/Animations/Baked 现存片段 ===");
foreach (var p in AssetDatabase.FindAssets("t:AnimationClip", new[] { "Assets/_Project/Animations" }))
{
    string path = AssetDatabase.GUIDToAssetPath(p);
    if (!path.Contains("/Baked/")) continue;
    var c = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
    if (c != null) sb.AppendLine("  " + Path.GetFileName(path).PadRight(34) + " len=" + c.length.ToString("F3") + " loop=" + c.isLooping);
}

File.WriteAllText(Path.Combine(projRoot, "Tools/reports/q_probe_ctl.txt"), sb.ToString(), new UTF8Encoding(false));
return sb.ToString();
