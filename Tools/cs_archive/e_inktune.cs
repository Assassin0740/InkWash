// e_inktune.cs —— 把观感 A/B 定下来的推荐参数写进**水墨材质资产**。
//
// 必须在**编辑期**跑（不加 --runtime）：Play 里改材质实例不会持久化，
// 而且 Play 里写的资产改动会在退出时被回滚（本项目踩过"调着调着工程被改了"的反面）。
//
// 为什么要有这个脚本、而不是直接改 s4_build.cs 重跑施工：
//   施工脚本会连带重装 Feature / 重建预制体，改动面太大；这里只动一个数。
//   （s4_build.cs 里的默认值已经同步改成同一个数，两条路结果一致。）
using UnityEditor;
using UnityEngine;

var sb = new System.Text.StringBuilder();
string[] names =
{
    "M_Character_Ink",
    "M_Ink",
    "M_Ink_Enemy_MoOu_0", "M_Ink_Enemy_MoTu_0", "M_Ink_Enemy_MoYan_0",
};
const string Dir = "Assets/_Project/Art/Materials";
const float Target = 0.75f;

int n = 0;
foreach (var nm in names)
{
    string p = Dir + "/" + nm + ".mat";
    var m = AssetDatabase.LoadAssetAtPath<Material>(p);
    if (m == null) { sb.AppendLine("  [缺] " + p); continue; }
    if (!m.HasProperty("_InkDensity")) { sb.AppendLine("  [无此属性] " + nm); continue; }
    float old = m.GetFloat("_InkDensity");
    m.SetFloat("_InkDensity", Target);
    EditorUtility.SetDirty(m);
    sb.AppendLine("  " + nm + "  _InkDensity " + old.ToString("0.###") + " → " + Target.ToString("0.###"));
    n++;
}
AssetDatabase.SaveAssets();
AssetDatabase.Refresh();
sb.AppendLine("共处理 " + n + " 个材质");
Debug.Log(sb.ToString());
