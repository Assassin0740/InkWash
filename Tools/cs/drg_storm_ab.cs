// drg_storm_ab.cs —— 弧带「看不见」的归因 A/B：**没画** vs **被龙自己的皮挡住**
//
// 为什么必须有这个实验：弧带锚点取在脊柱骨节（**身体内部**），只从骨节横向摊开 ±width/2。
// 若身体半径 > 摊开幅度，整条弧带落在身体轮廓内 ⇒ 被 `ZTest LEqual` 全部剔除。
// 而 `ActiveArcCount` 照报 3~10、控制台干净 ⇒ **从画面上分不出这两种情况**。
// 做法：把深度测试做成材质属性 `_ZTest`（4=LEqual / 8=Always），
//       在**同一条目、同一机位**下切三档跑，各自出图 + 数"脊柱走廊内的青像素"。
//   · LEqual 小 → Always 大  ⇒ **是深度剔除**（那就加大 arcCamBias，或正式采用 Always）
//   · LEqual ≈ Always ≈ 0   ⇒ **是根本没画**（去查条数/材质/网格，别再去调深度）
using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;

public class drg_storm_ab : MonoBehaviour
{
    const string DIR = "D:/Unity Project/InkWash/Tools/screenshots/enemies/S23/ab";
    const string RP  = "D:/Unity Project/InkWash/Tools/reports/drg_storm_ab.txt";
    const int W = 900, H = 600;
    static readonly BindingFlags BF = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;
    static readonly BindingFlags BFS = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static;

    readonly StringBuilder _sb = new StringBuilder();
    Type _tyShow, _tyDrg, _tyStorm;
    Component _show, _drg; Transform _drgT; object _storm;
    MethodInfo _mPlay, _mStop, _mLabel; PropertyInfo _pCount;
    Camera _cam;
    Color32[] _px; readonly bool[] _corr = new bool[W * H];
    MethodInfo _mSpinePos; PropertyInfo _pSpineN;
    readonly object[] _arg1 = new object[1];

    void Start() { StartCoroutine(Run()); }

    IEnumerator Run()
    {
        yield return null; yield return null;
        Directory.CreateDirectory(DIR);
        Time.captureFramerate = 30;

        foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (_tyShow == null) _tyShow = a.GetType("InkWash.DebugTools.ActionShowcase");
            if (_tyDrg == null) _tyDrg = a.GetType("InkWash.Enemies.EnemyDragon");
            if (_tyStorm == null) _tyStorm = a.GetType("InkWash.Effects.DragonStormVfx");
        }
        _sb.AppendLine("=== drg_storm_ab：弧带看不见的归因（同机位切 ZTest / 切 bias）===");
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
        _sb.AppendLine("条目：盘旋 idx=" + idx + "（选盘旋 —— 侧视条目青像素为 0，这里定位到底为什么）");
        if (idx < 0) { _sb.AppendLine("× 没找到条目"); yield return Done(); }

        _cam = MakeCam();
        _mStop.Invoke(_show, null);
        _mPlay.Invoke(_show, new object[] { idx });
        GameObject go = null;
        for (int i = 0; i < 150 && go == null; i++) { yield return null; go = GameObject.Find("Showcase_Actor_" + idx); }
        if (go == null) { _sb.AppendLine("× 没有 actor"); yield return Done(); }
        _drg = go.GetComponentInChildren(_tyDrg, true);
        _drgT = ((Component)_drg).transform;
        _pSpineN = _tyDrg.GetProperty("SpineCount", BF);
        _mSpinePos = _tyDrg.GetMethod("GetSpinePosition", BF);
        _tyDrg.GetField("stormVfx", BF).SetValue(_drg, true);

        for (int i = 0; i < 30; i++) yield return null;
        _storm = null;
        for (int i = 0; i < 120 && _storm == null; i++)
        {
            _storm = _tyStorm.GetProperty("Instance", BFS).GetValue(null, null);
            if (_storm == null) yield return null;
        }
        if (_storm == null) { _sb.AppendLine("× 没有 DragonStormVfx"); yield return Done(); }

