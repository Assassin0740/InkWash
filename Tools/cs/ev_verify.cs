using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

// ev_verify.cs —— 敌人「有色 + 红墨边」改造验收（同机位、同 tick 背靠背对照）
//
// 做法：逐个召唤演示场条目 → 运行时 clone 材质 →
//   ① 先用磁盘的新参数渲一帧（新）
//   ② 再把 clone 改回改造前的旧值渲一帧（旧）
//   同一机位、同一光照、同一 tick ⇒ 两帧唯一的差异就是材质参数。
// 判据：平均彩度（越大越有色）、纯黑占比（越小越好）、红边像素占比（R 显著高于 G/B）。
//
// ★ 旧值表是硬编码的，来自改造前对 .mat 的实测读取，用于做不可辩驳的对照。
public class ev_verify : MonoBehaviour
{
    const string ReportPath = "D:/Unity Project/InkWash/Tools/reports/ev_verify.txt";
    const string ShotDir    = "D:/Unity Project/InkWash/Tools/screenshots/enemies";
    const int DiffTh = 6;

    static readonly string[][] Objs =
    {
        new[] { "墨徒", "墨徒" },
        new[] { "墨偶", "墨偶" },
        new[] { "墨魇", "墨魇" },
        new[] { "墨山", "墨山" },
        new[] { "墨骨", "墨骨" },
        new[] { "墨龙", "墨龙" },
    };

    // 旧值（改造前实测）：_ChromaKeep, _BandBias, _OutlineWidth, r, g, b
    static readonly Dictionary<string, float[]> OLD = new Dictionary<string, float[]>
    {
        { "M_Ink_Enemy_MoShan",   new[] { 0.30f, -0.18f, 0.014f, 0.055f, 0.040f, 0.032f } },
        { "M_Ink_Enemy_MoGuai",   new[] { 0.28f, -0.16f, 0.014f, 0.058f, 0.042f, 0.034f } },
        { "M_Ink_Enemy_MoGu",     new[] { 0.22f, -0.24f, 0.014f, 0.050f, 0.038f, 0.030f } },
        { "M_Ink_Enemy_MoOu_0",   new[] { 0.12f, -0.42f, 0.020f, 0.055f, 0.035f, 0.028f } },
        { "M_Ink_Enemy_MoTu_0",   new[] { 0.12f, -0.42f, 0.020f, 0.055f, 0.035f, 0.028f } },
        { "M_Ink_Enemy_MoYan_0",  new[] { 0.12f, -0.42f, 0.020f, 0.055f, 0.035f, 0.028f } },
        { "M_Ink_Boss_Dragon",    new[] { 0.10f, -0.34f, 0.018f, 0.042f, 0.038f, 0.036f } },
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

        int count = (int)_pCount.GetValue(_sc, null);
        _sb.AppendLine("===== ev_verify —— 敌人「有色 + 红墨边」验收 =====");
        _sb.AppendLine("同机位 / 同光照 / 同 tick，唯一变量 = 材质参数（新 vs 旧）");
        _sb.AppendLine("判据：彩度↑、纯黑↓、红边像素（R-G>30 且 R-B>30）");
        _sb.AppendLine("改造内容：_ChromaKeep→0.45  _BandBias→+0.10  _OutlineWidth→0.018  _OutlineColor→(0.35,0.03,0.02)");
        _sb.AppendLine();

        foreach (var o in Objs)
        {
            string want = o[0], disp = o[1];
            int idx = -1;
            for (int i = 0; i < count; i++)
            {
                string lb = (string)_mLabel.Invoke(_sc, new object[] { i });
                if (lb.Contains(want)) { idx = i; break; }
            }
            if (idx < 0) { _sb.AppendLine("── " + disp + "：面板无此条目"); _sb.AppendLine(); continue; }

            _mPlay.Invoke(_sc, new object[] { idx });
            yield return new WaitForSeconds(2.5f);

            var actor = FindActor(idx);
            if (actor == null) { _sb.AppendLine("── " + disp + "：找不到演员"); _sb.AppendLine(); continue; }

            var rs = new List<Renderer>(actor.GetComponentsInChildren<Renderer>(true));
            var map = new Dictionary<Material, Material>();
            var srcName = new Dictionary<Material, string>();
            foreach (var r in rs)
            {
                var sm = r.sharedMaterial;
                if (sm == null) continue;
                Material inst;
                if (!map.ContainsKey(sm)) { inst = new Material(sm); inst.name = sm.name + "_EV"; map[sm] = inst; srcName[inst] = sm.name; }
                else inst = map[sm];
                r.sharedMaterial = inst;
            }
            if (map.Count == 0) { _sb.AppendLine("── " + disp + "：无材质"); _sb.AppendLine(); continue; }

            var b = Bounds0(actor);
            float halfH = Mathf.Max(b.extents.y, 0.05f);
            float dist = halfH / (0.50f * Mathf.Tan(_cam.fieldOfView * 0.5f * Mathf.Deg2Rad));

            _sb.AppendLine("── " + disp + "  idx=" + idx + "  渲染器=" + rs.Count + "  材质=" + map.Count + " 份");
            foreach (var kv in map)
                _sb.AppendLine("     材质 " + srcName[kv.Value].PadRight(24)
                               + "  _ChromaKeep=" + Fmt(kv.Value, "_ChromaKeep")
                               + "  _BandBias=" + Fmt(kv.Value, "_BandBias")
                               + "  _OLW=" + Fmt(kv.Value, "_OutlineWidth")
                               + "  _OutlineColor=" + ColFmt(kv.Value, "_OutlineColor"));

            // ① 新参数帧（磁盘现状）
            foreach (float deg in new float[] { 0f, 180f })
            {
                float r0 = deg * Mathf.Deg2Rad;
                Vector3 dir = new Vector3(Mathf.Sin(r0), 0.22f, Mathf.Cos(r0)).normalized;
                _cam.transform.position = b.center + dir * dist;
                _cam.transform.LookAt(b.center);
                Snap(disp, rs, "新", deg, deg == 0f);
                yield return null;
            }

            // ② 改回旧值后同机位再拍
            bool anyOld = false;
            foreach (var kv in map)
            {
                string nm = srcName[kv.Value];
                float[] v; if (!OLD.TryGetValue(nm, out v)) continue;
                anyOld = true;
                if (kv.Value.HasProperty("_ChromaKeep"))   kv.Value.SetFloat("_ChromaKeep", v[0]);
                if (kv.Value.HasProperty("_BandBias"))     kv.Value.SetFloat("_BandBias", v[1]);
                if (kv.Value.HasProperty("_OutlineWidth")) kv.Value.SetFloat("_OutlineWidth", v[2]);
                if (kv.Value.HasProperty("_OutlineColor")) kv.Value.SetColor("_OutlineColor", new Color(v[3], v[4], v[5], 1f));
            }
            if (!anyOld) _sb.AppendLine("     （无旧值可对照：该对象本轮未改）");

            foreach (float deg in new float[] { 0f, 180f })
            {
                float r0 = deg * Mathf.Deg2Rad;
                Vector3 dir = new Vector3(Mathf.Sin(r0), 0.22f, Mathf.Cos(r0)).normalized;
                _cam.transform.position = b.center + dir * dist;
                _cam.transform.LookAt(b.center);
                Snap(disp, rs, "旧", deg, deg == 0f);
                yield return null;
            }
            _sb.AppendLine();
        }

        yield return Done();
    }

