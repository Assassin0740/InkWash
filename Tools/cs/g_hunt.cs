// g-5：墙上"迷彩斑块"归因对照实验 —— 逐层关掉，看是哪一层在产生斑块
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using InkWash.CameraRig;

var sb = new StringBuilder();
string projRoot = Path.GetDirectoryName(Application.dataPath);
string shotDir = Path.Combine(projRoot, "Tools/screenshots/hunt");
Directory.CreateDirectory(shotDir);

Texture2D RenderFrame(Camera cam, int W, int H)
{
    var rt = RenderTexture.GetTemporary(W, H, 24, RenderTextureFormat.ARGB32);
    var prevT = cam.targetTexture;
    cam.targetTexture = rt;
    try { cam.Render(); }
    finally { cam.targetTexture = prevT; }
    var prevA = RenderTexture.active;
    RenderTexture.active = rt;
    var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
    tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
    tex.Apply();
    RenderTexture.active = prevA;
    RenderTexture.ReleaseTemporary(rt);
    return tex;
}

IEnumerator Body()
{
    int W = 960, H = 640;
    yield return null; yield return null;

    var cam = Camera.main;
    var tpc = UnityEngine.Object.FindObjectOfType<ThirdPersonCamera>();
    if (tpc != null) tpc.enabled = false;

    // 找 Wall_N 的那个 renderer（用 M_Whitebox_Wall）
    Renderer wallR = null;
    foreach (var r in UnityEngine.Object.FindObjectsOfType<Renderer>())
    {
        if (r.sharedMaterial == null || r.sharedMaterial.name != "M_Whitebox_Wall") continue;
        if (r.transform.name != "Wall_N") continue;
        wallR = r; break;
    }
    if (wallR == null) { Debug.LogError("[g_hunt] 找不到 Wall_N"); yield break; }

    var origMat = wallR.sharedMaterial;
    var inst = new Material(origMat); inst.name = "Wall(Hunt)";
    wallR.sharedMaterial = inst;
    sb.AppendLine("目标 = Wall_N  材质 " + origMat.name);
    foreach (var k in new[] { "_BandBias", "_Bands", "_BandSoftness", "_LadderSkew", "_GrainScale", "_GrainAmp", "_StrokeScale", "_StrokeStretch", "_StrokeAmp", "_InkMottle", "_MottleScale", "_OutlineWidth" })
        if (inst.HasProperty(k)) sb.AppendLine("    " + k + " = " + inst.GetFloat(k).ToString("0.###"));
    sb.AppendLine();

    // 相机对着 Wall_N 正面
    var camPos = new Vector3(0f, 2.2f, 12.5f);
    var look = new Vector3(0f, 2.0f, 22f);
    cam.transform.position = camPos;
    cam.transform.rotation = Quaternion.LookRotation((look - camPos).normalized, Vector3.up);
    cam.fieldOfView = 48f;
    yield return null; yield return null;

    var sun = (Light)null;
    foreach (var l in UnityEngine.Object.FindObjectsOfType<Light>())
        if (l.type == LightType.Directional && l.shadows != LightShadows.None) { sun = l; break; }

    // 基线参数
    float bGrain = inst.GetFloat("_GrainAmp");
    float bStroke = inst.GetFloat("_StrokeAmp");
    float bMottle = inst.GetFloat("_InkMottle");
    float bBands = inst.GetFloat("_Bands");
    float bBias = inst.GetFloat("_BandBias");
    var sunShadow = sun != null ? sun.shadows : LightShadows.None;

    var steps = new (string tag, Action apply)[] { };

    var list = new List<(string, Action)>();
    list.Add(("00_基线", () => { }));
    list.Add(("01_关主光阴影", () => { if (sun != null) sun.shadows = LightShadows.None; }));
    list.Add(("02_关纸颗粒Grain", () => { inst.SetFloat("_GrainAmp", 0f); }));
    list.Add(("03_关笔触Stroke", () => { inst.SetFloat("_StrokeAmp", 0f); }));
    list.Add(("04_关积墨Mottle", () => { inst.SetFloat("_InkMottle", 0f); }));
    list.Add(("05_关量化Bands1", () => { inst.SetFloat("_Bands", 1f); }));
    list.Add(("06_全关", () =>
    {
        if (sun != null) sun.shadows = LightShadows.None;
        inst.SetFloat("_GrainAmp", 0f); inst.SetFloat("_StrokeAmp", 0f);
        inst.SetFloat("_InkMottle", 0f); inst.SetFloat("_Bands", 1f);
    }));

    foreach (var (tag, apply) in list)
    {
        // 复位
        if (sun != null) sun.shadows = sunShadow;
        inst.SetFloat("_GrainAmp", bGrain); inst.SetFloat("_StrokeAmp", bStroke);
        inst.SetFloat("_InkMottle", bMottle); inst.SetFloat("_Bands", bBands);
        inst.SetFloat("_BandBias", bBias);
        apply();
        yield return null; yield return null; yield return null;

        var tex = RenderFrame(cam, W, H);
        if (tex == null) { sb.AppendLine("  渲染失败 " + tag); continue; }
        try { File.WriteAllBytes(Path.Combine(shotDir, tag + ".png"), tex.EncodeToPNG()); } catch { }

        // 量墙面区域的中频纹理能量（4px 步长 Laplacian）
        var px = tex.GetPixels32();
        int x0 = (int)(W * 0.10f), x1 = (int)(W * 0.90f);
        int y0 = (int)(H * 0.38f), y1 = (int)(H * 0.70f);
        double sum = 0; long n = 0; float mn = 1f, mx = 0f;
        Func<int, int, float> L = (x, y) => (0.2126f * px[y * W + x].r + 0.7152f * px[y * W + x].g + 0.0722f * px[y * W + x].b) / 255f;
        const int ST = 4;
        for (int y = y0 + ST; y < y1 - ST; y++)
            for (int x = x0 + ST; x < x1 - ST; x++)
            {
                float lap = Mathf.Abs(L(x, y + ST) + L(x, y - ST) + L(x + ST, y) + L(x - ST, y) - 4f * L(x, y));
                sum += lap; n++;
                float c = L(x, y);
                if (c < mn) mn = c; if (c > mx) mx = c;
            }
        sb.AppendLine(string.Format("  {0,-18} 笔痕能量={1:F2}e-3  明度范围[{2:F3},{3:F3}]",
            tag, n > 0 ? sum / n * 1000 : 0, mn, mx));
    }

    // 还原
    if (sun != null) sun.shadows = sunShadow;
    wallR.sharedMaterial = origMat;
    UnityEngine.Object.Destroy(inst);

    File.WriteAllText(Path.Combine(projRoot, "Tools/reports/g_hunt.txt"), sb.ToString());
    Debug.Log("[g_hunt] done\n" + sb.ToString());
    yield return null;
}

return Body();
