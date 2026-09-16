// perf_baseline.cs —— 性能基线（F2）：把"卡不卡"拆成可核对的数
//
// 为什么必须先做它：优化最容易被"感觉快了"骗过去。本项目对"看一眼"已经栽过
// 一次（画面墨感的整改就是靠 d_metrics 把观感变成数字才收住的），性能同理 ——
// 没有基线，F3 的对象池改完只能说"应该好点"，说不出"省了多少 GC / 掉了多少帧"。
//
// ★ 三个必要的度量设计（否则数字会撒谎）：
//   1) **分三个负载档**（静置 / 跑动 / 满负荷战斗）。单一"帧率"无法定位瓶颈：
//      若瓶颈在 VFX 的分配，只有战斗档会掉；若在渲染管线本身，三档一起掉。
//      档间差值就是"战斗额外开销"的直接度量。
//   2) **逐帧采样，报中位 + 1% 低 + 最差 + 长帧计数**。卡顿是个别长帧，
//      首尾差/平均值会把它们完全抹平 —— 于是"掉帧"在报告里永远显示满帧
//      （d_metrics 已经踩过一次）。
//   3) **度量窗口裁到事件边界之后**（先热身 0.8s 再开窗）。上一窗口遗留的
//      大块垃圾回收会集中落在新窗口第一帧，量出纯属探针自伤的尖峰。
//
// ★ GC 用两个独立证据交叉验证，且**不可用时必须吵**：
//   · ProfilerRecorder "GC.Alloc"（每帧累计分配字节）—— 拿不到就明说"不可用"，
//     绝不当成 0；
//   · GC.CollectionCount(0) 窗口前后差值 —— 不依赖 Profiler，必然拿得到。
//   只报后者能判断"有没有在分配"但报不出量级，所以两个都要。
//
// ★ 探针自身的开销不能污染测量：FindObjectsOfType 之类每帧全场景扫描
//   只在每 10~15 帧做一次（逐帧扫会把自己变成瓶颈）。
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Profiling;
using Unity.Profiling;
using InkWash.Effects;
using InkWash.Enemies;
using InkWash.Player;
using InkWash.Roguelike;
using InkWash.UI;

string gRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
string gRep = Path.Combine(gRoot, "Tools/reports/perf_baseline.txt");
StringBuilder gSb = new StringBuilder();

// 战斗档的目标方向（每 10 帧更新一次，避免全场景扫描污染测量）
// ★ 必须声明在 LoadWindow 之前：局部变量的作用域从声明点开始，声明在函数后面就用不了。
bool gHasTarget = false;
Vector3 gTarget = Vector3.zero;

string F3(float v) { return v.ToString("0.###"); }

List<float> Sorted(List<float> v)
{
    var c = new List<float>(v);
    c.Sort();
    return c;
}

float Pct(List<float> v, float p)
{
    if (v == null || v.Count == 0) return 0f;
    var c = Sorted(v);
    return c[Mathf.Clamp((int)(c.Count * p), 0, c.Count - 1)];
}

float Med(List<float> v) { return Pct(v, 0.5f); }

float Sum(List<float> v)
{
    if (v == null) return 0f;
    double s = 0;
    for (int i = 0; i < v.Count; i++) s += v[i];
    return (float)s;
}

/// 依次试若干候选计数器名，返回第一个绑定成功的 recorder。
/// 绑不上时 bound 保持 "(未绑定)" —— 调用方必须把它写进报告，不许静默当 0。
ProfilerRecorder TryRec(ProfilerCategory cat, string[] names, out string bound)
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

float Last(ProfilerRecorder r) { return r.Valid ? r.LastValue : 0f; }

/// 求"朝目标前进"的相机相对输入。
/// ★ 注入输入是**相机相对**的（ResolveMoveDirection = fwd*y + right*x），
///   喂世界方向会整体歪掉 —— 上一轮在"剑砍空气"上吃过这个亏。
Vector2 InputToward(Camera cam, Vector3 worldDir)
{
    Vector3 f = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up);
    if (f.sqrMagnitude < 1e-6f) f = Vector3.forward;
    f.Normalize();
    Vector3 r = Vector3.Cross(Vector3.up, f);
    Vector3 d = Vector3.ProjectOnPlane(worldDir, Vector3.up);
    if (d.sqrMagnitude < 1e-6f) return Vector2.zero;
    d.Normalize();
    return new Vector2(Vector3.Dot(d, r), Vector3.Dot(d, f));
}

// ---------- 主流程 ----------

