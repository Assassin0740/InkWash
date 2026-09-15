// r8_facts.cs —— 地面「灰面 / 斜灰带」定性 + 材质现状事实清点
//
// 目的（两个互斥假设，要一次分开）：
//   H1 灰块 = **墙投下的硬边阴影**        → 关方向光阴影后整块消失
//   H2 灰块 = **高台与地面的材质色差**    → 关阴影不变，改成与地面同参后消失
//
// 另外查 `_BrushTex` 到底有没有赋贴图。若为 null，SAMPLE_TEXTURE2D 返回白 1.0，
// 「积墨」层就不再是噪声，而退化成一次**均匀压暗**（看起来像"地面发灰"）。
using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using UnityEngine;

public class R8Facts
{
    static StringBuilder _sb;
    static void L(string s) { _sb.AppendLine(s); }
    static Camera _cam;

    const int BW = 16, BH = 10;   // 亮度网格：16 列 × 10 行

    static Material FindMat(string rndName)
    {
        foreach (var r in UnityEngine.Object.FindObjectsOfType<MeshRenderer>())
            if (r.name == rndName) return r.sharedMaterial;
        return null;
    }

    static void DumpMat(Material m)
    {
        if (m == null) { L("  (null)"); return; }
        L("  --- " + m.name + "   shader=" + m.shader.name + "  instanceID=" + m.GetInstanceID());
        var sh = m.shader;
        var f = new StringBuilder();
        for (int i = 0; i < sh.GetPropertyCount(); i++)
        {
            string pn = sh.GetPropertyName(i);
            var pt = sh.GetPropertyType(i);
            if (pt == UnityEngine.Rendering.ShaderPropertyType.Float ||
                pt == UnityEngine.Rendering.ShaderPropertyType.Range)
                f.Append(pn + "=" + m.GetFloat(pn).ToString("F4") + " ");
        }
        L("    floats: " + f.ToString());
        var x = new StringBuilder();
        for (int i = 0; i < sh.GetPropertyCount(); i++)
        {
            string pn = sh.GetPropertyName(i);
            if (sh.GetPropertyType(i) != UnityEngine.Rendering.ShaderPropertyType.Texture) continue;
            var t = m.GetTexture(pn);
            x.Append(pn + "=" + (t == null ? "**NULL**" : (t.name + "(" + t.width + "x" + t.height + ")")) + "  ");
        }
        L("    texs  : " + x.ToString());
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

    static float[] Grid(Texture2D t)
    {
        var px = t.GetPixels();
        var g = new float[BW * BH]; var n = new int[BW * BH];
        int W = t.width, H = t.height;
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                int ix = x * BW / W, iy = y * BH / H;
                float lum = 0.2126f * px[y * W + x].r + 0.7152f * px[y * W + x].g + 0.0722f * px[y * W + x].b;
                g[iy * BW + ix] += lum; n[iy * BW + ix]++;
            }
        for (int i = 0; i < g.Length; i++) g[i] /= Mathf.Max(1, n[i]);
        return g;
    }

    static void PrintGrid(string tag, float[] g)
    {
        L("  " + tag);
        for (int iy = BH - 1; iy >= 0; iy--)
        {
            var s = new StringBuilder("    ");
            for (int ix = 0; ix < BW; ix++) s.Append((g[iy * BW + ix]).ToString("F2") + (ix < BW - 1 ? " " : ""));
            L(s.ToString());
        }
    }

    static void PrintDelta(string tag, float[] a, float[] b)
    {
        // b - a，只在 |Δ| ≥ 0.04 的地方显示数字，其余显示 "."
        L("  " + tag);
        for (int iy = BH - 1; iy >= 0; iy--)
        {
            var s = new StringBuilder("    ");
            for (int ix = 0; ix < BW; ix++)
            {
                float d = b[iy * BW + ix] - a[iy * BW + ix];
                s.Append((Mathf.Abs(d) < 0.04f ? "  . " : d.ToString("+0.0;-0.0")) + (ix < BW - 1 ? " " : ""));
            }
            L(s.ToString());
        }
        int cnt = 0; float sum = 0, mx = 0;
        for (int i = 0; i < a.Length; i++) { float d = Mathf.Abs(b[i] - a[i]); if (d >= 0.04f) cnt++; sum += d; mx = Mathf.Max(mx, d); }
        L("    Δ超阈块数=" + cnt + "/" + a.Length + "  Δ均值=" + (sum / a.Length).ToString("F4") + "  Δ最大=" + mx.ToString("F4"));
    }

    static string _dir;
    static void SavePng(string n, Texture2D t)
    {
        File.WriteAllBytes(Path.Combine(_dir, n + ".png"), t.EncodeToPNG());
    }

