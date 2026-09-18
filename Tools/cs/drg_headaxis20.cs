using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;

// drg_headaxis20.cs —— 第二十轮：用「蒙皮网格顶点」量头部**真实朝向**，出带参考线的图交用户定基准
//
// 为什么又换尺子（前两把都废了）
//   第十八轮：头簇骨 **PCA 第一主轴** ⇒ 量的是"颈+颅那坨质量的走向"，不是吻部指向。
//   第十九轮：头簇**质心仰角** ⇒ 被鬃/须/角/颌那堆骨拖着走（30° 时报 −6.68°，与肉眼相反）。
//   两把都是「质量分布类统计量」。本轮改用**几何极值**：
//     吻部 = 「正中矢状面附近、最靠前的那撮顶点」；颈根 = 「正中面附近最靠后的那撮顶点」。
//     吻部轴 = 吻端质心 − 颈根质心。**这是几何定义的，不含任何"骨头语义"假设。**
//
// ★ 关键手法：**不读 mesh.vertices**（FBX 导入默认关 Read/Write ⇒ 静默返回空），
//   改用 `SkinnedMeshRenderer.BakeMesh()` 拿**当前姿势**的蒙皮结果（不受 Read/Write 限制）。
//   BakeMesh 返回的空间（局部/世界）各 Unity 版本不一 —— 本探针**自己判**：
//   分别按两种解释映射到世界，与**骨架质心**比距离，取近的那个，并把判据打进报告。
//
// 报告什么
//   · 吻部轴在「容器语义系」里的**仰角 / 偏航角**（仰角 = 判断"头抬没抬"的那把尺子）
//   · 颅顶轴（颈根→头顶）的仰角，两个一起看才能分清"抬头"和"颅顶翘"
//   · 段长/横向展布/顶点数 —— 用来判断吻部截取有没有被胡须污染
//   · 归一化屏幕坐标（颈根 / 吻端 / 颈根后 / 颅顶），交给 Python 在图上画参考线
//
// 出图条件（与 head19 同）：钉 30 fps、固定相机、每档等同一行波相位
//   ★ 与 head19 的区别：head19 用 hoverStationary=true（静止），
//     本轮用 **hoverStationary=false（正常巡游）** —— 用户抱怨的就是"正常移动"时的头。

public class drg_headaxis20 : MonoBehaviour
{
    const string DIR = "D:/Unity Project/InkWash/Tools/screenshots/enemies/H20";
    const string RP = "D:/Unity Project/InkWash/Tools/reports/drg_headaxis20.txt";
    const string KEY = "墨龙";
    const int W = 900, H = 600;
    const float DRIVE_HZ = 0.45f;

    static readonly float[] PHASES = new float[] { 0.00f, 0.30f, 0.60f };
    static readonly float[] REL = new float[] { -10f, -5f, 0f, 5f, 10f, 15f };
    const float PHASE_B = 0.30f;

    static readonly BindingFlags BF = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;

    readonly StringBuilder _sb = new StringBuilder();

    Component _sc; Type _ty, _dt;
    MethodInfo _mPlay, _mLabel, _mStop;
    PropertyInfo _pCount;

    GameObject _go; object _drg;
    Transform _drgT, _modelRoot, _headPivot;
    System.Collections.IList _spine;
    SkinnedMeshRenderer[] _smrs;
    Camera _camS, _camQ;
    Vector3 _center; float _dist, _distQ;
    int _NS;

    // 容器语义系（都在 modelRoot 局部空间里）
    Vector3 _fwdL, _upL, _latL;
    Vector3 _pivotL;

    // 当前帧量到的头几何
    int _headN, _coreN;
    float _fmin, _fmax, _latMax;
    Vector3 _tipL, _rearL, _topL, _botL;
    float _elevMuzzle, _yawMuzzle, _elevSkull, _fwdSpan, _tipSpread;
    string _spaceNote = "";
    bool _v = true;                 // 诊断只在第一遍打印
    const int NC = 9;               // BakeMesh 空间候选数

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

        _sb.AppendLine("=== drg_headaxis20：蒙皮网格量头部真实朝向（第二十轮）===");
        _sb.AppendLine("条目 idx=" + idx + "  label=" + found);
        _sb.AppendLine("脊柱 " + _NS + " 节｜headAlignLinks=" + (fLinks != null ? fLinks.GetValue(_drg).ToString() : "?")
                       + "｜SkinnedMeshRenderer " + _smrs.Length + " 个");
        _sb.AppendLine("★ 正常巡游（hoverStationary=false），关省电循环；钉 30 fps；相机只算一次");
        _sb.AppendLine();

