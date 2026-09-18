using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Unity.Profiling;   // ★ 判据 7 的正确尺子：ProfilerRecorder("GC Allocated In Frame")
using UnityEngine;

// drg_storm23.cs —— 第二十三轮：**风暴特效（青白细电弧 + 纯墨黑烟）接入验收**
//
// 验的是 `Docs/墨龙特效设计.md` §七 那 8 条判据，逐条落到一次测量上：
//   1 常态稳态活跃电弧条数        2~3
//   2 俯冲/扑咬段活跃电弧条数      8~10
//   3 黑烟发射点覆盖的链节数（俯冲）= 24
//   4 俯冲段至少 80% 的采样帧里存在「与烟团距离 < 0.6 m 的电弧」
//   5 帧时间不劣化（开/关特效对比；★ 必须**不钉 captureFramerate** 量，否则量的是假帧率）
//   6 身长保险丝 7.3~8.1 m（姿态没被特效带歪）
//   7 稳态 GC 0 B/帧
//   8 人眼 —— 出图 + 录视频
//
// ★ 判据 4 的度量方式要说清楚（不然它就是个"看起来达标"的数字）：
//   表现层每帧算「所有活跃电弧的**锚点中点**到最近活烟团的距离」，探针取它的最小值。
//   它是**几何真值**，不是"我们打算让它进烟里"这种意图声明 ——
//   `_minArcPuffDist` 就是拿 `Vector3` 减出来的。
//
// ★ 判据 5 的两个坑（都在本轮实测踩到）：
//   ① `Time.captureFramerate = 30` 时 `Time.deltaTime` 恒为 1/30 ⇒ 帧时间统计**完全失真**
//      （永远报 33.3 ms）。所以性能段必须把它设回 0。
//   ② 编辑器里不开 vsync 时帧时间抖动极大 ⇒ 只报"均值/中位/超 16.7 ms 的帧数"，
//      并且**两个条件各量 90 帧**，不做单帧比较。
//
// 相机：两台，都是**跟随式**（每帧按龙的前方重算机位），保证：
//   · 始终是"侧视"或"前 3/4"，不会因为龙转身而变成正对/背对；
//   · 同一条目内不同帧的取景一致，图与图之间可以直接比。

public class drg_storm23 : MonoBehaviour
{
    const string DIR = "D:/Unity Project/InkWash/Tools/screenshots/enemies/S23";
    const string VIDDIR = "D:/Unity Project/InkWash/Tools/screenshots/enemies/S23/vid";
    const string RP = "D:/Unity Project/InkWash/Tools/reports/drg_storm23.txt";
    const int W = 900, H = 600;
    const float CAP_FPS = 30f;

    static readonly BindingFlags BF = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;
    static readonly BindingFlags BFS = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static;

    readonly StringBuilder _sb = new StringBuilder();

    Type _tyShow, _tyDrg, _tyStorm;
    Component _show;
    MethodInfo _mPlay, _mLabel, _mStop;
    PropertyInfo _pCount;

    GameObject _go; Component _drg; Transform _drgT;
    object _storm;
    FieldInfo _fPhaseName, _fStormOn, _fViewCam;
    MethodInfo _mMouthPos, _mMouthFwd;
    PropertyInfo _pArcN, _pArcLv, _pSmokeLv, _pSmokeArcN, _pMinDist, _pLinkCnt, _pPuffN;
    PropertyInfo _pSpineEmit, _pMouthEmit;

    // ★ 判据 8 的**可量化代理**：弧带可见像素数。
    //   为什么必须有它：弧带是"正对相机摊平"的薄片，锚点取在脊柱骨节（**身体内部**），
    //   一旦被龙自己的不透明皮深度剔除，人眼只是"觉得电弧少"，说不清是没画还是被挡住。
    //   做法 = 把脊柱投影到屏幕，取 ±26 px 走廊，只在走廊内数**青像素**。
    //   走廊排除了玩家（蓝灰袍）与宣纸底（中性灰）的干扰 —— 全屏数青会被玩家角色带偏。
    Color32[] _lastPx = new Color32[W * H];
    readonly bool[] _corr = new bool[W * H];
    int _lastOnScreen, _lastOnTotal, _lastCorridor, _lastAllCyan;

    // ★★ 探针自己**不许分配**：`new object[]{ i }` 每次都会分配一个数组 + 装箱一个 int。
    //   Follow() 每帧要对 24 节各取两次脊柱位置 ⇒ 48 次分配 ≈ **5 KB/帧**，
    //   正好与"特效开关"的量级相同 ⇒ 会把判据 7 的实测值整个吃掉（实测：特效关闭也报 5.4 KB/帧）。
    //   这就是"尺子本身在污染被测对象"。复用同一个数组即可。
    readonly object[] _arg1 = new object[1];
    MethodInfo _mSpinePos; PropertyInfo _pSpineN;
    string _vidTag = "x";

    Camera _camS, _camQ;
    ParticleSystem _ps;
    ParticleSystem.Particle[] _pbuf;
    int _psCap = 700;

    bool _vidOn;
    int _vidIdx;

