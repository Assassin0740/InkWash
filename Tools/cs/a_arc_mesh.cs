// a_arc_mesh.cs —— 弧光锯齿：几何 vs 顶点色 的二分判定（运行时）
//
// 上游结论（a_arc_diag.cs，已实证）：关掉墨线/宣纸/墨晕三层后处理，锯齿**依然存在**
//   ⇒ 锯齿归属**几何/Shader**，与后处理无关。（两个 shader 侧假设此前也已证伪）
//   放大 6 倍可数出约 27 组齿 —— 恰好等于 BuildArcMesh 的 `segments = 28` ⇒ 27 个四边形段。
//
// 本探针要把"齿 = 四边形段"这个相关性升级成因果，用两个正交变量做二分：
//   变量一：段数 28 → 200   （若齿随段数变细 ⇒ 齿来自**网格离散化**）
//   变量二：顶点色 统一为白色 （若统一后齿消失 ⇒ 齿来自**顶点色数据**，即插值没生效）
//
// 做法：弧光一出现就把 MeshFilter.sharedMesh 换成探针新造的网格（SlashArcFade 只管材质，
//       换网格不影响它的生命周期），等 3 帧再抓，与基线同刻对齐。
// 新网格由**原网格顶点重采样**得到 —— 不重新实现 BuildArcMesh 的数学，
// 避免"探针里的公式写错了 ⇒ 得到假结论"（这类假证据踩过：把方法当字段查、报缺失）。
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using InkWash.Effects;
using InkWash.Player;
using InkWash.Rendering;
using InkWash.Roguelike;
using InkWash.UI;

IEnumerator Body()
{
    var sb = new StringBuilder();
    string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../Tools/screenshots/arcmesh"));
    Directory.CreateDirectory(dir);

    var ph = Object.FindObjectOfType<PlayerHealth>();
    if (ph == null) { Debug.LogError("[a_arc_mesh] 找不到 PlayerHealth"); yield break; }
    var go = ph.gameObject;
    var ctl = go.GetComponent<PlayerController>();
    var stance = go.GetComponentInChildren<CombatStance>(true);
    var panel = Object.FindObjectOfType<InkStylePanel>();
    var run = Object.FindObjectOfType<RunManager>();
    var choice = Object.FindObjectOfType<SkillChoicePanel>();
    var spawner = Object.FindObjectOfType<WaveSpawner>();

    if (panel != null) { panel.visible = false; panel.enabled = false; }
    if (choice != null) choice.Hide();

    ph.maxHealth = 100000f; ph.ResetHealth();
    if (spawner != null) { spawner.ResetForTest(); spawner.spawnInterval = 9999f; }
    if (run != null) { run.ResetForTest(); run.SetSeed(20260916); run.nextRoomDelay = 999f; }

    ctl.ResetToLocomotion();
    if (stance != null) { stance.combatExitDelay = 99999f; stance.ForceStance(true); }
    ctl.BeginInputOverride();
    ctl.SetInjectedMove(Vector2.zero, true);
    if (run != null) run.StartRun();

    // 归一化到"全开后处理"，与 a_arc_diag 的 00_all 同条件，便于跨探针比对
    InkStyleRegistry.EdgeOn = null;
    InkStyleRegistry.PaperOn = null;
    InkStyleRegistry.BloomOn = null;

    { float t = Time.unscaledTime; while (Time.unscaledTime - t < 1.5f) yield return null; }

    var names = new string[] { "A_orig28", "B_28_uniformcolor", "C_200_color", "D_200_uniformcolor" };
    var meshes = new Mesh[names.Length];

    sb.AppendLine("===== 弧光锯齿：网格二分判定 =====");

    for (int c = 0; c < names.Length; c++)
    {
        ctl.ResetToLocomotion();
        { float t = Time.unscaledTime; while (Time.unscaledTime - t < 0.45f) yield return null; }

        ctl.RequestInjectedAttack();

        float t0 = Time.unscaledTime;
        GameObject arc = null;
        while (arc == null && Time.unscaledTime - t0 < 1.2f)
        {
            arc = GameObject.Find("SlashArc");
            yield return null;
        }
        if (arc == null) { sb.AppendLine(names[c] + "  ** 没等到 SlashArc"); continue; }

        var mf = arc.GetComponent<MeshFilter>();
        var mr = arc.GetComponent<MeshRenderer>();
        Mesh orig = mf != null ? mf.sharedMesh : null;
        if (orig == null) { sb.AppendLine(names[c] + "  ** 没有网格"); continue; }

        if (c == 0)
        {
            // 基线：额外把原网格的真实数据打出来，供后续配置对照
            var ov = orig.vertices;
            int n = ov.Length / 3;
            sb.AppendLine("原网格剖析：verts=" + ov.Length + " rings=3 段数=" + n
                          + " tris=" + orig.triangles.Length / 3);
            sb.AppendLine("  i=0  内/中/外半径 = " + R(ov[0]) + " / " + R(ov[1]) + " / " + R(ov[2])
                          + "   角(中) = " + A(ov[1]).ToString("0.##") + "°");
            int mid = (n / 2) * 3;
            sb.AppendLine("  i=中 内/中/外半径 = " + R(ov[mid]) + " / " + R(ov[mid + 1]) + " / " + R(ov[mid + 2])
                          + "   角(中) = " + A(ov[mid + 1]).ToString("0.##") + "°");
            sb.AppendLine("  i=末 内/中/外半径 = " + R(ov[(n - 1) * 3]) + " / " + R(ov[(n - 1) * 3 + 1])
                          + " / " + R(ov[(n - 1) * 3 + 2]));
            var col = orig.colors;
            sb.AppendLine("  alpha 顶点色 i=0  内/中/外 = " + col[0].a.ToString("0.###") + "/"
                          + col[1].a.ToString("0.###") + "/" + col[2].a.ToString("0.###"));
            sb.AppendLine("  alpha 顶点色 i=中 内/中/外 = " + col[mid].a.ToString("0.###") + "/"
                          + col[mid + 1].a.ToString("0.###") + "/" + col[mid + 2].a.ToString("0.###"));
            sb.AppendLine("  绕序抽样（前 6 个三角形，正=逆时针/负=顺时针）:");
            var ot = orig.triangles;
            for (int k = 0; k < 6; k++)
            {
                Vector3 p0 = ov[ot[k * 3]], p1 = ov[ot[k * 3 + 1]], p2 = ov[ot[k * 3 + 2]];
                float sgn = (p1.x - p0.x) * (p2.y - p0.y) - (p2.x - p0.x) * (p1.y - p0.y);
                sb.AppendLine("    tri" + k + "  idx=(" + ot[k * 3] + "," + ot[k * 3 + 1] + "," + ot[k * 3 + 2]
                              + ")  有符号面积 = " + sgn.ToString("0.####"));
            }
        }
        else
        {
            if (meshes[c] == null)
            {
                int seg = (c == 1) ? 28 : 200;
                bool uniform = (c == 1 || c == 3);
                meshes[c] = Resample(orig, seg, uniform);
            }
            mf.sharedMesh = meshes[c];
        }

        for (int k = 0; k < 3; k++) yield return null;

        ScreenCapture.CaptureScreenshot(Path.Combine(dir, names[c] + ".png"));
        sb.AppendLine(names[c] + " → 抓图； 实际网格 verts=" + mf.sharedMesh.vertexCount
                      + " tris=" + mf.sharedMesh.triangles.Length / 3
                      + " colors=" + mf.sharedMesh.colors.Length
                      + "  name=" + mf.sharedMesh.name);

        while (arc != null && Time.unscaledTime - t0 < 2.5f) { arc = GameObject.Find("SlashArc"); yield return null; }
        { float t = Time.unscaledTime; while (Time.unscaledTime - t < 0.5f) yield return null; }
    }

    ctl.SetInjectedMove(Vector2.zero, true);
    ctl.EndInputOverride();
    if (stance != null) stance.combatExitDelay = 6f;

    for (int i = 1; i < meshes.Length; i++) if (meshes[i] != null) Object.Destroy(meshes[i]);

    File.WriteAllText(Path.Combine(Application.dataPath, "../Tools/reports/a_arc_mesh.txt"), sb.ToString());
    Debug.Log("[a_arc_mesh] done");
    yield return null;
}

