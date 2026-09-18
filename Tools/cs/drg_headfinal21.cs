using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;

// drg_headfinal21.cs —— 第二十一轮**定档验收**：读 prefab 值 + 出图（不覆盖任何值）
//
// 与 drg_head21 的关系：尺子一字未改（索引锁死 + 空间只判一次），只做三件事
//   ① ABS 从「−30…+30 六档」缩到「0 / 3 / 5 三档」—— 30 那段是错的，留着只会误事
//   ② Pass A2 的 pitch 换成**已写进 prefab/C# 的 3**，用来复测"出货值在 6 个相位下稳不稳"
//   ③ 多一台**前侧 3/4**机位（`h21f_front_*`）：侧视看俯仰、前侧看偏航与左右对称
//
// ★ 头会不会随行波点头 —— 第二十一轮已定论（见 drg_head21.txt）：
//   行波是**纯偏航**的：`SerpPoint` 的 `fwd`/`right` 都做了 `Flat()` ⇒ 中心线整条在水平面内
//   ⇒ `tan[i]` 水平 ⇒ `FromToRotation` 只能绕竖轴 ⇒ **链上任何一节都不产生俯仰**。
//   实测（6 相位，pitch=0）吻部仰角 −3.03/−3.03/−3.03/−3.04/−3.04/−3.03 ⇒ **极差 0.01°**。
//   同一把尺子在 6 档 pitch 上增益 0.9637（1:1）⇒ 它对俯仰是**灵的**，不是瞎子。
//   结论：头部俯仰**就是一个静态常数**，与相位无关，不需要任何动态补偿。

public class drg_headfinal21 : MonoBehaviour
{
    const string DIR = "D:/Unity Project/InkWash/Tools/screenshots/enemies/H21F";
    const string RP = "D:/Unity Project/InkWash/Tools/reports/drg_headfinal21.txt";
    const string KEY = "墨龙";
    const int W = 900, H = 600;
    const float DRIVE_HZ = 0.45f;

    static readonly float[] PHASES = new float[] { 0.00f, 0.1667f, 0.3333f, 0.50f, 0.6667f, 0.8333f };
    static readonly float[] ABS = new float[] { 5f, 3f, 0f };
    const float P_LEVEL = 3f;       // ★ 已写进 prefab/C# 的值（吻部水平）；Pass A2 复测它在 6 相位下的稳定性

    static readonly BindingFlags BF = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;
    const int NC = 9;

    readonly StringBuilder _sb = new StringBuilder();

    Component _sc; Type _ty, _dt;
    MethodInfo _mPlay, _mLabel, _mStop;
    PropertyInfo _pCount;

    GameObject _go; object _drg;
    Transform _drgT, _modelRoot, _headPivot;
    System.Collections.IList _spine;
    SkinnedMeshRenderer[] _smrs;
    Camera _camS, _camQ, _camF;
    Vector3 _center; float _dist, _distQ, _distF;
    int _NS;

    Vector3 _fwdL, _upL, _latL, _pivotL;

    // ── 锁 ──
    bool _locked;
    int _bestSpace = -1;
    List<Vector3> _rawFlat = new List<Vector3>();       // 拼接后的原始顶点（顺序恒定）
    List<int> _allIdx = new List<int>();                // 每个顶点属于哪个 SMR
    int[] _iHead, _iCore, _iTip, _iRear, _iTop, _iBot;
    Vector3[] _w = new Vector3[0];                      // 本帧世界顶点

    // ── 本帧读数 ──
    int _headN, _coreN, _tipN, _rearN;
    float _fwdSpan, _tipSpread;
    Vector3 _tipL, _rearL, _topL, _botL;
    float _elevMuzzle, _yawMuzzle, _elevSkull;
    string _spaceNote = "";
    bool _v = true;

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

