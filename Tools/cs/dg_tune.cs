using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

// dg_tune.cs —— 敌人墨色参数扫描（给用户挑）
//
// 已定位（dg_color.cs）：敌人不是"没颜色"，是【背光面掉进焦墨档】：
//   墨山 受光 0°  : 平均 RGB(83,76,69) 彩度 14.1  纯黑 13%   ← 暖褐，正常
//   墨山 背光 180°: 平均 RGB(47,43,40) 彩度  8.0  纯黑 52%   ← 纯黑剪影
//   主角 4 方位   : 彩度 15.9~20.5                 纯黑 18~24%  ← 始终有色
// 磁盘参数差：主角 _ChromaKeep 0.98 / _BandBias +0.15；墨山 0.30 / -0.18
//   ⇒ 色彩注入量差 3.3 倍、明暗偏移差 0.33（4 阶量化下 1.3 阶）
//
// 本探针固定其余参数不动，只扫 (_ChromaKeep, _BandBias) 组合，
// 每个组合在【受光 0°】与【背光 180°】各拍一张，并报彩度与纯黑占比。
// ★ 运行时 clone 材质，不碰磁盘资产。
public class dg_tune : MonoBehaviour
{
    const string ReportPath = "D:/Unity Project/InkWash/Tools/reports/dg_tune.txt";
    const string ShotDir    = "D:/Unity Project/InkWash/Tools/screenshots/color";
    const int DiffTh = 6;

    static readonly object[][] Presets = new object[][]
    {
        new object[] { "T0_现状",       0.30f, -0.18f },
        new object[] { "T1_只抬彩度",   0.75f, -0.18f },
        new object[] { "T2_只抬墨阶",   0.30f,  0.06f },
        new object[] { "T3_两者都抬",   0.75f,  0.06f },
        new object[] { "T4_接近主角",   0.95f,  0.13f },
    };

    readonly StringBuilder _sb = new StringBuilder();
    Component _sc; Type _ty;
    MethodInfo _mPlay, _mLabel, _mStop;
    PropertyInfo _pCount;
    Camera _cam;
    Vector3 _camPos0, _camRot0; float _fov0;

    void Start() { StartCoroutine(Run()); }

