// a_arc_geom.cs —— 把运行时弧面网格的**真实顶点/三角形/顶点色**导出成文本，供离线作图
//
// 为什么不再靠渲染图反推：连续四轮都在"看渲染图猜几何"，而渲染图里同时叠加了
//   顶点色插值 × alpha 混合 × 角色遮挡 × 相机透视 —— 四种因素叠在一起，
//   任何单张图都能被解释成两种互斥的病因（这几轮已经在这上面绕了两次弯）。
//   直接导出网格数字，把"几何到底长什么样"变成可离线核对的事实。
//
// 输出：Tools/reports/arc_geom.txt
//   段数 / 三角形数 / 顶点数
//   每个顶点：index x y z  alpha
//   每个三角形：三个顶点索引 + 有符号面积（判绕序）
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

    var ph = Object.FindObjectOfType<PlayerHealth>();
    if (ph == null) { Debug.LogError("[a_arc_geom] 找不到 PlayerHealth"); yield break; }
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

    ctl.RequestInjectedAttack();
    float t0 = Time.unscaledTime;
    GameObject arc = null;
    while (arc == null && Time.unscaledTime - t0 < 1.2f)
    {
        arc = GameObject.Find("SlashArc");
        yield return null;
    }
    if (arc == null) { Debug.LogError("[a_arc_geom] 没等到 SlashArc"); yield break; }

    var mf = arc.GetComponent<MeshFilter>();
    Mesh m = mf.sharedMesh;
    var v = m.vertices;
    var c = m.colors;
    var tr = m.triangles;
    int n = v.Length / 3;

    sb.AppendLine("# 弧面网格导出 verts=" + v.Length + " tris=" + tr.Length / 3 + " 段数=" + n);
    sb.AppendLine("V " + v.Length + " " + tr.Length / 3 + " " + n);
    for (int i = 0; i < v.Length; i++)
        sb.AppendLine("v " + i + " " + v[i].x.ToString("R") + " " + v[i].y.ToString("R")
                      + " " + v[i].z.ToString("R") + " " + c[i].a.ToString("R"));
    for (int k = 0; k < tr.Length / 3; k++)
    {
        int i0 = tr[k * 3], i1 = tr[k * 3 + 1], i2 = tr[k * 3 + 2];
        Vector3 p0 = v[i0], p1 = v[i1], p2 = v[i2];
        float s = (p1.x - p0.x) * (p2.y - p0.y) - (p2.x - p0.x) * (p1.y - p0.y);
        sb.AppendLine("t " + k + " " + i0 + " " + i1 + " " + i2 + " " + s.ToString("R"));
    }

    ctl.SetInjectedMove(Vector2.zero, true);
    ctl.EndInputOverride();
    if (stance != null) stance.combatExitDelay = 6f;

    File.WriteAllText(Path.Combine(Application.dataPath, "../Tools/reports/arc_geom.txt"), sb.ToString());
    Debug.Log("[a_arc_geom] done verts=" + v.Length + " tris=" + tr.Length / 3);
    yield return null;
}

return Body();
