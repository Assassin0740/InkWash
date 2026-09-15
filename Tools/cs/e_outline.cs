// e_outline.cs —— 增量 E 的尺子：**只量角色身上的墨线轮廓**
//
// 为什么不能用 d_metrics：d_metrics 把角色/敌人矩形**排除**在统计之外（这是它的
// 设计前提，防止"角色变黑"被误读成"整幅层次变好"）。副作用是——角色身上的任何
// 改动它都看不见。E 改的恰恰是角色轮廓 ⇒ 这是个**度量盲区**，必须单独补尺子。
//
// 手法：**同帧 A/B 差分**
//   · 在同一个帧里连续渲染两次（中间不 yield）⇒ 姿势/相机/光照逐位一致，没有动画漂移
//   · A = 轮廓宽 0，B = 轮廓宽 W ⇒ 差异像素**就是轮廓笔画本身**，不需要任何分割算法
//   · 对照组：宽 0 也做一次 A/B ⇒ 量出这套手法的**地板噪声**（若不为 0，阈值就不可信）
//   · 扫多档 W，厚度应随 W 线性 ⇒ 用 px/米 几何交叉验证尺子本身（尺子也会撒谎）
//
// 输出：Tools/reports/e_outline.txt + Tools/screenshots/outline/*.png
// ★ 故意不写 `using System;` —— 会和 `UnityEngine.Object` 撞名（Object 二义性）。
//   需要 System 里的东西一律全限定。
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
    string shotDir = Path.Combine(root, "Tools/screenshots/outline");
    string repPath = Path.Combine(root, "Tools/reports/e_outline.txt");
    Directory.CreateDirectory(shotDir);
    Directory.CreateDirectory(Path.GetDirectoryName(repPath));

    var cam = Camera.main;
    if (cam == null) { Debug.LogError("[e_outline] 找不到 MainCamera"); yield break; }
    var ph = Object.FindObjectOfType<PlayerHealth>();
    if (ph == null) { Debug.LogError("[e_outline] 找不到 PlayerHealth"); yield break; }
    var go = ph.gameObject;

    var ctl = go.GetComponent<PlayerController>();
    var stance = go.GetComponentInChildren<CombatStance>(true);
    var panel = Object.FindObjectOfType<InkStylePanel>();
    var choice = Object.FindObjectOfType<SkillChoicePanel>();
    var spawner = Object.FindObjectOfType<WaveSpawner>();
    var room = Object.FindObjectOfType<RoomController>();
    var run = Object.FindObjectOfType<RunManager>();

    if (panel != null) panel.visible = false;
    if (choice != null) choice.Hide();
    ph.maxHealth = 100000f; ph.ResetHealth();
    if (room != null) room.ResetForTest();
    if (run != null) { run.ResetForTest(); run.SetSeed(20260916); run.nextRoomDelay = 999f; }
    if (ctl != null)
    {
        ctl.ResetToLocomotion();
        ctl.BeginInputOverride();
        ctl.SetInjectedMove(Vector2.zero, false);
    }
    if (stance != null) { stance.combatExitDelay = 99999f; stance.ForceStance(true); }
    if (run != null) run.StartRun();

    if (spawner != null) spawner.enabled = false;
    foreach (var e in Object.FindObjectsOfType<EnemyBase>())
        if (e != null) e.gameObject.SetActive(false);

    { float t = Time.unscaledTime; while (Time.unscaledTime - t < 4.0f) yield return null; }

    // ---- 收集主体上带 _OutlineWidth 的材质槽，逐槽建**运行时实例**（结束时还原）----
    var seen = new HashSet<Renderer>();
    var hitR = new List<Renderer>();
    var hitSlots = new List<int>();
    var instM = new List<Material>();
    var origArr = new Dictionary<Renderer, Material[]>();

    void TryCollect(Renderer r)
    {
        if (r == null || seen.Contains(r)) return;
        seen.Add(r);
        var mats = r.sharedMaterials;
        if (mats == null || mats.Length == 0) return;
        bool any = false;
        for (int i = 0; i < mats.Length; i++)
            if (mats[i] != null && mats[i].HasProperty("_OutlineWidth")) any = true;
        if (!any) return;
        origArr[r] = mats;
        var copy = new Material[mats.Length];
        for (int i = 0; i < mats.Length; i++)
        {
            copy[i] = mats[i];
            if (mats[i] != null && mats[i].HasProperty("_OutlineWidth"))
            {
                var inst = new Material(mats[i]);
                copy[i] = inst;
                hitR.Add(r); hitSlots.Add(i); instM.Add(inst);
            }
        }
        r.sharedMaterials = copy;
    }

    foreach (var r in go.GetComponentsInChildren<Renderer>(true)) TryCollect(r);
    // 剑挂在手骨下、可能在 go 子树外，按材质名单独收
    foreach (var r in Object.FindObjectsOfType<Renderer>())
    {
        var sm = r.sharedMaterial;
        if (sm != null && sm.name.IndexOf("Sword", System.StringComparison.OrdinalIgnoreCase) >= 0) TryCollect(r);
    }
    if (instM.Count == 0) { Debug.LogError("[e_outline] 一个带 _OutlineWidth 的材质都没找到"); yield break; }

    // ---- 真高 / 屏幕高：用 bind-pose 网格包围盒，避开 SkinnedMeshRenderer.bounds 的 3 倍虚胖 ----
    Bounds trueAabb = new Bounds(); bool hasAabb = false;
    foreach (var r in seen)
    {
        Mesh mb = null;
        var smr = r as SkinnedMeshRenderer;
        if (smr != null) mb = smr.sharedMesh;
        else { var mf = r.GetComponent<MeshFilter>(); if (mf != null) mb = mf.sharedMesh; }
        if (mb == null) continue;
        Bounds wb = XformBounds(r.transform.localToWorldMatrix, mb.bounds);
        if (!hasAabb) { trueAabb = wb; hasAabb = true; } else trueAabb.Encapsulate(wb);
    }
    if (!hasAabb) { Debug.LogError("[e_outline] 拿不到网格包围盒"); yield break; }

    int W = cam.pixelWidth, H = cam.pixelHeight;

    // 屏幕高：取真 AABB 的上下端点（在中心 XZ 上）
    Vector3 ctr = trueAabb.center;
    Vector3 pTop = new Vector3(ctr.x, trueAabb.max.y, ctr.z);
    Vector3 pBot = new Vector3(ctr.x, trueAabb.min.y, ctr.z);
    float camDist = Vector3.Distance(cam.transform.position, ctr);
    float screenTop = cam.WorldToViewportPoint(pTop).y * H;
    float screenBot = cam.WorldToViewportPoint(pBot).y * H;
    float screenH = Mathf.Abs(screenTop - screenBot);
    float pxPerM = screenH / Mathf.Max(trueAabb.size.y, 0.001f);

    // 测量矩形 = 真 AABB 投影后外扩 20%（容纳轮廓外扩）
    Rect vp = ProjectRect(cam, trueAabb, 0.20f);
    int x0 = Mathf.Clamp(Mathf.FloorToInt(vp.xMin * W), 0, W - 2);
    int x1 = Mathf.Clamp(Mathf.CeilToInt(vp.xMax * W), x0 + 1, W - 1);
    int y0 = Mathf.Clamp(Mathf.FloorToInt(vp.yMin * H), 0, H - 2);
    int y1 = Mathf.Clamp(Mathf.CeilToInt(vp.yMax * H), y0 + 1, H - 1);

    var sb = new StringBuilder();
    sb.AppendLine("========== 增量 E 轮廓尺子（同帧 A/B 差分）==========");
    sb.AppendLine("相机 " + W + "x" + H + "　轮廓材质槽 " + instM.Count + " 个");
    sb.AppendLine("主体真高 " + F(trueAabb.size.y) + " m　屏幕高 " + F(screenH) + " px　⇒ " + F(pxPerM) + " px/米");
    sb.AppendLine("相机距离 " + F(camDist) + " m　_OutlineDistScale=" + F(instM[0].GetFloat("_OutlineDistScale"))
                  + "　_OutlineFacing=" + F(instM[0].GetFloat("_OutlineFacing"))
                  + "　_OutlineDry=" + F(instM[0].GetFloat("_OutlineDry")));
    sb.AppendLine("测量矩形 px (" + x0 + "," + y0 + ")~(" + x1 + "," + y1 + ")　共 " + ((x1 - x0 + 1) * (y1 - y0 + 1)) + " px");
    sb.AppendLine();
    sb.AppendLine("  W(m)    差分px  ↑暗px占比       水平段中位 垂直段中位  外露半宽中位  外露(米)  期望(米)  比值  PNG");
    sb.AppendLine("  -----  --------  ---------------  ----------  ----------  ------------  --------  --------  ----  ---");

    var widths = new float[] { 0f, 0.018f, 0.035f, 0.055f, 0.080f, 0.120f };
    for (int ci = 0; ci < widths.Length; ci++)
    {
        float w = widths[ci];
        string tag = w <= 0f ? "ctrl_w0" : ("w" + Mathf.RoundToInt(w * 1000f));

        SetOutline(instM, 0f);
        var ta = RenderFrame(cam, W, H);
        SetOutline(instM, w);
        var tb = RenderFrame(cam, W, H);
        if (ta == null || tb == null) { Debug.LogError("[e_outline] 渲染失败 " + tag); break; }

        string png = Path.Combine(shotDir, "outline_" + tag + ".png");
        try { File.WriteAllBytes(png, tb.EncodeToPNG()); } catch { }

        var ca = ta.GetPixels32(); var cb = tb.GetPixels32();
        Object.Destroy(ta); Object.Destroy(tb);

        int rw = x1 - x0 + 1, rh = y1 - y0 + 1;
        var ch = new bool[rw * rh];       // 有变化
        var chOut = new bool[rw * rh];    // 有变化 且 A 里是背景（= 露在外面的半支笔）
        long darkA = 0, darkB = 0, changed = 0;
        for (int y = 0; y < rh; y++)
        {
            int sy = y0 + y;
            for (int x = 0; x < rw; x++)
            {
                int si = sy * W + (x0 + x);
                Color32 pa = ca[si], pb = cb[si];
                float la = Lum(pa);
                if (la < 0.18f) darkA++;
                if (Lum(pb) < 0.18f) darkB++;
                float d = (Mathf.Abs(pa.r - pb.r) + Mathf.Abs(pa.g - pb.g) + Mathf.Abs(pa.b - pb.b)) / 255f;
                bool c = d > 0.06f;
                ch[y * rw + x] = c;
                if (c) { changed++; if (la > 0.45f) chOut[y * rw + x] = true; }
            }
        }
        float rectArea = (float)(rw * rh);
        float medH = MedianRun(ch, rw, rh, true);
        float medV = MedianRun(ch, rw, rh, false);
        float medOut = MedianRun(chOut, rw, rh, true);
        float outM = medOut / Mathf.Max(pxPerM, 0.001f);
        float expM = w * (1f + camDist * instM[0].GetFloat("_OutlineDistScale"));
        float ratio = expM > 0.0001f ? outM / expM : 0f;

        sb.AppendLine("  " + F3(w) + "  " + Pad(changed, 8) + "  "
                      + F2(darkA / rectArea * 100f) + "%→" + F2(darkB / rectArea * 100f) + "%"
                      + "      " + Pad(medH, 10) + "  " + Pad(medV, 10) + "  "
                      + Pad(medOut, 12) + "  " + F3(outM) + "   " + F3(expM) + "   "
                      + F2(ratio) + "  " + Path.GetFileName(png));

        if (ci == 0)
            sb.AppendLine("        ↑ 对照组：同参数渲两次的差分。若非 0，说明这套差分本身有噪声，后面所有数字都要打折。");
        yield return null;
    }

    // ---- 还原材质 ----
    foreach (var kv in origArr) if (kv.Key != null) kv.Key.sharedMaterials = kv.Value;

    if (ctl != null) { ctl.SetInjectedMove(Vector2.zero, true); ctl.EndInputOverride(); }
    if (stance != null) stance.combatExitDelay = 6f;

    string report = sb.ToString();
    File.WriteAllText(repPath, report, new UTF8Encoding(false));
    Debug.Log("[e_outline] 报告已落盘 " + repPath);
    Debug.Log(report);
    yield return null;
}

