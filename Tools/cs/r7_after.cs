// r7_after.cs —— 修复后复测：逐条带对比度剖面（最能反映"阴影里一片毛、受光面干净"）
//
// 为什么加"逐条带剖面"：整体对比度会把两种病混在一起 ——
//   修复前 阴影带对比度 >> 受光带（两块地板像两种材质）；
//   修复后 两者应当接近，剖面变平。单一均值看不出这件事。
using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using UnityEngine;

public class R7After
{
    static StringBuilder _sb;
    static void L(string s) { _sb.AppendLine(s); }

    static Camera _cam;
    static Material _matG, _matA, _matW;
    static readonly string[] Props = { "_GrainAmp", "_StrokeAmp", "_InkMottle", "_BrushStrength",
                                       "_StrokeStretch", "_StrokeScale", "_GrainScale", "_BandBias" };
    static readonly Dictionary<string, float> _sv = new Dictionary<string, float>();
    static float _ss = 1f;

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
            foreach (var p in Props) if (m.HasProperty(p) && !_sv.ContainsKey(m.name + "|" + p)) _sv[m.name + "|" + p] = m.GetFloat(p);
        }
    }
    static void SetMat(Material m, string p, float v) { if (m != null && m.HasProperty(p)) m.SetFloat(p, v); }
    static void RestoreAll()
    {
        foreach (var m in new[] { _matG, _matA, _matW })
        {
            if (m == null) continue;
            foreach (var p in Props) { string k = m.name + "|" + p; if (_sv.ContainsKey(k)) m.SetFloat(p, _sv[k]); }
        }
    }

    static Texture2D RenderTex(int w, int h)
    {
        var rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32);
        var prev = _cam.targetTexture; _cam.targetTexture = rt; _cam.Render(); _cam.targetTexture = prev;
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
        var hc = hi.GetPixels(); var lc = new Color[w * h]; int hw = hi.width;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                Color s = Color.black;
                for (int j = 0; j < f; j++) for (int i = 0; i < f; i++) s += hc[(y * f + j) * hw + x * f + i];
                lc[y * w + x] = s / (f * f);
            }
        lo.SetPixels(lc); lo.Apply(); return lo;
    }

    /// 逐条带剖面：8 条横带，每条给 局部对比度 / 低频带std / 亮度均值
    static void Profile(Texture2D t, out float[] con, out float[] band, out float[] mean, out float oc, out float omean)
    {
        int NB = 8;
        con = new float[NB]; band = new float[NB]; mean = new float[NB];
        var lp = DownN(t, 8);
        var px = t.GetPixels(); var lpx = lp.GetPixels();
        int W = t.width, H = t.height, LW = lp.width, LH = lp.height;
        var lum = new float[W * H];
        for (int i = 0; i < W * H; i++) lum[i] = 0.2126f * px[i].r + 0.7152f * px[i].g + 0.0722f * px[i].b;
        double allS = 0; int allN = 0; double allM = 0;
        for (int b = 0; b < NB; b++)
        {
            int y0 = Mathf.Clamp((int)(H * (b / (float)NB)) + 1, 1, H - 2);
            int y1 = Mathf.Clamp((int)(H * ((b + 1) / (float)NB)), y0 + 1, H - 1);
            double s = 0, m = 0; int n = 0;
            for (int y = y0; y < y1; y++)
                for (int x = 1; x < W - 1; x++)
                {
                    float c0 = lum[y * W + x];
                    s += Mathf.Max(Mathf.Abs(lum[y * W + x + 1] - c0), Mathf.Abs(lum[(y + 1) * W + x] - c0));
                    n++; m += c0;
                }
            con[b] = n > 0 ? (float)(s / n) : 0f;
            mean[b] = n > 0 ? (float)(m / n) : 0f;
            allS += s; allN += n; allM += m;

            int ly0 = Mathf.Clamp((int)(LH * (b / (float)NB)) + 1, 1, LH - 2);
            int ly1 = Mathf.Clamp((int)(LH * ((b + 1) / (float)NB)), ly0 + 1, LH - 1);
            double lm = 0, lq = 0; int ln = 0;
            for (int y = ly0; y < ly1; y++)
                for (int x = 1; x < LW - 1; x++)
                {
                    float v = 0.2126f * lpx[y * LW + x].r + 0.7152f * lpx[y * LW + x].g + 0.0722f * lpx[y * LW + x].b;
                    lm += v; lq += (double)v * v; ln++;
                }
            float lmean = ln > 0 ? (float)(lm / ln) : 0f;
            band[b] = ln > 0 ? Mathf.Sqrt(Mathf.Max(0f, (float)(lq / ln - (double)lmean * lmean))) : 0f;
        }
        UnityEngine.Object.Destroy(lp);
        oc = allN > 0 ? (float)(allS / allN) : 0f;
        omean = allN > 0 ? (float)(allM / allN) : 0f;
    }

    static void SavePng(string n, Texture2D t)
    {
        string d = Path.GetFullPath(Path.Combine(Application.dataPath, "../Tools/screenshots/ground"));
        Directory.CreateDirectory(d);
        File.WriteAllBytes(Path.Combine(d, n + ".png"), t.EncodeToPNG());
    }

    public static string Run()
    {
        _sb = new StringBuilder();
        L("================ r7_after ================");
        var live = Camera.main;
        var go = new GameObject("RT_GroundCam4");
        _cam = go.AddComponent<Camera>();
        if (live != null)
        {
            _cam.fieldOfView = live.fieldOfView; _cam.nearClipPlane = live.nearClipPlane;
            _cam.farClipPlane = live.farClipPlane; _cam.cullingMask = live.cullingMask;
        }
        _cam.clearFlags = CameraClearFlags.Skybox; _cam.enabled = false;
        Snapshot();

        var pc = UnityEngine.Object.FindObjectOfType<InkWash.Player.PlayerController>();
        if (pc != null)
        {
            var cc = pc.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            pc.transform.position = new Vector3(0f, 0.05f, -2.6f);
            pc.transform.rotation = Quaternion.identity;
            if (cc != null) cc.enabled = true;
        }
        var poses = new (string tag, Vector3 pos, Vector3 look)[]
        {
            ("P1_实机近", new Vector3(0f, 3.30f, -8.6f), new Vector3(0f, 0.55f, -1.4f)),
            ("P2_俯视",   new Vector3(0f, 5.60f, -7.4f), new Vector3(0f, 0.20f, 0.6f)),
        };

        foreach (var pose in poses)
        {
            L("");
            L("---------- " + pose.tag + " ----------");
            _cam.transform.position = pose.pos; _cam.transform.LookAt(pose.look);
            string[] tags = { "00_基线", "01_关纸颗粒", "02_关笔触", "03_关jitter", "04_全噪声关" };
            Action[] acts = {
                () => { },
                () => { SetMat(_matG,"_GrainAmp",0f); SetMat(_matA,"_GrainAmp",0f); SetMat(_matW,"_GrainAmp",0f); },
                () => { RestoreAll(); SetMat(_matG,"_StrokeAmp",0f); SetMat(_matA,"_StrokeAmp",0f); SetMat(_matW,"_StrokeAmp",0f); },
                () => { RestoreAll(); SetMat(_matG,"_BrushStrength",0f); SetMat(_matA,"_BrushStrength",0f); SetMat(_matW,"_BrushStrength",0f); },
                () => { RestoreAll(); SetMat(_matG,"_GrainAmp",0f); SetMat(_matA,"_GrainAmp",0f); SetMat(_matW,"_GrainAmp",0f);
                        SetMat(_matG,"_StrokeAmp",0f); SetMat(_matA,"_StrokeAmp",0f); SetMat(_matW,"_StrokeAmp",0f);
                        SetMat(_matG,"_BrushStrength",0f); SetMat(_matA,"_BrushStrength",0f); SetMat(_matW,"_BrushStrength",0f);
                        SetMat(_matG,"_InkMottle",0f); SetMat(_matA,"_InkMottle",0f); SetMat(_matW,"_InkMottle",0f); },
            };
            _ss = 2f;
            for (int k = 0; k < tags.Length; k++)
            {
                acts[k]();
                int w = 960, h = 540, ow = Mathf.RoundToInt(w * _ss);
                var raw = RenderTex(ow, h * 2);
                var t = DownN(raw, 2); UnityEngine.Object.Destroy(raw);
                float[] con, band, mean; float oc, om;
                Profile(t, out con, out band, out mean, out oc, out om);
                SavePng("v4_" + pose.tag + "_" + tags[k], t);
                UnityEngine.Object.Destroy(t);
                L("  " + tags[k] + "   整体对比度=" + oc.ToString("F5"));
                var sb2 = new StringBuilder("      逐带对比度(上→下): ");
                for (int b = 7; b >= 0; b--) sb2.Append(con[b].ToString("F5") + (b > 0 ? " " : ""));
                L(sb2.ToString());
                var sb3 = new StringBuilder("      逐带亮度  (上→下): ");
                for (int b = 7; b >= 0; b--) sb3.Append(mean[b].ToString("F3") + (b > 0 ? " " : ""));
                L(sb3.ToString());
                var sb4 = new StringBuilder("      逐带低频带std(上→下): ");
                for (int b = 7; b >= 0; b--) sb4.Append(band[b].ToString("F4") + (b > 0 ? " " : ""));
                L(sb4.ToString());
            }
            RestoreAll();
            // 4× 超采样对照（1× 的图上面已存）
            _ss = 4f;
            var raw4 = RenderTex(960 * 4, 540 * 4);
            var t4 = DownN(DownN(raw4, 2), 2); UnityEngine.Object.Destroy(raw4);
            float[] c4, b4, m4; float oc4, om4;
            Profile(t4, out c4, out b4, out m4, out oc4, out om4);
            SavePng("v4_" + pose.tag + "_4xSS", t4); UnityEngine.Object.Destroy(t4);
            L("  4×超采样 整体对比度=" + oc4.ToString("F5") + "（与上一轮 2× 基线比：掉幅大 ⇒ 仍有走样）");
            _ss = 1f;
        }

        RestoreAll();
        UnityEngine.Object.Destroy(go);
        L("");
        L("================ end ================");
        string outp = Path.GetFullPath(Path.Combine(Application.dataPath, "../Tools/reports/r7_after.txt"));
        File.WriteAllText(outp, _sb.ToString());
        return "written: " + outp + "  (" + _sb.Length + " chars)";
    }
}

return R7After.Run();
