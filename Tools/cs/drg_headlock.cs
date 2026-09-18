using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;

// drg_headlock.cs —— 第二十五轮：验证「头锁定基准姿态」是不是「龙头始终面向前方」的正解
//
// 【缘起】用户第四次反馈头部：「龙头要始终面向前方，现在还是有问题」，
//   并给出判据来源：「常态情况下，这个 fbx 精致姿态里面的头的位置就是对的，
//   跟脖子的相对位置是对的，方向也是」⇒ 目标姿态 = **模型原生基准姿态**，
//   移动时只要保持它即可（不是"调到某个静态角度"，而是要**消除摆动**）。
//
// 【上一轮（drg_headnod）的结论】移动时头在**上下甩 49.65°、左右歪 13.42°**（吻部轴、三相位），
//   机制 = `FromToRotation(倾斜基准段, 水平切向)` 是最小旋转，切向左右摆时把偏航耦合出俯仰，
//   头簇刚性挂在 spine[n-2] 上只能跟着甩。那条路给出的旋钮 `swimWaveHeadGain = 0`
//   把**头端一大段**的波都抽掉（代价：脖子变直杆）。
//
// 【本轮新增】`headLockToBase`：末尾 `headAlignLinks` 节**不读曲线切向**，直接采用基准朝向。
//   ⇒ 头 = 模型原生基准姿态（+ headAlign* 三条对齐角），**与行波相位无关**；
//   ⇒ **脖子照常摆动**（只锁头簇那 2 节：`spine[n-2]` 与无几何的 `spine[n-1]`）。
//   本探针要证明三件事：
//     ① 锁定后吻部仰角/偏航极差 ≈ 0（头真的定住了）
//     ② 锁定后**脖子/中段仍在摆**（不是把整条龙冻住）
//     ③ 出图目视：锁定的头 = 用户认可的那个 FBX 姿态
//
// 手法：hoverStationary = true（位置/朝向冻结，蛇形波照跑）、swimWaveHeadGain 保持现值 1、
//       钉 30 fps、侧视相机只算一次 ⇒ 各档图可直接叠看。

public class drg_headlock : MonoBehaviour
{
    const string DIR = "D:/Unity Project/InkWash/Tools/screenshots/enemies/H25";
    const string RP = "D:/Unity Project/InkWash/Tools/reports/drg_headlock.txt";
    const string KEY = "墨龙";
    const int W = 900, H = 600;
    const float DRIVE_HZ = 0.45f;

    static readonly float[] PHASES = new float[] { 0.00f, 0.30f, 0.60f };

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
    float _elevMuzzle, _yawMuzzle;
    Vector3 _tipL, _rearL;
    string _spaceNote = "";
    bool _v = true;
    const int NC = 9;

    int _shots;

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
        var fHg    = _dt.GetField("swimWaveHeadGain", BF);
        var fHover = _dt.GetField("hoverStationary", BF);
        var fPerf  = _dt.GetField("enablePerformanceCycle", BF);
        var fLock  = _dt.GetField("headLockToBase", BF);
        if (_spine == null || _modelRoot == null || fPitch == null || fHg == null || fLock == null)
        { _sb.AppendLine("× 字段缺失（headLockToBase 还没编译进去？）"); yield return Done(); }

        _NS = _spine.Count;
        _headPivot = _spine[_NS - 2] as Transform;
        _smrs = _go.GetComponentsInChildren<SkinnedMeshRenderer>(true);

        _sb.AppendLine("=== drg_headlock：headLockToBase —— 「龙头始终面向前方」（第二十五轮）===");
        _sb.AppendLine("条目 idx=" + idx + "  label=" + found);
        _sb.AppendLine("脊柱 " + _NS + " 节｜headAlignLinks=" + fLinks.GetValue(_drg)
                       + "｜swimWaveHeadGain 现值=" + fHg.GetValue(_drg)
                       + "｜headLockToBase 现值=" + fLock.GetValue(_drg));
        _sb.AppendLine("★ hoverStationary=true（位置/朝向冻结，**蛇形波照跑**）｜headAlignPitch 见各档");
        _sb.AppendLine("★ 钉 30 fps｜侧视相机只算一次 ⇒ 各档图可直接叠看");
        _sb.AppendLine();