        // 条数一律顶到上限，去掉"条数在爬坡"这个变量
        string[] arcFields = { "arcsIdle", "arcsTell", "arcsDive", "arcsStrike", "arcsRecover", "arcsBreath", "arcsRoar" };
        for (int i = 0; i < arcFields.Length; i++)
        {
            var fi = _tyStorm.GetField(arcFields[i], BF);
            if (fi != null) fi.SetValue(_storm, 10f);
        }
        var fViewCam = _tyStorm.GetField("viewCamera", BF);
        var fBias = _tyStorm.GetField("arcCamBias", BF);
        var pArcN = _tyStorm.GetProperty("ActiveArcCount", BF);

        // ★ 加一档 bias=0 做交叉验证：如果 bias=0 反而看得见，那"推出身体"这个修法本身就选错了方向。
        float[] biases = { 0f, 0.8f, 0.8f, 3.0f, 2.0f };
        float[] ztests = { 4f, 4f, 8f, 4f, 8f };
        string[] names = {
            "bias0.0_ZTest4(不推)",
            "bias0.8_ZTest4(现状)",
            "bias0.8_ZTest8(Always)",
            "bias3.0_ZTest4",
            "bias2.0_ZTest8"
        };

        for (int k = 0; k < biases.Length; k++)
        {
            if (fBias != null) fBias.SetValue(_storm, biases[k]);
            var fZ = _tyStorm.GetField("arcZTest", BF);
            if (fZ != null) fZ.SetValue(_storm, ztests[k]);
            var mat = (Material)_tyStorm.GetField("_arcMat", BF).GetValue(_storm);
            if (mat != null && mat.HasProperty("_ZTest")) mat.SetFloat("_ZTest", ztests[k]);
            if (fViewCam != null) fViewCam.SetValue(_storm, _cam);

            // 等抖动重算（22 Hz、30 fps 下约 2 帧一次；等 30 帧足够）
            for (int i = 0; i < 30; i++) { Follow(); yield return null; }

            int arcN = (int)pArcN.GetValue(_storm, null);
            Follow();
            Shot("ab" + k);
            int corridor = 0, inCorr = 0, all = 0;
            Count(out corridor, out inCorr, out all);
            string st = DumpArcState();   // 材质属性 + 槽位 0 的网格/渲染器状态
            _sb.AppendLine("  [" + names[k] + "]  活跃电弧 " + arcN
                           + "  走廊 " + corridor + " px  走廊内青 " + inCorr
                           + "  全屏青 " + all + "  ｜ " + st);
        }

        _sb.AppendLine();
        _sb.AppendLine("★ 怎么读：");
        _sb.AppendLine("  · 「ZTest8(Always)」明显 > 「ZTest4(现状)」 ⇒ **被龙自己的皮深度剔除**；");
        _sb.AppendLine("    正解是加大 arcCamBias（本次三档里还带了 bias=3.0 那一档做交叉验证）。");
        _sb.AppendLine("  · 三档都 ≈ 0 ⇒ **根本没画出来**，去查条数/材质/网格，别再碰深度。");
        yield return Done();
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