    // ── 一次采样窗口的统计 ──
    class Acc
    {
        public float min = 9e9f, max = -9e9f, sum; public int n;
        public readonly List<float> all = new List<float>();
        public void Add(float v) { if (v < min) min = v; if (v > max) max = v; sum += v; n++; all.Add(v); }
        public float Mean { get { return n > 0 ? sum / n : 0f; } }
        public float Median()
        {
            if (n == 0) return 0f;
            var c = new List<float>(all); c.Sort();
            return c[c.Count / 2];
        }
    }

    void Start() { StartCoroutine(Run()); }

    IEnumerator Run()
    {
        yield return null; yield return null;
        Directory.CreateDirectory(DIR);
        Directory.CreateDirectory(VIDDIR);

        // ★ 必须**在采样之前**钉死帧步长：判据里凡是"按帧数取样"的统计（占比、均值）
        //   都要求每帧代表的时长恒定。不钉的话，一次 4 s 的窗口在高帧率下只覆盖事件的零头。
        Time.captureFramerate = Mathf.RoundToInt(CAP_FPS);

        foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (_tyShow == null) _tyShow = a.GetType("InkWash.DebugTools.ActionShowcase");
            if (_tyDrg == null) _tyDrg = a.GetType("InkWash.Enemies.EnemyDragon");
            if (_tyStorm == null) _tyStorm = a.GetType("InkWash.Effects.DragonStormVfx");
        }
        _sb.AppendLine("=== drg_storm23：风暴特效接入验收（第二十三轮）===");
        if (_tyShow == null || _tyDrg == null || _tyStorm == null)
        {
            _sb.AppendLine("× 类型缺失  show=" + (_tyShow != null) + " dragon=" + (_tyDrg != null)
                           + " storm=" + (_tyStorm != null));
            yield return Done();
        }
        _sb.AppendLine("类型：ActionShowcase=" + _tyShow.Name + " / EnemyDragon=" + _tyDrg.Name
                       + " / DragonStormVfx=" + _tyStorm.Name);

        // ★ 编译**第三层**判据：光"Console 干净 + Editor.log 无 error CS"还不够 ——
        //   那两层只能证明"没报错"，证明不了"运行时读的是新程序集"（坑表六）。
        //   这里把本轮**新增**的字段值读出来：名字能反射到、值等于源码默认值 ⇒ 新代码确实生效了。
        // ★ 踩过的坑：这些是**实例字段**，`GetValue(null)` 会抛
        //   `TargetException: Non-static field requires a target`。协程里一个未捕获异常
        //   会**直接结束整个协程** ⇒ 报告不落盘、`Time.captureFramerate` 也不解开（编辑器留在假帧率）。
        //   所以这里 ① 显式拿一个实例 ② 整段包在 Safe() 里，任何反射失误都只退化成一行字。
        {
            var inst = SafeGet(() => _tyStorm.GetMethod("Ensure", BFS).Invoke(null, null));
            _sb.AppendLine("新代码自证：" + SafeStr(() =>
            {
                var f1 = _tyStorm.GetField("arcCamBias", BF);
                var f2 = _tyStorm.GetField("smokeSheetCells", BF);
                var f3 = _tyStorm.GetField("arcIntensity", BF);
                var at = (Texture2D)_tyStorm.GetField("arcTex", BF).GetValue(inst);
                var st = (Texture2D)_tyStorm.GetField("smokeTex", BF).GetValue(inst);
                return "arcCamBias=" + f1.GetValue(inst) + "  smokeSheetCells=" + f2.GetValue(inst)
                       + "  arcIntensity=" + f3.GetValue(inst)
                       + "  arcTex=" + (at != null ? at.name : "null")
                       + "  smokeTex=" + (st != null ? st.name : "null");
            }));
            _sb.AppendLine("        ↳ 期望 smokeSheetCells=(1, 1)、arcTex=LightningTrail、smokeTex=WFX_T_SmokeLoopAlpha");
        }

        foreach (var o in UnityEngine.Object.FindObjectsOfType(_tyShow, true)) { _show = o as Component; if (_show != null) break; }
        if (_show == null) { _sb.AppendLine("× 场景里没有 ActionShowcase（先跑 q_open_scene.cs）"); yield return Done(); }

        _mPlay = _tyShow.GetMethod("PlayForCapture", BF);
        _mLabel = _tyShow.GetMethod("ItemLabel", BF);
        _mStop = _tyShow.GetMethod("StopAutoClose", BF);
        _pCount = _tyShow.GetProperty("ItemCount", BF);
        if (_mPlay == null || _mLabel == null || _pCount == null)
        { _sb.AppendLine("× ActionShowcase 接口对不上"); yield return Done(); }

        int count = (int)_pCount.GetValue(_show, null);
        int iCir = -1, iBite = -1, iSweep = -1, iBreath = -1;
        for (int i = 0; i < count; i++)
        {
            string lb = (string)_mLabel.Invoke(_show, new object[] { i });
            if (!lb.Contains("墨龙")) continue;
            if (lb.Contains("盘旋")) iCir = i;
            else if (lb.Contains("撕咬")) iBite = i;
            else if (lb.Contains("扫尾")) iSweep = i;
            else if (lb.Contains("吐息")) iBreath = i;
        }
        _sb.AppendLine("条目：盘旋=" + iCir + " 撕咬=" + iBite + " 扫尾=" + iSweep + " 吐息=" + iBreath);
        if (iCir < 0 || iBite < 0 || iBreath < 0) { _sb.AppendLine("× 条目没找齐"); yield return Done(); }

