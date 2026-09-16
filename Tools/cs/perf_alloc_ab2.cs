// perf_alloc_ab2.cs —— 先证度量本身可靠，再谈优化（对账实验）
//
// 触发原因：perf_baseline 量"静置"得 5.96 KB/帧，perf_alloc_ab 量同一个静置得 12.99 KB/帧
// —— **同一个状态两次量差 2.2 倍**，那么 A/B 归因表（只解释出 18%）就不可信。
// 本项目硬规矩：只差一项未通过时，先怀疑度量本身，做对照实验。
//
// 两个待验证的混杂因子：
//   (a) **会话漂移**：Play 会话跑得越久，"每帧分配"读数是否越大？（基线里三个档
//       5.96 → 6.25 → 9.38 是单调上升的，当时归因于"战斗"，但可能只是时间效应）
//   (b) **RunManager 的主菜单 OnGUI**：perf_alloc_ab 全程没调 StartRun ⇒ 状态停在
//       MainMenu ⇒ OnGUI 每帧 `new GUIStyle` ×2 + `new Rect` ×3。而 perf_baseline
//       调过 StartRun，OnGUI 直接不画。这正好是两个探针之间唯一的显式状态差异。
//
// ★ 设计要点：**同一档重复测两次**。没有重复就没法把"档间差"和"运行噪声"分开 ——
//   这是上一版归因表最大的方法论缺陷（V1 与 V3 的差额到小数点后三位完全相同，
//   这本身就说明分辨率不够）。
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Profiling;
using Unity.Profiling;
using InkWash.Enemies;
using InkWash.Player;
using InkWash.Roguelike;
using InkWash.UI;

string gRoot3 = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
string gRep3 = Path.Combine(gRoot3, "Tools/reports/perf_alloc_ab2.txt");
StringBuilder gSb3 = new StringBuilder();

string F4(float v) { return v.ToString("0.##"); }

float Pct3(List<float> v, float p)
{
    if (v == null || v.Count == 0) return 0f;
    var c = new List<float>(v);
    c.Sort();
    return c[Mathf.Clamp((int)(c.Count * p), 0, c.Count - 1)];
}

ProfilerRecorder TryRec3(ProfilerCategory cat, string[] names, out string bound)
{
    bound = "(未绑定)";
    for (int i = 0; i < names.Length; i++)
    {
        try
        {
            var r = ProfilerRecorder.StartNew(cat, names[i], 1);
            if (r.Valid) { bound = names[i]; return r; }
            r.Dispose();
        }
        catch (System.Exception) { }
    }
    return default(ProfilerRecorder);
}

