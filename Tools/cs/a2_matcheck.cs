// a2_matcheck.cs —— 验证「ForceInk 抹平多材质」假设
//
// 背景：InkMaterialForcer.ForceInk 把 root 下**所有**渲染器换成**同一个**水墨材质。
//   对 KayKit 骷髅无害（整只怪本来就是单一 skeleton 材质）。
//   但新素材是"每个部位一张贴图"（Orge 3 张、Orc 8 张）⇒ 全换成同一张会**丢细节**。
//
// 本脚本要回答三个问题：
//   1. Ziyuan prefab 里每个渲染器**原本**用什么材质、贴图叫什么
//   2. 这些渲染器**共用几张不同的贴图**（决定要不要做"多材质变体"）
//   3. 编辑态下 InkMaterialSwap 配置成什么样
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

var sb = new StringBuilder();
string root = Path.GetDirectoryName(Application.dataPath);

sb.AppendLine("========== Ziyuan prefab 的材质分布 ==========");
foreach (var zn in new[] { "Z_Orge", "Z_Orc", "Z_Undead", "Z_Dragon", "Z_Dragon_LP" })
{
    string p = "Assets/_Project/Prefabs/Ziyuan/" + zn + ".prefab";
    var content = PrefabUtility.LoadPrefabContents(p);
    sb.AppendLine();
    sb.AppendLine("--- " + zn + " ---");
    if (content == null) { sb.AppendLine("  ★ 读不到"); continue; }

    // 渲染器 → 材质 → 贴图
    var texSeen = new HashSet<string>();
    foreach (var r in content.GetComponentsInChildren<Renderer>(true))
    {
        var mats = r.sharedMaterials;
        var desc = new StringBuilder();
        for (int i = 0; i < mats.Length; i++)
        {
            var m = mats[i];
            if (m == null) { desc.Append("<空> "); continue; }
            desc.Append(m.name).Append('(').Append(m.shader != null ? m.shader.name : "?").Append(')');
            var tex = m.HasProperty("_BaseMap") ? m.GetTexture("_BaseMap") : null;
            if (tex != null)
            {
                desc.Append(" tex=").Append(tex.name);
                texSeen.Add(tex.name);
            }
            desc.Append(' ');
        }
        sb.AppendLine("  " + r.name + " → " + desc);
    }
    sb.AppendLine("  ** 该 prefab 用到的不同贴图数 = " + texSeen.Count);
    foreach (var t in texSeen) sb.AppendLine("      · " + t);

    // InkMaterialSwap 配置
    var sw = content.GetComponentInChildren<InkMaterialSwap>(true);
    sb.AppendLine("  InkMaterialSwap: " + (sw == null ? "(无)" : "存在"));
    if (sw != null)
    {
        int lit = sw.litMaterials != null ? sw.litMaterials.Length : 0;
        int ink = sw.inkMaterials != null ? sw.inkMaterials.Length : 0;
        sb.AppendLine("    litMaterials[" + lit + "] inkMaterials[" + ink + "]");
        for (int i = 0; sw.inkMaterials != null && i < sw.inkMaterials.Length; i++)
            sb.AppendLine("    ink[" + i + "] = " + (sw.inkMaterials[i] != null ? sw.inkMaterials[i].name : "null"));
    }

    PrefabUtility.UnloadPrefabContents(content);
}

sb.AppendLine();
sb.AppendLine("========== 现有 KayKit 敌人对照 ==========");
foreach (var n in new[] { "Enemy_MoOu", "Enemy_MoTu", "Enemy_MoYan" })
{
    string p = "Assets/_Project/Prefabs/Enemies/" + n + ".prefab";
    var content = PrefabUtility.LoadPrefabContents(p);
    sb.AppendLine();
    sb.AppendLine("--- " + n + " ---");
    var texSeen = new HashSet<string>();
    foreach (var r in content.GetComponentsInChildren<Renderer>(true))
    {
        var mats = r.sharedMaterials;
        var desc = new StringBuilder();
        for (int i = 0; i < mats.Length; i++)
        {
            var m = mats[i];
            if (m == null) { desc.Append("<空> "); continue; }
            desc.Append(m.name).Append(' ');
            var tex = m.HasProperty("_BaseMap") ? m.GetTexture("_BaseMap") : null;
            if (tex != null) { desc.Append("tex=").Append(tex.name).Append(' '); texSeen.Add(tex.name); }
        }
        sb.AppendLine("  " + r.name + " → " + desc);
    }
    sb.AppendLine("  ** 不同贴图数 = " + texSeen.Count);
    var sw2 = content.GetComponentInChildren<InkMaterialSwap>(true);
    if (sw2 != null)
        sb.AppendLine("  InkMaterialSwap ink[" + (sw2.inkMaterials != null ? sw2.inkMaterials.Length : 0)
                      + "] lit[" + (sw2.litMaterials != null ? sw2.litMaterials.Length : 0) + "]");
    PrefabUtility.UnloadPrefabContents(content);
}

sb.AppendLine();
sb.AppendLine("========== 已建的水墨敌人材质 ==========");
foreach (var guid in AssetDatabase.FindAssets("t:Material M_Ink_Enemy"))
{
    var ap = AssetDatabase.GUIDToAssetPath(guid);
    var m = AssetDatabase.LoadAssetAtPath<Material>(ap);
    var tex = m.HasProperty("_BaseMap") ? m.GetTexture("_BaseMap") : null;
    sb.AppendLine("  " + m.name + "  shader=" + (m.shader != null ? m.shader.name : "?")
                  + "  _BaseMap=" + (tex != null ? tex.name : "<未设>"));
}
foreach (var guid in AssetDatabase.FindAssets("t:Material M_Ink_Boss"))
{
    var ap = AssetDatabase.GUIDToAssetPath(guid);
    var m = AssetDatabase.LoadAssetAtPath<Material>(ap);
    var tex = m.HasProperty("_BaseMap") ? m.GetTexture("_BaseMap") : null;
    sb.AppendLine("  " + m.name + "  shader=" + (m.shader != null ? m.shader.name : "?")
                  + "  _BaseMap=" + (tex != null ? tex.name : "<未设>"));
}

File.WriteAllText(Path.Combine(root, "Tools/reports/a2_matcheck.txt"), sb.ToString());
Debug.Log("[a2] 完成 " + sb.Length + " 字符");
