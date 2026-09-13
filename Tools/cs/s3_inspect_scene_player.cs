// 查清场景里的 Player 到底是「预制体实例」还是「普通对象」，以及哪些属性被场景覆盖了。
// 这决定了改预制体能不能影响场景 —— 上一版落地后场景仍读到旧值，必须定位原因。
using UnityEditor;
using UnityEngine;

var sb = new System.Text.StringBuilder();
var scene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
sb.AppendLine("活动场景 = " + scene.path + "  (dirty=" + scene.isDirty + ")");
sb.AppendLine("isPlaying = " + Application.isPlaying);

var ctl = Object.FindObjectOfType<InkWash.Player.PlayerController>();
if (ctl == null) return "[ERR] 场景里没有 PlayerController";
var go = ctl.gameObject;

var status = UnityEditor.PrefabUtility.GetPrefabInstanceStatus(go);
sb.AppendLine("Player 根对象 = " + go.name + "  层级 = " + go.transform.parent?.name);
sb.AppendLine("PrefabInstanceStatus = " + status);

var src = UnityEditor.PrefabUtility.GetCorrespondingObjectFromSource(go);
sb.AppendLine("对应源 = " + (src == null ? "(null)" : UnityEditor.AssetDatabase.GetAssetPath(src)));

if (status == UnityEditor.PrefabInstanceStatus.Connected)
{
    var mods = UnityEditor.PrefabUtility.GetPropertyModifications(go);
    sb.AppendLine();
    sb.AppendLine("场景上的属性覆盖（" + (mods == null ? 0 : mods.Length) + " 条）：");
    if (mods != null)
        for (int i = 0; i < mods.Length; i++)
            sb.AppendLine(string.Format("  [{0}] {1} = {2}", i,
                mods[i].propertyPath, mods[i].value));
}
else
{
    sb.AppendLine("不是连通的预制体实例 —— 改预制体不会影响它，必须直接改场景对象。");
}

// 顺带列出场景里所有 Player 相关对象的组件值
sb.AppendLine();
sb.AppendLine("场景实例上的组件值：");
sb.AppendLine("  walkSpeed=" + ctl.walkSpeed + " runSpeed=" + ctl.runSpeed
    + " walkRef=" + ctl.walkRefSpeed + " runRef=" + ctl.runRefSpeed + " dash=" + ctl.dashDuration);
var ik = go.GetComponent<InkWash.Player.FootIK>();
if (ik != null && ik.stateOffsets != null)
    foreach (var o in ik.stateOffsets) sb.AppendLine("  offset " + o.state + " = " + o.y);
return sb.ToString();
