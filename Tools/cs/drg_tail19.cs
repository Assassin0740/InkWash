using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;

// drg_tail19.cs —— 第十九轮：尾巴到底有没有「跟上身体的 sin」
//
// 用户反馈原话：「现在这个尾巴还是没有跟上sin的」。
// 代码层定位到两个独立缺陷（见 ApplyTailLevel 顶部长注释）：
//   ① 尾波**传播方向与身体相反**（两条波对着撞）
//   ② 接点切向不连续（横向分量 π·bodyAmp·gain : tailWaveAmp ≈ 3.61 : 0.35，差 10 倍）
//
// 本探针做三件事：
//   A. 修前 / 修后**同机位俯视**各录 100 帧（每 2 tick 一帧，覆盖**整 3 个周期** ⇒ 视频无缝循环）
//   B. 每 tick 采样一次全链横向廓形，给出三条**对目标行为敏感**的判据：
//        ① 接点折角 kink = Angle(身体首节方向, −尾巴首节方向)   —— 连续的话恒 ≈ 0°
//        ② 尾尖横向摆幅 p2p                                      —— 尾巴有没有参与
//        ③ 帧间廓形互相关位移（身体段 vs 尾巴段分别算）           —— 两段传播方向是否同向
//   C. 编译新鲜度第三层：回读新字段（DLL 里有 ≠ 运行时程序集里有 ≠ 字段值对）
//
// ★ 为什么必须钉 Time.captureFramerate：见坑表 27（不钉的话帧步长跟编辑器帧率走，
//   相位采样会错位，"整 3 个周期"这个前提就不成立）。

public class drg_tail19 : MonoBehaviour
{
    const string DIR = "D:/Unity Project/InkWash/Tools/screenshots/enemies/T19";
    const string RP = "D:/Unity Project/InkWash/Tools/reports/drg_tail19.txt";
    const string KEY = "墨龙";

    const int FPS = 30;
    const int TICKS = 200;     // 200 tick @30fps = 6.667 s = 3.000 个周期（周期 1/0.45 = 2.2222 s）
    const int SNAP_EVERY = 2;  // 每 2 tick 存一帧 ⇒ 100 帧
    const float DRIVE_HZ = 0.45f;

    static readonly BindingFlags BF = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;

    readonly StringBuilder _sb = new StringBuilder();
    Component _sc; Type _ty, _dt;
    MethodInfo _mPlay, _mLabel, _mStop;
    PropertyInfo _pCount;
    Camera _cam;
    GameObject _go;
    Transform _drgT;
    System.Collections.IList _spine, _tailChain;

    // 廓形站点：0..NT-1 = 尾尖→尾根；NT..NT+NS-1 = 脊柱 0（尾侧）→ 23（头）
    int _NT, _NS;
    Transform[] _st;
    float[] _lat;          // 当前帧廓形
    float[] _latPrev;      // 上一 tick 廓形
    readonly List<float[]> _prof = new List<float[]>();
    readonly List<float> _profT = new List<float>();
    readonly List<float> _kink = new List<float>();
    readonly List<float> _tipLat = new List<float>();
    readonly List<float> _midLat = new List<float>();

    int _n;

    void Start() { StartCoroutine(Run()); }

    IEnumerator Run()
    {
        yield return null; yield return null;
        Time.captureFramerate = FPS;

        Directory.CreateDirectory(DIR);
        foreach (var f in Directory.GetFiles(DIR, "*.png")) { try { File.Delete(f); } catch { } }

        foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (_ty == null) { var t1 = a.GetType("InkWash.DebugTools.ActionShowcase"); if (t1 != null) _ty = t1; }
            if (_dt == null) { var t2 = a.GetType("InkWash.Enemies.EnemyDragon"); if (t2 != null) _dt = t2; }
        }
        if (_ty == null || _dt == null) { _sb.AppendLine("× 类型缺失"); yield return Done(); }

        foreach (var o in UnityEngine.Object.FindObjectsOfType(_ty, true)) { _sc = o as Component; if (_sc != null) break; }
        if (_sc == null) { _sb.AppendLine("× 场景里没有 ActionShowcase（先跑 q_open_scene.cs）"); yield return Done(); }

        _mPlay = _ty.GetMethod("PlayForCapture", BF);
        _mLabel = _ty.GetMethod("ItemLabel", BF);
        _mStop = _ty.GetMethod("StopAutoClose", BF);
        _pCount = _ty.GetProperty("ItemCount", BF);

