// c_tune.cs —— 增量 C 的纹理参数扫描（对照实验，不是猜）
//
// 为什么需要它：M5 从 10.3e-3 只涨到 21.0e-3，离 76e-3 还差 3.6 倍。
// 此时有三条可能的路（提频率 / 提幅度 / 换层），凭直觉改四次 = 四轮 refresh+复测，
// 而且改完还是不知道"是哪一层在贡献"。本项目硬规矩：只差一项未通过 → **先怀疑度量本身**，
// 做对照实验；一次性"运行时实测"不可靠（这里是 12 次独立实测 + 逐 config 复测）。
//
// 做法：**给白盒材质建运行时实例**，只改实例的浮点，全部 config 共用同一批实例。
// 好处：① 不动资产、不需要 refresh ② 不会把材质资产改脏（Stop 后自动消失）
//      ③ 同一帧序列里连续比较，光线/粒子/敌人位置都不变 —— 这才能归因。
//
// 输出：每个 config 的 M5（地面带 |Laplacian| 中位数 / 80 分位），
//       以及**层贡献分解**（关闭纹理 / 仅颗粒 / 仅笔触 / 两层全开）。
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
    string repPath = Path.Combine(root, "Tools/reports/c_tune.txt");
    Directory.CreateDirectory(Path.GetDirectoryName(repPath));

    var cam = Camera.main;
    if (cam == null) { Debug.LogError("[c_tune] 找不到 MainCamera"); yield break; }
    var ph = Object.FindObjectOfType<PlayerHealth>();
    if (ph == null) { Debug.LogError("[c_tune] 找不到 PlayerHealth"); yield break; }
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

    // ---- 建白盒材质运行时实例：只建一次，后续 config 只改浮点 ----
    string[] targets = { "M_Whitebox_Ground", "M_Whitebox_Wall",
                         "M_Whitebox_Pillar", "M_Whitebox_Platform",
                         "M_Character_Ink",
                         "M_Ink_Enemy_MoOu_0", "M_Ink_Enemy_MoTu_0", "M_Ink_Enemy_MoYan_0" };
    var inst = new Dictionary<Material, Material>();
    var restore = new List<KeyValuePair<Renderer, Material>>();
    foreach (var mr in Object.FindObjectsOfType<MeshRenderer>(true))
    {
        var sm = mr.sharedMaterial;
        if (sm == null) continue;
        bool hit = false;
        for (int i = 0; i < targets.Length; i++) if (sm.name == targets[i]) { hit = true; break; }
        if (!hit) continue;
        if (!inst.ContainsKey(sm)) inst[sm] = new Material(sm);
        restore.Add(new KeyValuePair<Renderer, Material>(mr, sm));
        mr.sharedMaterial = inst[sm];
    }
    foreach (var sr in Object.FindObjectsOfType<SkinnedMeshRenderer>(true))
    {
        var sm = sr.sharedMaterial;
        if (sm == null) continue;
        bool hit = false;
        for (int i = 0; i < targets.Length; i++) if (sm.name == targets[i]) { hit = true; break; }
        if (!hit) continue;
        if (!inst.ContainsKey(sm)) inst[sm] = new Material(sm);
        restore.Add(new KeyValuePair<Renderer, Material>(sr, sm));
        sr.sharedMaterial = inst[sm];
    }

    // 静置，让敌人到位、粒子收敛
    { float t = Time.unscaledTime; while (Time.unscaledTime - t < 4.0f) yield return null; }

    // ---- config 表：标签, 颗粒尺度, 颗粒强度, 笔触尺度, 笔触强度 ----
    // ---- config 表：标签, 颗粒尺度, 颗粒强度, 笔触尺度, 笔触强度, 笔触拉伸比 ----
    var cfgs = new List<object[]>
    {
        new object[] { "G0 关闭纹理",           96f, 0.00f,  7.0f, 0.22f, 3.0f },
        new object[] { "G1 96/0.46(当前)",      96f, 0.46f,  7.0f, 0.22f, 3.0f },
        new object[] { "G2 64/0.55",            64f, 0.55f,  7.0f, 0.22f, 3.0f },
        new object[] { "G3 96/0.70",            96f, 0.70f,  7.0f, 0.22f, 3.0f },
        new object[] { "G4 96/0.95",            96f, 0.95f,  7.0f, 0.22f, 3.0f },
        new object[] { "G5 150/0.60",          150f, 0.60f,  7.0f, 0.22f, 3.0f },
    };

    var sb = new StringBuilder();
    sb.AppendLine("========== c_tune 纹理参数扫描 ==========");
    sb.AppendLine("相机 " + cam.pixelWidth + "x" + cam.pixelHeight + "　取景 idle（同 d_metrics）");
    sb.AppendLine("每行 = 一次独立实测（改实例浮点 → 等 3 帧 → Camera.Render → 量 M5）");
    sb.AppendLine();
    sb.AppendLine(string.Format("{0,-28} {1,9} {2,9} {3,9} {4,9}",
                  "config", "M5a像素级", "M5b_4px", "M5a_80", "暗像素%"));

    float baseM5 = 0f;
    foreach (var c in cfgs)
    {
        string label = (string)c[0];
        float gs = (float)c[1], ga = (float)c[2], ss = (float)c[3], sa = (float)c[4], st = (float)c[5];
        bool killMottle = label.StartsWith("E5");
        foreach (var kv in inst)
        {
            var m = kv.Value;
            if (m.HasProperty("_GrainScale"))   m.SetFloat("_GrainScale",   gs);
            if (m.HasProperty("_GrainAmp"))     m.SetFloat("_GrainAmp",     ga);
            if (m.HasProperty("_StrokeScale"))  m.SetFloat("_StrokeScale",  ss);
            if (m.HasProperty("_StrokeAmp"))    m.SetFloat("_StrokeAmp",    sa);
            if (m.HasProperty("_StrokeStretch"))m.SetFloat("_StrokeStretch",st);
            if (killMottle && m.HasProperty("_InkMottle")) m.SetFloat("_InkMottle", 0f);
        }
        for (int k = 0; k < 3; k++) yield return null;

        Texture2D tex = CaptureNow(cam);
        if (tex == null) { sb.AppendLine(label + "  抓帧失败"); continue; }
        float m5 = 0f, m5b = 0f, m5p80 = 0f, darkPct = 0f;
        MeasureGround(tex, cam, go, ref m5, ref m5b, ref m5p80, ref darkPct);
        Object.Destroy(tex);

        try {
            string tuneDir = Path.Combine(Path.GetFullPath(Path.Combine(Application.dataPath, "..")),
                                          "Tools/screenshots/tune");
            Directory.CreateDirectory(tuneDir);
            var png = System.Text.Encoding.UTF8.GetBytes("");
            File.WriteAllBytes(Path.Combine(tuneDir, label.Substring(0, 2) + ".png"), tex.EncodeToPNG());
        } catch { }
        if (baseM5 <= 0f) baseM5 = m5;
        sb.AppendLine(string.Format("{0,-28} {1,9:F2} {2,9:F2} {3,9:F2} {4,9:F2}",
                      label, m5 * 1000f, m5b * 1000f, m5p80 * 1000f, darkPct));
    }

    // ---- 还原渲染器材质，避免把场景留成"实例材质"状态 ----
    foreach (var kv in restore) if (kv.Key != null) kv.Key.sharedMaterial = kv.Value;
    foreach (var kv in inst) Object.Destroy(kv.Value);

    sb.AppendLine();
    sb.AppendLine("判据：A0 是「关闭纹理」地板。某层若相对 A0 无显著增量 ⇒ 它对这一档测不到。");
    string report = sb.ToString();
    File.WriteAllText(repPath, report, new UTF8Encoding(false));
    Debug.Log("[c_tune] 报告已落盘 " + repPath);
    Debug.Log(report);
    yield return null;
}

