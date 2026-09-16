// perf_alloc_ab.cs —— 每帧分配归因（F3 的前置实验）
//
// 为什么需要它：perf_baseline 量出空场就有 ~6 KB/帧的固定垃圾（≈1 MB/s），
// 但**静态扫描解释不了它**（4 处 OnGUI 里 3 处有门禁、Main 场景里根本没有 ActionShowcase）。
// 猜是没用的 —— 本轮已经猜错一次（以为瓶颈在弧光/弹丸的对象池）。
// 唯一可靠的办法是**逐个关掉子系统再量**：谁的差值大，谁就是元凶。
//
// ★ 实验设计（减法律）：不是"一个个打开"而是"一个个关掉"。因为打开是从零叠加，
//   耦合项会互相掩盖；关掉是在既有基线上做减法，差值直接等于该子系统的净贡献。
//
// ★ 探针自身绝不能污染测量：测量循环里**不许有任何全场景扫描**
//   （FindObjectsOfType 每次都返回新数组）。所有组件引用在循环外解析一次。
//   各档走的是同一条代码路径，所以探针自身的分配在各档之间是常量，差值仍然有效。
//
// ★ gen0 也要报：分配率高但 GC 从不触发时，说明 GC 是增量的/堆很大，
//   此时"每帧分配字节"才是前瞻指标（先兆），gen0 次数是滞后指标。
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Profiling;
using Unity.Profiling;
using InkWash.CameraRig;
using InkWash.Combat;
using InkWash.Core;
using InkWash.Effects;
using InkWash.Enemies;
using InkWash.Player;
using InkWash.Roguelike;
using InkWash.UI;

string gRoot2 = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
string gRep2 = Path.Combine(gRoot2, "Tools/reports/perf_alloc_ab.txt");
StringBuilder gSb2 = new StringBuilder();

string F2(float v) { return v.ToString("0.###"); }

float Pct2(List<float> v, float p)
{
    if (v == null || v.Count == 0) return 0f;
    var c = new List<float>(v);
    c.Sort();
    return c[Mathf.Clamp((int)(c.Count * p), 0, c.Count - 1)];
}

ProfilerRecorder TryRec2(ProfilerCategory cat, string[] names, out string bound)
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

