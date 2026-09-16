// perf_headroom.cs —— 真实帧耗与余量（把"帧率"从 vsync 的枷锁里放出来）
//
// 为什么需要它：perf_baseline 量出中位 162~176 fps，但同一次报告里
// `vSyncCount=1` —— 中位帧耗 6.19 ms 与 165 Hz 的 6.06 ms 几乎相等，
// 说明**读数是被垂直同步钉在天花板上的，不是能力的度量**。
// 拿这个数去支持论文里"1080p ≥120fps"，是拿天花板当结论。
//
// ★ 做法：同会话内先量 vsync 开、再量 vsync 关 —— 比较在同一会话内进行，
//   不受"每帧分配跨会话不可比"那个坑的影响（见 perf_alloc_ab2 的对账结论）。
//
// ★ 为什么只报**帧耗分位**而不报平均帧率：卡顿是个别长帧，平均值会抹平它。
//   同时报"最长帧"与">16.7ms 的帧数占比"，这才对应"手感卡不卡"。
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using Unity.Profiling;
using InkWash.Enemies;
using InkWash.Player;
using InkWash.Roguelike;
using InkWash.UI;

string gRoot4 = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
string gRep4 = Path.Combine(gRoot4, "Tools/reports/perf_headroom.txt");
StringBuilder gSb4 = new StringBuilder();

string F5(float v) { return v.ToString("0.###"); }

float Pct4(List<float> v, float p)
{
    if (v == null || v.Count == 0) return 0f;
    var c = new List<float>(v);
    c.Sort();
    return c[Mathf.Clamp((int)(c.Count * p), 0, c.Count - 1)];
}

ProfilerRecorder TryRec4(ProfilerCategory cat, string[] names, out string bound)
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

