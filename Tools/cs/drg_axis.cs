using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;

// drg_axis.cs —— 墨龙「头歪 / 尾巴出平面」专项诊断 + 验收（只读，不改任何参数）
//
// 用户（第十七轮）：
//   「尾巴要在同一个水平线上，不能单独摆动，龙头没有摆正，现在歪着头，跟落枕了一样」
//
// ■ 改前（drg_axis_before.txt）已定案：**单一成因**。
//   探针证明脊柱本身完全水平（段方向 pitch 恒 0.00°、链高差 ySpread 恒 0.000 m、
//   复形旋转轴偏离容器 up 0.00° = 纯 yaw）⇒「歪」不是波形算错。
//   真因是 `ApplyLimbMotion` 把 19 条几何分支**全部**当腿驱动，其中：
//     · `drgon_0206` 父=脊柱[0] 子树 28 骨 / 伸展 3.24 m / 首子方向 (0.02,0.11,−0.99)
//       ⇒ 沿身体轴的 3.24 m 长链 = **整条尾巴**，绕容器 right 摆 ±25° ⇒ 尾尖上下走 ±1.37 m
//     · `drgon_054 … drgon_0138` 共 14 条，父=脊柱[22] = **头饰簇**（鬃/须/角/颌）⇒ 头顶炸毛
//
// ■ 本版探针要判的四条（每条都直接对应一句用户原话）：
//   ① 尾巴在同一水平线上：尾分支**末端相对其根节的 y 极差** ≤ 0.05 m
//   ② 尾巴不单独摆动：尾分支的摆角极差 = 0°（刚性）
//   ③ 头不再"落枕"：头饰簇 14 条的摆角极差全 = 0°（刚性，只随头走）
//   ④ 爪子照旧在动：被判定为腿的分支摆角极差仍 ≥ 20°（不能为了治歪把腿也治死）
//   ＋ 回归：脊柱 pitch / ySpread / 复形轴偏离 不变差
//
// ★ 四条纪律（沿用本项目坑表）：
//   1. 取景用**骨骼链实际位置**算包围盒（`SkinnedMeshRenderer.bounds` 是绑定姿势盒，不更新）。
//   2. 参考系统一取 `drg.transform`（驱动的 `Flat(transform.forward)` 同源），不用 `_modelRoot`。
//   3. 不写死期望值，断言从被测配置推导。
//   4. Codely 是 Roslyn script，必须有顶层语句。

public class drg_axis : MonoBehaviour
{
    const string RP = "D:/Unity Project/InkWash/Tools/reports/drg_axis.txt";
    const string SD = "D:/Unity Project/InkWash/Tools/screenshots/enemies/AXS";
    const string KEY = "墨龙";

    const int FPS = 30;
    const int NFR = 60;           // phase 走 0.45 Hz × 2 s = 0.9 周期
    const float PITCH_BAD = 3f;   // 段方向偏离水平面多少度算"出平面"
    const float PLANE_TOL = 0.05f;// 分支末端相对根节的 y 极差容差（m）

    readonly StringBuilder _sb = new StringBuilder();
    Component _sc; Type _ty, _dt;
    MethodInfo _mPlay, _mLabel, _mStop;
    PropertyInfo _pCount;
    Camera _cam;
    GameObject _go;
    Transform _actor, _vroot, _drgT;
    IList _spine, _limbRoots, _limbBaseRel, _baseRel, _baseSegLocal, _segLen, _limbDriven, _limbAxisDot;
    FieldInfo _fWave, _fAmp, _fStat, _fStraight, _fLimbDeg, _fYaw, _fDrivenCnt, _fTailDot, _fHeadEx;

    static readonly BindingFlags BF = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;

    int _saved;

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

