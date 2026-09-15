// a_arc_diag.cs —— 弧光内缘「等距锯齿」归因探针（运行时）
//
// 现象：弧光是一笔月牙墨迹，但内缘/外侧出现**逐三角形的硬边面片**（放大 6 倍看
//       像一个低多边形扇面，相邻三角形的浓淡不连续）。
//
// 已经**证伪**的两个假设（别再回去试）：
//   ① `clip(a - 阈值)` 的等值线切出等距齿 —— 已把 clip 从 InkSlash.shader 整个去掉，锯齿仍在
//   ② 顺笔拉丝噪声频率过高 —— 已把 _StreakScale 从 44 降到 7，锯齿仍在
//
// 现在改成**归因实验**而不是继续猜：同一次挥砍、同一时刻，逐层关闭后处理各抓一张。
//   若「全关后处理」仍然后锯齿  ⇒ 归属**几何/Shader**（弧面本身）
//   若「全关后处理」锯齿消失     ⇒ 归属**后处理**（墨线/宣纸/墨晕其中之一）
//   另加一张 `_FlyingWhite = 0`（关掉噪声调制）作对照，判噪声是否参与。
//
// 为什么必须"同一时刻"：弧光寿命只有 arcLifetime（0.24s）且带放大曲线，
// 不同时刻的弧面大小/浓度都不同，两张图没有可比性。
using System.Collections;
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
    string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../Tools/screenshots/arcdiag"));
    Directory.CreateDirectory(dir);

    var ph = Object.FindObjectOfType<PlayerHealth>();
    if (ph == null) { Debug.LogError("[a_arc_diag] 找不到 PlayerHealth"); yield break; }
    var go = ph.gameObject;
    var ctl = go.GetComponent<PlayerController>();
    var vfx = go.GetComponentInChildren<SwordVfx>(true);
    var stance = go.GetComponentInChildren<CombatStance>(true);
    var panel = Object.FindObjectOfType<InkStylePanel>();
    var run = Object.FindObjectOfType<RunManager>();
    var choice = Object.FindObjectOfType<SkillChoicePanel>();
    var spawner = Object.FindObjectOfType<WaveSpawner>();

    if (panel != null) { panel.visible = false; panel.enabled = false; }
    if (choice != null) choice.Hide();

    sb.AppendLine("===== 弧光锯齿归因 =====");
    sb.AppendLine("三层状态 = " + InkStyleRegistry.Describe());

    ph.maxHealth = 100000f; ph.ResetHealth();
    if (spawner != null) { spawner.ResetForTest(); spawner.spawnInterval = 9999f; }
    if (run != null) { run.ResetForTest(); run.SetSeed(20260916); run.nextRoomDelay = 999f; }

    ctl.ResetToLocomotion();
    if (stance != null) { stance.combatExitDelay = 99999f; stance.ForceStance(true); }
    ctl.BeginInputOverride();
    ctl.SetInjectedMove(Vector2.zero, true);
    if (run != null) run.StartRun();

    { float t = Time.unscaledTime; while (Time.unscaledTime - t < 1.5f) yield return null; }

    // ---- 配置表：name / EdgeOn / PaperOn / BloomOn / flyWhiteOverride ----
    // flyWhite < 0 表示不改（用材质原值）
    var names = new string[] { "00_all", "01_noedge", "02_nopaper", "03_nobloom", "04_nopost", "05_flat_nonoise" };
    var edge = new bool?[] { null, false, true, true, false, null };
    var paper = new bool?[] { null, true, false, true, false, null };
    var bloom = new bool?[] { null, true, true, false, false, null };
    var fly = new float[] { -1f, -1f, -1f, -1f, -1f, 0f };

    sb.AppendLine();
    sb.AppendLine("---- 逐配置抓图 ----");

    for (int c = 0; c < names.Length; c++)
    {
        InkStyleRegistry.EdgeOn = edge[c];
        InkStyleRegistry.PaperOn = paper[c];
        InkStyleRegistry.BloomOn = bloom[c];

        // 复位到移动态，保证每一次挥砍的起始姿势/朝向完全一致（可比性的前提）
        ctl.ResetToLocomotion();
        { float t = Time.unscaledTime; while (Time.unscaledTime - t < 0.45f) yield return null; }

        ctl.RequestInjectedAttack();

        // 等弧光出现；它只活 arcLifetime，所以出现后固定延迟若干帧再抓，两次抓图对齐
        float t0 = Time.unscaledTime;
        GameObject arc = null;
        while (arc == null && Time.unscaledTime - t0 < 1.2f)
        {
            arc = GameObject.Find("SlashArc");
            yield return null;
        }

        if (arc == null)
        {
            sb.AppendLine(names[c] + "  ** 没等到 SlashArc");
            continue;
        }

        var mr = arc.GetComponent<MeshRenderer>();
        var mf = arc.GetComponent<MeshFilter>();

        // 噪声对照：把该实例的 _FlyingWhite 压到 0（改的是逐个体实例副本，不污染共享材质）
        bool applied = false;
        if (fly[c] >= 0f && mr != null && mr.material != null && mr.material.HasProperty("_FlyingWhite"))
        {
            mr.material.SetFloat("_FlyingWhite", fly[c]);
            applied = true;
        }

        // 出现后等 3 帧再抓：此时放大曲线已接近满尺寸，浓度还没开始掉
        for (int k = 0; k < 3; k++) yield return null;

        string png = Path.Combine(dir, names[c] + ".png");
        ScreenCapture.CaptureScreenshot(png);

        sb.AppendLine(names[c]);
        sb.AppendLine("    EdgeOn/PaperOn/BloomOn 设置 = " + S(edge[c]) + "/" + S(paper[c]) + "/" + S(bloom[c])
                      + "   实生效 = " + InkStyleRegistry.EdgeEnabled + "/" + InkStyleRegistry.PaperEnabled
                      + "/" + InkStyleRegistry.BloomEnabled);
        if (applied) sb.AppendLine("    已把 _FlyingWhite 压到 0");
        if (mr != null && mr.material != null)
        {
            var m = mr.material;
            sb.AppendLine("    材质 Shader = " + m.shader.name
                          + "   渲染队列 = " + m.renderQueue
                          + "   passes = " + m.passCount);
            sb.AppendLine("    _BaseColor = " + (m.HasProperty("_BaseColor") ? C(m.GetColor("_BaseColor")) : "(无)")
                          + "   _FlyingWhite = " + (m.HasProperty("_FlyingWhite") ? m.GetFloat("_FlyingWhite").ToString("0.###") : "(无)"));
        }
        if (mf != null && mf.sharedMesh != null)
        {
            var mesh = mf.sharedMesh;
            sb.AppendLine("    网格 verts=" + mesh.vertexCount
                          + "  colors=" + (mesh.colors != null ? mesh.colors.Length : -1)
                          + "  tris=" + mesh.triangles.Length / 3
                          + "  subMeshes=" + mesh.subMeshCount
                          + "  bounds=" + V3(mesh.bounds.size));
        }
        sb.AppendLine("    世界坐标 " + V3(arc.transform.position)
                      + "   localScale " + V3(arc.transform.localScale)
                      + "   朝向 " + V3(arc.transform.eulerAngles));

        // 等这一段彻底结束（弧光销毁 + 连击回位），再进下一配置
        while (arc != null && Time.unscaledTime - t0 < 2.5f) { arc = GameObject.Find("SlashArc"); yield return null; }
        { float t = Time.unscaledTime; while (Time.unscaledTime - t < 0.5f) yield return null; }
    }

    InkStyleRegistry.EdgeOn = null;
    InkStyleRegistry.PaperOn = null;
    InkStyleRegistry.BloomOn = null;

    ctl.SetInjectedMove(Vector2.zero, true);
    ctl.EndInputOverride();
    if (stance != null) stance.combatExitDelay = 6f;

    File.WriteAllText(Path.Combine(Application.dataPath, "../Tools/reports/a_arc_diag.txt"), sb.ToString());
    Debug.Log("[a_arc_diag] done");
    yield return null;
}

static string S(bool? b) { return b.HasValue ? (b.Value ? "on" : "off") : "默认"; }
static string V3(Vector3 v) { return "(" + v.x.ToString("0.###") + ", " + v.y.ToString("0.###") + ", " + v.z.ToString("0.###") + ")"; }
static string C(Color c) { return "(" + c.r.ToString("0.###") + ", " + c.g.ToString("0.###") + ", " + c.b.ToString("0.###") + ", " + c.a.ToString("0.###") + ")"; }

return Body();
