using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;

// drg_fix.cs —— 第十八轮验收 + 调参（报告 drg_fix.txt）
//
// 用户四条：
//   ① 头还是歪的，他的头没有顺着那个脖子的位置
//   ② 爪子应该是往后的（他要往前飞）
//   ③ 尾巴末端是歪的，翘起来了，应该保持同一水平线的高度
//   ④ 动画应该是不断重复的
//
// ■ 前情（drg_rest 已定案）：三条几何问题**全在基准姿态里** —— amp=0 与正常驱动两组数字逐位相同。
//   ⇒ 只能**主动校正**，靠调波形救不回来。修法见 EnemyDragon 的第十八轮字段区。
//
// ■ 本探针做两件事：
//   ① **头部俯仰 5 档扫描**（0/15/30/45/60°）出图，让人眼选（几何量当不了判据：
//      "头扇均值方向 vs 颈段"量到的是**颅顶**，货不对板 —— 见报告里的说明）。
//   ② 尾巴 / 四肢 / 循环性 走**数值判据**：
//        尾巴：逐节 |pitch| ≤ 2°、末端绝对抬升 |Δy| ≤ 0.05 m、横向摆幅 ≥ 0.20 m（要动）
//        四肢：每条腿 dot(容器 fwd) ≤ 0（往后）、摆角极差 ≥ 15°（还在动）
//        循环：把 swimWaveFreq 临时设成 1/3 Hz（周期 = 90 帧 = 整数）⇒
//              帧 f 与 f+90 的形状残差应 ≈ 0；同时给 lag=0 的残差当量级对照。
//        回归：脊柱 pitch 0° / ySpread 0 m

public class drg_fix : MonoBehaviour
{
    const string SD = "D:/Unity Project/InkWash/Tools/screenshots/enemies/FIX";
    const string RP = "D:/Unity Project/InkWash/Tools/reports/drg_fix.txt";
    const string KEY = "墨龙";

    const int FPS = 30;
    const int PERIOD = 90;        // 循环测试用：swimWaveFreq = 1/3 Hz ⇒ 周期 90 帧
    const int NLOOP = 270;        // 3 个周期

    const float PITCH_TOL = 2f;   // 尾巴逐节 |pitch| 容差（度）
    const float LIFT_TOL = 0.05f; // 尾尖绝对抬升容差（m）
    const float SWAY_MIN = 0.20f; // 尾巴横向摆幅下限（m）——不能治成死棍子

    readonly StringBuilder _sb = new StringBuilder();
    Component _sc; Type _ty, _dt;
    MethodInfo _mPlay, _mLabel, _mStop;
    PropertyInfo _pCount;
    Camera _cam;
    GameObject _go;
    Transform _drgT, _vroot;
    IList _spine, _limbRoots, _limbDriven, _limbAxisDot, _tailChain, _limbBaseRel;
    FieldInfo _fAmp, _fWave, _fFreq, _fStat, _fYaw, _fLimbDeg, _fSweep, _fHeadPitch, _fHeadYaw, _fHeadRoll;
    FieldInfo _fTailLevel, _fTailAmp, _fTailSpan;

    static readonly BindingFlags BF = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;

    int _saved;

    void Start() { StartCoroutine(Run()); }

    IEnumerator Run()
    {
        yield return null; yield return null;

        // ★★ 必须钉帧步长：本探针的"横向摆幅 / 摆角极差 / 整周期残差"全都按**帧数**取样，
        //   不钉的话 60 帧只覆盖周期的零头（实测摆幅量出 0.045 m、摆角 2.8°，全是假阴性），
        //   而"lag=90 帧 = 一个周期"这条前提也不成立。第一版就是栽在这。
        Time.captureFramerate = FPS;

        Directory.CreateDirectory(SD);
        foreach (var f in Directory.GetFiles(SD, "*.png")) { try { File.Delete(f); } catch { } }

        foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (_ty == null) { var t1 = a.GetType("InkWash.DebugTools.ActionShowcase"); if (t1 != null) _ty = t1; }
            if (_dt == null) { var t2 = a.GetType("InkWash.Enemies.EnemyDragon"); if (t2 != null) _dt = t2; }
        }
        if (_ty == null || _dt == null) { _sb.AppendLine("× 类型缺失"); yield return Done(); }

