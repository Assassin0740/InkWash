// r6_ground2.cs —— 地面纹样取证 v3：**实机视角** + 分材质分层 + 分屏带统计
//
// 前两版的教训：
//   v1 改错了材质（区域里主要是中央高台，我却只改 Ground）⇒ 全部读数逐位相同
//   v2 补了正向对照，但机位是我自己编的低掠射角，看到的是"远处地面发灰"，
//      并不是用户截图里那块"带斜向斜纹的暗色板"
// ⇒ v3 直接用**实机相机**（复制 Camera.main 的位姿），把角色放到高台边，
//   看到的就与该帧一致；并且**逐材质**做分层开关，能分清是地面还是高台贡献的。
using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using UnityEngine;

public class R6Ground
{
    static StringBuilder _sb;
    static void L(string s) { _sb.AppendLine(s); }

    static Camera _cam;
    static Material _matG, _matA, _matW;   // 地面 / 高台 / 院墙
    static readonly string[] Props = { "_GrainAmp", "_StrokeAmp", "_InkMottle", "_BrushStrength",
                                       "_StrokeStretch", "_StrokeScale", "_GrainScale", "_BandBias",
                                       "_InkDensity", "_AerialStrength" };
    static readonly Dictionary<string, float> _sv = new Dictionary<string, float>();
    static float _ss = 1f;

    static void Save(string n, float v) { if (!_sv.ContainsKey(n) && v == v) _sv[n] = v; }
    static void Snapshot()
    {
        foreach (var r in UnityEngine.Object.FindObjectsOfType<MeshRenderer>())
        {
            if (r.name == "Ground") _matG = r.sharedMaterial;
            if (r.name == "Arena") _matA = r.sharedMaterial;
            if (r.name == "Wall_N") _matW = r.sharedMaterial;
        }
        foreach (var m in new[] { _matG, _matA, _matW })
        {
            if (m == null) continue;
            foreach (var p in Props) if (m.HasProperty(p)) Save(m.name + "|" + p, m.GetFloat(p));
        }
    }
    static void SetMat(Material m, string p, float v) { if (m != null && m.HasProperty(p)) m.SetFloat(p, v); }
    static void RestoreAll()
    {
        foreach (var m in new[] { _matG, _matA, _matW })
        {
            if (m == null) continue;
            foreach (var p in Props)
            {
                string k = m.name + "|" + p;
                if (_sv.ContainsKey(k)) m.SetFloat(p, _sv[k]);
            }
        }
    }

