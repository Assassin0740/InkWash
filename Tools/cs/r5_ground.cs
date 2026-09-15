// r5_ground.cs —— 地面条纹取证 v2（修正 v1 的三个缺陷）
//
// v1 的错：
//   ① 只改 Ground 的材质，但相机区域里主要是**中央高台 Arena**（10×10×0.3）⇒ 改了没反应
//   ② 没有**正向对照**：没有"改一个肯定可见的属性，看均值是否真的动"这一步，
//      于是"全部分层读数逐位相同"这件事没被当场抓出来
//   ③ `mr.material` 可能与运行时调参面板登记的实例不是同一个 ⇒ 改了个孤儿
//
// v2 补齐：
//   · 同时改 Ground + Arena + Wall（场景三大面）
//   · 正向对照：_BandBias 置 −0.4 ⇒ 亮度均值必须明显下降，否则整轮读数无效
//   · 打印材质 instanceID，确认 set 的目标与 renderer 绑的是同一份
using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using UnityEngine;

public class R5Ground
{
    static StringBuilder _sb;
    static void L(string s) { _sb.AppendLine(s); }

    static Camera _cam;
    static readonly List<Renderer> _targets = new List<Renderer>();
    static readonly List<Material> _mats = new List<Material>();
    static readonly List<string> _names = new List<string>();
    static readonly Dictionary<string, float> _saved = new Dictionary<string, float>();
    static float _ss = 1f;

    static void Snapshot()
    {
        _targets.Clear(); _mats.Clear(); _names.Clear();
        foreach (var r in UnityEngine.Object.FindObjectsOfType<MeshRenderer>())
        {
            if (r.name != "Ground" && r.name != "Arena" && !r.name.StartsWith("Wall")) continue;
            _targets.Add(r);
            _mats.Add(r.sharedMaterial);
            _names.Add(r.name);
        }
    }

    static void SaveAll()
    {
        for (int i = 0; i < _mats.Count; i++)
        {
            var m = _mats[i];
            if (m == null) continue;
            foreach (var p in Props)
            {
                string k = i + "|" + p;
                if (m.HasProperty(p) && !_saved.ContainsKey(k)) _saved[k] = m.GetFloat(p);
            }
        }
    }
    static void RestoreAll()
    {
        for (int i = 0; i < _mats.Count; i++)
        {
            var m = _mats[i];
            if (m == null) continue;
            foreach (var p in Props)
            {
                string k = i + "|" + p;
                if (_saved.ContainsKey(k)) m.SetFloat(p, _saved[k]);
            }
        }
    }
    static void SetAll(string p, float v)
    {
        for (int i = 0; i < _mats.Count; i++)
            if (_mats[i] != null && _mats[i].HasProperty(p)) _mats[i].SetFloat(p, v);
    }
    static void ResetAll() { RestoreAll(); }

    static readonly string[] Props = { "_GrainAmp", "_StrokeAmp", "_InkMottle", "_BrushStrength",
                                       "_StrokeStretch", "_StrokeScale", "_AerialStrength", "_BandBias",
                                       "_HeightFade", "_InkDensity" };

    // ---------------- 渲染与度量 ----------------

