using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;

// drg_form2.cs —— 墨龙「原地形态 v2」验收 + 录像（第十七轮）
//
// 用户指令：
//   「尾巴要在同一个水平线上，不能单独摆动，龙头没有摆正，现在歪着头，跟落枕了一样」
//
// ■ 与 v1（drg_form.cs）的差别：
//   1. 段 A 换成**正侧视**：尾巴在不在同一水平线上，这个角度一眼可见（v1 只有俯视/跟车）。
//   2. 判据⑨只统计**被判定为腿**的分支 —— 尾巴与头饰簇现在是**刚性**的，
//      把它们算进"最差摆角"会得到 0° 的假阴性（v1 的口径已不适用）。
//   3. 新增两条直接对应用户原话的判据：
//        ⑩ 尾分支**末端相对根节**的 y 极差 ≤ 0.05 m（尾巴在同一水平线上）
//        ⑪ 头饰簇摆角极差 = 0°（头不再被奇形分支拽歪）
//
// ■ 成因（drg_axis 定案，报告 drg_axis_before.txt）：
//   `ApplyLimbMotion` 把 19 条几何分支**全部**当腿驱动，其中 1 条是整条尾巴（3.24 m）、
//   14 条是头饰簇 ⇒ 尾巴绕横轴上下翻（末端 ±1.37 m）、头顶炸毛。脊柱本身是水平的。
//
// ★ 纪律：取景用骨骼链实际位置（SMR.bounds 是绑定姿势盒）｜参考系取 drg.transform
//   （与驱动 `Flat(transform.forward)` 同源）｜不写死期望值｜Codely 脚本要有顶层语句。

public class drg_form2 : MonoBehaviour
{
    const string RP = "D:/Unity Project/InkWash/Tools/reports/drg_form2.txt";
    const string SD = "D:/Unity Project/InkWash/Tools/screenshots/enemies/DF2";
    const string KEY = "墨龙";
    const string PAT = "df2_";

    const int   FPS = 30;
    const int   NF  = 60;        // 每段 60 帧 = 2.0 s（swimWaveFreq 0.45 Hz ⇒ 0.9 周期）
    const float ZT  = 0.06f;     // 过零阈值（m）
    const float ANG_MIN = 5f;    // 「动起来了」的角度下限（度）
    const float PLANE_TOL = 0.05f;

    static readonly string[] TAG = {
        "A 正侧视 · 波数 1.00（看尾巴在不在同一水平线上）",
        "B 俯视 · 波数 0.75（一个弓）",
        "C 俯视 · 波数 1.00（一个完整 S）",
        "D 俯视 · 波数 1.25（一个 S + 1/4 波）",
        "E 俯视 · 波数 1.50（一个半波）",
    };
    static readonly string[] CAM = { "side", "top", "top", "top", "top" };
    static readonly float[]  WAV = { 1.0f, 0.75f, 1.0f, 1.25f, 1.5f };

    readonly StringBuilder _sb = new StringBuilder();
    Component _sc; Type _ty, _dt;
    MethodInfo _mPlay, _mLabel, _mStop;
    PropertyInfo _pCount;
    Camera _cam;
    GameObject _go;
    Transform _actor, _vroot, _drgT;
    IList _spine, _limbRoots, _limbBaseRel, _limbDriven, _limbAxisDot;
    FieldInfo _fWave, _fAmp, _fFreq, _fStat, _fLimbDeg, _fDrivenCnt;
    bool[] _isLeg;
    Transform[] _limbTip;
    float[] _limbReach;

    int _saved;
    Vector3 _pos0;

    static readonly BindingFlags BF = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;

    void Start() { StartCoroutine(Run()); }