        foreach (var o in UnityEngine.Object.FindObjectsOfType(_ty, true)) { _sc = o as Component; if (_sc != null) break; }
        if (_sc == null) { _sb.AppendLine("× 场景里没有 ActionShowcase（当前不是 Showcase 场景？）"); yield return Done(); }

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

        _sb.AppendLine("=== drg_fix：第十八轮（头对齐 / 尾巴水平 / 四肢后掠 / 循环）验收 ===");
        _sb.AppendLine("条目 idx=" + idx + "  label=" + found);
        _sb.AppendLine();

        _mStop.Invoke(_sc, null);
        _mPlay.Invoke(_sc, new object[] { idx });
        for (int i = 0; i < 90; i++) yield return null;

        _go = GameObject.Find("Showcase_Actor_" + idx);
        if (_go == null) { _sb.AppendLine("× 没找到 actor 实例"); yield return Done(); }

        var drg = _go.GetComponent(_dt);
        _drgT = drg.transform;
        _vroot = _dt.GetField("_modelRoot", BF).GetValue(drg) as Transform;
        _spine = _dt.GetField("_spine", BF).GetValue(drg) as IList;
        _limbRoots = _dt.GetField("_limbRoots", BF).GetValue(drg) as IList;
        _limbDriven = _dt.GetField("_limbDriven", BF).GetValue(drg) as IList;
        _limbAxisDot = _dt.GetField("_limbAxisDot", BF).GetValue(drg) as IList;
        _tailChain = _dt.GetField("_tailChain", BF).GetValue(drg) as IList;
        _limbBaseRel = _dt.GetField("_limbBaseRel", BF).GetValue(drg) as IList;
        _fAmp = _dt.GetField("swimWaveAmp", BF);
        _fWave = _dt.GetField("swimWaveCount", BF);
        _fFreq = _dt.GetField("swimWaveFreq", BF);
        _fStat = _dt.GetField("hoverStationary", BF);
        _fYaw = _dt.GetField("hoverStationaryYawDeg", BF);
        _fLimbDeg = _dt.GetField("limbSwingDeg", BF);
        _fSweep = _dt.GetField("limbSweepBackDeg", BF);
        _fHeadPitch = _dt.GetField("headAlignPitchDeg", BF);
        _fHeadYaw = _dt.GetField("headAlignYawDeg", BF);
        _fHeadRoll = _dt.GetField("headAlignRollDeg", BF);
        _fTailLevel = _dt.GetField("tailLevel", BF);
        _fTailAmp = _dt.GetField("tailWaveAmp", BF);
        _fTailSpan = _dt.GetField("tailWaveSpan", BF);

        if (_spine == null || _spine.Count < 3) { _sb.AppendLine("× 脊柱节数 < 3"); yield return Done(); }
        if (_fHeadPitch == null) { _sb.AppendLine("× 第十八轮字段不存在 —— 脚本没编译进 DLL？"); yield return Done(); }
        int NS = _spine.Count;
        int NL = _limbRoots == null ? 0 : _limbRoots.Count;
        int NT = _tailChain == null ? 0 : _tailChain.Count;

        // 原值
        float amp0 = (float)_fAmp.GetValue(drg), freq0 = (float)_fFreq.GetValue(drg);
        float headPitch0 = (float)_fHeadPitch.GetValue(drg);
        float sweep0 = (float)_fSweep.GetValue(drg);
        bool tl0 = (bool)_fTailLevel.GetValue(drg);

