// q_fix_offsets.cs —— 补回被清理脚本误删的 Rig|Idle_Loop 条目，并清掉已失效的 Sword_Idle_Loop 条目
//
// 误删原因（值得记进技能）：q_cleanup.cs 用 AssetDatabase.FindAssets("t:AnimationClip") 建「现存片段名」集合，
//   但该 API 对 FBX **不返回内部子资产**（UAL1 里的 Rig|Idle_Loop 就查不到），LoadAssetAtPath<AnimationClip>(fbx) 也返回 null，
//   于是 Rig|Idle_Loop 被当成死条目丢弃。正确做法是 AssetDatabase.LoadAllAssetsAtPath(fbx) 再逐个子资产收集。
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

IEnumerator Body()
{
    var sb = new StringBuilder();
    string projRoot = Path.GetDirectoryName(Application.dataPath);
    System.Action flush = () => File.WriteAllText(Path.Combine(projRoot, "Tools/reports/q_fix_offsets.txt"), sb.ToString());

    if (EditorApplication.isPlaying) { sb.AppendLine("[ERR] 在 Play 模式，先 stop。"); flush(); yield break; }

    const string PREFAB = "Assets/_Project/Prefabs/Player/Player.prefab";
    var contents = PrefabUtility.LoadPrefabContents(PREFAB);
    var ik = contents.GetComponent<InkWash.Player.FootIK>();
    if (ik == null) { sb.AppendLine("[ERR] 根上没有 FootIK"); PrefabUtility.UnloadPrefabContents(contents); flush(); yield break; }

    sb.AppendLine("---- 旧 ----");
    foreach (var o in ik.stateOffsets) sb.AppendLine("  " + o.state.PadRight(22) + o.y.ToString("F4"));

    var list = new List<InkWash.Player.FootIK.StateYOffset>();
    var seen = new HashSet<string>();
    foreach (var o in ik.stateOffsets)
    {
        if (o.state == "Sword_Idle_Loop") { sb.AppendLine("  丢弃已失效条目：Sword_Idle_Loop（combatIdleClip 已换成 Idle_Carry_A）"); continue; }
        list.Add(o); seen.Add(o.state);
    }
    void Ensure(string k, float v)
    {
        if (seen.Contains(k)) return;
        list.Add(new InkWash.Player.FootIK.StateYOffset { state = k, y = v });
        seen.Add(k);
        sb.AppendLine("  补回：" + k + " = " + v.ToString("F4"));
    }
    Ensure("Rig|Idle_Loop", 0.0307f);   // 非战斗待机（UAL1 FBX 内部片段，与 Idle 状态名同值）

    ik.stateOffsets = list.ToArray();
    EditorUtility.SetDirty(ik);
    PrefabUtility.SaveAsPrefabAsset(contents, PREFAB);
    PrefabUtility.UnloadPrefabContents(contents);
    AssetDatabase.SaveAssets();
    AssetDatabase.Refresh();

    sb.AppendLine();
    sb.AppendLine("---- 新（" + list.Count + " 条）----");
    var c2 = PrefabUtility.LoadPrefabContents(PREFAB);
    var ik2 = c2.GetComponent<InkWash.Player.FootIK>();
    foreach (var o in ik2.stateOffsets) sb.AppendLine("  " + o.state.PadRight(22) + o.y.ToString("F4"));
    sb.AppendLine("  liftSmooth = " + ik2.liftSmooth.ToString("F1"));
    PrefabUtility.UnloadPrefabContents(c2);

    flush();
    Debug.Log("[q_fix_offsets] done");
    yield return null;
}
return Body();