    static Texture2D RenderTex(int w, int h)
    {
        var rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32);
        var prev = _cam.targetTexture;
        _cam.targetTexture = rt; _cam.Render(); _cam.targetTexture = prev;
        var pa = RenderTexture.active; RenderTexture.active = rt;
        var t = new Texture2D(w, h, TextureFormat.RGBA32, false);
        t.ReadPixels(new Rect(0, 0, w, h), 0, 0); t.Apply();
        RenderTexture.active = pa; RenderTexture.ReleaseTemporary(rt);
        return t;
    }
    static Texture2D DownN(Texture2D hi, int f)
    {
        int w = hi.width / f, h = hi.height / f;
        var lo = new Texture2D(w, h, TextureFormat.RGBA32, false);
        var hc = hi.GetPixels(); var lc = new Color[w * h];
        int hw = hi.width;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                Color s = Color.black;
                for (int j = 0; j < f; j++)
                    for (int i = 0; i < f; i++)
                        s += hc[(y * f + j) * hw + x * f + i];
                lc[y * w + x] = s / (f * f);
            }
        lo.SetPixels(lc); lo.Apply(); return lo;
    }

    /// 区域统计：局部对比度（细纹理）、8× 降采样后的 std（**带/moiré**，低频条带）、亮度均值、亮度std
    static void Stats(Texture2D t, float yf, float yt,
                      out float contrast, out float bandStd, out float mean, out float std)
    {
        var px = t.GetPixels(); int W = t.width, H = t.height;
        var lum = new float[W * H];
        for (int i = 0; i < W * H; i++) lum[i] = 0.2126f * px[i].r + 0.7152f * px[i].g + 0.0722f * px[i].b;

        int y0 = Mathf.Clamp((int)(H * yf), 1, H - 2), y1 = Mathf.Clamp((int)(H * yt), y0 + 1, H - 1);
        double s = 0, m = 0, sq = 0; int n = 0;
        for (int y = y0; y < y1; y++)
            for (int x = 1; x < W - 1; x++)
            {
                float c0 = lum[y * W + x];
                s += Mathf.Max(Mathf.Abs(lum[y * W + x + 1] - c0), Mathf.Abs(lum[(y + 1) * W + x] - c0));
                n++; m += c0; sq += (double)c0 * c0;
            }
        contrast = n > 0 ? (float)(s / n) : 0f;
        mean = n > 0 ? (float)(m / n) : 0f;
        std = n > 0 ? Mathf.Sqrt(Mathf.Max(0f, (float)(sq / n - (double)mean * mean))) : 0f;

        // 8× 盒式降采样 → 低频条带
        var low = DownN(t, 8);
        var lp = low.GetPixels(); int LW = low.width, LH = low.height;
        int ly0 = Mathf.Clamp((int)(LH * yf), 1, LH - 2), ly1 = Mathf.Clamp((int)(LH * yt), ly0 + 1, LH - 1);
        double lm = 0, lq = 0; int ln = 0;
        for (int y = ly0; y < ly1; y++)
            for (int x = 1; x < LW - 1; x++)
            {
                float v = 0.2126f * lp[y * LW + x].r + 0.7152f * lp[y * LW + x].g + 0.0722f * lp[y * LW + x].b;
                lm += v; lq += (double)v * v; ln++;
            }
        float lmean = ln > 0 ? (float)(lm / ln) : 0f;
        bandStd = ln > 0 ? Mathf.Sqrt(Mathf.Max(0f, (float)(lq / ln - (double)lmean * lmean))) : 0f;
        UnityEngine.Object.Destroy(low);
    }

    static void SavePng(string name, Texture2D t)
    {
        string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../Tools/screenshots/ground"));
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, name + ".png"), t.EncodeToPNG());
    }

    public static string Run()
    {
        _sb = new StringBuilder();
        L("================ r6_ground2 v3 ================");

        var live = Camera.main;
        var go = new GameObject("RT_GroundCam3");
        _cam = go.AddComponent<Camera>();
        if (live != null)
        {
            _cam.fieldOfView = live.fieldOfView;
            _cam.nearClipPlane = live.nearClipPlane;
            _cam.farClipPlane = live.farClipPlane;
            _cam.cullingMask = live.cullingMask;
        }
        _cam.clearFlags = CameraClearFlags.Skybox;
        _cam.enabled = false;

        Snapshot();
        L("材质：Ground=" + (_matG != null ? _matG.name + "#" + _matG.GetInstanceID() : "null")
          + "  Arena=" + (_matA != null ? _matA.name + "#" + _matA.GetInstanceID() : "null")
          + "  Wall=" + (_matW != null ? _matW.name : "null"));

        // 把角色放到高台边上（用户截图里就是站在台边）
        var pc = UnityEngine.Object.FindObjectOfType<InkWash.Player.PlayerController>();
        if (pc != null)
        {
            var cc = pc.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            pc.transform.position = new Vector3(0f, 0.05f, -2.6f);
            pc.transform.rotation = Quaternion.Euler(0f, 0f, 0f);
            if (cc != null) cc.enabled = true;
        }

        var poses = new (string tag, Vector3 pos, Vector3 look)[]
        {
            ("P1_实机近", new Vector3(0f, 3.30f, -8.6f), new Vector3(0f, 0.55f, -1.4f)),
            ("P2_俯视",   new Vector3(0f, 5.60f, -7.4f), new Vector3(0f, 0.20f, 0.6f)),
        };

        L("");
        L("---------- ① 正向对照 ----------");
        _ss = 1f;
        _cam.transform.position = poses[0].pos; _cam.transform.LookAt(poses[0].look);
        float c, bs, mn, sd; Texture2D t;
        RenderMeasure(960, 540, out c, out bs, out mn, out sd, out t);
        SavePng("v3_ctl_base", t); float b0 = mn; UnityEngine.Object.Destroy(t);
        SetMat(_matA, "_BandBias", -0.40f);
        RenderMeasure(960, 540, out c, out bs, out mn, out sd, out t);
        SavePng("v3_ctl_bias", t); UnityEngine.Object.Destroy(t);
        bool ok = Mathf.Abs(mn - b0) > 0.03f;
        L("  基线均值=" + b0.ToString("F4") + "  高台 _BandBias=-0.4 后均值=" + mn.ToString("F4")
          + "   ⇒ " + (ok ? "探针能影响画面，读数有效" : "**影响不到画面，后续无效**"));
        RestoreAll();

        // ---------- ② 分材质 × 分层 ----------
        foreach (var pose in poses)
        {
            L("");
            L("---------- ② " + pose.tag + " 分材质分层（2× 超采样）----------");
            _ss = 2f;
            _cam.transform.position = pose.pos; _cam.transform.LookAt(pose.look);
            L("  " + string.Format("{0,-26} {1,10} {2,10} {3,10} {4,10}", "配置", "局部对比度", "低频带std", "亮度均值", "亮度std"));

            var cfgs = new (string tag, Action a)[]
            {
                ("00_基线",              () => { }),
                ("01_关jitter(全局)",    () => { SetMat(_matG, "_BrushStrength", 0f); SetMat(_matA, "_BrushStrength", 0f); SetMat(_matW, "_BrushStrength", 0f); }),
                ("02_关纸颗粒(全局)",    () => { RestoreAll(); SetMat(_matG, "_GrainAmp", 0f); SetMat(_matA, "_GrainAmp", 0f); SetMat(_matW, "_GrainAmp", 0f); }),
                ("03_关笔触(全局)",      () => { RestoreAll(); SetMat(_matG, "_StrokeAmp", 0f); SetMat(_matA, "_StrokeAmp", 0f); SetMat(_matW, "_StrokeAmp", 0f); }),
                ("04_关积墨(全局)",      () => { RestoreAll(); SetMat(_matG, "_InkMottle", 0f); SetMat(_matA, "_InkMottle", 0f); SetMat(_matW, "_InkMottle", 0f); }),
                ("05_全噪声关(全局)",    () => { RestoreAll(); SetMat(_matG, "_BrushStrength", 0f); SetMat(_matA, "_BrushStrength", 0f); SetMat(_matW, "_BrushStrength", 0f); SetMat(_matG, "_GrainAmp", 0f); SetMat(_matA, "_GrainAmp", 0f); SetMat(_matW, "_GrainAmp", 0f); SetMat(_matG, "_StrokeAmp", 0f); SetMat(_matA, "_StrokeAmp", 0f); SetMat(_matW, "_StrokeAmp", 0f); SetMat(_matG, "_InkMottle", 0f); SetMat(_matA, "_InkMottle", 0f); SetMat(_matW, "_InkMottle", 0f); }),
                ("06_只关高台噪声",      () => { RestoreAll(); SetMat(_matA, "_BrushStrength", 0f); SetMat(_matA, "_GrainAmp", 0f); SetMat(_matA, "_StrokeAmp", 0f); SetMat(_matA, "_InkMottle", 0f); }),
                ("07_只关地面噪声",      () => { RestoreAll(); SetMat(_matG, "_BrushStrength", 0f); SetMat(_matG, "_GrainAmp", 0f); SetMat(_matG, "_StrokeAmp", 0f); SetMat(_matG, "_InkMottle", 0f); }),
                ("08_关大气透视",        () => { RestoreAll(); SetMat(_matG, "_AerialStrength", 0f); SetMat(_matA, "_AerialStrength", 0f); SetMat(_matW, "_AerialStrength", 0f); }),
            };
            foreach (var cfg in cfgs)
            {
                cfg.a();
                RenderMeasure(960, 540, out c, out bs, out mn, out sd, out t);
                SavePng("v3_" + pose.tag + "_" + cfg.tag, t);
                L("  " + string.Format("{0,-26} {1,10:F5} {2,10:F5} {3,10:F4} {4,10:F4}", cfg.tag, c, bs, mn, sd));
                UnityEngine.Object.Destroy(t);
            }
            RestoreAll();
            // 一倍原生分辨率的大图（供肉眼核对）
            _ss = 1f;
            RenderMeasure(1280, 720, out c, out bs, out mn, out sd, out t);
            SavePng("v3_" + pose.tag + "_big", t); UnityEngine.Object.Destroy(t);
        }

        RestoreAll();
        UnityEngine.Object.Destroy(go);
        L("");
        L("================ end ================");
        string outp = Path.GetFullPath(Path.Combine(Application.dataPath, "../Tools/reports/r6_ground.txt"));
        File.WriteAllText(outp, _sb.ToString());
        return "written: " + outp + "  (" + _sb.Length + " chars)";
    }

    static void RenderMeasure(int w, int h, out float c, out float bs, out float mn, out float sd, out Texture2D t)
    {
        int ow = Mathf.RoundToInt(w * _ss), oh = Mathf.RoundToInt(h * _ss);
        var raw = RenderTex(ow, oh);
        t = _ss > 1.01f ? DownN(raw, 2) : raw;
        if (t != raw) UnityEngine.Object.Destroy(raw);
        Stats(t, 0.05f, 0.95f, out c, out bs, out mn, out sd);
    }
}

return R6Ground.Run();