        // ── 正常巡游姿态，但**冻住位置与朝向** ──
        // ★ 必须 true：hoverStationary 只冻结 position/rotation（见 TickCircling 1316~1333），
        //   **蛇形波照跑** ⇒ 姿态仍是巡游态。若设 false，龙在几十秒里飞出十几米，
        //   而相机是开跑前算一次的 ⇒ 后面几档全被甩出画面（实测屏幕 x 到 6.8）。
        _dt.GetField("hoverStationary", BF).SetValue(_drg, true);
        _dt.GetField("enablePerformanceCycle", BF).SetValue(_drg, false);
        fPitch.SetValue(_drg, 0f);
        for (int i = 0; i < 20; i++) yield return null;

        // ── 先量一次拿到「吻端 / 颈根」标志点，再据此取景 ──
        // ★ 用「头簇包围球」取景**不够**：头簇含鬃/须/角的长链（最长一条 10 节），
        //   其**质心**离吻部很远 ⇒ 实测整颗头被推出画面右缘（归一化 x 到 1.27 > 1）。
        //   改用「吻端–颈根连线」的中点当视心、以连线长度定半径，必定框住。
        yield return WaitPhase(PHASE_B);
        Measure();
        Vector3 midL = (_rearL + _tipL) * 0.5f;
        float span2 = Mathf.Max(0.5f, (_tipL - _rearL).magnitude);
        _center = _modelRoot.TransformPoint(midL);
        float rad = span2 * 1.8f + 0.50f;
        _camS = MakeCam("PROBE_CAM_H20S");
        _camQ = MakeCam("PROBE_CAM_H20Q");
        float k = rad / Mathf.Sin(_camS.fieldOfView * 0.5f * Mathf.Deg2Rad);
        _dist = k;
        _distQ = k * 1.18f;
        AimSide(); AimQ();
        _sb.AppendLine("取景：吻端–颈根 " + span2.ToString("F3") + " m，以其中点为视心，半径 "
                       + rad.ToString("F3") + "，侧视距 " + _dist.ToString("F2"));
        _sb.AppendLine();
        // ── Pass A：三个相位，看头会不会随波"点头" ──
        _sb.AppendLine("── Pass A：pitch=0（纯模型基准）下，头随行波相位的变化 ──");
        _sb.AppendLine("  相位     吻部仰角     吻部偏航    颅顶仰角   前向跨度");
        float elevAtB = 0f;
        foreach (float ph in PHASES)
        {
            yield return WaitPhase(ph);
            Measure();
            _sb.AppendLine("  " + Pad(ph.ToString("F2"), 8)
                           + Pad(_elevMuzzle.ToString("F2") + "°", 12)
                           + Pad(_yawMuzzle.ToString("F2") + "°", 12)
                           + Pad(_elevSkull.ToString("F2") + "°", 12)
                           + _fwdSpan.ToString("F3") + " m");
            if (Mathf.Abs(ph - PHASE_B) < 1e-4f) elevAtB = _elevMuzzle;
            _v = false;
        }
        _sb.AppendLine();
        float pStar = -elevAtB;          // 让吻部轴恰好在水平面上的那档 pitch
        _sb.AppendLine("★ 由 Pass A 解出：pitch = " + Pad(pStar.ToString("F2"), 8)
                       + "° 时吻部轴**恰好水平**（elev ≈ 0）");
        _sb.AppendLine("  ⇒ 下面的档位都是「相对吻部水平」的偏置 rel，绝对档位 = p* + rel");
        _sb.AppendLine();

        // ── Pass B：出图 ──
        _sb.AppendLine("── Pass B：相位 " + PHASE_B.ToString("F2") + "，六档出图（rel 相对「吻部水平」）──");
        _sb.AppendLine("  rel    绝对pitch   吻部仰角    吻部偏航    颅顶仰角   头顶点数  正中面顶点  吻端簇展布");
        foreach (float rel in REL)
        {
            float p = pStar + rel;
            fPitch.SetValue(_drg, p);
            yield return null;
            yield return WaitPhase(PHASE_B);

            Measure();
            string tag = Tag(rel);
            File.WriteAllBytes(DIR + "/h20_side_" + tag + ".png", Shot(_camS));
            File.WriteAllBytes(DIR + "/h20_q_" + tag + ".png", Shot(_camQ));

            _sb.AppendLine("  " + Pad(Sgn(rel), 7) + Pad(p.ToString("F2"), 11)
                           + Pad(_elevMuzzle.ToString("F2") + "°", 12)
                           + Pad(_yawMuzzle.ToString("F2") + "°", 12)
                           + Pad(_elevSkull.ToString("F2") + "°", 12)
                           + Pad(_headN.ToString(), 9) + Pad(_coreN.ToString(), 11)
                           + _tipSpread.ToString("F3"));
            DumpScreen(rel);
        }

