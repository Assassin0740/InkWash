// q_check_prefab.cs —— 为什么 prefab 文件里改成 `noGroundFixStates: []` 了，运行时还是读到 [Dash]？
// 直接问 Unity 内存里的那份 FootIK，并在强制重导后再问一次。
using System.IO;
using System.Text;
using UnityEngine;
using InkWash.Player;

var sb = new StringBuilder();
string path = "Assets/_Project/Prefabs/Player/Player.prefab";

void Dump(string tag)
{
    var go = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);
    var ik = go != null ? go.GetComponentInChildren<FootIK>(true) : null;
    sb.AppendLine(tag);
    sb.AppendLine("    预制体 = " + (go != null ? go.name : "null"));
    sb.AppendLine("    FootIK = " + (ik != null ? "有" : "无"));
    if (ik != null)
        sb.AppendLine("    noGroundFixStates.len = " + ik.noGroundFixStates.Length
            + "   [" + string.Join(",", ik.noGroundFixStates) + "]");
}

Dump("---------- ① 当前内存里的预制体 ----------");

UnityEditor.AssetDatabase.ImportAsset(path, UnityEditor.ImportAssetOptions.ForceUpdate);

Dump("---------- ② ForceUpdate 重导之后 ----------");

sb.AppendLine();
sb.AppendLine("---------- ③ 场景里的实例 ----------");
int n = 0;
foreach (var ik in Resources.FindObjectsOfTypeAll<FootIK>())
{
    var g = ik.gameObject;
    if (!g.scene.IsValid()) continue;          // 排除资产
    n++;
    sb.AppendLine("    " + g.name + " (场景 " + g.scene.name + ")"
        + "  noGroundFixStates.len = " + ik.noGroundFixStates.Length
        + "   [" + string.Join(",", ik.noGroundFixStates) + "]");
    sb.AppendLine("      预制体源 = "
        + (UnityEditor.PrefabUtility.GetCorrespondingObjectFromSource(g) != null ? "有（是预制体实例）" : "无（是独立副本）"));
}
sb.AppendLine("    共 " + n + " 个");

string rep = sb.ToString();
File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath, "../Tools/reports/q_prefab.txt")),
    rep, new UTF8Encoding(false));
return rep;