        int count = (int)_pCount.GetValue(_sc, null);
        int idx = -1; string found = "";
        for (int i = 0; i < count; i++)
        {
            string lb = (string)_mLabel.Invoke(_sc, new object[] { i });
            if (lb.Contains(KEY) && lb.Contains("盘旋")) { idx = i; found = lb; break; }
        }
        if (idx < 0) { _sb.AppendLine("× 没找到「墨龙 + 盘旋」条目"); yield return Done(); }

        _mStop.Invoke(_sc, null);
        _mPlay.Invoke(_sc, new object[] { idx });
        for (int i = 0; i < 90; i++) yield return null;

        _go = GameObject.Find("Showcase_Actor_" + idx);
        if (_go == null) { _sb.AppendLine("× 没找到 actor 实例"); yield return Done(); }

        var drg = _go.GetComponent(_dt);
        if (drg == null) { _sb.AppendLine("× actor 上没有 EnemyDragon"); yield return Done(); }
        _drgT = drg.transform;
        _spine = _dt.GetField("_spine", BF).GetValue(drg) as System.Collections.IList;
        _tailChain = _dt.GetField("_tailChain", BF).GetValue(drg) as System.Collections.IList;

        // ── 编译新鲜度第三层：新字段必须存在，且值要能回读 ──
        var fFollow = _dt.GetField("tailFollowBodyWave", BF);
        var fGain = _dt.GetField("tailFollowGain", BF);
        if (fFollow == null || fGain == null)
        {
            _sb.AppendLine("× 第十九轮字段缺失（tailFollowBodyWave / tailFollowGain）⇒ 运行时程序集是旧的");
            yield return Done();
        }
        if (_spine == null || _spine.Count < 3 || _tailChain == null || _tailChain.Count < 3)
        { _sb.AppendLine("× 脊柱 / 尾链节数不足"); yield return Done(); }

        _sb.AppendLine("=== drg_tail19：尾巴是否跟上身体的 sin（第十九轮）===");
        _sb.AppendLine("条目 idx=" + idx + "  label=" + found);
        _sb.AppendLine("帧步长钉死 " + FPS + " fps；每模式 " + TICKS + " tick = "
                       + (TICKS / (float)FPS).ToString("F3") + " s = "
                       + (TICKS / (float)FPS * DRIVE_HZ).ToString("F3") + " 个驱动周期");
        _sb.AppendLine("每 " + SNAP_EVERY + " tick 存一帧 ⇒ 每模式 " + (TICKS / SNAP_EVERY) + " 帧");
        _sb.AppendLine();

        // ── 运行期强制（不写资产）：巡游（正常移动）+ 关省电循环（幅度恒定，排除静默段干扰）──
        _sb.AppendLine("── 运行期强制（不落盘）──");
        SetAndReport(drg, "hoverStationary", false, false);
        SetAndReport(drg, "enablePerformanceCycle", false, false);
        _sb.AppendLine();

        _sb.AppendLine("── 生效参数 ──");
        foreach (var nm in new string[] { "hoverStationary", "hoverStationaryYawDeg", "swimWaveAmp", "swimWaveCount",
                                          "swimWaveFreq", "swimWaveTailGain", "swimWaveHeadGain",
                                          "enablePerformanceCycle", "limbSwingDeg", "limbSweepBackDeg",
                                          "tailLevel", "tailFollowBodyWave", "tailFollowGain",
                                          "tailWaveAmp", "tailWaveSpan", "tailPhaseLagDeg",
                                          "headAlignPitchDeg", "headAlignLinks" })
            _sb.AppendLine("  " + Pad(nm, 24) + " = " + _dt.GetField(nm, BF).GetValue(drg));
        _sb.AppendLine();

        _NS = _spine.Count;
        _NT = _tailChain.Count;
        _sb.AppendLine("脊柱节数 = " + _NS + "   尾巴链节数 = " + _NT);
        _sb.AppendLine();

        // ── 站点表：尾尖 → 尾根 → 脊柱 0 → 脊柱 23（头）──
        var st = new List<Transform>();
        for (int i = _NT - 1; i >= 0; i--) st.Add(_tailChain[i] as Transform);
        for (int i = 0; i < _NS; i++) st.Add(_spine[i] as Transform);
        _st = st.ToArray();
        for (int i = 0; i < _st.Length; i++)
            if (_st[i] == null) { _sb.AppendLine("× 站点 " + i + " 为空"); yield return Done(); }
        _lat = new float[_st.Length];
        _latPrev = new float[_st.Length];

        // ══════════ 第一遍：修后（跟随身体波形） ══════════
        _sb.AppendLine("── 第一遍：修后 tailFollowBodyWave = true ──");
        fFollow.SetValue(drg, true);
        yield return MeasureAndSnap(drg, "a", TICKS);
        var afterProf = new List<float[]>(_prof);
        var afterT = new List<float>(_profT);
        var afterKink = new List<float>(_kink);
        var afterTip = new List<float>(_tipLat);
        var afterMid = new List<float>(_midLat);

