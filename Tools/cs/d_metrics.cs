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

// ===== M6/M7/M9 的「角色本体精确掩码」共用槽 =====
// ★ 为什么要把这些东西放在**方法体最外层**：本地函数（local function）只能捕获
//   在它**声明之前**就可见的局部变量。Measure() 定义在后面，所以要在这里先声明。
bool gMaskOk = false;
bool[] gMask = null;          // 长度 = W*H，true = 该像素属于角色本体
int gMx0, gMy0, gMx1, gMy1;   // 掩码像素包围盒（闭区间）
Texture2D gMain = null;       // 同帧「真彩」帧（与掩码背靠背渲出 ⇒ 几何完全一致）
Texture2D gNoChroma = null;   // 同帧「墨彩关」帧（_ChromaKeep=0）
Texture2D gNoOutline = null;  // 同帧「轮廓关」帧（_OutlineWidth=0）
string gRegionInfo = "";      // 掩码/材质读回的实况（写进报告，便于复核）
float gKeepShip = 0f, gKeepRead = 0f, gOutShip = 0f;
string gShotDir = "";         // 截图目录（Measure 里存角色近景用）
string gCloseupPath = "";     // 最后一张近景路径（写进报告）

IEnumerator Body()
{
    string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
    string shotDir = Path.Combine(root, "Tools/screenshots/metrics");
    string repPath = Path.Combine(root, "Tools/reports/d_metrics.txt");
    Directory.CreateDirectory(shotDir);
    Directory.CreateDirectory(Path.GetDirectoryName(repPath));
    gShotDir = shotDir;

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

        // ---- 角色本体掩码 + 同帧对照组（全部在**同一个 tick** 内背靠背渲完）----
        // ★ 掩码手法与 e_tonality 一致：把角色本体材质整体换成 URP/Unlit 品红渲一次。
        //   换的是**整个 sharedMaterials 数组**，所以墨线 Pass（第二个 pass）根本没参与
        //   渲染 ⇒ 掩码 = **纯剪影**，不含外扩壳。这一点很关键：M7 要量的正是
        //   "剪影外侧那圈墨线"，掩码必须先把它排除掉。
        // ★ 四帧必须同帧：Camera.Render() 可在同一 tick 连续调用、几何完全一致；
        //   一旦 yield，Idle 动画就会走动，掩码和直测帧就错开 1~2 px。
        BuildRegionFrames(cam, go);
        Measure(sb, tag, tex, cam, go, ref idx);
        shots.Add(path);
        Object.Destroy(tex);
        if (gMain != null) { Object.Destroy(gMain); gMain = null; }
        if (gNoChroma != null) { Object.Destroy(gNoChroma); gNoChroma = null; }
        if (gNoOutline != null) { Object.Destroy(gNoOutline); gNoOutline = null; }
        gMask = null; gMaskOk = false;
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

/// 单帧渲染到 RT（含 URP 后处理：Camera.Render 走的是完整 URP 管线）
Texture2D ShootFrame(Camera cam, int W, int H)
{
    var rt = RenderTexture.GetTemporary(W, H, 24, RenderTextureFormat.ARGB32);
    var prevT = cam.targetTexture;
    var prevA = RenderTexture.active;
    cam.targetTexture = rt;
    cam.Render();
    cam.targetTexture = prevT;
    RenderTexture.active = rt;
    var t = new Texture2D(W, H, TextureFormat.RGBA32, false);
    t.ReadPixels(new Rect(0, 0, W, H), 0, 0);
    t.Apply();
    RenderTexture.active = prevA;
    RenderTexture.ReleaseTemporary(rt);
    return t;
}

/// 采集「角色本体掩码」+ 三张同帧对照帧（真彩 / 墨彩关 / 轮廓关）。
/// 全程无 yield ⇒ 四帧几何一致，掩码与直测帧像素对齐。
void BuildRegionFrames(Camera cam, GameObject go)
{
    gMaskOk = false; gMask = null; gRegionInfo = "";
    gMain = null; gNoChroma = null; gNoOutline = null;
    gKeepShip = 0f; gKeepRead = 0f; gOutShip = 0f;

    int W = cam.pixelWidth, H = cam.pixelHeight;
    if (W <= 8 || H <= 8) { gRegionInfo = "分辨率异常 " + W + "x" + H; return; }

    // ---- 收集角色**本体**的材质槽（排除剑：剑的墨线不属于"人物外轮廓"）----
    var rends = new List<Renderer>();
    var mats = new List<Material>();
    var origArr = new Dictionary<Renderer, Material[]>();
    var seen = new HashSet<Renderer>();
    foreach (var r in go.GetComponentsInChildren<Renderer>(true))
    {
        if (r == null || seen.Contains(r)) continue;
        seen.Add(r);
        var ms = r.sharedMaterials;
        if (ms == null || ms.Length == 0) continue;
        if (ms[0] != null && ms[0].name.IndexOf("Sword", System.StringComparison.OrdinalIgnoreCase) >= 0) continue;
        bool any = false;
        for (int i = 0; i < ms.Length; i++) if (ms[i] != null && ms[i].HasProperty("_BandBias")) any = true;
        if (!any) continue;
        origArr[r] = ms;
        rends.Add(r);
        for (int i = 0; i < ms.Length; i++) if (ms[i] != null && ms[i].HasProperty("_BandBias")) mats.Add(ms[i]);
    }
    if (mats.Count == 0) { gRegionInfo = "找不到角色本体材质（无 _BandBias 槽）"; return; }

    var sb2 = new StringBuilder();

    // ---- ① 掩码帧 ----
    var unlit = Shader.Find("Universal Render Pipeline/Unlit");
    if (unlit == null) { gRegionInfo = "找不到 URP/Unlit，掩码不可用"; return; }
    var magenta = new Material(unlit);
    magenta.SetColor("_BaseColor", new Color(1f, 0f, 1f, 1f));
    magenta.SetTexture("_BaseMap", Texture2D.whiteTexture);
    var one = new Material[1] { magenta };
    foreach (var r in rends) r.sharedMaterials = one;
    var tMask = ShootFrame(cam, W, H);
    foreach (var kv in origArr) if (kv.Key != null) kv.Key.sharedMaterials = kv.Value;
    Object.Destroy(magenta);

    if (tMask != null)
    {
        var mp = tMask.GetPixels32();
        var msk = new bool[W * H];
        long n = 0;
        for (int i = 0; i < msk.Length; i++)
        {
            // 品红判据用差值而不是绝对通道值：后处理的色调映射会把 (1,0,1) 压成
            // (0.9,0.16,0.85) 之类，但"红蓝都远高于绿"这个关系不会变。
            if ((mp[i].r - mp[i].g) > 30 && (mp[i].b - mp[i].g) > 30) { msk[i] = true; n++; }
        }
        if (n > 200)
        {
            int x0 = W, y0 = H, x1 = -1, y1 = -1;
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                    if (msk[y * W + x])
                    {
                        if (x < x0) x0 = x; if (x > x1) x1 = x;
                        if (y < y0) y0 = y; if (y > y1) y1 = y;
                    }
            gMask = msk; gMaskOk = true;
            gMx0 = x0; gMy0 = y0; gMx1 = x1; gMy1 = y1;
            sb2.Append("掩码 px " + n + " 包围盒 (" + x0 + "," + y0 + ")~(" + x1 + "," + y1 + ")");
        }
        else sb2.Append("掩码为空（品红被后处理吃掉了？n=" + n + "）");
        Object.Destroy(tMask);
    }
    else sb2.Append("掩码帧渲染失败");

    // ---- ② 真彩帧 ----
    gMain = ShootFrame(cam, W, H);

    // ---- ③ 墨彩关帧（同帧 A/B：证明 _ChromaKeep 到底贡献了多少色度）----
    {
        var back = new float[mats.Count];
        var applied = new List<int>();
        for (int i = 0; i < mats.Count; i++)
            if (mats[i].HasProperty("_ChromaKeep"))
            {
                back[i] = mats[i].GetFloat("_ChromaKeep");
                if (applied.Count == 0) gKeepShip = back[i];
                mats[i].SetFloat("_ChromaKeep", 0f);
                applied.Add(i);
            }
        if (applied.Count > 0)
        {
            // ★ HasProperty 只证明 shader **声明**了它，证明不了写入生效 ⇒ 读回复核
            gKeepRead = mats[applied[0]].GetFloat("_ChromaKeep");
            gNoChroma = ShootFrame(cam, W, H);
            foreach (var i in applied) mats[i].SetFloat("_ChromaKeep", back[i]);
        }
        else sb2.Append("　（材质无 _ChromaKeep，墨彩 A/B 跳过）");
    }

    // ---- ④ 轮廓关帧（同帧 A/B：证明 M7 量到的暗游程真的来自墨线外扩壳）----
    {
        var back = new float[mats.Count];
        var applied = new List<int>();
        for (int i = 0; i < mats.Count; i++)
            if (mats[i].HasProperty("_OutlineWidth"))
            {
                back[i] = mats[i].GetFloat("_OutlineWidth");
                if (applied.Count == 0) gOutShip = back[i];
                mats[i].SetFloat("_OutlineWidth", 0f);
                applied.Add(i);
            }
        if (applied.Count > 0)
        {
            gNoOutline = ShootFrame(cam, W, H);
            foreach (var i in applied) mats[i].SetFloat("_OutlineWidth", back[i]);
        }
    }

    sb2.Append("　_ChromaKeep 出厂 " + F(gKeepShip) + " → 置0读回 " + F(gKeepRead)
               + "　_OutlineWidth 出厂 " + F(gOutShip));
    gRegionInfo = sb2.ToString();
}

/// 把角色矩形附近裁出来放大 2× 存盘（用肉眼看轮廓粗细）
void SaveCloseup(Texture2D tex, int x0, int y0, int x1, int y1, string path)
{
    if (tex == null) return;
    int pad = 26;
    x0 = Mathf.Clamp(x0 - pad, 0, tex.width - 1); x1 = Mathf.Clamp(x1 + pad, 0, tex.width - 1);
    y0 = Mathf.Clamp(y0 - pad, 0, tex.height - 1); y1 = Mathf.Clamp(y1 + pad, 0, tex.height - 1);
    int cw = x1 - x0 + 1, ch = y1 - y0 + 1;
    if (cw < 8 || ch < 8) return;
    var src = tex.GetPixels(x0, y0, cw, ch);
    int ow = cw * 2, oh = ch * 2;
    var dst = new Color[ow * oh];
    for (int y = 0; y < oh; y++)
        for (int x = 0; x < ow; x++)
            dst[y * ow + x] = src[(y / 2) * cw + (x / 2)];
    var t2 = new Texture2D(ow, oh, TextureFormat.RGBA32, false);
    t2.SetPixels(dst); t2.Apply();
    try { File.WriteAllBytes(path, t2.EncodeToPNG()); } catch { }
    Object.Destroy(t2);
}

/// 从本体剪影的每行/每列端点**向外**扫暗游程，收集长度（= 墨线厚度采样）。
/// 只扫描影外侧 ⇒ 场景物体的轮廓进不来。
/// ★ 撞上 MAXSCAN 上限的一律**丢弃**而不是记成 24：撞上限说明那不是"一条线"而是
///   扫进了一片黑（前科：`_OutlineWidth=0.038` 时真线宽 13.7 px，MAXSCAN=14 刚好
///   把真值截成 14，报告里看着像"正常"，其实是量程不够）。
void OutlineRuns(bool[] mask, float[] Lf, int W, int H,
                 int mx0, int my0, int mx1, int my1, List<int> rh, List<int> rv, float inkTh)
{
    const int MAXSCAN = 24;
    for (int y = my0; y <= my1; y++)
    {
        int xl = -1, xr = -1;
        for (int x = mx0; x <= mx1; x++) if (mask[y * W + x]) { xl = x; break; }
        for (int x = mx1; x >= mx0; x--) if (mask[y * W + x]) { xr = x; break; }
        if (xl < 0) continue;
        int n = 0;
        for (int k = 1; k <= MAXSCAN && xl - k >= 0 && Lf[y * W + xl - k] < inkTh; k++) n++;
        if (n > 0 && n < MAXSCAN) rh.Add(n);
        n = 0;
        for (int k = 1; k <= MAXSCAN && xr + k < W && Lf[y * W + xr + k] < inkTh; k++) n++;
        if (n > 0 && n < MAXSCAN) rh.Add(n);
    }
    for (int x = mx0; x <= mx1; x++)
    {
        int yb = -1, yt = -1;
        for (int y = my0; y <= my1; y++) if (mask[y * W + x]) { yb = y; break; }
        for (int y = my1; y >= my0; y--) if (mask[y * W + x]) { yt = y; break; }
        if (yb < 0) continue;
        int n = 0;
        for (int k = 1; k <= MAXSCAN && yb - k >= 0 && Lf[(yb - k) * W + x] < inkTh; k++) n++;
        if (n > 0 && n < MAXSCAN) rv.Add(n);
        n = 0;
        for (int k = 1; k <= MAXSCAN && yt + k < H && Lf[(yt + k) * W + x] < inkTh; k++) n++;
        if (n > 0 && n < MAXSCAN) rv.Add(n);
    }
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

    // ---- M6 角色 vs 背景（口径 v2：以**本体精确掩码**为准）----
    // ★ 旧口径的两个缺陷：
    //   · `SkinnedMeshRenderer.bounds` 的投影比角色实际轮廓宽 3 倍以上
    //     （实测矩形宽 0.369 视口，而画面里角色只占 0.11）⇒ "矩形内 10% 分位"
    //     里混进大量背景，主体墨被系统性高估；
    //   · "矩形外扩 1.6 倍的环带"其实悬在离角色几百像素的地方 ⇒ 报的是远处墙面的
    //     亮度，而不是"角色身边的背景"。场景加了深色树石后它自然下滑，M6 就从
    //     0.56 掉到 0.51 —— **那是尺子在动，不是画面在变**。
    // 修订后问两个真正想知道的量：
    //   · 主体墨   = **本体像素**亮度的 10% 分位（主体的最深处）
    //   · 紧邻背景 = 本体包围盒内、**非本体**像素亮度的**中位**（= 角色身边的"纸"）
    // ★ 背景取**中位**而不是均值：包围盒里的非本体像素包含角色**自己的投影**与
    //   脚下杂物，它们是少数派但很深，会把均值拽下去（实测均值 0.589 / 中位 0.67）。
    //   要问的是"主体比身边的纸暗多少"，纸是多数派，所以该用中位。
    bool maskOk = gMaskOk && gMask != null && gMask.Length == W * H;
    Rect charRect = new Rect(0, 0, 0, 0);
    double charSum = 0; long charN = 0;
    float charInk10 = 0f, charInk25 = 0f;
    double bgSum = 0; long bgN = 0;
    double charMean = 0, bgMean = 0, bgMed = 0;
    if (maskOk)
    {
        var cv = new List<float>();
        var bv = new List<float>();
        for (int y = gMy0; y <= gMy1; y++)
            for (int x = gMx0; x <= gMx1; x++)
            {
                int i = y * W + x;
                if (gMask[i]) { cv.Add(L[i]); charSum += L[i]; charN++; }
                else { bv.Add(L[i]); bgSum += L[i]; bgN++; }
            }
        cv.Sort(); bv.Sort();
        if (cv.Count > 0)
        {
            charInk10 = cv[(int)(cv.Count * 0.10f)];
            charInk25 = cv[(int)(cv.Count * 0.25f)];
        }
        if (bv.Count > 0) bgMed = bv[bv.Count / 2];
        charRect = new Rect(gMx0 / (float)W, gMy0 / (float)H,
                            (gMx1 - gMx0 + 1) / (float)W, (gMy1 - gMy0 + 1) / (float)H);
        charMean = charN > 0 ? charSum / charN : 0;
        bgMean = bgN > 0 ? bgSum / bgN : 0;
    }
    else
    {
        // 掩码不可用时的兜底：退回"包围盒矩形 + 1.6× 环带"（口径较差，至少不为空）
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
            var cv = new List<float>();
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++) { charSum += L[y * W + x]; charN++; cv.Add(L[y * W + x]); }
            cv.Sort();
            if (cv.Count > 0)
            {
                charInk10 = cv[(int)(cv.Count * 0.10f)];
                charInk25 = cv[(int)(cv.Count * 0.25f)];
            }
            charMean = charN > 0 ? charSum / charN : 0;
            Vector2 cc = charRect.center;
            Vector2 half = charRect.size * 0.8f;
            var bv = new List<float>();
            for (int y = 0; y < H; y += 2)
                for (int x = 0; x < W; x += 2)
                {
                    float vx = (x + 0.5f) / W, vy = (y + 0.5f) / H;
                    if (!new Rect(cc.x - half.x, cc.y - half.y, half.x * 2f, half.y * 2f).Contains(new Vector2(vx, vy))) continue;
                    if (charRect.Contains(new Vector2(vx, vy))) continue;
                    bv.Add(L[y * W + x]); bgSum += L[y * W + x]; bgN++;
                }
            bv.Sort();
            if (bv.Count > 0) bgMed = bv[bv.Count / 2];
            bgMean = bgN > 0 ? bgSum / bgN : 0;
        }
    }
    double bgRef = maskOk ? bgMed : bgMean;

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

    // ---- M7 角色墨线宽度（口径 v2：只量**贴着本体剪影外侧**的暗游程）----
    // ★ 旧口径统计"全屏所有非排除区"的暗游程。场景几何加了墨线 Pass 之后，
    //   高台 / 柱子 / 墙脚 / 石门 / 石块的轮廓全部混进来 —— 读数从"角色墨线"变成了
    //   "随便谁的墨线"（水平段中位直接跳到 6px）。而本指标原本要回答的是
    //   "人物外轮廓粗细合不合适"，跟场景物体毫无关系。
    // 修订：墨线是 Cull Front 的外扩壳，**只可能出现在剪影外侧** ⇒ 从每行/每列的
    //   剪影端点向外扫，只量紧邻的暗游程。这同时天然排除了场景物体的轮廓。
    const float INK_TH = 0.18f;
    var runH = new List<int>(); var runV = new List<int>();
    if (maskOk) OutlineRuns(gMask, L, W, H, gMx0, gMy0, gMx1, gMy1, runH, runV, INK_TH);
    runH.Sort(); runV.Sort();
    float medH = runH.Count > 0 ? runH[runH.Count / 2] : 0f;
    float medV = runV.Count > 0 ? runV[runV.Count / 2] : 0f;
    float lineW = Mathf.Min(medH, medV);

    // 同帧 A/B：把 _OutlineWidth 置 0 再量一遍 ⇒ 差分就是"墨线本身"的贡献。
    // 这是本项目的一贯纪律（同帧 A/B，对照组差分应≈0），能证明 M7 量的确实是墨线
    // 而不是角色身上本来就有的暗部（腰带、剑鞘、褶皱）。
    var runHo = new List<int>(); var runVo = new List<int>();
    if (gNoOutline != null && maskOk)
    {
        var pxo = gNoOutline.GetPixels();
        var Lo = new float[W * H];
        for (int i = 0; i < pxo.Length; i++)
            Lo[i] = 0.2126f * pxo[i].r + 0.7152f * pxo[i].g + 0.0722f * pxo[i].b;
        OutlineRuns(gMask, Lo, W, H, gMx0, gMy0, gMx1, gMy1, runHo, runVo, INK_TH);
    }
    runHo.Sort(); runVo.Sort();
    float medHo = runHo.Count > 0 ? runHo[runHo.Count / 2] : 0f;
    float medVo = runVo.Count > 0 ? runVo[runVo.Count / 2] : 0f;
    float lineWo = Mathf.Min(medHo, medVo);

    // 全画面非角色区的暗像素占比（保留：粗看"墨量"）
    long darkN = 0, bgTotal = 0;
    for (int y = 0; y < H; y++)
        for (int x = 0; x < W; x++)
        {
            if (excluded(x, y)) continue;
            bgTotal++;
            if (L[y * W + x] < INK_TH) darkN++;
        }
    float inkRatio = bgTotal > 0 ? (float)darkN / bgTotal : 0f;

    // ---- M9 墨彩（丹青）：角色**本体**像素的色度 ----
    // ★ 为什么 M4 证明不了"人物有没有颜色"：M4 是全屏彩度均值，画面 80% 是宣纸，
    //   角色本身只占 ~2% 像素 ⇒ 角色从纯墨变成花青蓝衣，M4 也只动 0.001。
    //   于是"肉眼明明有颜色"和"M4=0.028"可以同时成立。M9 把统计域收进掩码。
    //   同时做同帧 A/B：把 _ChromaKeep 置 0 重量一次 ⇒ 差分 = 墨彩注入的净贡献。
    double m9Sum = 0, m9Sum2 = 0; long m9N = 0, m9Gray = 0;
    var m9v = new List<float>();
    double m9Sum0 = 0; long m9N0 = 0;
    if (maskOk)
    {
        for (int y = gMy0; y <= gMy1; y++)
            for (int x = gMx0; x <= gMx1; x++)
            {
                int i = y * W + x;
                if (!gMask[i]) continue;
                float mx = Mathf.Max(px[i].r, Mathf.Max(px[i].g, px[i].b));
                float mn = Mathf.Min(px[i].r, Mathf.Min(px[i].g, px[i].b));
                float c = mx - mn;
                m9Sum += c; m9Sum2 += c * c; m9N++;
                m9v.Add(c);
                if (c > 0.06f && L[i] > 0.20f) m9Gray++;
            }
        m9v.Sort();
    }
    if (gNoChroma != null && maskOk)
    {
        var pxo = gNoChroma.GetPixels();
        for (int y = gMy0; y <= gMy1; y++)
            for (int x = gMx0; x <= gMx1; x++)
            {
                int i = y * W + x;
                if (!gMask[i]) continue;
                float mx = Mathf.Max(pxo[i].r, Mathf.Max(pxo[i].g, pxo[i].b));
                float mn = Mathf.Min(pxo[i].r, Mathf.Min(pxo[i].g, pxo[i].b));
                m9Sum0 += mx - mn; m9N0++;
            }
    }
    float m9Mean = m9N > 0 ? (float)(m9Sum / m9N) : 0f;
    float m9P90 = m9v.Count > 0 ? m9v[(int)(m9v.Count * 0.90f)] : 0f;
    float m9Mean0 = m9N0 > 0 ? (float)(m9Sum0 / m9N0) : 0f;
    float m9GrayPct = m9N > 0 ? m9Gray * 100f / m9N : 0f;

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
    sb.AppendLine("  M6 分离度(紧邻背景中位−主体墨10%) = " + F((float)(bgRef - charInk10))
                  + "　(主体墨 " + F(charInk10) + " / 紧邻背景 " + F((float)bgRef)
                  + "　本体均值 " + F((float)charMean) + "　包围盒内背景均值 " + F((float)bgMean)
                  + ")　(目标 ≥0.30)");
    sb.AppendLine("     ※ 口径 v2：主体墨 = **本体掩码像素**的 10% 分位；紧邻背景 = 本体包围盒内、非本体像素的**中位**。");
    sb.AppendLine("     ※ 目标回到 **≥0.30**（计划文档第 326 行的原始设计判据）。旧报告里的 ≥0.55 是"
                  + "旧口径下 0.547 反推出来的，属「尺子跟着读数走」；而且它和本作已定的"
                  + "「主体留白 + 分离度交给墨线轮廓」的设计直接矛盾 —— 留白主体本来就不该有高分离度。");
    sb.AppendLine("  M7 角色墨线宽         = " + F(lineW) + " px　(水平段中位 " + F(medH)
                  + " / 垂直段中位 " + F(medV) + "　采样 " + runH.Count + "+" + runV.Count + ")　(目标 3.5~6)");
    sb.AppendLine("     同帧A/B 轮廓关      = " + F(lineWo) + " px　(水平 " + F(medHo)
                  + " / 垂直 " + F(medVo) + ")　⇒ 墨线净贡献 " + F(lineW - lineWo) + " px　(目标 2~4)");
    sb.AppendLine("     ※ 目标 3.5~6 px 的依据：`h_oline` 标定出 px ≈ 300×_OutlineWidth + 1.6，");
    sb.AppendLine("        其中 1.6~2 px 是剪影自身暗缘的**地板**（关掉轮廓也剩 2 px），去不掉。");
    sb.AppendLine("        旧目标 1.8~2.6 px 是「加轮廓 Pass 之前」的基线，当时 M7 量的是别的暗游程，");
    sb.AppendLine("        拿它当判据会把一条 13.7 px 的粗黑边判成合格 —— 已作废。");
    sb.AppendLine("  M9 角色墨彩 彩度       = " + F(m9Mean) + "　(90%分位 " + F(m9P90)
                  + "　非灰像素占比 " + F(m9GrayPct) + " %)");
    sb.AppendLine("     同帧A/B 墨彩关      = " + F(m9Mean0)
                  + "　⇒ _ChromaKeep 净贡献 " + F(m9Mean - m9Mean0) + "　(目标 ≥0.03 ≈ 通道差 8/255)");
    sb.AppendLine("     暗像素占非角色区比  = " + F(inkRatio * 100f) + " %");
    sb.AppendLine("  亮度阶梯 5/20/50/80/95 = " + F(P05) + " / " + F(P20) + " / "
                  + F(P50) + " / " + F(P80) + " / " + F(P95));
    sb.AppendLine("  排除矩形 " + excl.Count + " 个　角色矩形 "
                  + (charRect.width > 0f ? ("(" + F(charRect.xMin) + "," + F(charRect.yMin) + ")~("
                     + F(charRect.xMax) + "," + F(charRect.yMax) + ")") : "无"));
    sb.AppendLine("  本体掩码 " + (gRegionInfo.Length > 0 ? gRegionInfo : "（未采集）"));
    if (maskOk && gShotDir.Length > 0)
    {
        gCloseupPath = Path.Combine(gShotDir, "char_closeup_" + tag + ".png");
        SaveCloseup(tex, gMx0, gMy0, gMx1, gMy1, gCloseupPath);
        sb.AppendLine("  角色近景（2× 放大，供肉眼核对轮廓粗细）" + gCloseupPath);
    }
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