        _drg = _go.GetComponent(_dt);
        _drgT = (Component)_drg != null ? ((Component)_drg).transform : _go.transform;
        _spine = _dt.GetField("_spine", BF).GetValue(_drg) as System.Collections.IList;
        _modelRoot = _dt.GetField("_modelRoot", BF).GetValue(_drg) as Transform;
        var fPitch = _dt.GetField("headAlignPitchDeg", BF);
        var fLinks = _dt.GetField("headAlignLinks", BF);
        if (_spine == null || _modelRoot == null || fPitch == null) { _sb.AppendLine("× 字段缺失"); yield return Done(); }
        _NS = _spine.Count;
        _headPivot = _spine[_NS - 2] as Transform;
        _smrs = _go.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        float livePitch = Convert.ToSingle(fPitch.GetValue(_drg));

        _sb.AppendLine("=== drg_head21：吻部轴尺子「锁死」后复测（第二十一轮）===");
        _sb.AppendLine("条目 idx=" + idx + "  label=" + found);
        _sb.AppendLine("脊柱 " + _NS + " 节｜headAlignLinks=" + (fLinks != null ? fLinks.GetValue(_drg).ToString() : "?")
                       + "｜线上 headAlignPitchDeg=" + livePitch.ToString("F1"));
        _sb.AppendLine("★ 正常巡游姿态（hoverStationary=true 只冻结位置/朝向，蛇形波照跑）；钉 30 fps");
        _sb.AppendLine("★ 顶点索引在第 0 档选定后**锁死**，之后每帧不重选 —— 见文件头注释");
        _sb.AppendLine();

        _dt.GetField("hoverStationary", BF).SetValue(_drg, true);
        _dt.GetField("enablePerformanceCycle", BF).SetValue(_drg, false);
        fPitch.SetValue(_drg, 0f);
        for (int i = 0; i < 20; i++) yield return null;

        yield return WaitPhase(0.50f);
        Bake(); Lock();
        Vector3 midL = (_rearL + _tipL) * 0.5f;
        float span2 = Mathf.Max(0.5f, (_tipL - _rearL).magnitude);
        _center = _modelRoot.TransformPoint(midL);
        float rad = span2 * 1.8f + 0.50f;
        _camS = MakeCam("PROBE_CAM_H21S");
        _camQ = MakeCam("PROBE_CAM_H21Q");
        _camF = MakeCam("PROBE_CAM_H21F");
        float k = rad / Mathf.Sin(_camS.fieldOfView * 0.5f * Mathf.Deg2Rad);
        _dist = k; _distQ = k * 1.18f; _distF = k * 1.10f;
        AimSide(); AimQ(); AimF();
        _sb.AppendLine("取景：吻端–颈根 " + span2.ToString("F3") + " m，半径 " + rad.ToString("F3")
                       + "，侧视距 " + _dist.ToString("F2"));
        _sb.AppendLine("锁定：头区 " + _headN + " 点｜正中面 " + _coreN + " 点｜吻端簇 " + _tipN
                       + "｜颈根后 " + _rearN);
        _sb.AppendLine();

        // ── Pass A：pitch=0（纯模型基准），6 相位 ──
        _sb.AppendLine("── Pass A：pitch=0（纯模型基准），一个行波周期 6 等分 ──");
        _sb.AppendLine("  相位      吻部仰角    吻部偏航    颅顶仰角    前向跨度   吻端簇展布");
        fPitch.SetValue(_drg, 0f);
        float minA = 9e9f, maxA = -9e9f;
        foreach (float ph in PHASES)
        {
            yield return WaitPhase(ph);
            Bake(); Measure();
            minA = Mathf.Min(minA, _elevMuzzle); maxA = Mathf.Max(maxA, _elevMuzzle);
            _sb.AppendLine("  " + Pad(ph.ToString("F3"), 9) + Pad(_elevMuzzle.ToString("F2") + "°", 12)
                           + Pad(_yawMuzzle.ToString("F2") + "°", 12) + Pad(_elevSkull.ToString("F2") + "°", 12)
                           + Pad(_fwdSpan.ToString("F4") + " m", 11) + _tipSpread.ToString("F4"));
            _v = false;
        }
        _sb.AppendLine("  ⇒ pitch=0 时吻部仰角 极差 = " + (maxA - minA).ToString("F2") + "°（"
                       + minA.ToString("F2") + " ~ " + maxA.ToString("F2") + "）");
        _sb.AppendLine();