IEnumerator Body4()
{
    gSb4.AppendLine("========== perf_headroom 真实帧耗与余量 ==========");
    gSb4.AppendLine("生成时间 " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
    gSb4.AppendLine("分辨率 " + Screen.width + "x" + Screen.height
                   + "　场景 " + UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
    gSb4.AppendLine("CPU " + SystemInfo.processorType + "　GPU " + SystemInfo.graphicsDeviceName);

    var cam = Camera.main;
    var ph = Object.FindObjectOfType<PlayerHealth>();
    if (cam == null || ph == null)
    {
        gSb4.AppendLine("★ 失败：找不到 MainCamera 或 PlayerHealth ⇒ 需在 Main 场景的 Play 中运行");
        File.WriteAllText(gRep4, gSb4.ToString(), new UTF8Encoding(false));
        yield break;
    }

    int vsync0 = QualitySettings.vSyncCount;
    int tfr0 = Application.targetFrameRate;
    gSb4.AppendLine("改动前　vSyncCount=" + vsync0 + "　targetFrameRate=" + tfr0);

    var player = ph.gameObject;
    var ctl = player.GetComponent<PlayerController>();
    var panel = Object.FindObjectOfType<InkStylePanel>();
    var choice = Object.FindObjectOfType<SkillChoicePanel>();
    var run = Object.FindObjectOfType<RunManager>();
    var room = Object.FindObjectOfType<RoomController>();
    var spawner = Object.FindObjectOfType<WaveSpawner>();

    string bDraw, bSet, bTri;
    var recDraw = TryRec4(ProfilerCategory.Render, new[] { "Draw Calls Count" }, out bDraw);
    var recSet = TryRec4(ProfilerCategory.Render, new[] { "SetPass Calls Count" }, out bSet);
    var recTri = TryRec4(ProfilerCategory.Render, new[] { "Triangles Count" }, out bTri);

    // 统一的静置/战斗前置：与 perf_baseline 保持一致，便于对照
    ph.maxHealth = 100000f; ph.ResetHealth();
    if (panel != null) panel.visible = false;
    if (choice != null) choice.Hide();
    if (room != null) room.ResetForTest();
    if (run != null) { run.ResetForTest(); run.SetSeed(20260916); run.nextRoomDelay = 999f; }
    if (ctl != null) { ctl.ResetToLocomotion(); ctl.BeginInputOverride(); ctl.SetInjectedMove(Vector2.zero, false); }
    if (run != null) run.StartRun();
    foreach (var e in Object.FindObjectsOfType<EnemyBase>())
        if (e != null) Object.Destroy(e.gameObject);
    if (spawner != null) spawner.enabled = false;
    {
        float t = Time.unscaledTime;
        while (Time.unscaledTime - t < 2.0f) yield return null;
    }

    // ---- W1 vsync 开 · 静置 ----
    yield return Window4("W1 静置 · 垂直同步 开（原状）", 3f, false, ctl, player, cam, spawner,
                         recDraw, recSet, recTri);

    // ---- W2 vsync 关 · 静置（同会话对照）----
    QualitySettings.vSyncCount = 0;
    Application.targetFrameRate = -1;
    { float t = Time.unscaledTime; while (Time.unscaledTime - t < 0.5f) yield return null; }
    yield return Window4("W2 静置 · 垂直同步 **关**", 3f, false, ctl, player, cam, spawner,
                         recDraw, recSet, recTri);

    // ---- W3 vsync 关 · 满负荷战斗（真实余量）----
    int spawned = 0;
    GameObject prefab = null;
    if (spawner != null && spawner.waves != null)
        for (int i = 0; i < spawner.waves.Length && prefab == null; i++)
            if (spawner.waves[i] != null && spawner.waves[i].entries != null)
                for (int j = 0; j < spawner.waves[i].entries.Length; j++)
                    if (spawner.waves[i].entries[j] != null && spawner.waves[i].entries[j].prefab != null)
                    { prefab = spawner.waves[i].entries[j].prefab; break; }

    if (prefab != null)
    {
        Vector3 c = player.transform.position;
        for (int i = 0; i < 6; i++)
        {
            float a = i * Mathf.PI * 2f / 6f;
            Vector3 p = c + new Vector3(Mathf.Cos(a) * 6f, 0f, Mathf.Sin(a) * 6f);
            if (Object.Instantiate(prefab, p, Quaternion.LookRotation(c - p, Vector3.up)) != null) spawned++;
        }
    }
    gSb4.AppendLine("战斗档：手工生成敌人 " + spawned + " 只（波次生成器在本场景不刷怪，已在基线报告里记录）");
    {
        float t = Time.unscaledTime;
        while (Time.unscaledTime - t < 2.0f) yield return null;
    }
    yield return Window4("W3 战斗 · 垂直同步 关", 10f, true, ctl, player, cam, spawner,
                         recDraw, recSet, recTri);

    // 清场并复位
    foreach (var e in Object.FindObjectsOfType<EnemyBase>())
        if (e != null) Object.Destroy(e.gameObject);
    if (ctl != null) { ctl.SetInjectedMove(Vector2.zero, false); ctl.EndInputOverride(); }
    QualitySettings.vSyncCount = vsync0;
    Application.targetFrameRate = tfr0;
    gSb4.AppendLine("已恢复　vSyncCount=" + QualitySettings.vSyncCount
                   + "　targetFrameRate=" + Application.targetFrameRate);

    gSb4.AppendLine();
    gSb4.AppendLine("---------- 判定口径 ----------");
    gSb4.AppendLine("  · 论文/毕设的 1080p 目标：战斗档中位 ≥120 fps，且 >16.7ms 的帧占比 ≤1%。");
    gSb4.AppendLine("  · 这是**编辑器内**的数：仍含编辑器开销（窗口重绘、bridge 轮询），");
    gSb4.AppendLine("    故它给出的是**下界**——真机构建只会更快。");

    File.WriteAllText(gRep4, gSb4.ToString(), new UTF8Encoding(false));
    Debug.Log("[perf_headroom] 报告已落盘 " + gRep4);
    Debug.Log(gSb4.ToString());
    yield break;
}

IEnumerator Window4(string tag, float seconds, bool combat, PlayerController ctl,
                    GameObject player, Camera cam, WaveSpawner spawner,
                    ProfilerRecorder recDraw, ProfilerRecorder recSet, ProfilerRecorder recTri)
{
    {
        float tw = Time.unscaledTime;
        while (Time.unscaledTime - tw < 0.8f) yield return null;
    }

    var dts = new List<float>(4096);
    var dr = new List<float>(4096);
    var st = new List<float>(4096);
    var tr = new List<float>(4096);
    bool hasTarget = false;
    Vector3 target = Vector3.zero;
    float lastAtk = -9f;
    int frame = 0;
    int kills = 0;

    float t0 = Time.unscaledTime;
    while (Time.unscaledTime - t0 < seconds)
    {
        if (combat && ctl != null)
        {
            if (frame % 10 == 0)
            {
                Vector3 pp = player.transform.position;
                EnemyBase best = null; float bd = float.MaxValue;
                var all = Object.FindObjectsOfType<EnemyBase>();
                for (int i = 0; i < all.Length; i++)
                {
                    if (all[i] == null) continue;
                    float d = (all[i].transform.position - pp).sqrMagnitude;
                    if (d < bd) { bd = d; best = all[i]; }
                }
                hasTarget = best != null;
                target = hasTarget ? best.transform.position : Vector3.zero;
            }
            Vector3 dd = hasTarget ? target - player.transform.position : Vector3.zero;
            Vector3 f = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up);
            if (f.sqrMagnitude < 1e-6f) f = Vector3.forward;
            f.Normalize();
            Vector3 rr = Vector3.Cross(Vector3.up, f);
            Vector3 dp = Vector3.ProjectOnPlane(dd, Vector3.up);
            Vector2 mv = dp.sqrMagnitude < 1e-6f ? Vector2.zero
                       : new Vector2(Vector3.Dot(dp.normalized, rr), Vector3.Dot(dp.normalized, f));
            ctl.SetInjectedMove(mv, false);

            bool canPress = ctl.Phase != ActionPhase.Attack || ctl.IsCancelWindowOpen;
            if (canPress && Time.unscaledTime - lastAtk > 0.12f)
            {
                lastAtk = Time.unscaledTime;
                ctl.RequestInjectedAttack();
            }
        }

        dts.Add(Time.unscaledDeltaTime * 1000f);
        if (recDraw.Valid) dr.Add(recDraw.LastValue);
        if (recSet.Valid) st.Add(recSet.LastValue);
        if (recTri.Valid) tr.Add(recTri.LastValue);
        frame++;
        yield return null;
    }

    float med = Pct4(dts, 0.5f);
    float p99 = Pct4(dts, 0.99f);
    float worst = Pct4(dts, 1f);
    int over167 = 0;
    for (int i = 0; i < dts.Count; i++) if (dts[i] > 16.67f) over167++;

    gSb4.AppendLine("---------- [" + tag + "] " + dts.Count + " 帧 ----------");
    gSb4.AppendLine("  帧耗 中位 " + F5(med) + " ms（" + F5(1000f / Mathf.Max(med, 1e-4f)) + " fps）"
                   + "　1%低 " + F5(p99) + " ms（" + F5(1000f / Mathf.Max(p99, 1e-4f)) + " fps）"
                   + "　最长 " + F5(worst) + " ms");
    gSb4.AppendLine("  >16.7ms 的帧 " + over167 + " / " + dts.Count
                   + "（占 " + F5(100f * over167 / Mathf.Max(1, dts.Count)) + " %）");
    if (dr.Count > 0)
        gSb4.AppendLine("  draw call 中位 " + Pct4(dr, 0.5f) + "　setpass 中位 " + Pct4(st, 0.5f)
                       + "　三角面 中位 " + Pct4(tr, 0.5f));
    gSb4.AppendLine();
}

return Body4();