        _sb.AppendLine("=== drg_axis：墨龙「头歪 / 尾巴出平面」专项诊断 + 验收 ===");
        _sb.AppendLine("条目 idx=" + idx + "  label=" + found);
        _sb.AppendLine("观察 " + NFR + " 帧（" + (NFR / (float)FPS).ToString("F2") + " s，约 0.9 个波周期）");
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
        _baseRel = _dt.GetField("_baseRel", BF).GetValue(drg) as IList;
        _baseSegLocal = _dt.GetField("_baseSegLocal", BF).GetValue(drg) as IList;
        _segLen = _dt.GetField("_segLen", BF).GetValue(drg) as IList;
        _limbRoots = _dt.GetField("_limbRoots", BF).GetValue(drg) as IList;
        _limbBaseRel = _dt.GetField("_limbBaseRel", BF).GetValue(drg) as IList;
        _limbDriven = _dt.GetField("_limbDriven", BF).GetValue(drg) as IList;
        _limbAxisDot = _dt.GetField("_limbAxisDot", BF).GetValue(drg) as IList;
        _fDrivenCnt = _dt.GetField("_limbDrivenCount", BF);
        _fWave = _dt.GetField("swimWaveCount", BF);
        _fAmp = _dt.GetField("swimWaveAmp", BF);
        _fStat = _dt.GetField("hoverStationary", BF);
        _fStraight = _dt.GetField("straightenSpine", BF);
        _fLimbDeg = _dt.GetField("limbSwingDeg", BF);
        _fYaw = _dt.GetField("hoverStationaryYawDeg", BF);
        _fTailDot = _dt.GetField("limbTailAxisDot", BF);
        _fHeadEx = _dt.GetField("limbHeadExcludeLinks", BF);

        int NS = _spine == null ? 0 : _spine.Count;
        int NL = _limbRoots == null ? 0 : _limbRoots.Count;
        if (NS < 3) { _sb.AppendLine("× 脊柱节数 < 3"); yield return Done(); }

        if (_fStat != null) _fStat.SetValue(drg, true);
        if (_fWave != null) _fWave.SetValue(drg, 1.0f);
        for (int i = 0; i < 6; i++) yield return null;

        _sb.AppendLine("── 运行时参数回读 ──");
        _sb.AppendLine("  hoverStationary       = " + _fStat.GetValue(drg) + "（本探针强制 true）");
        _sb.AppendLine("  straightenSpine       = " + _fStraight.GetValue(drg));
        _sb.AppendLine("  swimWaveAmp           = " + _fAmp.GetValue(drg));
        _sb.AppendLine("  swimWaveCount         = " + _fWave.GetValue(drg) + "（本探针强制 1.0）");
        _sb.AppendLine("  limbSwingDeg          = " + _fLimbDeg.GetValue(drg));
        _sb.AppendLine("  limbTailAxisDot       = " + (_fTailDot == null ? "（字段不存在！未编译？）" : _fTailDot.GetValue(drg).ToString()));
        _sb.AppendLine("  limbHeadExcludeLinks  = " + (_fHeadEx == null ? "（字段不存在！未编译？）" : _fHeadEx.GetValue(drg).ToString()));
        _sb.AppendLine("  hoverStationaryYawDeg = " + _fYaw.GetValue(drg));
        _sb.AppendLine();

        _sb.AppendLine("── 容器朝向 ──");
        _sb.AppendLine("  _drgT（驱动参考系, 世界）    euler = " + F(_drgT.eulerAngles));
        _sb.AppendLine("  _modelRoot（Visual, 世界）   euler = " + F(_vroot.eulerAngles));
        _sb.AppendLine("  _modelRoot（Visual, 相对父） euler = " + F(_vroot.localEulerAngles));
        _sb.AppendLine("  _modelRoot.forward 仰角 = "
            + Vector3.Angle(_vroot.forward, Vector3.ProjectOnPlane(_vroot.forward, Vector3.up)).ToString("F2") + "°（0 = 水平）");
        _sb.AppendLine();

        Quaternion rootRot = _vroot.rotation;
        Quaternion invRoot = Quaternion.Inverse(rootRot);

