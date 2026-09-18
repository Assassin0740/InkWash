using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;

// drg_dive.cs —— 墨龙「螺旋 / 蜿蜒俯冲 + 整身扑咬」验收 + 录像（第二阶段）
//
// 用户定案（问答逐项确认）：
//   · 俯冲 = **螺旋 / 蜿蜒俯冲**（不是直线戳下来）
//   · 咬击 = **整身扑咬**（不是只有脖子点头）
//   · 攻击全程**严格保持 S 形**（原话：「时时刻刻都要遵循这个道理」）
//
// ■ 三条判据，一一对应上面三条定案
//   ① **俯冲段的弦垂距**不能塌。旧实现 Tell/Dive/Strike/Recover 四个相位里
//      **一次都没有调用** `ApplySpineOffsetsRaw` ⇒ 攻击全程身体是直的。
//      合格线取「盘旋段的 0.7 倍」——从被测数据本身推导，不写死期望值。
//   ② **俯冲轨迹的横向偏离**：对「俯冲段首帧→末帧」这条直线，算中间帧的最大横向偏离。
//      直线冲（旧行为）≈ 0；螺旋俯冲应 ≥ 1 m（`diveSpiralAmp` 默认 2.2）。
//      ★ 螺旋项用 `sin(2π·turns·u)`，u=0/1 时恰好归零 ⇒ 拿首末帧当基准是合法的。
//   ③ **咬击时尾部也参与**：逐节旋转极差。旧实现只动头颈那几节，尾部极差 ≈ 0。
//
// ★★ 本探针的关键是**按相位分组**。不分组，就分不清
//    「盘旋时身体弯」和「俯冲时身体还弯不弯」—— 而后者才是这次要验的东西。
//
// ★ 脚本形态：Codely 是 Roslyn script，必须有顶层语句；运行时探针末尾要 new 一个宿主对象。

public class drg_dive : MonoBehaviour
{
    const string RP  = "D:/Unity Project/InkWash/Tools/reports/drg_dive.txt";
    const string SD  = "D:/Unity Project/InkWash/Tools/screenshots/enemies/DRD";
    const string KEY = "墨龙";

    const int FPS   = 30;
    const int NF    = 150;    // 5.0 s
    const int TRIG  = 40;     // 第 40 帧强制触发攻击（前 1.3 s 留作盘旋基线）

    readonly StringBuilder _sb = new StringBuilder();
    Component _sc; Type _ty, _dt;
    MethodInfo _mPlay, _mLabel, _mStop, _mForce, _mEnter;
    PropertyInfo _pCount;
    Camera _cam;
    GameObject _go;
    Transform _actor, _vroot;
    IList _spine, _baseRel;
    FieldInfo _fPhase, _fLift;

    int _saved;

    static readonly BindingFlags BF = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;

    void Start() { StartCoroutine(Run()); }

    struct Row { public int F; public string Phase; public float Bend, BendH, BendV, Lift, Len; public Vector3 P; }

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

        _sb.AppendLine("=== drg_dive：墨龙「螺旋俯冲 + 整身扑咬」验收（第二阶段）===");
        _sb.AppendLine("条目 idx=" + idx + "  label=" + found);
        _sb.AppendLine("captureFramerate = " + FPS + "   frames = " + NF + "   强制攻击帧 = " + TRIG);
        _sb.AppendLine();

        _mStop.Invoke(_sc, null);
        _mPlay.Invoke(_sc, new object[] { idx });
        for (int i = 0; i < 90; i++) yield return null;   // 等它起飞、进入盘旋

        _go = GameObject.Find("Showcase_Actor_" + idx);
        if (_go == null) { _sb.AppendLine("× 没找到 actor 实例 Showcase_Actor_" + idx); yield return Done(); }
        _actor = _go.transform;

        var drg = _go.GetComponent(_dt);
        _vroot   = _dt.GetField("_modelRoot", BF).GetValue(drg) as Transform;
        _spine   = _dt.GetField("_spine", BF).GetValue(drg) as IList;
        _baseRel = _dt.GetField("_baseRel", BF).GetValue(drg) as IList;
        _fPhase  = _dt.GetField("_divePhaseName", BF);
        _fLift   = _dt.GetField("_currentLift", BF);
        _mForce  = _dt.GetMethod("ForceNextAttackForTest", BF);
        _mEnter  = _dt.GetMethod("ForceEnterAttackForTest", BF);

        if (_mForce == null || _mEnter == null)
        { _sb.AppendLine("× 缺少强制攻击接口（ForceNextAttackForTest / ForceEnterAttackForTest）"); yield return Done(); }

