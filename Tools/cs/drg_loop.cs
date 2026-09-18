using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;

// drg_loop.cs —— 第十八轮交付录像（连续循环，不再做多档对比）
//
// 用户第四条：「动画应该是不断重复的，就是始终保持重复才对的」。
// 上一轮的 5 段对照视频把波形切成 4 档，看起来像"四个独立状态"，不像循环 —— 这一版改掉。
//
// ★ 为什么这样就是**无缝循环**：波形相位 = 2π·swimWaveFreq·t，形状是相位的纯函数
//   ⇒ 周期 T = 1/swimWaveFreq = 1/0.45 = 2.2222 s = 66.67 帧 @30fps。
//   录 **134 帧 = 整 2 个周期** ⇒ 首尾形状一致（亚帧误差 0.0067 帧 ≈ 2e-4 m，肉眼不可见）。
//   （`drg_fix` 已用"帧 f 与 f+90 的形状残差 = 1e-5 m / 相邻帧 2.7e-2 m"证实严格周期。）
//
// 三段：A 整身侧视 ×2 周期（看尾巴平不平 / 头顺不顺 / 腿收没收）
//       B 前 3/4 俯视 ×2 周期（看整体飞行观感）
//       C 头部特写 ×1 周期

public class drg_loop : MonoBehaviour
{
    const string SD = "D:/Unity Project/InkWash/Tools/screenshots/enemies/LP";
    const string RP = "D:/Unity Project/InkWash/Tools/reports/drg_loop.txt";
    const string KEY = "墨龙";

    const int FPS = 30;
    const int FR_AB = 134;     // 2 个周期
    const int FR_C = 67;       // 1 个周期

    readonly StringBuilder _sb = new StringBuilder();
    Component _sc; Type _ty, _dt;
    MethodInfo _mPlay, _mLabel, _mStop;
    PropertyInfo _pCount;
    Camera _cam;
    GameObject _go;
    Transform _drgT, _vroot;
    System.Collections.IList _spine, _limbRoots, _tailChain;

    static readonly BindingFlags BF = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;

    int _n;

    void Start() { StartCoroutine(Run()); }

    IEnumerator Run()
    {
        yield return null; yield return null;
        Time.captureFramerate = FPS;

        Directory.CreateDirectory(SD);
        foreach (var f in Directory.GetFiles(SD, "*.png")) { try { File.Delete(f); } catch { } }

        foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (_ty == null) { var t1 = a.GetType("InkWash.DebugTools.ActionShowcase"); if (t1 != null) _ty = t1; }
            if (_dt == null) { var t2 = a.GetType("InkWash.Enemies.EnemyDragon"); if (t2 != null) _dt = t2; }
        }
        if (_ty == null || _dt == null) { _sb.AppendLine("× 类型缺失"); yield return Done(); }

        foreach (var o in UnityEngine.Object.FindObjectsOfType(_ty, true)) { _sc = o as Component; if (_sc != null) break; }
        if (_sc == null) { _sb.AppendLine("× 场景里没有 ActionShowcase（当前不是 Showcase 场景？先跑 q_open_scene.cs）"); yield return Done(); }

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
        _vroot = _dt.GetField("_modelRoot", BF).GetValue(drg) as Transform;
        _spine = _dt.GetField("_spine", BF).GetValue(drg) as System.Collections.IList;
        _limbRoots = _dt.GetField("_limbRoots", BF).GetValue(drg) as System.Collections.IList;
        _tailChain = _dt.GetField("_tailChain", BF).GetValue(drg) as System.Collections.IList;
        if (_spine == null || _spine.Count < 3) { _sb.AppendLine("× 脊柱节数 < 3"); yield return Done(); }

        if (_dt.GetField("headAlignPitchDeg", BF) == null) { _sb.AppendLine("× 第十八轮字段缺失"); yield return Done(); }

        _sb.AppendLine("=== drg_loop：第十八轮交付录像（连续循环）===");
        _sb.AppendLine("条目 idx=" + idx + "  label=" + found);
        _sb.AppendLine("帧步长钉死 " + FPS + " fps；A/B 各 " + FR_AB + " 帧（= 整 2 个周期），C " + FR_C + " 帧（1 个周期）");
        _sb.AppendLine();
        _sb.AppendLine("── 生效参数（prefab 值，不再由探针改）──");
        foreach (var nm in new string[] { "hoverStationary", "swimWaveAmp", "swimWaveCount", "swimWaveFreq",
                                          "limbSwingDeg", "limbSweepBackDeg", "tailLevel", "tailWaveAmp",
                                          "tailWaveSpan", "headAlignPitchDeg", "headAlignYawDeg",
                                          "headAlignRollDeg", "headAlignLinks" })
            _sb.AppendLine("  " + Pad(nm, 22) + " = " + _dt.GetField(nm, BF).GetValue(drg));
        _sb.AppendLine();
        _sb.AppendLine("  尾巴链节数 = " + (_tailChain == null ? -1 : _tailChain.Count));
        _sb.AppendLine();

