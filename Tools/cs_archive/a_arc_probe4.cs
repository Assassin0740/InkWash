// a_arc_probe4.cs —— 弧光锯齿：不透明红 + 隐藏角色（第 4 轮，修正上一轮的自身缺陷）
//
// 上一轮（a_arc_probe3.cs）**探针自己写错了**，得到的是假证据，记录在此以免重犯：
//   为了让弧光"不透明"我 `Object.Destroy` 了 SlashArcFade 组件，结果三张图里**弧光整个没渲染**，
//   于是"没有红色像素"这个观测完全不能说明几何有没有问题。
//   ★ 教训：要改一个逐帧被写回的字段，优先**改它的基准来源**或**接受它的写回**，
//     不要为了拿一个瞬时值去拆组件 —— 拆掉组件会连带改掉生命周期/缩放，引入更大的混淆。
//   实际上根本不用拆：只把 `_BaseColor` 的 **rgb 换成红**、alpha 留给 SlashArcFade 管，
//   渲出来仍是 (≈245, 34, 34) 的明显红色（α≈0.83 与浅灰背景混合）—— 足够判读。
//
// 同时修掉上一轮另一个可比性问题：**不要在每轮把玩家瞬移回原位**。
//   瞬移后第三人称相机会带着阻尼慢慢追，0.45s 远不够，取景就变了；
//   上一轮 R2/R3 里角色跑到画面左下角就是这么来的。改成不瞬移、只依赖相机自然跟随。
//
// 还剩的问题只有一个是非题：
//   弧光区域里"没有弧光的位置"到底是 (甲) 几何真的缺失 还是 (乙) 被角色挡住。
//   纯红 + 角色隐藏 ⇒ 红 = 有几何；非红 = 没画出来。
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
    string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../Tools/screenshots/arcprobe4"));
    Directory.CreateDirectory(dir);

    var ph = Object.FindObjectOfType<PlayerHealth>();
    if (ph == null) { Debug.LogError("[a_arc_probe4] 找不到 PlayerHealth"); yield break; }
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

    // 角色视觉不在 ph.gameObject 之下（上一轮只收到 3 个 Renderer 就是证据），
    // 改用**根节点**向下收集；并逐个打印名字，确认收对了对象。
    var root = ph.transform.root;
    var charRends = new List<Renderer>();
    foreach (var r in root.GetComponentsInChildren<Renderer>(true))
    {
        if (r.gameObject.name.Contains("SlashArc")) continue;
        charRends.Add(r);
    }
    sb.AppendLine("角色根节点 = " + root.name);
    sb.AppendLine("收集到 Renderer " + charRends.Count + " 个：");
    foreach (var r in charRends) sb.AppendLine("    " + r.GetType().Name + "  " + r.name);

    var names = new string[] { "R1_red", "R2_red_nochar", "R3_red_nochar_200" };
    var hideChar = new bool[] { false, true, true };
    var use200 = new bool[] { false, false, true };
    var meshes = new Mesh[names.Length];

    sb.AppendLine();
    sb.AppendLine("===== 纯红弧光 =====");

    for (int c = 0; c < names.Length; c++)
    {
        ctl.ResetToLocomotion();
        foreach (var r in charRends) if (r != null) r.enabled = !hideChar[c];

        { float t = Time.unscaledTime; while (Time.unscaledTime - t < 0.5f) yield return null; }

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
        if (use200[c])
        {
            if (meshes[c] == null) meshes[c] = Resample(mf.sharedMesh, 200);
            mf.sharedMesh = meshes[c];
        }
        if (mr != null && mr.material != null && mr.material.HasProperty("_BaseColor"))
        {
            var bc = mr.material.GetColor("_BaseColor");
            bc.r = 1f; bc.g = 0f; bc.b = 0f;      // 只换 rgb，alpha 留给 SlashArcFade
            mr.material.SetColor("_BaseColor", bc);
            if (mr.material.HasProperty("_FlyingWhite")) mr.material.SetFloat("_FlyingWhite", 0f);
        }

        for (int k = 0; k < 3; k++) yield return null;

        ScreenCapture.CaptureScreenshot(Path.Combine(dir, names[c] + ".png"));

        var cam = Camera.main;
        sb.AppendLine(names[c] + " → 抓图； 角色可见=" + !hideChar[c]
                      + "  tris=" + mf.sharedMesh.triangles.Length / 3
                      + "  isVisible=" + (mr != null ? mr.isVisible.ToString() : "?")
                      + "  距离相机=" + (cam != null ? Vector3.Distance(cam.transform.position, arc.transform.position).ToString("0.##") : "?")
                      + "  屏幕z=" + (cam != null ? cam.WorldToScreenPoint(arc.transform.position).z.ToString("0.##") : "?")
                      + "  scale=" + V3(arc.transform.localScale));
        { float t = Time.unscaledTime; while (Time.unscaledTime - t < 0.2f) yield return null; }
    }

    foreach (var r in charRends) if (r != null) r.enabled = true;
    ctl.SetInjectedMove(Vector2.zero, true);
    ctl.EndInputOverride();
    if (stance != null) stance.combatExitDelay = 6f;
    for (int i = 0; i < meshes.Length; i++) if (meshes[i] != null) Object.Destroy(meshes[i]);

    File.WriteAllText(Path.Combine(Application.dataPath, "../Tools/reports/a_arc_probe4.txt"), sb.ToString());
    Debug.Log("[a_arc_probe4] done");
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
    var m = new Mesh { name = "Probe4_" + seg2 };
    m.vertices = verts; m.colors = cols; m.triangles = tris; m.RecalculateBounds();
    return m;
}

static string V3(Vector3 v) { return "(" + v.x.ToString("0.###") + ", " + v.y.ToString("0.###") + ", " + v.z.ToString("0.###") + ")"; }

return Body();