IEnumerator Body()
{
    gSb.AppendLine("========== perf_baseline 性能基线（F2）==========");
    gSb.AppendLine("生成时间 " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
    gSb.AppendLine("Unity " + Application.unityVersion + "　平台 " + Application.platform);
    gSb.AppendLine("CPU " + SystemInfo.processorType + "（" + SystemInfo.processorCount + " 核）");
    gSb.AppendLine("GPU " + SystemInfo.graphicsDeviceName + "　API " + SystemInfo.graphicsDeviceType);
    gSb.AppendLine("渲染管线 " + UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline);
    gSb.AppendLine("分辨率 " + Screen.width + "x" + Screen.height
                   + "　vSyncCount=" + QualitySettings.vSyncCount
                   + "　targetFrameRate=" + Application.targetFrameRate
                   + "　场景 " + UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);

    var cam = Camera.main;
    var ph = Object.FindObjectOfType<PlayerHealth>();
    if (cam == null || ph == null)
    {
        gSb.AppendLine("★ 失败：找不到 MainCamera 或 PlayerHealth ⇒ 先跑 perf_open 切到 Main.unity 并进 Play");
        File.WriteAllText(gRep, gSb.ToString(), new UTF8Encoding(false));
        Debug.LogError("[perf_baseline] 前置条件不满足，报告已落盘");
        yield break;
    }

    var player = ph.gameObject;
    var ctl = player.GetComponent<PlayerController>();
    var stance = player.GetComponentInChildren<CombatStance>(true);
    var vfx = player.GetComponentInChildren<SwordVfx>(true);
    var panel = Object.FindObjectOfType<InkStylePanel>();
    var run = Object.FindObjectOfType<RunManager>();
    var choice = Object.FindObjectOfType<SkillChoicePanel>();
    var spawner = Object.FindObjectOfType<WaveSpawner>();
    var room = Object.FindObjectOfType<RoomController>();

    if (ctl == null)
    {
        gSb.AppendLine("★ 失败：玩家上没有 PlayerController");
        File.WriteAllText(gRep, gSb.ToString(), new UTF8Encoding(false));
        yield break;
    }

    if (panel != null) panel.visible = false;
    if (choice != null) choice.Hide();

    // 玩家无敌：否则战斗档打到一半就死了，窗口长度不可控。
    // 这不影响性能结论 —— 我们要的是"负载持续存在"，不是"能打多久"。
    ph.maxHealth = 100000f; ph.ResetHealth();

    if (room != null) room.ResetForTest();
    if (run != null) { run.ResetForTest(); run.SetSeed(20260916); run.nextRoomDelay = 999f; }
    ctl.ResetToLocomotion();
    if (stance != null) { stance.combatExitDelay = 99999f; stance.ForceStance(true); }
    ctl.BeginInputOverride();
    ctl.SetInjectedMove(Vector2.zero, false);
    if (run != null) run.StartRun();

    {
        float t = Time.unscaledTime;
        while (Time.unscaledTime - t < 2.0f) yield return null;
    }

    // 清场：让"静置/跑动"两档真的是空场，档间差才等于"战斗额外开销"
    int preCleared = 0;
    foreach (var e in Object.FindObjectsOfType<EnemyBase>())
        if (e != null) { Object.Destroy(e.gameObject); preCleared++; }
    if (spawner != null) spawner.enabled = false;
    gSb.AppendLine("清场：已有敌人 " + preCleared + " 只已移除，波次生成器已关闭");
    {
        float t = Time.unscaledTime;
        while (Time.unscaledTime - t < 1.0f) yield return null;
    }

    // ---------- 绑定计数器（成败一律写进报告）----------
    string bDraw, bSet, bBatch, bTri, bGcM, bGcI;
    var recDraw = TryRec(ProfilerCategory.Render, new[] { "Draw Calls Count" }, out bDraw);
    var recSet = TryRec(ProfilerCategory.Render, new[] { "SetPass Calls Count" }, out bSet);
    var recBatch = TryRec(ProfilerCategory.Render, new[] { "Batches Count" }, out bBatch);
    var recTri = TryRec(ProfilerCategory.Render, new[] { "Triangles Count" }, out bTri);
    var recGcM = TryRec(ProfilerCategory.Memory, new[] { "GC.Alloc", "GC Allocated In Frame" }, out bGcM);
    var recGcI = default(ProfilerRecorder);
    bGcI = "(未尝试：Memory 已绑定)";
    if (!recGcM.Valid) recGcI = TryRec(ProfilerCategory.Internal, new[] { "GC.Alloc" }, out bGcI);

    gSb.AppendLine();
    gSb.AppendLine("---------- 计数器绑定 ----------");
    gSb.AppendLine("  Draw Calls Count    " + bDraw);
    gSb.AppendLine("  SetPass Calls Count " + bSet);
    gSb.AppendLine("  Batches Count       " + bBatch);
    gSb.AppendLine("  Triangles Count     " + bTri);
    gSb.AppendLine("  GC.Alloc (Memory)   " + bGcM);
    gSb.AppendLine("  GC.Alloc (Internal) " + bGcI);
    gSb.AppendLine("  ※ 「未绑定」= 该计数器在本机/本版本不可用，报告中相关行一律写「不可用」，不写 0。");
    gSb.AppendLine();

    // ---------- 三个负载档 ----------
    yield return LoadWindow("静置", 6f, 0, cam, ctl, player, vfx, spawner, recGcM, recGcI,
                            recDraw, recSet, recBatch, recTri);
    yield return LoadWindow("跑动", 6f, 1, cam, ctl, player, vfx, spawner, recGcM, recGcI,
                            recDraw, recSet, recBatch, recTri);
    yield return LoadWindow("满负荷战斗", 14f, 2, cam, ctl, player, vfx, spawner, recGcM, recGcI,
                            recDraw, recSet, recBatch, recTri);

    ctl.SetInjectedMove(Vector2.zero, false);
    ctl.EndInputOverride();
    if (stance != null) stance.combatExitDelay = 6f;

    gSb.AppendLine("---------- 判定口径 ----------");
    gSb.AppendLine("  · 硬判据只看**战斗档**：中位帧率 ≥120 fps（1080p 设计目标）、gen0 GC 次数 ≤1 次/窗口。");
    gSb.AppendLine("  · 编辑器 Play 含编辑器开销并受 vsync 上限约束 ⇒ 绝对帧率仅作回退告警，不作硬判据。");
    gSb.AppendLine("  · 静置/跑动两档的作用是**定性**：它们的分配量就是「没有战斗时也在产生的垃圾」的地板。");

    File.WriteAllText(gRep, gSb.ToString(), new UTF8Encoding(false));
    Debug.Log("[perf_baseline] 报告已落盘 " + gRep);
    Debug.Log(gSb.ToString());
    yield break;
}

/// 单个负载档：热身 0.8s → 逐帧采样 seconds 秒。
/// mode: 0=静置（无输入）1=跑动（前进不攻击）2=满负荷战斗（敌人 + 连打 + 移动）
IEnumerator LoadWindow(string tag, float seconds, int mode,
                       Camera cam, PlayerController ctl, GameObject player, SwordVfx vfx,
                       WaveSpawner spawner,
                       ProfilerRecorder recGcM, ProfilerRecorder recGcI,
                       ProfilerRecorder recDraw, ProfilerRecorder recSet,
                       ProfilerRecorder recBatch, ProfilerRecorder recTri)
{
    // ---- 战斗档：开波次生成器、等敌人到场；到场不足则手工补齐（并明说走了哪条路）----
    if (mode == 2 && spawner != null)
    {
        spawner.ResetForTest();
        spawner.spawnInterval = 0.35f;
        if (spawner.waves != null)
            for (int i = 0; i < spawner.waves.Length; i++)
                if (spawner.waves[i] != null) spawner.waves[i].delayBefore = 0.2f;
        spawner.enabled = true;

        float tw = Time.unscaledTime;
        while (Time.unscaledTime - tw < 8f && spawner.AliveEnemies < 2) yield return null;

        gSb.AppendLine("  [战斗档准备] 等 " + F3(Time.unscaledTime - tw) + "s　在场敌人 " + spawner.AliveEnemies
                      + "　累计生成 " + spawner.SpawnedCount + "　生成失败 " + spawner.SpawnFailedCount
                      + "　波次 " + spawner.CurrentWave + "/" + spawner.WaveCount);

        int manual = 0;
        if (spawner.AliveEnemies < 2)
        {
            // 波次生成器必须过 NavMesh.SamplePosition，失败就静默不刷 —— 若本场景
            // 没烤导航网格，战斗档会退化成空场，基线就白跑了。这里手工补齐，
            // 并在报告里**明写"走了手工补齐"**，免得把退化数据当正常数据用。
            GameObject prefab = null;
            if (spawner.waves != null)
                for (int i = 0; i < spawner.waves.Length && prefab == null; i++)
                    if (spawner.waves[i] != null && spawner.waves[i].entries != null)
                        for (int j = 0; j < spawner.waves[i].entries.Length; j++)
                            if (spawner.waves[i].entries[j] != null && spawner.waves[i].entries[j].prefab != null)
                            { prefab = spawner.waves[i].entries[j].prefab; break; }

            if (prefab == null)
                gSb.AppendLine("  ★ 波次配置里找不到任何可用 prefab ⇒ 战斗档只能是空场");
            else
            {
                Vector3 c = player.transform.position;
                for (int i = 0; i < 6; i++)
                {
                    float a = i * Mathf.PI * 2f / 6f;
                    Vector3 p = c + new Vector3(Mathf.Cos(a) * 6f, 0f, Mathf.Sin(a) * 6f);
                    var go = Object.Instantiate(prefab, p, Quaternion.LookRotation(c - p, Vector3.up));
                    if (go != null) manual++;
                }
                gSb.AppendLine("  ★ 波次生成器只到场 " + spawner.AliveEnemies + " 只 ⇒ **手工补齐 " + manual
                              + " 只**（本档负载来自手工生成，不含波次逻辑开销）");
                float tw2 = Time.unscaledTime;
                while (Time.unscaledTime - tw2 < 1.5f) yield return null;
            }
        }
        if (manual == 0 && spawner.AliveEnemies < 2)
            gSb.AppendLine("  ★★ 警告：本档在场敌人不足 2 只 ⇒ 负载不足，结论**不可用于**判定战斗开销");
    }

    if (mode == 0) ctl.SetInjectedMove(Vector2.zero, false);
    else ctl.SetInjectedMove(Vector2.up, true);

    // ---- 热身：把度量窗口裁到事件边界之后 ----
    {
        float tw = Time.unscaledTime;
        while (Time.unscaledTime - tw < 0.8f) yield return null;
    }

    var dts = new List<float>(4096);
    var gc = new List<float>(4096);
    var draw = new List<float>(4096);
    var setp = new List<float>(4096);
    var batch = new List<float>(4096);
    var tri = new List<float>(4096);
    int enemyPeak = 0, projPeak = 0, inkActivePeak = 0;

    int gc0a = 0, gc1a = 0, gc2a = 0;
    try { gc0a = GC.CollectionCount(0); gc1a = GC.CollectionCount(1); gc2a = GC.CollectionCount(2); } catch (System.Exception) { }
    long monoA = 0, natA = 0;
    try { monoA = Profiler.GetMonoUsedSizeLong(); natA = Profiler.GetTotalAllocatedMemoryLong(); } catch (System.Exception) { }

    int swing0 = vfx != null ? vfx.SwingCount : 0;
    int arc0 = vfx != null ? vfx.ArcSpawnCount : 0;
    int ink0 = InkHitVfx.Instance != null ? InkHitVfx.Instance.SpawnCount : 0;

    float lastAtk = -9f;
    int frame = 0;
    float wallT0 = Time.unscaledTime;
    while (Time.unscaledTime - wallT0 < seconds)
    {
        if (mode == 2)
        {
            // 每 10 帧重算一次目标（逐帧全场景扫描会把自己变成瓶颈）
            if (frame % 10 == 0)
            {
                Vector3 pp = player.transform.position;
                EnemyBase best = null; float bestD = float.MaxValue;
                var all = Object.FindObjectsOfType<EnemyBase>();
                for (int i = 0; i < all.Length; i++)
                {
                    if (all[i] == null) continue;
                    float d = (all[i].transform.position - pp).sqrMagnitude;
                    if (d < bestD) { bestD = d; best = all[i]; }
                }
                gHasTarget = best != null;
                gTarget = gHasTarget ? best.transform.position : Vector3.zero;
            }
            ctl.SetInjectedMove(gHasTarget ? InputToward(cam, gTarget - player.transform.position) : Vector2.zero, false);

            // 连打：只在能出手的时机按（与 S2 连击段同一套判据，避免按键被丢弃后
            // 攻击频率失真 —— 那会让"负载"变成随机的）
            bool canPress = ctl.Phase != ActionPhase.Attack || ctl.IsCancelWindowOpen;
            if (canPress && Time.unscaledTime - lastAtk > 0.12f)
            {
                lastAtk = Time.unscaledTime;
                ctl.RequestInjectedAttack();
            }
        }

        // ---- 逐帧采样 ----
        dts.Add(Time.unscaledDeltaTime * 1000f);
        if (recGcM.Valid) gc.Add(Last(recGcM));
        else if (recGcI.Valid) gc.Add(Last(recGcI));
        if (recDraw.Valid) draw.Add(Last(recDraw));
        if (recSet.Valid) setp.Add(Last(recSet));
        if (recBatch.Valid) batch.Add(Last(recBatch));
        if (recTri.Valid) tri.Add(Last(recTri));

        if (frame % 15 == 0)
        {
            int ne = Object.FindObjectsOfType<EnemyBase>().Length;
            if (ne > enemyPeak) enemyPeak = ne;
            int np = Object.FindObjectsOfType<InkProjectile>().Length;
            if (np > projPeak) projPeak = np;
        }
        int ia = InkHitVfx.Instance != null ? InkHitVfx.Instance.ActiveCount : 0;
        if (ia > inkActivePeak) inkActivePeak = ia;

        frame++;
        yield return null;
    }

    int gc0b = 0, gc1b = 0, gc2b = 0;
    try { gc0b = GC.CollectionCount(0); gc1b = GC.CollectionCount(1); gc2b = GC.CollectionCount(2); } catch (System.Exception) { }
    long monoB = 0, natB = 0;
    try { monoB = Profiler.GetMonoUsedSizeLong(); natB = Profiler.GetTotalAllocatedMemoryLong(); } catch (System.Exception) { }

    float dur = Time.unscaledTime - wallT0;
    float medMs = Med(dts), p99Ms = Pct(dts, 0.99f), worstMs = Pct(dts, 1f);
    int over33 = 0, over16 = 0;
    for (int i = 0; i < dts.Count; i++)
    {
        if (dts[i] > 33.34f) over33++;
        if (dts[i] > 16.67f) over16++;
    }

    gSb.AppendLine("---------- [" + tag + "] " + F3(dur) + "s / " + frame + " 帧 ----------");
    gSb.AppendLine("  帧率(中位) " + F3(1000f / Mathf.Max(medMs, 1e-4f)) + " fps　帧耗 中位 " + F3(medMs)
                   + " ms / 1%低 " + F3(p99Ms) + " ms / 最差 " + F3(worstMs) + " ms");
    gSb.AppendLine("  长帧　>33.3ms(跌到30fps下) " + over33 + " 帧　>16.7ms(跌到60fps下) " + over16
                   + " 帧（占 " + F3(100f * over16 / Mathf.Max(1, frame)) + " %）");
    gSb.AppendLine("  GC　gen0 " + (gc0b - gc0a) + " 次　gen1 " + (gc1b - gc1a) + " 次　gen2 " + (gc2b - gc2a) + " 次");
    if (gc.Count > 0)
        gSb.AppendLine("     每帧分配 中位 " + F3(Med(gc) / 1024f) + " KB　95% " + F3(Pct(gc, 0.95f) / 1024f)
                       + " KB　窗口合计 " + F3(Sum(gc) / 1048576f) + " MB");
    else
        gSb.AppendLine("     每帧分配 **不可用**（GC.Alloc 计数器未绑定）");
    gSb.AppendLine("     托管堆 " + F3(monoA / 1048576f) + " → " + F3(monoB / 1048576f) + " MB（净增 "
                   + F3((monoB - monoA) / 1048576f) + "）　本机总分配内存 " + F3(natA / 1048576f)
                   + " → " + F3(natB / 1048576f) + " MB");
    if (draw.Count > 0)
        gSb.AppendLine("  渲染　draw call 中位 " + Med(draw) + "　setpass 中位 " + Med(setp)
                       + "　batches 中位 " + Med(batch) + "　三角面 中位 " + Med(tri));
    else
        gSb.AppendLine("  渲染　**不可用**（draw call 计数器未绑定）");
    if (vfx != null)
        gSb.AppendLine("  负载　挥砍 " + (vfx.SwingCount - swing0) + " 次　弧光生成 " + (vfx.ArcSpawnCount - arc0)
                       + " 个（同刻静态活跃 " + SwordVfx.ActiveArcCount + "）　　溅墨生成 "
                       + (InkHitVfx.Instance != null ? InkHitVfx.Instance.SpawnCount - ink0 : 0)
                       + " 个（活跃峰值 " + inkActivePeak + "）");
    gSb.AppendLine("  负载　在场敌人峰值 " + enemyPeak + "　弹丸峰值 " + projPeak);
    gSb.AppendLine();

    if (mode == 2 && spawner != null) spawner.enabled = false;
}

return Body();