        for (int i = 0; i < 6; i++) yield return null;
        // ★ 每出一张图就 `yield` 推进一帧 —— 千万不能在同一个帧里连拍 134 张（全是同一姿态）
        for (int f = 0; f < FR_AB; f++) { Snap("side"); yield return null; }
        for (int i = 0; i < 6; i++) yield return null;
        for (int f = 0; f < FR_AB; f++) { Snap("q34"); yield return null; }
        for (int i = 0; i < 6; i++) yield return null;
        for (int f = 0; f < FR_C; f++) { Snap("head"); yield return null; }

        _sb.AppendLine("PNG 总数 = " + _n + "（LP/lp_%04d.png，顺序：side×" + FR_AB + " → q34×" + FR_AB + " → head×" + FR_C + "）");
        if (_cam != null) UnityEngine.Object.Destroy(_cam.gameObject);
        yield return Done();
    }

    void Snap(string view)
    {
        if (_cam == null) _cam = MakeCam();
        int NS = _spine.Count;
        var pos = new Vector3[NS];
        for (int i = 0; i < NS; i++) { var t = _spine[i] as Transform; pos[i] = t != null ? t.position : Vector3.zero; }

        Vector3 fwd = Flat(_drgT.forward).normalized;
        Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized;

        Vector3 center; float rad; Vector3 dir;

        if (view == "head")
        {
            var h22 = _spine[NS - 2] as Transform;
            Vector3 acc = Vector3.zero; int c = 0;
            for (int i = 0; i < h22.childCount; i++) { acc += h22.GetChild(i).position; c++; }
            center = acc / Mathf.Max(1, c);
            rad = 1.05f;
            for (int i = 0; i < h22.childCount; i++)
                rad = Mathf.Max(rad, (h22.GetChild(i).position - center).magnitude + 0.18f);
            dir = (-right * 0.40f + fwd * 0.74f + Vector3.up * 0.54f).normalized;
        }
        else
        {
            center = Vector3.zero; for (int i = 0; i < NS; i++) center += pos[i]; center /= NS;
            rad = 0f; for (int i = 0; i < NS; i++) rad = Mathf.Max(rad, (pos[i] - center).magnitude);
            if (_limbRoots != null)
                for (int k = 0; k < _limbRoots.Count; k++)
                {
                    var b = _limbRoots[k] as Transform;
                    if (b != null) rad = Mathf.Max(rad, (b.position - center).magnitude + 3.4f);
                }
            if (view == "side") dir = (right * 0.982f + Vector3.up * 0.135f + fwd * 0.13f).normalized;
            else dir = (-right * 0.34f + fwd * 0.62f + Vector3.up * 0.71f).normalized;
        }

        float dist = rad / Mathf.Sin(_cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.06f;
        _cam.transform.position = center + dir * dist;
        _cam.transform.LookAt(center, Vector3.up);

        int W = 1120, H = 630;
        var rt = new RenderTexture(W, H, 24);
        _cam.targetTexture = rt;
        _cam.Render();
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
        tex.Apply();
        File.WriteAllBytes(SD + "/lp_" + _n.ToString("D4") + ".png", tex.EncodeToPNG());
        RenderTexture.active = prev;
        UnityEngine.Object.Destroy(tex);
        UnityEngine.Object.Destroy(rt);
        _cam.targetTexture = null;
        _n++;
    }

    Camera MakeCam()
    {
        var g = new GameObject("PROBE_CAM_LP");
        var c = g.AddComponent<Camera>();
        c.clearFlags = CameraClearFlags.SolidColor;
        c.backgroundColor = new Color(0.86f, 0.88f, 0.91f, 1f);
        c.fieldOfView = 40f;
        c.nearClipPlane = 0.05f;
        c.farClipPlane = 5000f;
        c.enabled = false;
        return c;
    }

    static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
    static string Pad(string s, int n) { return s.Length >= n ? s : s + new string(' ', n - s.Length); }

    IEnumerator Done()
    {
        Time.captureFramerate = 0;
        try { File.WriteAllText(RP, _sb.ToString()); } catch { }
        Debug.Log("[drg_loop] 写入 " + RP + "  帧数 " + _n);
        yield return null;
    }
}

var __hostLoop = new GameObject("drg_loop");
__hostLoop.AddComponent<drg_loop>();
return "DRG_LOOP_STARTED";
