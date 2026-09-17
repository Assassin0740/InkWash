// z23_cleanup_rig.cs —— 提交前收尾：把拍图用的临时展示架 Z_Rig 从主场景移除
//
// 为什么不在 z22 里一起删：
//   z22 跑的时候还要再拍 F1 全景图与 E1/E2 对拍，Z_Rig 是那些图的取景对象。
//   图拍完、图板生成完，它就成了纯垃圾 —— 留在 Main.unity 里会让每次进 Play
//   都在舞台上多 5 个不参与战斗的模型（还会被 InkMaterialForcer 扫描一遍）。
//
// 注意：删的是**场景实例**，不是 prefab。prefab 资产在 Assets/_Project/Prefabs/Ziyuan/ 下，
// 后续接战斗系统时要用。
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

var sb = new StringBuilder();

var rig = GameObject.Find("Z_Rig");
if (rig != null)
{
    sb.AppendLine("Z_Rig 子对象 " + rig.transform.childCount + " 个：");
    for (int i = 0; i < rig.transform.childCount; i++)
        sb.AppendLine("   - " + rig.transform.GetChild(i).name);
    UnityEngine.Object.DestroyImmediate(rig);
    sb.AppendLine("已删 Z_Rig");
}
else
{
    sb.AppendLine("(无) Z_Rig —— 已经清过了");
}

// 顺手扫一遍还有没有别的 z* 预览残留
foreach (var n in new[] { "Z_Cam", "Z_Light", "Z_Orge_PBR" })
{
    var go = GameObject.Find(n);
    if (go != null) { UnityEngine.Object.DestroyImmediate(go); sb.AppendLine("删残留 " + n); }
}

// 复核：场景里不该再有 Z_ 前缀的对象
var sc = EditorSceneManager.GetActiveScene();
int leftover = 0;
foreach (var go in sc.GetRootGameObjects())
{
    if (go.name.StartsWith("Z_")) { sb.AppendLine("★ 仍有残留根对象 " + go.name); leftover++; }
}
sb.AppendLine("场景根对象残留 Z_* 数量 = " + leftover);

EditorSceneManager.MarkSceneDirty(sc);
EditorSceneManager.SaveScene(sc);
sb.AppendLine("场景已保存 " + sc.path);

AssetDatabase.SaveAssets();
AssetDatabase.Refresh();

File.WriteAllText(Path.Combine(Path.GetDirectoryName(Application.dataPath), "Tools/reports/z23_cleanup_rig.txt"), sb.ToString());
Debug.Log("[z23] " + sb.ToString());
