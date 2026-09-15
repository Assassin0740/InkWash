// q_apply3.cs —— 编辑态落地：① Player.controller 的 Run 状态 motion → Run02_Sprint_Carry
//                            ② Player.prefab 的 CombatStance.combatIdleClip → Baked/Idle_Carry_A
//
// ⚠ 必须编辑模式（Play 下改预制体/控制器会被快照静默回滚）。
// 为什么不改场景里那个 override controller：它是 CombatStance.Awake 里 new 出来的**运行期内存对象**，
//   只覆盖 Idle 槽位；Run 槽位的片段来自 base controller(Player.controller) 的 Run 状态 motion。
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

IEnumerator Body()
{
    var sb = new StringBuilder();
    string projRoot = Path.GetDirectoryName(Application.dataPath);
    System.Action flush = () => File.WriteAllText(Path.Combine(projRoot, "Tools/reports/q_apply3.txt"), sb.ToString());

    if (EditorApplication.isPlaying)
    {
        sb.AppendLine("[ERR] 在 Play 模式，先 stop 再跑。");
        flush(); yield break;
    }

    const string CTRL = "Assets/_Project/Animations/Player.controller";
    const string PREFAB = "Assets/_Project/Prefabs/Player/Player.prefab";
    const string NEW_RUN = "Assets/_Project/Animations/Baked/Run02_Sprint_Carry.anim";
    const string NEW_IDLE = "Assets/_Project/Animations/Baked/Idle_Carry_A.anim";

    var newRun = AssetDatabase.LoadAssetAtPath<AnimationClip>(NEW_RUN);
    var newIdle = AssetDatabase.LoadAssetAtPath<AnimationClip>(NEW_IDLE);
    sb.AppendLine("newRun  = " + (newRun != null ? newRun.name + " len=" + newRun.length.ToString("F3") : "<缺> " + NEW_RUN));
    sb.AppendLine("newIdle = " + (newIdle != null ? newIdle.name + " len=" + newIdle.length.ToString("F3") : "<缺> " + NEW_IDLE));
    if (newRun == null || newIdle == null) { sb.AppendLine("[ERR] 片段缺失，中止"); flush(); yield break; }
    sb.AppendLine();

    // ---------- ① 控制器 Run 状态 ----------
    sb.AppendLine("=== ① Player.controller 的 Run 状态 ===");
    var ac = AssetDatabase.LoadAssetAtPath<AnimatorController>(CTRL);
    if (ac == null) sb.AppendLine("[ERR] 载不到 " + CTRL);
    else
    {
        bool done = false;
        foreach (var l in ac.layers)
        {
            foreach (var cs in l.stateMachine.states)
            {
                if (cs.state.name != "Run") continue;
                string old = cs.state.motion != null ? cs.state.motion.name : "<null>";
                cs.state.motion = newRun;
                EditorUtility.SetDirty(cs.state);
                sb.AppendLine("  层 \"" + l.name + "\" 状态 Run: " + old + "  →  " + newRun.name);
                done = true;
            }
        }
        if (!done) sb.AppendLine("  [ERR] 没找到 Run 状态");
        EditorUtility.SetDirty(ac);
        AssetDatabase.SaveAssets();
    }
    sb.AppendLine();

    // ---------- ② prefab 的 CombatStance ----------
    sb.AppendLine("=== ② Player.prefab 的 CombatStance.combatIdleClip ===");
    var contents = PrefabUtility.LoadPrefabContents(PREFAB);
    var stance = contents.GetComponentInChildren<InkWash.Player.CombatStance>(true);
    if (stance == null) sb.AppendLine("[ERR] prefab 里找不到 CombatStance");
    else
    {
        string old = stance.combatIdleClip != null ? stance.combatIdleClip.name : "<null>";
        stance.combatIdleClip = newIdle;
        sb.AppendLine("  combatIdleClip: " + old + "  →  " + newIdle.name);
        EditorUtility.SetDirty(stance);
        PrefabUtility.SaveAsPrefabAsset(contents, PREFAB);
        sb.AppendLine("  已保存 " + PREFAB);
    }
    PrefabUtility.UnloadPrefabContents(contents);
    AssetDatabase.SaveAssets();
    AssetDatabase.Refresh();
    sb.AppendLine();

    // ---------- ③ 复核（重新载入读一遍）----------
    sb.AppendLine("=== ③ 复核 ===");
    var ac2 = AssetDatabase.LoadAssetAtPath<AnimatorController>(CTRL);
    foreach (var l in ac2.layers)
        foreach (var cs in l.stateMachine.states)
            if (cs.state.name == "Run")
                sb.AppendLine("  控制器 Run motion = " + (cs.state.motion != null ? cs.state.motion.name : "<null>")
                    + "   路径 " + (cs.state.motion != null ? AssetDatabase.GetAssetPath(cs.state.motion) : "-"));
    var c2 = PrefabUtility.LoadPrefabContents(PREFAB);
    var s2 = c2.GetComponentInChildren<InkWash.Player.CombatStance>(true);
    if (s2 != null)
        sb.AppendLine("  prefab combatIdleClip = " + (s2.combatIdleClip != null ? s2.combatIdleClip.name : "<null>")
            + "   路径 " + (s2.combatIdleClip != null ? AssetDatabase.GetAssetPath(s2.combatIdleClip) : "-"));
    PrefabUtility.UnloadPrefabContents(c2);

    flush();
    Debug.Log("[q_apply3] done");
    yield return null;
}
return Body();