    static Texture2D RenderTex(int w, int h)
    {
        var rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32);
        var prev = _cam.targetTexture;
        _cam.targetTexture = rt;
        _cam.Render();
        _cam.targetTexture = prev;
        var pa = RenderTexture.active;
        RenderTexture.active = rt;
        var t = new Texture2D(w, h, TextureFormat.RGBA32, false);
        t.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        t.Apply();
        RenderTexture.active = pa;
        RenderTexture.ReleaseTemporary(rt);
        return t;
    }

    static Texture2D Down2(Texture2D hi, int w, int h)
    {
        var lo = new Texture2D(w, h, TextureFormat.RGBA32, false);
        var hc = hi.GetPixels();
        var lc = new Color[w * h];
        int hw = hi.width;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i0 = (y * 2) * hw + x * 2;
                lc[y * w + x] = (hc[i0] + hc[i0 + 1] + hc[i0 + hw] + hc[i0 + hw + 1]) * 0.25f;
            }
        lo.SetPixels(lc); lo.Apply();
        return lo;
    }

    /// 同一帧里渲一张，返回 (局部对比度, 行内标准差, 亮度均值, 亮度std)
    static void Measure(int w, int h, float yFrom, float yTo,
                        out float contrast, out float rowStd, out float mean, out float std, out Texture2D tex)
    {
        int ow = Mathf.RoundToInt(w * _ss), oh = Mathf.RoundToInt(h * _ss);
        var raw = RenderTex(ow, oh);
        tex = _ss > 1.01f ? Down2(raw, w, h) : raw;
        if (tex != raw) UnityEngine.Object.Destroy(raw);

        var px = tex.GetPixels();
        int W = tex.width, H = tex.height;
        var lum = new float[W * H];
        for (int i = 0; i < W * H; i++)
            lum[i] = 0.2126f * px[i].r + 0.7152f * px[i].g + 0.0722f * px[i].b;

        int y0 = Mathf.Clamp((int)(H * yFrom), 1, H - 2);
        int y1 = Mathf.Clamp((int)(H * yTo), y0 + 1, H - 1);
        double s = 0, rsum = 0, msum = 0, sq = 0; int n = 0, rows = 0;
        contrast = 0f; rowStd = 0f; mean = 0f; std = 0f;
        double rowStdAcc = 0;
        for (int y = y0; y < y1; y++)
        {
            double rowS = 0, rowM = 0; int rn = 0;
            for (int x = 1; x < W - 1; x++)
            {
                float c0 = lum[y * W + x];
                float dx = Mathf.Abs(lum[y * W + x + 1] - c0);
                float dy = Mathf.Abs(lum[(y + 1) * W + x] - c0);
                s += Mathf.Max(dx, dy); n++;
                msum += c0; sq += (double)c0 * c0;
                rowS += c0; rowM += c0 * c0; rn++;
            }
            double rm = rowS / Mathf.Max(rn, 1);
            rowStdAcc += Mathf.Sqrt(Mathf.Max(0f, (float)(rowM / Mathf.Max(rn, 1) - rm * rm)));
            rows++;
        }
        contrast = n > 0 ? (float)(s / n) : 0f;
        mean = n > 0 ? (float)(msum / n) : 0f;
        std = n > 0 ? Mathf.Sqrt(Mathf.Max(0f, (float)(sq / n - (double)mean * mean))) : 0f;
        rowStd = rows > 0 ? (float)(rowStdAcc / rows) : 0f;
    }

    static void Save(string name, Texture2D t)
    {
        string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../Tools/screenshots/ground"));
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, name + ".png"), t.EncodeToPNG());
    }

    public static string Run()
    {
        _sb = new StringBuilder();
        L("================ r5_ground v2 ================");

        var src = Camera.main;
        var go = new GameObject("RT_GroundCam2");
        _cam = go.AddComponent<Camera>();
        if (src != null)
        {
            _cam.fieldOfView = src.fieldOfView;
            _cam.nearClipPlane = src.nearClipPlane;
            _cam.farClipPlane = src.farClipPlane;
            _cam.cullingMask = src.cullingMask;
        }
        _cam.clearFlags = CameraClearFlags.Skybox;
        _cam.enabled = false;

        Snapshot();
        SaveAll();
        L("被观测的渲染器 " + _targets.Count + " 个：");
        for (int i = 0; i < _names.Count; i++)
            L("  " + _names[i].PadRight(10) + " mat=" + (_mats[i] != null ? _mats[i].name : "null")
              + " instanceID=" + (_mats[i] != null ? _mats[i].GetInstanceID() : 0)
              + " rendererMat=" + (_targets[i].sharedMaterial != null ? _targets[i].sharedMaterial.GetInstanceID() : 0)
              + (_mats[i] != null && _targets[i].sharedMaterial != null && _mats[i].GetInstanceID() == _targets[i].sharedMaterial.GetInstanceID() ? "  ✓同一份" : "  ✗不同"));
        for (int i = 0; i < _mats.Count; i++)
        {
            var m = _mats[i];
            if (m == null) continue;
            L("  " + _names[i].PadRight(10) + " bands=" + m.GetFloat("_Bands").ToString("F1")
              + " bias=" + m.GetFloat("_BandBias").ToString("F3")
              + " grain=" + m.GetFloat("_GrainAmp").ToString("F2")
              + " stroke=" + m.GetFloat("_StrokeAmp").ToString("F2")
              + " stretch=" + m.GetFloat("_StrokeStretch").ToString("F1")
              + " mottle=" + m.GetFloat("_InkMottle").ToString("F2"));
        }

        var player = GameObject.Find("Player");
        bool pWas = player != null && player.activeSelf;
        if (player != null) player.SetActive(false);

        // 两个机位：一个掠射看高台+地面，一个俯视看地面
        var poses = new (string tag, Vector3 pos, Vector3 look)[]
        {
            ("g_low",  new Vector3(0f, 1.35f, -12.5f), new Vector3(0f, 0.25f, 3f)),
            ("g_mid",  new Vector3(0f, 3.60f, -11.0f), new Vector3(0f, 0.30f, 4f)),
        };

        L("");
        L("---------- ① 正向对照：整轮读数有效吗 ----------");
        _ss = 1f;
        _cam.transform.position = poses[0].pos; _cam.transform.LookAt(poses[0].look);
        float c, rs, mn, sd; Texture2D t;
        Measure(960, 540, 0.02f, 0.62f, out c, out rs, out mn, out sd, out t);
        L("  基线                      对比度=" + c.ToString("F5") + "  行std=" + rs.ToString("F5") + "  均值=" + mn.ToString("F4"));
        float baseMean = mn, baseC = c, baseRS = rs;
        Save("ctl_base", t); UnityEngine.Object.Destroy(t);

        SetAll("_BandBias", -0.40f);
        Measure(960, 540, 0.02f, 0.62f, out c, out rs, out mn, out sd, out t);
        L("  正向对照 _BandBias=-0.40   对比度=" + c.ToString("F5") + "  行std=" + rs.ToString("F5") + "  均值=" + mn.ToString("F4")
          + "   Δ均值=" + (mn - baseMean).ToString("F4"));
        Save("ctl_biasdown", t); UnityEngine.Object.Destroy(t);
        bool ctlOK = Mathf.Abs(mn - baseMean) > 0.05f;
        L("  ⇒ 探针" + (ctlOK ? "**能**影响画面（读数有效）" : "**影响不到画面** —— 后面所有读数作废，先查材质实例"));
        ResetAll();

        // ---------- ② 分层归因 ----------
        L("");
        L("---------- ② 噪声层归因（2× 超采样，掠射机位 g_low）----------");
        _ss = 2f;
        var cfgs = new (string tag, Action act)[]
        {
            ("00_基线",            () => { }),
            ("01_关jitter",        () => SetAll("_BrushStrength", 0f)),
            ("02_关纸颗粒",        () => { ResetAll(); SetAll("_GrainAmp", 0f); }),
            ("03_关笔触",          () => { ResetAll(); SetAll("_StrokeAmp", 0f); }),
            ("04_关积墨",          () => { ResetAll(); SetAll("_InkMottle", 0f); }),
            ("05_笔触各向同性",    () => { ResetAll(); SetAll("_StrokeStretch", 1f); }),
            ("06_笔触尺度×0.35",   () => { ResetAll(); SetAll("_StrokeScale", 2.45f); }),
            ("07_全噪声关",        () => { ResetAll(); SetAll("_BrushStrength", 0f); SetAll("_GrainAmp", 0f); SetAll("_StrokeAmp", 0f); SetAll("_InkMottle", 0f); }),
        };
        var rows = new List<string>();
        foreach (var cfg in cfgs)
        {
            cfg.act();
            Measure(960, 540, 0.02f, 0.62f, out c, out rs, out mn, out sd, out t);
            Save("lay2_" + cfg.tag, t); UnityEngine.Object.Destroy(t);
            L("  " + cfg.tag.PadRight(20) + " 对比度=" + c.ToString("F5") + "  行std=" + rs.ToString("F5") + "  均值=" + mn.ToString("F4"));
        }
        ResetAll();

        // ---------- ③ 视角依赖 + 超采样 ----------
        L("");
        L("---------- ③ 超采样对照（1× vs 2×）----------");
        foreach (var p in poses)
        {
            _cam.transform.position = p.pos; _cam.transform.LookAt(p.look);
            _ss = 1f; Measure(960, 540, 0.02f, 0.62f, out c, out rs, out mn, out sd, out t);
            float c1 = c, rs1 = rs; Save("nav2_" + p.tag + "_1x", t); UnityEngine.Object.Destroy(t);
            _ss = 2f; Measure(960, 540, 0.02f, 0.62f, out c, out rs, out mn, out sd, out t);
            L("  " + p.tag + " 1x  对比度=" + c1.ToString("F5") + "  行std=" + rs1.ToString("F5"));
            L("  " + p.tag + " 2x  对比度=" + c.ToString("F5") + "  行std=" + rs.ToString("F5")
              + "   变化 " + ((c - c1) / Mathf.Max(c1, 1e-6f) * 100f).ToString("F1") + "% / "
              + ((rs - rs1) / Mathf.Max(rs1, 1e-6f) * 100f).ToString("F1") + "%");
            Save("nav2_" + p.tag + "_2x", t); UnityEngine.Object.Destroy(t);
        }

        ResetAll();
        if (player != null) player.SetActive(pWas);
        UnityEngine.Object.Destroy(go);

        L("");
        L("================ end ================");
        string outp = Path.GetFullPath(Path.Combine(Application.dataPath, "../Tools/reports/r5_ground.txt"));
        File.WriteAllText(outp, _sb.ToString());
        return "written: " + outp + "  (" + _sb.Length + " chars)";
    }
}

return R5Ground.Run();