        // ── Pass A2：pitch = −23（候选"吻部水平"档），同 6 相位 ──
        _sb.AppendLine("── Pass A2：pitch=" + P_LEVEL.ToString("F0") + "（候选「吻部水平」档），同 6 相位 ──");
        _sb.AppendLine("  相位      吻部仰角    吻部偏航    颅顶仰角    前向跨度   吻端簇展布");
        fPitch.SetValue(_drg, P_LEVEL);
        float minB = 9e9f, maxB = -9e9f, sumB = 0f;
        foreach (float ph in PHASES)
        {
            yield return WaitPhase(ph);
            Bake(); Measure();
            minB = Mathf.Min(minB, _elevMuzzle); maxB = Mathf.Max(maxB, _elevMuzzle);
            sumB += _elevMuzzle;
            _sb.AppendLine("  " + Pad(ph.ToString("F3"), 9) + Pad(_elevMuzzle.ToString("F2") + "°", 12)
                           + Pad(_yawMuzzle.ToString("F2") + "°", 12) + Pad(_elevSkull.ToString("F2") + "°", 12)
                           + Pad(_fwdSpan.ToString("F4") + " m", 11) + _tipSpread.ToString("F4"));
        }
        _sb.AppendLine("  ⇒ 极差 = " + (maxB - minB).ToString("F2") + "°（" + minB.ToString("F2")
                       + " ~ " + maxB.ToString("F2") + "），均值 = " + (sumB / PHASES.Length).ToString("F2") + "°");
        _sb.AppendLine();

        // ── Pass B：六档绝对 pitch 出图（相位 0.50）──
        _sb.AppendLine("── Pass B：绝对 pitch 三档出图（同一行波相位 0.50；侧视 + 俯视 3/4 + 前侧 3/4）──");
        _sb.AppendLine("  绝对pitch  吻部仰角   吻部偏航   颅顶仰角   前向跨度");
        foreach (float p in ABS)
        {
            fPitch.SetValue(_drg, p);
            yield return null;
            yield return WaitPhase(0.50f);
            Bake(); Measure();
            string tag = Tag(p);
            File.WriteAllBytes(DIR + "/h21f_side_" + tag + ".png", Shot(_camS));
            File.WriteAllBytes(DIR + "/h21f_q_" + tag + ".png", Shot(_camQ));
            File.WriteAllBytes(DIR + "/h21f_front_" + tag + ".png", Shot(_camF));
            _sb.AppendLine("  " + Pad(p.ToString("F1"), 11) + Pad(_elevMuzzle.ToString("F2") + "°", 11)
                           + Pad(_yawMuzzle.ToString("F2") + "°", 11)
                           + Pad(_elevSkull.ToString("F2") + "°", 11) + _fwdSpan.ToString("F4") + " m");
            DumpScreen(p);
        }

        _sb.AppendLine();
        _sb.AppendLine("★ 怎么读这张表：");
        _sb.AppendLine("  · Pass A2 = **出货值 3**，六个相位的仰角极差就是「巡游中头会不会点头」的答案。");
        _sb.AppendLine("  · 前向跨度（前向跨度会随头**偏航**按 cos 收缩，这是正常的）不是体温计，");
        _sb.AppendLine("    真正的体温计是：吻部**偏航**必须随相位大幅摆动（行波在跑）+ 吻部**仰角**不动。");
        _sb.AppendLine("  · 换算（Pass B 六档线性拟合）：吻部仰角 = 0.9637 × headAlignPitchDeg − 3.09°。");
        _sb.AppendLine(_spaceNote);