        fHover.SetValue(_drg, true);
        fPerf.SetValue(_drg, false);
        fHg.SetValue(_drg, 1f);
        for (int i = 0; i < 20; i++) yield return null;

        // ── 取景：以「吻端–颈根连线」中点为视心（不能只用头簇包围球：含鬃/须长链会把头推出画面）──
        fLock.SetValue(_drg, false);
        fPitch.SetValue(_drg, 0f);
        yield return WaitPhase(PHASES[1]);
        Measure();
        Vector3 midL = (_rearL + _tipL) * 0.5f;
        float span2 = Mathf.Max(0.5f, (_tipL - _rearL).magnitude);
        _center = _modelRoot.TransformPoint(midL);
        float rad = span2 * 1.8f + 0.50f;
        _camS = MakeCam("PROBE_CAM_H25S");
        _dist = rad / Mathf.Sin(_camS.fieldOfView * 0.5f * Mathf.Deg2Rad);
        AimSide();
        _sb.AppendLine("取景：吻端–颈根 " + span2.ToString("F3") + " m，侧视距 " + _dist.ToString("F2"));
        _sb.AppendLine();

        // ── 主表：三档 × 三相位 ──
        _sb.AppendLine("── 档位 × 行波相位 ⇒ 吻部朝向 + 链上横向摆幅（容器局部系，m）──");
        _sb.AppendLine("   档位                        ph0.00      ph0.30      ph0.60    仰角极差   偏航极差   颈[19]摆幅  中[12]摆幅");
        string[] labels = new string[] { "A 不锁 pitch=0（旧行为）", "B 锁定 pitch=0（纯基准）", "C 锁定 pitch=-12（现静态值）" };
        bool[] locks = new bool[] { false, true, true };
        float[] pitches = new float[] { 0f, 0f, -12f };

        for (int d = 0; d < 3; d++)
        {
            fLock.SetValue(_drg, locks[d]);
            fPitch.SetValue(_drg, pitches[d]);
            yield return null;

            var ev = new float[PHASES.Length];
            var yv = new float[PHASES.Length];
            var nv = new float[PHASES.Length];
            var mv = new float[PHASES.Length];

            for (int k = 0; k < PHASES.Length; k++)
            {
                yield return WaitPhase(PHASES[k]);
                Measure();
                ev[k] = _elevMuzzle; yv[k] = _yawMuzzle;
                nv[k] = LatOf(19); mv[k] = LatOf(12);

                bool shoot = (d < 2) || (k == 0);
                if (shoot)
                {
                    string tag0 = (d == 0 ? "hl_free" : (d == 1 ? "hl_locked" : "hl_locked12"));
                    WriteShot(tag0 + "_ph" + ((int)(PHASES[k] * 100)).ToString("D3"));
                }
                _v = false;
            }

            _sb.AppendLine("   " + Pad(labels[d], 26)
                           + Pad(ev[0].ToString("F2") + "°", 12)
                           + Pad(ev[1].ToString("F2") + "°", 12)
                           + Pad(ev[2].ToString("F2") + "°", 12)
                           + Pad(Spread(ev).ToString("F2") + "°", 11)
                           + Pad(Spread(yv).ToString("F2") + "°", 11)
                           + Pad(Spread(nv).ToString("F3"), 12)
                           + Spread(mv).ToString("F3"));
        }

        fLock.SetValue(_drg, true);
        fPitch.SetValue(_drg, -12f);

        _sb.AppendLine();
        _sb.AppendLine("★ 读法：");
        _sb.AppendLine("  · 「仰角/偏航极差」= 移动一个行波周期内，头摆了多大。**越小越像「头始终面向前方」**。");
        _sb.AppendLine("  · 「颈[19]/中[12]摆幅」= 对应骨节的**横向位置**极差 ⇒ 证明身体**没有被冻住**。");
        _sb.AppendLine("  · A 档 = 旧行为（头随波甩）；B 档 = 头锁定 + 纯基准姿态；C 档 = 头锁定 + 现静态值。");
        _sb.AppendLine("  · 出图 " + _shots + " 张 → " + DIR);
        _sb.AppendLine(_spaceNote);

