// a50_swap_audit.cs —— 审计所有 prefab 的 InkMaterialSwap 槽位配置
//
// 背景：a49 跑 Play 时刷出一条警告
//   [InkStylePanel] Model 换材质配置有问题：槽位数不符：lit=0 ink=1（会错位）
// 栈指向 PrefabUtility.InstantiatePrefab ⇒ 是某个 prefab 上的 InkMaterialSwap 配错了。
// lit=0 且 ink=1 ⇒ Validate 判不合格 ⇒ 面板回退到"不改材质"，
// 于是那个渲染器**在水墨模式下仍然穿原 PBR 材质** —— 静默的视觉 bug。
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using InkWash.UI;

var root = Path.GetDirectoryName(Application.dataPath);
var sb = new StringBuilder();
sb.AppendLine("========== a50 InkMaterialSwap 槽位审计 ==========");
sb.AppendLine();

var guids = AssetDatabase.FindAssets("t:GameObject", new[] { "Assets/_Project/Prefabs" });
int nPrefab = 0, nSwap = 0, nBad = 0;
foreach (var g in guids)
{
    var path = AssetDatabase.GUIDToAssetPath(g);
    var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
    if (go == null) continue;
    nPrefab++;

    foreach (var sw in go.GetComponentsInChildren<InkMaterialSwap>(true))
    {
        nSwap++;
        string problem;
        bool ok = sw.Validate(out problem);
        string lit = sw.litMaterials == null ? "null" : sw.litMaterials.Length.ToString();
        string ink = sw.inkMaterials == null ? "null" : sw.inkMaterials.Length.ToString();
        string line = "  " + Path.GetFileName(path) + "  /  " + Rel(sw.transform, go.transform)
                      + "   lit=" + lit + " ink=" + ink;
        if (!ok)
        {
            line += "   ★ " + problem;
            nBad++;
        }
        sb.AppendLine(line);

        if (sw.target == null) sb.AppendLine("       ★ target 为空（会在 OnEnable 取第一个 Renderer）");
        else sb.AppendLine("       target = " + Rel(sw.target.transform, go.transform)
                           + "  槽数 = " + (sw.target.sharedMaterials != null ? sw.target.sharedMaterials.Length : 0));
    }
}
sb.AppendLine();
sb.AppendLine("prefab 总数 = " + nPrefab + "   InkMaterialSwap 总数 = " + nSwap + "   ★ 不合格 = " + nBad);
sb.AppendLine();

// 场景里的也查一遍
sb.AppendLine("---- 场景内（Main） ----");
var sceneGo = UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects();
int nSceneSwap = 0;
foreach (var r in sceneGo)
    foreach (var sw in r.GetComponentsInChildren<InkMaterialSwap>(true))
    {
        nSceneSwap++;
        string problem;
        bool ok = sw.Validate(out problem);
        sb.AppendLine("  " + sw.name + "  lit=" + (sw.litMaterials == null ? "null" : sw.litMaterials.Length.ToString())
                      + " ink=" + (sw.inkMaterials == null ? "null" : sw.inkMaterials.Length.ToString())
                      + (ok ? "  ✓" : "  ★ " + problem));
    }
sb.AppendLine("  共 " + nSceneSwap + " 个");

string Rel(Transform t, Transform rootT)
{
    var s = t.name; var p = t.parent; int g2 = 0;
    while (p != null && g2++ < 10) { s = p.name + "/" + s; if (p == rootT) break; p = p.parent; }
    return s;
}

File.WriteAllText(Path.Combine(root, "Tools/reports/a50_swap_audit.txt"), sb.ToString());
Debug.Log("[a50]\n" + sb.ToString());