        // ══════════ 第二遍：修前（旧版独立行波） ══════════
        _sb.AppendLine();
        _sb.AppendLine("── 第二遍：修前 tailFollowBodyWave = false（旧版独立行波）──");
        fFollow.SetValue(drg, false);
        yield return MeasureAndSnap(drg, "b", TICKS);

        // ── 汇总 ──
        _sb.AppendLine();
        _sb.AppendLine("══════ 判据 ══════");
        _sb.AppendLine();
        _sb.AppendLine("① 接点折角 kink = Angle(身体首节方向, −尾巴首节方向)  —— 波形连续的话恒 ≈ 0°");
        Report("修后 follow = true ", afterKink);
        Report("修前 follow = false", _kink);
        _sb.AppendLine();
        _sb.AppendLine("② 横向摆幅（p2p，单位 m）");
        _sb.AppendLine("   " + Pad("", 22) + Pad("尾尖", 14) + "身体中段");
        _sb.AppendLine("   " + Pad("修后 follow = true", 22) + Pad(P2P(afterTip), 14) + P2P(afterMid));
        _sb.AppendLine("   " + Pad("修前 follow = false", 22) + Pad(P2P(_tipLat), 14) + P2P(_midLat));
        _sb.AppendLine();
        _sb.AppendLine("③ 帧间廓形互相关位移（站为单位；负 = 波形往「尾尖」方向走）");
        _sb.AppendLine("   身体段与尾巴段**同号 = 同向传播**；异号 = 两条波对着撞");
        _sb.AppendLine("   " + Pad("", 22) + Pad("身体段 kBody", 18) + "尾巴段 kTail");
        var a3 = XCorr2(afterProf, afterT);
        _sb.AppendLine("   " + Pad("修后 follow = true", 22) + Pad(a3[0].ToString("F3"), 18) + a3[1].ToString("F3"));
        var b3 = XCorr2(_prof, _profT);
        _sb.AppendLine("   " + Pad("修前 follow = false", 22) + Pad(b3[0].ToString("F3"), 18) + b3[1].ToString("F3"));
        _sb.AppendLine();
        _sb.AppendLine("④ 解调空间相位斜率（rad/m，沿链从尾尖到头的方向）");
        _sb.AppendLine("   跟随模式：整条链（含接点）应当是**同一个斜率**（= 2π·W/身体总骨长）");
        PhaseSlope("修后 follow = true ", afterProf, afterT);
        PhaseSlope("修前 follow = false", _prof, _profT);