        if (_camS != null) { if (_camS.targetTexture != null) UnityEngine.Object.Destroy(_camS.targetTexture); UnityEngine.Object.Destroy(_camS.gameObject); }
        yield return Done();
    }

    // ───────────────────────── 量（口径与 drg_headaxis20 / drg_headnod 一致，便于对照）─────────────────────────

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
        if (_headN < 64) { _elevMuzzle = 0f; _yawMuzzle = 0f; _coreN = 0; return; }

        float fmin = float.MaxValue, fmax = float.MinValue, latMax = 0f;
        for (int i = 0; i < _headN; i++)
        {
            Vector3 d = head[i] - _pivotL;
            float pf = Vector3.Dot(d, _fwdL), pl = Mathf.Abs(Vector3.Dot(d, _latL));
            if (pf < fmin) fmin = pf;
            if (pf > fmax) fmax = pf;
            if (pl > latMax) latMax = pl;
        }

        float latCut = Mathf.Max(0.05f, latMax * 0.22f);
        var core = new List<Vector3>();
        for (int i = 0; i < _headN; i++)
        {
            Vector3 d = head[i] - _pivotL;
            if (Mathf.Abs(Vector3.Dot(d, _latL)) <= latCut) core.Add(head[i]);
        }
        _coreN = core.Count;
        if (_coreN < 32) { _elevMuzzle = 0f; _yawMuzzle = 0f; return; }

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
        for (int i = 0; i < _coreN; i++)
        {
            float pf = Vector3.Dot(core[i] - _pivotL, _fwdL);
            if (pf >= cfmax - 0.02f * cspan) { tipAcc += core[i]; tipN++; }
            if (pf <= cfmin + 0.12f * cspan) { rearAcc += core[i]; rearN++; }
        }
        if (tipN == 0 || rearN == 0) { _elevMuzzle = 0f; _yawMuzzle = 0f; return; }
        _tipL = tipAcc / tipN;
        _rearL = rearAcc / rearN;

        Vector3 m = _tipL - _rearL;
        _elevMuzzle = Mathf.Asin(Mathf.Clamp(Vector3.Dot(m.normalized, _upL), -1f, 1f)) * Mathf.Rad2Deg;
        _yawMuzzle = Mathf.Atan2(Vector3.Dot(m.normalized, _latL), Vector3.Dot(m.normalized, _fwdL)) * Mathf.Rad2Deg;
    }

    /// <summary>某个脊柱节在**容器局部系**里沿横轴（_latL）的坐标。用来证明身体还在摆。</summary>
    float LatOf(int i)
    {
        if (i < 0 || i >= _NS) return 0f;
        var t = _spine[i] as Transform;
        if (t == null) return 0f;
        Vector3 p = _modelRoot.worldToLocalMatrix.MultiplyPoint3x4(t.position);
        return Vector3.Dot(p, _latL);
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

    void WriteShot(string name)
    {
        File.WriteAllBytes(DIR + "/" + name + ".png", Shot(_camS));
        _shots++;
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

    static float Spread(float[] a)
    {
        float mn = float.MaxValue, mx = float.MinValue;
        for (int i = 0; i < a.Length; i++) { if (a[i] < mn) mn = a[i]; if (a[i] > mx) mx = a[i]; }
        return mx - mn;
    }

    static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
    static string Pad(string s, int n) { return s.Length >= n ? s : s + new string(' ', n - s.Length); }

    IEnumerator Done()
    {
        Time.captureFramerate = 0;
        try { File.WriteAllText(RP, _sb.ToString()); } catch { }
        Debug.Log("[drg_headlock] 写入 " + RP);
        yield return null;
    }
}

var __hostH25 = new GameObject("drg_headlock");
__hostH25.AddComponent<drg_headlock>();
return "DRG_HEADLOCK_STARTED";