IEnumerator Body2()
{
    gSb2.AppendLine("========== perf_alloc_ab 每帧分配归因 ==========");
    gSb2.AppendLine("生成时间 " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
    gSb2.AppendLine("分辨率 " + Screen.width + "x" + Screen.height + "　vSync " + QualitySettings.vSyncCount
                   + "　场景 " + UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);

    // GC 模式：增量 GC 会把长暂停切成小片，这解释了"分配很高但 gen0 计数为 0"
    try
    {
        gSb2.AppendLine("GC　isIncremental=" + UnityEngine.Scripting.GarbageCollector.isIncremental
                       + "　GCMode=" + UnityEngine.Scripting.GarbageCollector.GCMode);
    }
    catch (System.Exception e) { gSb2.AppendLine("GC 模式读取失败: " + e.Message); }

    var cam = Camera.main;
    var ph = Object.FindObjectOfType<PlayerHealth>();
    if (cam == null || ph == null)
    {
        gSb2.AppendLine("★ 失败：找不到 MainCamera 或 PlayerHealth ⇒ 先在 Play 里跑，且场景须为 Main");
        File.WriteAllText(gRep2, gSb2.ToString(), new UTF8Encoding(false));
        Debug.LogError("[perf_alloc_ab] 前置条件不满足");
        yield break;
    }

    var player = ph.gameObject;
    var ctl = player.GetComponent<PlayerController>();
    var stance = player.GetComponentInChildren<CombatStance>(true);
    var vfx = player.GetComponentInChildren<SwordVfx>(true);
    var foot = player.GetComponentInChildren<FootIK>(true);

    // ---- 组件解析：全部在这里做一次，测量循环里绝不再扫场景 ----
    var run = Object.FindObjectOfType<RunManager>();
    var panel = Object.FindObjectOfType<InkStylePanel>();
    var choice = Object.FindObjectOfType<SkillChoicePanel>();
    var spawner = Object.FindObjectOfType<WaveSpawner>();
    var room = Object.FindObjectOfType<RoomController>();
    var hitVfx = InkHitVfx.Instance != null ? InkHitVfx.Instance
              : Object.FindObjectOfType<InkHitVfx>();
    var audio = Object.FindObjectOfType<AudioDirector>();
    var rig = player.GetComponentInChildren<ThirdPersonCamera>(true);
    if (rig == null) rig = Object.FindObjectOfType<ThirdPersonCamera>();

    // Cinemachine Brain 按**类型名**找（本项目硬规矩：CinemachineBrain 挂在主相机上会每帧覆盖手设机位；
    // 这里要把它当成"可能的每帧分配源"来测，所以不做销毁、只做开关）
    MonoBehaviour[] brain = null;
    {
        var all = cam.GetComponents<MonoBehaviour>();
        foreach (var mb in all)
            if (mb != null && mb.GetType().Name == "CinemachineBrain") brain = new[] { mb };
    }

    gSb2.AppendLine();
    gSb2.AppendLine("---------- 组件解析（找不到就明写，不许静默）----------");
    gSb2.AppendLine("  RunManager " + Mark(run) + "　InkStylePanel " + Mark(panel)
                   + "　SkillChoicePanel " + Mark(choice) + "　WaveSpawner " + Mark(spawner));
    gSb2.AppendLine("  RoomController " + Mark(room) + "　InkHitVfx " + Mark(hitVfx)
                   + "　AudioDirector " + Mark(audio) + "　ThirdPersonCamera " + Mark(rig));
    gSb2.AppendLine("  CinemachineBrain " + (brain != null ? "有（挂在主相机上）" : "没有"));
    gSb2.AppendLine("  PlayerController " + Mark(ctl) + "　CombatStance " + Mark(stance)
                   + "　SwordVfx " + Mark(vfx) + "　FootIK " + Mark(foot));

    // ---- 场景状态：与 perf_baseline 的"静置档"对齐（空场、无输入）----
    ph.maxHealth = 100000f; ph.ResetHealth();
    if (room != null) room.ResetForTest();
    if (run != null) { run.ResetForTest(); run.SetSeed(20260916); run.nextRoomDelay = 999f; }
    if (panel != null) panel.visible = false;
    if (choice != null) choice.Hide();
    if (ctl != null) { ctl.ResetToLocomotion(); ctl.BeginInputOverride(); ctl.SetInjectedMove(Vector2.zero, false); }
    if (stance != null) stance.combatExitDelay = 99999f;

    foreach (var e in Object.FindObjectsOfType<EnemyBase>())
        if (e != null) Object.Destroy(e.gameObject);
    if (spawner != null) spawner.enabled = false;
    {
        float t = Time.unscaledTime;
        while (Time.unscaledTime - t < 1.5f) yield return null;
    }

    string bGc;
    var recGc = TryRec2(ProfilerCategory.Memory, new[] { "GC.Alloc" }, out bGc);
    string bDraw;
    var recDraw = TryRec2(ProfilerCategory.Render, new[] { "Draw Calls Count" }, out bDraw);
    gSb2.AppendLine();
    gSb2.AppendLine("计数器　GC.Alloc=" + bGc + "　Draw Calls=" + bDraw);

    // ---- 实验档位 ----
    var names = new List<string>();
    var applyAll = new List<System.Action>();
    var undoAll = new List<System.Action>();

    // 关掉一个子系统 = 关掉**组件本身**（enabled=false），而不是 SetActive(false)：
    // 后者会把同一对象上别的组件也一起停掉，差值就不干净了。

    // V0 原样
    names.Add("V0 原样（静置）");
    applyAll.Add(() => { });
    undoAll.Add(() => { });

    // V1 关主菜单/结算 GUI（RunManager.OnGUI 每帧 new Rect + new GUIStyle）
    names.Add("V1 关 RunManager（含其 OnGUI）");
    applyAll.Add(() => { if (run != null) run.enabled = false; });
    undoAll.Add(() => { if (run != null) run.enabled = true; });

    // V2 关三处 UI（InkStylePanel / SkillChoicePanel 已早退，一并关掉以确认它们确实不产垃圾）
    names.Add("V2 关全部 UI 组件");
    applyAll.Add(() => { if (panel != null) panel.enabled = false; if (choice != null) choice.enabled = false; });
    undoAll.Add(() => { if (panel != null) panel.enabled = true; if (choice != null) choice.enabled = true; });

    // V3 关玩家（控制器 + 战斗姿态 + 剑特效 + FootIK）
    names.Add("V3 关玩家（Controller/Stance/SwordVfx/FootIK）");
    applyAll.Add(() =>
    {
        if (ctl != null) ctl.enabled = false;
        if (stance != null) stance.enabled = false;
        if (vfx != null) vfx.enabled = false;
        if (foot != null) foot.enabled = false;
    });
    undoAll.Add(() =>
    {
        if (ctl != null) ctl.enabled = true;
        if (stance != null) stance.enabled = true;
        if (vfx != null) vfx.enabled = true;
        if (foot != null) foot.enabled = true;
    });

    // V4 关相机（第三人称相机 + Cinemachine Brain）
    names.Add("V4 关相机系统（ThirdPersonCamera + CinemachineBrain）");
    applyAll.Add(() =>
    {
        if (rig != null) rig.enabled = false;
        if (brain != null) foreach (var b in brain) if (b != null) b.enabled = false;
    });
    undoAll.Add(() =>
    {
        if (rig != null) rig.enabled = true;
        if (brain != null) foreach (var b in brain) if (b != null) b.enabled = true;
    });

    // V5 关特效与音频（溅墨池 + 波次生成器 + 音频导演）
    names.Add("V5 关特效/音频（InkHitVfx + WaveSpawner + AudioDirector）");
    applyAll.Add(() =>
    {
        if (hitVfx != null) hitVfx.enabled = false;
        if (spawner != null) spawner.enabled = false;
        if (audio != null) audio.enabled = false;
    });
    undoAll.Add(() =>
    {
        if (hitVfx != null) hitVfx.enabled = true;
        if (audio != null) audio.enabled = true;
        // spawner 保持关闭：它是波次来源，重开会让后续档出现敌人
    });

    // V6 全关（剩余地板 = 渲染管线 + 引擎本身）
    names.Add("V6 以上全关（剩余地板）");
    applyAll.Add(() =>
    {
        for (int i = 1; i < 6; i++) applyAll[i]();
    });
    undoAll.Add(() =>
    {
        if (run != null) run.enabled = true;
        if (panel != null) panel.enabled = true;
        if (choice != null) choice.enabled = true;
        if (ctl != null) ctl.enabled = true;
        if (stance != null) stance.enabled = true;
        if (vfx != null) vfx.enabled = true;
        if (foot != null) foot.enabled = true;
        if (rig != null) rig.enabled = true;
        if (brain != null) foreach (var b in brain) if (b != null) b.enabled = true;
        if (hitVfx != null) hitVfx.enabled = true;
        if (audio != null) audio.enabled = true;
    });

    gSb2.AppendLine();
    gSb2.AppendLine("---------- 各档每帧分配（每档 3.0s，先热身 0.8s）----------");
    gSb2.AppendLine("  档位　　　　　　　　　　　　　　　　　 每帧分配中位　95%　　　窗口合计　gen0　draw call 中位");

    var results = new List<float>();
    for (int v = 0; v < names.Count; v++)
    {
        // ★ 每档前**先全恢复、再只施加本档**。
        //   第一版写成"只恢复本档" ⇒ 上一档关掉的组件会一直留着，档位之间互相污染
        //   （V1 关掉的 RunManager 在 V2~V6 里全程关着，差值全部失真）。
        for (int k = 0; k < undoAll.Count; k++) undoAll[k]();
        applyAll[v]();

        {
            float tw = Time.unscaledTime;
            while (Time.unscaledTime - tw < 0.8f) yield return null;
        }

        int g0a = 0;
        try { g0a = GC.CollectionCount(0); } catch (System.Exception) { }
        var alloc = new List<float>(1024);
        var dr = new List<float>(1024);
        float t0 = Time.unscaledTime;
        while (Time.unscaledTime - t0 < 3.0f)
        {
            if (recGc.Valid) alloc.Add(recGc.LastValue);
            if (recDraw.Valid) dr.Add(recDraw.LastValue);
            yield return null;
        }
        int g0b = 0;
        try { g0b = GC.CollectionCount(0); } catch (System.Exception) { }

        float med = Pct2(alloc, 0.5f);
        results.Add(med);
        gSb2.AppendLine("  " + names[v].PadRight(40)
                       + F2(med / 1024f).PadLeft(8) + " KB"
                       + F2(Pct2(alloc, 0.95f) / 1024f).PadLeft(9) + " KB"
                       + F2(Sum3(alloc) / 1048576f).PadLeft(10) + " MB"
                       + (g0b - g0a).ToString().PadLeft(6)
                       + Pct2(dr, 0.5f).ToString().PadLeft(14));
    }

    // 全部恢复
    for (int k = 0; k < undoAll.Count; k++) undoAll[k]();
    if (spawner != null) spawner.enabled = false;

    // ---- 归因表 ----
    gSb2.AppendLine();
    gSb2.AppendLine("---------- 归因（相对 V0 的差额）----------");
    for (int v = 1; v < names.Count; v++)
    {
        float d = results[0] - results[v];
        string verdict;
        float baseKb = results[0] / 1024f;
        if (Mathf.Abs(d) < 0.5f * 1024f) verdict = "≈ 0：不是它的锅（在噪声量级内）";
        else if (d > 0f) verdict = "贡献 " + F2(d / 1024f) + " KB/帧"
                              + (baseKb > 0.01f ? "（占基线 " + F2(100f * d / Mathf.Max(results[0], 1f)) + " %）" : "");
        else verdict = "★ 反而增加 " + F2(-d / 1024f) + " KB/帧（关掉它分配变多？多半是量测噪声或 GC 时机）";
        gSb2.AppendLine("  " + names[v].PadRight(40) + verdict);
    }

    gSb2.AppendLine();
    gSb2.AppendLine("---------- 判读口径 ----------");
    gSb2.AppendLine("  · 单档 3.0s × ~165fps ≈ 500 帧，中位数足够稳；但差值 <0.5 KB/帧 一律视为噪声。");
    gSb2.AppendLine("  · 若 V6（全关）仍显著 >0 ⇒ 剩余分配来自**渲染管线 / 引擎本身**，");
    gSb2.AppendLine("    那就不是对象池能解决的：改由「减少每帧分配」转为「接受地板值、只削战斗峰值」。");

    File.WriteAllText(gRep2, gSb2.ToString(), new UTF8Encoding(false));
    Debug.Log("[perf_alloc_ab] 报告已落盘 " + gRep2);
    Debug.Log(gSb2.ToString());
    yield break;
}

string Mark(Component c) { return c != null ? "有" : "**无**"; }

float Sum3(List<float> v)
{
    if (v == null) return 0f;
    double s = 0;
    for (int i = 0; i < v.Count; i++) s += v[i];
    return (float)s;
}

return Body2();