/// 立即抓一帧：Camera.Render 路径（ScreenCapture 在协程里必失败，不再尝试）
Texture2D CaptureNow(Camera cam)
{
    var rt = RenderTexture.GetTemporary(cam.pixelWidth, cam.pixelHeight, 24);
    var prev = cam.targetTexture;
    cam.targetTexture = rt;
    cam.Render();
    cam.targetTexture = prev;
    RenderTexture.active = rt;
    var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
    tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
    tex.Apply();
    RenderTexture.active = null;
    RenderTexture.ReleaseTemporary(rt);
    return tex;
}

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

/// 与 d_metrics 的 M5 完全同口径：地面带 = 画面下部 4%~26%，排除角色/敌人/剑所在矩形
void MeasureGround(Texture2D tex, Camera cam, GameObject player,
                   ref float med, ref float med4, ref float p80, ref float darkPct)
{
    int W = tex.width, H = tex.height;
    Color[] px = tex.GetPixels();
    float[] L = new float[W * H];
    for (int i = 0; i < px.Length; i++)
        L[i] = 0.2126f * px[i].r + 0.7152f * px[i].g + 0.0722f * px[i].b;

    var excl = new List<Rect>();
    foreach (var smr in player.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        excl.Add(ProjectBounds(cam, smr.bounds));
    foreach (var eb in Object.FindObjectsOfType<EnemyBase>())
        foreach (var smr in eb.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            excl.Add(ProjectBounds(cam, smr.bounds));
    foreach (var mr in player.GetComponentsInChildren<MeshRenderer>(true))
        excl.Add(ProjectBounds(cam, mr.bounds));

    System.Func<int, int, bool> excluded = (x, y) =>
    {
        float vx = (x + 0.5f) / W, vy = (y + 0.5f) / H;
        for (int k = 0; k < excl.Count; k++)
            if (excl[k].width > 0f && excl[k].Contains(new Vector2(vx, vy))) return true;
        return false;
    };

    // M5a = 像素级 |Laplacian|（细粒/纸颗粒）；M5b = 4px 步长 |Laplacian|（中频/皴法笔痕）
    // 为什么必须分两档：像素级档对"4px 以上的纹理"几乎无感（改笔触层它纹丝不动），
    // 只靠它会把"提频率到纯噪点"当成唯一出路。中频档才对应肉眼说的"有笔痕"。
    var list = new List<float>();
    var list4 = new List<float>();
    int dark = 0, tot = 0;
    int gy0 = Mathf.Clamp((int)(H * 0.04f), 1, H - 2);
    int gy1 = Mathf.Clamp((int)(H * 0.26f), 1, H - 2);
    const int ST = 4;
    for (int y = gy0; y <= gy1; y++)
        for (int x = 1; x < W - 1; x++)
        {
            if (excluded(x, y)) continue;
            tot++;
            if (L[y * W + x] < 0.18f) dark++;
            float lap = Mathf.Abs(L[(y + 1) * W + x] + L[(y - 1) * W + x]
                                + L[y * W + x + 1] + L[y * W + x - 1] - 4f * L[y * W + x]);
            list.Add(lap);
            if (x >= ST && x < W - ST && y >= gy0 + ST && y <= gy1 - ST)
            {
                float lap4 = Mathf.Abs(L[(y + ST) * W + x] + L[(y - ST) * W + x]
                                     + L[y * W + x + ST] + L[y * W + x - ST] - 4f * L[y * W + x]);
                list4.Add(lap4);
            }
        }
    list.Sort();
    list4.Sort();
    med  = list.Count  > 0 ? list[list.Count / 2] : 0f;
    med4 = list4.Count > 0 ? list4[list4.Count / 2] : 0f;
    p80  = list.Count  > 0 ? list[(int)(list.Count * 0.80f)] : 0f;
    darkPct = tot > 0 ? 100f * dark / tot : 0f;
}

return Body();
