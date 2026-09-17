using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using System.IO;
using System.Text;

// chk_mat_state.cs —— 只读探针：回答「敌人材质到底丢没丢」
// 判据：材质槽 null 数 == 0 且每个材质的 shader 非 null 且名字命中 ⇒ 材质没丢。
// Unity 在材质槽为 null 时才会渲染成洋红（missing material）。
string[] names = { "MoShan", "MoGuai", "MoGu", "MoLong", "MoYan", "MoTu", "MoOu" };
var sb = new StringBuilder();
sb.AppendLine("=== 敌人 prefab 材质 / shader 状态（只读，编辑模式）===");
sb.AppendLine("判据：空槽(null)==0 且 shader 名字非空 ⇒ 材质没有丢失");
sb.AppendLine();

foreach (string n in names)
{
    var guids = AssetDatabase.FindAssets("t:Prefab Z_Enemy_" + n);
    if (guids.Length == 0) { sb.AppendLine("--- " + n + " : prefab 未找到"); continue; }
    string path = AssetDatabase.GUIDToAssetPath(guids[0]);
    var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
    if (go == null) { sb.AppendLine("--- " + n + " : 加载失败 " + path); continue; }

    var smrs = go.GetComponentsInChildren<SkinnedMeshRenderer>(true);
    var mats = new List<string>();
    var shaders = new List<string>();
    int nullSlot = 0, totalSlot = 0, noMesh = 0;

    foreach (var s in smrs)
    {
        if (s.sharedMesh == null) noMesh++;
        var arr = s.sharedMaterials;
        if (arr == null) { nullSlot++; continue; }
        for (int i = 0; i < arr.Length; i++)
        {
            totalSlot++;
            if (arr[i] == null) { nullSlot++; continue; }
            if (!mats.Contains(arr[i].name)) mats.Add(arr[i].name);
            string sh = arr[i].shader == null ? "<shader=null>" : arr[i].shader.name;
            if (!shaders.Contains(sh)) shaders.Add(sh);
        }
    }

    sb.AppendLine("--- " + n + "    " + path);
    sb.AppendLine("    SMR=" + smrs.Length + "   无网格=" + noMesh
                  + "   材质槽=" + totalSlot + "   空槽(null)=" + nullSlot);
    sb.AppendLine("    材质名: " + string.Join(" | ", mats.ToArray()));
    sb.AppendLine("    shader: " + string.Join(" | ", shaders.ToArray()));
    sb.AppendLine();
}

Directory.CreateDirectory("Tools/reports");
File.WriteAllText("Tools/reports/chk_mat_state.txt", sb.ToString());
Debug.Log("[MAT] 写入 Tools/reports/chk_mat_state.txt");
return "MAT_OK";