        if (_fStat != null) _fStat.SetValue(drg, true);
        if (_fYaw != null) _fYaw.SetValue(drg, 0f);
        if (_fWave != null) _fWave.SetValue(drg, 1.0f);
        for (int i = 0; i < 8; i++) yield return null;

        _sb.AppendLine("── 运行时参数（原值，跑完写回）──");
        _sb.AppendLine("  swimWaveAmp = " + amp0 + "   swimWaveCount = " + _fWave.GetValue(drg)
            + "   swimWaveFreq = " + freq0);
        _sb.AppendLine("  limbSwingDeg = " + _fLimbDeg.GetValue(drg) + "   limbSweepBackDeg = " + sweep0);
        _sb.AppendLine("  tailLevel = " + tl0 + "   tailWaveAmp = " + _fTailAmp.GetValue(drg)
            + "   tailWaveSpan = " + _fTailSpan.GetValue(drg));
        _sb.AppendLine("  headAlignPitch/Yaw/Roll = " + headPitch0 + " / " + _fHeadYaw.GetValue(drg)
            + " / " + _fHeadRoll.GetValue(drg) + "   headAlignLinks = " + _dt.GetField("headAlignLinks", BF).GetValue(drg));
        _sb.AppendLine("  脊柱节数 " + NS + "   分支 " + NL + "   尾巴链 " + NT + " 节");
        _sb.AppendLine();

        // ══════ ① 头部俯仰：先算"头的长轴"，再围绕它出图 ══════
        _sb.AppendLine("── ① 头部俯仰（headAlignPitchDeg，抬头为正）──");
        _sb.AppendLine("   ★ 上一版探针的「头扇均值方向 vs 颈段」量到 52.43°，但那是口径错误：");
        _sb.AppendLine("     头扇 26 枚叶子骨的方向 (0.219,0.762,0.610) = **朝上朝前**，是颅顶/鬃根，");
        _sb.AppendLine("     把它转到与颈轴平行 = 把头**下压 52°**，与用户要的正好相反。弃用。");
        _sb.AppendLine("   ★ 本版改用**头簇的长轴（PCA 第一主轴）**：对 `_spine[Count-2]` 之下、");
        _sb.AppendLine("     不属于任何 `_limbRoots` 分支的骨骼位置做 PCA（= 颅骨/吻部，不含鬃须角颌）。");

        // 先回到基准姿态（去掉波形干扰）再量长轴
        float ampSave = (float)_fAmp.GetValue(drg);
        _fAmp.SetValue(drg, 0f);
        _fHeadPitch.SetValue(drg, 0f);
        for (int i = 0; i < 8; i++) yield return null;

        var h22b = _spine[NS - 2] as Transform;
        var h23b = _spine[NS - 1] as Transform;
        float hExt, hRad; int hCount;
        Vector3 hAxis = HeadLongAxis(h22b, h23b, out hExt, out hRad, out hCount);
        Vector3 hAxisC = Quaternion.Inverse(_vroot.rotation) * hAxis;
        float hPitch = Mathf.Asin(Mathf.Clamp(-hAxisC.y, -1f, 1f)) * Mathf.Rad2Deg;
        float neckPitch = 0f;
        if (h22b != null && h23b != null)
        {
            Vector3 nd = Quaternion.Inverse(_vroot.rotation) * (h23b.position - h22b.position).normalized;
            neckPitch = Mathf.Asin(Mathf.Clamp(-nd.y, -1f, 1f)) * Mathf.Rad2Deg;
        }
        _sb.AppendLine("     头簇点数 = " + hCount + "；长轴（容器系）= " + F3(hAxisC)
            + "   长轴俯仰 = " + hPitch.ToString("F2") + "°"
            + "   颈段俯仰 = " + neckPitch.ToString("F2") + "°");
        _sb.AppendLine("     沿长轴的展布 = " + hExt.ToString("F3") + " m，垂直方向半径 = " + hRad.ToString("F3") + " m"
            + "   扁平度 = " + (hRad > 1e-6f ? (hExt / hRad).ToString("F2") : "n/a")
            + (hExt > hRad * 1.3f ? "  ⇒ 长轴可信" : "  ⇒ ⚠ 长轴不可信（头接近球形）"));
        _fAmp.SetValue(drg, ampSave);
        for (int i = 0; i < 6; i++) yield return null;

