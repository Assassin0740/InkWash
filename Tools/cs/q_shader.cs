using System.Collections;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

IEnumerator Body()
{
    var sb = new StringBuilder();
    string projRoot = Path.GetDirectoryName(Application.dataPath);
    string p = "Assets/_Project/Art/Shaders/InkCharacter.shader";
    sb.AppendLine("asset 存在 = " + (File.Exists(p)));
    var obj = AssetDatabase.LoadAssetAtPath<Object>(p);
    sb.AppendLine("LoadAssetAtPath → " + (obj == null ? "<null>" : obj.GetType().FullName + "  name=" + obj.name));
    var sh = AssetDatabase.LoadAssetAtPath<Shader>(p);
    sb.AppendLine("作为 Shader → " + (sh == null ? "<null>" : sh.name + "  isSupported=" + sh.isSupported + "  passCount=" + sh.passCount));
    sb.AppendLine("Shader.Find 三个：");
    foreach (var n in new[] { "InkWash/InkCharacter", "Hidden/InkWash/InkEdge", "Hidden/InkWash/InkPaper" })
        sb.AppendLine("  " + n + " → " + (Shader.Find(n) != null));
    sb.AppendLine("Assets 下所有 shader：");
    foreach (var g in AssetDatabase.FindAssets("t:Shader", new[] { "Assets/_Project" }))
        sb.AppendLine("  " + AssetDatabase.GUIDToAssetPath(g));
    sb.AppendLine("Art/Shaders 下所有资产：");
    foreach (var g in AssetDatabase.FindAssets("", new[] { "Assets/_Project/Art/Shaders" }))
        sb.AppendLine("  " + AssetDatabase.GUIDToAssetPath(g));
    File.WriteAllText(Path.Combine(projRoot, "Tools/reports/q_shader.txt"), sb.ToString());
    Debug.Log("[q_shader] done");
    yield return null;
}
return Body();
