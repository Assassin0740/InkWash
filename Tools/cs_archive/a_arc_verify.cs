// a_arc_verify.cs —— 弧面三角剖分修复后的**真实观感**验收（运行时）
//
// 修的是 SwordVfx.BuildArcMesh 的三角剖分（每个四边形原来共享的是"同一环上相邻两点的连线"，
// 那是一条边界边而不是对角线 ⇒ 两个三角形重叠成蝴蝶结、四边形里留三角形空洞 ⇒ 屏幕上一把等距梳子）。
//
// 本探针只做一件事：**在所有后处理默认开启、角色正常显示**的真实条件下，
// 沿弧光寿命取 3 个时刻各抓一张，看它是不是"一笔连续的月牙墨"。
// 刻意不关任何层 —— 修复前那几张"关掉后处理"的对照图就是为了排除后处理，
// 现在结论已经落地，验收就该用玩家真正看到的配置。
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
    string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../Tools/screenshots/arcverify"));
    Directory.CreateDirectory(dir);

    var ph = Object.FindObjectOfType<PlayerHealth>();
    if (ph == null) { Debug.LogError("[a_arc_verify] 找不到 PlayerHealth"); yield break; }
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

    sb.AppendLine("===== 弧光三角剖分修复后验收（全后处理开启、角色可见）=====");
    sb.AppendLine("三层状态 = " + InkStyleRegistry.Describe());

    // 三段连击各抓一次，同时覆盖"单段/连段"两种走位
    for (int step = 1; step <= 2; step++)
    {
        ctl.ResetToLocomotion();
        { float t = Time.unscaledTime; while (Time.unscaledTime - t < 0.5f) yield return null; }

        ctl.RequestInjectedAttack();

        float t0 = Time.unscaledTime;
        GameObject arc = null;
        while (arc == null && Time.unscaledTime - t0 < 1.2f)
        {
            arc = GameObject.Find("SlashArc");
            yield return null;
        }
        if (arc == null) { sb.AppendLine("第 " + step + " 刀：** 没等到 SlashArc"); continue; }

        var mf = arc.GetComponent<MeshFilter>();
        var mr = arc.GetComponent<MeshRenderer>();
        var mesh = mf != null ? mf.sharedMesh : null;
        sb.AppendLine();
        sb.AppendLine("第 " + step + " 刀：");
        if (mesh != null)
        {
            var tv = mesh.vertices; var tt = mesh.triangles;
            int pos = 0, neg = 0;
            for (int k = 0; k < tt.Length / 3; k++)
            {
                Vector3 p0 = tv[tt[k * 3]], p1 = tv[tt[k * 3 + 1]], p2 = tv[tt[k * 3 + 2]];
                float s = (p1.x - p0.x) * (p2.y - p0.y) - (p2.x - p0.x) * (p1.y - p0.y);
                if (s > 0f) pos++; else if (s < 0f) neg++;
            }
            sb.AppendLine("  网格 verts=" + tv.Length + " tris=" + tt.Length / 3
                          + " 绕序 逆时针=" + pos + " 顺时针=" + neg);
        }
        if (mr != null && mr.material != null)
            sb.AppendLine("  材质 " + mr.material.shader.name
                          + " _BaseColor.a=" + (mr.material.HasProperty("_BaseColor")
                              ? mr.material.GetColor("_BaseColor").a.ToString("0.###") : "?"));

        // 三个时刻：刚出现 / 中段最浓 / 临近消散
        string[] tags = new string[] { "t0", "t1", "t2" };
        int[] wait = new int[] { 1, 4, 7 };
        int acc = 0;
        for (int k = 0; k < 3; k++)
        {
            while (acc < wait[k]) { acc++; yield return null; }
            ScreenCapture.CaptureScreenshot(Path.Combine(dir, "arc" + step + "_" + tags[k] + ".png"));
        }
        sb.AppendLine("  已抓 arc" + step + "_t0/t1/t2.png");

        while (arc != null && Time.unscaledTime - t0 < 2.5f) { arc = GameObject.Find("SlashArc"); yield return null; }
        { float t = Time.unscaledTime; while (Time.unscaledTime - t < 0.5f) yield return null; }
    }

    ctl.SetInjectedMove(Vector2.zero, true);
    ctl.EndInputOverride();
    if (stance != null) stance.combatExitDelay = 6f;

    File.WriteAllText(Path.Combine(Application.dataPath, "../Tools/reports/a_arc_verify.txt"), sb.ToString());
    Debug.Log("[a_arc_verify] done");
    yield return null;
}

return Body();
