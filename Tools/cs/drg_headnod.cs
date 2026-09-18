using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;

// drg_headnod.cs —— 第二十四轮：查明「移动时龙头上下甩 ~50°」的成因，并验证一个可自调的旋钮
//
// 【缘起】用户第三次反馈头部：「龙头要始终面向前方，现在还是有问题」。
//   用 drg_headaxis20 的 Pass A 实测（pitch=0、纯基准、三个行波相位）：
//       相位 0.00 → 吻部仰角 −33.83° ｜ 0.30 → +15.88° ｜ 0.60 → −33.33°
//   ⇒ **极差 49.7°**。而同机位出图目视确认：0.00 那张脖子+头明显下折、0.30 那张平视略抬。
//   ⇒ 不是尺子瞎了，是**真的在点头**。
//
// 【机制】SerpPoint 的中心线是**纯水平**的（fwd = Flat(transform.forward)、right = Cross(up,fwd)），
//   所以每节的目标切向 tan[i] 都在水平面内。但写入用的是
//       q = FromToRotation(_baseSegLocal[i], tan[i])
//   这是**最小旋转**：基准段方向 a 本身是倾斜的（模型原生大 C/S 弯），
//   b 在水平面内左右摆时，cross(a,b) 这个轴跟着转 ⇒ 合成旋转必然带出俯仰/滚转。
//   头簇（39 叶 + 鬃/须/角/颌）刚性挂在 spine[22] 上 ⇒ 整块被甩出面外。
//   ★ 旧驱动 ApplySpineOffsetsRaw 用的是 AngleAxis(yaw, up) —— 绕世界 up 的**纯偏航**，
//     所以**不产生**仰角变化。那条「头不会随行波俯仰（实测 6 相位极差 0.01°）」的注释
//     写于旧驱动时期，换成蛇形驱动后已经**失效**，必须改掉（坑表条目 46）。
//
// 【被验证的旋钮】SerpPoint 里 env(u) = sin(πu)·Lerp(tg, hg, u)，于是
//       env'(1) = π·cos(π)·hg + sin(π)·(hg−tg) = −π·hg
//   ⇒ **hg = 0 时 env'(1) = 0** ⇒ lat'(1) = 0 ⇒ 头端切向 = 纯轴向、**与相位无关**
//   ⇒ q 恒定 ⇒ 头不再随波甩。而尾端 env'(0) = π·tg 不受影响（尾巴照旧摆）。
//   本探针就是来验这条推理的：扫 hg ∈ {1.0（现值）, 0.5, 0.0} × 三相位，看仰角极差。
//
// 手法：hoverStationary = true（冻结位置/朝向，但**蛇形波照跑**）、pitch = 0（纯基准）、
//       钉 30 fps、侧视相机只算一次 ⇒ 三张图可以直接叠着看。

public class drg_headnod : MonoBehaviour
{
    const string DIR = "D:/Unity Project/InkWash/Tools/screenshots/enemies/H24";
    const string RP = "D:/Unity Project/InkWash/Tools/reports/drg_headnod.txt";
    const string KEY = "墨龙";
    const int W = 900, H = 600;
    const float DRIVE_HZ = 0.45f;

    static readonly float[] PHASES = new float[] { 0.00f, 0.30f, 0.60f };
    static readonly float[] HGS = new float[] { 1.00f, 0.50f, 0.00f };

    static readonly BindingFlags BF = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;

    readonly StringBuilder _sb = new StringBuilder();

    Component _sc; Type _ty, _dt;
    MethodInfo _mPlay, _mLabel, _mStop;
    PropertyInfo _pCount;

    GameObject _go; object _drg;
    Transform _drgT, _modelRoot, _headPivot;
    System.Collections.IList _spine;
    SkinnedMeshRenderer[] _smrs;
    Camera _camS;
    Vector3 _center; float _dist;
    int _NS;

    Vector3 _fwdL, _upL, _latL;
    Vector3 _pivotL;

