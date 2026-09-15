// h_oline.cs —— 角色墨线宽度标定扫描（把「外轮廓粗细」从世界米换成屏幕像素）
//
// 起因：用户说「人物的这个外轮廓可以减淡一点」。上一轮把 `_OutlineWidth` 从
//       0.055 降到 0.038 —— 但那是**世界米**，而"一条线看起来粗不粗"是**屏幕像素**的事。
//       实测：角色高 2.17 m 占屏 577 px ⇒ ≈266 px/m，再乘 `_OutlineDistScale` 在 6 m
//       处的 ×1.36 ⇒ 0.038 m 画出来是 **13.7 px**。计划文档里写的目标 1.8~2.6 px
//       是加轮廓 Pass **之前**的基线（那时 M7 量的是别的东西），从来不是为这条线定的。
//
// 本探针做两件事：
//   ① 给出「`_OutlineWidth`(世界米) → 屏幕 px」的实测标定曲线（不要靠算，要靠量）
//   ② 每个宽度存一张 2× 角色近景 ⇒ 肉眼挑一个，而不是让数字替人做审美判断
//
// 口径与 d_metrics 的 M7 完全一致：从本体剪影外侧向外扫暗游程。剑不参与（掩码只含
// 本体），所以这里量到的就是"人物外轮廓"。
// 输出：Tools/reports/h_oline.txt + Tools/screenshots/oline/w{NN}_closeup.png
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
    string shotDir = Path.Combine(root, "Tools/screenshots/oline");
    string repPath = Path.Combine(root, "Tools/reports/h_oline.txt");
    Directory.CreateDirectory(shotDir);
    Directory.CreateDirectory(Path.GetDirectoryName(repPath));

    var cam = Camera.main;
    if (cam == null) { Debug.LogError("[h_oline] 找不到 MainCamera"); yield break; }
    var ph = Object.FindObjectOfType<PlayerHealth>();
    if (ph == null) { Debug.LogError("[h_oline] 找不到 PlayerHealth"); yield break; }
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
    if (spawner != null) { spawner.ResetForTest(); spawner.spawnInterval = 999f; }
    if (room != null) room.ResetForTest();
    if (run != null) { run.ResetForTest(); run.SetSeed(20260916); run.nextRoomDelay = 999f; }
    ctl.ResetToLocomotion();
    if (stance != null) { stance.combatExitDelay = 99999f; stance.ForceStance(true); }
    ctl.BeginInputOverride();
    ctl.SetInjectedMove(Vector2.zero, false);
    if (run != null) run.StartRun();
    { float t = Time.unscaledTime; while (Time.unscaledTime - t < 4.0f) yield return null; }

    int W = cam.pixelWidth, H = cam.pixelHeight;

    // ---- 收集本体材质槽（排除剑）----
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
        origArr[r] = ms; rends.Add(r);
        for (int i = 0; i < ms.Length; i++) if (ms[i] != null && ms[i].HasProperty("_BandBias")) mats.Add(ms[i]);
    }
    if (mats.Count == 0) { Debug.LogError("[h_oline] 找不到本体材质"); yield break; }

    var sb = new StringBuilder();
    sb.AppendLine("========== h_oline 角色墨线宽度标定 ==========");
    sb.AppendLine("相机 " + W + "x" + H + "　本体材质槽 " + mats.Count + "　渲染器 " + rends.Count);

    // ---- 掩码（品红）----
    var unlit = Shader.Find("Universal Render Pipeline/Unlit");
    if (unlit == null) { Debug.LogError("[h_oline] 找不到 URP/Unlit"); yield break; }
    var magenta = new Material(unlit);
    magenta.SetColor("_BaseColor", new Color(1f, 0f, 1f, 1f));
    magenta.SetTexture("_BaseMap", Texture2D.whiteTexture);
    var one = new Material[1] { magenta };
    foreach (var r in rends) r.sharedMaterials = one;
    var tMask = ShootFrame(cam, W, H);
    foreach (var kv in origArr) if (kv.Key != null) kv.Key.sharedMaterials = kv.Value;
    Object.Destroy(magenta);
    if (tMask == null) { Debug.LogError("[h_oline] 掩码渲染失败"); yield break; }

    var mp = tMask.GetPixels32();
    var mask = new bool[W * H];
    long nm = 0;
    for (int i = 0; i < mask.Length; i++)
        if ((mp[i].r - mp[i].g) > 30 && (mp[i].b - mp[i].g) > 30) { mask[i] = true; nm++; }
    Object.Destroy(tMask);
    int mx0 = W, my0 = H, mx1 = -1, my1 = -1;
    for (int y = 0; y < H; y++)
        for (int x = 0; x < W; x++)
            if (mask[y * W + x])
            {
                if (x < mx0) mx0 = x; if (x > mx1) mx1 = x;
                if (y < my0) my0 = y; if (y > my1) my1 = y;
            }
    if (nm < 200) { Debug.LogError("[h_oline] 掩码为空"); yield break; }
    float bodyPx = my1 - my0 + 1;
    sb.AppendLine("掩码 px " + nm + "　包围盒 (" + mx0 + "," + my0 + ")~(" + mx1 + "," + my1
                  + ")　角色屏高 " + bodyPx + " px");

    float baseW = mats[0].GetFloat("_OutlineWidth");
    sb.AppendLine();
    sb.AppendLine("  宽度(米)  折合px   水平中位  垂直中位  采样H+V   外侧暗带中位  近景");
    sb.AppendLine("  --------  -------  --------  --------  --------  ------------  ----");

    var widths = new float[] { baseW, 0.030f, 0.024f, 0.018f, 0.012f, 0.008f };
    var results = new List<string>();
    foreach (var w in widths)
    {
        for (int i = 0; i < mats.Count; i++) mats[i].SetFloat("_OutlineWidth", w);
        var tex = ShootFrame(cam, W, H);
        if (tex == null) { sb.AppendLine("  " + w.ToString("0.000") + "  渲染失败"); continue; }
        var px = tex.GetPixels();
        var L = new float[W * H];
        for (int i = 0; i < px.Length; i++) L[i] = 0.2126f * px[i].r + 0.7152f * px[i].g + 0.0722f * px[i].b;

        var rh = new List<int>(); var rv = new List<int>();
        int capped = 0;
        OutlineRuns(mask, L, W, H, mx0, my0, mx1, my1, rh, rv, 0.18f, ref capped);
        rh.Sort(); rv.Sort();
        float mh = rh.Count > 0 ? rh[rh.Count / 2] : 0f;
        float mv = rv.Count > 0 ? rv[rv.Count / 2] : 0f;
        float pw = Mathf.Min(mh, mv);

        // 同帧的"暗带"整体统计：外侧环里 <0.18 的像素数 / 剪影周长（粗看墨量）
        string png = Path.Combine(shotDir, "w" + Mathf.RoundToInt(w * 1000f).ToString("000") + "_closeup.png");
        SaveCloseup(tex, mx0, my0, mx1, my1, png);
        string line = "  " + w.ToString("0.000").PadLeft(8) + "  " + (w * 362f).ToString("0.0").PadLeft(7)
                    + "  " + mh.ToString("0.0").PadLeft(8) + "  " + mv.ToString("0.0").PadLeft(8)
                    + "  " + (rh.Count + "+" + rv.Count).PadLeft(8)
                    + "  " + pw.ToString("0.0").PadLeft(12)
                    + "  " + Path.GetFileName(png) + (capped > 0 ? ("（撞上限 " + capped + "）") : "");
        sb.AppendLine(line);
        results.Add(line);
        Object.Destroy(tex);
        yield return null;
    }

    for (int i = 0; i < mats.Count; i++) mats[i].SetFloat("_OutlineWidth", baseW);
    float chk = mats[0].GetFloat("_OutlineWidth");
    sb.AppendLine();
    sb.AppendLine("  还原 _OutlineWidth = " + chk.ToString("0.###") + "（出厂 " + baseW.ToString("0.###") + "）"
                  + (Mathf.Abs(chk - baseW) < 1e-6f ? " ✅" : " ❌"));
    sb.AppendLine();
    sb.AppendLine("  ※ 「折合px」= 宽度 × 362（由角色屏高与距离补偿反推的换算率，仅供内插参考）。");
    sb.AppendLine("  ※ 判据：不应再沿用计划里 1.8~2.6 px —— 那是**加轮廓 Pass 之前**的基线数，");
    sb.AppendLine("     当时 M7 量的是别的暗游程。真正的判据只能是「看着像墨线、不像贴纸黑边」。");

    ctl.SetInjectedMove(Vector2.zero, true);
    ctl.EndInputOverride();
    if (stance != null) stance.combatExitDelay = 6f;

    string report = sb.ToString();
    File.WriteAllText(repPath, report, new UTF8Encoding(false));
    Debug.Log("[h_oline] 报告已落盘 " + repPath);
    Debug.Log(report);
    yield return null;
}

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

