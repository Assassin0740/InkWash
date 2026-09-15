// a_arc_probe3.cs —— 弧光锯齿：不透明纯红 + 隐藏角色（运行时）
//
// 前三轮已经把范围收窄到：
//   a_arc_diag.cs  关掉墨线/宣纸/墨晕 ⇒ 锯齿仍在（与后处理无关）
//   a_arc_mesh.cs  ① 顶点色统一不透明白 ⇒ 锯齿更明显（与浓淡梯度无关）
//                  ② 段数 28→200 ⇒ 齿数变 199（齿 = 网格四边形段）
//                  ③ 像素网格实测：弧光区域里"暗值 ~35（弧光）"与"亮值 ~190（背景/角色）"
//                     **交替**出现 ⇒ 半个三角形的位置没有弧光
//   a_arc_mesh2.cs 把全部三角形绕序统一成逆时针 ⇒ **锯齿不变**
//                  （这一条很关键：`Cull Off` 的 Pass 里绕序本不该有影响，
//                   但它同时排除了"统一绕序"这条修法，别再试）
//
// 剩下的二分只剩一个：那些"没有弧光的位置"到底是
//   (甲) 几何真的缺失  —— 网格/渲染问题
//   (乙) 被别的物体挡住 —— 弧面是竖直平面，正切过角色身体，角色写深度、弧光 ZWrite Off
//
// 做法：把弧光的 `_BaseColor` 改成**不透明纯红**（同时拆掉 SlashArcFade，
//       否则它每帧会把 alpha 写回 0.832*fade —— 不改会以为"改了没生效"）。
//       红/非红一目了然，不再受"白角色 + 灰背景都是 ~200"的干扰。
//       然后分别"角色可见 / 角色隐藏"各抓一张。
//
// 另外把玩家的位置与朝向在每次挥砍前**显式复位**：`ResetToLocomotion()` 不复位位置，
// 连击的前冲会累计位移，几轮下来相机取景完全变了 —— 那样两张图没有可比性（上一轮就吃了这个亏）。
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
    string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../Tools/screenshots/arcprobe"));
    Directory.CreateDirectory(dir);

    var ph = Object.FindObjectOfType<PlayerHealth>();
    if (ph == null) { Debug.LogError("[a_arc_probe3] 找不到 PlayerHealth"); yield break; }
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

    { float t = Time.unscaledTime; while (Time.unscaledTime - t < 1.5f) yield return null; }

    // 记住玩家的初始位置/朝向 —— 每次挥砍前复位，保证取景逐像素可比
    Vector3 homePos = go.transform.position;
    Quaternion homeRot = go.transform.rotation;
    sb.AppendLine("玩家初始位置 " + V3(homePos) + "  朝向 " + V3(homeRot.eulerAngles));

    // 收集角色身上的全部 Renderer，供"隐藏角色"用
    var charRends = new List<Renderer>();
    foreach (var r in go.GetComponentsInChildren<Renderer>(true)) charRends.Add(r);
    sb.AppendLine("角色身上 Renderer 数 = " + charRends.Count);

    var names = new string[] { "R1_red_char", "R2_red_nochar", "R3_red_nochar_200" };
    var hideChar = new bool[] { false, true, true };
    var use200 = new bool[] { false, false, true };
    var meshes = new Mesh[names.Length];

    sb.AppendLine();
    sb.AppendLine("===== 不透明纯红弧光 =====");

    for (int c = 0; c < names.Length; c++)
    {
        go.transform.position = homePos;
        go.transform.rotation = homeRot;
        ctl.ResetToLocomotion();

        foreach (var r in charRends) if (r != null) r.enabled = !hideChar[c];

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

        // 拆掉 SlashArcFade（它每帧把 _BaseColor.a 写回基准值，会把"纯红不透明"冲掉）
        foreach (var mb in arc.GetComponents<MonoBehaviour>())
        {
            if (mb != null && mb.GetType().Name == "SlashArcFade") Object.Destroy(mb);
        }

        var mf = arc.GetComponent<MeshFilter>();
        var mr = arc.GetComponent<MeshRenderer>();
        if (use200[c])
        {
            if (meshes[c] == null) meshes[c] = Resample(mf.sharedMesh, 200);
            mf.sharedMesh = meshes[c];
        }
        if (mr != null && mr.material != null)
        {
            mr.material.SetColor("_BaseColor", new Color(1f, 0f, 0f, 1f));
            if (mr.material.HasProperty("_FlyingWhite")) mr.material.SetFloat("_FlyingWhite", 0f);
            mr.material.renderQueue = 3900;   // 押到最晚，排除"被后画的半透明物体盖住"
        }
        arc.transform.localScale = Vector3.one;   // 拆掉渐变后停在出生尺寸，比 0.84 更好看

        for (int k = 0; k < 3; k++) yield return null;

        ScreenCapture.CaptureScreenshot(Path.Combine(dir, names[c] + ".png"));
        sb.AppendLine(names[c] + " → 抓图； 角色可见=" + !hideChar[c]
                      + "  网格 tris=" + mf.sharedMesh.triangles.Length / 3
                      + "  BaseColor=" + C(mr.material.GetColor("_BaseColor"))
                      + "  queue=" + mr.material.renderQueue);

        if (arc != null) Object.Destroy(arc);
        { float t = Time.unscaledTime; while (Time.unscaledTime - t < 0.6f) yield return null; }
    }

    foreach (var r in charRends) if (r != null) r.enabled = true;
    go.transform.position = homePos;
    go.transform.rotation = homeRot;
    ctl.SetInjectedMove(Vector2.zero, true);
    ctl.EndInputOverride();
    if (stance != null) stance.combatExitDelay = 6f;
    for (int i = 0; i < meshes.Length; i++) if (meshes[i] != null) Object.Destroy(meshes[i]);

    File.WriteAllText(Path.Combine(Application.dataPath, "../Tools/reports/a_arc_probe3.txt"), sb.ToString());
    Debug.Log("[a_arc_probe3] done");
    yield return null;
}

static Mesh Resample(Mesh src, int seg2)
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
            cols[j * 3 + r] = Color.Lerp(c0[i0 * 3 + r], c0[(i0 + 1) * 3 + r], f);
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
    var m = new Mesh { name = "Probe3_" + seg2 };
    m.vertices = verts; m.colors = cols; m.triangles = tris; m.RecalculateBounds();
    return m;
}

static string V3(Vector3 v) { return "(" + v.x.ToString("0.###") + ", " + v.y.ToString("0.###") + ", " + v.z.ToString("0.###") + ")"; }
static string C(Color c) { return "(" + c.r.ToString("0.###") + ", " + c.g.ToString("0.###") + ", " + c.b.ToString("0.###") + ", " + c.a.ToString("0.###") + ")"; }

return Body();
