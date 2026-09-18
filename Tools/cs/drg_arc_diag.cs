using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;

// drg_arc_diag.cs —— 归因「盘旋侧视下青弧不可见」的**决定性**探针（第二十三轮遗留）
//
// 为什么需要它：`drg_storm23` 报「盘旋 侧视 0 像素 ｜ 前3/4 816 像素」，
// 而 A/B 探针把 `_ZTest` 切成 Always 之后仍然是 0 ⇒ 深度剔除基本被排除。
// 可代码上又读不出漏洞：
//   · `across = Cross(tangent, toCam)` 落在**屏幕平面内** ⇒ 弧带永远正面朝相机，不可能侧缩成线；
//   · `a.mesh.bounds` 写死 400 m 且跟随龙的位置 ⇒ 不可能被视锥剔除。
// 纸面推不出结论 ⇒ 说明这个"看不见"还没被任何一把已有的尺子覆盖到。
//
// 本探针的做法（**零颜色假设**）：同一帧内连续 `Camera.Render()` 多次，每次只改一个变量，
// 逐张出图 + 逐张数「与基准的差异像素」：
//   ① 只留电弧 · 黑底        —— 回答"到底画了没有"。不依赖任何阈值，亮就是亮。
//   ② 基准（原样）            —— 后面各档的对照基准。
//   ③ ZTest = 8 (Always)      —— 被龙自己的不透明皮剔除？（把上一轮 A/B 的结论钉死在**同 tick**）
//   ④ arcCamBias → 6 m + 重建网格 —— 锚点还在身体内部、推出得不够？
//   ⑤ _Intensity → 6          —— 只是太淡，没到阈值？
// 「差异像素」= 与基准逐像素 `max|ΔRGB| > 20` 的个数。它数的是"这一档比基准多画/少画了什么"，
// **不预设电弧应该是什么颜色** —— 上一轮的教训就是「数青像素」把画面左下角的蓝色玩家
// 当成了电弧（ab0 报 52，实际那 52 像素是玩家角色）。
//
// ★ 同 tick 是硬要求：盘旋姿态每秒转几十度。上一轮 A/B 每档等了 30 帧，
//   五档根本不在同一姿态上 ⇒ 「bias0 有 52、bias0.8 是 0」很可能只是**时间差**，不是 bias 差。
public class drg_arc_diag : MonoBehaviour
{
    const string DIR = "D:/Unity Project/InkWash/Tools/screenshots/enemies/S23/diag";
    const string RP  = "D:/Unity Project/InkWash/Tools/reports/drg_arc_diag.txt";
    const int W = 900, H = 600;
    const int DIFF_THRESH = 20;

    static readonly BindingFlags BF  = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;
    static readonly BindingFlags BFS = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static;

    readonly StringBuilder _sb = new StringBuilder();
    Type _tyShow, _tyDrg, _tyStorm;
    Component _show, _drg; Transform _drgT; GameObject _go; object _storm;
    MethodInfo _mPlay, _mStop, _mLabel, _mSpinePos, _mRebuild;
    PropertyInfo _pCount, _pSpineN, _pArcN;
    readonly object[] _arg1 = new object[1];

    Camera _cam;
    Color32[] _base;
    readonly List<Renderer> _allRend = new List<Renderer>();

    void Start() { StartCoroutine(Run()); }

    IEnumerator Run()
    {
        // ★ 上一轮踩过：探针中途异常退出会把 captureFramerate 留在 30（deltaTime 恒 1/30）。
        //   本探针做的是"单帧内连续渲染"，本来就不需要钉帧步长 —— 显式清掉，顺带自愈。
        Time.captureFramerate = 0;
        Time.timeScale = 1f;

        yield return null; yield return null;
        Directory.CreateDirectory(DIR);

        foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (_tyShow == null) _tyShow = a.GetType("InkWash.DebugTools.ActionShowcase");
            if (_tyDrg == null) _tyDrg = a.GetType("InkWash.Enemies.EnemyDragon");
            if (_tyStorm == null) _tyStorm = a.GetType("InkWash.Effects.DragonStormVfx");
        }
        _sb.AppendLine("=== drg_arc_diag：盘旋侧视弧带不可见的归因（同帧多趟渲染）===");
        if (_tyShow == null || _tyDrg == null || _tyStorm == null) { _sb.AppendLine("× 类型缺失"); yield return Done(); }