// ======================================================================

void SetOutline(List<Material> ms, float w)
{
    for (int i = 0; i < ms.Count; i++) ms[i].SetFloat("_OutlineWidth", w);
}

Texture2D RenderFrame(Camera cam, int W, int H)
{
    var rt = RenderTexture.GetTemporary(W, H, 24, RenderTextureFormat.ARGB32);
    var prevT = cam.targetTexture;
    cam.targetTexture = rt;
    try { cam.Render(); }
    finally { cam.targetTexture = prevT; }
    var prevA = RenderTexture.active;
    RenderTexture.active = rt;
    var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
    tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
    tex.Apply();
    RenderTexture.active = prevA;
    RenderTexture.ReleaseTemporary(rt);
    return tex;
}

/// 变化像素沿行/列成段，返回段长的**中位数**（段长 ≥1）。稀疏噪声会被中位数压掉。
float MedianRun(bool[] m, int rw, int rh, bool horizontal)
{
    var runs = new List<int>();
    int outer = horizontal ? rh : rw, inner = horizontal ? rw : rh;
    for (int o = 0; o < outer; o++)
    {
        int run = 0;
        for (int i = 0; i < inner; i++)
        {
            bool v = horizontal ? m[o * rw + i] : m[i * rw + o];
            if (v) run++;
            else { if (run > 0) runs.Add(run); run = 0; }
        }
        if (run > 0) runs.Add(run);
    }
    if (runs.Count == 0) return 0f;
    runs.Sort();
    return runs[runs.Count / 2];
}

