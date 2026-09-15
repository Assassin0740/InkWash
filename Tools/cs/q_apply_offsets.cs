// q_apply_offsets.cs —— 把本轮两个新片段显式写进 Player.prefab 的 FootIK 偏移表
//
// ⚠ 必须在**编辑模式**下跑（不是 --runtime）：Play 下改预制体会被快照静默还原。
//
// 为什么要加「片段名」条目：FootIK.ResolveStateOffset 现在是**片段名优先、状态名兜底**。
// Idle 状态运行期会在两段之间切换：
//     Rig|Idle_Loop  (非战斗垂手)   ← 原 Idle 偏移 0.0307 就是为它标的
//     Sword_Idle_Loop(战斗持剑)     ← 本轮新换，实测需要 0.0477
// 一个状态一个数值必然有一段是错的（战斗待机实测恒定下沉 2.35 cm，全靠 IK 一直往上拽）。
// 把片段名写进表，两段各拿各的值。
//
// 数值来源：Tools/reports/q_offset3.txt（2026-09-15 重测）
//          口径：全踩地取最浅(hi)、有腾空取最深(lo)；offset = -pick + groundClearance
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

IEnumerator Body()
{
    var sb = new System.Text.StringBuilder();
    string projRoot = System.IO.Path.GetDirectoryName(Application.dataPath);
    System.Action flush = () => System.IO.File.WriteAllText(System.IO.Path.Combine(projRoot, "Tools/reports/q_apply_offsets.txt"), sb.ToString());

    const string PREFAB = "Assets/_Project/Prefabs/Player/Player.prefab";

    if (EditorApplication.isPlaying)
    {
        sb.AppendLine("[ERR] 当前在 Play 模式！先 stop 再跑本脚本，否则改动会被快照回滚。");
        flush(); yield break;
    }

    // 状态名兜底项：数值不动（上一轮已验收通过），只是原样保留
    var stateTarget = new (string k, float v)[]
    {
        ("Idle",    0.0307f),
        ("Walk",   -0.0554f),
        ("Run",     0.1735f),
        ("Dash",   -0.0041f),
        ("Atk1",    0.0200f),
        ("Atk1Rec", 0.1800f),
        ("Atk2",    0.0300f),
        ("Atk2Rec", 0.3600f),
        ("Atk3",    0.1000f),
    };

    // 新增：片段名精确绑定（优先于状态名命中）
    //   Run01_Carry 取 0.1735 而不是公式值 0.1807 —— IK 只上抬，静态偏移宁小勿大：
    //   小一点让 IK 去贴地，大一点会把整段跑步抬成"悬空"（6b 判据 ≤ +0.05 m）。
    var clipTarget = new (string k, float v)[]
    {
        ("Rig|Idle_Loop",  0.0307f),   // 非战斗待机（与 Idle 状态名同值，显式化）
        ("Sword_Idle_Loop", 0.0477f),  // ★ 战斗待机，本轮新片段，实测修正 +1.7 cm
        ("Feng_Walk_Loop", -0.0554f),  // 走路（与 Walk 状态名同值，显式化）
        ("Run01_Carry",    0.1735f),   // ★ 跑步，本轮新片段，显式化以免下次换片段时静默套用旧值
    };

    var contents = PrefabUtility.LoadPrefabContents(PREFAB);
    var ik = contents.GetComponent<InkWash.Player.FootIK>();
    if (ik == null) { sb.AppendLine("[ERR] 根上没有 FootIK"); PrefabUtility.UnloadPrefabContents(contents); flush(); yield break; }

    sb.AppendLine("---- 旧表 ----");
    if (ik.stateOffsets != null)
        foreach (var o in ik.stateOffsets) sb.AppendLine("  " + o.state.PadRight(20) + " " + o.y.ToString("F4"));

    var list = new List<InkWash.Player.FootIK.StateYOffset>();
    foreach (var e in stateTarget) list.Add(new InkWash.Player.FootIK.StateYOffset { state = e.k, y = e.v });
    foreach (var e in clipTarget) list.Add(new InkWash.Player.FootIK.StateYOffset { state = e.k, y = e.v });
    ik.stateOffsets = list.ToArray();

    sb.AppendLine("---- 新表（状态名 " + stateTarget.Length + " 条 + 片段名 " + clipTarget.Length + " 条）----");
    foreach (var o in ik.stateOffsets) sb.AppendLine("  " + o.state.PadRight(20) + " " + o.y.ToString("F4"));

    PrefabUtility.SaveAsPrefabAsset(contents, PREFAB);
    PrefabUtility.UnloadPrefabContents(contents);
    sb.AppendLine("已保存 " + PREFAB);

    flush();
    Debug.Log("[q_apply_offsets] done");
    yield return null;
}
return Body();
