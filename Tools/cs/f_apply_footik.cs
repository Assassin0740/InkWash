// 把 f_calib_footik.cs 量出的 9 段偏移推进 Player.prefab。
//
// ⚠ 必须在**编辑模式**下运行（不是 --runtime）。Play 模式下改预制体会在退出 Play 时
//   被快照静默还原 —— 改了等于没改。
//
// 数值来源：Tools/reports/F_footik.txt（2026-09-14 换 Feng 后重测）
// 规则：offset = -(该状态无 IK 时网格最低点相对地面) + 0.003
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

IEnumerator Body()
{
    var sb = new System.Text.StringBuilder();
    string projRoot = System.IO.Path.GetDirectoryName(Application.dataPath);
    System.Action flush = () => System.IO.File.WriteAllText(System.IO.Path.Combine(projRoot, "Tools/reports/f_apply_footik.txt"), sb.ToString());

    const string PREFAB = "Assets/_Project/Prefabs/Player/Player.prefab";

    if (EditorApplication.isPlaying)
    {
        sb.AppendLine("[ERR] 当前在 Play 模式！请先 stop 再跑本脚本 —— 否则改动会被快照回滚。");
        flush();
        yield break;
    }
    sb.AppendLine("EditorApplication.isPlaying = false ✓");

    // 目标偏移（Feng 重标定）
    var target = new Dictionary<string, float>
    {
        { "Idle",    -0.0003f },
        { "Walk",     0.0569f },
        { "Run",     -0.2294f },
        { "Dash",    -0.0041f },
        { "Atk1",     0.0446f },
        { "Atk1Rec", -0.0030f },
        { "Atk2",     0.0494f },
        { "Atk2Rec",  0.0022f },
        { "Atk3",     0.0129f },
    };

    var contents = PrefabUtility.LoadPrefabContents(PREFAB);
    var ik = contents.GetComponent<InkWash.Player.FootIK>();
    if (ik == null) { sb.AppendLine("[ERR] 根上没有 FootIK"); PrefabUtility.UnloadPrefabContents(contents); flush(); yield break; }

    sb.AppendLine("---- 旧值（KayKit 时代）----");
    if (ik.stateOffsets != null)
        foreach (var o in ik.stateOffsets) sb.AppendLine("  " + o.state + "  " + o.y.ToString("F4"));

    var list = new List<InkWash.Player.FootIK.StateYOffset>();
    foreach (var kv in target)
        list.Add(new InkWash.Player.FootIK.StateYOffset { state = kv.Key, y = kv.Value });
    ik.stateOffsets = list.ToArray();

    sb.AppendLine("---- 新值（Feng）----");
    foreach (var o in ik.stateOffsets) sb.AppendLine("  " + o.state + "  " + o.y.ToString("F4"));

    var vis = contents.transform.Find("Visual");
    sb.AppendLine("Visual.localPosition = " + vis.localPosition.ToString("F4") + "  localScale = " + vis.localScale.ToString("F4"));

    PrefabUtility.SaveAsPrefabAsset(contents, PREFAB);
    PrefabUtility.UnloadPrefabContents(contents);
    sb.AppendLine("已保存 " + PREFAB);

    flush();
    Debug.Log("[apply-feng] " + sb.ToString());
    yield return null;
}
return Body();
