using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;

// drg_form.cs —— 墨龙「原地蛇形形态」专项验收 + 录像
//
// 用户指令（本轮）：
//   「你龙原地不动去做动作，先把这个动作做好了再动，现在本身的动作都不对呢」
//   「现在这个头和后面的爪子，还有尾巴都没有动呢」
//
// ■ 本探针只做一件事：**把身体波形从移动里摘出来**，看清它到底是什么形状。
//   移动版（drg_serp）里龙以 8 m/s 巡游、相机跟拍，位移和波形糊在一起 ——
//   用户说"动作不对"，但从那段视频里没法定位是哪一维不对。
//
// ■ 原地由 EnemyDragon.hoverStationary 保证：不前进、不转向、不改朝向、
//   高度恒定、并忽略静默段的幅度缩放。本探针同时**验证它真的没动**。
//
// ■★ 本轮新增的三条判据，直接对应用户说的"头 / 爪 / 尾没动"：
//   ⑦ **尾端段方向角极差**（第 0 节）≥ 5°
//   ⑧ **头端段方向角极差**（末节）≥ 5°
//   ⑨ **四肢摆角极差**（每个 `_limbRoots[k]` 相对基准的最大偏离）≥ 5°
//   旧包络 `sin(πu)^n` 在两端归零 ⇒ 端点切向 = 纯轴向 ⇒ ⑦⑧ 恒为 0（实测）；
//   四肢旧写法绕**骨骼自己的局部 right** ⇒ 部分腿退化成"自转"，⑨ 读不出来。
//
// ■ 段定义：波数扫描（0.75 / 1.0 / 1.25 / 1.5）+ 一段跟车视角，让用户直接挑。
//   每段 72 帧（2.4 s；swimWaveFreq 0.45 Hz ⇒ 1.08 个波周期）。
//
// ★ 取景必须用**骨骼链的实际位置**算包围盒：
//   `Renderer.bounds` 对 SkinnedMeshRenderer 是**绑定姿势**的包围盒，不随骨骼更新
//   ⇒ 上一版相机取景偏掉，画面里只有半截龙（实测）。
// ★ 脚本形态：Codely 是 Roslyn script，必须有顶层语句；运行时探针末尾要 new 宿主对象。

public class drg_form : MonoBehaviour
{
    const string RP  = "D:/Unity Project/InkWash/Tools/reports/drg_form.txt";
    const string SD  = "D:/Unity Project/InkWash/Tools/screenshots/enemies/DFM";
    const string KEY = "墨龙";

    const int   FPS = 30;
    const int   NF  = 72;        // 每段帧数
    const float ZT  = 0.06f;     // 过零阈值（m）
    const float ANG_MIN = 5f;    // "动起来了"的角度下限（度）

    static readonly string[] TAG = {
        "A 俯视 · 波数 0.75（一个弓）",
        "B 俯视 · 波数 1.00（一个完整 S）",
        "C 俯视 · 波数 1.25（一个 S + 1/4 波）",
        "D 俯视 · 波数 1.50（一个半波）",
        "E 跟车视角 · 波数 1.00（游戏实际观感）",
    };
    static readonly string[] CAM = { "top", "top", "top", "top", "back" };
    static readonly float[]  WAV = { 0.75f, 1.0f, 1.25f, 1.5f, 1.0f };

    readonly StringBuilder _sb = new StringBuilder();
    Component _sc; Type _ty, _dt;
    MethodInfo _mPlay, _mLabel, _mStop;
    PropertyInfo _pCount;
    Camera _cam;
    GameObject _go;
    Transform _actor, _vroot, _drgT;
    IList _spine, _limbRoots, _limbBaseRel;
    FieldInfo _fWave, _fAmp, _fFreq, _fStat, _fLimbDeg;

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

        _sb.AppendLine("=== drg_form：墨龙「原地蛇形形态」专项 ===");
        _sb.AppendLine("条目 idx=" + idx + "  label=" + found);
        _sb.AppendLine("captureFramerate=" + FPS + "  每段 " + NF + " 帧 ("
            + (NF / (float)FPS).ToString("F1") + " s)  共 " + TAG.Length + " 段 = "
            + (TAG.Length * NF / (float)FPS).ToString("F1") + " s");
        _sb.AppendLine();

        _mStop.Invoke(_sc, null);
        _mPlay.Invoke(_sc, new object[] { idx });
        for (int i = 0; i < 90; i++) yield return null;   // 等它起飞、进入盘旋

        _go = GameObject.Find("Showcase_Actor_" + idx);
        if (_go == null) { _sb.AppendLine("× 没找到 actor 实例 Showcase_Actor_" + idx); yield return Done(); }
        _actor = _go.transform;