    System.Collections.IEnumerator Run()
    {
        yield return null; yield return null;

        foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
        { var t = a.GetType("InkWash.DebugTools.ActionShowcase"); if (t != null) { _ty = t; break; } }
        if (_ty == null) { _sb.AppendLine("x 找不到 ActionShowcase"); yield return Done(); }
        foreach (var o in UnityEngine.Object.FindObjectsOfType(_ty, true)) { _sc = o as Component; if (_sc != null) break; }
        if (_sc == null) { _sb.AppendLine("x 场景里没有 ActionShowcase"); yield return Done(); }

        _mPlay  = _ty.GetMethod("PlayForCapture", BindingFlags.Public | BindingFlags.Instance);
        _mLabel = _ty.GetMethod("ItemLabel",     BindingFlags.Public | BindingFlags.Instance);
        _mStop  = _ty.GetMethod("StopAutoClose", BindingFlags.Public | BindingFlags.Instance);
        _pCount = _ty.GetProperty("ItemCount",   BindingFlags.Public | BindingFlags.Instance);
        _mStop.Invoke(_sc, null);

        _cam = Camera.main;
        if (_cam == null) { _sb.AppendLine("x Camera.main 为空"); yield return Done(); }
        _camPos0 = _cam.transform.position; _camRot0 = _cam.transform.eulerAngles; _fov0 = _cam.fieldOfView;

        _sb.AppendLine("===== dg_tune —— 敌人墨色参数扫描（墨山为样本）=====");
        _sb.AppendLine("固定其余参数，只扫 (_ChromaKeep, _BandBias)；每档在受光 0° 与背光 180° 各拍一张");
        _sb.AppendLine("运行时 clone 材质，不写磁盘。判据：平均彩度（越大越有色）、纯黑(RGB三通道均<8)占比（越小越好）");
        _sb.AppendLine();

        int count = (int)_pCount.GetValue(_sc, null);
        int idx = -1;
        for (int i = 0; i < count; i++)
        {
            string lb = (string)_mLabel.Invoke(_sc, new object[] { i });
            if (lb.Contains("墨山") && idx < 0) idx = i;
        }
        if (idx < 0) { _sb.AppendLine("x 面板里没有墨山条目"); yield return Done(); }

        _mPlay.Invoke(_sc, new object[] { idx });
        yield return new WaitForSeconds(2.5f);

        var actor = FindActor(idx);
        if (actor == null) { _sb.AppendLine("x 找不到墨山演员"); yield return Done(); }

        var rs = new List<Renderer>(actor.GetComponentsInChildren<Renderer>(true));
        _sb.AppendLine("演员 " + actor.name + "，渲染器 " + rs.Count + " 个");

        // clone 每个不同源材质一份，之后只改 clone
        var map = new Dictionary<Material, Material>();
        foreach (var r in rs)
        {
            var sm = r.sharedMaterial;
            if (sm == null) continue;
            Material inst;
            if (!map.ContainsKey(sm)) { inst = new Material(sm); inst.name = sm.name + "_TUNE"; map[sm] = inst; }
            else inst = map[sm];
            r.sharedMaterial = inst;
        }
        _sb.AppendLine("已 clone 材质 " + map.Count + " 份（不写磁盘）：");
        foreach (var kv in map)
            _sb.AppendLine("   源 " + kv.Key.name + "  shader=" + kv.Key.shader.name
                           + "  原 _ChromaKeep=" + Fmt(kv.Key, "_ChromaKeep") + " _BandBias=" + Fmt(kv.Key, "_BandBias"));
        _sb.AppendLine();

        var b = Bounds0(actor);
        _sb.AppendLine("包围盒 center=" + b.center.ToString("F2") + " size=" + b.size.ToString("F2"));
        _sb.AppendLine();

        // 固定取景（同一机位，所有档位可比）
        float halfH = Mathf.Max(b.extents.y, 0.05f);
        float dist = halfH / (0.50f * Mathf.Tan(_cam.fieldOfView * 0.5f * Mathf.Deg2Rad));

        foreach (var p in Presets)
        {
            string tag = (string)p[0];
            float ck = (float)p[1], bb = (float)p[2];
            foreach (var kv in map)
            {
                kv.Value.SetFloat("_ChromaKeep", ck);
                kv.Value.SetFloat("_BandBias", bb);
            }

            _sb.AppendLine("── " + tag + "   _ChromaKeep=" + ck.ToString("F2") + "  _BandBias=" + bb.ToString("F2"));

            foreach (float deg in new float[] { 0f, 180f })
            {
                float r = deg * Mathf.Deg2Rad;
                Vector3 dir = new Vector3(Mathf.Sin(r), 0.22f, Mathf.Cos(r)).normalized;
                _cam.transform.position = b.center + dir * dist;
                _cam.transform.LookAt(b.center);
                Snap(actor, rs, tag, deg);
            }
            _sb.AppendLine();
        }

        yield return Done();
    }

    static string Fmt(Material m, string p)
    { return m.HasProperty(p) ? m.GetFloat(p).ToString("F2") : "<无>"; }

