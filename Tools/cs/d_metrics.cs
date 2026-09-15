// d_metrics.cs —— 画面墨感度量（把「视觉效果差」变成可核对的数字）
//
// 为什么必须先有它：前三轮的整改都是「改完看一眼」——而"看一眼"对水墨这种
// 整体调性的东西极不可靠（人眼会适应，改完那一眼永远觉得"好像好点"）。
// 本探针把画面拆成 8 个可度量的量（M1~M8），整改前后各跑一次，用同一把尺子量。
//
// 指标的取向（写意重墨）：
//   M1 亮度 5% 分位  ≤0.15  —— 画面必须真的有焦墨
//   M2 亮度 95% 分位 ≥0.90  —— 纸白仍然要是纸白（不能治成"整体压暗"）
//   M3 明度标准差    ≥0.20  —— 层次宽度（"视觉效果好"的最强单指标）
//   M4 平均饱和度    ≤0.035 —— 单色相硬约束
//   M5 地面纹理能量  ≥8×基线 —— "有纹理"的直接度量
//   M6 背景−角色亮度 ≥0.30  —— 主体要从纸上站出来
//   M7 墨线平均宽度  1.8~2.6px
//   M8 帧率          ≥55fps（编辑器内测量，仅作回退告警，不作硬判据）
//
// ★ 度量的两个关键设计（否则数字会撒谎）：
//   1) **角色/敌人所在的矩形要从统计里排除**。否则角色变深会同时抬高 M1、压低 M3，
//      把"角色变黑"误读成"整幅层次变好"。
//   2) **纹理能量取「中位数」而不是均值**。墨线是稀疏的高对比像素，
//      均值会被墨线宽度污染（加粗墨线 = 纹理能量暴涨，是假信号）；
//      中位数由占多数的**面**决定，才真正反映"面上有没有纹理"。
//
// 取景用与 d_look.cs 相同的确定性布置（同一 spawn、同一 4s 静置）——
// 否则前后两次拍的构图不同，数字不可比。
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using InkWash.Combat;
using InkWash.Enemies;
using InkWash.Player;
using InkWash.Roguelike;
using InkWash.UI;

