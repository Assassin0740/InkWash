// z22_cleanup —— 移除 PBR 对照实例与预览辅助对象，恢复场景整洁
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

var sb = new StringBuilder();

foreach (var n in new[] { "Z_Orge_PBR", "Z_Cam", "Z_Light" })
{
    var go = GameObject.Find(n);
    if (go != null) { UnityEngine.Object.DestroyImmediate(go); sb.AppendLine("删 " + n); }
    else sb.AppendLine("(无) " + n);
}

// Z_Rig 保留（用户可能想进 Play 直接看），但清掉多余的 meta
var rig = GameObject.Find("Z_Rig");
if (rig != null)
{
    // 只留 5 个素材
    var keep = new[] { "Z_Orge", "Z_Orc", "Z_Undead", "Z_Dragon", "Z_Dragon_LP" };
    for (int i = rig.transform.childCount - 1; i >= 0; i--)
    {
        var c = rig.transform.GetChild(i);
        if (System.Array.IndexOf(keep, c.name) < 0)
        {
            sb.AppendLine("删 Z_Rig 下多余 " + c.name);
            UnityEngine.Object.DestroyImmediate(c.gameObject);
        }
    }
    sb.AppendLine("Z_Rig 保留，子对象数 = " + rig.transform.childCount);
}

var sc = EditorSceneManager.GetActiveScene();
EditorSceneManager.MarkSceneDirty(sc);
EditorSceneManager.SaveScene(sc);
sb.AppendLine("场景已保存 " + sc.path);

AssetDatabase.SaveAssets();
Directory.CreateDirectory("Tools/reports");
File.WriteAllText("Tools/reports/z22_cleanup.txt", sb.ToString(), Encoding.UTF8);
return sb.ToString();