    void Snap(GameObject go, List<Renderer> rs, string tag, float deg)
    {
        var wasOn = new List<bool>(); var wasSh = new List<ShadowCastingMode>();
        foreach (var r in rs) { wasOn.Add(r.enabled); wasSh.Add(r.shadowCastingMode); }
        foreach (var r in rs) r.shadowCastingMode = ShadowCastingMode.Off;

        foreach (var r in rs) r.enabled = true;
        var texA = Render();
        foreach (var r in rs) r.enabled = false;
        var texB = Render();

        for (int k = 0; k < rs.Count; k++) { rs[k].enabled = wasOn[k]; rs[k].shadowCastingMode = wasSh[k]; }

        int w = texA.width, h = texA.height;
        var pa = texA.GetPixels32(); var pb = texB.GetPixels32();
        long n = 0, sR = 0, sG = 0, sB = 0, sCh = 0, sLu = 0, black = 0;
        for (int k = 0; k < pa.Length; k++)
        {
            int d = Mathf.Max(Mathf.Abs(pa[k].r - pb[k].r), Mathf.Max(Mathf.Abs(pa[k].g - pb[k].g), Mathf.Abs(pa[k].b - pb[k].b)));
            if (d <= DiffTh) continue;
            n++;
            int R = pa[k].r, G = pa[k].g, B = pa[k].b;
            sR += R; sG += G; sB += B;
            sCh += Mathf.Max(R, Mathf.Max(G, B)) - Mathf.Min(R, Mathf.Min(G, B));
            sLu += (int)(0.2126f * R + 0.7152f * G + 0.0722f * B);
            if (R < 8 && G < 8 && B < 8) black++;
        }

        Directory.CreateDirectory(ShotDir);
        string fn = ShotDir + "/TN_" + tag + "_" + deg.ToString("F0") + ".png";
        try { File.WriteAllBytes(fn, texA.EncodeToPNG()); } catch (Exception e) { _sb.AppendLine("    写图失败 " + e.Message); }

        if (n == 0) { _sb.AppendLine("    [" + deg.ToString("F0") + "°] 0 像素"); Destroy(texA); Destroy(texB); return; }

        _sb.AppendLine("    [" + deg.ToString("F0") + "°] 像素 " + n
                       + "  平均色 RGB(" + (sR / n) + "," + (sG / n) + "," + (sB / n) + ")"
                       + "  彩度 " + ((float)sCh / n).ToString("F1")
                       + "  亮度 " + (sLu / n)
                       + "  纯黑占比 " + (100f * black / n).ToString("F0") + "%");
        Destroy(texA); Destroy(texB);
    }

    Texture2D Render()
    {
        int w = Mathf.Clamp(Screen.width, 320, 1280);
        int h = Mathf.Clamp(Screen.height, 180, 720);
        var rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32);
        var prev = _cam.targetTexture;
        _cam.targetTexture = rt;
        _cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        tex.Apply();
        _cam.targetTexture = prev;
        RenderTexture.active = null;
        RenderTexture.ReleaseTemporary(rt);
        return tex;
    }

    static GameObject FindActor(int i)
    {
        string want = "Showcase_Actor_" + i;
        GameObject best = null;
        foreach (var root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
            foreach (var tr in root.GetComponentsInChildren<Transform>(true))
            {
                if (tr.gameObject.name == want) return tr.gameObject;
                if (best == null && tr.gameObject.name.StartsWith("Showcase_Actor_")) best = tr.gameObject;
            }
        return best;
    }

    static Bounds Bounds0(GameObject go)
    {
        var rs = go.GetComponentsInChildren<Renderer>(true);
        bool has = false; Bounds b = new Bounds();
        foreach (var r in rs)
        {
            if (r == null) continue;
            if (!has) { b = r.bounds; has = true; } else b.Encapsulate(r.bounds);
        }
        return b;
    }

    System.Collections.IEnumerator Done()
    {
        yield return null;
        if (_cam != null)
        {
            _cam.transform.position = _camPos0;
            _cam.transform.eulerAngles = _camRot0;
            _cam.fieldOfView = _fov0;
        }
        string s = _sb.ToString();
        Debug.Log(s);
        try { File.WriteAllText(ReportPath, s); } catch { }
        enabled = false;
    }
}

var g = new GameObject("dg_tune");
g.AddComponent<dg_tune>();
return "DG_TUNE_STARTED";