        if (_camS != null) { if (_camS.targetTexture != null) UnityEngine.Object.Destroy(_camS.targetTexture); UnityEngine.Object.Destroy(_camS.gameObject); }
        if (_camQ != null) { if (_camQ.targetTexture != null) UnityEngine.Object.Destroy(_camQ.targetTexture); UnityEngine.Object.Destroy(_camQ.gameObject); }
        if (_camF != null) { if (_camF.targetTexture != null) UnityEngine.Object.Destroy(_camF.targetTexture); UnityEngine.Object.Destroy(_camF.gameObject); }
        yield return Done();
    }

    // ───────────────────────── Bake / 锁 / 量 ─────────────────────────

    /// <summary>把每个 SMR 的当前姿势顶点烘焙到 `_rawFlat`（顺序：SMR 顺序 → 顶点顺序，跨帧恒定）。</summary>
    void Bake()
    {
        _rawFlat.Clear(); _allIdx.Clear();
        var tmp = new Mesh();
        int used = 0;
        for (int s = 0; s < _smrs.Length; s++)
        {
            var smr = _smrs[s];
            if (smr == null || smr.sharedMesh == null) continue;
            try { smr.BakeMesh(tmp); } catch (Exception e)
            { if (_v) _sb.AppendLine("  × BakeMesh " + s + ": " + e.GetType().Name); continue; }
            var vs = tmp.vertices;
            if (vs == null || vs.Length == 0) continue;
            used++;
            if (_v) _sb.AppendLine("  [SMR " + s + "] " + smr.name + "  顶点 " + vs.Length
                                   + "  lossy=" + smr.transform.lossyScale.ToString("F5"));
            for (int i = 0; i < vs.Length; i++) { _rawFlat.Add(vs[i]); _allIdx.Add(s); }
        }
        UnityEngine.Object.Destroy(tmp);
        _rawN = used;
        if (_bestSpace < 0) SolveSpace();
        ToWorld();
    }

    int _rawN = 0;

    /// <summary>候选空间枚举 + 打分，只做一次。判据：脊骨落在点云包围盒内的比例（网格必须包住骨架）。</summary>
    void SolveSpace()
    {
        var bPts = new List<Vector3>();
        Vector3 bMin = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
        Vector3 bMax = new Vector3(float.MinValue, float.MinValue, float.MinValue);
        Vector3 bCen = Vector3.zero;
        for (int i = 0; i < _NS; i++)
        {
            var t = _spine[i] as Transform; if (t == null) continue;
            Vector3 p = t.position; bPts.Add(p);
            bMin = Vector3.Min(bMin, p); bMax = Vector3.Max(bMax, p); bCen += p;
        }
        if (bPts.Count > 0) bCen /= bPts.Count;
        float bSize = Mathf.Max(0.1f, (bMax - bMin).magnitude);

        int best = -1; float bestScore = -1f;
        for (int c = 0; c < NC; c++)
        {
            Vector3 cMin = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            Vector3 cMax = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            Vector3 cCen = Vector3.zero; int n = 0;
            for (int i = 0; i < _rawFlat.Count; i += Mathf.Max(1, _rawFlat.Count / 4000))
            {
                Vector3 p = MatFor(c, _allIdx[i]).MultiplyPoint3x4(_rawFlat[i]);
                cMin = Vector3.Min(cMin, p); cMax = Vector3.Max(cMax, p); cCen += p; n++;
            }
            if (n == 0) continue;
            cCen /= n;
            float cSize = (cMax - cMin).magnitude;
            int inside = 0;
            for (int i = 0; i < bPts.Count; i++)
            {
                Vector3 p = bPts[i];
                if (p.x > cMin.x && p.x < cMax.x && p.y > cMin.y && p.y < cMax.y
                    && p.z > cMin.z && p.z < cMax.z) inside++;
            }
            float frac = bPts.Count > 0 ? (float)inside / bPts.Count : 0f;
            float ratio = Mathf.Max(cSize, bSize) / Mathf.Max(0.01f, Mathf.Min(cSize, bSize));
            float sizeScore = ratio < 3f ? 1f : (ratio < 10f ? 0.3f : 0f);
            float distScore = Mathf.Clamp01(1f - (cCen - bCen).magnitude / (bSize * 2f));
            float sc = frac * 100f + sizeScore * 10f + distScore;
            if (_v) _sb.AppendLine("    " + Pad(NM[c], 22) + "尺寸 " + cSize.ToString("F2")
                                   + "  骨在盒内 " + (frac * 100f).ToString("F0") + "%  得分 " + sc.ToString("F2"));
            if (sc > bestScore) { bestScore = sc; best = c; }
        }
        _bestSpace = Mathf.Max(0, best);
        _spaceNote = "★ BakeMesh 空间判定（只判一次并锁定）：采用「" + NM[_bestSpace]
                     + "」（得分 " + bestScore.ToString("F2") + "，本帧顶点 " + _rawFlat.Count
                     + "，SMR " + _rawN + " 个）";
    }

    static readonly string[] NM = new string[]
    { "原样", "SMR(带缩放)", "modelRoot(带缩放)", "颈根骨(带缩放)", "rootBone(带缩放)",
      "modelRoot(单位缩放)", "SMR(单位缩放)", "rootBone(单位缩放)", "颈根骨(单位缩放)" };

    Matrix4x4 MatFor(int c, int smrIdx)
    {
        var smr = _smrs[smrIdx];
        var rb = smr.rootBone != null ? smr.rootBone : _modelRoot;
        switch (c)
        {
            case 0: return Matrix4x4.identity;
            case 1: return smr.transform.localToWorldMatrix;
            case 2: return _modelRoot.localToWorldMatrix;
            case 3: return _headPivot.localToWorldMatrix;
            case 4: return rb.localToWorldMatrix;
            case 5: return Matrix4x4.TRS(_modelRoot.position, _modelRoot.rotation, Vector3.one);
            case 6: return Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one);
            case 7: return Matrix4x4.TRS(rb.position, rb.rotation, Vector3.one);
            default: return Matrix4x4.TRS(_headPivot.position, _headPivot.rotation, Vector3.one);
        }
    }

    void ToWorld()
    {
        if (_w.Length != _rawFlat.Count) _w = new Vector3[_rawFlat.Count];
        var cache = new Matrix4x4[_smrs.Length];
        for (int s = 0; s < _smrs.Length; s++) cache[s] = MatFor(_bestSpace, s);
        for (int i = 0; i < _rawFlat.Count; i++)
            _w[i] = cache[_allIdx[i]].MultiplyPoint3x4(_rawFlat[i]);
    }

    /// <summary>在第 0 档选定六组顶点索引并锁死。</summary>
    void Lock()
    {
        var inv = _modelRoot.worldToLocalMatrix;
        Vector3 f = _drgT.forward; f.y = 0f;
        if (f.sqrMagnitude < 1e-6f) f = Vector3.forward;
        f.Normalize();
        _fwdL = inv.MultiplyVector(f).normalized;
        _upL = inv.MultiplyVector(Vector3.up);
        _latL = Vector3.Cross(_upL, _fwdL);
        if (_latL.sqrMagnitude < 1e-8f) _latL = Vector3.right;
        _latL.Normalize();
        _upL = Vector3.Cross(_fwdL, _latL).normalized;
        _pivotL = inv.MultiplyPoint3x4(_headPivot.position);
        Vector3 tailL = inv.MultiplyPoint3x4(((Transform)_spine[0]).position);
        Vector3 headL = inv.MultiplyPoint3x4(((Transform)_spine[_NS - 2]).position);
        if (Vector3.Dot(headL - tailL, _fwdL) < 0f) { _fwdL = -_fwdL; _latL = -_latL; }

        var head = new List<int>();
        var lmin = new List<float>(); var latA = new List<float>(); var fwdA = new List<float>();
        for (int i = 0; i < _w.Length; i++)
        {
            Vector3 q = inv.MultiplyPoint3x4(_w[i]);
            Vector3 d = q - _pivotL;
            float pf = Vector3.Dot(d, _fwdL);
            if (pf <= -0.15f) continue;
            head.Add(i); fwdA.Add(pf); latA.Add(Mathf.Abs(Vector3.Dot(d, _latL)));
        }
        _iHead = head.ToArray();
        _headN = _iHead.Length;
        if (_headN < 64) { _sb.AppendLine("× 头区顶点太少 " + _headN); return; }

        float latMax = 0f, fmin = float.MaxValue, fmax = float.MinValue;
        for (int k = 0; k < _headN; k++)
        {
            if (latA[k] > latMax) latMax = latA[k];
            if (fwdA[k] < fmin) fmin = fwdA[k];
            if (fwdA[k] > fmax) fmax = fwdA[k];
        }
        float latCut = Mathf.Max(0.05f, latMax * 0.22f);
        var core = new List<int>(); var cfA = new List<float>();
        for (int k = 0; k < _headN; k++)
            if (latA[k] <= latCut) { core.Add(_iHead[k]); cfA.Add(fwdA[k]); }
        _iCore = core.ToArray(); _coreN = _iCore.Length;
        if (_coreN < 32) { _sb.AppendLine("× 正中面顶点太少 " + _coreN); return; }

        float cfmin = float.MaxValue, cfmax = float.MinValue;
        for (int k = 0; k < _coreN; k++)
        { if (cfA[k] < cfmin) cfmin = cfA[k]; if (cfA[k] > cfmax) cfmax = cfA[k]; }
        float cspan = Mathf.Max(1e-4f, cfmax - cfmin);

        var tip = new List<int>(); var rear = new List<int>();
        for (int k = 0; k < _coreN; k++)
        {
            if (cfA[k] >= cfmax - 0.02f * cspan) tip.Add(_iCore[k]);
            if (cfA[k] <= cfmin + 0.12f * cspan) rear.Add(_iCore[k]);
        }
        _iTip = tip.ToArray(); _iRear = rear.ToArray();
        _tipN = _iTip.Length; _rearN = _iRear.Length;
        _fwdSpan = fmax - fmin;
        if (_tipN == 0 || _rearN == 0) { _sb.AppendLine("× 吻端/颈根簇为空"); return; }

        // 顶/底（用锁定的 core 集内部极值）
        float vmax = float.MinValue, vmin = float.MaxValue;
        var va = new List<float>();
        for (int k = 0; k < _coreN; k++)
        {
            float pv = Vector3.Dot(inv.MultiplyPoint3x4(_w[_iCore[k]]) - _pivotL, _upL);
            va.Add(pv); if (pv > vmax) vmax = pv; if (pv < vmin) vmin = pv;
        }
        float vr = Mathf.Max(1e-4f, vmax - vmin);
        var top = new List<int>(); var bot = new List<int>();
        for (int k = 0; k < _coreN; k++)
        {
            if (va[k] >= vmax - 0.04f * vr) top.Add(_iCore[k]);
            if (va[k] <= vmin + 0.04f * vr) bot.Add(_iCore[k]);
        }
        _iTop = top.ToArray(); _iBot = bot.ToArray();

        _locked = true;
        Measure();                 // 顺手把 tip/rear/top 填上，供取景
    }

    /// <summary>只按**锁定索引**取值 —— 索引不变 ⇒ 集合不变 ⇒ 读数抖动只能来自姿态。</summary>
    void Measure()
    {
        if (!_locked) return;
        var inv = _modelRoot.worldToLocalMatrix;
        Vector3 acc; float fmin = float.MaxValue, fmax = float.MinValue;
        acc = Vector3.zero;
        for (int k = 0; k < _iHead.Length; k++)
        {
            float pf = Vector3.Dot(inv.MultiplyPoint3x4(_w[_iHead[k]]) - _pivotL, _fwdL);
            if (pf < fmin) fmin = pf; if (pf > fmax) fmax = pf;
        }
        _fwdSpan = fmax - fmin;

        acc = Vector3.zero;
        for (int k = 0; k < _tipN; k++) acc += inv.MultiplyPoint3x4(_w[_iTip[k]]);
        _tipL = acc / Mathf.Max(1, _tipN);
        acc = Vector3.zero;
        for (int k = 0; k < _rearN; k++) acc += inv.MultiplyPoint3x4(_w[_iRear[k]]);
        _rearL = acc / Mathf.Max(1, _rearN);
        acc = Vector3.zero;
        for (int k = 0; k < _iTop.Length; k++) acc += inv.MultiplyPoint3x4(_w[_iTop[k]]);
        _topL = _iTop.Length > 0 ? acc / _iTop.Length : _tipL;
        acc = Vector3.zero;
        for (int k = 0; k < _iBot.Length; k++) acc += inv.MultiplyPoint3x4(_w[_iBot[k]]);
        _botL = _iBot.Length > 0 ? acc / _iBot.Length : _rearL;

        float spread = 0f;
        for (int k = 0; k < _tipN; k++)
            spread = Mathf.Max(spread, (inv.MultiplyPoint3x4(_w[_iTip[k]]) - _tipL).magnitude);
        _tipSpread = spread;

        Vector3 m = _tipL - _rearL;
        _elevMuzzle = Mathf.Asin(Mathf.Clamp(Vector3.Dot(m.normalized, _upL), -1f, 1f)) * Mathf.Rad2Deg;
        _yawMuzzle = Mathf.Atan2(Vector3.Dot(m.normalized, _latL), Vector3.Dot(m.normalized, _fwdL)) * Mathf.Rad2Deg;
        Vector3 s = _topL - _rearL;
        _elevSkull = Mathf.Asin(Mathf.Clamp(Vector3.Dot(s.normalized, _upL), -1f, 1f)) * Mathf.Rad2Deg;
    }

    void DumpScreen(float p)
    {
        _sb.AppendLine("    SCREEN p=" + Tag(p) + " neck " + SP(_pivotL) + " tip " + SP(_tipL)
                       + " rear " + SP(_rearL) + " top " + SP(_topL) + " bot " + SP(_botL)
                       + " refA " + SP(_rearL - 1.6f * _fwdL) + " refB " + SP(_rearL + 1.6f * _fwdL));
    }

    string SP(Vector3 local)
    {
        Vector3 wp = _modelRoot.TransformPoint(local);
        float pw = Mathf.Max(1, _camS.pixelWidth), ph = Mathf.Max(1, _camS.pixelHeight);
        Vector3 sp = _camS.WorldToScreenPoint(wp);
        return (sp.x / pw).ToString("F4") + " " + ((ph - sp.y) / ph).ToString("F4");
    }

    IEnumerator WaitPhase(float target)
    {
        for (int i = 0; i < 240; i++)
        {
            float fr = Mathf.Repeat(DRIVE_HZ * Time.time, 1f);
            float d = Mathf.Abs(fr - target);
            if (d > 0.5f) d = 1f - d;
            if (d < 0.010f) yield break;
            yield return null;
        }
    }

    void AimSide()
    {
        Vector3 fwd = Flat(_drgT.forward).normalized;
        Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized;
        Vector3 dir = (right * 0.990f + Vector3.up * 0.07f + fwd * 0.10f).normalized;
        _camS.transform.position = _center + dir * _dist;
        _camS.transform.LookAt(_center, Vector3.up);
    }

    void AimQ()
    {
        Vector3 fwd = Flat(_drgT.forward).normalized;
        Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized;
        Vector3 dir = (-right * 0.40f + fwd * 0.74f + Vector3.up * 0.54f).normalized;
        _camQ.transform.position = _center + dir * _distQ;
        _camQ.transform.LookAt(_center, Vector3.up);
    }

    /// <summary>前侧 3/4：从**吻部正前方偏侧上方**看（侧视看俯仰、这个机位看偏航与左右对称）。</summary>
    void AimF()
    {
        Vector3 fwd = Flat(_drgT.forward).normalized;
        Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized;
        Vector3 dir = (fwd * 0.92f + right * 0.30f + Vector3.up * 0.25f).normalized;
        _camF.transform.position = _center + dir * _distF;
        _camF.transform.LookAt(_center, Vector3.up);
    }

    byte[] Shot(Camera cam)
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
        c.fieldOfView = 40f;
        c.nearClipPlane = 0.03f;
        c.farClipPlane = 5000f;
        c.enabled = false;
        c.targetTexture = new RenderTexture(W, H, 24);
        return c;
    }

    static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
    static string Pad(string s, int n) { return s.Length >= n ? s : s + new string(' ', n - s.Length); }

    static string Tag(float p)
    {
        string s = (p >= 0f ? "p" : "m") + Mathf.Abs(Mathf.RoundToInt(p)).ToString("D2");
        return s;
    }

    IEnumerator Done()
    {
        Time.captureFramerate = 0;
        try { File.WriteAllText(RP, _sb.ToString()); } catch { }
        Debug.Log("[drg_headfinal21] 写入 " + RP);
        yield return null;
    }
}

var __hostH21 = new GameObject("drg_headfinal21");
__hostH21.AddComponent<drg_headfinal21>();
return "DRG_HEADFINAL21_STARTED";
