// e_tonality.cs —— 主体墨阶尺子：**量角色本体自己的明度分布**
//
// 为什么需要它：d_metrics 把角色矩形排除，e_outline 只量轮廓的像素占用。
// 两者都答不了"角色本体现在几号墨"。实测图像显示角色塌成一张**黑剪纸**
// （ramp ≈ lambert*0.8 + ambient*0.4 + _BandBias(-0.38) ≈ 0.02 ⇒ 最底一阶，
//  再乘 lerp(1, albedo, _InkDensity=0.62) 而对黑衫 albedo≈0.05 ⇒ 均匀 ×0.41）
// ⇒ 轮廓墨色 L≈0.041 与本体 L≈0.04 **同值**，线条等于没画。
//
// 手法：
//   · **精确掩码**：把角色材质临时换成 URP/Unlit 品红，同帧渲一次 ⇒ 品红像素就是角色本体。
//     （比"隐藏角色再差分"干净：不会把角色投在地上的阴影算进来）
//   · **全扫在同一帧内完成**（配置之间不 yield）⇒ 所有配置用的是**逐位相同**的姿势
//   · 扫 _BandBias × _InkDensity 两维，每组都存 PNG，最后一条重复 T0 做**复现性对照**
//
// 判据：
//   · 层次宽度 p90−p10  —— "有没有形"的最强单指标（写意重墨不能是单值色块）
//   · 五色覆盖 焦/浓/重/淡/清 —— 看是不是真的"墨分五色"
//   · 分离度 bgMean − p10 —— 与 M6 同口径，不能为了变白把主体糊进纸里
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
    string shotDir = Path.Combine(root, "Tools/screenshots/tonality");
    string repPath = Path.Combine(root, "Tools/reports/e_tonality.txt");
    Directory.CreateDirectory(shotDir);
    Directory.CreateDirectory(Path.GetDirectoryName(repPath));

    var cam = Camera.main;
    if (cam == null) { Debug.LogError("[e_tonality] 找不到 MainCamera"); yield break; }
    var ph = Object.FindObjectOfType<PlayerHealth>();
    if (ph == null) { Debug.LogError("[e_tonality] 找不到 PlayerHealth"); yield break; }
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

    var cam2 = Camera.main;   // 4s 里相机可能换过实例，重新取一次

    // ---- 收集**角色本体**的材质槽（排除剑）----
    var seen = new HashSet<Renderer>();
    var charR = new List<Renderer>();
    var origArr = new Dictionary<Renderer, Material[]>();
    var instArr = new Dictionary<Renderer, Material[]>();   // 掩码用完要还原成**实例**数组
    var instM = new List<Material>();
    int W = cam2.pixelWidth, H = cam2.pixelHeight;

    foreach (var r in go.GetComponentsInChildren<Renderer>(true))
    {
        if (r == null || seen.Contains(r)) continue;
        seen.Add(r);
        var mats = r.sharedMaterials;
        if (mats == null || mats.Length == 0) continue;
        bool isSword = mats[0] != null && mats[0].name.IndexOf("Sword", System.StringComparison.OrdinalIgnoreCase) >= 0;
        if (isSword) continue;
        bool any = false;
        for (int i = 0; i < mats.Length; i++)
            if (mats[i] != null && mats[i].HasProperty("_BandBias")) any = true;
        if (!any) continue;
        origArr[r] = mats;
        var copy = (Material[])mats.Clone();
        for (int i = 0; i < mats.Length; i++)
        {
            if (mats[i] == null || !mats[i].HasProperty("_BandBias")) continue;
            var inst = new Material(mats[i]);
            copy[i] = inst;
            charR.Add(r); instM.Add(inst);
        }
        r.sharedMaterials = copy;
        instArr[r] = copy;
    }
    charR = new List<Renderer>(instArr.Keys);   // 去重：一个 renderer 只处理一次
    if (instM.Count == 0) { Debug.LogError("[e_tonality] 没找到角色本体材质槽"); yield break; }

    float shipOutline = instM[0].GetFloat("_OutlineWidth");

    // ---- 品红掩码材质 ----
    var unlit = Shader.Find("Universal Render Pipeline/Unlit");
    if (unlit == null) { Debug.LogError("[e_tonality] 找不到 URP/Unlit，无法做掩码"); yield break; }
    var magenta = new Material(unlit);
    magenta.SetColor("_BaseColor", new Color(1f, 0f, 1f, 1f));
    magenta.SetTexture("_BaseMap", Texture2D.whiteTexture);

    // ---- 主体真高 / 屏幕高 / 测量矩形 ----
    Bounds trueAabb = new Bounds(); bool hasAabb = false;
    foreach (var r in seen)
    {
        Mesh mb = null;
        var smr = r as SkinnedMeshRenderer;
        if (smr != null) mb = smr.sharedMesh;
        else { var mf = r.GetComponent<MeshFilter>(); if (mf != null) mb = mf.sharedMesh; }
        if (mb == null) continue;
        var wb = XformBounds(r.transform.localToWorldMatrix, mb.bounds);
        if (!hasAabb) { trueAabb = wb; hasAabb = true; } else trueAabb.Encapsulate(wb);
    }
    Rect vp = ProjectRect(cam2, trueAabb, 0.20f);
    int x0 = Mathf.Clamp(Mathf.FloorToInt(vp.xMin * W), 0, W - 2);
    int x1 = Mathf.Clamp(Mathf.CeilToInt(vp.xMax * W), x0 + 1, W - 1);
    int y0 = Mathf.Clamp(Mathf.FloorToInt(vp.yMin * H), 0, H - 2);
    int y1 = Mathf.Clamp(Mathf.CeilToInt(vp.yMax * H), y0 + 1, H - 1);

    var sb = new StringBuilder();
    sb.AppendLine("========== 主体墨阶尺子（同帧扫描 _BandBias × _InkDensity）==========");
    sb.AppendLine("相机 " + W + "x" + H + "　角色本体材质槽 " + instM.Count + " 个　出货轮廓宽 " + shipOutline + " m");
    sb.AppendLine("主体真高 " + F(trueAabb.size.y) + " m　测量矩形 px (" + x0 + "," + y0 + ")~(" + x1 + "," + y1 + ")");
    sb.AppendLine("五色阈值：焦<0.14　浓0.14~0.30　重0.30~0.48　淡0.48~0.72　清>0.72");
    sb.AppendLine();
    sb.AppendLine(" 标签  _BandBias _InkDens  本体p10 p25 p50 p75 p90  层次宽  分离度  | 焦/浓/重/淡/清 %          | 掩码px  本体均明  轮廓新增暗px  暗占比  PNG");
    sb.AppendLine(" ----  --------- --------  -----------------------  ------  ------  +-------------------------+ ------- --------  ------------  ------  ---");

    // 配置：前三个隔离 _BandBias，后三个隔离 _InkDensity，最后一条重复 T0 做复现对照
    var cfgs = new float[][]
    {
        new float[] { -0.38f, 0.62f },   // T0 现状
        new float[] { -0.10f, 0.62f },   // T1
        new float[] {  0.15f, 0.62f },   // T2
        new float[] {  0.15f, 0.35f },   // T3
        new float[] {  0.40f, 0.35f },   // T4
        new float[] {  0.40f, 0.15f },   // T5
        new float[] {  0.65f, 0.15f },   // T6
        new float[] { -0.38f, 0.62f },   // T0b 复现对照
    };
    string[] tags = { "T0", "T1", "T2", "T3", "T4", "T5", "T6", "T0b" };

    for (int ci = 0; ci < cfgs.Length; ci++)
    {
        float bias = cfgs[ci][0], ink = cfgs[ci][1];
        for (int i = 0; i < instM.Count; i++)
        {
            instM[i].SetFloat("_BandBias", bias);
            instM[i].SetFloat("_InkDensity", ink);
            instM[i].SetFloat("_OutlineWidth", 0f);
        }

        // ① 掩码：换成品红渲一次
        var mag = new Material[1] { magenta };
        foreach (var r in charR) r.sharedMaterials = mag;
        var tMask = RenderFrame(cam2, W, H);
        foreach (var r in charR) r.sharedMaterials = instArr[r];
        if (tMask == null) { Debug.LogError("[e_tonality] 掩码渲染失败"); break; }

        // ② 本体（轮廓关）
        var tBody = RenderFrame(cam2, W, H);
        // ③ 带上出货轮廓
        for (int i = 0; i < instM.Count; i++) instM[i].SetFloat("_OutlineWidth", shipOutline);
        var tOut = RenderFrame(cam2, W, H);
        if (tBody == null || tOut == null) { Debug.LogError("[e_tonality] 渲染失败 " + tags[ci]); break; }

        string png = Path.Combine(shotDir, "tonal_" + tags[ci] + ".png");
        try { File.WriteAllBytes(png, tOut.EncodeToPNG()); } catch { }

        var cm = tMask.GetPixels32(); var cb = tBody.GetPixels32(); var co = tOut.GetPixels32();
        Object.Destroy(tMask); Object.Destroy(tBody); Object.Destroy(tOut);

        var hist = new int[100];
        long nMask = 0, nBg = 0, darkOut = 0;
        double sumBody = 0; double sumBg = 0;
        long[] five = new long[5];
        int px0 = x0, px1 = x1, py0 = y0, py1 = y1;
        for (int y = py0; y <= py1; y++)
        {
            for (int x = px0; x <= px1; x++)
            {
                int si = y * W + x;
                Color32 m = cm[si];
                bool isChar = ((float)(m.r - m.g) > 0.12f) && ((float)(m.b - m.g) > 0.12f);
                float lb = Lum(cb[si]);
                if (isChar)
                {
                    nMask++;
                    int b = Mathf.Clamp(Mathf.RoundToInt(lb * 99f), 0, 99);
                    hist[b]++;
                    sumBody += lb;
                    if (lb < 0.14f) five[0]++;
                    else if (lb < 0.30f) five[1]++;
                    else if (lb < 0.48f) five[2]++;
                    else if (lb < 0.72f) five[3]++;
                    else five[4]++;
                }
                else
                {
                    nBg++; sumBg += lb;
                    if (Lum(co[si]) < 0.18f) darkOut++;
                }
            }
        }
        if (nMask == 0) { sb.AppendLine("  " + tags[ci] + "  掩码为空（品红被后处理改了色？）"); continue; }

        float p10 = Pct(hist, nMask, 0.10f), p25 = Pct(hist, nMask, 0.25f), p50 = Pct(hist, nMask, 0.50f);
        float p75 = Pct(hist, nMask, 0.75f), p90 = Pct(hist, nMask, 0.90f);
        float bgMean = nBg > 0 ? (float)(sumBg / nBg) : 0f;
        float bodyMean = (float)(sumBody / nMask);
        float darkBgFrac = nBg > 0 ? darkOut / (float)nBg * 100f : 0f;

        sb.AppendLine("  " + tags[ci].PadRight(4) + "  " + F2(bias).PadLeft(9) + " " + F2(ink).PadLeft(8)
                      + "  " + F2(p10) + " " + F2(p25) + " " + F2(p50) + " " + F2(p75) + " " + F2(p90)
                      + "  " + F2(p90 - p10).PadLeft(6) + "  " + F2(bgMean - p10).PadLeft(6)
                      + "  | " + Pct5(five[0], nMask) + "/" + Pct5(five[1], nMask) + "/" + Pct5(five[2], nMask)
                      + "/" + Pct5(five[3], nMask) + "/" + Pct5(five[4], nMask)
                      + " | " + nMask.ToString().PadLeft(7) + " " + F2(bodyMean).PadLeft(8)
                      + "  " + darkOut.ToString().PadLeft(12) + "  " + F2(darkBgFrac).PadLeft(6) + "  " + Path.GetFileName(png));
        yield return null;
    }

    foreach (var kv in origArr) if (kv.Key != null) kv.Key.sharedMaterials = kv.Value;
    if (ctl != null) { ctl.SetInjectedMove(Vector2.zero, true); ctl.EndInputOverride(); }
    if (stance != null) stance.combatExitDelay = 6f;

    string report = sb.ToString();
    File.WriteAllText(repPath, report, new UTF8Encoding(false));
    Debug.Log("[e_tonality] 报告已落盘 " + repPath);
    Debug.Log(report);
    yield return null;
}

// ======================================================================

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

float Pct(int[] hist, long total, float p)
{
    long want = (long)(total * p);
    long acc = 0;
    for (int i = 0; i < hist.Length; i++)
    {
        acc += hist[i];
        if (acc >= want) return i / 99f;
    }
    return 1f;
}

string Pct5(long v, long total) { return (v * 100f / total).ToString("0.0").PadLeft(5); }

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

return Body();