        // ── 参数回读：证明 C# 新默认值确实生效（新字段 prefab 里没有同名键 ⇒ 走 C# 默认值）──
        _sb.AppendLine("── 运行时参数回读（第二阶段新字段）──");
        string[] pf = { "diveSpiralAmp", "diveSpiralTurns", "diveSpiralVertAmp", "diveSwimScale",
                        "hoverAmplitudeDeg", "hoverPitchAmplitudeDeg", "hoverPhaseStepDeg", "hoverFrequency",
                        "waveAmpRootGain", "waveAmpHeadGain", "strikeLift", "biteHeadLinks" };
        foreach (var n in pf)
        {
            var f = _dt.GetField(n, BF);
            _sb.AppendLine("   " + n.PadRight(24) + (f == null ? "<无>" : f.GetValue(drg).ToString()));
        }
        _sb.AppendLine();
        _sb.AppendLine("_spine.Count = " + (_spine == null ? -1 : _spine.Count)
                     + "   _baseRel.Count = " + (_baseRel == null ? -1 : _baseRel.Count)
                     + "   _modelRoot = " + (_vroot == null ? "NULL" : _vroot.name));
        _sb.AppendLine();

        _cam = MakeCam();

        int NS = _spine == null ? 0 : _spine.Count;
        if (NS < 3) { _sb.AppendLine("× 脊柱节数 < 3，无法量弦垂距"); yield return Done(); }
        var rows = new List<Row>();
        var perLinkMin = new float[NS];
        var perLinkMax = new float[NS];
        for (int i = 0; i < NS; i++) { perLinkMin[i] = float.MaxValue; perLinkMax[i] = float.MinValue; }

        var liftSeq = new List<float>();     // 全程序列，用来画"高度"变化
        var trig = false;

        Time.captureFramerate = FPS;

        for (int f = 0; f < NF; f++)
        {
            try
            {
                if (!trig && f >= TRIG)
                {
                    trig = true;
                    _mForce.Invoke(drg, new object[] { "Bite" });   // 先定招
                    _mEnter.Invoke(drg, null);                      // 再切状态（顺序不能反）
                }

                var pos = new Vector3[NS];
                Quaternion rootRot = _vroot != null ? _vroot.rotation : _actor.rotation;

                for (int i = 0; i < NS; i++)
                {
                    var t = _spine[i] as Transform;
                    if (t == null) { pos[i] = Vector3.zero; continue; }
                    pos[i] = t.position;
                    var rel = (Quaternion)_baseRel[i];
                    float off = Quaternion.Angle(t.rotation, rootRot * rel);
                    if (off < perLinkMin[i]) perLinkMin[i] = off;
                    if (off > perLinkMax[i]) perLinkMax[i] = off;
                }

                // ── 弦垂距（拆水平/垂直）──
                Vector3 a0 = pos[0], b0 = pos[NS - 1];
                Vector3 ab = b0 - a0;
                float abLen = ab.magnitude;
                float mx = 0f, mxH = 0f, mxV = 0f;
                if (abLen > 1e-4f)
                {
                    Vector3 u = ab / abLen;
                    for (int i = 1; i < NS - 1; i++)
                    {
                        Vector3 p = pos[i] - a0;
                        Vector3 dev = p - Vector3.Dot(p, u) * u;
                        float d = dev.magnitude;
                        if (d > mx) mx = d;
                        float dh = new Vector2(dev.x, dev.z).magnitude;
                        float dv = Mathf.Abs(dev.y);
                        if (dh > mxH) mxH = dh;
                        if (dv > mxV) mxV = dv;
                    }
                }

                string phase = _fPhase != null ? (_fPhase.GetValue(drg) as string) : "?";
                if (string.IsNullOrEmpty(phase)) phase = "?";
                float lift = _fLift != null ? Convert.ToSingle(_fLift.GetValue(drg)) : 0f;

                rows.Add(new Row { F = f, Phase = phase, Bend = mx, BendH = mxH, BendV = mxV,
                                   Lift = lift, Len = abLen, P = _actor.position });
                liftSeq.Add(lift);

                if (f % 15 == 0)
                    _sb.AppendLine("帧 " + f.ToString("D3") + "  phase=" + phase.PadRight(8)
                        + " 弦垂距=" + mx.ToString("F2").PadRight(6)
                        + " 身长=" + abLen.ToString("F2").PadRight(6)
                        + " lift=" + lift.ToString("F2"));

                Snap(f);
            }
            catch (Exception ex) { _sb.AppendLine("  × 帧 " + f + " 异常 " + ex.Message); }

            Flush();
            yield return null;
        }

        Time.captureFramerate = 0;

        // ══════════════ 汇总（按相位分组） ══════════════
        _sb.AppendLine();
        _sb.AppendLine("===== 汇总：按相位分组 =====");

