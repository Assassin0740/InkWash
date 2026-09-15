// r9_after.cs —— 地面补丁后的验证出图 + 「纹理是否真的活了」的判据
//
// 判据（对比 r8 的失败读数）：
//   r8：关掉 `_InkMottle`，全画面最大变化 **0.0024**（积墨是死的）
//   r9：应当显著变大；同理关笔触/关纸颗粒也应有可测变化。
//   若仍≈0，说明噪声参数还是超出 Nyquist，补丁没生效。
using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using UnityEngine;

public class R9After
{
    static StringBuilder _sb;
    static void L(string s) { _sb.AppendLine(s); }
    static Camera _cam;
    static string _dir;

    static Material FindMat(string n)
    {
        foreach (var r in UnityEngine.Object.FindObjectsOfType<MeshRenderer>())
            if (r.name == n) return r.sharedMaterial;
        return null;
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
            for (int xx = 0; xx < w; xx++)
            {
                Color s = Color.black;
                for (int j = 0; j < f; j++) for (int i = 0; i < f; i++) s += hc[(y * f + j) * hw + xx * f + i];
                lc[y * w + xx] = s / (f * f);
            }
        lo.SetPixels(lc); lo.Apply(); return lo;
    }

    /// 局部对比度（相邻像素亮度差的均值）+ 低频带 std（破平涂的直接指标）
    static void Stat(Texture2D t, out float con, out float band, out float mean)
    {
        var px = t.GetPixels();
        int W = t.width, H = t.height;
        var lum = new float[W * H];
        for (int i = 0; i < W * H; i++) lum[i] = 0.2126f * px[i].r + 0.7152f * px[i].g + 0.0722f * px[i].b;
        double s = 0, m = 0; int n = 0;
        for (int y = 1; y < H - 1; y++)
            for (int x = 1; x < W - 1; x++)
            {
                float c0 = lum[y * W + x];
                s += Mathf.Max(Mathf.Abs(lum[y * W + x + 1] - c0), Mathf.Abs(lum[(y + 1) * W + x] - c0));
                n++; m += c0;
            }
        con = n > 0 ? (float)(s / n) : 0f;
        mean = n > 0 ? (float)(m / n) : 0f;

        var lp = DownN(t, 16);
        var lpx = lp.GetPixels();
        double lm = 0, lq = 0; int ln = 0;
        for (int i = 0; i < lpx.Length; i++)
        {
            float v = 0.2126f * lpx[i].r + 0.7152f * lpx[i].g + 0.0722f * lpx[i].b;
            lm += v; lq += (double)v * v; ln++;
        }
        float lmean = ln > 0 ? (float)(lm / ln) : 0f;
        band = ln > 0 ? Mathf.Sqrt(Mathf.Max(0f, (float)(lq / ln - (double)lmean * lmean))) : 0f;
        UnityEngine.Object.Destroy(lp);
    }

    static void SavePng(string n, Texture2D t)
    {
        File.WriteAllBytes(Path.Combine(_dir, n + ".png"), t.EncodeToPNG());
    }