        // ── 分支分类表 ──
        _sb.AppendLine("── `_limbRoots` 分支分类（哪些真是腿）──");
        var spineIdx = new Dictionary<Transform, int>();
        for (int i = 0; i < NS; i++) { var t = _spine[i] as Transform; if (t != null && !spineIdx.ContainsKey(t)) spineIdx[t] = i; }

        var limbParentSpine = new int[NL];
        var limbSubtree = new int[NL];
        var limbReach = new float[NL];
        var limbDir = new Vector3[NL];
        var limbTip = new Transform[NL];
        for (int k = 0; k < NL; k++)
        {
            var b = _limbRoots[k] as Transform;
            limbParentSpine[k] = -1;
            if (b == null) continue;
            if (b.parent != null && spineIdx.ContainsKey(b.parent)) limbParentSpine[k] = spineIdx[b.parent];

            int cnt = 0; float reach = 0f; Transform tip = b; float bestD = -1f;
            var stack = new Stack<Transform>(); stack.Push(b);
            while (stack.Count > 0)
            {
                var t = stack.Pop(); cnt++;
                float d = (t.position - b.position).magnitude;
                if (d > reach) reach = d;
                if (d > bestD) { bestD = d; tip = t; }
                for (int c = 0; c < t.childCount; c++) stack.Push(t.GetChild(c));
            }
            limbSubtree[k] = cnt; limbReach[k] = reach; limbTip[k] = tip;

            Vector3 dir = Vector3.zero;
            if (b.childCount > 0) dir = (invRoot * (b.GetChild(0).position - b.position)).normalized;
            limbDir[k] = dir;
        }

        int nDriven = 0, nTailLike = 0, nHeadLike = 0;
        for (int k = 0; k < NL; k++)
        {
            var b = _limbRoots[k] as Transform;
            if (b == null) { _sb.AppendLine("  [" + k + "] NULL"); continue; }
            bool driven = _limbDriven != null && k < _limbDriven.Count && (bool)_limbDriven[k];
            float ad = _limbAxisDot != null && k < _limbAxisDot.Count ? (float)_limbAxisDot[k] : -1f;
            string pos = limbParentSpine[k] < 0 ? "不在脊柱上" :
                ("脊柱[" + limbParentSpine[k] + "]/" + (NS - 1));
            string why = driven ? "腿 ⇒ 划水"
                : (ad >= 0.8f ? "沿身体轴 ⇒ 尾巴，刚性" : "头端 ⇒ 头饰簇，刚性");
            if (driven) nDriven++;
            else if (ad >= 0.8f) nTailLike++;
            else nHeadLike++;

            _sb.AppendLine("  [" + (k < 10 ? " " : "") + k + "] " + (driven ? "★驱动" : "·刚性") + " " + b.name
                + "  父=" + (b.parent != null ? b.parent.name : "?") + " " + pos
                + "  子树 " + limbSubtree[k] + " 骨 / 伸展 " + limbReach[k].ToString("F2") + " m"
                + "  首子方向=" + F3(limbDir[k]) + " |z|=" + ad.ToString("F3")
                + "  " + why);
        }
        _sb.AppendLine("  ★ 驱动（腿）= " + nDriven + "   尾巴型 = " + nTailLike + "   头饰型 = " + nHeadLike
            + "   共 " + NL);
        if (_fDrivenCnt != null) _sb.AppendLine("  ★ 脚本内 `_limbDrivenCount` = " + _fDrivenCnt.GetValue(drg)
            + "（应与上面「驱动」条数一致）");
        _sb.AppendLine();

        // ── 逐帧量 ──
        _sb.AppendLine("── 逐帧量 ──");
        var pitchMin = new float[NS]; var pitchMax = new float[NS];
        for (int i = 0; i < NS; i++) { pitchMin[i] = float.MaxValue; pitchMax[i] = float.MinValue; }

        var swMin = new float[NL]; var swMax = new float[NL];
        var tyMin = new float[NL]; var tyMax = new float[NL];   // 末端相对根节的 y（去掉整体平移）
        for (int k = 0; k < NL; k++) { swMin[k] = float.MaxValue; swMax[k] = float.MinValue; tyMin[k] = float.MaxValue; tyMax[k] = float.MinValue; }