    public static string Run()
    {
        _sb = new StringBuilder();
        L("================ r8_facts ================");
        _dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../Tools/screenshots/ground"));
        Directory.CreateDirectory(_dir);

        var matG = FindMat("Ground");
        var matA = FindMat("Arena");
        var matW = FindMat("Wall_N");
        var matP = FindMat("Pillar_C");     // 可能不存在，容错
        L("");
        L("---------- ① 材质现状（运行时实例） ----------");
        L(" [Ground]");  DumpMat(matG);
        L(" [Arena]");   DumpMat(matA);
        L(" [Wall_N]");  DumpMat(matW);
        if (matP != null) { L(" [Pillar_C]"); DumpMat(matP); }

        // 场景里所有白盒渲染器 → 材质映射，确认没有"漏网"的材质
        L("");
        L("---------- ② 白盒渲染器 → 材质 ----------");
        var seen = new Dictionary<int, string>();
        foreach (var r in UnityEngine.Object.FindObjectsOfType<MeshRenderer>())
        {
            var m = r.sharedMaterial;
            if (m == null) { L("  " + r.name + " → (no material)"); continue; }
            if (!m.shader.name.Contains("Ink")) { L("  " + r.name + " → " + m.shader.name + " / " + m.name + "  ** 非水墨 **"); continue; }
            if (!seen.ContainsKey(m.GetInstanceID()))
            {
                seen[m.GetInstanceID()] = r.name;
                L("  " + r.name + " → " + m.name + "   (_Bands=" + m.GetFloat("_Bands").ToString("F0")
                  + " _BandBias=" + m.GetFloat("_BandBias").ToString("F3") + ")");
            }
        }

        // ---- 相机 + 角色就位 ----
        var live = Camera.main;
        var go = new GameObject("RT_GroundCam5");
        _cam = go.AddComponent<Camera>();
        if (live != null)
        {
            _cam.fieldOfView = live.fieldOfView; _cam.nearClipPlane = live.nearClipPlane;
            _cam.farClipPlane = live.farClipPlane; _cam.cullingMask = live.cullingMask;
        }
        _cam.clearFlags = CameraClearFlags.Skybox; _cam.enabled = false;
        _cam.transform.position = new Vector3(0f, 3.30f, -8.6f);
        _cam.transform.LookAt(new Vector3(0f, 0.55f, -1.4f));

        var pc = UnityEngine.Object.FindObjectOfType<InkWash.Player.PlayerController>();
        if (pc != null)
        {
            var cc = pc.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            pc.transform.position = new Vector3(0f, 0.05f, -2.6f);
            pc.transform.rotation = Quaternion.identity;
            if (cc != null) cc.enabled = true;
        }

        var lights = new List<Light>();
        Light sun = null;
        foreach (var l in UnityEngine.Object.FindObjectsOfType<Light>())
            if (l.type == LightType.Directional) { lights.Add(l); if (sun == null) sun = l; }
        L("");
        L("---------- ③ 方向光 ----------");
        if (sun == null) L("  没找到方向光");
        else L("  " + sun.name + "  shadows=" + sun.shadows + "  intensity=" + sun.intensity.ToString("F3")
               + "  shadowStrength=" + sun.shadowStrength.ToString("F3") + "  rot=" + sun.transform.eulerAngles.ToString("F1"));

        string[] tags = { "00_基线", "01_关方向光阴影", "02_高台同参", "03_关积墨", "04_墙同参" };
        float[][] grids = new float[tags.Length][];
        var svBiasA = matA != null ? matA.GetFloat("_BandBias") : 0f;
        var svMotA = matA != null ? matA.GetFloat("_InkMottle") : 0f;
        var svGrnA = matA != null ? matA.GetFloat("_GrainAmp") : 0f;
        var svShadows = sun != null ? sun.shadows : LightShadows.None;
        var svMotG = matG != null ? matG.GetFloat("_InkMottle") : 0f;
        var svMotW = matW != null ? matW.GetFloat("_InkMottle") : 0f;
        var svBiasW = matW != null ? matW.GetFloat("_BandBias") : 0f;

        for (int k = 0; k < tags.Length; k++)
        {
            // 每个变体前先复位
            if (sun != null) sun.shadows = svShadows;
            if (matA != null) { matA.SetFloat("_BandBias", svBiasA); matA.SetFloat("_InkMottle", svMotA); matA.SetFloat("_GrainAmp", svGrnA); }
            if (matG != null) matG.SetFloat("_InkMottle", svMotG);
            if (matW != null) { matW.SetFloat("_InkMottle", svMotW); matW.SetFloat("_BandBias", svBiasW); }

            if (k == 1 && sun != null) sun.shadows = LightShadows.None;
            if (k == 2 && matA != null && matG != null)
            { matA.SetFloat("_BandBias", matG.GetFloat("_BandBias")); matA.SetFloat("_InkMottle", matG.GetFloat("_InkMottle")); matA.SetFloat("_GrainAmp", matG.GetFloat("_GrainAmp")); }
            if (k == 3) { if (matG != null) matG.SetFloat("_InkMottle", 0f); if (matA != null) matA.SetFloat("_InkMottle", 0f); if (matW != null) matW.SetFloat("_InkMottle", 0f); }
            if (k == 4 && matW != null && matG != null)
            { matW.SetFloat("_BandBias", matG.GetFloat("_BandBias")); matW.SetFloat("_InkMottle", matG.GetFloat("_InkMottle")); }

            var raw = RenderTex(1920, 1080);
            var t = DownN(raw, 2); UnityEngine.Object.Destroy(raw);
            SavePng("v5_P1_" + tags[k], t);
            grids[k] = Grid(t);
            UnityEngine.Object.Destroy(t);
        }
        if (sun != null) sun.shadows = svShadows;

        L("");
        L("---------- ④ 逐变体亮度网格（16×10，上→下） ----------");
        PrintGrid("00_基线（这就是画面本身）", grids[0]);
        L("");
        L("---------- ⑤ 逐变体差分（相对基线，只显示 |Δ|≥0.04） ----------");
        for (int k = 1; k < tags.Length; k++) { PrintDelta(tags[k] + " − 基线", grids[0], grids[k]); L(""); }

        UnityEngine.Object.Destroy(go);
        L("================ end ================");
        string outp = Path.GetFullPath(Path.Combine(Application.dataPath, "../Tools/reports/r8_facts.txt"));
        File.WriteAllText(outp, _sb.ToString());
        return "written: " + outp + "  (" + _sb.Length + " chars)";
    }
}

return R8Facts.Run();
