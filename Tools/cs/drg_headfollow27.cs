using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;

// drg_headfollow27.cs —— 第二十七轮：头「随颈」的 A/B（第二版）
//
// 【用户原话】「现在确实是面对到位置了，但是脖子动的时候，头一直保持一个方向没有旋转，要面向过来」
//   上一轮换基把头**方向**修对了，但写点是
//       _spine[i].rotation = rootRot · headExtra · _restRel[i]
//   —— 这是**相对容器**的绝对锁：容器（= 整条龙）转头跟着转，「面向前方」成立；
//      但**脖子自己摆（行波）时头一动不动**，头成了钉在颈根上的装饰。
//
// 【三档】headNeckFollowWeight = 0（旧绝对锁）/ 0.5 / 1（随颈，出货）
//
// 【判据（全部选**不含慢漂移**的量）】
//   A' 「头/颈 p2p 比」= 头载体偏航极差 ÷ 颈节偏航极差：与幅度漂移无关，1:1 跟随即 1.000、锁死即 0.000
//   B  「相对角误差」= Angle( inv(R[i−1])·R[i] , inv(_restRel[i−1])·_restRel[i] )：与相位无关的精确量
//   D  自检：w=0 时头载体 vs `_restRel[N−2]` 的总偏差应 ≈ 0.00°（第二十六轮换基的回归自检）
//
//   ⚠ 自检 C（「三档颈偏航 p2p 必须逐位相同」）**在本工程不成立，是个坏尺子**：
//     「容器系朝向」里混了一条**尚未定位的慢分量**（容器跟随滞后量级；`bodySwayAmp` 只改
//     `_modelRoot.localPosition`，纯位移，不是它）⇒ 同一驱动器不同时刻的绝对值能差 40°，
//     三档窗口相隔数秒就给出 25.54/22.96/19.92（差 28%）。**别用裸 p2p 当「没动别人」的判据**。
//
// 【采样纪律】`Time.captureFramerate = 30` 钉死（坑表 27），每档连采 150 帧
//   （swimWaveFreq = 0.45 Hz ⇒ 周期 66.67 帧 ⇒ 150 帧 = 2.25 个周期）。
//   ★ p2p 只要求窗口 **≥ 1 个周期**（任何 ≥ 1 周期的窗口必含极值），不要求整数倍；
//     但也别太长 —— 窗口越长那条慢分量撑得越大。
//
// 【出图】★ 第二版改用**冻结姿态 + 同机位**对照，不再「等相位」：
//   等相位本身就不可靠 —— 本工程有两条驱动路径，`sweepFrequency`(1.15) 与
//   `swimWaveFreq`(0.45) **不是同一个频率**，按错的频率等相位等于没等（本探针第一版就这么翻车）。
//   新做法：先把 `EnemyDragon` 组件禁用（姿态冻住），再在同一机位下把末尾两节
//   **按两条公式各写一遍**并渲染 ⇒ 两张图**只差头的朝向**，与相位无关、与漂移无关。
//   顺带自检：冻结前先比「我的手算」与「组件刚写下的值」，必须 ≈ 0（证明重写忠实）。

public class drg_headfollow27 : MonoBehaviour
{
    const string DIR = "D:/Unity Project/InkWash/Tools/screenshots/enemies/H28";
    const string RP = "D:/Unity Project/InkWash/Tools/reports/drg_headfollow27.txt";
    const string KEY = "墨龙";
    const int W = 900, H = 600;
    const int CYCLE_FRAMES = 150;
    const int TAIL_FRAMES = 8;
    const int SHOT_MOMENTS = 6;

    static readonly float[] WEIGHTS = new float[] { 0f, 0.5f, 1f };
    static readonly BindingFlags BF = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;

    readonly StringBuilder _sb = new StringBuilder();

    Component _sc; Type _ty, _dt;
    MethodInfo _mPlay, _mLabel, _mStop;
    PropertyInfo _pCount;

    GameObject _go; object _drg; Behaviour _drgC;
    Transform _modelRoot;
    System.Collections.IList _spine, _restRel;
    FieldInfo _fW, _fWave;
    int _NS, _iHead, _iNeck;

    Camera _cam;
    int _shots;
    Vector3 _f0;
    bool _hasF0;