        _sb.AppendLine();
        _sb.AppendLine("PNG：T19/t19a_%04d.png（修后）＋ T19/t19b_%04d.png（修前），各 " + (TICKS / SNAP_EVERY) + " 帧");
        if (_cam != null) UnityEngine.Object.Destroy(_cam.gameObject);
        yield return Done();
    }

    // ──────────────────────────────────────────────────────────────────────
    IEnumerator MeasureAndSnap(Component drg, string tag, int ticks)
    {
        _prof.Clear(); _profT.Clear(); _kink.Clear(); _tipLat.Clear(); _midLat.Clear();
        _n = 0;
        yield return null;
        for (int k = 0; k < ticks; k++)
        {
            Sample(drg);
            if (k % SNAP_EVERY == 0) Snap(tag);
            yield return null;
        }
    }

    void Sample(Component drg)
    {
        Vector3 fwd = Flat(_drgT.forward).normalized;
        if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.forward;
        Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized;
        Vector3 origin = (_spine[0] as Transform).position;

        for (int i = 0; i < _st.Length; i++)
            _lat[i] = Vector3.Dot(_st[i].position - origin, right);

        // 接点折角：身体首节（朝头）与尾巴首节（朝尾）应当共线 ⇒ Angle(b, −t) ≈ 0
        Vector3 b0 = ((_spine[1] as Transform).position - (_spine[0] as Transform).position).normalized;
        Vector3 t0 = ((_tailChain[1] as Transform).position - (_tailChain[0] as Transform).position).normalized;
        _kink.Add(Vector3.Angle(b0, -t0));

        _tipLat.Add(_lat[0]);                       // 站点 0 = 尾尖
        _midLat.Add(_lat[_NT + _NS / 2]);           // 身体中段

        var copy = new float[_lat.Length];
        Array.Copy(_lat, copy, _lat.Length);
        _prof.Add(copy);
        _profT.Add(Time.time);
    }

    // ── 统计 ──
    void Report(string label, List<float> v)
    {
        float mn = float.MaxValue, mx = float.MinValue, sum = 0f;
        for (int i = 0; i < v.Count; i++) { mn = Mathf.Min(mn, v[i]); mx = Mathf.Max(mx, v[i]); sum += v[i]; }
        _sb.AppendLine("   " + Pad(label, 22) + "min " + Pad(mn.ToString("F2") + "°", 10)
                       + "max " + Pad(mx.ToString("F2") + "°", 10)
                       + "均值 " + Pad((sum / Mathf.Max(1, v.Count)).ToString("F2") + "°", 10)
                       + "极差 " + (mx - mn).ToString("F2") + "°");
    }

    static string P2P(List<float> v)
    {
        float mn = float.MaxValue, mx = float.MinValue;
        for (int i = 0; i < v.Count; i++) { mn = Mathf.Min(mn, v[i]); mx = Mathf.Max(mx, v[i]); }
        return (mx - mn).ToString("F3");
    }

    /// <summary>
    /// 帧间廓形互相关：找整数位移 k 使 P_f[s] ≈ P_{f+1}[s+k]（两两平均）。
    /// 返回 [身体段平均 k, 尾巴段平均 k]。负 k = 廓形往站号小的方向（尾尖）移动。
    /// </summary>
    float[] XCorr2(List<float[]> prof, List<float> t)
    {
        int NS = _st.Length;
        int bodyLo = _NT, bodyHi = NS - 1;      // 脊柱段
        int tailLo = 0, tailHi = _NT - 1;       // 尾链段
        double sb = 0; int nb = 0;
        double st = 0; int nt = 0;
        for (int f = 0; f + 1 < prof.Count; f++)
        {
            float[] A = prof[f], B = prof[f + 1];
            int kb = BestShift(A, B, bodyLo, bodyHi);
            int kt = BestShift(A, B, tailLo, tailHi);
            if (kb != int.MinValue) { sb += kb; nb++; }
            if (kt != int.MinValue) { st += kt; nt++; }
        }
        return new float[] { nb > 0 ? (float)(sb / nb) : 0f, nt > 0 ? (float)(st / nt) : 0f };
    }

    static int BestShift(float[] A, float[] B, int lo, int hi)
    {
        int bestK = int.MinValue; double best = double.MaxValue; int cnt = 0;
        for (int k = -3; k <= 3; k++)
        {
            double e = 0; int c = 0;
            for (int s = lo; s <= hi; s++)
            {
                int j = s + k;
                if (j < lo || j > hi) continue;
                double d = A[s] - B[j];
                e += d * d; c++;
            }
            if (c < 3) continue;
            if (e < best) { best = e; bestK = k; cnt = c; }
        }
        return cnt >= 3 ? bestK : int.MinValue;   // 注意：k=0 是合法结果，别用 0 当"失败"
    }

    /// <summary>
    /// 解调空间相位：对每个站点把 lat(t) 与 exp(−i·2π·f·t) 做相关，取相位。
    /// 对 lat = A·sin(ωt + ψ)，有 Σ lat·e^{−iωt} = (N/2)A·e^{i(ψ−π/2)} ⇒ ψ = atan2 + π/2。
    /// 再对**站间弧长**求斜率（用相邻站点距离做权重），报尾巴段 / 身体段各自的斜率。
    /// </summary>
    void PhaseSlope(string label, List<float[]> prof, List<float> t)
    {
        int NS = _st.Length, N = prof.Count;
        if (N < 8) { _sb.AppendLine("   " + label + " 样本不足"); return; }
        var psi = new float[NS];
        var amp = new float[NS];
        for (int s = 0; s < NS; s++)
        {
            double re = 0, im = 0;
            for (int f = 0; f < N; f++)
            {
                double ph = -2.0 * Math.PI * DRIVE_HZ * t[f];
                re += prof[f][s] * Math.Cos(ph);
                im += prof[f][s] * Math.Sin(ph);
            }
            double a = Math.Sqrt(re * re + im * im) * 2.0 / N;      // 振幅
            double p = Math.Atan2(im, re) + Math.PI / 2.0;           // ψ
            amp[s] = (float)a;
            psi[s] = (float)p;
        }
        // 按相邻站点弧长解缠 + 求斜率
        float slopeTail = FitSlope(psi, amp, 0, _NT - 1);
        float slopeBody = FitSlope(psi, amp, _NT, NS - 1);
        float ampTip = amp[0], ampMid = amp[_NT + _NS / 2];
        _sb.AppendLine("   " + Pad(label, 22) + "尾巴段斜率 " + Pad(slopeTail.ToString("F3"), 9)
                       + "身体段斜率 " + Pad(slopeBody.ToString("F3"), 9)
                       + "｜尾尖振幅 " + Pad(ampTip.ToString("F3"), 8) + "身体中段振幅 " + ampMid.ToString("F3"));
    }

    /// <summary>相邻站点解缠后线性拟合 ψ vs 弧长，返回斜率（rad/m）。</summary>
    float FitSlope(float[] psi, float[] amp, int lo, int hi)
    {
        int n = hi - lo + 1;
        if (n < 3) return 0f;
        var arc = new float[n];
        arc[0] = 0f;
        for (int i = 1; i < n; i++)
            arc[i] = arc[i - 1] + Vector3.Distance(_st[lo + i - 1].position, _st[lo + i].position);
        float unwrap = 0f; var un = new float[n]; un[0] = 0f;
        float prev = psi[lo];
        for (int i = 1; i < n; i++)
        {
            float d = psi[lo + i] - prev;
            while (d > Mathf.PI) d -= 2f * Mathf.PI;
            while (d < -Mathf.PI) d += 2f * Mathf.PI;
            // 相邻站点相位差通常在 (0, π)，解缠到 (−π/2, 3π/2)
            unwrap += d;
            un[i] = unwrap;
            prev = psi[lo + i];
        }
        // 最小二乘 y = k·x
        double sxy = 0, sxx = 0;
        for (int i = 0; i < n; i++) { sxy += arc[i] * un[i]; sxx += arc[i] * arc[i]; }
        if (sxx < 1e-9) return 0f;
        return (float)(sxy / sxx);
    }

    // ── 出帧（俯视：横向波形看得最清楚）──
    void Snap(string tag)
    {
        if (_cam == null) _cam = MakeCam();
        Vector3 c = Vector3.zero;
        for (int i = 0; i < _st.Length; i++) c += _st[i].position;
        c /= _st.Length;
        float rad = 0f;
        for (int i = 0; i < _st.Length; i++) rad = Mathf.Max(rad, (_st[i].position - c).magnitude);
        rad += 0.6f;

        Vector3 fwd = Flat(_drgT.forward).normalized;
        // 俯视 + 一点点前倾，避免 LookAt 与 up 退化
        Vector3 dir = (Vector3.up * 3.4f + fwd * 0.30f).normalized;
        float dist = rad / Mathf.Sin(_cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.06f;
        _cam.transform.position = c + dir * dist;
        _cam.transform.LookAt(c, Vector3.up);

        int W = 1120, H = 630;
        var rt = new RenderTexture(W, H, 24);
        _cam.targetTexture = rt;
        _cam.Render();
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
        tex.Apply();
        File.WriteAllBytes(DIR + "/t19" + tag + "_" + _n.ToString("D4") + ".png", tex.EncodeToPNG());
        RenderTexture.active = prev;
        UnityEngine.Object.Destroy(tex);
        UnityEngine.Object.Destroy(rt);
        _cam.targetTexture = null;
        _n++;
    }

    Camera MakeCam()
    {
        var g = new GameObject("PROBE_CAM_T19");
        var c = g.AddComponent<Camera>();
        c.clearFlags = CameraClearFlags.SolidColor;
        c.backgroundColor = new Color(0.86f, 0.88f, 0.91f, 1f);
        c.fieldOfView = 40f;
        c.nearClipPlane = 0.05f;
        c.farClipPlane = 5000f;
        c.enabled = false;      // ★ enabled=false 时 Render() 只清屏不画模型（坑表三-7），
        return c;               //   但本探针是手动 Render()，与 drg_loop 同款写法，已验证可出图
    }

    void SetAndReport(Component drg, string field, object value, bool onlyIfDiff)
    {
        var f = _dt.GetField(field, BF);
        if (f == null) { _sb.AppendLine("  " + Pad(field, 24) + " 字段不存在"); return; }
        var before = f.GetValue(drg);
        f.SetValue(drg, value);
        _sb.AppendLine("  " + Pad(field, 24) + " " + before + " → " + f.GetValue(drg));
    }

    static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
    static string Pad(string s, int n) { return s.Length >= n ? s : s + new string(' ', n - s.Length); }

    IEnumerator Done()
    {
        Time.captureFramerate = 0;
        try { File.WriteAllText(RP, _sb.ToString()); } catch { }
        Debug.Log("[drg_tail19] 写入 " + RP);
        yield return null;
    }
}

var __hostT19 = new GameObject("drg_tail19");
__hostT19.AddComponent<drg_tail19>();
return "DRG_TAIL19_STARTED";