        var drg = _go.GetComponent(_dt);
        _drgT      = drg.transform;      // 与驱动同源的参考系
        _vroot     = _dt.GetField("_modelRoot", BF).GetValue(drg) as Transform;
        _spine     = _dt.GetField("_spine", BF).GetValue(drg) as IList;
        _limbRoots = _dt.GetField("_limbRoots", BF).GetValue(drg) as IList;
        _limbBaseRel = _dt.GetField("_limbBaseRel", BF).GetValue(drg) as IList;
        _fWave    = _dt.GetField("swimWaveCount", BF);
        _fAmp     = _dt.GetField("swimWaveAmp", BF);
        _fFreq    = _dt.GetField("swimWaveFreq", BF);
        _fStat    = _dt.GetField("hoverStationary", BF);
        _fLimbDeg = _dt.GetField("limbSwingDeg", BF);

        int NS = _spine == null ? 0 : _spine.Count;
        int NL = _limbRoots == null ? 0 : _limbRoots.Count;
        if (NS < 3) { _sb.AppendLine("× 脊柱节数 < 3"); yield return Done(); }

        float baseFold = 0f;
        for (int i = 0; i + 1 < NS; i++)
        {
            var a = _spine[i] as Transform; var b = _spine[i + 1] as Transform;
            if (a != null && b != null) baseFold += Vector3.Distance(a.position, b.position);
        }

        _sb.AppendLine("── 运行时参数回读 ──");
        _sb.AppendLine("  hoverStationary    = " + _fStat.GetValue(drg)
            + "   ← 必须 True，否则本轮整段录像都在移动，白拍");
        _sb.AppendLine("  swimWaveAmp        = " + _fAmp.GetValue(drg));
        _sb.AppendLine("  swimWaveFreq       = " + _fFreq.GetValue(drg));
        _sb.AppendLine("  swimWaveCount      = " + _fWave.GetValue(drg) + "（会被逐段覆盖）");
        _sb.AppendLine("  limbSwingDeg       = " + _fLimbDeg.GetValue(drg));
        _sb.AppendLine();
        _sb.AppendLine("_spine.Count = " + NS + "   _modelRoot = " + (_vroot == null ? "NULL" : _vroot.name));
        _sb.AppendLine("_limbRoots.Count = " + NL + "（脊柱之外被当成肢体驱动的分支骨）");
        for (int k = 0; k < NL; k++)
        {
            var b = _limbRoots[k] as Transform;
            _sb.AppendLine("   [" + k + "] " + (b == null ? "NULL" : b.name)
                + "   父=" + (b != null && b.parent != null ? b.parent.name : "?"));
        }
        _sb.AppendLine("基准【折线总长】= " + baseFold.ToString("F3") + " m（逐节骨长之和，刚性骨骼 ⇒ 恒定）");
        _sb.AppendLine();

        if (_fStat != null && !(bool)_fStat.GetValue(drg))
        {
            _sb.AppendLine("⚠ hoverStationary 是 False ⇒ 强制打开（本轮的全部结论都依赖原地）");
            _fStat.SetValue(drg, true);
            for (int i = 0; i < 6; i++) yield return null;
        }

        _cam = MakeCam();
        _pos0 = _actor.position;
        Time.captureFramerate = FPS;

        float driftMax = 0f;
        float tailAngAll = 0f, headAngAll = 0f, limbAngAll = 0f;

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
            // 逐节段方向角（相对容器 fwd，水平面内）的极值
            var linkAngMin = new float[NS]; var linkAngMax = new float[NS];
            for (int i = 0; i < NS; i++) { linkAngMin[i] = float.MaxValue; linkAngMax[i] = float.MinValue; }
            var limbMin = new float[NL]; var limbMax = new float[NL];
            for (int k = 0; k < NL; k++) { limbMin[k] = float.MaxValue; limbMax[k] = float.MinValue; }

            var latRows = new List<float[]>();