    public static string Run()
    {
        _sb = new StringBuilder();
        L("================ r9_after ================");
        _dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../Tools/screenshots/ground"));
        Directory.CreateDirectory(_dir);

        var matG = FindMat("Ground");
        var matA = FindMat("Arena");
        var matW = FindMat("Wall_N");
        L("");
        L("---------- 参数复核（确认补丁真的落到运行时）----------");
        foreach (var m in new[] { matG, matA, matW })
        {
            if (m == null) continue;
            L("  " + m.name + "  _Bands=" + m.GetFloat("_Bands").ToString("F0")
              + " _BandBias=" + m.GetFloat("_BandBias").ToString("F3")
              + " _GrainScale=" + m.GetFloat("_GrainScale").ToString("F1")
              + " _GrainAmp=" + m.GetFloat("_GrainAmp").ToString("F3")
              + " _StrokeScale=" + m.GetFloat("_StrokeScale").ToString("F2")
              + " _StrokeAmp=" + m.GetFloat("_StrokeAmp").ToString("F3")
              + " _InkMottle=" + m.GetFloat("_InkMottle").ToString("F3")
              + " _MottleScale=" + m.GetFloat("_MottleScale").ToString("F2"));
        }

        var live = Camera.main;
        var go = new GameObject("RT_GroundCam6");
        _cam = go.AddComponent<Camera>();
        if (live != null)
        {
            _cam.fieldOfView = live.fieldOfView; _cam.nearClipPlane = live.nearClipPlane;
            _cam.farClipPlane = live.farClipPlane; _cam.cullingMask = live.cullingMask;
        }
        _cam.clearFlags = CameraClearFlags.Skybox; _cam.enabled = false;

        var pc = UnityEngine.Object.FindObjectOfType<InkWash.Player.PlayerController>();
        if (pc != null)
        {
            var cc = pc.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            pc.transform.position = new Vector3(0f, 0.05f, -2.6f);
            pc.transform.rotation = Quaternion.identity;
            if (cc != null) cc.enabled = true;
        }

        Light sun = null;
        foreach (var l in UnityEngine.Object.FindObjectsOfType<Light>())
            if (l.type == LightType.Directional && sun == null) sun = l;
        var svShadows = sun != null ? sun.shadows : LightShadows.None;

        var poses = new (string tag, Vector3 pos, Vector3 look)[]
        {
            ("P1_实机近", new Vector3(0f, 3.30f, -8.6f), new Vector3(0f, 0.55f, -1.4f)),
            ("P2_俯视",   new Vector3(0f, 5.60f, -7.4f), new Vector3(0f, 0.20f, 0.6f)),
            ("P3_掠射",   new Vector3(7.2f, 1.05f, -10.5f), new Vector3(0f, 0.55f, 0.2f)),
        };
        string[] tags = { "00_基线", "01_关方向光阴影", "02_关积墨", "03_关笔触", "04_关纸颗粒" };

        var svMotG = matG != null ? matG.GetFloat("_InkMottle") : 0f;
        var svMotA = matA != null ? matA.GetFloat("_InkMottle") : 0f;
        var svMotW = matW != null ? matW.GetFloat("_InkMottle") : 0f;
        var svStrG = matG != null ? matG.GetFloat("_StrokeAmp") : 0f;
        var svStrA = matA != null ? matA.GetFloat("_StrokeAmp") : 0f;
        var svStrW = matW != null ? matW.GetFloat("_StrokeAmp") : 0f;
        var svGrnG = matG != null ? matG.GetFloat("_GrainAmp") : 0f;
        var svGrnA = matA != null ? matA.GetFloat("_GrainAmp") : 0f;
        var svGrnW = matW != null ? matW.GetFloat("_GrainAmp") : 0f;

        foreach (var pose in poses)
        {
            L("");
            L("---------- " + pose.tag + " ----------");
            _cam.transform.position = pose.pos; _cam.transform.LookAt(pose.look);
            float baseCon = 0, baseBand = 0;
            for (int k = 0; k < tags.Length; k++)
            {
                if (sun != null) sun.shadows = svShadows;
                if (matG != null) { matG.SetFloat("_InkMottle", svMotG); matG.SetFloat("_StrokeAmp", svStrG); matG.SetFloat("_GrainAmp", svGrnG); }
                if (matA != null) { matA.SetFloat("_InkMottle", svMotA); matA.SetFloat("_StrokeAmp", svStrA); matA.SetFloat("_GrainAmp", svGrnA); }
                if (matW != null) { matW.SetFloat("_InkMottle", svMotW); matW.SetFloat("_StrokeAmp", svStrW); matW.SetFloat("_GrainAmp", svGrnW); }

                if (k == 1 && sun != null) sun.shadows = LightShadows.None;
                if (k == 2) { matG?.SetFloat("_InkMottle", 0f); matA?.SetFloat("_InkMottle", 0f); matW?.SetFloat("_InkMottle", 0f); }
                if (k == 3) { matG?.SetFloat("_StrokeAmp", 0f); matA?.SetFloat("_StrokeAmp", 0f); matW?.SetFloat("_StrokeAmp", 0f); }
                if (k == 4) { matG?.SetFloat("_GrainAmp", 0f); matA?.SetFloat("_GrainAmp", 0f); matW?.SetFloat("_GrainAmp", 0f); }

                var raw = RenderTex(1920, 1080);
                var t = DownN(raw, 2); UnityEngine.Object.Destroy(raw);
                SavePng("v6_" + pose.tag + "_" + tags[k], t);
                float con, band, mean; Stat(t, out con, out band, out mean);
                UnityEngine.Object.Destroy(t);
                if (k == 0) { baseCon = con; baseBand = band; }
                L("  " + tags[k] + "  对比度=" + con.ToString("F5")
                  + "  低频带std=" + band.ToString("F5") + "  均值=" + mean.ToString("F4")
                  + (k == 0 ? "" : ("   Δ对比度=" + (con - baseCon).ToString("+0.00000;-0.00000")
                                   + "  Δ低频带std=" + (band - baseBand).ToString("+0.00000;-0.00000"))));
            }
        }
        if (sun != null) sun.shadows = svShadows;
        if (matG != null) { matG.SetFloat("_InkMottle", svMotG); matG.SetFloat("_StrokeAmp", svStrG); matG.SetFloat("_GrainAmp", svGrnG); }
        if (matA != null) { matA.SetFloat("_InkMottle", svMotA); matA.SetFloat("_StrokeAmp", svStrA); matA.SetFloat("_GrainAmp", svGrnA); }
        if (matW != null) { matW.SetFloat("_InkMottle", svMotW); matW.SetFloat("_StrokeAmp", svStrW); matW.SetFloat("_GrainAmp", svGrnW); }

        UnityEngine.Object.Destroy(go);
        L("");
        L("================ end ================");
        string outp = Path.GetFullPath(Path.Combine(Application.dataPath, "../Tools/reports/r9_after.txt"));
        File.WriteAllText(outp, _sb.ToString());
        return "written: " + outp + "  (" + _sb.Length + " chars)";
    }
}

return R9After.Run();
