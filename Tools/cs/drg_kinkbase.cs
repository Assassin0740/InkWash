using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;

// drg_kinkbase.cs —— 「接点折角」这把尺子的**噪声底座**
//
// 起因：drg_tail19 实测修后接点折角均值 6.1°（max 10.2°），与我"应当 ≈ 0°"的预期不符。
// 分析假设：链是按「弦 = 骨长」驱动的，弦方向相对真实切线的偏差 ≈ ½·lat''·Δu/T，
//   首阶占比只与**段长**有关 ⇒ 接点两侧的弦分别偏向切线的两边 ⇒ 夹角 ≠ 0，且与曲率同相。
//   **若这个假设成立，身体自己相邻两节的夹角应该同量级** —— 也就是说 6.1° 是"尺子的本底"，
//   不是"接点断了"。本探针就是来证伪/证实这一点的：三组数字放一起比。
//
// 三组（同一时刻取）：
//   ① 身体相邻节夹角  Angle(dir_i, dir_{i+1})     —— 脊柱内部（纯粹是曲线自己的弯）
//   ② 尾巴相邻节夹角  Angle(tdir_i, tdir_{i+1})   —— 尾链内部
//   ③ 接点夹角        Angle(body dir_0, −tail dir_0) —— 被测对象
// 判据：③ 的统计量落在 ①/② 的同一量级 ⇒ 接点与身体内部一样平滑，没有"断"。
//
// 无渲染、无出图，纯数值（约 20 s）。

public class drg_kinkbase : MonoBehaviour
{
    const string RP = "D:/Unity Project/InkWash/Tools/reports/drg_kinkbase.txt";
    const string KEY = "墨龙";
    const int TICKS = 200;
    const int FPS = 30;

    static readonly BindingFlags BF = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;

    readonly StringBuilder _sb = new StringBuilder();
    Component _sc; Type _ty, _dt;
    MethodInfo _mPlay, _mLabel, _mStop;
    PropertyInfo _pCount;
    GameObject _go;
    Transform _drgT;
    System.Collections.IList _spine, _tailChain;
    int _NS, _NT;

    readonly List<float> _body = new List<float>();
    readonly List<float> _tail = new List<float>();
    readonly List<float> _junc = new List<float>();

    void Start() { StartCoroutine(Run()); }

    IEnumerator Run()
    {
        yield return null; yield return null;
        Time.captureFramerate = FPS;

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

        var drg = _go.GetComponent(_dt);
        _drgT = drg.transform;
        _spine = _dt.GetField("_spine", BF).GetValue(drg) as System.Collections.IList;
        _tailChain = _dt.GetField("_tailChain", BF).GetValue(drg) as System.Collections.IList;
        var fFollow = _dt.GetField("tailFollowBodyWave", BF);
        if (fFollow == null || _spine == null || _tailChain == null)
        { _sb.AppendLine("× 字段缺失 / 链为空"); yield return Done(); }

        _NS = _spine.Count; _NT = _tailChain.Count;

        _sb.AppendLine("=== drg_kinkbase：接点折角这把尺子的噪声底座 ===");
        _sb.AppendLine("条目 idx=" + idx + "  label=" + found);
        _sb.AppendLine("脊柱 " + _NS + " 节｜尾巴链 " + _NT + " 节｜" + TICKS + " tick = "
                       + (TICKS / (float)FPS * 0.45f).ToString("F3") + " 个驱动周期");
        _sb.AppendLine();
        _dt.GetField("hoverStationary", BF).SetValue(drg, false);
        _dt.GetField("enablePerformanceCycle", BF).SetValue(drg, false);

        foreach (bool follow in new bool[] { true, false })
        {
            fFollow.SetValue(drg, follow);
            _body.Clear(); _tail.Clear(); _junc.Clear();
            for (int i = 0; i < 8; i++) yield return null;
            for (int k = 0; k < TICKS; k++) { Sample(); yield return null; }

            _sb.AppendLine("── tailFollowBodyWave = " + follow + " ──");
            _sb.AppendLine("  组                                 n    均值     最大");
            Group("① 身体相邻节夹角 (脊柱内部)", _body);
            Group("② 尾巴相邻节夹角 (尾链内部)", _tail);
            Group("③ 接点夹角 (被测对象)", _junc);
            float mb = Mean(_body);
            _sb.AppendLine("  ⇒ ③ 均值 / ① 均值 = " + (Mean(_junc) / Mathf.Max(1e-4f, mb)).ToString("F2")
                           + "（接近 1 = 接点与身体内部一样平滑）");
            _sb.AppendLine();
        }

        _sb.AppendLine("段长（用于核对「弦偏差 ∝ 段长」这个解释）：");
        _sb.AppendLine("  身体首段 " + SegLen(_spine, 0).ToString("F3") + " m｜身体平均 "
                       + AvgSeg(_spine, _NS).ToString("F3") + " m"
                       + "｜尾巴首段 " + SegLen(_tailChain, 0).ToString("F3") + " m｜尾巴平均 "
                       + AvgSeg(_tailChain, _NT).ToString("F3") + " m");
        yield return Done();
    }