    void Start() { StartCoroutine(Run()); }

    IEnumerator Run()
    {
        yield return null; yield return null;
        Time.captureFramerate = 30;
        Directory.CreateDirectory(DIR);

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
        var comp = _go.GetComponent(_dt);
        if (comp == null) { _sb.AppendLine("× actor 上没有 EnemyDragon"); yield return Done(); }
        _drg = comp; _drgC = comp as Behaviour;

        var fSpine = _dt.GetField("_spine", BF);
        var fRest = _dt.GetField("_restRel", BF);
        var fModelRoot = _dt.GetField("_modelRoot", BF);
        var fLinks = _dt.GetField("headAlignLinks", BF);
        var fLock = _dt.GetField("headLockToBase", BF);
        var fUseRest = _dt.GetField("headLockUseRestPose", BF);
        var fExtraP = _dt.GetField("headAlignPitchDeg", BF);
        var fExtraY = _dt.GetField("headAlignYawDeg", BF);
        var fExtraR = _dt.GetField("headAlignRollDeg", BF);
        var fAmp = _dt.GetField("swimWaveAmp", BF);
        _fW = _dt.GetField("headNeckFollowWeight", BF);
        _fWave = _dt.GetField("swimWaveFreq", BF);          // ★ 驱动真相位用这个，不是 sweepFrequency
        if (_fW == null) { _sb.AppendLine("× 字段 headNeckFollowWeight 缺失（补丁没编译进去？）"); yield return Done(); }
        if (fSpine == null || fRest == null || fModelRoot == null) { _sb.AppendLine("× 字段缺失"); yield return Done(); }

        // ★★ 对照实验纪律：与「权重」无关的慢调制全部冻结（第一版没冻 ⇒ 自检 C 当场报警）
        var fHover = _dt.GetField("hoverStationary", BF);
        var fPerf = _dt.GetField("enablePerformanceCycle", BF);
        if (fHover != null) fHover.SetValue(_drg, true);
        if (fPerf != null) fPerf.SetValue(_drg, false);

        _spine = fSpine.GetValue(_drg) as System.Collections.IList;
        _restRel = fRest.GetValue(_drg) as System.Collections.IList;
        _modelRoot = fModelRoot.GetValue(_drg) as Transform;
        if (_spine == null || _restRel == null || _modelRoot == null) { _sb.AppendLine("× _spine/_restRel/_modelRoot 为空"); yield return Done(); }

        _NS = _spine.Count;
        _iHead = _NS - 2;
        _iNeck = _NS - 3;
        if (_restRel.Count != _NS) { _sb.AppendLine("× _restRel 长度 " + _restRel.Count + " ≠ _spine " + _NS); yield return Done(); }

        var tHead = (Transform)_spine[_iHead];
        var tNeck = (Transform)_spine[_iNeck];

        float ep = fExtraP != null ? (float)fExtraP.GetValue(_drg) : 0f;
        float ey = fExtraY != null ? (float)fExtraY.GetValue(_drg) : 0f;
        float er = fExtraR != null ? (float)fExtraR.GetValue(_drg) : 0f;
        Quaternion extra = Quaternion.Euler(-ep, ey, er);

        _sb.AppendLine("drg_headfollow27 ｜ 第二十七轮：头「随颈」权重 A/B");
        _sb.AppendLine("冻结：hoverStationary=" + (fHover != null ? fHover.GetValue(_drg).ToString() : "n/a")
                       + "｜enablePerformanceCycle=" + (fPerf != null ? fPerf.GetValue(_drg).ToString() : "n/a"));
        _sb.AppendLine("驱动条目 = 「" + found + "」｜索引 " + idx + "｜脊柱 " + _NS + " 节");
        _sb.AppendLine("straightenSpine=" + _dt.GetField("straightenSpine", BF).GetValue(_drg)
                       + "｜headLockToBase=" + fLock.GetValue(_drg)
                       + "｜headLockUseRestPose=" + fUseRest.GetValue(_drg)
                       + "｜headAlignLinks=" + fLinks.GetValue(_drg));
        _sb.AppendLine("swimWaveAmp=" + (fAmp != null ? fAmp.GetValue(_drg).ToString() : "n/a")
                       + "｜swimWaveFreq=" + WaveHz().ToString("0.00") + " Hz ⇒ 周期 "
                       + (30f / Mathf.Max(1e-4f, WaveHz())).ToString("0.00") + " 帧");
        _sb.AppendLine("headExtra = " + extra.eulerAngles.ToString("0.0") + "（pitch/yaw/roll 对齐角）");
        _sb.AppendLine("链条 [N−2] " + tHead.name + " ← 父 " + (tHead.parent != null ? tHead.parent.name : "null")
                       + "｜[N−3] " + tNeck.name + " ← 父 " + (tNeck.parent != null ? tNeck.parent.name : "null"));
        _sb.AppendLine("采样帧数/档 = " + CYCLE_FRAMES + "（captureFramerate=30 ⇒ "
                       + (CYCLE_FRAMES * WaveHz() / 30f).ToString("0.00") + " 个周期）");
        _sb.AppendLine();

        Quaternion lRelNative = Quaternion.Inverse((Quaternion)_restRel[_iNeck]) * (Quaternion)_restRel[_iHead];
        _sb.AppendLine("FBX 原生「颈节 → 头载体」局部角 = "
                       + Quaternion.Angle(lRelNative, Quaternion.identity).ToString("0.00") + "°（这是 w=1 要命中的目标）");
        _sb.AppendLine();

        _cam = MakeCam("drg_hf27_cam");

        // ───────── 测量段：三档 × 整周期 ─────────
        _sb.AppendLine("档位      帧数   头偏航p2p   头仰角p2p   颈偏航p2p   头/颈比   相对角误差max   vs基准max");
        for (int m = 0; m < WEIGHTS.Length; m++)
        {
            _fW.SetValue(_drg, WEIGHTS[m]);
            yield return null; yield return null;
            _hasF0 = false;

            float yMin = 1e9f, yMax = -1e9f, eMin = 1e9f, eMax = -1e9f;
            float nkMin = 1e9f, nkMax = -1e9f, rMax = 0f, sMax = 0f;
            var tailN = new List<float>(); var tailH = new List<float>();

            for (int f = 0; f < CYCLE_FRAMES; f++)
            {
                Quaternion rH = RelOf(_iHead);
                Quaternion rN = RelOf(_iNeck);
                Vector3 fH = (rH * Vector3.forward).normalized;
                Vector3 fN = (rN * Vector3.forward).normalized;
                if (!_hasF0) { _f0 = fH; _hasF0 = true; }
                float yaw = Vector3.SignedAngle(_f0, fH, Vector3.up);
                float elev = Mathf.Asin(Mathf.Clamp(fH.y, -1f, 1f)) * Mathf.Rad2Deg;
                float neckYaw = Vector3.SignedAngle(_f0, fN, Vector3.up);
                float relErr = Quaternion.Angle(Quaternion.Inverse(rN) * rH, lRelNative);
                float stErr = Quaternion.Angle(rH, (Quaternion)_restRel[_iHead]);

                if (yaw < yMin) yMin = yaw; if (yaw > yMax) yMax = yaw;
                if (elev < eMin) eMin = elev; if (elev > eMax) eMax = elev;
                if (neckYaw < nkMin) nkMin = neckYaw; if (neckYaw > nkMax) nkMax = neckYaw;
                if (relErr > rMax) rMax = relErr;
                if (stErr > sMax) sMax = stErr;
                if (f >= CYCLE_FRAMES - TAIL_FRAMES) { tailN.Add(neckYaw); tailH.Add(yaw); }

                yield return null;
            }

            float headP2P = yMax - yMin, neckP2P = nkMax - nkMin;
            float ratio = neckP2P > 0.5f ? headP2P / neckP2P : 0f;
            _sb.AppendLine(Pad(WEIGHTS[m].ToString("0.00"), 9) + Pad(CYCLE_FRAMES.ToString(), 6)
                           + Pad(headP2P.ToString("0.00"), 12) + Pad((eMax - eMin).ToString("0.00"), 12)
                           + Pad(neckP2P.ToString("0.00"), 12) + Pad(ratio.ToString("0.000"), 10)
                           + Pad(rMax.ToString("0.00"), 15) + sMax.ToString("0.00"));
            if (m == 0 || m == WEIGHTS.Length - 1)
                _sb.AppendLine("   末 " + TAIL_FRAMES + " 帧（颈偏航 / 头偏航 / 头−颈）：" + FmtTriple(tailN, tailH));
        }
        _sb.AppendLine();
        _sb.AppendLine("读法：");
        _sb.AppendLine("  A' 「头/颈比」= 头偏航 p2p ÷ 颈偏航 p2p。与幅度漂移无关：1:1 跟随 ⇒ 1.000，被锁死 ⇒ 0.000。");
        _sb.AppendLine("  B  「相对角误差」= 头与父节的局部角 偏离 FBX 原生值多少（w=1 应 ≈ 0 ⇒ 动得对）。");
        _sb.AppendLine("  D  「vs基准」w=0 应 ≈ 0.00°（第二十六轮换基的回归自检）。");
        _sb.AppendLine("  ⚠ 「颈偏航p2p」三档**不会**逐位相同 —— 它混了一条慢分量（见文件头），别拿它当自检。");
        _sb.AppendLine();

        // ───────── 出图段：冻结姿态 + 同机位 A/B ─────────
        _sb.AppendLine("出图（先冻姿态，再同机位分别写两条公式；两张图只差头的朝向）：");
        _fW.SetValue(_drg, 1f);
        yield return null; yield return null;

        for (int si = 0; si < SHOT_MOMENTS; si++)
        {
            // 组件刚按出货档（w=1）驱动过一帧 ⇒ 此刻它写下的值应当 = 我的手算
            Quaternion rootRot = _modelRoot.rotation;
            Quaternion mineFold = ComputeFollow(_iHead, extra);
            float faith = Quaternion.Angle(mineFold, ((Transform)_spine[_iHead]).rotation);
            _sb.AppendLine("  时刻 " + (si + 1) + " 忠实性自检（我的手算 vs 组件刚写下）= " + faith.ToString("0.0000") + "°");

            _drgC.enabled = false;                    // ★ 姿态冻住
            Vector3 focus = HeadFocus();              // ★ 两档共用同一个视心 ⇒ 机位完全相同

            ApplyVariant(false, rootRot, extra);
            yield return null; ApplyVariant(false, rootRot, extra);
            yield return null; ApplyVariant(false, rootRot, extra);
            float dA = HeadYawNow() - NeckYawNow();
            Shot("head_w0_s" + si + "_top", focus, 3.6f, 0.05f, 0.15f, 0.99f);
            Shot("head_w0_s" + si + "_front", focus, 3.6f, 0.99f, 0.12f, 0.06f);
            if (si == 0) Shot("wide_w0", BodyFocus(), 14f, 0.05f, 0.99f, 0.06f);

            ApplyVariant(true, rootRot, extra);
            yield return null; ApplyVariant(true, rootRot, extra);
            yield return null; ApplyVariant(true, rootRot, extra);
            float dB = HeadYawNow() - NeckYawNow();
            Shot("head_w1_s" + si + "_top", focus, 3.6f, 0.05f, 0.15f, 0.99f);
            Shot("head_w1_s" + si + "_front", focus, 3.6f, 0.99f, 0.12f, 0.06f);
            if (si == 0) Shot("wide_w1", BodyFocus(), 14f, 0.05f, 0.99f, 0.06f);

            _drgC.enabled = true;
            yield return null;

            _sb.AppendLine("          头−颈 夹角：A(绝对锁) " + dA.ToString("+0.0;-0.0")
                           + "°    B(随颈) " + dB.ToString("+0.0;-0.0") + "°");
        }

        _fW.SetValue(_drg, 1f);
        _sb.AppendLine();
        _sb.AppendLine("已复位 headNeckFollowWeight = 1（出货值）｜共出图 " + _shots + " 张");
        yield return Done();
    }