        foreach (var o in UnityEngine.Object.FindObjectsOfType(_tyShow, true)) { _show = o as Component; if (_show != null) break; }
        if (_show == null) { _sb.AppendLine("× 没有 ActionShowcase"); yield return Done(); }
        _mPlay = _tyShow.GetMethod("PlayForCapture", BF);
        _mStop = _tyShow.GetMethod("StopAutoClose", BF);
        _pCount = _tyShow.GetProperty("ItemCount", BF);
        _mLabel = _tyShow.GetMethod("ItemLabel", BF);
        int count = (int)_pCount.GetValue(_show, null);
        int idx = -1;
        for (int i = 0; i < count; i++)
        {
            string lb = (string)_mLabel.Invoke(_show, new object[] { i });
            if (lb.Contains("墨龙") && lb.Contains("盘旋")) { idx = i; break; }
        }
        _sb.AppendLine("条目：盘旋 idx=" + idx);
        if (idx < 0) { _sb.AppendLine("× 没找到条目"); yield return Done(); }

        _cam = MakeCam();
        _mStop.Invoke(_show, null);
        _mPlay.Invoke(_show, new object[] { idx });
        _go = null;
        for (int i = 0; i < 150 && _go == null; i++) { yield return null; _go = GameObject.Find("Showcase_Actor_" + idx); }
        if (_go == null) { _sb.AppendLine("× 没有 actor"); yield return Done(); }
        _drg = _go.GetComponentInChildren(_tyDrg, true);
        if (_drg == null) { _sb.AppendLine("× actor 里没有 EnemyDragon"); yield return Done(); }
        _drgT = ((Component)_drg).transform;
        _pSpineN = _tyDrg.GetProperty("SpineCount", BF);
        _mSpinePos = _tyDrg.GetMethod("GetSpinePosition", BF);
        _tyDrg.GetField("stormVfx", BF).SetValue(_drg, true);

        for (int i = 0; i < 30; i++) yield return null;
        for (int i = 0; i < 120 && _storm == null; i++)
        {
            _storm = _tyStorm.GetProperty("Instance", BFS).GetValue(null, null);
            if (_storm == null) yield return null;
        }
        if (_storm == null) { _sb.AppendLine("× 没有 DragonStormVfx 实例"); yield return Done(); }

        _pArcN   = _tyStorm.GetProperty("ActiveArcCount", BF);
        _mRebuild = _tyStorm.GetMethod("Rebuild", BF);
        _tyStorm.GetField("viewCamera", BF).SetValue(_storm, _cam);

        for (int i = 0; i < 40; i++) { Follow(); yield return null; }

        // ══════════════════════════════════════════════════════════════
        //  同帧五趟渲染 —— 龙姿态在整个过程中**一帧都没走过**
        // ══════════════════════════════════════════════════════════════
        Follow();
        var mat = (Material)_tyStorm.GetField("_arcMat", BF).GetValue(_storm);
        float z0 = mat != null && mat.HasProperty("_ZTest") ? mat.GetFloat("_ZTest") : 4f;
        float i0 = mat != null && mat.HasProperty("_Intensity") ? mat.GetFloat("_Intensity") : 1f;
        float b0 = (float)_tyStorm.GetField("arcCamBias", BF).GetValue(_storm);

        _sb.AppendLine();
        _sb.AppendLine("── 同帧五趟 ──");
        _sb.AppendLine("  基准档位： _ZTest=" + z0 + "  _Intensity=" + i0 + "  arcCamBias=" + b0
                       + "  活跃电弧=" + (int)_pArcN.GetValue(_storm, null));

        // ① 只留电弧 · 黑底
        int hidden = HideAllButArcs(true);
        var bg0 = _cam.backgroundColor;
        _cam.backgroundColor = Color.black;
        Color32[] only = Shot("d1_arcsonly");
        _cam.backgroundColor = bg0;
        HideAllButArcs(false);
        int lit = CountNonBlack(only);
        _sb.AppendLine("  ① 只留电弧(黑底)：关掉了 " + hidden + " 个渲染器 ｜ 非黑像素 " + lit
                       + Bbox(only, 24) + "   ← **这是「画了没有」的零假设判据**：亮=画了");

        // ② 基准（出货档位）
        _base = Shot("d0_base");
        int baseCyan = CountCyan(_base);
        _sb.AppendLine("  ② 基准(出货档 ZTest=4 bias=0.8)：全屏青像素 " + baseCyan);