    void Count(out int corridor, out int inCorr, out int all)
    {
        int n = (int)_pSpineN.GetValue(_drg, null);
        var pts = new Vector3[n];
        for (int i = 0; i < n; i++) { _arg1[0] = i; pts[i] = _cam.WorldToScreenPoint((Vector3)_mSpinePos.Invoke(_drg, _arg1)); }
        System.Array.Clear(_corr, 0, _corr.Length);
        const int R = 26;
        int xmin = W - 1, xmax = 0, ymin = H - 1, ymax = 0;
        for (int i = 0; i < n; i++)
        {
            xmin = Mathf.Min(xmin, Mathf.FloorToInt(pts[i].x) - R); xmax = Mathf.Max(xmax, Mathf.CeilToInt(pts[i].x) + R);
            ymin = Mathf.Min(ymin, Mathf.FloorToInt(pts[i].y) - R); ymax = Mathf.Max(ymax, Mathf.CeilToInt(pts[i].y) + R);
        }
        xmin = Mathf.Max(0, xmin); ymin = Mathf.Max(0, ymin);
        xmax = Mathf.Min(W - 1, xmax); ymax = Mathf.Min(H - 1, ymax);
        for (int y = ymin; y <= ymax; y++)
            for (int x = xmin; x <= xmax; x++)
                for (int i = 0; i + 1 < n; i++)
                    if (Dist(x + 0.5f, y + 0.5f, pts[i], pts[i + 1]) <= R) { _corr[y * W + x] = true; break; }
        corridor = 0; inCorr = 0; all = 0;
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                Color32 p = _px[y * W + x];
                if (!(p.b - p.r > 30 && p.g - p.r > 18)) continue;
                all++;
                if (_corr[y * W + x]) inCorr++;
            }
        for (int y = ymin; y <= ymax; y++)
            for (int x = xmin; x <= xmax; x++)
                if (_corr[y * W + x]) corridor++;
    }

    /// <summary>
    /// ★ 把"看起来应该生效"的东西**倒出来看**：材质到底有没有 `_ZTest`、值是多少、
    ///   槽位 0 的弧带是不是真的 active/enabled、网格有多少顶点。
    ///   这一条是"两档 A/B 只差 4% ⇒ 开始怀疑开关根本没生效"逼出来的。
    /// </summary>
    string DumpArcState()
    {
        var mat = (Material)_tyStorm.GetField("_arcMat", BF).GetValue(_storm);
        string ms = mat == null ? "mat=null" : ("mat=" + (mat.shader != null ? mat.shader.name : "null")
            + (mat.HasProperty("_ZTest") ? (" ZTest=" + mat.GetFloat("_ZTest")) : " 无_ZTest属性")
            + (mat.HasProperty("_Intensity") ? (" I=" + mat.GetFloat("_Intensity")) : ""));
        var arcs = (System.Collections.IList)_tyStorm.GetField("_arcs", BF).GetValue(_storm);
        if (arcs == null || arcs.Count == 0) return ms + " ｜ 无槽位";
        object a0 = arcs[0];
        var tArc = a0.GetType();
        var go = (GameObject)tArc.GetField("go").GetValue(a0);
        var mr = (MeshRenderer)tArc.GetField("mr").GetValue(a0);
        var mesh = (Mesh)tArc.GetField("mesh").GetValue(a0);
        var verts = (Vector3[])tArc.GetField("verts").GetValue(a0);
        var spanV = (bool)tArc.GetField("spanValid").GetValue(a0);
        return ms + " ｜ 槽0 go.active=" + (go != null && go.activeSelf)
               + " mr.enabled=" + (mr != null && mr.enabled)
               + " verts=" + (mesh != null ? mesh.vertexCount : -1)
               + " spanValid=" + spanV
               + " v0=" + verts[0].ToString("F2") + " v1=" + verts[1].ToString("F2");
    }

    static float Dist(float px, float py, Vector3 a, Vector3 b)
    {
        float vx = b.x - a.x, vy = b.y - a.y;
        float L2 = vx * vx + vy * vy;
        float t = L2 > 1e-6f ? Mathf.Clamp01(((px - a.x) * vx + (py - a.y) * vy) / L2) : 0f;
        float dx = px - (a.x + vx * t), dy = py - (a.y + vy * t);
        return Mathf.Sqrt(dx * dx + dy * dy);
    }

    void Shot(string tag)
    {
        var prev = RenderTexture.active;
        _cam.Render();
        RenderTexture.active = _cam.targetTexture;
        var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
        tex.Apply();
        _px = tex.GetPixels32();
        byte[] png = tex.EncodeToPNG();
        RenderTexture.active = prev;
        UnityEngine.Object.Destroy(tex);
        try { File.WriteAllBytes(DIR + "/" + tag + ".png", png); } catch { }
    }

    Camera MakeCam()
    {
        var g = new GameObject("PROBE_CAM_AB");
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
        Debug.Log("[drg_storm_ab] 写入 " + RP);
        yield return null;
    }
}

var __hostAB = new GameObject("drg_storm_ab");
__hostAB.AddComponent<drg_storm_ab>();
return "DRG_STORM_AB_STARTED";