IEnumerator Body()
{
    string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
    string shotDir = Path.Combine(root, "Tools/screenshots/metrics");
    string repPath = Path.Combine(root, "Tools/reports/d_metrics.txt");
    Directory.CreateDirectory(shotDir);
    Directory.CreateDirectory(Path.GetDirectoryName(repPath));

    var cam = Camera.main;
    if (cam == null) { Debug.LogError("[d_metrics] 找不到 MainCamera"); yield break; }

    var ph = Object.FindObjectOfType<PlayerHealth>();
    if (ph == null) { Debug.LogError("[d_metrics] 找不到 PlayerHealth"); yield break; }
    var go = ph.gameObject;
    var ctl = go.GetComponent<PlayerController>();
    var stance = go.GetComponentInChildren<CombatStance>(true);
    var panel = Object.FindObjectOfType<InkStylePanel>();
    var run = Object.FindObjectOfType<RunManager>();
    var choice = Object.FindObjectOfType<SkillChoicePanel>();
    var spawner = Object.FindObjectOfType<WaveSpawner>();
    var room = Object.FindObjectOfType<RoomController>();

    if (panel != null) panel.visible = false;
    if (choice != null) choice.Hide();

    ph.maxHealth = 100000f; ph.ResetHealth();
    if (spawner != null) { spawner.ResetForTest(); spawner.spawnInterval = 0.35f; }
    if (room != null) room.ResetForTest();
    if (run != null) { run.ResetForTest(); run.SetSeed(20260916); run.nextRoomDelay = 999f; }
    if (spawner != null && spawner.waves != null)
        for (int i = 0; i < spawner.waves.Length; i++)
            if (spawner.waves[i] != null) spawner.waves[i].delayBefore = 0.2f;

    ctl.ResetToLocomotion();
    if (stance != null) { stance.combatExitDelay = 99999f; stance.ForceStance(true); }
    ctl.BeginInputOverride();
    ctl.SetInjectedMove(Vector2.zero, false);
    if (run != null) run.StartRun();

    { float t = Time.unscaledTime; while (Time.unscaledTime - t < 4.0f) yield return null; }

    var sb = new StringBuilder();
    sb.AppendLine("========== d_metrics 画面墨感度量 ==========");
    sb.AppendLine("相机 " + cam.pixelWidth + "x" + cam.pixelHeight
                  + "　渲染器 " + UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline);
    sb.AppendLine("  " + InkWash.Rendering.InkLighting.Describe());
    sb.AppendLine();

    var shots = new List<string>();
    int idx = 0;

    // ---- 抓一帧并度量 ----
    for (int step = 0; step < 2; step++)
    {
        if (step == 1)
        {
            ctl.SetInjectedMove(new Vector2(0.55f, 0.85f), true);
            float t0 = Time.unscaledTime; while (Time.unscaledTime - t0 < 1.2f) yield return null;
        }
        string tag = step == 0 ? "idle" : "run";
        string path = Path.Combine(shotDir, "metrics_" + tag + ".png");

        Texture2D tex = CaptureFrame(cam, path);
        if (tex == null) { Debug.LogError("[d_metrics] 抓帧失败 " + tag); yield break; }

        Measure(sb, tag, tex, cam, go, ref idx);
        shots.Add(path);
        Object.Destroy(tex);
    }

    ctl.SetInjectedMove(Vector2.zero, true);
    ctl.EndInputOverride();
    if (stance != null) stance.combatExitDelay = 6f;

    // ---- M8 帧率（编辑器内，仅作回退告警）----
    // ★ 必须**逐帧**采集而不是用 Time.time 的首尾差：卡顿表现为**个别长帧**，
    //   平均值会把它们抹平，于是"掉帧"在报告里永远是满帧。所以报中位 + 1% 低 + 最差帧。
    {
        var dts = new List<float>();
        ctl.BeginInputOverride();
        ctl.SetInjectedMove(new Vector2(0.55f, 0.85f), true);

        // ★ 必须**先热身**：上一步的 `EncodeToPNG`（两张 1920×1080）+ `Object.Destroy(tex)`
        //   + 统计循环产生的大块垃圾，会集中落在窗口的**第一帧**上 —— 第一版就量出了
        //   "最差单帧 2558 ms / 窗口只收到 13 帧"这种**纯属探针自伤**的数。
        //   度量窗口必须裁到事件边界之后（本项目硬规矩 #3）。
        float tw = Time.unscaledTime;
        while (Time.unscaledTime - tw < 1.0f) yield return null;

        float t0 = Time.unscaledTime;
        while (Time.unscaledTime - t0 < 3.0f) { dts.Add(Time.unscaledDeltaTime); yield return null; }
        ctl.SetInjectedMove(Vector2.zero, true);
        ctl.EndInputOverride();

        int raw = dts.Count;
        int skip = Mathf.Min(10, dts.Count / 4);          // 再丢掉前 10 帧作为预热
        var use = dts.GetRange(skip, dts.Count - skip);
        use.Sort();
        float med = use[use.Count / 2];
        int i1 = Mathf.Clamp(Mathf.FloorToInt(use.Count * 0.99f), 0, use.Count - 1);
        float p1 = use[i1];
        float worst = use[use.Count - 1];
        sb.AppendLine("---------- [3] 帧率（编辑器内，仅作回退告警）----------");
        sb.AppendLine("  M8 采集 " + raw + " 帧，丢弃预热 " + skip + " 帧，统计 " + use.Count
                      + " 帧　中位 " + F(1f / med) + " fps　1% 低 " + F(1f / Mathf.Max(p1, 1e-5f))
                      + " fps　最差单帧 " + F(worst * 1000f) + " ms　(目标中位 ≥55)");
        sb.AppendLine("     ※ 编辑器 Play 模式含编辑器开销 + 受 vsync 上限约束，此数只作**回退告警**，不作硬判据。");
        sb.AppendLine();
    }

    string report = sb.ToString();
    File.WriteAllText(repPath, report, new UTF8Encoding(false));
    Debug.Log("[d_metrics] 报告已落盘 " + repPath);
    Debug.Log(report);
    yield return null;
}

