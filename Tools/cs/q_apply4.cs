// q_apply4.cs —— 编辑态落地本轮最终参数：
//   ① 控制器 Run 状态 motion → Run04_KI_Carry（修好右脚 Twist、保留 Up-Down 均值、右臂持剑）
//   ② FootIK.stateOffsets：Run 系 0.12（原 0.1735，按扫描 (0.12,150) 档）、新增 Idle_Carry_A 显式条目
//   ③ FootIK.liftSmooth：14 → 150（★本轮关键：提速后 BodyLift 能跟上落地突变，才能用小 BodyBase 不飘又不穿地）
//
// 依据 Tools/reports/q_scan3.txt：
//   BodyBase=0.12 liftSmooth=150 → 实测最低 −0.015（体检 ≥−0.02 通过）、中位 0.048（原方案 0.164，飘量降 11.6cm）、穿地帧 0.0%
//   对照组 BodyBase=0.24 liftSmooth=14 → 实测最低 −0.010 但中位 0.164（用户看到的「高出来一格」）
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

IEnumerator Body()
{
    var sb = new StringBuilder();
    string projRoot = Path.GetDirectoryName(Application.dataPath);
    System.Action flush = () => File.WriteAllText(Path.Combine(projRoot, "Tools/reports/q_apply4.txt"), sb.ToString());

    if (EditorApplication.isPlaying)
    {
        sb.AppendLine("[ERR] 在 Play 模式，先 stop。");
        flush(); yield break;
    }

    const string CTRL = "Assets/_Project/Animations/Player.controller";
    const string PREFAB = "Assets/_Project/Prefabs/Player/Player.prefab";
    const string NEW_RUN = "Assets/_Project/Animations/Baked/Run04_KI_Carry.anim";
    const float RUN_Y = 0.12f;
    const float LIFT_SMOOTH = 150f;

    var newRun = AssetDatabase.LoadAssetAtPath<AnimationClip>(NEW_RUN);
    if (newRun == null) { sb.AppendLine("[ERR] 缺 " + NEW_RUN); flush(); yield break; }

    // ---------- ① 控制器 Run ----------
    sb.AppendLine("=== ① Player.controller Run 状态 ===");
    var ac = AssetDatabase.LoadAssetAtPath<AnimatorController>(CTRL);
    foreach (var l in ac.layers)
        foreach (var cs in l.stateMachine.states)
            if (cs.state.name == "Run")
            {
                string old = cs.state.motion != null ? cs.state.motion.name : "<null>";
                cs.state.motion = newRun;
                EditorUtility.SetDirty(cs.state);
                sb.AppendLine("  Run: " + old + "  →  " + newRun.name);
            }
    EditorUtility.SetDirty(ac);
    AssetDatabase.SaveAssets();

    // ---------- ② + ③ prefab ----------
    sb.AppendLine();
    sb.AppendLine("=== ② Player.prefab：stateOffsets + liftSmooth ===");
    var contents = PrefabUtility.LoadPrefabContents(PREFAB);
    var ik = contents.GetComponent<InkWash.Player.FootIK>();
    var stance = contents.GetComponentInChildren<InkWash.Player.CombatStance>(true);
    if (ik == null) sb.AppendLine("  [ERR] 根上没有 FootIK");
    else
    {
        sb.AppendLine("  ---- 旧 ----");
        if (ik.stateOffsets != null)
            foreach (var o in ik.stateOffsets) sb.AppendLine("    " + o.state.PadRight(22) + o.y.ToString("F4"));
        sb.AppendLine("    liftSmooth = " + ik.liftSmooth.ToString("F1"));

        var list = new List<InkWash.Player.FootIK.StateYOffset>();
        var seen = new HashSet<string>();
        if (ik.stateOffsets != null)
            foreach (var o in ik.stateOffsets)
            {
                if (o.state == "Run") { list.Add(new InkWash.Player.FootIK.StateYOffset { state = "Run", y = RUN_Y }); seen.Add("Run"); }
                else if (o.state == "Run01_Carry") { /* 旧片段，已被 Run04_KI_Carry 取代，丢弃 */ }
                else { list.Add(new InkWash.Player.FootIK.StateYOffset { state = o.state, y = o.y }); seen.Add(o.state); }
            }
        void Ensure(string k, float v)
        {
            if (seen.Contains(k)) return;
            list.Add(new InkWash.Player.FootIK.StateYOffset { state = k, y = v });
            seen.Add(k);
        }
        Ensure("Run04_KI_Carry", RUN_Y);   // ★ 本轮新跑步片段（片名优先命中）
        Ensure("Run02_Sprint_Carry", RUN_Y);
        Ensure("Idle_Carry_A", 0.0307f);   // ★ 本轮新持剑待机（腿部与 Rig|Idle_Loop 同源，同值）

        ik.stateOffsets = list.ToArray();
        ik.liftSmooth = LIFT_SMOOTH;
        EditorUtility.SetDirty(ik);

        sb.AppendLine("  ---- 新 ----");
        foreach (var o in ik.stateOffsets) sb.AppendLine("    " + o.state.PadRight(22) + o.y.ToString("F4"));
        sb.AppendLine("    liftSmooth = " + ik.liftSmooth.ToString("F1"));

        sb.AppendLine();
        sb.AppendLine("  CombatStance.combatIdleClip = "
            + (stance != null && stance.combatIdleClip != null ? stance.combatIdleClip.name : "<null>"));
        PrefabUtility.SaveAsPrefabAsset(contents, PREFAB);
        sb.AppendLine("  已保存 " + PREFAB);
    }
    PrefabUtility.UnloadPrefabContents(contents);
    AssetDatabase.SaveAssets();
    AssetDatabase.Refresh();

    // ---------- ④ 复核 ----------
    sb.AppendLine();
    sb.AppendLine("=== ④ 复核 ===");
    var ac2 = AssetDatabase.LoadAssetAtPath<AnimatorController>(CTRL);
    foreach (var l in ac2.layers)
        foreach (var cs in l.stateMachine.states)
            if (cs.state.name == "Run")
                sb.AppendLine("  控制器 Run motion = " + (cs.state.motion != null ? cs.state.motion.name : "<null>"));
    var c2 = PrefabUtility.LoadPrefabContents(PREFAB);
    var ik2 = c2.GetComponent<InkWash.Player.FootIK>();
    sb.AppendLine("  liftSmooth = " + ik2.liftSmooth.ToString("F1") + "  条目 " + ik2.stateOffsets.Length + " 条");
    foreach (var o in ik2.stateOffsets)
        if (o.state.Contains("Run") || o.state.Contains("Idle")) sb.AppendLine("    " + o.state.PadRight(22) + o.y.ToString("F4"));
    PrefabUtility.UnloadPrefabContents(c2);

    flush();
    Debug.Log("[q_apply4] done");
    yield return null;
}
return Body();