        float axisTiltMax = 0f; int axisTiltIdx = -1;
        float ySpreadMax = 0f, pitchAbsMax = 0f; int pitchIdx = -1;

        for (int f = 0; f < NFR; f++)
        {
            var pos = new Vector3[NS];
            for (int i = 0; i < NS; i++) { var t = _spine[i] as Transform; pos[i] = t != null ? t.position : Vector3.zero; }

            for (int i = 0; i < NS - 1; i++)
            {
                Vector3 d = pos[i + 1] - pos[i];
                if (d.sqrMagnitude < 1e-10f) continue;
                d.Normalize();
                float pitch = Mathf.Asin(Mathf.Clamp(-d.y, -1f, 1f)) * Mathf.Rad2Deg;
                if (pitch < pitchMin[i]) pitchMin[i] = pitch;
                if (pitch > pitchMax[i]) pitchMax[i] = pitch;
                float ap = Mathf.Abs(pitch);
                if (ap > pitchAbsMax) { pitchAbsMax = ap; pitchIdx = i; }
            }

            float yMin = float.MaxValue, yMax = float.MinValue;
            for (int i = 0; i < NS; i++) { if (pos[i].y < yMin) yMin = pos[i].y; if (pos[i].y > yMax) yMax = pos[i].y; }
            float ys = yMax - yMin;
            if (ys > ySpreadMax) ySpreadMax = ys;

            for (int i = 0; i < NS; i++)
            {
                var t = _spine[i] as Transform;
                if (t == null) continue;
                Quaternion B = i < _baseRel.Count ? (Quaternion)_baseRel[i] : Quaternion.identity;
                Quaternion Q = invRoot * t.rotation;
                Quaternion D = Q * Quaternion.Inverse(B);
                if (Quaternion.Angle(Quaternion.identity, D) > 1e-3f)
                {
                    Vector3 ax; float ang; D.ToAngleAxis(out ang, out ax);
                    float tilt = Vector3.Angle(ax, Vector3.up);
                    tilt = Mathf.Min(tilt, 180f - tilt);
                    if (tilt > axisTiltMax) { axisTiltMax = tilt; axisTiltIdx = i; }
                }
            }

            for (int k = 0; k < NL; k++)
            {
                var b = _limbRoots[k] as Transform;
                if (b == null) continue;
                if (k < _limbBaseRel.Count && _limbBaseRel[k] != null)
                {
                    float a = Quaternion.Angle((Quaternion)_limbBaseRel[k], b.localRotation);
                    if (a < swMin[k]) swMin[k] = a;
                    if (a > swMax[k]) swMax[k] = a;
                }
                var tip = limbTip[k];
                if (tip != null) { float ry = tip.position.y - b.position.y; if (ry < tyMin[k]) tyMin[k] = ry; if (ry > tyMax[k]) tyMax[k] = ry; }
            }

            if (f == NFR / 2) { Snap("top"); Snap("side"); Snap("head"); Snap("tail"); }
            Flush();
            yield return null;
        }

        _sb.AppendLine("  逐节 pitch 极差（度）：");
        for (int i = 0; i < NS - 1; i += 2)
            _sb.AppendLine("    [" + i.ToString("D2") + "] " + pitchMin[i].ToString("F1") + " ~ " + pitchMax[i].ToString("F1"));
        _sb.AppendLine();
        _sb.AppendLine("  ★ 脊柱段方向最大 |pitch| = " + pitchAbsMax.ToString("F2") + "°（第 " + pitchIdx + " 节）   "
            + (pitchAbsMax <= PITCH_BAD ? "✅ 脊柱在同一水平面内" : "❌ 脊柱出平面"));
        _sb.AppendLine("  ★ 脊柱链高差 ySpread 最大 = " + ySpreadMax.ToString("F3") + " m（纯水平游动应 ≈ 0）");
        _sb.AppendLine("  ★ 复形旋转轴最大偏离容器 up = " + axisTiltMax.ToString("F2") + "°   "
            + (axisTiltMax <= 2f ? "✅ 纯 yaw" : "❌ 混入 pitch/roll"));
        _sb.AppendLine();