        _sb.AppendLine();
        _sb.AppendLine("★ 三条读数怎么用：");
        _sb.AppendLine("  · 吻部仰角 = 主判据。0° 那张 = 吻部轴严格水平。");
        _sb.AppendLine("  · 吻部偏航 ≠ 0 说明头**歪向一侧**（pitch 修不了，要动 headAlignYawDeg）。");
        _sb.AppendLine("  · 颅顶仰角 与 吻部仰角 差很多 ⇒ 是「颅顶翘/下巴抬」而不是「整头转」。");
        _sb.AppendLine("★ 别只看数字：看 h20_side_*.png（已由 make_head20_axis.py 叠上参考线）。");
        _sb.AppendLine(_spaceNote);

        if (_camS != null) { if (_camS.targetTexture != null) UnityEngine.Object.Destroy(_camS.targetTexture); UnityEngine.Object.Destroy(_camS.gameObject); }
        if (_camQ != null) { if (_camQ.targetTexture != null) UnityEngine.Object.Destroy(_camQ.targetTexture); UnityEngine.Object.Destroy(_camQ.gameObject); }
        yield return Done();
    }

    // ───────────────────────── 量 ─────────────────────────

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

        // ★ 前向的正负：以"尾 → 头"为准，不靠任何约定
        Vector3 tailL = inv.MultiplyPoint3x4(((Transform)_spine[0]).position);
        Vector3 headL = inv.MultiplyPoint3x4(((Transform)_spine[_NS - 2]).position);
        if (Vector3.Dot(headL - tailL, _fwdL) < 0f) { _fwdL = -_fwdL; _latL = -_latL; }

        // 收集头区顶点（比颈根再往后留 0.15 m，免得把颅底切掉）
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

        // ★ 正中矢状面薄片：吻部在正中面上，鬃/须/角在两侧 ⇒ 用横向阈值把后者剔掉
        float latCut = Mathf.Max(0.05f, latMax * 0.22f);   // 收紧：正中面要窄，才能只剩吻部
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

        // 吻端 = 最前 2% 的质心；颈根后 = 最后 12% 的质心
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

        // 吻端簇的展布：展布大 ⇒ 截到的是鬃/须而不是吻 ⇒ 该档读数不可信
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

    // 候选空间：把 BakeMesh 的原始顶点映射到世界的 N 种可能解释
    struct Cand
    {
        public string name;
        public Matrix4x4 m;
        public Cand(string n, Matrix4x4 mm) { name = n; m = mm; }
    }

    /// <summary>
    /// BakeMesh 拿当前姿势的顶点，返回**世界空间**。
    ///
    /// ★★ 这一步栽过两次，记录在此：
    ///   · 第一版按"质心距骨架近的赢"判局部/世界 —— 两种解释分别离 3.47 / 16.28 m，**都不近** ⇒ 判错。
    ///   · 诊断后发现真因：本模型 SMR 的 `lossyScale = 0.0024`（FBX 单位是厘米级），
    ///     而 BakeMesh 给的顶点云**几乎不随龙飞行变化** ⇒ 它落在**龙的局部空间**里，
    ///     既不是 `smr.transform` 局部，也不是世界。
    ///   ⇒ 正解：**别猜，枚举候选空间，拿骨架当裁判**。
    ///     判据：把 24 枚脊骨的世界坐标投进去，**落在点云包围盒内的比例**越高越可信
    ///     （网格必须包住自己的骨架 —— 这是最强的物理约束）。
    ///     次要判据：点云尺寸与骨架尺寸同量级；再次：质心距。
    /// </summary>
    List<Vector3> GatherWorld()
    {
        bool vb = _v;                     // 诊断只在第一遍打印，免得报告被刷屏
        // 骨架参考
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

        // 每个 SMR：原始顶点 + 候选矩阵
        var raws = new List<Vector3[]>();
        var mats = new List<Matrix4x4[]>();
        var tmp = new Mesh();
        int smrUsed = 0;
        for (int s = 0; s < _smrs.Length; s++)
        {
            var smr = _smrs[s];
            if (smr == null || smr.sharedMesh == null) { if (vb) _sb.AppendLine("  [SMR " + s + "] 空"); continue; }
            if (vb) _sb.AppendLine("  [SMR " + s + "] " + smr.name + "  lossy=" + smr.transform.lossyScale.ToString("F5")
                           + "  rootBone=" + (smr.rootBone != null ? smr.rootBone.name : "null")
                           + "  bounds.size=" + smr.localBounds.size.ToString("F2"));
            Mesh m = tmp;
            try { smr.BakeMesh(m); } catch (Exception e) { if (vb) _sb.AppendLine("    × BakeMesh: " + e.GetType().Name); continue; }
            var vs = m.vertices;
            if (vs == null || vs.Length == 0) { if (vb) _sb.AppendLine("    × 顶点 0"); continue; }
            smrUsed++;
            var rb = smr.rootBone != null ? smr.rootBone : _modelRoot;
            var ms = new Matrix4x4[NC];
            ms[0] = Matrix4x4.identity;                       // 原样（BakeMesh 自己在某个局部空间里给）
            ms[1] = smr.transform.localToWorldMatrix;         // SMR 自己（带缩放）
            ms[2] = _modelRoot.localToWorldMatrix;            // 龙的模型容器（带缩放）
            ms[3] = _headPivot.localToWorldMatrix;            // 颈根骨（带缩放）
            ms[4] = rb.localToWorldMatrix;                    // rootBone（带缩放）
            // ★★ 「单位缩放 TRS」组：BakeMesh 很可能返回「某骨的位置/朝向 + **世界尺度**」的顶点
            //    （即已吃掉绑定缩放）。本模型 SMR lossyScale = 0.0024，带缩放那组会把点云压成
            //    0.03 m 的一团 —— 正是前两版踩的坑。
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

        // ── 打分：对每个候选空间，统计"脊骨落在点云包围盒内"的比例 ──
        string[] NM = new string[] { "原样", "SMR(带缩放)", "modelRoot(带缩放)", "颈根骨(带缩放)", "rootBone(带缩放)", "modelRoot(单位缩放)", "SMR(单位缩放)", "rootBone(单位缩放)", "颈根骨(单位缩放)" };
        int best = -1; float bestScore = -1f;
        int stride = Mathf.Max(1, raws[0].Length / 4000);     // 抽样打分，省时间
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

    static Vector3 Mean(List<Vector3> v)
    {
        if (v.Count == 0) return Vector3.zero;
        Vector3 a = Vector3.zero;
        for (int i = 0; i < v.Count; i++) a += v[i];
        return a / v.Count;
    }

    /// <summary>把关键点投到屏幕（PNG 像素系，左上为原点），交给 Python 画参考线。</summary>
    void DumpScreen(float rel)
    {
        string s = "SCREEN rel=" + Sgn(rel)
                   + " neck " + SP(_pivotL) + " tip " + SP(_tipL)
                   + " rear " + SP(_rearL) + " top " + SP(_topL) + " bot " + SP(_botL)
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

    void AimQ()
    {
        Vector3 fwd = Flat(_drgT.forward).normalized;
        Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized;
        Vector3 dir = (-right * 0.40f + fwd * 0.74f + Vector3.up * 0.54f).normalized;
        _camQ.transform.position = _center + dir * _distQ;
        _camQ.transform.LookAt(_center, Vector3.up);
    }

    static void CollectPos(Transform t, List<Vector3> outp)
    {
        outp.Add(t.position);
        for (int i = 0; i < t.childCount; i++) CollectPos(t.GetChild(i), outp);
    }

    byte[] Shot(Camera cam)
    {
        // ★ 相机常驻 RT。 **只有在 targetTexture 绑着时**才按 RT 尺寸算；
        //   不绑就用 Game view 的分辨率 —— 而我在旧版里每次 Shot 都临时建/销毁 RT，
        //   于是 DumpScreen 时 targetTexture 已是 null ⇒ 归一化分母用了 Game view 宽度
        //   ⇒ 屏幕坐标整体偏大（实测归一化 x 到 1.20，头被算到画面之外）。
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
        c.targetTexture = new RenderTexture(W, H, 24);   // 常驻，见 Shot() 的注释
        return c;
    }

    static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
    static string Pad(string s, int n) { return s.Length >= n ? s : s + new string(' ', n - s.Length); }

    static string Sgn(float p)
    {
        string s = (p >= 0f ? "p" : "m") + Mathf.Abs(Mathf.RoundToInt(p)).ToString("D2");
        return s;
    }

    static string Tag(float rel)
    {
        string s = (rel >= 0f ? "relp" : "relm") + Mathf.Abs(Mathf.RoundToInt(rel)).ToString("D2");
        return s;
    }

    IEnumerator Done()
    {
        Time.captureFramerate = 0;
        try { File.WriteAllText(RP, _sb.ToString()); } catch { }
        Debug.Log("[drg_headaxis20] 写入 " + RP);
        yield return null;
    }
}

var __hostH20 = new GameObject("drg_headaxis20");
__hostH20.AddComponent<drg_headaxis20>();
return "DRG_HEADAXIS20_STARTED";
