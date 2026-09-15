// q_wire3.cs —— 把「左手也握拳」写进 Player.prefab，并重新读盘复核。
// 用 PrefabUtility.LoadPrefabContents / SaveAsPrefabAsset 走正规 API，不手改 YAML。
using System.Collections;
using UnityEngine;
using UnityEditor;

IEnumerator Body()
{
    var sb = new System.Text.StringBuilder();
    string projRoot = System.IO.Path.GetDirectoryName(Application.dataPath);
    string path = "Assets/_Project/Prefabs/Player/Player.prefab";

    yield return null;

    var root = PrefabUtility.LoadPrefabContents(path);
    var pose = root.GetComponentInChildren<InkWash.Effects.WeaponHandPose>(true);
    if (pose == null)
    {
        sb.AppendLine("！没找到 WeaponHandPose");
    }
    else
    {
        sb.AppendLine("========== 改前 ==========");
        sb.AppendLine("  rightHand      = " + pose.rightHand);
        sb.AppendLine("  leftHand       = " + pose.leftHand);
        sb.AppendLine("  fingerCurl     = " + pose.fingerCurl);
        sb.AppendLine("  thumbCurl      = " + pose.thumbCurl);
        sb.AppendLine("  leftFingerCurl = " + pose.leftFingerCurl + "   (新增字段，取 C# 默认)");
        sb.AppendLine("  leftThumbCurl  = " + pose.leftThumbCurl);

        pose.leftHand = true;
        EditorUtility.SetDirty(pose);
        PrefabUtility.SaveAsPrefabAsset(root, path);
        sb.AppendLine();
        sb.AppendLine("已写入并保存。");
    }
    PrefabUtility.UnloadPrefabContents(root);

    // ---- 重新读盘复核（别信内存里的对象）----
    AssetDatabase.Refresh();
    var check = AssetDatabase.LoadAssetAtPath<GameObject>(path);
    var p2 = check != null ? check.GetComponentInChildren<InkWash.Effects.WeaponHandPose>(true) : null;
    sb.AppendLine();
    sb.AppendLine("========== 重新读盘复核 ==========");
    if (p2 == null) sb.AppendLine("！复核失败：读不到组件");
    else
    {
        sb.AppendLine("  rightHand      = " + p2.rightHand);
        sb.AppendLine("  leftHand       = " + p2.leftHand);
        sb.AppendLine("  fingerCurl     = " + p2.fingerCurl);
        sb.AppendLine("  thumbCurl      = " + p2.thumbCurl);
        sb.AppendLine("  leftFingerCurl = " + p2.leftFingerCurl);
        sb.AppendLine("  leftThumbCurl  = " + p2.leftThumbCurl);
        sb.AppendLine("  路径 = " + AssetDatabase.GetAssetPath(p2));
    }

    // ---- 顺带确认 CombatStance 还在、开关逻辑没被破坏 ----
    var cs = check != null ? check.GetComponentInChildren<InkWash.Player.CombatStance>(true) : null;
    sb.AppendLine("  CombatStance.handPose = " + (cs != null && cs.handPose != null ? cs.handPose.name : "<未接线>"));
    sb.AppendLine("  CombatStance.backSocket = " + (cs != null && cs.backSocket != null ? cs.backSocket.name : "<未接线>"));

    System.IO.File.WriteAllText(System.IO.Path.Combine(projRoot, "Tools/reports/q_wire3.txt"), sb.ToString());
    Debug.Log("[q_wire3] done");
    yield return null;
}

return Body();
