// r4_ground.cs —— 地面条纹取证（Play 模式）
//
// 用户反馈：「这个地板随着视角变动还是会这样」。要区分两件完全不同的事：
//   (A) 真的是世界空间里存在的图案（只是太显眼）—— 超采样后仍在，局部对比度不掉
//   (B) **走样**（欠采样摩尔纹）—— 超采样后消失，局部对比度大幅下降
// 再用"逐层关掉噪声"做归因，定位是哪一层贡献了条带。
//
// 手法要点（来自本项目既往踩坑）：
//   · 所有对照必须**同一帧内背靠背 Camera.Render()**，中间绝不 yield —— 一 yield 姿势/相机就差 1~2 px
//   · 度量用**局部对比度**（4 邻域一阶差分的均值），对斜向条带也敏感
//   · 统计区只取画面下方（相机专门对着地面）
using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using UnityEngine;

public class R4Ground
{
    static StringBuilder _sb;
    static void L(string s) { _sb.AppendLine(s); }

    static Camera _cam;
    static RenderTexture _rt;
    static Material _groundInst, _arenaInst;
    static readonly Dictionary<string, float> _saved = new Dictionary<string, float>();

    static void Save(Material m, string p)
    {
        if (m == null || _saved.ContainsKey(p)) return;
        if (m.HasProperty(p)) _saved[p] = m.GetFloat(p);
    }
    static void Restore(Material m, string p)
    {
        if (m == null || !_saved.ContainsKey(p)) return;
        m.SetFloat(p, _saved[p]);
    }

    static float _ss = 1f;   // 超采样倍率

    static Texture2D RenderAt(int srcW, int srcH, int outW, int outH)
    {
        var rt = RenderTexture.GetTemporary(outW, outH, 24, RenderTextureFormat.ARGB32);
        var prev = _cam.targetTexture;
        _cam.targetTexture = rt;
        _cam.Render();
        _cam.targetTexture = prev;

        var prevActive = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(outW, outH, TextureFormat.RGBA32, false);
        tex.ReadPixels(new Rect(0, 0, outW, outH), 0, 0);   // ★ 读整张；超采样靠"渲大再降采样"
        tex.Apply();
        RenderTexture.active = prevActive;
        RenderTexture.ReleaseTemporary(rt);
        return tex;
    }