        // ★ 符号：`hRoll = asin(−axis.y)`，所以 **轴朝下 ⇒ 读出正值**。
        //   `headAlignPitchDeg` 定义为「抬头为正」 ⇒ 轴朝下 30° 就要抬 30°，`auto = +hPitch`。
        //   （第一版写成 `−hPitch`，于是扫描全跑到"低头"方向去，出图全废 —— 同一个符号坑第二次。）
        float auto = hPitch;
        _sb.AppendLine("     ⇒ 让**长轴**水平所需 headAlignPitchDeg = " + auto.ToString("F1") + "°"
            + "（轴朝下 ⇒ 抬头；符号：hDecl = asin(−axis.y)，轴朝下读出正值）");
        _sb.AppendLine("     下面在 " + (auto - 20f).ToString("F0") + "° / " + (auto - 10f).ToString("F0")
            + "° / " + auto.ToString("F0") + "° / " + (auto + 10f).ToString("F0") + "° / " + (auto + 20f).ToString("F0") + "° 出图");
        float[] pitches = { auto - 20f, auto - 10f, auto, auto + 10f, auto + 20f };
        foreach (var p in pitches)
        {
            _fHeadPitch.SetValue(drg, p);
            for (int i = 0; i < 8; i++) yield return null;
            Snap("head_side", "p" + Mathf.RoundToInt(p).ToString("D2"));
            Snap("head_q", "p" + Mathf.RoundToInt(p).ToString("D2"));
            _sb.AppendLine("  已出图 p" + Mathf.RoundToInt(p).ToString("D2") + "（抬头 " + p.ToString("F1") + "°）");
        }
        _fHeadPitch.SetValue(drg, headPitch0);
        for (int i = 0; i < 6; i++) yield return null;
        _sb.AppendLine("  图在 FIX/head_side_p**.png / head_q_p**.png");
        _sb.AppendLine();

        // ══════ ② 尾巴 / 四肢 / 回归：逐帧量 ══════
        _sb.AppendLine("── ② 尾巴 / 四肢 / 回归（60 帧）──");
        // 尾巴：逐节段方向 pitch（世界）—— 用户要的是"同一水平线"
        float pitchAbsMax = 0f; int pitchIdx = -1;
        float liftMin = float.MaxValue, liftMax = float.MinValue;
        float ySpreadMax = 0f;
        float swayMin = float.MaxValue, swayMax = float.MinValue;
        var limbDirs = new Vector3[60][];

        // 尾巴链的基准姿态（第一帧，随后逐帧比）
        var tail0 = new Vector3[NT];
        for (int i = 0; i < NT; i++) tail0[i] = (_tailChain[i] as Transform).position;

