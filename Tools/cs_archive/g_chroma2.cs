// g-6：_ChromaKeep 到底有没有生效 —— 开到极限 + 正/背面各拍一张
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using InkWash.CameraRig;

var sb = new StringBuilder();
string projRoot = Path.GetDirectoryName(Application.dataPath);
string shotDir = Path.Combine(projRoot, "Tools/screenshots/chroma");
Directory.CreateDirectory(shotDir);

float Chr(Color32 c)
{
    int mx = Mathf.Max(c.r, Mathf.Max(c.g, c.b)), mn = Mathf.Min(c.r, Mathf.Min(c.g, c.b));
    return mx <= 0 ? 0f : (mx - mn) / (float)mx;
}
float Lum(Color32 c) { return (0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b) / 255f; }

Texture2D RenderFrame(Camera cam, int W, int H)
{
    var rt = RenderTexture.GetTemporary(W, H, 24, RenderTextureFormat.ARGB32);
    var prevT = cam.targetTexture; cam.targetTexture = rt;
    try { cam.Render(); } finally { cam.targetTexture = prevT; }
    var prevA = RenderTexture.active; RenderTexture.active = rt;
    var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
    tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); tex.Apply();
    RenderTexture.active = prevA; RenderTexture.ReleaseTemporary(rt);
    return tex;
}

IEnumerator Body()
{
    int W = 800, H = 640;
    yield return null; yield return null;
    yield return new WaitForSeconds(1.2f);

    var cam = Camera.main;
    var tpc = UnityEngine.Object.FindObjectOfType<ThirdPersonCamera>();
    if (tpc != null) tpc.enabled = false;
    yield return null;

    Renderer body = null;
    foreach (var r in UnityEngine.Object.FindObjectsOfType<Renderer>())
    {
        var t = r.transform; string full = t.name;
        while (t.parent != null) { full = t.parent.name + "/" + full; t = t.parent; }
        if (full.IndexOf("Player/Visual", StringComparison.Ordinal) == 0 && full.EndsWith("/Mesh")) { body = r; break; }
    }
    if (body == null) { Debug.LogError("[g_chroma2] 找不到角色本体"); yield break; }

    var walk = body.transform;
    while (walk.parent != null && walk.parent.name != "Player") walk = walk.parent;
    var fwd = walk.parent != null ? walk.parent.forward : Vector3.forward;
    fwd.y = 0f; if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward; fwd.Normalize();

    var mat = body.sharedMaterial;
    var inst = new Material(mat); inst.name = mat.name + "(C2)";
    var origMats = body.sharedMaterials;
    var clone = (Material[])origMats.Clone();
    for (int i = 0; i < clone.Length; i++) if (clone[i] == mat) clone[i] = inst;
    body.sharedMaterials = clone;

    // 贴图到底是什么 —— 直接问材质
    sb.AppendLine("材质 " + mat.name + "  _BaseColor=" + inst.GetColor("_BaseColor").ToString("F2"));
    var tex = inst.GetTexture("_BaseMap") as Texture2D;
    sb.AppendLine("_BaseMap = " + (tex == null ? "null" : tex.name + "  " + tex.width + "x" + tex.height + "  fmt=" + tex.format));
    sb.AppendLine();

    // 试验矩阵：侧面? 保色? 贴图占比?
    var cases = new (string tag, bool front, float keep, float dens)[]
    {
        ("01_back_k1.0_d0.62", false, 1.00f, 0.62f),
        ("02_front_k1.0_d0.62", true, 1.00f, 0.62f),
        ("03_front_k0.45_d0.62", true, 0.45f, 0.62f),
        ("04_front_k1.0_d0.15", true, 1.00f, 0.15f),
        ("05_back_k0.45_d0.62", false, 0.45f, 0.62f),
    };

    sb.AppendLine("case                    彩色占比  平均彩度  平均亮度  高饱和占比");
    foreach (var (tag, front, keep, dens) in cases)
    {
        inst.SetFloat("_ChromaKeep", keep);
        inst.SetFloat("_InkDensity", dens);
        yield return null; yield return null;

        var pivot = body.bounds.center;
        var camPos = pivot + fwd * (front ? 3.0f : -3.0f) + Vector3.up * 0.42f;
        cam.transform.position = camPos;
        cam.transform.rotation = Quaternion.LookRotation((pivot - camPos).normalized, Vector3.up);
        cam.fieldOfView = 40f;
        yield return null; yield return null; yield return null;

        var t = RenderFrame(cam, W, H);
        if (t == null) { sb.AppendLine("  渲染失败 " + tag); continue; }
        try { File.WriteAllBytes(Path.Combine(shotDir, "c2_" + tag + ".png"), t.EncodeToPNG()); } catch { }

        var px = t.GetPixels32();
        int x0 = (int)(W * 0.30f), x1 = (int)(W * 0.70f);
        int y0 = (int)(H * 0.12f), y1 = (int)(H * 0.88f);
        long n = 0, colored = 0, hiSat = 0; double cs = 0, ls = 0;
        for (int y = y0; y < y1; y++)
            for (int x = x0; x < x1; x++)
            {
                var c = px[y * W + x];
                float l = Lum(c);
                if (l > 0.90f) continue;
                n++; cs += Chr(c); ls += l;
                if (Chr(c) > 0.06f) colored++;
                if (Chr(c) > 0.25f) hiSat++;
            }
        UnityEngine.Object.Destroy(t);
        sb.AppendLine(string.Format("  {0,-22} {1,6:P1}  {2,6:F3}   {3,6:F3}   {4,6:P1}",
            tag, n > 0 ? colored / (double)n : 0, n > 0 ? cs / n : 0, n > 0 ? ls / n : 0, n > 0 ? hiSat / (double)n : 0));
    }

    body.sharedMaterials = origMats;
    UnityEngine.Object.Destroy(inst);
    File.WriteAllText(Path.Combine(projRoot, "Tools/reports/g_chroma2.txt"), sb.ToString());
    Debug.Log("[g_chroma2] done\n" + sb.ToString());
    yield return null;
}

return Body();