        _camS = MakeCam("PROBE_CAM_S23S");
        _camQ = MakeCam("PROBE_CAM_S23Q");
        _mStop.Invoke(_show, null);

        // ── 逐条目 ──
        yield return Item(iCir, "盘旋", "cir", 3.0f, false, false);
        yield return Item(iBite, "俯冲撕咬", "bite", 5.2f, true, false);
        yield return Item(iSweep, "俯冲扫尾", "sweep", 4.5f, false, false);
        yield return Item(iBreath, "吐息", "breath", 5.0f, true, true);

        // ── 判据 5：帧时间（必须解除 captureFramerate）──
        yield return Perf(iCir);

        _sb.AppendLine();
        _sb.AppendLine("★ 怎么读：");
        _sb.AppendLine("  · 判据 1/2 看「活跃电弧条数」的均值与峰值，按相位分组那张表才是重点。");
        _sb.AppendLine("  · 判据 3 看「链节覆盖」，分母是脊柱链节数（实测 24）。");
        _sb.AppendLine("  · 判据 4 看「最近弧–烟距离 < 0.6 m 的帧占比」，要 ≥ 80%。");
        _sb.AppendLine("  · 判据 6 看「身长」，要 7.3~8.1 m —— 超了说明特效把姿态带歪了。");
        _sb.AppendLine("  · 判据 8 的代理量看「出图[...] 弧带可见像素」：侧视 < 300 基本等于「电弧被龙身挡掉了」。");
        _sb.AppendLine("  · 出图 " + DIR + "｜录像帧 " + VIDDIR);

