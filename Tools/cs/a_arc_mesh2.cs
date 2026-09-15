// a_arc_mesh2.cs —— 弧光锯齿：绕序 / 分带 二分（运行时）
//
// 上游两个探针的结论：
//   a_arc_diag.cs  关掉全部后处理，锯齿仍在  ⇒ 与墨线/宣纸/墨晕无关
//   a_arc_mesh.cs  ① 顶点色统一成不透明白，锯齿**更明显**（⇒ 与顶点色浓淡无关）
//                  ② 段数 28→200，齿数跟着变成 199 个（⇒ 齿 = 网格的四边形段）
//                  ③ 实测每个四边形的两个三角形**绕序相反**（有符号面积 -0.0042 / +0.0060 交替）
//
// 归纳出的假设：每个四边形只画出了一半三角形 —— 即发生了**背面剔除**，
//   于是剩下的一半三角形彼此只共用一个顶点、互不相连 ⇒ 屏幕上是一把**梳子**。
//   这也解释了为什么"统一成不透明白反而更明显"：不透明时被剔掉的洞直接露出背景。
//
// 本探针用两个正交变量把假设钉死：
//   · 绕序统一为逆时针（全部正面积） → 若锯齿消失 ⇒ 病因就是绕序
//   · 只留内带 / 只留外带          → 看锯齿属于哪一带（决定改哪一段）
//
// 注意：InkSlash.shader 的 Pass 里写的是 `Cull Off`。若绕序确实影响画面，
//   说明 `Cull Off` 并没有真正生效 —— 这本身就是一条要写进技能的环境结论。
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;
using InkWash.Effects;
using InkWash.Player;
using InkWash.Rendering;
using InkWash.Roguelike;
using InkWash.UI;

IEnumerator Body()
{
    var sb = new StringBuilder();
    string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../Tools/screenshots/arcmesh2"));
    Directory.CreateDirectory(dir);

    var ph = Object.FindObjectOfType<PlayerHealth>();
    if (ph == null) { Debug.LogError("[a_arc_mesh2] 找不到 PlayerHealth"); yield break; }
    var go = ph.gameObject;
    var ctl = go.GetComponent<PlayerController>();
    var stance = go.GetComponentInChildren<CombatStance>(true);
    var panel = Object.FindObjectOfType<InkStylePanel>();
    var run = Object.FindObjectOfType<RunManager>();
    var choice = Object.FindObjectOfType<SkillChoicePanel>();
    var spawner = Object.FindObjectOfType<WaveSpawner>();

    if (panel != null) { panel.visible = false; panel.enabled = false; }
    if (choice != null) choice.Hide();

    ph.maxHealth = 100000f; ph.ResetHealth();
    if (spawner != null) { spawner.ResetForTest(); spawner.spawnInterval = 9999f; }
    if (run != null) { run.ResetForTest(); run.SetSeed(20260916); run.nextRoomDelay = 999f; }

    ctl.ResetToLocomotion();
    if (stance != null) { stance.combatExitDelay = 99999f; stance.ForceStance(true); }
    ctl.BeginInputOverride();
    ctl.SetInjectedMove(Vector2.zero, true);
    if (run != null) run.StartRun();

    { float t = Time.unscaledTime; while (Time.unscaledTime - t < 1.5f) yield return null; }

    var names = new string[] { "A_orig28", "E_28_ccw", "F_28_innerband", "G_28_outerband" };
    var meshes = new Mesh[names.Length];

    sb.AppendLine("===== 弧光锯齿：绕序 / 分带 =====");

    for (int c = 0; c < names.Length; c++)
    {
        ctl.ResetToLocomotion();
        { float t = Time.unscaledTime; while (Time.unscaledTime - t < 0.45f) yield return null; }

        ctl.RequestInjectedAttack();

        float t0 = Time.unscaledTime;
        GameObject arc = null;
        while (arc == null && Time.unscaledTime - t0 < 1.2f)
        {
            arc = GameObject.Find("SlashArc");
            yield return null;
        }
        if (arc == null) { sb.AppendLine(names[c] + "  ** 没等到 SlashArc"); continue; }

        var mf = arc.GetComponent<MeshFilter>();
        Mesh orig = mf != null ? mf.sharedMesh : null;
        if (orig == null) { sb.AppendLine(names[c] + "  ** 没有网格"); continue; }

        if (c > 0)
        {
            if (meshes[c] == null) meshes[c] = Variant(orig, c);
            mf.sharedMesh = meshes[c];
        }

        for (int k = 0; k < 3; k++) yield return null;

        ScreenCapture.CaptureScreenshot(Path.Combine(dir, names[c] + ".png"));

        var m = mf.sharedMesh;
        var tv = m.vertices; var tt = m.triangles;
        int pos = 0, neg = 0;
        for (int k = 0; k < tt.Length / 3; k++)
        {
            Vector3 p0 = tv[tt[k * 3]], p1 = tv[tt[k * 3 + 1]], p2 = tv[tt[k * 3 + 2]];
            float s = (p1.x - p0.x) * (p2.y - p0.y) - (p2.x - p0.x) * (p1.y - p0.y);
            if (s > 0f) pos++; else if (s < 0f) neg++;
        }
        sb.AppendLine(names[c] + " → 抓图； tris=" + tt.Length / 3
                      + "  绕序分布 逆时针=" + pos + " 顺时针=" + neg
                      + "  name=" + m.name);

        while (arc != null && Time.unscaledTime - t0 < 2.5f) { arc = GameObject.Find("SlashArc"); yield return null; }
        { float t = Time.unscaledTime; while (Time.unscaledTime - t < 0.5f) yield return null; }
    }

    ctl.SetInjectedMove(Vector2.zero, true);
    ctl.EndInputOverride();
    if (stance != null) stance.combatExitDelay = 6f;

    for (int i = 1; i < meshes.Length; i++) if (meshes[i] != null) Object.Destroy(meshes[i]);

    File.WriteAllText(Path.Combine(Application.dataPath, "../Tools/reports/a_arc_mesh2.txt"), sb.ToString());
    Debug.Log("[a_arc_mesh2] done");
    yield return null;
}