        // ★ 分组要按「真的在攻击中」判定，不能只看相位名：
        //   `_dive` 在攻击结束后**不会回到 None**，会一直停在 Recover
        //   ⇒ 第一版探针把攻击结束后已经回到盘旋的那 50 多帧也算进了「攻击段」。
        //   判据：Tell / Dive / Strike 必定在攻击中；Recover 要看高度有没有升回盘旋（约 7 m）。
        var gAll = new List<Row>(); var gDive = new List<Row>();
        foreach (var r in rows)
        {
            gAll.Add(r);
            bool inAttack = r.Phase == "Tell" || r.Phase == "Dive" || r.Phase == "Strike"
                            || (r.Phase == "Recover" && r.Lift < 6.5f);
            if (inAttack) gDive.Add(r);
        }

        _sb.AppendLine("  [全程]     帧数=" + gAll.Count
            + "  弦垂距 最小=" + MinB(gAll).ToString("F2")
            + "  均值=" + AvgB(gAll).ToString("F2") + " m");
        _sb.AppendLine("  [攻击段]   帧数=" + gDive.Count
            + "  弦垂距 最小=" + MinB(gDive).ToString("F2")
            + "  均值=" + AvgB(gDive).ToString("F2") + " m");

        // ★ 盘旋段 = 攻击段之外的全部帧。
        //   第一版探针按 `Phase == "None"` 找，但 `_divePhaseName` 在非攻击态其实是 **"-"**
        //   ⇒ hoverRows 恒为空 ⇒ 判据① 的比值恒为 0.00（假阴性，差点误判成"没改好"）。
        var hoverRows = new List<Row>();
        foreach (var r in rows) if (!gDive.Contains(r)) hoverRows.Add(r);
        if (hoverRows.Count > 0)
            _sb.AppendLine("  [盘旋段]   帧数=" + hoverRows.Count
                + "  弦垂距 最小=" + MinB(hoverRows).ToString("F2")
                + "  均值=" + AvgB(hoverRows).ToString("F2") + " m  ← 基线");

        // ── 判据 ①：攻击段弦垂距 / 盘旋段弦垂距 ──
        float baseBend = hoverRows.Count > 0 ? AvgB(hoverRows) : 0f;
        float diveBend = gDive.Count > 0 ? AvgB(gDive) : 0f;
        float ratio = baseBend > 1e-4f ? diveBend / baseBend : 0f;
        _sb.AppendLine();
        _sb.AppendLine("★ 判据① 攻击段 / 盘旋段 弦垂距比 = " + ratio.ToString("F2")
            + "   （合格线 ≥0.70；旧实现攻击段身体是直的 ⇒ 比值会很低）");
        _sb.AppendLine("       攻击段弦垂距最小值 = " + MinB(gDive).ToString("F2") + " m（不得≈0）");

        // ── 判据 ②：俯冲轨迹的横向偏离 ──
        var diveOnly = new List<Row>();
        foreach (var r in rows) if (r.Phase == "Dive") diveOnly.Add(r);
        if (diveOnly.Count >= 5)
        {
            Vector3 s = diveOnly[0].P, e = diveOnly[diveOnly.Count - 1].P;
            Vector3 d = e - s; d.y = 0f;
            float lat = 0f; float vRange = 0f;
            float lmin = float.MaxValue, lmax = float.MinValue;
            if (d.sqrMagnitude > 0.01f)
            {
                Vector3 dn = d.normalized;
                for (int i = 1; i < diveOnly.Count - 1; i++)
                {
                    Vector3 q = diveOnly[i].P; q.y = 0f;
                    Vector3 rel = q - s;
                    Vector3 dev = rel - Vector3.Dot(rel, dn) * dn;
                    float m = dev.magnitude;
                    if (m > lat) lat = m;
                }
            }
            for (int i = 0; i < diveOnly.Count; i++)
            {
                if (diveOnly[i].Lift < lmin) lmin = diveOnly[i].Lift;
                if (diveOnly[i].Lift > lmax) lmax = diveOnly[i].Lift;
            }
            vRange = lmax - lmin;
            _sb.AppendLine();
            _sb.AppendLine("★ 判据② 俯冲段轨迹横向偏离直线 最大 = " + lat.ToString("F2")
                + " m   （合格线 ≥1.0 m；旧实现是直线 ⇒ ≈0）");
            _sb.AppendLine("       俯冲段水平行程 = " + d.magnitude.ToString("F2")
                + " m   竖直落差(lift) = " + vRange.ToString("F2") + " m");
            _sb.AppendLine("       俯冲段帧数 = " + diveOnly.Count + "  ≈ "
                + (diveOnly.Count / (float)FPS).ToString("F2") + " s");
        }
        else
        {
            _sb.AppendLine();
            _sb.AppendLine("⚠ 判据② 跳过了：没抓到 Dive 相位（帧数 " + diveOnly.Count + "）");
        }

