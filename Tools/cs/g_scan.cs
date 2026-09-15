// g-8：① 观察角色材质 id 的逐帧变化（谁在重置）② 同帧内扫 _ChromaKeep（跨帧会被覆盖）
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
    int W = 640, H = 520;
    yield return null; yield return null;
    yield return new WaitForSeconds(1.0f);

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
    if (body == null) { Debug.LogError("[g_scan] 无角色"); yield break; }

    var walk = body.transform;
    while (walk.parent != null && walk.parent.name != "Player") walk = walk.parent;
    var fwd = walk.parent != null ? walk.parent.forward : Vector3.forward;
    fwd.y = 0f; if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward; fwd.Normalize();

    // ---------- ① 逐帧观察材质 id ----------
    sb.AppendLine("=== ① 角色材质 instanceID 逐帧序列 ===");
    var ids = new List<string>();
    for (int i = 0; i < 14; i++) { ids.Add(body.sharedMaterial.GetInstanceID().ToString()); yield return null; }
    sb.AppendLine("  " + string.Join(" ", ids.ToArray()));
    var uniq = new HashSet<string>(ids);
    sb.AppendLine("  不同 id 数 = " + uniq.Count + (uniq.Count == 1 ? "  ⇒ 运行时不再被替换" : "  ⇒ 存在替换"));

    // ---------- ② 同帧内扫描 ----------
    var mat = body.sharedMaterial;
    var inst = new Material(mat); inst.name = mat.name + "(Scan)";
    var origMats = body.sharedMaterials;
    var clone = (Material[])origMats.Clone();
    for (int i = 0; i < clone.Length; i++) if (clone[i] == mat) clone[i] = inst;

    var pivot = body.bounds.center;
    var camPos = pivot + fwd * 3.0f + Vector3.up * 0.42f;
    cam.transform.position = camPos;
    cam.transform.rotation = Quaternion.LookRotation((pivot - camPos).normalized, Vector3.up);
    cam.fieldOfView = 40f;
    yield return null;
    yield return null;

    body.sharedMaterials = clone;      // ← 设完立刻用，整段不 yield

    sb.AppendLine();
    sb.AppendLine("=== ② 同帧扫描（_ChromaKeep × _InkDensity）===");
    sb.AppendLine("  keep  dens   彩色占比  平均彩度  平均亮度  高饱和占比");

    var cases = new (float keep, float dens)[]
    {
        (0.08f, 0.62f), (0.30f, 0.62f), (0.60f, 0.62f), (1.00f, 0.62f), (1.00f, 1.00f),
    };
    foreach (var (keep, dens) in cases)
    {
        inst.SetFloat("_ChromaKeep", keep);
        inst.SetFloat("_InkDensity", dens);
        var t = RenderFrame(cam, W, H);
        if (t == null) continue;

        string tag = string.Format("k{0:0.00}_d{1:0.00}", keep, dens).Replace('.', '_');
        try { File.WriteAllBytes(Path.Combine(shotDir, "scan_" + tag + ".png"), t.EncodeToPNG()); } catch { }

        var px = t.GetPixels32();
        int x0 = (int)(W * 0.32f), x1 = (int)(W * 0.68f);
        int y0 = (int)(H * 0.12f), y1 = (int)(H * 0.88f);
        long n = 0, colored = 0, hi = 0; double cs = 0, ls = 0;
        for (int y = y0; y < y1; y++)
            for (int x = x0; x < x1; x++)
            {
                var c = px[y * W + x];
                float l = Lum(c);
                if (l > 0.90f) continue;
                n++; cs += Chr(c); ls += l;
                if (Chr(c) > 0.06f) colored++;
                if (Chr(c) > 0.25f) hi++;
            }
        UnityEngine.Object.Destroy(t);
        sb.AppendLine(string.Format("  {0,4:F2}  {1,4:F2}   {2,6:P1}  {3,6:F3}   {4,6:F3}   {5,6:P1}",
            keep, dens, n > 0 ? colored / (double)n : 0, n > 0 ? cs / n : 0, n > 0 ? ls / n : 0, n > 0 ? hi / (double)n : 0));
    }

    sb.AppendLine();
    sb.AppendLine("  扫描后 sharedMaterial.id = " + body.sharedMaterial.GetInstanceID()
                  + "   期望 " + inst.GetInstanceID() + (body.sharedMaterial == inst ? "  ✅ 仍是实例" : "  ❌ 已被换走"));
    body.sharedMaterials = origMats;
    UnityEngine.Object.Destroy(inst);

    File.WriteAllText(Path.Combine(projRoot, "Tools/reports/g_scan.txt"), sb.ToString());
    Debug.Log("[g_scan] done\n" + sb.ToString());
    yield return null;
}

return Body();
