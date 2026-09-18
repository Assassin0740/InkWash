using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;

// drg_serp.cs —— 墨龙「蛇形游动 = 骨骼排布成 sin 曲线」验收 + 录像
//
// 用户原话（本轮唯一的指令）：
//   「你这个龙他很明显做不到完整的这个蜿蜒前行啊，你先做好他的这个移动，一直重复就好了，
//     这个蜿蜒前行移动，类似 Sin 那样，**要让他的身体里面的骨骼贴合 sin，而不是整体的移动贴合 sin**，
//     这是两个方向」
//
// ■ 这次要验的东西和以往**不是一回事**：
//   旧验收量的是「弦垂距」（链相对尾→头弦的最大垂距）—— 它只能证明"身体是弯的"，
//   证明不了"弯的是 sin"。一条 C 形（单侧弓）的弦垂距可以很大，但那是半个波，不是蜿蜒。
//   所以本探针的判据全部围绕**波形本身**：
//
//   ① **过零次数**（每帧统计，取全程最小值）：
//      lat[i] 沿链的符号变化次数。完整 S（正峰 + 负峰）至少要 **2 次**过零。
//      ★ 这条判据是"完整"二字的直接量化 —— 参数扫描（serp_param）已证明：
//        波数 1.25 / 1.5 时某些相位下最少过零 = 1（退化成 C 形），≥ 1.8 时稳定 = 2。
//      ★ 阈值 0.06 m：两端 env→0 ⇒ lat 趋近 0，不设阈值会被浮点噪声刷出假过零。
//   ② **链内峰峰横向极差** ≥ 1.2 m：身体上确实有肉眼可见的起伏。
//   ③ **两端都在轴线上**：头端 |lat| ≤ 0.30 m、尾端 = 0（尾是链根，位置由 _rootJoint 定，
//      我控制不了 ⇒ 曲线的 lat(0) 必须为 0，这条同时是"曲线锚在链根上"的自检）。
//      ★ 头端小的收益：头沿轴线走 ⇒ 真蛇那种"头稳、用来瞄准"。
//   ④ **折线总长**恒定（保险丝）：24 节骨长之和与基准一致，形状变化不该改变身长。
//      对比：事后左乘那版曾让身长从 7.5 m 塌到 3.3 m（龙蜷成一圈）。
//   ⑤ **波形在传播**：帧 f0 与 f1 的 lat 序列做互相关，峰值位移 ≠ 0 ⇒ 是行波不是驻波。
//   ⑥ **逐节全动**：每节的 lat 极差都 > 0.05 m（尾部也参与，不是只有头在扭）。
//
// ★ 录像视角是**跟着龙转的**（相机方向由容器 fwd/right 构造）：
//   龙在绕圈巡游，用固定世界方向的相机看，龙的"前方"一直在变 ⇒ 从画面里读不出 S 形。
//   锁定到龙的参考系后，画面里龙始终朝同一侧，横向摆动才看得出来。
//
// ★ 脚本形态：Codely 是 Roslyn script，必须有顶层语句；运行时探针末尾要 new 一个宿主对象。

public class drg_serp : MonoBehaviour
{
    const string RP  = "D:/Unity Project/InkWash/Tools/reports/drg_serp.txt";
    const string SD  = "D:/Unity Project/InkWash/Tools/screenshots/enemies/DRS";
    const string KEY = "墨龙";

    const int FPS = 30;
    const int NF  = 120;      // 4.0 s（swimWaveFreq=0.45 Hz ⇒ 1.8 个波周期）

    const float ZT = 0.06f;   // 过零阈值（m）

    readonly StringBuilder _sb = new StringBuilder();
    Component _sc; Type _ty, _dt;
    MethodInfo _mPlay, _mLabel, _mStop, _mHover;
    PropertyInfo _pCount;
    Camera _cam;
    GameObject _go;
    Transform _actor, _vroot;
    IList _spine;
    FieldInfo _fLen;

    int _saved;
    float _refFoldLen = -1f;

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