        // ── 判据 ③：逐节极差（看尾部有没有参与"整身扑咬"）──
        _sb.AppendLine();
        _sb.AppendLine("===== 判据③ 逐节旋转极差（度）=====");
        int moving = 0;
        for (int i = 0; i < NS; i++)
        {
            float rng = perLinkMax[i] - perLinkMin[i];
            if (rng > 5f) moving++;
            if (i % 2 == 0 || i == NS - 1)
            {
                var t = _spine[i] as Transform;
                _sb.AppendLine("  [" + i.ToString("D2") + "] " + (t == null ? "NULL" : t.name).PadRight(14)
                    + " 极差=" + rng.ToString("F2").PadRight(8) + (rng > 5f ? "✓" : "⚠"));
            }
        }
        _sb.AppendLine("  摆动显著的节数 = " + moving + " / " + NS);
        _sb.AppendLine("  ★ 尾部侧（i 小）极差只要不是 0，就说明「整身扑咬」真的让尾巴也动了");

        _sb.AppendLine();
        _sb.AppendLine("PNG 帧数 = " + _saved + "  → " + SD);
        _sb.AppendLine("高度(lift) 序列（每 5 帧取一个）:");
        var line = new StringBuilder("   ");
        for (int i = 0; i < liftSeq.Count; i += 5) line.Append(liftSeq[i].ToString("F1") + " ");
        _sb.AppendLine(line.ToString());

        if (_cam != null) UnityEngine.Object.Destroy(_cam.gameObject);
        yield return Done();
    }

    static float AvgB(List<Row> v) { if (v.Count == 0) return 0f; float s = 0f; for (int i = 0; i < v.Count; i++) s += v[i].Bend; return s / v.Count; }
    static float MinB(List<Row> v) { float m = float.MaxValue; for (int i = 0; i < v.Count; i++) if (v[i].Bend < m) m = v[i].Bend; return v.Count == 0 ? 0f : m; }

    Camera MakeCam()
    {
        var g = new GameObject("PROBE_CAM_DRD");
        var c = g.AddComponent<Camera>();
        c.clearFlags = CameraClearFlags.SolidColor;
        c.backgroundColor = new Color(0.86f, 0.88f, 0.91f, 1f);
        c.fieldOfView = 45f;
        c.nearClipPlane = 0.05f;
        c.farClipPlane = 5000f;
        c.enabled = false;
        return c;
    }

    float _camY = float.NaN;

    void Snap(int f)
    {
        var rds = _go.GetComponentsInChildren<Renderer>(true);
        if (rds.Length == 0) return;
        var b = rds[0].bounds;
        for (int i = 1; i < rds.Length; i++) b.Encapsulate(rds[i].bounds);

        // ★ 相机 Y 锁定在**第一帧**的高度 ⇒ 俯冲下降在画面里看得出来。
        //   若相机跟着龙一起降，"逐渐下降"会被抵消掉，等于什么都没验。
        if (float.IsNaN(_camY)) _camY = b.center.y;
        Vector3 center = new Vector3(b.center.x, _camY, b.center.z);

        float rad = Mathf.Max(b.extents.magnitude, 1f);
        float dist = rad / Mathf.Sin(_cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.35f;

        // 取景方向固定在世界空间（不锁龙前方）⇒ 能看清身体走向，不被自转带着转。
        // ★ 仰角压低到 0.20：仰角高会把"垂直起伏"投影压扁。
        Vector3 dir = new Vector3(0.62f, 0.20f, -0.76f).normalized;
        _cam.transform.position = center + dir * dist;
        _cam.transform.LookAt(center);

        int W = 960, H = 540;
        var rt = new RenderTexture(W, H, 24);
        _cam.targetTexture = rt;
        _cam.Render();
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
        tex.Apply();
        File.WriteAllBytes(SD + "/drd_" + f.ToString("D4") + ".png", tex.EncodeToPNG());
        RenderTexture.active = prev;
        UnityEngine.Object.Destroy(tex);
        UnityEngine.Object.Destroy(rt);
        _cam.targetTexture = null;
        _saved++;
    }

    IEnumerator Done()
    {
        Time.captureFramerate = 0;
        Flush();
        Debug.Log("[drg_dive] 写入 " + RP + "  帧数 " + _saved);
        yield return null;
    }

    void Flush() { try { File.WriteAllText(RP, _sb.ToString()); } catch { } }
}

var __hostDrd = new GameObject("drg_dive");
__hostDrd.AddComponent<drg_dive>();
return "DRG_DIVE_STARTED";
