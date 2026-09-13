// 编辑期复核：退出 Play 后，场景实例应当已同步预制体的新值。
using UnityEditor;
using UnityEngine;

var sb = new System.Text.StringBuilder();
sb.AppendLine("isPlaying = " + Application.isPlaying);
sb.AppendLine("活动场景 = " + UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene().path);

// 顺手强制重导入预制体，确保实例一定拿到最新序列化值
UnityEditor.AssetDatabase.ImportAsset("Assets/_Project/Prefabs/Player/Player.prefab",
    UnityEditor.ImportAssetOptions.ForceUpdate);

var ctl = Object.FindObjectOfType<InkWash.Player.PlayerController>();
if (ctl == null) return "[ERR] 场景里没有 PlayerController";
var go = ctl.gameObject;
sb.AppendLine("Player = " + go.name + "  PrefabInstanceStatus = "
    + UnityEditor.PrefabUtility.GetPrefabInstanceStatus(go));

var mods = UnityEditor.PrefabUtility.GetPropertyModifications(go);
int n = mods == null ? 0 : mods.Length;
sb.AppendLine("场景属性覆盖条数 = " + n);
if (mods != null)
    for (int i = 0; i < n; i++)
        sb.AppendLine("   " + mods[i].propertyPath + " = " + mods[i].value);

sb.AppendLine();
sb.AppendLine("walkSpeed=" + ctl.walkSpeed + "  runSpeed=" + ctl.runSpeed
    + "  walkRef=" + ctl.walkRefSpeed + "  runRef=" + ctl.runRefSpeed
    + "  dash=" + ctl.dashDuration);
sb.AppendLine("comboSwingDuration = [" + string.Join(", ", ctl.comboSwingDuration) + "]");

var vis = go.transform.Find("Visual");
sb.AppendLine("Visual.localPosition = " + (vis != null ? vis.localPosition.ToString("F4") : "(缺失)"));

var ik = go.GetComponent<InkWash.Player.FootIK>();
sb.AppendLine("FootIK.stateOffsets:");
if (ik != null && ik.stateOffsets != null)
    foreach (var o in ik.stateOffsets) sb.AppendLine(string.Format("   {0,-9} {1,8:F4}", o.state, o.y));
return sb.ToString();