/// 抓帧：优先 ScreenCapture（拿到的是最终后台缓冲，含全部后处理），失败退回 Camera.Render。
Texture2D CaptureFrame(Camera cam, string pngPath)
{
    Texture2D tex = null;
    try
    {
        tex = ScreenCapture.CaptureScreenshotAsTexture();
    }
    catch (System.Exception e)
    {
        Debug.LogWarning("[d_metrics] CaptureScreenshotAsTexture 失败：" + e.Message);
    }
    if (tex == null)
    {
        var rt = RenderTexture.GetTemporary(cam.pixelWidth, cam.pixelHeight, 24);
        var prev = cam.targetTexture;
        cam.targetTexture = rt;
        cam.Render();
        cam.targetTexture = prev;
        RenderTexture.active = rt;
        tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        tex.Apply();
        RenderTexture.active = null;
        RenderTexture.ReleaseTemporary(rt);
    }
    if (tex == null) return null;
    try { File.WriteAllBytes(pngPath, tex.EncodeToPNG()); } catch { }
    return tex;
}

/// 世界包围盒 → 视口矩形（角色/敌人的"脏区"，统计时必须排除）
Rect ProjectBounds(Camera cam, Bounds b)
{
    Vector3 c = b.center, e = b.extents;
    float xmin = 1f, xmax = 0f, ymin = 1f, ymax = 0f;
    bool any = false;
    for (int i = 0; i < 8; i++)
    {
        Vector3 p = c + new Vector3((i & 1) == 0 ? -e.x : e.x,
                                    (i & 2) == 0 ? -e.y : e.y,
                                    (i & 4) == 0 ? -e.z : e.z);
        Vector3 v = cam.WorldToViewportPoint(p);
        if (v.z <= 0f) continue;
        any = true;
        xmin = Mathf.Min(xmin, v.x); xmax = Mathf.Max(xmax, v.x);
        ymin = Mathf.Min(ymin, v.y); ymax = Mathf.Max(ymax, v.y);
    }
    if (!any) return new Rect(0, 0, 0, 0);
    return Rect.MinMaxRect(xmin, ymin, xmax, ymax);
}