        // ── 分支逐条：摆角极差 + 末端离面 ──
        _sb.AppendLine("── 分支逐条：摆角极差 / 末端（相对根节）y 极差 ──");
        float tailPlane = -1f, tailSwing = -1f, legsWorstSwing = float.MaxValue, headClusterSwing = 0f;
        for (int k = 0; k < NL; k++)
        {
            var b = _limbRoots[k] as Transform;
            if (b == null) continue;
            bool driven = _limbDriven != null && k < _limbDriven.Count && (bool)_limbDriven[k];
            float ad = _limbAxisDot != null && k < _limbAxisDot.Count ? (float)_limbAxisDot[k] : -1f;
            float sw = swMax[k] - swMin[k];
            float ty = tyMax[k] - tyMin[k];

            _sb.AppendLine("  [" + (k < 10 ? " " : "") + k + "] " + (driven ? "★驱动" : "·刚性") + " " + b.name
                + "  摆角极差 " + sw.ToString("F2") + "°"
                + "  末端 |Δy| = " + ty.ToString("F3") + " m"
                + "  伸展 " + limbReach[k].ToString("F2") + " m");

            if (!driven && ad >= 0.8f) { if (tailPlane < 0f || ty > tailPlane) tailPlane = ty; if (sw > tailSwing) tailSwing = sw; }
            if (!driven && ad < 0.8f) { if (sw > headClusterSwing) headClusterSwing = sw; }
            if (driven && sw < legsWorstSwing) legsWorstSwing = sw;
        }
        _sb.AppendLine();

        _sb.AppendLine("===== 逐条判据 =====");
        _sb.AppendLine("  ① 尾巴在同一水平线上：尾分支末端 |Δy| 最大 = " + tailPlane.ToString("F3") + " m   "
            + (tailPlane >= 0f && tailPlane <= PLANE_TOL ? "✅" : "❌（> " + PLANE_TOL + " m）"));
        _sb.AppendLine("  ② 尾巴不单独摆动：尾分支摆角极差 = " + tailSwing.ToString("F2") + "°   "
            + (tailSwing >= 0f && tailSwing <= 0.01f ? "✅ 刚性" : "❌ 仍在动"));
        _sb.AppendLine("  ③ 头不再落枕：头饰簇摆角极差 = " + headClusterSwing.ToString("F2") + "°   "
            + (headClusterSwing <= 0.01f ? "✅ 刚性（只随头走）" : "❌ 仍在动"));
        _sb.AppendLine("  ④ 爪子照旧在动：腿摆角极差 最差 = " + (legsWorstSwing == float.MaxValue ? "无" : legsWorstSwing.ToString("F2") + "°") + "   "
            + (legsWorstSwing >= 20f ? "✅" : "❌ 腿被治死了"));
        _sb.AppendLine("  回归：脊柱 pitch " + pitchAbsMax.ToString("F2") + "° / ySpread " + ySpreadMax.ToString("F3")
            + " m / 复形轴偏 " + axisTiltMax.ToString("F2") + "°");
        _sb.AppendLine();
        _sb.AppendLine("★ 四张图（同一 tick）：AXS/axs_top.png / axs_side.png / axs_head.png / axs_tail.png");
        _sb.AppendLine("   axs_side 是**正侧视** ⇒ 尾巴在不在同一水平线一眼可见；axs_head 看头正不正");
        _sb.AppendLine();
        _sb.AppendLine("PNG 帧数 = " + _saved);