            for (int f = 0; f < NF; f++)
            {
                try
                {
                    var pos = new Vector3[NS];
                    for (int i = 0; i < NS; i++)
                    {
                        var t = _spine[i] as Transform;
                        pos[i] = t != null ? t.position : Vector3.zero;
                    }

                    // ★ 参考系必须与驱动一致：驱动用的是 `Flat(transform.forward)`
                    //   （EnemyDragon 所在节点），不是 `_modelRoot` 的朝向 —— Visual 有本地
                    //   偏航时两者会差一个角度，投影出来的 lat 就全错了（上一版踩到）。
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

                    // ── 逐节段方向角（水平面内，相对容器 fwd）──
                    for (int i = 0; i + 1 < NS; i++)
                    {
                        Vector3 d = pos[i + 1] - pos[i];
                        Vector3 dh = new Vector3(d.x, 0f, d.z);
                        if (dh.sqrMagnitude < 1e-10f) continue;
                        float ang = Vector3.SignedAngle(fwd, dh, Vector3.up);
                        if (ang < linkAngMin[i]) linkAngMin[i] = ang;
                        if (ang > linkAngMax[i]) linkAngMax[i] = ang;
                    }

                    // ── 四肢相对基准的姿态偏离 ──
                    for (int k = 0; k < NL; k++)
                    {
                        var b = _limbRoots[k] as Transform;
                        if (b == null || k >= _limbBaseRel.Count) continue;
                        var bq = _limbBaseRel[k];
                        if (bq == null) continue;
                        float a = Quaternion.Angle((Quaternion)bq, b.localRotation);
                        if (a < limbMin[k]) limbMin[k] = a;
                        if (a > limbMax[k]) limbMax[k] = a;
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
                    // ★ 「完整 S」的准确口径：**正峰与负峰都要出现过**（不是"过零次数"——
                    //   过零次数在曲线两端归零时会被边沿的微小波动刷高，也会在单侧 C 形上
                    //   因为阈值 0.06 而虚报）。这里直接记 max+ / min-。
                    if (mx > posMax) posMax = mx;
                    if (mn < negMin) negMin = mn;

                    if (z < zMin) zMin = z; if (z > zMax) zMax = z; zSum += z;
                    if (p2p < p2pMin) p2pMin = p2p; if (p2p > p2pMax) p2pMax = p2p; p2pSum += p2p;
                    if (Mathf.Abs(lat[NS - 1]) > hMax) hMax = Mathf.Abs(lat[NS - 1]);
                    if (fold < foldMin) foldMin = fold; if (fold > foldMax) foldMax = fold;
                    if (axial < axMin) axMin = axial; if (axial > axMax) axMax = axial;
                    latRows.Add(lat);

                    float drift = Flat(_actor.position - _pos0).magnitude;
                    if (drift > driftMax) driftMax = drift;

                    if (f % 18 == 0)
                    {
                        var line = new StringBuilder("  帧 " + f.ToString("D2") + "  过零=" + z
                            + "  峰峰=" + p2p.ToString("F2") + "  头=" + lat[NS - 1].ToString("F2")
                            + "  折线=" + fold.ToString("F2") + "  轴向=" + axial.ToString("F2") + "  lat=");
                        for (int i = 0; i < NS; i += 3) line.Append(lat[i].ToString("F2") + " ");
                        _sb.AppendLine(line.ToString());
                    }

                    Snap(s * NF + f, CAM[s]);
                }
                catch (Exception ex) { _sb.AppendLine("  × 帧 " + f + " 异常 " + ex.Message); }

                Flush();
                yield return null;
            }

            // ── 段内汇总 ──
            float foldErr = Mathf.Max(Mathf.Abs(foldMin - baseFold), Mathf.Abs(foldMax - baseFold));
            float tailAng = linkAngMax[0] - linkAngMin[0];
            float headAng = linkAngMax[NS - 2] - linkAngMin[NS - 2];
            if (tailAng > tailAngAll) tailAngAll = tailAng;
            if (headAng > headAngAll) headAngAll = headAng;
            float limbWorst = float.MaxValue;
            for (int k = 0; k < NL; k++)
            {
                float a = limbMax[k] - limbMin[k];
                if (a < limbWorst) limbWorst = a;
                if (a > limbAngAll) limbAngAll = a;
            }

            _sb.AppendLine("  ── 段 " + (char)('A' + s) + " 汇总 ──");
            _sb.AppendLine("    过零   " + zMin + " ~ " + zMax + "  均值 " + (zSum / NF).ToString("F2")
                + "（旧口径，仅存档）");
            _sb.AppendLine("  ★ 完整S 正峰最大 " + posMax.ToString("F2") + " m / 负峰最小 " + negMin.ToString("F2") + " m   "
                + ((posMax > 0.30f && negMin < -0.30f) ? "✅ 全程有正弯也有负弯" : "❌ 某侧缺失（退化成 C 形）"));
            _sb.AppendLine("    峰峰   " + p2pMin.ToString("F2") + " ~ " + p2pMax.ToString("F2")
                + "  均值 " + (p2pSum / NF).ToString("F2") + " m   "
                + (p2pSum / NF >= 1.2f ? "✅" : "⚠ 幅度偏小"));
            _sb.AppendLine("    头端|lat| 最大 " + hMax.ToString("F3") + " m（现在头要动，只是记录）");
            _sb.AppendLine("    折线   " + foldMin.ToString("F3") + " ~ " + foldMax.ToString("F3")
                + " m（基准 " + baseFold.ToString("F3") + "，偏差 " + foldErr.ToString("F4") + "）   "
                + (foldErr <= 0.02f ? "✅ 未塌缩" : "❌ 身长变了"));
            _sb.AppendLine("    轴向跨度 " + axMin.ToString("F2") + " ~ " + axMax.ToString("F2") + " m");
            _sb.AppendLine("  ★ ⑦ 尾端（第 0 节）段方向角极差 = " + tailAng.ToString("F2") + "°   "
                + (tailAng >= ANG_MIN ? "✅ 尾巴在摆" : "❌ 尾巴没动"));
            _sb.AppendLine("  ★ ⑧ 头端（第 " + (NS - 2) + " 节）段方向角极差 = " + headAng.ToString("F2") + "°   "
                + (headAng >= ANG_MIN ? "✅ 头在动" : "❌ 头没动"));
            if (NL > 0)
            {
                var lw = new StringBuilder();
                for (int k = 0; k < NL; k++)
                    lw.Append("[" + k + "]" + (limbMax[k] - limbMin[k]).ToString("F1") + "° ");
                _sb.AppendLine("  ★ ⑨ 四肢摆角极差 = " + lw.ToString()
                    + "  最差 " + limbWorst.ToString("F2") + "°   "
                    + (limbWorst >= ANG_MIN ? "✅ 爪子在动" : "❌ 有爪子没动"));
            }

            // 逐节全动
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
            + (driftMax <= 0.01f ? "✅ 完全静止（用户要求达成）" : "❌ 仍在移动"));
        _sb.AppendLine("★ ⑦ 尾端最好成绩 = " + tailAngAll.ToString("F2") + "°   "
            + (tailAngAll >= ANG_MIN ? "✅" : "❌ 尾巴全程没动"));
        _sb.AppendLine("★ ⑧ 头端最好成绩 = " + headAngAll.ToString("F2") + "°   "
            + (headAngAll >= ANG_MIN ? "✅" : "❌ 头全程没动"));
        if (_limbRoots != null && _limbRoots.Count > 0)
            _sb.AppendLine("★ ⑨ 四肢最好成绩 = " + limbAngAll.ToString("F2") + "°   "
                + (limbAngAll >= ANG_MIN ? "✅" : "❌ 四肢全程没动"));
        else
            _sb.AppendLine("★ ⑨ 四肢：`_limbRoots` 为空 ⇒ 没有识别到任何肢体（这个要单独查）");
        _sb.AppendLine();
        _sb.AppendLine("★ 5 段对照已按帧号连续录出，帧目录 " + SD);
        _sb.AppendLine("    A 0000~0071   B 0072~0143   C 0144~0215   D 0216~0287   E 0288~0359");
        _sb.AppendLine();
        _sb.AppendLine("PNG 帧数 = " + _saved);