Bounds XformBounds(Matrix4x4 m, Bounds b)
{
    Vector3 c = m.MultiplyPoint3x4(b.center);
    Vector3 e = b.extents;
    Vector3 ax = m.MultiplyVector(new Vector3(e.x, 0f, 0f));
    Vector3 ay = m.MultiplyVector(new Vector3(0f, e.y, 0f));
    Vector3 az = m.MultiplyVector(new Vector3(0f, 0f, e.z));
    Vector3 ne = new Vector3(Mathf.Abs(ax.x) + Mathf.Abs(ay.x) + Mathf.Abs(az.x),
                             Mathf.Abs(ax.y) + Mathf.Abs(ay.y) + Mathf.Abs(az.y),
                             Mathf.Abs(ax.z) + Mathf.Abs(ay.z) + Mathf.Abs(az.z));
    return new Bounds(c, ne * 2f);
}

Rect ProjectRect(Camera cam, Bounds b, float margin)
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
    if (!any) return new Rect(0f, 0f, 0f, 0f);
    float dx = (xmax - xmin) * margin, dy = (ymax - ymin) * margin;
    return Rect.MinMaxRect(xmin - dx, ymin - dy, xmax + dx, ymax + dy);
}

float Lum(Color32 c)
{
    return (0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b) / 255f;
}

string F(float v) { return v.ToString("0.###"); }
string F2(float v) { return v.ToString("0.00"); }
string F3(float v) { return v.ToString("0.000"); }
string Pad(float v, int w) { return (v <= 0f ? "-" : v.ToString("0.#")).PadLeft(w); }

return Body();