    int _headN, _coreN;
    float _fmin, _fmax, _latMax;
    Vector3 _tipL, _rearL, _topL, _botL;
    float _elevMuzzle, _yawMuzzle, _elevSkull, _fwdSpan, _tipSpread;
    string _spaceNote = "";
    bool _v = true;
    const int NC = 9;

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
        var fHg = _dt.GetField("swimWaveHeadGain", BF);
        var fHover = _dt.GetField("hoverStationary", BF);
        var fPerf = _dt.GetField("enablePerformanceCycle", BF);
        if (_spine == null || _modelRoot == null || fPitch == null || fHg == null)
        { _sb.AppendLine("× 字段缺失"); yield return Done(); }
        _NS = _spine.Count;
        _headPivot = _spine[_NS - 2] as Transform;
        _smrs = _go.GetComponentsInChildren<SkinnedMeshRenderer>(true);

        _sb.AppendLine("=== drg_headnod：头随行波点头 —— swimWaveHeadGain 是不是那把钥匙（第二十四轮）===");
        _sb.AppendLine("条目 idx=" + idx + "  label=" + found);
        _sb.AppendLine("脊柱 " + _NS + " 节｜headAlignLinks=" + (fLinks != null ? fLinks.GetValue(_drg).ToString() : "?")
                       + "｜swimWaveHeadGain 现值=" + fHg.GetValue(_drg).ToString()
                       + "｜SkinnedMeshRenderer " + _smrs.Length + " 个");
        _sb.AppendLine("★ hoverStationary=true（位置/朝向冻结，**蛇形波照跑**）｜headAlignPitchDeg=0（纯模型基准）");
        _sb.AppendLine("★ 钉 30 fps｜侧视相机只算一次 ⇒ 各档图可直接叠看");
        _sb.AppendLine();

        // ── 冻结位置与朝向；关省电循环；pitch 归零 ──
        fHover.SetValue(_drg, true);
        fPerf.SetValue(_drg, false);
        fPitch.SetValue(_drg, 0f);
        for (int i = 0; i < 20; i++) yield return null;

        // ── 取景：以「吻端–颈根连线」中点为视心（不能只用头簇包围球：含鬃/须长链会把头推出画面）──
        yield return WaitPhase(PHASES[1]);
        Measure();
        Vector3 midL = (_rearL + _tipL) * 0.5f;
        float span2 = Mathf.Max(0.5f, (_tipL - _rearL).magnitude);
        _center = _modelRoot.TransformPoint(midL);
        float rad = span2 * 1.8f + 0.50f;
        _camS = MakeCam("PROBE_CAM_H24S");
        _dist = rad / Mathf.Sin(_camS.fieldOfView * 0.5f * Mathf.Deg2Rad);
        AimSide();
        _sb.AppendLine("取景：吻端–颈根 " + span2.ToString("F3") + " m，侧视距 " + _dist.ToString("F2"));
        _sb.AppendLine();

        // ── 主表：hg × 相位 的吻部仰角 ──
        _sb.AppendLine("── swimWaveHeadGain × 行波相位 ⇒ 吻部仰角（度）──");
        _sb.AppendLine("   hg      ph0.00     ph0.30     ph0.60     极差      吻部偏航极差");
        float[] p000 = null;
        foreach (float hg in HGS)
        {
            fHg.SetValue(_drg, hg);
            yield return null;
            var elev = new float[PHASES.Length];
            var yaw = new float[PHASES.Length];
            for (int k = 0; k < PHASES.Length; k++)
            {
                yield return WaitPhase(PHASES[k]);
                Measure();
                elev[k] = _elevMuzzle; yaw[k] = _yawMuzzle;
                if (Mathf.Abs(hg - 1f) < 1e-4f)
                {
                    File.WriteAllBytes(DIR + "/hn2_hg100_ph" + ((int)(PHASES[k] * 100)).ToString("D3") + ".png", Shot(_camS));
                    DumpScreen(hg, PHASES[k]);
                }
                if (Mathf.Abs(hg - 0f) < 1e-4f)
                    File.WriteAllBytes(DIR + "/hn2_hg000_ph" + ((int)(PHASES[k] * 100)).ToString("D3") + ".png", Shot(_camS));
                _v = false;
            }
            float eMin = Mathf.Min(elev), eMax = Mathf.Max(elev);
            float yMin = Mathf.Min(yaw), yMax = Mathf.Max(yaw);
            if (p000 == null) p000 = elev;
            _sb.AppendLine("  " + Pad(hg.ToString("F2"), 8)
                           + Pad(elev[0].ToString("F2") + "°", 11)
                           + Pad(elev[1].ToString("F2") + "°", 11)
                           + Pad(elev[2].ToString("F2") + "°", 11)
                           + Pad((eMax - eMin).ToString("F2") + "°", 11)
                           + (yMax - yMin).ToString("F2") + "°");
        }
        fHg.SetValue(_drg, HGS[0]);