    // ───────────────────────── 测量 ─────────────────────────

    Quaternion RelOf(int i) { return Quaternion.Inverse(_modelRoot.rotation) * ((Transform)_spine[i]).rotation; }

    float HeadYawNow() { Vector3 f = (RelOf(_iHead) * Vector3.forward).normalized; return Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg; }
    float NeckYawNow() { Vector3 f = (RelOf(_iNeck) * Vector3.forward).normalized; return Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg; }

    /// <summary>复刻 `ApplySerpentineSpine` 的**随颈**分支：父节世界旋转 · headExtra · FBX 原生局部角。</summary>
    Quaternion ComputeFollow(int i, Quaternion extra)
    {
        Quaternion rel = (Quaternion)_restRel[i];
        Quaternion relPrev = (Quaternion)_restRel[i - 1];
        return ((Transform)_spine[i - 1]).rotation * extra * (Quaternion.Inverse(relPrev) * rel);
    }

    /// <summary>按两条公式之一改写末尾 `headAlignLinks` 节（只改旋转，绝不动位置）。</summary>
    void ApplyVariant(bool follow, Quaternion rootRot, Quaternion extra)
    {
        for (int i = _iHead; i < _NS; i++)
        {
            var t = (Transform)_spine[i];
            if (t == null) continue;
            Quaternion rel = (Quaternion)_restRel[i];
            t.rotation = follow ? ComputeFollow(i, extra) : (rootRot * extra * rel);
        }
    }

