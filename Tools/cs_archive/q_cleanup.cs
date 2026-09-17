// q_cleanup.cs —— 清理本轮未采用的烘焙变体，并清掉随之失效的 stateOffsets 死条目
// 保留：Idle_Carry_A（在用：持剑待机）、Run04_KI_Carry（在用：跑步）
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

IEnumerator Body()
{
    var sb = new StringBuilder();
    string projRoot = Path.GetDirectoryName(Application.dataPath);
    System.Action flush = () => File.WriteAllText(Path.Combine(projRoot, "Tools/reports/q_cleanup.txt"), sb.ToString());

    if (EditorApplication.isPlaying) { sb.AppendLine("[ERR] 在 Play 模式，先 stop。"); flush(); yield break; }

    const string DIR = "Assets/_Project/Animations/Baked";
    const string PREFAB = "Assets/_Project/Prefabs/Player/Player.prefab";
    const string CTRL = "Assets/_Project/Animations/Player.controller";

    string[] keep = { "Idle_Carry_A", "Run04_KI_Carry" };

    // ---------- ① 先确认在用的片段确实被引用 ----------
    sb.AppendLine("=== ① 引用检查（删之前先确认 keep 列表真的在用）===");
    var ac = AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(CTRL);
    var usedByCtrl = new HashSet<string>();
    foreach (var l in ac.layers)
        foreach (var cs in l.stateMachine.states)
            if (cs.state.motion != null) usedByCtrl.Add(cs.state.motion.name);
    sb.AppendLine("  控制器在用：" + string.Join(", ", usedByCtrl));

    var contents = PrefabUtility.LoadPrefabContents(PREFAB);
    var stance = contents.GetComponentInChildren<InkWash.Player.CombatStance>(true);
    string idle = stance != null && stance.combatIdleClip != null ? stance.combatIdleClip.name : "<null>";
    sb.AppendLine("  CombatStance.combatIdleClip = " + idle);
    if (!keep.Contains(idle)) { sb.AppendLine("[ERR] 持剑待机不在 keep 列表，中止"); PrefabUtility.UnloadPrefabContents(contents); flush(); yield break; }
    if (!usedByCtrl.Contains("Run04_KI_Carry")) { sb.AppendLine("[ERR] Run04_KI_Carry 没被控制器引用，中止"); PrefabUtility.UnloadPrefabContents(contents); flush(); yield break; }
    sb.AppendLine("  ✓ keep 列表与实际引用一致，可以删其余");
    sb.AppendLine();

    // ---------- ② 删除未采用变体 ----------
    sb.AppendLine("=== ② 删除未采用的烘焙变体 ===");
    var all = AssetDatabase.FindAssets("t:AnimationClip", new[] { DIR })
        .Select(AssetDatabase.GUIDToAssetPath).Where(p => p.EndsWith(".anim")).ToList();
    int del = 0;
    foreach (var p in all)
    {
        string n = Path.GetFileNameWithoutExtension(p);
        if (keep.Contains(n)) { sb.AppendLine("  保留 " + n); continue; }
        if (AssetDatabase.DeleteAsset(p)) { sb.AppendLine("  删除 " + p); del++; }
        else sb.AppendLine("  [WARN] 删除失败 " + p);
    }
    sb.AppendLine("  共删除 " + del + " 个");
    sb.AppendLine();

    // ---------- ③ 清 stateOffsets 死条目 ----------
    sb.AppendLine("=== ③ stateOffsets 死条目清理 ===");
    var ik = contents.GetComponent<InkWash.Player.FootIK>();
    if (ik != null && ik.stateOffsets != null)
    {
        var alive = new HashSet<string>();
        foreach (var p in AssetDatabase.FindAssets("t:AnimationClip"))
        {
            var cl = AssetDatabase.LoadAssetAtPath<AnimationClip>(AssetDatabase.GUIDToAssetPath(p));
            if (cl != null) alive.Add(cl.name);
        }

        var list = new List<InkWash.Player.FootIK.StateYOffset>();
        foreach (var o in ik.stateOffsets)
        {
            // 只清理「看起来是片段名」的条目（带 | 或 与现存片段同名）；状态名（Idle/Walk/...）一律保留
            bool looksLikeStateName = new[] { "Idle", "Walk", "Run", "Dash", "Atk1", "Atk1Rec", "Atk2", "Atk2Rec", "Atk3" }.Contains(o.state);
            if (looksLikeStateName || alive.Contains(o.state)) list.Add(o);
            else sb.AppendLine("  丢弃死条目：" + o.state + " (" + o.y.ToString("F4") + ")");
        }
        ik.stateOffsets = list.ToArray();
        EditorUtility.SetDirty(ik);
        sb.AppendLine("  剩余 " + ik.stateOffsets.Length + " 条：");
        foreach (var o in ik.stateOffsets) sb.AppendLine("    " + o.state.PadRight(22) + o.y.ToString("F4"));
        PrefabUtility.SaveAsPrefabAsset(contents, PREFAB);
        sb.AppendLine("  已保存 " + PREFAB);
    }
    PrefabUtility.UnloadPrefabContents(contents);

    AssetDatabase.SaveAssets();
    AssetDatabase.Refresh();

    // ---------- ④ 复核 ----------
    sb.AppendLine();
    sb.AppendLine("=== ④ 复核：Baked 目录剩余 ===");
    foreach (var p in AssetDatabase.FindAssets("t:AnimationClip", new[] { DIR })
        .Select(AssetDatabase.GUIDToAssetPath).Where(p => p.EndsWith(".anim")).OrderBy(p => p))
        sb.AppendLine("  " + Path.GetFileName(p));

    flush();
    Debug.Log("[q_cleanup] done");
    yield return null;
}
return Body();