        _sb.AppendLine();
        _sb.AppendLine("★ 读法：");
        _sb.AppendLine("  · 「极差」= 移动一个行波周期内，头上下甩了多少度。**越小越像「头始终面向前方」**。");
        _sb.AppendLine("  · hg=1.00 是本工程现值。若 hg=0.00 的极差≈0 ⇒ 用户只需在 Inspector 把");
        _sb.AppendLine("    EnemyDragon.swimWaveHeadGain 改成 0（无需改代码）。");
        _sb.AppendLine("  · 代价：hg 越小，**头端的身子**摆幅越小（波在接近头处收敛成直杆）。");
        _sb.AppendLine("    这是「头朝向」与「头端随波」的取舍，属审美，由用户拍板。");
        _sb.AppendLine(_spaceNote);

        if (_camS != null) { if (_camS.targetTexture != null) UnityEngine.Object.Destroy(_camS.targetTexture); UnityEngine.Object.Destroy(_camS.gameObject); }
        yield return Done();
    }

    // ───────────────────────── 量（口径与 drg_headaxis20 一致，便于对照）─────────────────────────

    void Measure()
    {
        var world = GatherWorld();

        Matrix4x4 inv = _modelRoot.worldToLocalMatrix;
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

        var head = new List<Vector3>();
        for (int i = 0; i < world.Count; i++)
        {
            Vector3 q = inv.MultiplyPoint3x4(world[i]);
            if (Vector3.Dot(q - _pivotL, _fwdL) > -0.15f) head.Add(q);
        }
        _headN = head.Count;
        if (_headN < 64) { _elevMuzzle = 0f; _yawMuzzle = 0f; _elevSkull = 0f; _coreN = 0; return; }

        float fmin = float.MaxValue, fmax = float.MinValue, latMax = 0f;
        for (int i = 0; i < _headN; i++)
        {
            Vector3 d = head[i] - _pivotL;
            float pf = Vector3.Dot(d, _fwdL), pl = Mathf.Abs(Vector3.Dot(d, _latL));
            if (pf < fmin) fmin = pf;
            if (pf > fmax) fmax = pf;
            if (pl > latMax) latMax = pl;
        }
        _fmin = fmin; _fmax = fmax; _latMax = latMax;
        _fwdSpan = fmax - fmin;

        float latCut = Mathf.Max(0.05f, latMax * 0.22f);
        var core = new List<Vector3>();
        for (int i = 0; i < _headN; i++)
        {
            Vector3 d = head[i] - _pivotL;
            if (Mathf.Abs(Vector3.Dot(d, _latL)) <= latCut) core.Add(head[i]);
        }
        _coreN = core.Count;
        if (_coreN < 32) { _elevMuzzle = 0f; _yawMuzzle = 0f; _elevSkull = 0f; return; }

        float cfmin = float.MaxValue, cfmax = float.MinValue;
        for (int i = 0; i < _coreN; i++)
        {
            float pf = Vector3.Dot(core[i] - _pivotL, _fwdL);
            if (pf < cfmin) cfmin = pf;
            if (pf > cfmax) cfmax = pf;
        }
        float cspan = Mathf.Max(1e-4f, cfmax - cfmin);

        Vector3 tipAcc = Vector3.zero; int tipN = 0;
        Vector3 rearAcc = Vector3.zero; int rearN = 0;
        Vector3 topAcc = Vector3.zero; int topN = 0;
        Vector3 botAcc = Vector3.zero; int botN = 0;
        float vmax = float.MinValue, vmin = float.MaxValue;
        for (int i = 0; i < _coreN; i++)
        {
            float pf = Vector3.Dot(core[i] - _pivotL, _fwdL);
            float pv = Vector3.Dot(core[i] - _pivotL, _upL);
            if (pf >= cfmax - 0.02f * cspan) { tipAcc += core[i]; tipN++; }
            if (pf <= cfmin + 0.12f * cspan) { rearAcc += core[i]; rearN++; }
            if (pv > vmax) vmax = pv;
            if (pv < vmin) vmin = pv;
        }
        for (int i = 0; i < _coreN; i++)
        {
            float pv = Vector3.Dot(core[i] - _pivotL, _upL);
            if (pv >= vmax - 0.04f * Mathf.Max(1e-4f, vmax - vmin)) { topAcc += core[i]; topN++; }
            if (pv <= vmin + 0.04f * Mathf.Max(1e-4f, vmax - vmin)) { botAcc += core[i]; botN++; }
        }
        if (tipN == 0 || rearN == 0) { _elevMuzzle = 0f; _yawMuzzle = 0f; _elevSkull = 0f; return; }
        _tipL = tipAcc / tipN;
        _rearL = rearAcc / rearN;
        _topL = topN > 0 ? topAcc / topN : _tipL;
        _botL = botN > 0 ? botAcc / botN : _rearL;

        float tipSpread = 0f;
        for (int i = 0; i < _coreN; i++)
        {
            float pf = Vector3.Dot(core[i] - _pivotL, _fwdL);
            if (pf >= cfmax - 0.02f * cspan) tipSpread = Mathf.Max(tipSpread, (core[i] - _tipL).magnitude);
        }
        _tipSpread = tipSpread;

        Vector3 m = _tipL - _rearL;
        _elevMuzzle = Mathf.Asin(Mathf.Clamp(Vector3.Dot(m.normalized, _upL), -1f, 1f)) * Mathf.Rad2Deg;
        _yawMuzzle = Mathf.Atan2(Vector3.Dot(m.normalized, _latL), Vector3.Dot(m.normalized, _fwdL)) * Mathf.Rad2Deg;
        Vector3 s = _topL - _rearL;
        _elevSkull = Mathf.Asin(Mathf.Clamp(Vector3.Dot(s.normalized, _upL), -1f, 1f)) * Mathf.Rad2Deg;
    }

    struct Cand
    {
        public string name;
        public Matrix4x4 m;
        public Cand(string n, Matrix4x4 mm) { name = n; m = mm; }
    }

    /// <summary>
    /// BakeMesh 拿当前姿势的顶点，返回**世界空间**。
    /// ★ 别猜 BakeMesh 在哪个空间 —— 枚举 9 个候选，拿**骨架**当裁判（网格必须包住自己的骨架）。
    ///   本模型 SMR 的 lossyScale = 0.0024（FBX 厘米级），带缩放的候选项会把点云压成 0.03 m。
    /// </summary>
    List<Vector3> GatherWorld()
    {
        bool vb = _v;
        var bPts = new List<Vector3>();
        Vector3 bMin = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
        Vector3 bMax = new Vector3(float.MinValue, float.MinValue, float.MinValue);
        Vector3 bCen = Vector3.zero;
        for (int i = 0; i < _NS; i++)
        {
            var t = _spine[i] as Transform; if (t == null) continue;
            Vector3 p = t.position;
            bPts.Add(p);
            bMin = Vector3.Min(bMin, p); bMax = Vector3.Max(bMax, p);
            bCen += p;
        }
        if (bPts.Count > 0) bCen /= bPts.Count;
        float bSize = Mathf.Max(0.1f, (bMax - bMin).magnitude);

        var raws = new List<Vector3[]>();
        var mats = new List<Matrix4x4[]>();
        var tmp = new Mesh();
        int smrUsed = 0;
        for (int s = 0; s < _smrs.Length; s++)
        {
            var smr = _smrs[s];
            if (smr == null || smr.sharedMesh == null) { if (vb) _sb.AppendLine("  [SMR " + s + "] 空"); continue; }
            if (vb) _sb.AppendLine("  [SMR " + s + "] " + smr.name + "  lossy=" + smr.transform.lossyScale.ToString("F5")
                           + "  bounds.size=" + smr.localBounds.size.ToString("F2"));
            Mesh m = tmp;
            try { smr.BakeMesh(m); } catch (Exception e) { if (vb) _sb.AppendLine("    × BakeMesh: " + e.GetType().Name); continue; }
            var vs = m.vertices;
            if (vs == null || vs.Length == 0) { if (vb) _sb.AppendLine("    × 顶点 0"); continue; }
            smrUsed++;
            var rb = smr.rootBone != null ? smr.rootBone : _modelRoot;
            var ms = new Matrix4x4[NC];
            ms[0] = Matrix4x4.identity;
            ms[1] = smr.transform.localToWorldMatrix;
            ms[2] = _modelRoot.localToWorldMatrix;
            ms[3] = _headPivot.localToWorldMatrix;
            ms[4] = rb.localToWorldMatrix;
            ms[5] = Matrix4x4.TRS(_modelRoot.position, _modelRoot.rotation, Vector3.one);
            ms[6] = Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one);
            ms[7] = Matrix4x4.TRS(rb.position, rb.rotation, Vector3.one);
            ms[8] = Matrix4x4.TRS(_headPivot.position, _headPivot.rotation, Vector3.one);
            raws.Add(vs); mats.Add(ms);
        }
        UnityEngine.Object.Destroy(tmp);

        if (smrUsed == 0 || raws.Count == 0)
        {
            _sb.AppendLine("  × 没有可用顶点");
            _spaceNote = "× BakeMesh 没拿到顶点";
            return new List<Vector3>();
        }
        if (vb) _sb.AppendLine("  骨架：质心 " + bCen.ToString("F2") + "  尺寸 " + bSize.ToString("F2") + " m");

        string[] NM = new string[] { "原样", "SMR(带缩放)", "modelRoot(带缩放)", "颈根骨(带缩放)", "rootBone(带缩放)", "modelRoot(单位缩放)", "SMR(单位缩放)", "rootBone(单位缩放)", "颈根骨(单位缩放)" };
        int best = -1; float bestScore = -1f;
        int stride = Mathf.Max(1, raws[0].Length / 4000);
        for (int c = 0; c < NC; c++)
        {
            Vector3 cMin = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            Vector3 cMax = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            Vector3 cCen = Vector3.zero; int n = 0;
            for (int s = 0; s < raws.Count; s++)
            {
                var vs = raws[s]; var mm = mats[s][c];
                for (int i = 0; i < vs.Length; i += stride)
                {
                    Vector3 p = mm.MultiplyPoint3x4(vs[i]);
                    cMin = Vector3.Min(cMin, p); cMax = Vector3.Max(cMax, p);
                    cCen += p; n++;
                }
            }
            if (n == 0) continue;
            cCen /= n;
            Vector3 size = cMax - cMin;
            float cSize = size.magnitude;
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
            if (vb) _sb.AppendLine("    " + Pad(NM[c], 22) + "bbox " + cMin.ToString("F2") + " → " + cMax.ToString("F2")
                           + "  尺寸 " + cSize.ToString("F2") + "  骨在盒内 " + (frac * 100f).ToString("F0") + "%"
                           + "  得分 " + sc.ToString("F2"));
            if (sc > bestScore) { bestScore = sc; best = c; }
        }

        _spaceNote = "★ BakeMesh 空间判定：采用「" + NM[Mathf.Max(0, best)] + "」（得分 " + bestScore.ToString("F2") + "）";
        var outw = new List<Vector3>();
        for (int s = 0; s < raws.Count; s++)
        {
            var vs = raws[s]; var mm = mats[s][best];
            for (int i = 0; i < vs.Length; i++) outw.Add(mm.MultiplyPoint3x4(vs[i]));
        }
        return outw;
    }

    void DumpScreen(float hg, float ph)
    {
        string s = "SCREEN hg=" + hg.ToString("F2") + " ph=" + ph.ToString("F2")
                   + " neck " + SP(_pivotL) + " tip " + SP(_tipL) + " rear " + SP(_rearL)
                   + " top " + SP(_topL) + " bot " + SP(_botL)
                   + " refA " + SP(_rearL - 1.6f * _fwdL) + " refB " + SP(_rearL + 1.6f * _fwdL);
        _sb.AppendLine("    " + s);
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
            if (d < 0.012f) yield break;
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

    IEnumerator Done()
    {
        Time.captureFramerate = 0;
        try { File.WriteAllText(RP, _sb.ToString()); } catch { }
        Debug.Log("[drg_headnod] 写入 " + RP);
        yield return null;
    }
}

var __hostH24 = new GameObject("drg_headnod");
__hostH24.AddComponent<drg_headnod>();
return "DRG_HEADNOD_STARTED";