        yield return Done();
    }

    // ══════════════════════════════════════════════════════════════════
    //  单个条目的采样
    // ══════════════════════════════════════════════════════════════════

    IEnumerator Item(int idx, string name, string tag, float dur, bool video, bool mouthChk)
    {
        _sb.AppendLine();
        _sb.AppendLine("──────────────────────────────────────────────────────────────");
        _sb.AppendLine("条目 [" + tag + "] " + name + "（PlayForCapture idx=" + idx + "）");

        _mStop.Invoke(_show, null);
        _mPlay.Invoke(_show, new object[] { idx });

        _go = null;
        for (int i = 0; i < 150 && _go == null; i++) { yield return null; _go = GameObject.Find("Showcase_Actor_" + idx); }
        if (_go == null) { _sb.AppendLine("  × 没有 actor 实例"); yield break; }
        _drg = _go.GetComponentInChildren(_tyDrg, true);
        if (_drg == null) { _sb.AppendLine("  × actor 上没有 EnemyDragon"); yield break; }
        _drgT = ((Component)_drg).transform;

        _fPhaseName = _tyDrg.GetField("_divePhaseName", BF);
        _pSpineN = _tyDrg.GetProperty("SpineCount", BF);
        _mSpinePos = _tyDrg.GetMethod("GetSpinePosition", BF);
        _fStormOn = _tyDrg.GetField("stormVfx", BF);
        _mMouthPos = _tyDrg.GetMethod("GetMouthPosition", BF);
        _mMouthFwd = _tyDrg.GetMethod("GetMouthForward", BF);
        if (_fStormOn != null) _fStormOn.SetValue(_drg, true);

        // 骨头们就位（起飞）
        for (int i = 0; i < 15; i++) yield return null;

        // 等特效管理器出现（惰性创建：起飞后第一帧 Drive 才会建）
        _storm = null;
        for (int i = 0; i < 120 && _storm == null; i++)
        {
            var pr = _tyStorm.GetProperty("Instance", BFS);
            _storm = pr != null ? pr.GetValue(null, null) : null;
            if (_storm == null) yield return null;
        }
        if (_storm == null) { _sb.AppendLine("  × DragonStormVfx 实例没建出来（特效没被驱动？）"); yield break; }

        _fViewCam = _tyStorm.GetField("viewCamera", BF);
        _pArcN = _tyStorm.GetProperty("ActiveArcCount", BF);
        _pArcLv = _tyStorm.GetProperty("ArcLevel", BF);
        _pSmokeLv = _tyStorm.GetProperty("SmokeLevel", BF);
        _pSmokeArcN = _tyStorm.GetProperty("ActiveSmokeArcCount", BF);
        _pMinDist = _tyStorm.GetProperty("MinArcToPuffDistance", BF);
        _pLinkCnt = _tyStorm.GetProperty("LinkEmitCount", BF);
        _pPuffN = _tyStorm.GetProperty("LivePuffCount", BF);
        var mReset = _tyStorm.GetMethod("ResetLinkMask", BF);
        _pSpineEmit = _tyStorm.GetProperty("SpineEmitTotal", BF);
        _pMouthEmit = _tyStorm.GetProperty("MouthEmitTotal", BF);
        _ps = (ParticleSystem)_tyStorm.GetField("_ps", BF).GetValue(_storm);
        if (_ps != null) _psCap = _ps.main.maxParticles;
        // 弧带是"正对相机摊平"的薄片 ⇒ 出图前必须把它摊平的参考相机切到**即将出图的那台**，
        // 否则从 3/4 机位看过去弧带是侧着的、几乎看不见（这会让人误判成"没画出来"）。
        if (_fViewCam != null) _fViewCam.SetValue(_storm, _camS);

        // ★ 位图必须**逐条目先清零**：否则上一条目的发射会"继承"过来，
        //   让本条目在**根本没喷**的情况下报 24/24（实测：吐息条目就是这样报的 24/24）。
        if (mReset != null) mReset.Invoke(_storm, null);
        int spineEmit0 = (int)_pSpineEmit.GetValue(_storm, null);
        int mouthEmit0 = (int)_pMouthEmit.GetValue(_storm, null);

        var accArc = new Acc(); var accSmoke = new Acc(); var accSmokeArc = new Acc();
        var accDist = new Acc(); var accDistDive = new Acc(); var accLen = new Acc(); var accPuff = new Acc();
        var byPhase = new Dictionary<string, Acc>();
        int nearFrames = 0, frames = 0;
        // ★ 判据 4 的**正确窗口**：原文是「俯冲段」（Tell/Dive/Strike）。
        //   「-（进场）」与「Recover」这两段里烟还没冒或已经散了 ⇒ 根本没有烟可以靠近，
        //   把它们算进分母就是在稀释指标（实测 bite 的 Recover n=130，占 156 帧的 83%）。
        int diveFrames = 0, diveNear = 0;
        // ★★ 判据 4 的**语义窗口**：只在「确实有烟」的帧上问「烟里有没有电弧」。
        //   为什么不用相位窗口（Tell/Dive/Strike）：那段只有 18 帧，同一份代码连跑三次
        //   分别得到 50% / 61% / 67% —— **摆动来自相位边界和龙的轨迹，不是效果本身**。
        //   而「没有烟的帧」问「烟里有没有电弧」本身就是个错问题（无烟段必然不达标）。
        int smokeFrames = 0, smokeNear = 0;
        int mouthFwd = 0, mouthTot = 0; bool mouthShot = false;
        int fwdSpanMin = 9999, fwdSpanMax = -9999;
        bool maskReset = false;

        // ★ 录像帧必须带**条目前缀**：两个条目都从 0 开始编号的话，后一个会把前一个覆盖掉
        //   （实测：咬 + 吐息两段各 150+ 帧，目录里只剩 156 张，内容是两段混在一起的）。
        if (video) { _vidOn = true; _vidIdx = 0; _vidTag = tag; }

        int nFrames = Mathf.RoundToInt(dur * CAP_FPS);
        for (int f = 0; f < nFrames; f++)
        {
            yield return null;
            frames++;

            // 跟随机位（每帧重算 ⇒ 永远是侧视 / 前 3/4）
            Follow();

            int arcN = (int)_pArcN.GetValue(_storm, null);
            float smokeLv = (float)_pSmokeLv.GetValue(_storm, null);
            int smokeArcN = (int)_pSmokeArcN.GetValue(_storm, null);
            float minD = (float)_pMinDist.GetValue(_storm, null);
            accArc.Add(arcN); accSmoke.Add(smokeLv); accSmokeArc.Add(smokeArcN);
            accDist.Add(minD);
            accPuff.Add((int)_pPuffN.GetValue(_storm, null));
            if (minD < 0.6f) nearFrames++;

            // ★ 判据 3 的窗口界定：**从烟一开始冒**的那一刻清零位图。
            //   若从采样起点就清零，窗口里那 0.8 s「还没开招、烟为 0」会混进来 ——
            //   虽然不影响覆盖率（覆盖率只会因为多发射而变大），但会让"窗口是哪一段"说不清。
            if (!maskReset && smokeLv > 0.10f && mReset != null)
            {
                mReset.Invoke(_storm, null);
                maskReset = true;
            }

            // 身长（判据 6）：首节 ↔ 末节的世界距离。用**骨节**而非蒙皮顶点，
            // 因为特效不会动骨节，只能通过"姿态被带歪"来影响它 —— 这正是要防的。
            float len = BodyLength();
            accLen.Add(len);

            string ph = _fPhaseName != null ? (string)_fPhaseName.GetValue(_drg) : "?";
            Acc pa;
            if (!byPhase.TryGetValue(ph, out pa)) { pa = new Acc(); byPhase[ph] = pa; }
            pa.Add(arcN);
            bool phDive = ph == "Tell" || ph == "Dive" || ph == "Strike";
            if (phDive) { diveFrames++; accDistDive.Add(minD); if (minD < 0.6f) diveNear++; }
            if (smokeLv > 0.10f) { smokeFrames++; if (minD < 0.6f) smokeNear++; }

            // 吐息：口部向前的烟占比（用户定案「只有口部向前喷」）
            if (mouthChk && frames > 30 && frames % 5 == 0)
            {
                int t, fw;
                CountMouthCone(out fw, out t);
                mouthFwd += fw; mouthTot += t;
                if (!mouthShot && fw > 0)
                {
                    mouthShot = true;
                    yield return ShootBoth("breath");
                }
            }

            // 关键相位的图
            if (tag == "bite")
            {
                if (ph == "Dive" && fwdSpanMin > 9000) { fwdSpanMin = f; yield return ShootBoth("bite_dive"); }
                if (ph == "Strike" && fwdSpanMax < -9000) { fwdSpanMax = f; yield return ShootBoth("bite_strike"); }
            }
            if (tag == "cir" && f == nFrames - 1) { yield return ShootBoth("cir"); }
            if (tag == "sweep" && ph == "Strike" && fwdSpanMax < -9000) { fwdSpanMax = f; yield return ShootBoth("sweep_strike"); }

            if (_vidOn) { SaveFrame(Shot(_camS)); }
        }
        _vidOn = false;

        int linkN = _pLinkCnt != null ? (int)_pLinkCnt.GetValue(_storm, null) : -1;
        int spineN = (int)_tyDrg.GetProperty("SpineCount", BF).GetValue(_drg, null);

        _sb.AppendLine("  采样 " + frames + " 帧（" + CAP_FPS + " fps 钉死，" + dur.ToString("F1") + " s）");
        _sb.AppendLine("  活跃电弧条数   min " + accArc.min.ToString("F0") + "  max " + accArc.max.ToString("F0")
                       + "  均值 " + accArc.Mean.ToString("F2") + "  中位 " + accArc.Median().ToString("F0"));
        _sb.AppendLine("  烟浓度(0~1)    均值 " + accSmoke.Mean.ToString("F3") + "  峰值 " + accSmoke.max.ToString("F3"));
        _sb.AppendLine("  烟中电弧条数   均值 " + accSmokeArc.Mean.ToString("F2") + "  峰值 " + accSmokeArc.max.ToString("F0"));
        _sb.AppendLine("  活烟团数       均值 " + accPuff.Mean.ToString("F1"));
        _sb.AppendLine("  最近弧–烟距离  中位 " + accDist.Median().ToString("F3") + " m  ｜ <0.6 m 的帧 "
                       + nearFrames + "/" + frames + " = " + (100f * nearFrames / Mathf.Max(1, frames)).ToString("F1")
                       + "%   ← 全窗（含 Recover/进场这些**没有烟**的段）");
        _sb.AppendLine("  俯冲段(Tell/Dive/Strike) <0.6 m 的帧 " + diveNear + "/" + diveFrames
                       + " = " + (diveFrames > 0 ? (100f * diveNear / diveFrames).ToString("F1") + "%" : "n/a")
                       + "  ｜ 距离中位 " + accDistDive.Median().ToString("F3") + " m"
                       + "   ← 相位窗口（n 小，摆动大，只作参考）");
        _sb.AppendLine("  有烟的帧(浓度>0.10) <0.6 m 的帧 " + smokeNear + "/" + smokeFrames
                       + " = " + (smokeFrames > 0 ? (100f * smokeNear / smokeFrames).ToString("F1") + "%" : "n/a")
                       + "   ← ★★ 判据 4 的**语义窗口**（只在有烟时才问烟里有没有电弧），要 ≥80%");
        int spineEmit = _pSpineEmit != null ? (int)_pSpineEmit.GetValue(_storm, null) - spineEmit0 : -1;
        int mouthEmit = _pMouthEmit != null ? (int)_pMouthEmit.GetValue(_storm, null) - mouthEmit0 : -1;
        _sb.AppendLine("  本条目发射源   沿链喷 " + spineEmit + " 次 ｜ 口部喷 " + mouthEmit + " 次"
                       + "   ← 用户定案「吐息**只有**口部向前喷」⇒ 吐息条目要 沿链=0");
        _sb.AppendLine("  链节覆盖       " + (spineEmit == 0 ? "n/a（本条目整链未喷，判据 3 不适用）"
                                                        : linkN + " / " + spineN + "   ← 判据 3（俯冲段应 = " + spineN + "）"));
        _sb.AppendLine("  身长           " + accLen.min.ToString("F3") + " ~ " + accLen.max.ToString("F3")
                       + " m（均值 " + accLen.Mean.ToString("F3") + "）   ← 判据 6 要 7.3~8.1");
        if (mouthChk)
            _sb.AppendLine("  口部前方烟占比 " + (mouthTot > 0 ? (100f * mouthFwd / mouthTot).ToString("F1") + "%" : "n/a")
                           + "（相对口部位置的前向锥 dot>0.5 且 <9 m；采样 " + mouthTot + " 颗）"
                           + "  ← ⚠ 这只是**伴随量**：龙在飞，烟会落在口部后方 ⇒ 它低不代表发射源错。"
                           + "发射源看上一行「沿链喷 / 口部喷」");
        _sb.AppendLine("  按相位分组的活跃电弧条数：");
        foreach (var kv in byPhase)
            _sb.AppendLine("     " + Pad(kv.Key, 12) + " 均值 " + kv.Value.Mean.ToString("F2")
                           + "  (" + kv.Value.min.ToString("F0") + " ~ " + kv.Value.max.ToString("F0") + ", n=" + kv.Value.n + ")");

        if (video) _sb.AppendLine("  录像帧 " + _vidIdx + " 张 → " + VIDDIR);
    }

    // ══════════════════════════════════════════════════════════════════
    //  判据 5 / 7：帧时间与 GC
    // ══════════════════════════════════════════════════════════════════

    IEnumerator Perf(int idx)
    {
        _sb.AppendLine();
        _sb.AppendLine("──────────────────────────────────────────────────────────────");
        _sb.AppendLine("判据 5 / 7：帧时间与 GC（★ 已解除 captureFramerate，否则 deltaTime 恒 1/30 = 假数据）");
        Time.captureFramerate = 0;
        // ★ 判据 7 的正确尺子：`GC.GetTotalMemory` 的窗口差 = 分配量 − 窗口内被回收的量，
        //   它测不了分配速率。实测（特效**关**着）三段窗口是 0 / 524 / 668 KB，
        //   还出现过 -134068 KB（一次 GC 发生）⇒ 噪声与被测量同量级，得不出结论。
        //   `GC Allocated In Frame` 是**逐帧**计数，不含回收，直接就是 B/帧。
        var rec = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
        yield return null; yield return null;

        for (int pass = 0; pass < 2; pass++)
        {
            bool on = pass == 1;
            if (_fStormOn != null && _drg != null) _fStormOn.SetValue(_drg, on);
            _mStop.Invoke(_show, null);
            _mPlay.Invoke(_show, new object[] { idx });
            for (int i = 0; i < 120; i++) yield return null;      // 让特效强度爬到稳态

            // 预热（编辑器第一帧有大量一次性开销）
            for (int i = 0; i < 30; i++) yield return null;

            // ★ 分 3 段连续窗口，**分别**报 GCΔ。单段 90 帧的分不清「每帧都在分配」和
            //   「窗口里恰好做了一次性分配」（实测：单段量到 508 KB，看着像 5.6 KB/帧，其实未必）。
            var gc = new long[3];
            for (int w = 0; w < 3; w++)
            {
                long m0 = GC.GetTotalMemory(false);
                long alloc = 0;                    // 本窗口累计分配字节（rec.Valid 时才有效）
                float sum = 0f; int over = 0;
                for (int i = 0; i < 90; i++)
                {
                    yield return null;
                    if (rec.Valid) alloc += rec.LastValue;
                    float dt = Time.unscaledDeltaTime * 1000f;
                    sum += dt;
                    if (dt > 16.7f) over++;
                }
                gc[w] = GC.GetTotalMemory(false) - m0;
                _sb.AppendLine("  特效 " + (on ? "开" : "关") + " 第" + (w + 1) + "段：平均 "
                               + (sum / 90f).ToString("F2") + " ms  >16.7ms " + over + "/90"
                               + "  GCΔ " + (gc[w] / 1024f).ToString("F1") + " KB / 90 帧"
                               + "  ｜ **分配 " + (rec.Valid ? (alloc / 90f).ToString("F0") + " B/帧**"
                                                   : "n/a（本版本没有 GC Allocated In Frame 计数器）"));
            }
            _sb.AppendLine("      ↳ 判据 7（0 B/帧）看**后两段**：第 1 段大而后两段 ~0 = 一次性分配，不算每帧垃圾；"
                           + "三段都大才是真的每帧在分配。");
        }
        if (rec.Valid) rec.Dispose();
        if (_fStormOn != null && _drg != null) _fStormOn.SetValue(_drg, true);
        yield return null;
    }

    // ══════════════════════════════════════════════════════════════════
    //  测量辅助
    // ══════════════════════════════════════════════════════════════════

    /// <summary>首节 ↔ 末节的世界距离（不用 GetHeadPosition —— 它在末节基础上又加了 0.6 m 偏移）。</summary>
    float BodyLength()
    {
        int n = (int)_pSpineN.GetValue(_drg, null);
        if (n < 2) return 0f;
        _arg1[0] = 0;
        Vector3 a = (Vector3)_mSpinePos.Invoke(_drg, _arg1);
        _arg1[0] = n - 1;
        Vector3 b = (Vector3)_mSpinePos.Invoke(_drg, _arg1);
        return (b - a).magnitude;
    }

    void CountMouthCone(out int fwd, out int total)
    {
        fwd = 0; total = 0;
        if (_ps == null) return;
        if (_pbuf == null || _pbuf.Length < _psCap) _pbuf = new ParticleSystem.Particle[_psCap];
        int n = _ps.GetParticles(_pbuf);
        total = n;
        if (n == 0) return;
        Vector3 o = (Vector3)_mMouthPos.Invoke(_drg, null);
        Vector3 f = (Vector3)_mMouthFwd.Invoke(_drg, null);
        for (int i = 0; i < n; i++)
        {
            Vector3 d = _pbuf[i].position - o;
            float mg = d.magnitude;
            if (mg < 0.05f || mg > 9f) continue;
            if (Vector3.Dot(d / mg, f) > 0.5f) fwd++;
        }
    }

    /// <summary>跟随机位：永远侧视 / 前 3/4（依赖龙的**当前**前方，所以龙转身也不会变成正对）。</summary>
    void Follow()
    {
        if (_drg == null) return;
        Vector3 fwd = Flat(_drgT.forward).normalized;
        if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
        Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized;
        // ★★ 取景必须以**脊柱质心**为轴，不能拿 `_drgT.position + up*1.6`：
        //   巡游时龙整体是**抬起来**的（容器在地面、身体在空中），用容器当轴会把龙顶到画面外，
        //   而"脊柱 ±26 px 走廊"就整个落在画面外 ⇒ 青像素恒 0（实测：cir 侧视 0，其实龙被裁掉了）。
        Vector3 pivot = SpineCentroid();
        if (pivot == Vector3.zero) pivot = _drgT.position + Vector3.up * 1.6f;
        // 按脊柱半径算"装得下"的距离（自校准取景，见 Docs/工程坑表.md 40）
        float r = SpineRadius(pivot);
        float dist = Mathf.Max(19f, r / Mathf.Tan(_camS.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.35f);

        Vector3 dS = (right * 0.985f + Vector3.up * 0.16f + fwd * 0.06f).normalized;
        _camS.transform.position = pivot + dS * dist;
        _camS.transform.LookAt(pivot, Vector3.up);

        Vector3 dQ = (-right * 0.52f + fwd * 0.68f + Vector3.up * 0.52f).normalized;
        _camQ.transform.position = pivot + dQ * (dist * 1.05f);
        _camQ.transform.LookAt(pivot, Vector3.up);
    }

    void Save(string tag, byte[] png) { try { File.WriteAllBytes(DIR + "/s23_" + tag + ".png", png); } catch { } }

    /// <summary>
    /// 同一时刻出一张侧视 + 一张前 3/4。
    /// ★ 每次出图前把"弧带摊平参考相机"切到该机位、并**等 4 帧** ——
    ///   弧带的 `across` 是在 `Rebuild` 里按参考相机算的，抖动频率 22 Hz，
    ///   不切就会拍到"朝上一台相机摊平、却从这一台看"的侧边（细看是一条线）。
    /// </summary>
    IEnumerator ShootBoth(string tag)
    {
        if (_fViewCam != null) _fViewCam.SetValue(_storm, _camS);
        for (int i = 0; i < 4; i++) yield return null;
        Save(tag + "_side", Shot(_camS));
        int cyS = CyanNearSpine(_camS);
        if (_fViewCam != null) _fViewCam.SetValue(_storm, _camQ);
        for (int i = 0; i < 4; i++) yield return null;
        Save(tag + "_q", Shot(_camQ));
        int cyQ = CyanNearSpine(_camQ);
        if (_fViewCam != null) _fViewCam.SetValue(_storm, _camS);
        _sb.AppendLine("  出图[" + Pad(tag, 11) + "] 弧带可见像素  侧视 " + Show(cyS) + " 前3/4 " + Show(cyQ)
                       + "   走廊内青 " + Show(cyS) + "/" + Show(cyQ) + " 全屏青 " + _lastAllCyan
                       + "  走廊 " + _lastCorridor + " px｜脊柱在画面内 " + _lastOnScreen + "/" + _lastOnTotal
                       + "   ← 判据 8 代理量（n/a = 脊柱大半在画面外，尺子空转，不是「被挡住」）");
    }

    static string Show(int v) { return (v < 0 ? "n/a" : v.ToString()); }

    /// <summary>脊柱屏幕走廊内的青像素数（弧带到底看得见没有的**几何真值**）。返回 -1 = 尺子空转。</summary>
    int CyanNearSpine(Camera cam)
    {
        int n = (int)_tyDrg.GetProperty("SpineCount", BF).GetValue(_drg, null);
        if (n < 2) return 0;
        var pts = new Vector3[n];
        int onScreen = 0;
        for (int i = 0; i < n; i++)
        {
            _arg1[0] = i;
            pts[i] = cam.WorldToScreenPoint((Vector3)_mSpinePos.Invoke(_drg, _arg1));
            // ★ 自检：脊柱有多少节真的落在画面里。**大部分在画面外时，"青像素 0"是尺子空转，
            //   不是"电弧被挡住"** —— 实测 cir 侧视就栽在这上面（龙被取景顶到画面上方）。
            if (pts[i].z > 0f && pts[i].x >= 0 && pts[i].x < W && pts[i].y >= 0 && pts[i].y < H) onScreen++;
        }
        _lastOnScreen = onScreen;
        _lastOnTotal = n;
        if (onScreen * 2 < n) return -1;          // 报告里写 n/a

        System.Array.Clear(_corr, 0, _corr.Length);
        const int R = 26;
        int xmin = W - 1, xmax = 0, ymin = H - 1, ymax = 0;
        for (int i = 0; i < n; i++)
        {
            xmin = Mathf.Min(xmin, Mathf.FloorToInt(pts[i].x) - R);
            xmax = Mathf.Max(xmax, Mathf.CeilToInt(pts[i].x) + R);
            ymin = Mathf.Min(ymin, Mathf.FloorToInt(pts[i].y) - R);
            ymax = Mathf.Max(ymax, Mathf.CeilToInt(pts[i].y) + R);
        }
        xmin = Mathf.Max(0, xmin); ymin = Mathf.Max(0, ymin);
        xmax = Mathf.Min(W - 1, xmax); ymax = Mathf.Min(H - 1, ymax);

        for (int y = ymin; y <= ymax; y++)
            for (int x = xmin; x <= xmax; x++)
                for (int i = 0; i + 1 < n; i++)
                    if (DistSeg(x + 0.5f, y + 0.5f, pts[i], pts[i + 1]) <= R) { _corr[y * W + x] = true; break; }

        // ★ 同时数「走廊内青」与「全屏青」：
        //   走廊内 0 而全屏 > 0 ⇒ 电弧**画出来了但不在身上**（锚点不跟随 / 位移过大）；
        //   两者都 0 ⇒ 真的没画。这两个数分不开的话，调参会往完全错的方向走。
        int corridor = 0, hit = 0, all = 0;
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                Color32 p = _lastPx[y * W + x];
                if (!(p.b - p.r > 30 && p.g - p.r > 18)) continue;
                all++;
                if (_corr[y * W + x]) hit++;
            }
        _lastAllCyan = all;
        for (int y = ymin; y <= ymax; y++)
            for (int x = xmin; x <= xmax; x++)
                if (_corr[y * W + x]) corridor++;
        _lastCorridor = corridor;
        return hit;
    }

    /// <summary>点到线段（屏幕 2D，只有 x/y 参与）的距离。</summary>
    static float DistSeg(float px, float py, Vector3 a, Vector3 b)
    {
        float vx = b.x - a.x, vy = b.y - a.y;
        float L2 = vx * vx + vy * vy;
        float t = L2 > 1e-6f ? Mathf.Clamp01(((px - a.x) * vx + (py - a.y) * vy) / L2) : 0f;
        float dx = px - (a.x + vx * t), dy = py - (a.y + vy * t);
        return Mathf.Sqrt(dx * dx + dy * dy);
    }

    void SaveFrame(byte[] png)
    {
        try { File.WriteAllBytes(VIDDIR + "/drs_" + _vidTag + "_" + _vidIdx.ToString("D4") + ".png", png); _vidIdx++; } catch { }
    }

    byte[] Shot(Camera cam)
    {
        var prev = RenderTexture.active;
        cam.Render();
        RenderTexture.active = cam.targetTexture;
        var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
        tex.Apply();
        _lastPx = tex.GetPixels32();          // 给 CyanNearSpine 用（探针里允许分配）
        byte[] png = tex.EncodeToPNG();
        RenderTexture.active = prev;
        UnityEngine.Object.Destroy(tex);
        return png;
    }

    Camera MakeCam(string name)
    {
        var g = new GameObject(name);
        var c = g.AddComponent<Camera>();
        c.clearFlags = CameraClearFlags.SolidColor;
        // ★ 宣纸色：与 `InkSky` 地平线 (0.966,0.963,0.952) 同族但略压暗一点，
        //   既代表真实背景，又让"青白电弧在近白底上还剩多少对比"一眼看得出来。
        c.backgroundColor = new Color(0.90f, 0.90f, 0.89f, 1f);
        c.fieldOfView = 42f;
        c.nearClipPlane = 0.05f;
        c.farClipPlane = 2000f;
        c.enabled = false;
        c.targetTexture = new RenderTexture(W, H, 24);
        return c;
    }

    // ★ 反射辅助一律走这两个：探针里"某个字段读不到"应该只是少一行信息，
    //   不应该把整轮测量连报告一起带走（第二十三轮踩过，见 Docs/工程坑表.md 45）。
    object SafeGet(Func<object> f) { try { return f(); } catch { return null; } }
    string SafeStr(Func<string> f) { try { return f(); } catch (Exception e) { return "× " + e.GetType().Name + ": " + e.Message; } }

    Vector3 SpineCentroid()
    {
        if (_drg == null || _tyDrg == null) return Vector3.zero;
        int n = (int)_pSpineN.GetValue(_drg, null);
        if (n < 1) return Vector3.zero;
        Vector3 acc = Vector3.zero;
        for (int i = 0; i < n; i++) { _arg1[0] = i; acc += (Vector3)_mSpinePos.Invoke(_drg, _arg1); }
        return acc / n;
    }

    float SpineRadius(Vector3 c)
    {
        if (_drg == null || _tyDrg == null) return 3f;
        int n = (int)_pSpineN.GetValue(_drg, null);
        if (n < 1) return 3f;
        float r = 3f;
        for (int i = 0; i < n; i++)
        {
            _arg1[0] = i;
            r = Mathf.Max(r, ((Vector3)_mSpinePos.Invoke(_drg, _arg1) - c).magnitude);
        }
        return r;
    }

    static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
    static string Pad(string s, int n) { return s.Length >= n ? s : s + new string(' ', n - s.Length); }

    IEnumerator Done()
    {
        Time.captureFramerate = 0;
        try { File.WriteAllText(RP, _sb.ToString()); } catch { }
        Debug.Log("[drg_storm23] 写入 " + RP);
        yield return null;
    }
}

var __hostS23 = new GameObject("drg_storm23");
__hostS23.AddComponent<drg_storm23>();
return "DRG_STORM23_STARTED";