        for (int f = 0; f < 60; f++)
        {
            var pos = new Vector3[NS];
            for (int i = 0; i < NS; i++) { var t = _spine[i] as Transform; pos[i] = t != null ? t.position : Vector3.zero; }

            float yMin = float.MaxValue, yMax = float.MinValue;
            for (int i = 0; i < NS; i++) { if (pos[i].y < yMin) yMin = pos[i].y; if (pos[i].y > yMax) yMax = pos[i].y; }
            ySpreadMax = Mathf.Max(ySpreadMax, yMax - yMin);

            // 尾巴：逐节段方向 pitch（世界）—— 用户要的是"同一水平线"
            float tpAbs = 0f; int tpIdx = -1;
            for (int i = 0; i + 1 < NT; i++)
            {
                var a = _tailChain[i] as Transform; var b = _tailChain[i + 1] as Transform;
                if (a == null || b == null) continue;
                Vector3 d = b.position - a.position;
                if (d.sqrMagnitude < 1e-10f) continue;
                d.Normalize();
                float p = Mathf.Asin(Mathf.Clamp(-d.y, -1f, 1f)) * Mathf.Rad2Deg;
                if (Mathf.Abs(p) > tpAbs) { tpAbs = Mathf.Abs(p); tpIdx = i; }
            }
            if (tpAbs > pitchAbsMax) { pitchAbsMax = tpAbs; pitchIdx = tpIdx; }

            // 尾尖绝对抬升 + 横向摆幅（都用**尾尖**，不用全链均值）
            // ★ 第一版用"全链相对尾根的平均 x"当摆幅，量出 0.181 m 判"被治死" —— **口径错**：
            //   横向包络是 `amp·u`（尾根 0 → 尾尖最大），平均值本来就被压掉一大半。
            //   用户要看的是"尾巴动不动"，那就该量**尾尖**。
            if (NT > 0)
            {
                var root = _tailChain[0] as Transform;
                var tipT = _tailChain[NT - 1] as Transform;
                if (root != null && tipT != null)
                {
                    float lift = tipT.position.y - (root.parent != null ? root.parent.position.y : root.position.y);
                    liftMin = Mathf.Min(liftMin, lift); liftMax = Mathf.Max(liftMax, lift);

                    Vector3 rC = Quaternion.Inverse(_vroot.rotation)
                                 * Vector3.Cross(Vector3.up, Flat(_drgT.forward).normalized);
                    float lat = Vector3.Dot(tipT.position - root.position, rC);
                    swayMin = Mathf.Min(swayMin, lat); swayMax = Mathf.Max(swayMax, lat);
                }
            }

            // 四肢：量**首段方向**的变化（不是"相对基准的四元数夹角"）
            // ★ 第一版量 `Quaternion.Angle(_limbBaseRel[k], localRotation)`，读出 4.5° 判"被治死" ——
            //   又是**口径错**：划水轴(容器 right)与后掠轴(容器 up)垂直，绕垂直轴转 58° 之后
            //   再叠一个绕水平轴 ±25° 的摆动，合成四元数的**总转角**几乎不变（2·acos(cos29°cos12.5°)=62.6°
            //   vs 58° ⇒ 极差只有 4.6°）。这个量对"划水"接近盲的。
            //   改量首段方向：这才是"爪子在不在划"。
            limbDirs[f] = new Vector3[NL];
            for (int k = 0; k < NL; k++)
            {
                var b = _limbRoots[k] as Transform;
                if (b == null || b.childCount == 0) continue;
                limbDirs[f][k] = (_vroot.InverseTransformDirection(b.GetChild(0).position - b.position)).normalized;
            }

            if (f == 20) { Snap("body_side", "fixed"); Snap("body_top", "fixed"); }
            yield return null;
        }