        if (_cam != null) UnityEngine.Object.Destroy(_cam.gameObject);
        yield return Done();
    }

    static string F(Vector3 v) { return "(" + v.x.ToString("F2") + ", " + v.y.ToString("F2") + ", " + v.z.ToString("F2") + ")"; }
    static string F3(Vector3 v) { return "(" + v.x.ToString("F3") + ", " + v.y.ToString("F3") + ", " + v.z.ToString("F3") + ")"; }

    void Snap(string view)
    {
        if (_cam == null) _cam = MakeCam();
        int NS = _spine.Count;
        var pos = new Vector3[NS];
        for (int i = 0; i < NS; i++) { var t = _spine[i] as Transform; pos[i] = t != null ? t.position : Vector3.zero; }

        Vector3 fwd = Flat(_drgT.forward); fwd.Normalize();
        Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized;

        Vector3 center; float rad; Vector3 dir;
        if (view == "top")
        {
            center = Vector3.zero; for (int i = 0; i < NS; i++) center += pos[i]; center /= NS;
            rad = 0f; for (int i = 0; i < NS; i++) rad = Mathf.Max(rad, (pos[i] - center).magnitude);
            dir = (Vector3.up * 0.92f + fwd * 0.30f + right * 0.22f).normalized;
        }
        else if (view == "side")
        {
            center = Vector3.zero; for (int i = 0; i < NS; i++) center += pos[i]; center /= NS;
            rad = 0f; for (int i = 0; i < NS; i++) rad = Mathf.Max(rad, (pos[i] - center).magnitude);
            // 把尾巴也算进取景范围，否则"尾巴翘起来"可能被裁掉
            var tailRoot = FindTailLike();
            if (tailRoot != null) rad = Mathf.Max(rad, (tailRoot.position - center).magnitude + 3.4f);
            dir = (right * 0.97f + Vector3.up * 0.06f + fwd * 0.02f).normalized;
        }
        else if (view == "head")
        {
            center = Vector3.zero; int c = 0;
            for (int i = NS - 5; i < NS; i++) { center += pos[i]; c++; }
            center /= c;
            rad = 1.6f;
            dir = (fwd * 0.55f + Vector3.up * 0.42f + right * 0.72f).normalized;
        }
        else
        {
            center = Vector3.zero; int c = 0;
            for (int i = 0; i < 5 && i < NS; i++) { center += pos[i]; c++; }
            center /= c;
            rad = 1.8f;
            dir = (-fwd * 0.55f + Vector3.up * 0.45f + right * 0.72f).normalized;
        }

        float dist = rad / Mathf.Sin(_cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.12f;
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
        File.WriteAllBytes(SD + "/axs_" + view + ".png", tex.EncodeToPNG());
        RenderTexture.active = prev;
        UnityEngine.Object.Destroy(tex);
        UnityEngine.Object.Destroy(rt);
        _cam.targetTexture = null;
        _saved++;
    }

    Transform FindTailLike()
    {
        if (_limbRoots == null || _limbAxisDot == null) return null;
        for (int k = 0; k < _limbRoots.Count && k < _limbAxisDot.Count; k++)
            if ((float)_limbAxisDot[k] >= 0.8f) return _limbRoots[k] as Transform;
        return null;
    }

    Camera MakeCam()
    {
        var g = new GameObject("PROBE_CAM_AXS");
        var c = g.AddComponent<Camera>();
        c.clearFlags = CameraClearFlags.SolidColor;
        c.backgroundColor = new Color(0.86f, 0.88f, 0.91f, 1f);
        c.fieldOfView = 45f;
        c.nearClipPlane = 0.05f;
        c.farClipPlane = 5000f;
        c.enabled = false;
        return c;
    }

    static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

    IEnumerator Done()
    {
        Time.captureFramerate = 0;
        Flush();
        Debug.Log("[drg_axis] 写入 " + RP + "  帧数 " + _saved);
        yield return null;
    }

    void Flush() { try { File.WriteAllText(RP, _sb.ToString()); } catch { } }
}

var __hostAxs = new GameObject("drg_axis");
__hostAxs.AddComponent<drg_axis>();
return "DRG_AXIS_STARTED";