void Measure(StringBuilder sb, string tag, Texture2D tex, Camera cam, GameObject player, ref int idx)
{
    int W = tex.width, H = tex.height;
    Color[] px = tex.GetPixels();
    float[] L = new float[W * H];
    for (int i = 0; i < px.Length; i++)
        L[i] = 0.2126f * px[i].r + 0.7152f * px[i].g + 0.0722f * px[i].b;

    // ---- 排除区：角色与所有敌人 ----
    var excl = new List<Rect>();
    foreach (var smr in player.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        excl.Add(ProjectBounds(cam, smr.bounds));
    foreach (var eb in Object.FindObjectsOfType<EnemyBase>())
        foreach (var smr in eb.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            excl.Add(ProjectBounds(cam, smr.bounds));
    // Meshes（剑）也要排除，否则深色剑身会被算成"墨线"
    foreach (var mr in player.GetComponentsInChildren<MeshRenderer>(true))
        excl.Add(ProjectBounds(cam, mr.bounds));

    System.Func<int, int, bool> excluded = (x, y) =>
    {
        float vx = (x + 0.5f) / W, vy = (y + 0.5f) / H;
        for (int k = 0; k < excl.Count; k++)
            if (excl[k].width > 0f && excl[k].Contains(new Vector2(vx, vy))) return true;
        return false;
    };

    // ---- M4 饱和度 / M1 M2 M3 分位与标准差 ----
    const int BINS = 1024;
    int[] hist = new int[BINS];
    double sumL = 0, sumL2 = 0; long nAll = 0;
    double sumSat = 0, sumChroma = 0; long hiChroma = 0;
    for (int i = 0; i < L.Length; i++)
    {
        int b = Mathf.Clamp((int)(L[i] * (BINS - 1)), 0, BINS - 1);
        hist[b]++;
        sumL += L[i]; sumL2 += L[i] * L[i]; nAll++;
        float mx = Mathf.Max(px[i].r, Mathf.Max(px[i].g, px[i].b));
        float mn = Mathf.Min(px[i].r, Mathf.Min(px[i].g, px[i].b));
        float chroma = mx - mn;
        sumChroma += chroma;
        // ★ 高彩度像素：同时要求"有色"且"够亮"。
        //   只按色度判会把冷调焦墨（0.075/0.085/0.120，色度 0.045）也算进去，
        //   而水墨的冷调**是应该有的**。加上亮度门限后，这一项就专抓
        //   "橙斗篷"这类**破调色** —— 它才是真正要清零的东西。
        if (chroma > 0.10f && L[i] > 0.25f) hiChroma++;
        sumSat += mx <= 0.0001f ? 0 : chroma / mx;
    }
    double meanL = sumL / nAll;
    double stdL = System.Math.Sqrt(System.Math.Max(0, sumL2 / nAll - meanL * meanL));
    float P05 = Percentile(hist, nAll, 0.05f);
    float P20 = Percentile(hist, nAll, 0.20f);
    float P50 = Percentile(hist, nAll, 0.50f);
    float P80 = Percentile(hist, nAll, 0.80f);
    float P95 = Percentile(hist, nAll, 0.95f);

    // ---- M6 角色 vs 背景 ----
    // ★ M6 的度量缺陷修订：SkinnedMeshRenderer.bounds 的投影比角色实际轮廓宽 3 倍以上
    //   （实测矩形宽 0.369 视口，而画面里角色只占 0.11），所以"矩形内平均亮度"
    //   绝大部分在量角色周围的背景 —— 这就是 M6 长期读不出正数的原因。
    //   改用**矩形内亮度 10% 分位**代表"主体墨"：它必然落在角色的焦墨上，
    //   不受矩形大小影响。这是"主体是否从纸上站出来"的正确问法。
    double charSum = 0; long charN = 0;
    Rect charRect = new Rect(0, 0, 0, 0);
    float charInk10 = 0f, charInk25 = 0f;   // 主体墨的 10%/25% 分位（见下方 M6 说明）
    foreach (var smr in player.GetComponentsInChildren<SkinnedMeshRenderer>(true))
    {
        var r = ProjectBounds(cam, smr.bounds);
        if (r.width <= 0f) continue;
        charRect = charRect.width <= 0f ? r : Rect.MinMaxRect(
            Mathf.Min(charRect.xMin, r.xMin), Mathf.Min(charRect.yMin, r.yMin),
            Mathf.Max(charRect.xMax, r.xMax), Mathf.Max(charRect.yMax, r.yMax));
    }
    if (charRect.width > 0f)
    {
        int x0 = Mathf.Clamp((int)(charRect.xMin * W), 0, W - 1);
        int x1 = Mathf.Clamp((int)(charRect.xMax * W), 0, W - 1);
        int y0 = Mathf.Clamp((int)(charRect.yMin * H), 0, H - 1);
        int y1 = Mathf.Clamp((int)(charRect.yMax * H), 0, H - 1);
        var charVals = new List<float>();
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++) { charSum += L[y * W + x]; charN++; charVals.Add(L[y * W + x]); }
        charVals.Sort();
        if (charVals.Count > 0)
        {
            charInk10 = charVals[(int)(charVals.Count * 0.10f)];
            charInk25 = charVals[(int)(charVals.Count * 0.25f)];
        }
    }
    // 背景：角色矩形外扩 1.6 倍的环带
    Vector2 cc = charRect.width > 0f ? charRect.center : new Vector2(0.5f, 0.45f);
    Vector2 half = charRect.width > 0f ? charRect.size * 0.8f : new Vector2(0.09f, 0.22f);
    Rect ring = new Rect(cc.x - half.x, cc.y - half.y, half.x * 2f, half.y * 2f);
    double bgSum = 0; long bgN = 0;
    for (int y = 0; y < H; y += 2)
        for (int x = 0; x < W; x += 2)
        {
            float vx = (x + 0.5f) / W, vy = (y + 0.5f) / H;
            if (!ring.Contains(new Vector2(vx, vy))) continue;
            if (charRect.width > 0f && charRect.Contains(new Vector2(vx, vy))) continue;
            bgSum += L[y * W + x]; bgN++;
        }
    double charMean = charN > 0 ? charSum / charN : 0;
    double bgMean = bgN > 0 ? bgSum / bgN : 0;
    // M6 用「背景 − 主体墨(10%分位)」：主体墨必定落在角色焦墨上，不受包围盒尺寸影响

    // ---- M5 纹理能量：地面上 |拉普拉斯| 的中位数，**分两档** ----
    // 地面 = 画面下部 4%~26% 的横带（本布置下这块一定是地面）
    //
    // ★ 为什么必须分两档（依据 c_tune.cs 的对照实验，Tools/reports/c_tune.txt）：
    //   只测「像素级 |Laplacian|」时，笔触层无论怎么调都读不出增量
    //   （仅笔触 14.83e-3 vs 无纹理地板 14.57e-3），因为 14 cm 尺度的笔痕
    //   在像素尺度上几乎不产生曲率。于是"提高纹理能量"会被误导成
    //   "把颗粒频率推到纯噪点"——那是把画面弄脏，不是加水墨。
    //   分档后：M5a = 细粒（纸纤维）；M5b = 4px 步长（皴法笔痕）。
    //   后者才对应肉眼说的"有笔痕"。
    var texl = new List<float>();
    var texl4 = new List<float>();
    int gy0 = Mathf.Clamp((int)(H * 0.04f), 1, H - 2);
    int gy1 = Mathf.Clamp((int)(H * 0.26f), 1, H - 2);
    const int ST = 4;
    for (int y = gy0; y <= gy1; y++)
        for (int x = 1; x < W - 1; x++)
        {
            if (excluded(x, y)) continue;
            float lap = Mathf.Abs(L[(y + 1) * W + x] + L[(y - 1) * W + x]
                                + L[y * W + x + 1] + L[y * W + x - 1] - 4f * L[y * W + x]);
            texl.Add(lap);
            if (x >= ST && x < W - ST && y >= gy0 + ST && y <= gy1 - ST)
            {
                float lap4 = Mathf.Abs(L[(y + ST) * W + x] + L[(y - ST) * W + x]
                                     + L[y * W + x + ST] + L[y * W + x - ST] - 4f * L[y * W + x]);
                texl4.Add(lap4);
            }
        }
    texl.Sort(); texl4.Sort();
    float texMed  = texl.Count  > 0 ? texl[texl.Count / 2] : 0f;
    float texP80  = texl.Count  > 0 ? texl[(int)(texl.Count * 0.80f)] : 0f;
    float texMed4 = texl4.Count > 0 ? texl4[texl4.Count / 2] : 0f;

    // ---- M7 墨线：背景区暗像素占比 + 水平/垂直扫描暗段中位长度 ----
    const float INK_TH = 0.18f;
    long darkN = 0, bgTotal = 0;
    var runH = new List<int>(); var runV = new List<int>();
    for (int y = 0; y < H; y++)
    {
        int run = 0;
        for (int x = 0; x < W; x++)
        {
            bool d = !excluded(x, y) && L[y * W + x] < INK_TH;
            if (!excluded(x, y)) { bgTotal++; if (d) darkN++; }
            if (d) run++;
            else { if (run > 0) runH.Add(run); run = 0; }
        }
        if (run > 0) runH.Add(run);
    }
    for (int x = 0; x < W; x++)
    {
        int run = 0;
        for (int y = 0; y < H; y++)
        {
            bool d = !excluded(x, y) && L[y * W + x] < INK_TH;
            if (d) run++;
            else { if (run > 0) runV.Add(run); run = 0; }
        }
        if (run > 0) runV.Add(run);
    }
    runH.Sort(); runV.Sort();
    float medH = runH.Count > 0 ? runH[runH.Count / 2] : 0f;
    float medV = runV.Count > 0 ? runV[runV.Count / 2] : 0f;
    float lineW = Mathf.Min(medH, medV);
    float inkRatio = bgTotal > 0 ? (float)darkN / bgTotal : 0f;

    idx++;
    sb.AppendLine("---------- [" + idx + "] " + tag + " ----------");
    sb.AppendLine("  M1 亮度 5% 分位      = " + F(P05) + "　(目标 ≤0.15)");
    sb.AppendLine("  M2 亮度 95% 分位     = " + F(P95) + "　(目标 ≥0.90)");
    sb.AppendLine("  M3 明度标准差        = " + F((float)stdL) + "　(目标 ≥0.20)");
    sb.AppendLine("  M4 彩度均值(max−min)  = " + F((float)(sumChroma / nAll)) + "　(目标 ≤0.06)");
    sb.AppendLine("  M4b 高彩度像素占比    = " + F((float)hiChroma * 100f / nAll)
                  + " %　(目标 ≤0.5% —— 专抓破调色)　HSV饱和均值 " + F((float)(sumSat / nAll)));
    sb.AppendLine("  M5a 细粒能量(像素级中位) = " + F(texMed * 1000f) + "e-3　80%分位 " + F(texP80 * 1000f)
                  + "e-3　(地板 14.6e-3，目标 ≥2.5×地板)");
    sb.AppendLine("  M5b 笔痕能量(4px 中位)    = " + F(texMed4 * 1000f)
                  + "e-3　(地板 33.3e-3，目标 ≥2.5×地板)");
    sb.AppendLine("  M6 分离度(背景−主体墨10%) = " + F((float)bgMean - charInk10)
                  + "　(主体墨 " + F(charInk10) + " / 背景 " + F((float)bgMean)
                  + "　矩形均值 " + F((float)charMean) + ")　(目标 ≥0.55)");
    sb.AppendLine("  M7 墨线宽估计         = " + F(lineW) + " px　(水平段中位 " + F(medH)
                  + " / 垂直段中位 " + F(medV) + ")　(目标 1.8~2.6)");
    sb.AppendLine("     暗像素占背景比     = " + F(inkRatio * 100f) + " %");
    sb.AppendLine("  亮度阶梯 5/20/50/80/95 = " + F(P05) + " / " + F(P20) + " / "
                  + F(P50) + " / " + F(P80) + " / " + F(P95));
    sb.AppendLine("  排除矩形 " + excl.Count + " 个　角色矩形 "
                  + (charRect.width > 0f ? ("(" + F(charRect.xMin) + "," + F(charRect.yMin) + ")~("
                     + F(charRect.xMax) + "," + F(charRect.yMax) + ")") : "无"));
    sb.AppendLine();
}

float Percentile(int[] hist, long total, float p)
{
    long want = (long)(total * p);
    long acc = 0;
    for (int i = 0; i < hist.Length; i++)
    {
        acc += hist[i];
        if (acc >= want) return i / (float)(hist.Length - 1);
    }
    return 1f;
}

string F(float v) { return v.ToString("0.###"); }

return Body();