/// <summary>把原弧面网格按索引空间线性重采样成 seg2 段；uniform=true 时顶点色统一为不透明白。</summary>
static Mesh Resample(Mesh src, int seg2, bool uniform)
{
    var v0 = src.vertices;
    var c0 = src.colors;
    int n = v0.Length / 3;
    var verts = new Vector3[seg2 * 3];
    var cols = new Color[seg2 * 3];
    var tris = new int[(seg2 - 1) * 12];

    for (int j = 0; j < seg2; j++)
    {
        float u = (float)j / (seg2 - 1) * (n - 1);
        int i0 = Mathf.Min((int)u, n - 2);
        float f = u - i0;
        for (int r = 0; r < 3; r++)
        {
            verts[j * 3 + r] = Vector3.Lerp(v0[i0 * 3 + r], v0[(i0 + 1) * 3 + r], f);
            cols[j * 3 + r] = uniform ? new Color(1f, 1f, 1f, 1f) : Color.Lerp(c0[i0 * 3 + r], c0[(i0 + 1) * 3 + r], f);
        }
    }
    for (int i = 0; i < seg2 - 1; i++)
    {
        int o = i * 12, a0 = i * 3, a1 = (i + 1) * 3;
        tris[o + 0] = a0 + 0; tris[o + 1] = a0 + 1; tris[o + 2] = a1 + 0;
        tris[o + 3] = a0 + 0; tris[o + 4] = a1 + 0; tris[o + 5] = a1 + 1;
        tris[o + 6] = a0 + 1; tris[o + 7] = a0 + 2; tris[o + 8] = a1 + 1;
        tris[o + 9] = a0 + 1; tris[o + 10] = a1 + 1; tris[o + 11] = a1 + 2;
    }

    var m = new Mesh { name = "ProbeArc" + seg2 + (uniform ? "U" : "C") };
    m.vertices = verts;
    m.colors = cols;
    m.triangles = tris;
    m.RecalculateBounds();
    return m;
}

static float R(Vector3 v) { return new Vector2(v.x, v.y).magnitude; }
static float A(Vector3 v) { return Mathf.Atan2(v.y, v.x) * Mathf.Rad2Deg; }

return Body();