        // ③ 同帧扫描 (ZTest, bias) —— 回答"NaN 修好之后，bias/ZTest 这两个急救手段还需要吗"
        float[] zs = { 4f, 4f, 4f, 8f, 4f };
        float[] bs = { 0.8f, 0f, 2f, 0.8f, 0.8f };
        string[] ns = { "ZTest4 bias0.8(出货)", "ZTest4 bias0", "ZTest4 bias2", "ZTest8 bias0.8", "ZTest4 bias0.2" };
        _sb.AppendLine("  ③ 同帧扫描（全部**同一帧**、龙姿态一帧没走）：");
        for (int k = 0; k < zs.Length; k++)
        {
            if (mat != null && mat.HasProperty("_ZTest")) mat.SetFloat("_ZTest", zs[k]);
            _tyStorm.GetField("arcCamBias", BF).SetValue(_storm, bs[k]);
            RebuildAll();
            Color32[] px = Shot("d" + (k + 2) + "_sw" + k);
            _sb.AppendLine("     [" + ns[k] + "]  青像素 " + CountCyan(px) + " ｜ 与基准差 " + Diff(_base, px)
                           + " px ｜ 非黑 " + CountNonBlack(px));
        }
        if (mat != null && mat.HasProperty("_ZTest")) mat.SetFloat("_ZTest", 4f);
        _tyStorm.GetField("arcCamBias", BF).SetValue(_storm, 0.8f);
        RebuildAll();
        int nanCnt = CountNaN();
        _sb.AppendLine("  ④ NaN 自检：NaN 顶点数 = " + nanCnt + "   ← 修好之前是「每条弧带最后一节 2 个顶点 + 2 个 alpha」");