IEnumerator Body3()
{
    gSb3.AppendLine("========== perf_alloc_ab2 度量可靠性对账 ==========");
    gSb3.AppendLine("生成时间 " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
    gSb3.AppendLine("场景 " + UnityEngine.SceneManagement.SceneManager.GetActiveScene().name
                   + "　分辨率 " + Screen.width + "x" + Screen.height + "　vSync " + QualitySettings.vSyncCount);

    var cam = Camera.main;
    var ph = Object.FindObjectOfType<PlayerHealth>();
    if (cam == null || ph == null)
    {
        gSb3.AppendLine("★ 失败：找不到 MainCamera 或 PlayerHealth ⇒ 需在 Main 场景的 Play 中运行");
        File.WriteAllText(gRep3, gSb3.ToString(), new UTF8Encoding(false));
        yield break;
    }

    var player = ph.gameObject;
    var ctl = player.GetComponent<PlayerController>();
    var run = Object.FindObjectOfType<RunManager>();
    var panel = Object.FindObjectOfType<InkStylePanel>();
    var choice = Object.FindObjectOfType<SkillChoicePanel>();
    var spawner = Object.FindObjectOfType<WaveSpawner>();
    var room = Object.FindObjectOfType<RoomController>();

    ph.maxHealth = 100000f; ph.ResetHealth();
    if (panel != null) panel.visible = false;
    if (choice != null) choice.Hide();
    if (room != null) room.ResetForTest();
    if (run != null) { run.ResetForTest(); run.SetSeed(20260916); run.nextRoomDelay = 999f; }
    if (ctl != null) { ctl.ResetToLocomotion(); ctl.BeginInputOverride(); ctl.SetInjectedMove(Vector2.zero, false); }

    foreach (var e in Object.FindObjectsOfType<EnemyBase>())
        if (e != null) Object.Destroy(e.gameObject);
    if (spawner != null) spawner.enabled = false;
    {
        float t = Time.unscaledTime;
        while (Time.unscaledTime - t < 1.5f) yield return null;
    }

    string bGc, bDraw, bTris;
    var recGc = TryRec3(ProfilerCategory.Memory, new[] { "GC.Alloc" }, out bGc);
    var recDraw = TryRec3(ProfilerCategory.Render, new[] { "Draw Calls Count" }, out bDraw);
    var recTris = TryRec3(ProfilerCategory.Render, new[] { "Triangles Count" }, out bTris);
    gSb3.AppendLine("计数器　GC.Alloc=" + bGc + "　Draw=" + bDraw + "　Tri=" + bTris);
    gSb3.AppendLine("RunManager 状态 = " + (run != null ? run.enabled.ToString() : "无")
                   + "（主菜单是否在画由内部 _state 决定，探针只能用 StartRun 切换）");
    gSb3.AppendLine();

    var labels = new List<string>();
    var meds = new List<float>();
    var p95s = new List<float>();
    var totals = new List<float>();
    var draws = new List<float>();

    // ---- 第 1 组：主菜单状态（不调 StartRun），RunManager 开 → 关 → 关 → 开 ----
    yield return Window3("G1a 主菜单·RunManager 开", 3f, run, null, recGc, recDraw,
                         labels, meds, p95s, totals, draws);
    yield return Window3("G1b 主菜单·RunManager 开（重复）", 3f, run, null, recGc, recDraw,
                         labels, meds, p95s, totals, draws);
    yield return Window3("G1c 主菜单·RunManager **关**", 3f, run, false, recGc, recDraw,
                         labels, meds, p95s, totals, draws);
    yield return Window3("G1d 主菜单·RunManager **关**（重复）", 3f, run, false, recGc, recDraw,
                         labels, meds, p95s, totals, draws);

    // ---- 第 2 组：进局（StartRun）后同条件，用于和 perf_baseline 的"静置档"对账 ----
    if (run != null) { run.enabled = true; run.StartRun(); }
    {
        float t = Time.unscaledTime;
        while (Time.unscaledTime - t < 2.0f) yield return null;
    }
    yield return Window3("G2a 已进局·RunManager 开", 3f, run, null, recGc, recDraw,
                         labels, meds, p95s, totals, draws);
    yield return Window3("G2b 已进局·RunManager 开（重复）", 3f, run, null, recGc, recDraw,
                         labels, meds, p95s, totals, draws);

    // ---- 第 3 组：会话漂移 —— 同条件再测一轮，看有没有随时间上升的趋势 ----
    yield return Window3("G3a 已进局·第三次（漂移检查）", 3f, run, null, recGc, recDraw,
                         labels, meds, p95s, totals, draws);

    gSb3.AppendLine();
    gSb3.AppendLine("---------- 汇总 ----------");
    gSb3.AppendLine("  档位　　　　　　　　　　　　　　　　　　　　中位 KB/帧　 95% KB/帧　合计 MB　draw");
    for (int i = 0; i < labels.Count; i++)
        gSb3.AppendLine("  " + labels[i].PadRight(38)
                       + F4(meds[i] / 1024f).PadLeft(10)
                       + F4(p95s[i] / 1024f).PadLeft(12)
                       + F4(totals[i] / 1048576f).PadLeft(10)
                       + draws[i].ToString().PadLeft(6));

    // ---- 判定 ----
    gSb3.AppendLine();
    gSb3.AppendLine("---------- 判定 ----------");
    float d12 = Mathf.Abs(meds[0] - meds[1]);
    float d34 = Mathf.Abs(meds[2] - meds[3]);
    float dAvg = (d12 + d34) * 0.5f;
    gSb3.AppendLine("  ① 重复测量噪声（同档两次的差）："
                   + F4(d12 / 1024f) + " KB / " + F4(d34 / 1024f) + " KB　平均 " + F4(dAvg / 1024f) + " KB/帧");
    gSb3.AppendLine("     判据：噪声 > 0.5 KB/帧 ⇒ 「每帧分配」这个尺子分辨率不够，");
    gSb3.AppendLine("           任何小于该量级的 A/B 差额都不能当结论（上一版归因表就栽在这）。");
    float menuDelta = (meds[0] + meds[1]) / 2f - (meds[2] + meds[3]) / 2f;
    gSb3.AppendLine("  ② 主菜单 OnGUI 的净成本（G1 开 − G1 关）：" + F4(menuDelta / 1024f) + " KB/帧"
                   + "　（须显著大于噪声才算数）");
    float drift = meds[5] - meds[4];
    gSb3.AppendLine("  ③ 会话漂移（已进局第 3 次 − 第 1 次）：" + F4(drift / 1024f) + " KB/帧");
    gSb3.AppendLine();
    gSb3.AppendLine("  ④ 与 perf_baseline「静置档 5.96 KB/帧」对账：本探针 G2a = "
                   + F4(meds[4] / 1024f) + " KB/帧");
    gSb3.AppendLine("     若两者接近 ⇒ 基线的静置读数可信，A/B 探针 12.99 的高值是主菜单造成的；");
    gSb3.AppendLine("     若仍差很远 ⇒ 「每帧分配」读数受会话/编辑器状态影响，只适合同会话内相对比较。");

    File.WriteAllText(gRep3, gSb3.ToString(), new UTF8Encoding(false));
    Debug.Log("[perf_alloc_ab2] 报告已落盘 " + gRep3);
    Debug.Log(gSb3.ToString());
    yield break;
}

/// 采一档：热身 0.8s → 采 3s。setRunEnabled=null 表示不动 RunManager。
IEnumerator Window3(string label, float seconds, RunManager run, bool? setRunEnabled,
                    ProfilerRecorder recGc, ProfilerRecorder recDraw,
                    List<string> labels, List<float> meds, List<float> p95s,
                    List<float> totals, List<float> draws)
{
    if (run != null && setRunEnabled.HasValue) run.enabled = setRunEnabled.Value;

    {
        float tw = Time.unscaledTime;
        while (Time.unscaledTime - tw < 0.8f) yield return null;
    }

    var alloc = new List<float>(1024);
    var dr = new List<float>(1024);
    float t0 = Time.unscaledTime;
    while (Time.unscaledTime - t0 < seconds)
    {
        if (recGc.Valid) alloc.Add(recGc.LastValue);
        if (recDraw.Valid) dr.Add(recDraw.LastValue);
        yield return null;
    }

    double s = 0;
    for (int i = 0; i < alloc.Count; i++) s += alloc[i];
    labels.Add(label);
    meds.Add(Pct3(alloc, 0.5f));
    p95s.Add(Pct3(alloc, 0.95f));
    totals.Add((float)s);
    draws.Add(Pct3(dr, 0.5f));
}

return Body3();