    IEnumerator Run()
    {
        yield return null; yield return null;

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

        _mPlay  = _ty.GetMethod("PlayForCapture", BF);
        _mLabel = _ty.GetMethod("ItemLabel", BF);
        _mStop  = _ty.GetMethod("StopAutoClose", BF);
        _pCount = _ty.GetProperty("ItemCount", BF);

        int count = (int)_pCount.GetValue(_sc, null);
        int idx = -1; string found = "";
        for (int i = 0; i < count; i++)
        {
            string lb = (string)_mLabel.Invoke(_sc, new object[] { i });
            if (lb.Contains(KEY) && lb.Contains("盘旋")) { idx = i; found = lb; break; }
        }
        if (idx < 0) { _sb.AppendLine("× 没找到「墨龙 + 盘旋」条目"); yield return Done(); }

        _sb.AppendLine("=== drg_form2：墨龙「原地形态 v2」验收（头正 / 尾平）===");
        _sb.AppendLine("条目 idx=" + idx + "  label=" + found);
        _sb.AppendLine("captureFramerate=" + FPS + "  每段 " + NF + " 帧 ("
            + (NF / (float)FPS).ToString("F1") + " s)  共 " + TAG.Length + " 段 = "
            + (TAG.Length * NF / (float)FPS).ToString("F1") + " s");
        _sb.AppendLine();

        _mStop.Invoke(_sc, null);
        _mPlay.Invoke(_sc, new object[] { idx });
        for (int i = 0; i < 90; i++) yield return null;

        _go = GameObject.Find("Showcase_Actor_" + idx);
        if (_go == null) { _sb.AppendLine("× 没找到 actor 实例 Showcase_Actor_" + idx); yield return Done(); }
        _actor = _go.transform;

        var drg = _go.GetComponent(_dt);
        _drgT = drg.transform;
        _vroot = _dt.GetField("_modelRoot", BF).GetValue(drg) as Transform;
        _spine = _dt.GetField("_spine", BF).GetValue(drg) as IList;
        _limbRoots = _dt.GetField("_limbRoots", BF).GetValue(drg) as IList;
        _limbBaseRel = _dt.GetField("_limbBaseRel", BF).GetValue(drg) as IList;
        _limbDriven = _dt.GetField("_limbDriven", BF).GetValue(drg) as IList;
        _limbAxisDot = _dt.GetField("_limbAxisDot", BF).GetValue(drg) as IList;
        _fDrivenCnt = _dt.GetField("_limbDrivenCount", BF);
        _fWave   = _dt.GetField("swimWaveCount", BF);
        _fAmp    = _dt.GetField("swimWaveAmp", BF);
        _fFreq   = _dt.GetField("swimWaveFreq", BF);
        _fStat   = _dt.GetField("hoverStationary", BF);
        _fLimbDeg = _dt.GetField("limbSwingDeg", BF);

        int NS = _spine == null ? 0 : _spine.Count;
        int NL = _limbRoots == null ? 0 : _limbRoots.Count;
        if (NS < 3) { _sb.AppendLine("× 脊柱节数 < 3"); yield return Done(); }

        if (_fStat != null && !(bool)_fStat.GetValue(drg))
        {
            _sb.AppendLine("⚠ hoverStationary 是 False ⇒ 强制打开（本轮结论都依赖原地）");
            _fStat.SetValue(drg, true);
            for (int i = 0; i < 6; i++) yield return null;
        }

        // ── 分支分类快照（与 drg_axis 同一口径）──
        _isLeg = new bool[NL];
        _limbTip = new Transform[NL];
        _limbReach = new float[NL];
        int nLeg = 0, nTail = 0, nHead = 0;
        for (int k = 0; k < NL; k++)
        {
            var b = _limbRoots[k] as Transform;
            if (b == null) continue;
            _isLeg[k] = _limbDriven != null && k < _limbDriven.Count && (bool)_limbDriven[k];
            float ad = _limbAxisDot != null && k < _limbAxisDot.Count ? (float)_limbAxisDot[k] : -1f;
            if (_isLeg[k]) nLeg++; else if (ad >= 0.8f) nTail++; else nHead++;

            float reach = 0f; Transform tip = b; float best = -1f;
            var st = new Stack<Transform>(); st.Push(b);
            while (st.Count > 0)
            {
                var t = st.Pop();
                float d = (t.position - b.position).magnitude;
                if (d > reach) reach = d;
                if (d > best) { best = d; tip = t; }
                for (int c = 0; c < t.childCount; c++) st.Push(t.GetChild(c));
            }
            _limbReach[k] = reach;
            _limbTip[k] = tip;
        }

        float baseFold = 0f;
        for (int i = 0; i + 1 < NS; i++)
        {
            var a = _spine[i] as Transform; var b = _spine[i + 1] as Transform;
            if (a != null && b != null) baseFold += Vector3.Distance(a.position, b.position);
        }

        _sb.AppendLine("── 运行时参数回读 ──");
        _sb.AppendLine("  hoverStationary = " + _fStat.GetValue(drg) + "   ← 必须 True（原地调形态）");
        _sb.AppendLine("  swimWaveAmp     = " + _fAmp.GetValue(drg));
        _sb.AppendLine("  swimWaveFreq    = " + _fFreq.GetValue(drg));
        _sb.AppendLine("  swimWaveCount   = " + _fWave.GetValue(drg) + "（会被逐段覆盖）");
        _sb.AppendLine("  limbSwingDeg    = " + _fLimbDeg.GetValue(drg));
        _sb.AppendLine();
        _sb.AppendLine("_spine.Count = " + NS + "   _modelRoot = " + (_vroot == null ? "NULL" : _vroot.name));
        _sb.AppendLine("_limbRoots.Count = " + NL
            + "   ⇒ 腿 " + nLeg + " / 尾巴型 " + nTail + " / 头饰型 " + nHead
            + "（脚本内 _limbDrivenCount = " + (_fDrivenCnt == null ? "?" : _fDrivenCnt.GetValue(drg).ToString()) + "）");
        for (int k = 0; k < NL; k++)
        {
            var b = _limbRoots[k] as Transform;
            if (b == null) { continue; }
            float ad = _limbAxisDot != null && k < _limbAxisDot.Count ? (float)_limbAxisDot[k] : -1f;
            _sb.AppendLine("   [" + (k < 10 ? " " : "") + k + "] " + (_isLeg[k] ? "★驱动" : "·刚性") + " "
                + b.name + "  伸展 " + _limbReach[k].ToString("F2") + " m  |z|=" + ad.ToString("F3"));
        }
        _sb.AppendLine("基准【折线总长】= " + baseFold.ToString("F3") + " m（刚性骨骼 ⇒ 恒定）");
        _sb.AppendLine();

        if (_fStat != null && !(bool)_fStat.GetValue(drg)) { _fStat.SetValue(drg, true); for (int i = 0; i < 6; i++) yield return null; }

        _cam = MakeCam();
        _pos0 = _actor.position;
        Time.captureFramerate = FPS;

        float driftMax = 0f;
        float tailAngAll = 0f, headAngAll = 0f, legAngAll = 0f;
        float tailPlaneAll = 0f, headClusterAll = 0f;

        for (int s = 0; s < TAG.Length; s++)
        {
            _fWave.SetValue(drg, WAV[s]);
            yield return null;

            _sb.AppendLine("──────── 段 " + (char)('A' + s) + "：" + TAG[s] + " ────────");
            _sb.AppendLine("  相机=" + CAM[s] + "  波数=" + WAV[s]
                + "  帧区间 " + (s * NF).ToString("D4") + "~" + (s * NF + NF - 1).ToString("D4"));

            int zMin = int.MaxValue, zMax = int.MinValue; float zSum = 0f;
            float p2pMin = float.MaxValue, p2pMax = float.MinValue, p2pSum = 0f;
            float foldMin = float.MaxValue, foldMax = float.MinValue;
            float axMin = float.MaxValue, axMax = float.MinValue;
            float hMax = 0f, posMax = float.MinValue, negMin = float.MaxValue;
            var linkAngMin = new float[NS]; var linkAngMax = new float[NS];
            for (int i = 0; i < NS; i++) { linkAngMin[i] = float.MaxValue; linkAngMax[i] = float.MinValue; }
            var legMin = new float[NL]; var legMax = new float[NL];
            var rigMin = new float[NL]; var rigMax = new float[NL];
            var tailYMin = new float[NL]; var tailYMax = new float[NL];
            for (int k = 0; k < NL; k++)
            {
                legMin[k] = float.MaxValue; legMax[k] = float.MinValue;
                rigMin[k] = float.MaxValue; rigMax[k] = float.MinValue;
                tailYMin[k] = float.MaxValue; tailYMax[k] = float.MinValue;
            }

            for (int f = 0; f < NF; f++)
            {
                try
                {
                    var pos = new Vector3[NS];
                    for (int i = 0; i < NS; i++) { var t = _spine[i] as Transform; pos[i] = t != null ? t.position : Vector3.zero; }

                    Vector3 fwd = _drgT.forward; fwd.y = 0f;
                    if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.forward;
                    fwd.Normalize();
                    Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized;

                    Vector3 o = pos[0];
                    var lat = new float[NS];
                    float fold = 0f;
                    for (int i = 0; i < NS; i++)
                    {
                        Vector3 d = pos[i] - o;
                        lat[i] = Vector3.Dot(d, right);
                        if (i > 0) fold += Vector3.Distance(pos[i - 1], pos[i]);
                    }
                    float axial = Vector3.Dot(pos[NS - 1] - o, fwd);

                    for (int i = 0; i + 1 < NS; i++)
                    {
                        Vector3 d = pos[i + 1] - pos[i];
                        Vector3 dh = new Vector3(d.x, 0f, d.z);
                        if (dh.sqrMagnitude < 1e-10f) continue;
                        float ang = Vector3.SignedAngle(fwd, dh, Vector3.up);
                        if (ang < linkAngMin[i]) linkAngMin[i] = ang;
                        if (ang > linkAngMax[i]) linkAngMax[i] = ang;
                    }

                    for (int k = 0; k < NL; k++)
                    {
                        var b = _limbRoots[k] as Transform;
                        if (b == null || k >= _limbBaseRel.Count || _limbBaseRel[k] == null) continue;
                        float a = Quaternion.Angle((Quaternion)_limbBaseRel[k], b.localRotation);
                        if (_isLeg[k]) { if (a < legMin[k]) legMin[k] = a; if (a > legMax[k]) legMax[k] = a; }
                        else { if (a < rigMin[k]) rigMin[k] = a; if (a > rigMax[k]) rigMax[k] = a; }
                        var tip = _limbTip[k];
                        if (tip != null)
                        {
                            float ry = tip.position.y - b.position.y;
                            if (ry < tailYMin[k]) tailYMin[k] = ry;
                            if (ry > tailYMax[k]) tailYMax[k] = ry;
                        }
                    }

                    int z = 0, prevs = 0;
                    for (int i = 0; i < NS; i++)
                    {
                        int sg = lat[i] > ZT ? 1 : (lat[i] < -ZT ? -1 : 0);
                        if (sg != 0) { if (prevs != 0 && sg != prevs) z++; prevs = sg; }
                    }

                    float mn = float.MaxValue, mx = float.MinValue;
                    for (int i = 0; i < NS; i++) { if (lat[i] < mn) mn = lat[i]; if (lat[i] > mx) mx = lat[i]; }
                    float p2p = mx - mn;
                    if (mx > posMax) posMax = mx;
                    if (mn < negMin) negMin = mn;

                    if (z < zMin) zMin = z; if (z > zMax) zMax = z; zSum += z;
                    if (p2p < p2pMin) p2pMin = p2p; if (p2p > p2pMax) p2pMax = p2p; p2pSum += p2p;
                    if (Mathf.Abs(lat[NS - 1]) > hMax) hMax = Mathf.Abs(lat[NS - 1]);
                    if (fold < foldMin) foldMin = fold; if (fold > foldMax) foldMax = fold;
                    if (axial < axMin) axMin = axial; if (axial > axMax) axMax = axial;

                    float drift = Flat(_actor.position - _pos0).magnitude;
                    if (drift > driftMax) driftMax = drift;

                    if (f % 15 == 0)
                    {
                        var line = new StringBuilder("  帧 " + f.ToString("D2") + "  过零=" + z
                            + "  峰峰=" + p2p.ToString("F2") + "  头=" + lat[NS - 1].ToString("F2")
                            + "  折线=" + fold.ToString("F2") + "  轴向=" + axial.ToString("F2") + "  lat=");
                        for (int i = 0; i < NS; i += 4) line.Append(lat[i].ToString("F2") + " ");
                        _sb.AppendLine(line.ToString());
                    }

                    Snap(s * NF + f, CAM[s]);
                }
                catch (Exception ex) { _sb.AppendLine("  × 帧 " + f + " 异常 " + ex.Message); }

                Flush();
                yield return null;
            }

            float foldErr = Mathf.Max(Mathf.Abs(foldMin - baseFold), Mathf.Abs(foldMax - baseFold));
            float tailAng = linkAngMax[0] - linkAngMin[0];
            float headAng = linkAngMax[NS - 2] - linkAngMin[NS - 2];
            if (tailAng > tailAngAll) tailAngAll = tailAng;
            if (headAng > headAngAll) headAngAll = headAng;

            float legWorst = float.MaxValue;
            var lw = new StringBuilder();
            for (int k = 0; k < NL; k++)
            {
                if (!_isLeg[k]) continue;
                float a = legMax[k] - legMin[k];
                lw.Append("[" + k + "]" + a.ToString("F1") + "° ");
                if (a < legWorst) legWorst = a;
                if (a > legAngAll) legAngAll = a;
            }

            // 刚性分支（尾 / 头饰）的摆角极差，应当恒为 0
            float rigWorst = 0f;
            // 尾巴（沿身体轴的那条）末端 y 极差
            float tailPlane = 0f; string tailName = "-";
            for (int k = 0; k < NL; k++)
            {
                if (_isLeg[k]) continue;
                float ra = rigMax[k] - rigMin[k];
                if (ra > rigWorst) rigWorst = ra;
                float ad = _limbAxisDot != null && k < _limbAxisDot.Count ? (float)_limbAxisDot[k] : 0f;
                if (ad >= 0.8f && _limbReach[k] > 2f)
                {
                    float ty = tailYMax[k] - tailYMin[k];
                    if (ty > tailPlane) { tailPlane = ty; tailName = ((Transform)_limbRoots[k]).name; }
                }
            }
            if (tailPlane > tailPlaneAll) tailPlaneAll = tailPlane;
            if (rigWorst > headClusterAll) headClusterAll = rigWorst;

            _sb.AppendLine("  ── 段 " + (char)('A' + s) + " 汇总 ──");
            _sb.AppendLine("    过零   " + zMin + " ~ " + zMax + "  均值 " + (zSum / NF).ToString("F2") + "（旧口径，仅存档）");
            _sb.AppendLine("  ★ 完整S 正峰最大 " + posMax.ToString("F2") + " m / 负峰最小 " + negMin.ToString("F2") + " m   "
                + ((posMax > 0.30f && negMin < -0.30f) ? "✅ 全程有正弯也有负弯" : "❌ 某侧缺失（退化成 C 形）"));
            _sb.AppendLine("    峰峰   " + p2pMin.ToString("F2") + " ~ " + p2pMax.ToString("F2")
                + "  均值 " + (p2pSum / NF).ToString("F2") + " m   "
                + (p2pSum / NF >= 1.2f ? "✅" : "⚠ 幅度偏小"));
            _sb.AppendLine("    头端|lat| 最大 " + hMax.ToString("F3") + " m（记录用）");
            _sb.AppendLine("    折线   " + foldMin.ToString("F3") + " ~ " + foldMax.ToString("F3")
                + " m（基准 " + baseFold.ToString("F3") + "，偏差 " + foldErr.ToString("F4") + "）   "
                + (foldErr <= 0.02f ? "✅ 未塌缩" : "❌ 身长变了"));
            _sb.AppendLine("    轴向跨度 " + axMin.ToString("F2") + " ~ " + axMax.ToString("F2") + " m");
            _sb.AppendLine("  ★ ⑦ 尾端（第 0 节）段方向角极差 = " + tailAng.ToString("F2") + "°   "
                + (tailAng >= ANG_MIN ? "✅ 身体波形到尾端" : "❌ 尾端没动"));
            _sb.AppendLine("  ★ ⑧ 头端（第 " + (NS - 2) + " 节）段方向角极差 = " + headAng.ToString("F2") + "°   "
                + (headAng >= ANG_MIN ? "✅ 头在动" : "❌ 头没动"));
            if (lw.Length > 0)
                _sb.AppendLine("  ★ ⑨ 腿摆角极差（只算驱动）= " + lw.ToString()
                    + "  最差 " + legWorst.ToString("F2") + "°   "
                    + (legWorst >= ANG_MIN ? "✅ 爪子在动" : "❌ 腿被治死了"));
            _sb.AppendLine("  ★ ⑩ 刚性分支（尾+头饰）摆角极差 = " + rigWorst.ToString("F2") + "°   "
                + (rigWorst <= 0.01f ? "✅ 完全刚性" : "❌ 仍有非腿分支在动"));
            _sb.AppendLine("  ★ ⑪ 尾分支末端（相对根节）y 极差 = " + tailPlane.ToString("F3") + " m（" + tailName + "）   "
                + (tailPlane <= PLANE_TOL ? "✅ 尾巴在同一水平线上" : "❌ 尾巴出平面"));

            var mid = new StringBuilder("     逐节段方向角极差  ");
            int moving = 0;
            for (int i = 0; i < NS - 1; i++)
            {
                float a = linkAngMax[i] - linkAngMin[i];
                if (a >= ANG_MIN) moving++;
                if (i % 4 == 0) mid.Append("[" + i.ToString("D2") + "]" + a.ToString("F1") + " ");
            }
            _sb.AppendLine(mid.ToString());
            _sb.AppendLine("     段方向角 ≥" + ANG_MIN + "° 的节数 = " + moving + " / " + (NS - 1));
            _sb.AppendLine();
        }

        Time.captureFramerate = 0;

        _sb.AppendLine("===== 总判据 =====");
        _sb.AppendLine("★ 原地不动：整段录制的位置漂移最大 = " + driftMax.ToString("F4") + " m   "
            + (driftMax <= 0.01f ? "✅ 完全静止" : "❌ 仍在移动"));
        _sb.AppendLine("★ ⑦ 尾端最好成绩 = " + tailAngAll.ToString("F2") + "°   " + (tailAngAll >= ANG_MIN ? "✅" : "❌"));
        _sb.AppendLine("★ ⑧ 头端最好成绩 = " + headAngAll.ToString("F2") + "°   " + (headAngAll >= ANG_MIN ? "✅" : "❌"));
        _sb.AppendLine("★ ⑨ 腿最好成绩 = " + legAngAll.ToString("F2") + "°   " + (legAngAll >= ANG_MIN ? "✅" : "❌"));
        _sb.AppendLine("★ ⑩ 非腿分支全程保持刚性：最差摆角极差 = " + headClusterAll.ToString("F2") + "°   "
            + (headClusterAll <= 0.01f ? "✅" : "❌"));
        _sb.AppendLine("★ ⑪ 尾巴全程在同一水平线上：末端 |Δy| 最大 = " + tailPlaneAll.ToString("F3") + " m   "
            + (tailPlaneAll <= PLANE_TOL ? "✅" : "❌"));
        _sb.AppendLine();
        _sb.AppendLine("★ 5 段对照已按帧号连续录出，帧目录 " + SD + "  帧前缀 " + PAT);
        _sb.AppendLine("    A(side) 0000~0059   B..E(top) 0060~0299");
        _sb.AppendLine();
        _sb.AppendLine("PNG 帧数 = " + _saved);

        if (_cam != null) UnityEngine.Object.Destroy(_cam.gameObject);
        yield return Done();
    }