        // ── 逐条电弧的状态 + 屏幕落点 ──
        _sb.AppendLine();
        _sb.AppendLine("── 逐条电弧状态 ──");
        DumpArcs();
        _sb.AppendLine();
        _sb.AppendLine("── 脊柱投影 ──");
        DumpSpine();
        yield return Done();
    }

    /// <summary>关掉"所有名字不以 DragonArc_ 开头的渲染器"。true=关，false=按原状恢复。</summary>
    int HideAllButArcs(bool hide)
    {
        if (hide)
        {
            _allRend.Clear();
            var rs = UnityEngine.Object.FindObjectsOfType<Renderer>(true);
            for (int i = 0; i < rs.Length; i++)
            {
                var r = rs[i];
                if (r == null) continue;
                if (r.gameObject.name.StartsWith("DragonArc_")) continue;
                if (!r.enabled) continue;
                _allRend.Add(r);
                r.enabled = false;
            }
            return _allRend.Count;
        }
        for (int i = 0; i < _allRend.Count; i++) if (_allRend[i] != null) _allRend[i].enabled = true;
        return 0;
    }

    void RebuildAll()
    {
        var arcs = (IList)_tyStorm.GetField("_arcs", BF).GetValue(_storm);
        if (arcs == null) return;
        for (int i = 0; i < arcs.Count; i++)
        {
            try { _mRebuild.Invoke(_storm, new object[] { arcs[i], _cam, false }); } catch { }
        }
    }

    void DumpArcs()
    {
        var arcs = (IList)_tyStorm.GetField("_arcs", BF).GetValue(_storm);
        if (arcs == null) { _sb.AppendLine("  × 无 _arcs"); return; }
        for (int i = 0; i < arcs.Count; i++)
        {
            object a = arcs[i];
            var t = a.GetType();
            var go = (GameObject)t.GetField("go").GetValue(a);
            var mr = (MeshRenderer)t.GetField("mr").GetValue(a);
            var mesh = (Mesh)t.GetField("mesh").GetValue(a);
            var verts = (Vector3[])t.GetField("verts").GetValue(a);
            var cols = (Color[])t.GetField("cols").GetValue(a);
            bool act = (bool)t.GetField("active").GetValue(a);
            bool span = (bool)t.GetField("spanValid").GetValue(a);
            bool fsm = (bool)t.GetField("fromSmoke").GetValue(a);
            float w = (float)t.GetField("width").GetValue(a);
            float br = (float)t.GetField("bright").GetValue(a);
            float jt = (float)t.GetField("jitter").GetValue(a);
            int st = (int)t.GetField("stations").GetValue(a);
            int ia = (int)t.GetField("i0").GetValue(a), ib = (int)t.GetField("i1").GetValue(a);
            if (!act && i >= 6) continue;   // 只打前几条够看的
            float cs = 0f; for (int c = 0; c < cols.Length; c++) cs += cols[c].a;

            Vector3 s0 = _cam.WorldToScreenPoint(verts[0]);
            Vector3 s1 = _cam.WorldToScreenPoint(verts[verts.Length - 1]);
            Vector3 sp0 = _cam.WorldToScreenPoint(verts[verts.Length / 2]);
            _sb.AppendLine("  #" + i + " act=" + act + " go.active=" + (go != null && go.activeSelf)
                           + " mr.en=" + (mr != null && mr.enabled) + " visible=" + (mr != null && mr.isVisible)
                           + " st=" + st + " w=" + w.ToString("F3") + " jit=" + jt.ToString("F3")
                           + " bright=" + br.ToString("F2") + " Σα=" + cs.ToString("F2")
                           + " span=" + span + " i" + ia + "-" + ib + " smoke=" + fsm);
            _sb.AppendLine("       网格 vC=" + (mesh != null ? mesh.vertexCount : -1)
                           + " bounds=" + (mesh != null ? mesh.bounds.size.ToString("F1") : "-")
                           + "  屏 p0=(" + s0.x.ToString("F0") + "," + s0.y.ToString("F0") + ")"
                           + " pm=(" + sp0.x.ToString("F0") + "," + sp0.y.ToString("F0") + ")"
                           + " p1=(" + s1.x.ToString("F0") + "," + s1.y.ToString("F0") + ")"
                           + "  世界 p0=" + verts[0].ToString("F2") + " p1=" + verts[verts.Length - 1].ToString("F2"));

            // ★ 末节中间量：NaN 到底出在哪一环
            var pts = (Vector3[])t.GetField("pts").GetValue(a);
            var jits = (float[])t.GetField("jit").GetValue(a);
            var acr = (Vector3[])t.GetField("across").GetValue(a);
            int last = st - 1;
            _sb.AppendLine("       末节 k=" + last + "  u=1.0  pts=" + pts[last].ToString("F3")
                           + "  across=" + acr[last].ToString("F3") + "  jit=" + jits[last].ToString("F3")
                           + "  差=" + (pts[last] - pts[last - 1]).ToString("F3")
                           + "  cols[n-1].a=" + cols[cols.Length - 1].a.ToString("F3"));
        }
    }

    void DumpSpine()
    {
        int n = (int)_pSpineN.GetValue(_drg, null);
        float xmin = 9e9f, xmax = -9e9f, ymin = 9e9f, ymax = -9e9f;
        int off = 0;
        for (int i = 0; i < n; i++)
        {
            _arg1[0] = i;
            Vector3 w = (Vector3)_mSpinePos.Invoke(_drg, _arg1);
            Vector3 s = _cam.WorldToScreenPoint(w);
            if (s.z <= 0f) { off++; continue; }
            if (s.x < xmin) xmin = s.x; if (s.x > xmax) xmax = s.x;
            if (s.y < ymin) ymin = s.y; if (s.y > ymax) ymax = s.y;
        }
        _sb.AppendLine("  脊柱节数 " + n + "  背后 " + off + " 节  屏 bbox x[" + xmin.ToString("F0") + "," + xmax.ToString("F0")
                       + "] y[" + ymin.ToString("F0") + "," + ymax.ToString("F0") + "]  (画面 " + W + "x" + H + ")");
    }

    void Follow()
    {
        int n = (int)_pSpineN.GetValue(_drg, null);
        Vector3 c = Vector3.zero;
        for (int i = 0; i < n; i++) { _arg1[0] = i; c += (Vector3)_mSpinePos.Invoke(_drg, _arg1); }
        c /= n;
        float r = 3f;
        for (int i = 0; i < n; i++) { _arg1[0] = i; r = Mathf.Max(r, ((Vector3)_mSpinePos.Invoke(_drg, _arg1) - c).magnitude); }
        float dist = Mathf.Max(19f, r / Mathf.Tan(_cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.35f);
        Vector3 fwd = _drgT.forward; fwd.y = 0f; fwd.Normalize();
        Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized;
        Vector3 d = (right * 0.985f + Vector3.up * 0.16f + fwd * 0.06f).normalized;
        _cam.transform.position = c + d * dist;
        _cam.transform.LookAt(c, Vector3.up);
    }

    // ★ 只判 RGB，**不能判 alpha**：相机 clearFlags=SolidColor 且背景不透明 ⇒
    //   整张图 alpha 恒为 255，"a > 8" 会让判据恒真（这一版差点把整屏 540000 报成"画了"）。
    static int CountNonBlack(Color32[] px)
    {
        int k = 0;
        for (int i = 0; i < px.Length; i++)
            if ((int)px[i].r + px[i].g + px[i].b > 24) k++;
        return k;
    }

    static string Bbox(Color32[] px, int th)
    {
        int xmin = W - 1, xmax = 0, ymin = H - 1, ymax = 0, k = 0;
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                Color32 p = px[y * W + x];
                if ((int)p.r + p.g + p.b <= th) continue;
                k++;
                if (x < xmin) xmin = x; if (x > xmax) xmax = x;
                if (y < ymin) ymin = y; if (y > ymax) ymax = y;
            }
        if (k == 0) return "  bbox=空";
        return "  bbox x[" + xmin + "," + xmax + "] y[" + ymin + "," + ymax + "]  注：图像 y 向上为 " + H;
    }

    static int CountCyan(Color32[] px)
    {
        int k = 0;
        for (int i = 0; i < px.Length; i++)
            if (px[i].b - px[i].r > 30 && px[i].g - px[i].r > 18) k++;
        return k;
    }

    /// <summary>数活跃弧带里 NaN 的顶点分量与 NaN 的顶点色 alpha。修好之前应该是 2*2=4/条。</summary>
    int CountNaN()
    {
        var arcs = (IList)_tyStorm.GetField("_arcs", BF).GetValue(_storm);
        if (arcs == null) return -1;
        int k = 0;
        for (int i = 0; i < arcs.Count; i++)
        {
            object a = arcs[i];
            var t = a.GetType();
            if (!(bool)t.GetField("active").GetValue(a)) continue;
            var verts = (Vector3[])t.GetField("verts").GetValue(a);
            var cols = (Color[])t.GetField("cols").GetValue(a);
            for (int c = 0; c < verts.Length; c++)
                if (float.IsNaN(verts[c].x) || float.IsNaN(verts[c].y) || float.IsNaN(verts[c].z)) k++;
            for (int c = 0; c < cols.Length; c++)
                if (float.IsNaN(cols[c].a)) k++;
        }
        return k;
    }

    int Diff(Color32[] a, Color32[] b)
    {
        int k = 0;
        for (int i = 0; i < a.Length; i++)
        {
            int dr = Mathf.Abs(a[i].r - b[i].r), dg = Mathf.Abs(a[i].g - b[i].g), db = Mathf.Abs(a[i].b - b[i].b);
            if (Mathf.Max(dr, Mathf.Max(dg, db)) > DIFF_THRESH) k++;
        }
        return k;
    }

    Color32[] Shot(string tag)
    {
        var prev = RenderTexture.active;
        _cam.Render();
        RenderTexture.active = _cam.targetTexture;
        var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
        tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
        tex.Apply();
        Color32[] px = tex.GetPixels32();
        byte[] png = tex.EncodeToPNG();
        RenderTexture.active = prev;
        UnityEngine.Object.Destroy(tex);
        try { File.WriteAllBytes(DIR + "/" + tag + ".png", png); } catch { }
        return px;
    }

    Camera MakeCam()
    {
        var g = new GameObject("PROBE_CAM_DIAG");
        var c = g.AddComponent<Camera>();
        c.clearFlags = CameraClearFlags.SolidColor;
        c.backgroundColor = new Color(0.90f, 0.90f, 0.89f, 1f);
        c.fieldOfView = 42f; c.nearClipPlane = 0.05f; c.farClipPlane = 2000f;
        c.enabled = false;
        c.targetTexture = new RenderTexture(W, H, 24);
        return c;
    }

    IEnumerator Done()
    {
        Time.captureFramerate = 0;
        try { File.WriteAllText(RP, _sb.ToString()); } catch { }
        Debug.Log("[drg_arc_diag] 写入 " + RP);
        yield return null;
    }
}

var __hostD = new GameObject("drg_arc_diag");
__hostD.AddComponent<drg_arc_diag>();
return "DRG_ARC_DIAG_STARTED";