        _sb.AppendLine("=== drg_serp：墨龙「蛇形游动 = 骨骼排布成 sin 曲线」验收 ===");
        _sb.AppendLine("条目 idx=" + idx + "  label=" + found);
        _sb.AppendLine("captureFramerate=" + FPS + "  frames=" + NF + "  (" + (NF / (float)FPS).ToString("F1") + " s)");
        _sb.AppendLine();

        _mStop.Invoke(_sc, null);
        _mPlay.Invoke(_sc, new object[] { idx });
        for (int i = 0; i < 90; i++) yield return null;   // 等它起飞、进入盘旋

        _go = GameObject.Find("Showcase_Actor_" + idx);
        if (_go == null) { _sb.AppendLine("× 没找到 actor 实例 Showcase_Actor_" + idx); yield return Done(); }
        _actor = _go.transform;

        var drg = _go.GetComponent(_dt);
        _vroot = _dt.GetField("_modelRoot", BF).GetValue(drg) as Transform;
        _spine = _dt.GetField("_spine", BF).GetValue(drg) as IList;
        _fLen  = _dt.GetField("_spineLinksFound", BF);
        _mHover = _dt.GetMethod("ForceBeginHoverForTest", BF);
        if (_mHover != null) { try { _mHover.Invoke(drg, null); } catch { } }

        // ── 参数回读（新字段 prefab 里没有同名键 ⇒ 走 C# 默认值）──
        _sb.AppendLine("── 运行时参数回读 ──");
        string[] pf = { "swimWaveAmp", "swimWaveCount", "swimWaveFreq", "swimWaveEnvPow",
                        "pathWaveAmpDeg", "swimSpeed", "swimTurnRate", "orbitAngularSpeedDeg",
                        "restMotionScale", "roarSpineGain" };
        foreach (var n in pf)
        {
            var f = _dt.GetField(n, BF);
            _sb.AppendLine("   " + n.PadRight(24) + (f == null ? "<无>" : f.GetValue(drg).ToString()));
        }
        _sb.AppendLine();

        int NS = _spine == null ? 0 : _spine.Count;
        _sb.AppendLine("_spine.Count = " + NS + "   _modelRoot = " + (_vroot == null ? "NULL" : _vroot.name));
        if (NS < 3) { _sb.AppendLine("× 脊柱节数 < 3"); yield return Done(); }

        // ── 基准骨长表（用来做"折线总长恒定"这条保险丝）──
        var segLen = new float[NS - 1];
        for (int i = 0; i + 1 < NS; i++)
        {
            var a = _spine[i] as Transform; var b = _spine[i + 1] as Transform;
            segLen[i] = (a != null && b != null) ? Vector3.Distance(a.position, b.position) : 0f;
        }
        float baseFold = 0f; for (int i = 0; i < NS - 1; i++) baseFold += segLen[i];
        _sb.AppendLine("基准【折线总长】= " + baseFold.ToString("F3") + " m  （逐节骨长之和，刚性骨骼 ⇒ 恒定）");
        _sb.AppendLine("      首尾直线   = " + Vector3.Distance((_spine[0] as Transform).position,
                                                             (_spine[NS - 1] as Transform).position).ToString("F3") + " m");
        _sb.AppendLine();

        _cam = MakeCam();

        // 逐帧记录：lat 序列（侧向偏移）+ 轴向位置 + 折线总长 + 逐节极差
        var latSeq = new List<float[]>();
        var axSeq  = new List<float[]>();
        var foldSeq = new List<float>();
        var headSeq = new List<float>();
        var zeroSeq = new List<int>();
        var p2pSeq  = new List<float>();
        var linkMin = new float[NS]; var linkMax = new float[NS];
        for (int i = 0; i < NS; i++) { linkMin[i] = float.MaxValue; linkMax[i] = float.MinValue; }

        Time.captureFramerate = FPS;

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

                // 参考系 = 容器自身的水平前向/右向（龙在绕圈转向，必须用它自己的系）
                Quaternion rootRot = _vroot != null ? _vroot.rotation : _actor.rotation;
                Vector3 fwd = rootRot * Vector3.forward; fwd.y = 0f;
                if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.forward;
                fwd.Normalize();
                Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized;