    void Sample()
    {
        // ① 身体相邻节
        for (int i = 0; i + 2 < _NS; i++)
            _body.Add(Vector3.Angle(Dir(_spine, i), Dir(_spine, i + 1)));
        // ② 尾巴相邻节
        for (int i = 0; i + 2 < _NT; i++)
            _tail.Add(Vector3.Angle(Dir(_tailChain, i), Dir(_tailChain, i + 1)));
        // ③ 接点：身体首节（朝头）与尾巴首节（朝尾）共线 ⇒ Angle(b, −t)
        Vector3 b0 = Dir(_spine, 0);
        Vector3 t0 = Dir(_tailChain, 0);
        _junc.Add(Vector3.Angle(b0, -t0));
    }

    static Vector3 Dir(System.Collections.IList chain, int i)
    {
        var a = chain[i] as Transform;
        var b = chain[i + 1] as Transform;
        if (a == null || b == null) return Vector3.forward;
        Vector3 d = b.position - a.position;
        return d.sqrMagnitude > 1e-12f ? d.normalized : Vector3.forward;
    }

    static float SegLen(System.Collections.IList chain, int i)
    {
        var a = chain[i] as Transform;
        var b = chain[i + 1] as Transform;
        return (a == null || b == null) ? 0f : Vector3.Distance(a.position, b.position);
    }

    static float AvgSeg(System.Collections.IList chain, int n)
    {
        float s = 0f; int c = 0;
        for (int i = 0; i + 1 < n; i++) { s += SegLen(chain, i); c++; }
        return c > 0 ? s / c : 0f;
    }

    void Group(string label, List<float> v)
    {
        float mn = float.MaxValue, mx = float.MinValue;
        for (int i = 0; i < v.Count; i++) { mn = Mathf.Min(mn, v[i]); mx = Mathf.Max(mx, v[i]); }
        _sb.AppendLine("  " + Pad(label, 34) + Pad(v.Count.ToString(), 6)
                       + Pad(mn.ToString("F2") + "°", 9) + Pad(Mean(v).ToString("F2") + "°", 9)
                       + mx.ToString("F2") + "°");
    }

    static float Mean(List<float> v)
    {
        if (v.Count == 0) return 0f;
        float s = 0f;
        for (int i = 0; i < v.Count; i++) s += v[i];
        return s / v.Count;
    }

    static string Pad(string s, int n) { return s.Length >= n ? s : s + new string(' ', n - s.Length); }

    IEnumerator Done()
    {
        Time.captureFramerate = 0;
        try { File.WriteAllText(RP, _sb.ToString()); } catch { }
        Debug.Log("[drg_kinkbase] 写入 " + RP);
        yield return null;
    }
}

var __hostKB = new GameObject("drg_kinkbase");
__hostKB.AddComponent<drg_kinkbase>();
return "DRG_KINKBASE_STARTED";