    Camera MakeCam()
    {
        var g = new GameObject("PROBE_CAM_DF2");
        var c = g.AddComponent<Camera>();
        c.clearFlags = CameraClearFlags.SolidColor;
        c.backgroundColor = new Color(0.86f, 0.88f, 0.91f, 1f);
        c.fieldOfView = 45f;
        c.nearClipPlane = 0.05f;
        c.farClipPlane = 5000f;
        c.enabled = false;
        return c;
    }

    void Snap(int f, string camName)
    {
        int NS = _spine.Count;
        Vector3 center = Vector3.zero; int cnt = 0;
        for (int i = 0; i < NS; i++)
        {
            var t = _spine[i] as Transform;
            if (t != null) { center += t.position; cnt++; }
        }
        if (cnt == 0) return;
        center /= cnt;
        float rad = 1f;
        for (int i = 0; i < NS; i++)
        {
            var t = _spine[i] as Transform;
            if (t == null) continue;
            float d = (t.position - center).magnitude;
            if (d > rad) rad = d;
        }
        // ★ 侧视必须把整条尾巴也框进来（3.24 m），否则"尾巴翘起来"会被裁在画面外
        if (camName == "side")
        {
            for (int k = 0; k < _limbReach.Length; k++)
                if (_limbReach[k] > 2f)
                {
                    var b = _limbRoots[k] as Transform;
                    if (b != null) rad = Mathf.Max(rad, (b.position - center).magnitude + _limbReach[k] + 0.4f);
                }
        }
        float dist = rad / Mathf.Sin(_cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.12f;

        Vector3 fwd = _drgT.forward; fwd.y = 0f;
        if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.forward;
        fwd.Normalize();
        Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized;

        Vector3 dir;
        if (camName == "top")
            dir = (Vector3.up * 0.92f + fwd * 0.30f + right * 0.22f).normalized;
        else if (camName == "side")
            dir = (right * 0.97f + Vector3.up * 0.08f + fwd * 0.02f).normalized;   // 正侧视、近水平
        else
            dir = (-fwd * 0.80f + Vector3.up * 0.52f + right * 0.20f).normalized;

        _cam.transform.position = center + dir * dist;
        _cam.transform.LookAt(center, Vector3.up);

        int W = 960, H = 540;
        var rt = new RenderTexture(W, H, 24);
        _cam.targetTexture = rt;
        _cam.Render();
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
        tex.Apply();
        File.WriteAllBytes(SD + "/" + PAT + f.ToString("D4") + ".png", tex.EncodeToPNG());
        RenderTexture.active = prev;
        UnityEngine.Object.Destroy(tex);
        UnityEngine.Object.Destroy(rt);
        _cam.targetTexture = null;
        _saved++;
    }

    static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

    IEnumerator Done()
    {
        Time.captureFramerate = 0;
        Flush();
        Debug.Log("[drg_form2] 写入 " + RP + "  帧数 " + _saved);
        yield return null;
    }

    void Flush() { try { File.WriteAllText(RP, _sb.ToString()); } catch { } }
}

var __hostDf2 = new GameObject("drg_form2");
__hostDf2.AddComponent<drg_form2>();
return "DRG_FORM2_STARTED";