    float WaveHz() { return _fWave == null ? 0f : (float)_fWave.GetValue(_drg); }

    static string FmtTriple(List<float> a, List<float> b)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < a.Count; i++)
        {
            if (i > 0) sb.Append(" | ");
            sb.Append(a[i].ToString("0.0")).Append('/').Append(b[i].ToString("0.0"))
              .Append('/').Append((b[i] - a[i]).ToString("+0.0;-0.0"));
        }
        return sb.ToString();
    }

    // ───────────────────────── 出图 ─────────────────────────

    /// <summary>头簇（挂点整棵子树）质心当视心 —— 不做任何「颈根 + 前向偏移」的假设。</summary>
    Vector3 HeadFocus()
    {
        var c = (Transform)_spine[_iHead];
        Vector3 sum = Vector3.zero; int n = 0;
        var st = new Stack<Transform>();
        foreach (Transform ch in c) st.Push(ch);
        while (st.Count > 0 && n < 400)
        {
            var t = st.Pop();
            sum += t.position; n++;
            foreach (Transform ch in t) st.Push(ch);
        }
        if (n < 4) return c.position;
        return sum / n;
    }

    Vector3 BodyFocus()
    {
        Vector3 a = ((Transform)_spine[0]).position;
        Vector3 b = ((Transform)_spine[_NS - 1]).position;
        return (a + b) * 0.5f;
    }

    void Shot(string name, Vector3 focus, float dist, float aFwd, float aRight, float aUp)
    {
        Vector3 fwd = FlatN(_modelRoot.forward);
        Vector3 up = _modelRoot.up.normalized;
        Vector3 right = Vector3.Cross(up, fwd).normalized;
        Vector3 dir = (fwd * aFwd + right * aRight + up * aUp).normalized;
        _cam.transform.position = focus + dir * dist;
        _cam.transform.LookAt(focus, up);
        File.WriteAllBytes(DIR + "/" + name + ".png", ShotBytes(_cam));
        _shots++;
    }

    static Vector3 FlatN(Vector3 v) { v.y = 0f; if (v.sqrMagnitude < 1e-8f) v = Vector3.forward; return v.normalized; }

    byte[] ShotBytes(Camera cam)
    {
        if (cam.targetTexture == null) cam.targetTexture = new RenderTexture(W, H, 24);
        cam.Render();
        var prev = RenderTexture.active;
        RenderTexture.active = cam.targetTexture;
        var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
        tex.Apply();
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
        c.backgroundColor = new Color(0.86f, 0.88f, 0.91f, 1f);
        c.fieldOfView = 35f;
        c.nearClipPlane = 0.03f;
        c.farClipPlane = 5000f;
        c.enabled = false;
        c.targetTexture = new RenderTexture(W, H, 24);
        return c;
    }

    static string Pad(string s, int n) { return s.Length >= n ? s : s + new string(' ', n - s.Length); }

    IEnumerator Done()
    {
        Time.captureFramerate = 0;
        try { File.WriteAllText(RP, _sb.ToString()); } catch { }
        Debug.Log("[drg_headfollow27] 写入 " + RP + "  出图 " + _shots + " 张");
        yield return null;
    }
}

var __hostH28 = new GameObject("drg_headfollow27");
__hostH28.AddComponent<drg_headfollow27>();
return "DRG_HEADFOLLOW27_STARTED";