/// <summary>
/// 用原网格的顶点/顶点色重建索引：
///   1 = 全部绕序统一为逆时针
///   2 = 只留内带（内缘→中脊）
///   3 = 只留外带（中脊→外缘）
/// 顶点与顶点色**原样搬运**，只改三角形，保证唯一变量是"哪些三角形、朝哪边"。
/// </summary>
static Mesh Variant(Mesh src, int kind)
{
    var v = src.vertices;
    var col = src.colors;
    int n = v.Length / 3;
    var list = new System.Collections.Generic.List<int>();
    var idx = new int[(n - 1) * 12];

    for (int i = 0; i < n - 1; i++)
    {
        int o = i * 12, a0 = i * 3, a1 = (i + 1) * 3;

        // 原实现的第一对（内带）：T1 顺时针、T2 逆时针 —— 这里统一成逆时针
        //   T1 = (inner_i, mid_i, inner_{i+1})       → 反成 (inner_i, inner_{i+1}, mid_i)
        //   T2 = (inner_i, inner_{i+1}, mid_{i+1})   → 本来就是逆时针
        idx[o + 0] = a0 + 0; idx[o + 1] = a1 + 0; idx[o + 2] = a0 + 1;
        idx[o + 3] = a0 + 0; idx[o + 4] = a1 + 0; idx[o + 5] = a1 + 1;

        // 第二对（外带）：T3 顺时针、T4 逆时针
        //   T3 = (mid_i, outer_i, mid_{i+1})         → 反成 (mid_i, mid_{i+1}, outer_i)
        //   T4 = (mid_i, mid_{i+1}, outer_{i+1})     → 本来就是逆时针
        idx[o + 6] = a0 + 1; idx[o + 7] = a1 + 1; idx[o + 8] = a0 + 2;
        idx[o + 9] = a0 + 1; idx[o + 10] = a1 + 1; idx[o + 11] = a1 + 2;

        if (kind == 2) { list.Add(idx[o + 0]); list.Add(idx[o + 1]); list.Add(idx[o + 2]);
                         list.Add(idx[o + 3]); list.Add(idx[o + 4]); list.Add(idx[o + 5]); }
        else if (kind == 3) { list.Add(idx[o + 6]); list.Add(idx[o + 7]); list.Add(idx[o + 8]);
                              list.Add(idx[o + 9]); list.Add(idx[o + 10]); list.Add(idx[o + 11]); }
        else { for (int k = 0; k < 12; k++) list.Add(idx[o + k]); }
    }

    var m = new Mesh { name = "Probe2_" + kind };
    m.vertices = v;
    m.colors = col;
    m.triangles = list.ToArray();
    m.RecalculateBounds();
    return m;
}

return Body();