        if (_cam != null) UnityEngine.Object.Destroy(_cam.gameObject);
        yield return Done();
    }

    Camera MakeCam()
    {
        var g = new GameObject("PROBE_CAM_DFM");
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
        // ★★ 取景用**骨骼链的实际位置**，不能用 Renderer.bounds：
        //   SkinnedMeshRenderer.bounds 是**绑定姿势**的包围盒，不随骨骼变形更新
        //   ⇒ 上一版相机对着 old 位置取景，画面里只有半截龙（实测踩到）。
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
        // 余量 1.12：1.45 时龙只占画面 40%，观感太远（实测）
        float dist = rad / Mathf.Sin(_cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.12f;

        Vector3 fwd = _drgT.forward; fwd.y = 0f;
        if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.forward;
        fwd.Normalize();
        Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized;

        Vector3 dir;
        if (camName == "top")
        {
            // 近正俯视（留 30% 的前后分量，避免 LookAt 的 up 与视线平行而退化）
            dir = (Vector3.up * 0.92f + fwd * 0.30f + right * 0.22f).normalized;
        }
        else
        {
            // 跟车视角：正后方偏上 —— 玩家实际看到的"左右摆动"就是这个角度
            dir = (-fwd * 0.80f + Vector3.up * 0.52f + right * 0.20f).normalized;
        }

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
        File.WriteAllBytes(SD + "/dfm_" + f.ToString("D4") + ".png", tex.EncodeToPNG());
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
        Debug.Log("[drg_form] 写入 " + RP + "  帧数 " + _saved);
        yield return null;
    }

    void Flush() { try { File.WriteAllText(RP, _sb.ToString()); } catch { } }
}

var __hostDfm = new GameObject("drg_form");
__hostDfm.AddComponent<drg_form>();
return "DRG_FORM_STARTED";
