// f_mats.cs —— 增量 F 的前置诊断：敌人身上到底挂了什么材质
//
// 为什么要先查：debug 截图里骷髅是"亮蓝灰骨头 + 橙斗篷 + 高光"，明显不是 M_Ink_Enemy_*。
// 而 `InkStylePanel` 的换材质是**按条目手工配的**（InkMaterialSwap），
// 只要有一个渲染器没被配进去，它就会保持 KayKit 原材质 —— 那正是画面上唯一的高饱和色。
// 本项目硬规矩：别靠名字猜资产，先把真实引用列出来。
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using InkWash.Enemies;
using InkWash.Player;
using InkWash.UI;

IEnumerator Body()
{
    string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
    string repPath = Path.Combine(root, "Tools/reports/f_mats.txt");
    Directory.CreateDirectory(Path.GetDirectoryName(repPath));

    var spawner = Object.FindObjectOfType<WaveSpawner>();
    if (spawner != null) { spawner.ResetForTest(); spawner.spawnInterval = 0.4f; }
    var run = Object.FindObjectOfType<RunManager>();
    if (run != null) { run.ResetForTest(); run.SetSeed(20260916); run.nextRoomDelay = 999f; }
    if (run != null) run.StartRun();
    { float t = Time.unscaledTime; while (Time.unscaledTime - t < 5.0f) yield return null; }

    var sb = new StringBuilder();
    sb.AppendLine("========== f_mats 材质引用诊断 ==========");
    sb.AppendLine();

    // ---- 1) 玩家 ----
    var ph = Object.FindObjectOfType<PlayerHealth>();
    if (ph != null)
    {
        sb.AppendLine("---- 玩家 " + ph.gameObject.name + " ----");
        DumpRenderers(sb, ph.gameObject, "  ");
    }

    // ---- 2) 敌人 ----
    var enemies = Object.FindObjectsOfType<EnemyBase>();
    sb.AppendLine("---- 敌人 x" + enemies.Length + " ----");
    foreach (var e in enemies)
    {
        sb.AppendLine("  [" + e.name + "]");
        DumpRenderers(sb, e.gameObject, "    ");
    }

    // ---- 3) 场景内所有"非水墨"渲染器（管线之外的东西）----
    sb.AppendLine();
    sb.AppendLine("---- 全场景非水墨 Shader 的渲染器 ----");
    var bad = new Dictionary<string, int>();
    var badSample = new Dictionary<string, string>();
    foreach (var r in Object.FindObjectsOfType<Renderer>())
    {
        var m = r.sharedMaterial;
        string sh = m == null ? "<无材质>" : m.shader.name;
        if (sh.StartsWith("InkWash/") || sh.StartsWith("Hidden/InkWash/")) continue;
        if (sh.Contains("Universal Render Pipeline/Unlit") || sh.Contains("Universal Render Pipeline/Lit"))
        {
            // URP 内置可能是 VFX/特效，单独归类
        }
        string full = sh + "  ←  材质「" + (m == null ? "<无>" : m.name) + "」";
        if (!bad.ContainsKey(full)) { bad[full] = 0; badSample[full] = PathOf(r); }
        bad[full]++;
    }
    foreach (var kv in bad)
        sb.AppendLine("  x" + kv.Value.ToString().PadRight(3) + kv.Key + "\n        例：" + badSample[kv.Key]);

    // ---- 4) 换材质条目（InkMaterialSwap） ----
    sb.AppendLine();
    sb.AppendLine("---- InkMaterialSwap 条目 ----");
    var swaps = Object.FindObjectsOfType<InkMaterialSwap>(true);
    sb.AppendLine("  共 " + swaps.Length + " 个");
    foreach (var sw in swaps)
        sb.AppendLine("  · " + sw.name + "  target=" + (sw.target == null ? "<空>" : sw.target.name)
                      + "  lit=" + (sw.litMaterials == null ? 0 : sw.litMaterials.Length)
                      + "  ink=" + (sw.inkMaterials == null ? 0 : sw.inkMaterials.Length));

    string report = sb.ToString();
    File.WriteAllText(repPath, report, new UTF8Encoding(false));
    Debug.Log("[f_mats] 报告已落盘 " + repPath);
    Debug.Log(report);
    yield return null;
}

string PathOf(Renderer r)
{
    string p = r.name;
    var t = r.transform.parent;
    int guard = 0;
    while (t != null && guard++ < 6) { p = t.name + "/" + p; t = t.parent; }
    return p;
}

void DumpRenderers(StringBuilder sb, GameObject go, string pad)
{
    foreach (var r in go.GetComponentsInChildren<Renderer>(true))
    {
        var mats = r.sharedMaterials;
        string list = "";
        for (int i = 0; i < mats.Length; i++)
            list += (i > 0 ? " | " : "") + (mats[i] == null ? "<空>" : mats[i].name);
        string sh = (mats.Length > 0 && mats[0] != null) ? mats[0].shader.name : "<无着色器>";
        sb.AppendLine(pad + r.GetType().Name + " " + r.name
                      + "  active=" + r.gameObject.activeInHierarchy
                      + "\n" + pad + "   材质: " + list
                      + "\n" + pad + "   Shader: " + sh);
    }
}

return Body();