    static string Fmt(Material m, string p) { return m.HasProperty(p) ? m.GetFloat(p).ToString("F3") : "<无>"; }
    static string ColFmt(Material m, string p)
    {
        if (!m.HasProperty(p)) return "<无>";
        var c = m.GetColor(p);
        return "(" + c.r.ToString("F3") + "," + c.g.ToString("F3") + "," + c.b.ToString("F3") + ")";
    }

    void Snap(string disp, List<Renderer> rs, string ver, float deg, bool saveImg)
    {
        var wasOn = new List<bool>(); var wasSh = new List<ShadowCastingMode>();
        foreach (var r in rs) { wasOn.Add(r.enabled); wasSh.Add(r.shadowCastingMode); }
        foreach (var r in rs) r.shadowCastingMode = ShadowCastingMode.Off;

        foreach (var r in rs) r.enabled = true;
        var texA = Render();
        foreach (var r in rs) r.enabled = false;
        var texB = Render();

        for (int k = 0; k < rs.Count; k++) { rs[k].enabled = wasOn[k]; rs[k].shadowCastingMode = wasSh[k]; }

        var pa = texA.GetPixels32(); var pb = texB.GetPixels32();
        long n = 0, sR = 0, sG = 0, sB = 0, sCh = 0, sLu = 0, black = 0, red = 0;
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
            if (R - G > 30 && R - B > 30) red++;
        }

        if (saveImg)
        {
            Directory.CreateDirectory(ShotDir);
            string fn = ShotDir + "/EVV_" + disp + "_" + ver + "_" + texA.width + "x" + texA.height + ".png";
            try { File.WriteAllBytes(fn, texA.EncodeToPNG()); } catch (Exception e) { _sb.AppendLine("     写图失败 " + e.Message); }
        }

        if (n == 0) { _sb.AppendLine("     [" + deg.ToString("F0") + "°/" + ver + "] 0 像素"); Destroy(texA); Destroy(texB); return; }

        _sb.AppendLine("     [" + deg.ToString("F0") + "°/" + ver + "] 像素 " + n.ToString().PadLeft(6)
                       + "  RGB(" + (sR / n).ToString().PadLeft(3) + "," + (sG / n).ToString().PadLeft(3) + "," + (sB / n).ToString().PadLeft(3) + ")"
                       + "  彩度 " + ((float)sCh / n).ToString("F1").PadLeft(5)
                       + "  亮度 " + (sLu / n).ToString().PadLeft(3)
                       + "  纯黑 " + (100f * black / n).ToString("F0").PadLeft(3) + "%"
                       + "  红边 " + (100f * red / n).ToString("F1").PadLeft(5) + "%");
        Destroy(texA); Destroy(texB);
    }

    Texture2D Render()
    {
        int w = Mathf.Clamp(Screen.width, 320, 1600);
        int h = Mathf.Clamp(Screen.height, 180, 900);
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
        GameObject best = null;
        foreach (var root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
            foreach (var tr in root.GetComponentsInChildren<Transform>(true))
            {
                if (tr.gameObject.name == "Showcase_Actor_" + i) return tr.gameObject;
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

var g = new GameObject("ev_verify");
g.AddComponent<ev_verify>();
return "EV_VERIFY_STARTED";