                Vector3 o = pos[0];
                var lat = new float[NS]; var ax = new float[NS];
                float fold = 0f;
                for (int i = 0; i < NS; i++)
                {
                    Vector3 d = pos[i] - o;
                    ax[i]  = Vector3.Dot(d, fwd);
                    lat[i] = Vector3.Dot(d, right);
                    if (i > 0) fold += Vector3.Distance(pos[i - 1], pos[i]);
                    if (lat[i] < linkMin[i]) linkMin[i] = lat[i];
                    if (lat[i] > linkMax[i]) linkMax[i] = lat[i];
                }

                // 过零次数（正负反相的次数）
                int z = 0, prevs = 0;
                for (int i = 0; i < NS; i++)
                {
                    int s = lat[i] > ZT ? 1 : (lat[i] < -ZT ? -1 : 0);
                    if (s != 0) { if (prevs != 0 && s != prevs) z++; prevs = s; }
                }

                float p2p = 0f, mn = float.MaxValue, mx = float.MinValue;
                for (int i = 0; i < NS; i++) { if (lat[i] < mn) mn = lat[i]; if (lat[i] > mx) mx = lat[i]; }
                p2p = mx - mn;

                latSeq.Add(lat); axSeq.Add(ax); foldSeq.Add(fold);
                headSeq.Add(lat[NS - 1]); zeroSeq.Add(z); p2pSeq.Add(p2p);

                if (f % 12 == 0)
                {
                    var line = new StringBuilder("帧 " + f.ToString("D3") + "  过零=" + z
                        + "  峰峰=" + p2p.ToString("F2") + "  头=" + lat[NS - 1].ToString("F2")
                        + "  折线=" + fold.ToString("F2") + "  lat=");
                    for (int i = 0; i < NS; i += 3) line.Append(lat[i].ToString("F2") + " ");
                    _sb.AppendLine(line.ToString());
                }

