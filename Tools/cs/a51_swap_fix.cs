// a51_swap_fix.cs —— 修 InkMaterialSwap 的 10 处配置缺陷
//
// a50 审计结果：13 个 InkMaterialSwap 里 10 个不合格，分两类：
//
//   ① lit=0 ink=1（6 个：Z_Dragon / Z_Dragon_LP / Z_Orc / Z_Orge / Z_Undead / Z_Enemy_MoLong/Visual/Model）
//      `litMaterials` 是空数组 ⇒ Validate 判 false ⇒ 面板**放弃换材质**
//      ⇒ stage 0（白模）切回去时，这些渲染器还原不成 PBR。
//      修法：把渲染器**当前的** sharedMaterials 快照填进 litMaterials。
//
//   ② target 为空（7 个）
//      靠 OnEnable 的 `GetComponentInChildren<Renderer>()` 兜底 ⇒ 取到任意第一个渲染器。
//      修法：显式填 target。
//
// ★ 注意：Z_Enemy_MoLong 上有**两个** InkMaterialSwap —— 一个在根（管整体），一个在
//   Visual/Model（管龙的 SMR）。第二个是 a42 建 prefab 时从 Z_Dragon 一起带过来的，
//   它是重复的（根上那个已经覆盖了）。这里**保留**它但把 lit 补齐，因为它是 Z_Dragon
//   实例自带的、以后单独用 Z_Dragon 时还要靠它。
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using InkWash.UI;

var root = Path.GetDirectoryName(Application.dataPath);
var sb = new StringBuilder();
sb.AppendLine("========== a51 修 InkMaterialSwap 配置 ==========");
sb.AppendLine();

var guids = AssetDatabase.FindAssets("t:GameObject", new[] { "Assets/_Project/Prefabs" });
var touched = new List<string>();
int nFixLit = 0, nFixTarget = 0;

foreach (var g in guids)
{
    var path = AssetDatabase.GUIDToAssetPath(g);
    if (path.IndexOf("Ziyuan/") < 0 && path.IndexOf("Enemies/") < 0) continue;

    var contents = PrefabUtility.LoadPrefabContents(path);
    if (contents == null) continue;
    bool dirty = false;

    foreach (var sw in contents.GetComponentsInChildren<InkMaterialSwap>(true))
    {
        // ---- ② target 为空 → 显式填 ----
        if (sw.target == null)
        {
            var r = sw.GetComponent<Renderer>();
            if (r == null) r = sw.GetComponentInChildren<Renderer>();
            if (r != null)
            {
                sw.target = r;
                sb.AppendLine("[" + Path.GetFileName(path) + "] " + sw.name
                              + "：填 target = " + r.name);
                nFixTarget++;
                dirty = true;
            }
        }

        // ---- ① lit 为空 → 用渲染器当前材质快照补齐 ----
        if (sw.target != null && (sw.litMaterials == null || sw.litMaterials.Length == 0)
            && sw.inkMaterials != null && sw.inkMaterials.Length > 0)
        {
            var cur = sw.target.sharedMaterials;
            if (cur != null && cur.Length == sw.inkMaterials.Length)
            {
                sw.litMaterials = cur;
                sb.AppendLine("[" + Path.GetFileName(path) + "] " + sw.name
                              + "：填 litMaterials = [" + Names(cur) + "]  (ink 对应 [" + Names(sw.inkMaterials) + "])");
                nFixLit++;
                dirty = true;
            }
            else
            {
                sb.AppendLine("[" + Path.GetFileName(path) + "] " + sw.name
                              + "：★ 槽数仍不符：渲染器=" + (cur == null ? "null" : cur.Length.ToString())
                              + " ink=" + sw.inkMaterials.Length + " —— 需人工判断");
            }
        }
    }

    if (dirty)
    {
        PrefabUtility.SaveAsPrefabAsset(contents, path);
        touched.Add(Path.GetFileName(path));
    }
    PrefabUtility.UnloadPrefabContents(contents);
}

sb.AppendLine();
sb.AppendLine("修 target = " + nFixTarget + " 处");
sb.AppendLine("修 litMaterials = " + nFixLit + " 处");
sb.AppendLine("改动 prefab = " + touched.Count + "：" + string.Join(", ", touched));

string Names(Material[] ms)
{
    var l = new List<string>();
    foreach (var m in ms) l.Add(m != null ? m.name : "null");
    return string.Join(",", l);
}

AssetDatabase.SaveAssets();
AssetDatabase.Refresh();
File.WriteAllText(Path.Combine(root, "Tools/reports/a51_swap_fix.txt"), sb.ToString());
Debug.Log("[a51]\n" + sb.ToString());