        _sb.AppendLine("  ★ 尾巴逐节最大 |pitch| = " + pitchAbsMax.ToString("F2") + "°（第 " + pitchIdx + " 节）   "
            + (pitchAbsMax <= PITCH_TOL ? "✅ 在水平面内" : "❌ 仍出平面"));
        _sb.AppendLine("  ★ 尾尖绝对抬升 Δy = " + liftMin.ToString("F3") + " ~ " + liftMax.ToString("F3") + " m   "
            + (Mathf.Max(Mathf.Abs(liftMin), Mathf.Abs(liftMax)) <= LIFT_TOL ? "✅ 与身体同高" : "❌ 仍翘/垂"));
        _sb.AppendLine("  ★ 尾巴横向摆幅（**尾尖**，相对尾根）= " + (swayMax - swayMin).ToString("F3") + " m   "
            + (swayMax - swayMin >= SWAY_MIN ? "✅ 在动（不是死棍子）" : "❌ 被治死了"));
        _sb.AppendLine("  ★ 脊柱链高差 ySpread = " + ySpreadMax.ToString("F3") + " m（回归：应 ≈ 0）");
        _sb.AppendLine();
        _sb.AppendLine("  ★ 四肢（后掠 dot(容器 fwd) / 首段方向变化幅度）：");
        yield return null;
        Quaternion invRoot0 = Quaternion.Inverse(_vroot.rotation);
        for (int k = 0; k < NL; k++)
        {
            bool dr = _limbDriven != null && k < _limbDriven.Count && (bool)_limbDriven[k];
            if (!dr) continue;
            var b = _limbRoots[k] as Transform; if (b == null || b.childCount == 0) continue;
            Vector3 d = (invRoot0 * (b.GetChild(0).position - b.position)).normalized;
            float dotFwd = Vector3.Dot(d, Vector3.forward);

            float rng = 0f;
            for (int f = 0; f < 60; f++)
                for (int g = f + 1; g < 60; g++)
                {
                    if (limbDirs[f] == null || limbDirs[g] == null) continue;
                    if (limbDirs[f][k].sqrMagnitude < 1e-8f || limbDirs[g][k].sqrMagnitude < 1e-8f) continue;
                    rng = Mathf.Max(rng, Vector3.Angle(limbDirs[f][k], limbDirs[g][k]));
                }
            _sb.AppendLine("    " + b.name + "  首段 " + F3(d) + "  dot(fwd) = " + dotFwd.ToString("F3")
                + " " + (dotFwd <= 0f ? "✅往后" : "❌仍往前") + "   首段方向变化幅度 " + rng.ToString("F1") + "° "
                + (rng >= 20f ? "✅在划" : "❌被治死"));
        }

        // ══════ ③ 循环性 ══════
        _sb.AppendLine();
        _sb.AppendLine("── ③ 循环性：把 swimWaveFreq 临时设成 1/" + PERIOD + " Hz（周期 = " + PERIOD + " 帧，整数）──");
        _fFreq.SetValue(drg, (float)FPS / PERIOD);
        for (int i = 0; i < 10; i++) yield return null;

        int NP = NS + NT;
        var buf = new Vector3[NLOOP][];
        for (int f = 0; f < NLOOP; f++)
        {
            buf[f] = new Vector3[NP];
            Vector3 c = Vector3.zero;
            for (int i = 0; i < NS; i++) { var t = _spine[i] as Transform; Vector3 v = t != null ? t.position : Vector3.zero; buf[f][i] = v; c += v; }
            for (int i = 0; i < NT; i++) { var t = _tailChain[i] as Transform; Vector3 v = t != null ? t.position : Vector3.zero; buf[f][NS + i] = v; c += v; }
            c /= Mathf.Max(1, NP);
            for (int i = 0; i < NP; i++) buf[f][i] -= c;    // 去质心 ⇒ 只比形状
            yield return null;
        }
        float r0 = Rms(buf[0], buf[1]);                       // 相邻帧：周期运动的"每帧变化量"
        float rp = 0f; float rpMax = 0f;
        for (int f = 0; f + PERIOD < NLOOP; f++)
        {
            float r = Rms(buf[f], buf[f + PERIOD]);
            rp += r; if (r > rpMax) rpMax = r;
        }
        rp /= Mathf.Max(1, NLOOP - PERIOD);
        _sb.AppendLine("  相邻帧（lag=1）  残差 RMS = " + r0.ToString("F5") + " m   ← 运动幅度量级");
        _sb.AppendLine("  lag=" + PERIOD + "（整周期）残差 RMS = " + rp.ToString("F5") + " m，最大 " + rpMax.ToString("F5") + " m");
        _sb.AppendLine("  ⇒ 整周期残差 / 相邻帧残差 = " + (r0 > 1e-9f ? (rp / r0).ToString("F4") : "n/a") + "   "
            + (rp <= r0 * 0.02f ? "✅ 严格周期、循环无缝" : "❌ 一个周期后形状没有回到原位"));
        _fFreq.SetValue(drg, freq0);
        for (int i = 0; i < 6; i++) yield return null;