/// 与 d_metrics 的 M7 同口径：从剪影端点向外扫暗游程。
/// 这里把上限放到 24 并统计"撞上限"的次数 —— 撞上限说明那不是一条线，是一片黑，
/// 必须丢弃（本探针要量的可能是 14 px 的粗线，所以不能像 d_metrics 那样一上来就卡 14）。
void OutlineRuns(bool[] mask, float[] Lf, int W, int H,
                 int mx0, int my0, int mx1, int my1, List<int> rh, List<int> rv, float inkTh, ref int capped)
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
        if (n > 0) { if (n >= MAXSCAN) capped++; else rh.Add(n); }
        n = 0;
        for (int k = 1; k <= MAXSCAN && xr + k < W && Lf[y * W + xr + k] < inkTh; k++) n++;
        if (n > 0) { if (n >= MAXSCAN) capped++; else rh.Add(n); }
    }
    for (int x = mx0; x <= mx1; x++)
    {
        int yb = -1, yt = -1;
        for (int y = my0; y <= my1; y++) if (mask[y * W + x]) { yb = y; break; }
        for (int y = my1; y >= my0; y--) if (mask[y * W + x]) { yt = y; break; }
        if (yb < 0) continue;
        int n = 0;
        for (int k = 1; k <= MAXSCAN && yb - k >= 0 && Lf[(yb - k) * W + x] < inkTh; k++) n++;
        if (n > 0) { if (n >= MAXSCAN) capped++; else rv.Add(n); }
        n = 0;
        for (int k = 1; k <= MAXSCAN && yt + k < H && Lf[(yt + k) * W + x] < inkTh; k++) n++;
        if (n > 0) { if (n >= MAXSCAN) capped++; else rv.Add(n); }
    }
}

void SaveCloseup(Texture2D tex, int x0, int y0, int x1, int y1, string path)
{
    if (tex == null) return;
    int pad = 24;
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

return Body();