    /// 把 2× 超采样图做 2×2 盒式降采样 —— 与原生渲染逐像素可比
    static Texture2D Downsample(Texture2D hi, int w, int h)
    {
        var lo = new Texture2D(w, h, TextureFormat.RGBA32, false);
        var hc = hi.GetPixels();
        var lc = new Color[w * h];
        int hw = hi.width;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i0 = (y * 2) * hw + x * 2;
                var a = hc[i0]; var b = hc[i0 + 1];
                int i1 = i0 + hw;
                var c = hc[i1]; var d = hc[i1 + 1];
                lc[y * w + x] = (a + b + c + d) * 0.25f;
            }
        lo.SetPixels(lc);
        lo.Apply();
        return lo;
    }

    /// 区域局部对比度：4 邻域一阶差分绝对值的均值（亮度假）；同时给 std
    static void LocalContrast(Texture2D t, float yFrom, float yTo, out float contrast, out float std, out float mean)
    {
        int w = t.width, h = t.height;
        int y0 = Mathf.Clamp((int)(h * yFrom), 1, h - 2);
        int y1 = Mathf.Clamp((int)(h * yTo), y0 + 1, h - 1);
        var px = t.GetPixels();
        double sum = 0.0; int n = 0; double s2 = 0.0;
        var lum = new float[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var c = px[y * w + x];
                lum[y * w + x] = 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;
            }
        double mSum = 0.0; int mN = 0;
        for (int y = y0; y < y1; y++)
            for (int x = 1; x < w - 1; x++)
            {
                float c0 = lum[y * w + x];
                float dx = Mathf.Abs(lum[y * w + x + 1] - c0);
                float dy = Mathf.Abs(lum[(y + 1) * w + x] - c0);
                sum += Mathf.Max(dx, dy); n++;
                mSum += c0; mN++;
            }
        contrast = n > 0 ? (float)(sum / n) : 0f;
        mean = mN > 0 ? (float)(mSum / mN) : 0f;
        for (int y = y0; y < y1; y++)
            for (int x = 1; x < w - 1; x++)
            {
                float d = lum[y * w + x] - mean; s2 += d * d;
            }
        std = n > 0 ? Mathf.Sqrt((float)(s2 / n)) : 0f;
    }

    static void Shot(string name, int srcW, int srcH, out float contrast, out float std, out float mean)
    {
        int outW = Mathf.RoundToInt(srcW * _ss);
        int outH = Mathf.RoundToInt(srcH * _ss);
        var raw = RenderAt(srcW, srcH, outW, outH);
        var tex = _ss > 1.01f ? Downsample(raw, srcW, srcH) : raw;
        LocalContrast(tex, 0.25f, 0.95f, out contrast, out std, out mean);
        string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../Tools/screenshots/ground"));
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, name + ".png"), tex.EncodeToPNG());
        UnityEngine.Object.Destroy(tex);
        if (tex != raw) UnityEngine.Object.Destroy(raw);
    }

    public static void Setup()
    {
        var src = Camera.main;
        var go = new GameObject("RT_GroundCam");
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
    }

    public static string Run()
    {
        _sb = new StringBuilder();
        L("================ r4_ground ================");
        Setup();
        // 找地面
        GameObject ground = null, arena = null;
        foreach (var r in UnityEngine.Object.FindObjectsOfType<MeshRenderer>())
        {
            if (r.name == "Ground") ground = r.gameObject;
            if (r.name == "Arena") arena = r.gameObject;
        }
        L("Ground=" + (ground != null ? "ok" : "缺失") + "  Arena=" + (arena != null ? "ok" : "缺失"));
        _groundInst = ground != null ? ground.GetComponent<MeshRenderer>().material : null;
        _arenaInst = arena != null ? arena.GetComponent<MeshRenderer>().material : null;

        string[] props = { "_GrainAmp", "_StrokeAmp", "_InkMottle", "_BrushStrength", "_StrokeStretch", "_StrokeScale", "_AerialStrength", "_BandBias" };
        foreach (var p in props) { Save(_groundInst, p); Save(_arenaInst, p); }

        // 参数面板可能把 _BandBias 等改过，先打印
        foreach (var p in props)
            L("  ground " + p + " = " + (_groundInst != null && _groundInst.HasProperty(p) ? _groundInst.GetFloat(p).ToString("F3") : "-"));
        L("");

        // ---- 相机：低掠射角对着地面（用户截图那类视角）----
        // 先关掉玩家/敌人，避免它们混进统计区
        var player = GameObject.Find("Player");
        bool playerWasActive = player != null && player.activeSelf;
        if (player != null) player.SetActive(false);

        _ss = 1f;
        var poses = new (string tag, Vector3 pos, Vector3 look)[]
        {
            ("A_low",  new Vector3(0f, 1.30f, -13f), new Vector3(0f, 0.2f, 4f)),    // 贴地掠射
            ("B_mid",  new Vector3(0f, 3.20f, -12f), new Vector3(0f, 0.2f, 5f)),    // 中等俯角
            ("C_high", new Vector3(2.5f, 6.50f, -9f), new Vector3(0f, 0.2f, 4f)),   // 高看
        };

        L("---------- ① 视角依赖 + 超采样对照（1× vs 2× 降采样）----------");
        L(string.Format("  {0,-26} {1,10} {2,10} {3,10}", "配置", "局部对比度", "亮度std", "亮度均值"));
        foreach (var p in poses)
        {
            _cam.transform.position = p.pos;
            _cam.transform.LookAt(p.look);
            _ss = 1f;
            float c1, s1, m1; Shot("nav_" + p.tag + "_1x", 960, 540, out c1, out s1, out m1);
            _ss = 2f;
            float c2, s2, m2; Shot("nav_" + p.tag + "_2x", 960, 540, out c2, out s2, out m2);
            L(string.Format("  {0,-26} {1,10:F5} {2,10:F4} {3,10:F4}", p.tag + " 1x", c1, s1, m1));
            L(string.Format("  {0,-26} {1,10:F5} {2,10:F4} {3,10:F4}", p.tag + " 2x↓", c2, s2, m2));
            L("      超采样后对比度变化 = " + ((c2 - c1) / Mathf.Max(c1, 1e-6f) * 100f).ToString("F1") + " %"
              + (c2 < c1 * 0.65f ? "   ★ 掉幅 >35% ⇒ 走样为主" : "   （掉幅不大 ⇒ 不是纯走样）"));
        }

        // ---- ② 逐层归因（固定 A_low 视角，2× 超采样，避免把走样算成层的功劳）----
        L("");
        L("---------- ② 噪声层归因（视角 A_low，2× 超采样）----------");
        _ss = 2f;
        _cam.transform.position = poses[0].pos;
        _cam.transform.LookAt(poses[0].look);

        var cfgs = new (string tag, Action apply)[]
        {
            ("00_基线",             () => { }),
            ("01_关jitter",         () => { _groundInst.SetFloat("_BrushStrength", 0f); }),
            ("02_关纸颗粒",         () => { _groundInst.SetFloat("_BrushStrength", _saved["_BrushStrength"]); _groundInst.SetFloat("_GrainAmp", 0f); }),
            ("03_关笔触",           () => { _groundInst.SetFloat("_BrushStrength", _saved["_BrushStrength"]); _groundInst.SetFloat("_GrainAmp", _saved["_GrainAmp"]); _groundInst.SetFloat("_StrokeAmp", 0f); }),
            ("04_关积墨",           () => { _groundInst.SetFloat("_StrokeAmp", _saved["_StrokeAmp"]); _groundInst.SetFloat("_InkMottle", 0f); }),
            ("05_笔触各向同性",     () => { _groundInst.SetFloat("_InkMottle", _saved["_InkMottle"]); _groundInst.SetFloat("_StrokeStretch", 1f); }),
            ("06_关大气透视",       () => { _groundInst.SetFloat("_StrokeStretch", _saved["_StrokeStretch"]); _groundInst.SetFloat("_AerialStrength", 0f); }),
            ("07_全关(只剩墨色)",   () => { _groundInst.SetFloat("_AerialStrength", _saved["_AerialStrength"]); _groundInst.SetFloat("_BrushStrength", 0f); _groundInst.SetFloat("_GrainAmp", 0f); _groundInst.SetFloat("_StrokeAmp", 0f); _groundInst.SetFloat("_InkMottle", 0f); }),
        };
        foreach (var cfg in cfgs)
        {
            cfg.apply();
            float c, s, m; Shot("lay_" + cfg.tag, 960, 540, out c, out s, out m);
            L(string.Format("  {0,-26} {1,10:F5} {2,10:F4} {3,10:F4}", cfg.tag, c, s, m));
        }
        foreach (var p in props) { Restore(_groundInst, p); Restore(_arenaInst, p); }

        if (player != null) player.SetActive(playerWasActive);

        L("");
        L("================ end ================");
        string outp = Path.GetFullPath(Path.Combine(Application.dataPath, "../Tools/reports/r4_ground.txt"));
        File.WriteAllText(outp, _sb.ToString());
        return "written: " + outp + "  (" + _sb.Length + " chars)";
    }

    public static string Cleanup()
    {
        if (_cam != null) UnityEngine.Object.Destroy(_cam.gameObject);
        if (_rt != null) { _rt.Release(); _rt = null; }
        return "cleaned";
    }
}

return R4Ground.Run() + " || " + R4Ground.Cleanup();