        // 恢复
        _fHeadPitch.SetValue(drg, headPitch0);
        _sb.AppendLine();
        _sb.AppendLine("★ 图：FIX/head_side_p**.png · head_q_p**.png（头扫描）· body_side_fixed / body_top_fixed");
        _sb.AppendLine("PNG 帧数 = " + _saved);

        if (_cam != null) UnityEngine.Object.Destroy(_cam.gameObject);
        yield return Done();
    }

    /// <summary>
    /// 头簇的**长轴**：对 `root` 之下、不属于任何 `_limbRoots` 分支的骨位置做 PCA，取第一主轴（世界空间）。
    /// 排除 `skip`（`_spine[Count-1]`，无几何的链尾骨）与全部分支（鬃/须/角/颌会让主轴偏向装饰）。
    /// out 参数给出沿主轴的展布 `ext` 与垂直方向半径 `rad`，用来判断"长轴"这个说法可不可信。
    /// </summary>
    Vector3 HeadLongAxis(Transform root, Transform skip, out float ext, out float rad, out int count)
    {
        ext = 0f; rad = 0f; count = 0;
        if (root == null) return Vector3.forward;

        var excluded = new HashSet<Transform>();
        if (_limbRoots != null)
        {
            for (int k = 0; k < _limbRoots.Count; k++)
            {
                var b = _limbRoots[k] as Transform;
                if (b == null) continue;
                var st0 = new Stack<Transform>();
                st0.Push(b);
                while (st0.Count > 0)
                {
                    var t = st0.Pop();
                    excluded.Add(t);
                    for (int c = 0; c < t.childCount; c++) st0.Push(t.GetChild(c));
                }
            }
        }

        var pts = new List<Vector3>();
        var stack = new Stack<Transform>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var t = stack.Pop();
            for (int c = 0; c < t.childCount; c++)
            {
                var ch = t.GetChild(c);
                if (ch == skip || excluded.Contains(ch)) continue;
                pts.Add(ch.position);
                stack.Push(ch);
            }
        }
        if (pts.Count < 4) return Vector3.forward;
        count = pts.Count;

        Vector3 mean = Vector3.zero;
        for (int i = 0; i < pts.Count; i++) mean += pts[i];
        mean /= pts.Count;

        float xx = 0f, xy = 0f, xz = 0f, yy = 0f, yz = 0f, zz = 0f;
        for (int i = 0; i < pts.Count; i++)
        {
            Vector3 d = pts[i] - mean;
            xx += d.x * d.x; xy += d.x * d.y; xz += d.x * d.z;
            yy += d.y * d.y; yz += d.y * d.z; zz += d.z * d.z;
        }

        // 幂迭代求最大特征方向
        Vector3 v = new Vector3(0.577f, 0.577f, 0.577f);
        for (int it = 0; it < 64; it++)
        {
            Vector3 nv = new Vector3(xx * v.x + xy * v.y + xz * v.z,
                                     xy * v.x + yy * v.y + yz * v.z,
                                     xz * v.x + yz * v.y + zz * v.z);
            if (nv.sqrMagnitude < 1e-14f) break;
            v = nv.normalized;
        }

        float lo = float.MaxValue, hi = float.MinValue, r2 = 0f;
        for (int i = 0; i < pts.Count; i++)
        {
            Vector3 d = pts[i] - mean;
            float t = Vector3.Dot(d, v);
            lo = Mathf.Min(lo, t); hi = Mathf.Max(hi, t);
            r2 = Mathf.Max(r2, (d - v * t).magnitude);
        }
        ext = hi - lo; rad = r2;
        return v;
    }

    static float Rms(Vector3[] a, Vector3[] b)
    {
        float s = 0f;
        for (int i = 0; i < a.Length; i++) s += (a[i] - b[i]).sqrMagnitude;
        return Mathf.Sqrt(s / Mathf.Max(1, a.Length));
    }

    void Snap(string view, string tag)
    {
        if (_cam == null) _cam = MakeCam();
        int NS = _spine.Count;
        Vector3 fwd = Flat(_drgT.forward).normalized;
        Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized;

        Vector3 center; float rad; Vector3 dir;

        if (view == "head_side" || view == "head_q")
        {
            var h22 = _spine[NS - 2] as Transform;
            Vector3 acc = Vector3.zero; int c = 0;
            for (int i = 0; i < h22.childCount; i++) { acc += h22.GetChild(i).position; c++; }
            center = acc / Mathf.Max(1, c);
            rad = 1.05f;
            for (int i = 0; i < h22.childCount; i++)
                rad = Mathf.Max(rad, (h22.GetChild(i).position - center).magnitude + 0.18f);
            if (view == "head_side") dir = (right * 0.98f + Vector3.up * 0.14f + fwd * 0.10f).normalized;
            else dir = (-right * 0.42f + fwd * 0.72f + Vector3.up * 0.55f).normalized;
        }
        else
        {
            var pos = new Vector3[NS];
            for (int i = 0; i < NS; i++) { var t = _spine[i] as Transform; pos[i] = t != null ? t.position : Vector3.zero; }
            center = Vector3.zero; for (int i = 0; i < NS; i++) center += pos[i]; center /= NS;
            rad = 0f; for (int i = 0; i < NS; i++) rad = Mathf.Max(rad, (pos[i] - center).magnitude);
            if (_limbRoots != null)
                for (int k = 0; k < _limbRoots.Count; k++)
                {
                    var b = _limbRoots[k] as Transform;
                    if (b != null) rad = Mathf.Max(rad, (b.position - center).magnitude + 3.4f);
                }
            if (view == "body_side") dir = (right * 0.985f + Vector3.up * 0.10f + fwd * 0.14f).normalized;
            else dir = (Vector3.up * 0.985f + fwd * 0.16f + right * 0.05f).normalized;
        }

        float dist = rad / Mathf.Sin(_cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.06f;
        _cam.transform.position = center + dir * dist;
        _cam.transform.LookAt(center, Vector3.up);

        int W = 1120, H = 630;
        var rt = new RenderTexture(W, H, 24);
        _cam.targetTexture = rt;
        _cam.Render();
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
        tex.Apply();
        File.WriteAllBytes(SD + "/" + view + "_" + tag + ".png", tex.EncodeToPNG());
        RenderTexture.active = prev;
        UnityEngine.Object.Destroy(tex);
        UnityEngine.Object.Destroy(rt);
        _cam.targetTexture = null;
        _saved++;
        try { File.WriteAllText(RP, _sb.ToString()); } catch { }
    }

    Camera MakeCam()
    {
        var g = new GameObject("PROBE_CAM_FIX");
        var c = g.AddComponent<Camera>();
        c.clearFlags = CameraClearFlags.SolidColor;
        c.backgroundColor = new Color(0.86f, 0.88f, 0.91f, 1f);
        c.fieldOfView = 40f;
        c.nearClipPlane = 0.05f;
        c.farClipPlane = 5000f;
        c.enabled = false;
        return c;
    }

    static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
    static string F3(Vector3 v) { return "(" + v.x.ToString("F3") + "," + v.y.ToString("F3") + "," + v.z.ToString("F3") + ")"; }

    IEnumerator Done()
    {
        Time.captureFramerate = 0;
        try { File.WriteAllText(RP, _sb.ToString()); } catch { }
        Debug.Log("[drg_fix] 写入 " + RP + "  帧数 " + _saved);
        yield return null;
    }
}

var __hostFix = new GameObject("drg_fix");
__hostFix.AddComponent<drg_fix>();
return "DRG_FIX_STARTED";