                Snap(f);
            }
            catch (Exception ex) { _sb.AppendLine("  × 帧 " + f + " 异常 " + ex.Message); }

            Flush();
            yield return null;
        }

        Time.captureFramerate = 0;

        // ══════════════ 汇总 ══════════════
        _sb.AppendLine();
        _sb.AppendLine("===== 汇总 =====");

        // ★ 跳过**启动瞬态**：探针开跑时龙可能还没完全进入盘旋、身体还是基准直线
        //   （实测帧 000 的 lat 全为 0 ⇒ 过零 0），那几帧会把"最少过零"钉成 0
        //   —— 第一版就踩了这个假阴性。只统计后 4/5 的帧。
        int warm = Mathf.Max(1, NF / 5);
        int zMin = int.MaxValue, zMax = int.MinValue; float zSum = 0f; int zN = 0;
        for (int i = warm; i < zeroSeq.Count; i++) { if (zeroSeq[i] < zMin) zMin = zeroSeq[i]; if (zeroSeq[i] > zMax) zMax = zeroSeq[i]; zSum += zeroSeq[i]; zN++; }
        float zAvg = zN > 0 ? zSum / zN : 0f;

        float p2pMin = float.MaxValue, p2pMax = float.MinValue, p2pSum = 0f; int pN = 0;
        for (int i = warm; i < p2pSeq.Count; i++) { if (p2pSeq[i] < p2pMin) p2pMin = p2pSeq[i]; if (p2pSeq[i] > p2pMax) p2pMax = p2pSeq[i]; p2pSum += p2pSeq[i]; pN++; }
        float p2pAvg = pN > 0 ? p2pSum / pN : 0f;

        float hMax = 0f; for (int i = 0; i < headSeq.Count; i++) if (Mathf.Abs(headSeq[i]) > hMax) hMax = Mathf.Abs(headSeq[i]);

        float foldMin = float.MaxValue, foldMax = float.MinValue;
        for (int i = 0; i < foldSeq.Count; i++) { if (foldSeq[i] < foldMin) foldMin = foldSeq[i]; if (foldSeq[i] > foldMax) foldMax = foldSeq[i]; }

        _sb.AppendLine("★ 判据① 【完整蜿蜒】 过零次数  最少=" + zMin + "  最多=" + zMax + "  均值=" + zAvg.ToString("F2"));
        _sb.AppendLine("       合格线 ≥2（正峰 + 负峰 = 一个完整 S）；=1 表示退化成单侧 C 形");
        _sb.AppendLine("       " + (zMin >= 2 ? "✅ 通过" : "❌ 不通过"));
        _sb.AppendLine();
        _sb.AppendLine("★ 判据② 【幅度】 链内峰峰横向极差  最小=" + p2pMin.ToString("F2")
            + "  最大=" + p2pMax.ToString("F2") + "  均值=" + p2pAvg.ToString("F2") + " m");
        _sb.AppendLine("       合格线 均值 ≥1.2 m；" + (p2pAvg >= 1.2f ? "✅ 通过" : "❌ 不通过"));
        _sb.AppendLine();
        _sb.AppendLine("★ 判据③ 【两端在轴上】 头端 |lat| 最大 = " + hMax.ToString("F3") + " m（合格 ≤0.30）"
            + "  " + (hMax <= 0.30f ? "✅" : "❌"));
        _sb.AppendLine("       尾端恒为 0（链根位置由 _rootJoint 决定，曲线 lat(0) 必须为 0 —— 这是硬约束）");
        _sb.AppendLine();
        _sb.AppendLine("★ 判据④ 【身长保险丝】 折线总长 " + foldMin.ToString("F3") + " ~ "
            + foldMax.ToString("F3") + " m  （基准 " + baseFold.ToString("F3") + "）");
        float foldErr = Mathf.Max(Mathf.Abs(foldMin - baseFold), Mathf.Abs(foldMax - baseFold));
        _sb.AppendLine("       与基准的最大偏差 = " + foldErr.ToString("F4") + " m（合格 ≤0.02）"
            + "  " + (foldErr <= 0.02f ? "✅" : "❌"));

        // ── 判据⑤：行波（互相关）──
        _sb.AppendLine();
        // ★ 取样间隔必须**远小于波周期**：swimWaveFreq = 0.45 Hz ⇒ 周期 2.22 s（66 帧）。
        //   第一版取 f0 = NF/5、f1 = NF*4/5（间隔 2.40 s ≈ 1.08 个周期）⇒ 波形正好走回原位，
        //   互相关峰值退化到 -1 节、波速被算成 0.15 m/s（应该是 ~2 m/s）—— 判据本身失效。
        //   取 8 帧 = 0.27 s。
        int f0 = NF / 2, f1 = NF / 2 + 8;
        if (latSeq.Count > f1)
        {
            var A = latSeq[f0]; var B = latSeq[f1];
            float best = float.MinValue; int bestK = 0;
            var cs = new StringBuilder();
            for (int k = -6; k <= 6; k++)
            {
                float s = 0f; int n = 0;
                for (int i = 0; i < NS; i++)
                {
                    int j = i + k;
                    if (j < 0 || j >= NS) continue;
                    s += A[i] * B[j]; n++;
                }
                if (n > 0) s /= n;
                if (s > best) { best = s; bestK = k; }
                cs.Append(k.ToString("+0;-0;0") + ":" + s.ToString("F2") + " ");
            }
            _sb.AppendLine("★ 判据⑤ 【行波】 帧" + f0 + " × 帧" + f1 + " 的 lat 互相关，峰值位移 = "
                + bestK + " 节 / " + ((f1 - f0) / (float)FPS).ToString("F2") + " s");
            _sb.AppendLine("       互相关序列 " + cs.ToString());
            _sb.AppendLine("       " + (bestK != 0 ? "✅ 非驻波（波形在沿身体移动）" : "⚠ 驻波（波形原地鼓）"));
            _sb.AppendLine("       ★ bestK>0 = 波向 i 增大（头侧）传；<0 = 向尾侧传。方向本身无对错，看观感。");
            _sb.AppendLine("       波速 ≈ " + (Mathf.Abs(bestK) / ((f1 - f0) / (float)FPS) * (baseFold / (NS - 1))).ToString("F2") + " m/s");
        }

        // ── 判据⑥：逐节全动 ──
        _sb.AppendLine();
        _sb.AppendLine("===== 判据⑥ 逐节 lat 极差 =====");
        // ★ 只统计**中段** i ∈ [3, NS-1]。两端各有 2 节天然摆幅小，那是包络的**有意设计**
        //   （env = sin³(πu) 在 u→0 与 u→1 处归零 ⇒ 尾段收窄留在轴上、头段收窄保证头沿轴线走），
        //   把它们算进去就是一条永远不达标的假判据：
        //     · 第 0 节 = 链根/锚点，lat 恒为 0（位置由 `_rootJoint` 决定，驱动层控制不了）
        //     · 第 1~2 节在 u ≈ 0.04 处，env ≈ 0.0006 ⇒ 摆幅 < 0.05 m
        const int MID = 3;
        int moving = 0; var detail = new StringBuilder("  ");
        for (int i = MID; i < NS; i++)
        {
            float rng = linkMax[i] - linkMin[i];
            if (rng > 0.05f) moving++;
            if (i % 3 == 0 || i == NS - 1) detail.Append("[" + i.ToString("D2") + "]" + rng.ToString("F2") + " ");
        }
        _sb.AppendLine(detail.ToString());
        _sb.AppendLine("  中段（i≥" + MID + "，剔除锚点与包络收窄区）摆幅 > 0.05 m 的节数 = "
            + moving + " / " + (NS - MID)
            + (moving >= NS - MID ? "  ✅ 从尾段到头部全参与" : "  ⚠ 有节几乎不动"));
        _sb.AppendLine("  锚点附近（i=0..2）极差 = [" + (linkMax[0] - linkMin[0]).ToString("F2") + " "
            + (linkMax[1] - linkMin[1]).ToString("F2") + " " + (linkMax[2] - linkMin[2]).ToString("F2")
            + "]  ← 包络收窄的预期结果，不是缺陷");

        _sb.AppendLine();
        _sb.AppendLine("PNG 帧数 = " + _saved + "  → " + SD);

        if (_cam != null) UnityEngine.Object.Destroy(_cam.gameObject);
        yield return Done();
    }

    Camera MakeCam()
    {
        var g = new GameObject("PROBE_CAM_DRS");
        var c = g.AddComponent<Camera>();
        c.clearFlags = CameraClearFlags.SolidColor;
        c.backgroundColor = new Color(0.86f, 0.88f, 0.91f, 1f);
        c.fieldOfView = 45f;
        c.nearClipPlane = 0.05f;
        c.farClipPlane = 5000f;
        c.enabled = false;
        return c;
    }

    void Snap(int f)
    {
        var rds = _go.GetComponentsInChildren<Renderer>(true);
        if (rds.Length == 0) return;
        var b = rds[0].bounds;
        for (int i = 1; i < rds.Length; i++) b.Encapsulate(rds[i].bounds);

        float rad = Mathf.Max(b.extents.magnitude, 1f);
        float dist = rad / Mathf.Sin(_cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.15f;

        // ★★ 相机方向由**容器自己的 fwd/right** 构造 ⇒ 画面里龙的长轴始终横躺、朝同一侧。
        //   用固定世界方向的话，龙一路绕圈，它在画面里会一直转，横向摆动读不出来。
        //   仰角压到 0.55 左右：偏俯视，水平 sin 形看得最清楚（这次的验收对象就是水平 S 形）。
        Quaternion rootRot = _vroot != null ? _vroot.rotation : _actor.rotation;
        Vector3 fwd = rootRot * Vector3.forward; fwd.y = 0f;
        if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.forward;
        fwd.Normalize();
        Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized;

        Vector3 dir = (-fwd * 0.30f + right * 0.78f + Vector3.up * 0.72f).normalized;
        Vector3 center = b.center;
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
        File.WriteAllBytes(SD + "/drs_" + f.ToString("D4") + ".png", tex.EncodeToPNG());
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
        Debug.Log("[drg_serp] 写入 " + RP + "  帧数 " + _saved);
        yield return null;
    }

    void Flush() { try { File.WriteAllText(RP, _sb.ToString()); } catch { } }
}

var __hostDrs = new GameObject("drg_serp");
__hostDrs.AddComponent<drg_serp>();
return "DRG_SERP_STARTED";
