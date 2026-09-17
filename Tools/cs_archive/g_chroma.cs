// g-4：扫描 _ChromaKeep —— "水墨不只是黑白"要靠贴图里本来就有的花青/赭石透出来
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
    int mx = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
    int mn = Mathf.Min(c.r, Mathf.Min(c.g, c.b));
    return mx <= 0 ? 0f : (mx - mn) / (float)mx;
}
float Lum(Color32 c) { return (0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b) / 255f; }

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
    int W = 900, H = 700;
    yield return null; yield return null;
    // 等动画稳定到 idle 姿势
    yield return new WaitForSeconds(1.2f);

    var cam = Camera.main;
    var tpc = UnityEngine.Object.FindObjectOfType<ThirdPersonCamera>();
    if (tpc != null) tpc.enabled = false;
    yield return null;

    // 找角色本体渲染器
    Renderer body = null;
    Transform root = null;
    foreach (var r in UnityEngine.Object.FindObjectsOfType<Renderer>())
    {
        var p = r.transform.name;
        string full = r.transform.name;
        var t = r.transform;
        while (t.parent != null) { full = t.parent.name + "/" + full; t = t.parent; }
        if (full.IndexOf("Player/Visual", StringComparison.Ordinal) == 0 && full.EndsWith("/Mesh"))
        { body = r; root = r.transform; break; }
    }
    if (body == null) { Debug.LogError("[g_chroma] 找不到角色本体"); yield break; }

    // 角色朝向：往上找带 PlayerController / 有 forward 的父级；退化为世界 +z
    var fwd = Vector3.forward;
    var walk = body.transform;
    while (walk.parent != null && walk.parent.name != "Player") walk = walk.parent;
    if (walk.parent != null) fwd = walk.parent.forward;
    fwd.y = 0f;
    if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
    fwd.Normalize();

    var mat = body.sharedMaterial;
    sb.AppendLine("角色材质 = " + mat.name + "  shader=" + mat.shader.name);
    sb.AppendLine("fwd=" + fwd.ToString("F2"));

    var inst = new Material(mat);
    inst.name = mat.name + "(Scan)";
    var origMats = body.sharedMaterials;
    var clone = (Material[])origMats.Clone();
    for (int i = 0; i < clone.Length; i++) if (clone[i] == mat) clone[i] = inst;
    body.sharedMaterials = clone;

    float[] keep = { 0.08f, 0.22f, 0.38f, 0.55f };
    sb.AppendLine();
    sb.AppendLine("ChromaKeep  彩色像素占比  角色平均彩度  角色平均亮度  高饱和(>0.25)占比");

    foreach (var k in keep)
    {
        inst.SetFloat("_ChromaKeep", k);
        yield return null; yield return null;

        var pivot = body.bounds.center;
        var camPos = pivot - fwd * 3.1f + Vector3.up * 0.45f;
        cam.transform.position = camPos;
        cam.transform.rotation = Quaternion.LookRotation((pivot - camPos).normalized, Vector3.up);
        cam.fieldOfView = 42f;
        yield return null;
        yield return null;
        yield return null;

        var tex = RenderFrame(cam, W, H);
        if (tex == null) { sb.AppendLine("  渲染失败 " + k); continue; }
        string tag = "chroma_" + k.ToString("0.00").Replace('.', '_');
        try { File.WriteAllBytes(Path.Combine(shotDir, tag + "_close.png"), tex.EncodeToPNG()); } catch { }

        // 角色在画面中央：取中央 46% 宽、70% 高作为统计窗（近景充满画面，背景只剩边角）
        var px = tex.GetPixels32();
        int x0 = (int)(W * 0.27f), x1 = (int)(W * 0.73f);
        int y0 = (int)(H * 0.15f), y1 = (int)(H * 0.85f);
        long n = 0, colored = 0, hiSat = 0;
        double cs = 0, ls = 0;
        for (int y = y0; y < y1; y++)
            for (int x = x0; x < x1; x++)
            {
                var c = px[y * W + x];
                float l = Lum(c);
                if (l > 0.93f) continue;              // 跳过纸白背景
                n++;
                cs += Chr(c); ls += l;
                if (Chr(c) > 0.06f) colored++;
                if (Chr(c) > 0.25f) hiSat++;
            }
        UnityEngine.Object.Destroy(tex);

        double avgC = n > 0 ? cs / n : 0, avgL = n > 0 ? ls / n : 0;
        sb.AppendLine(string.Format("  {0,4:F2}      {1,6:P1}       {2,6:F3}       {3,6:F3}      {4,6:P1}",
            k, n > 0 ? colored / (double)n : 0, avgC, avgL, n > 0 ? hiSat / (double)n : 0));

        // 顺便拍一张实机第三人称，看整体观感
        var gv = pivot + new Vector3(0f, 0.9f, 0f) - fwd * 4.2f + Vector3.right * 0.8f;
        cam.transform.position = gv;
        cam.transform.rotation = Quaternion.LookRotation((pivot + Vector3.up * 0.4f - gv).normalized, Vector3.up);
        cam.fieldOfView = 48f;
        yield return null; yield return null;
        var t2 = RenderFrame(cam, W, H);
        if (t2 != null)
        {
            try { File.WriteAllBytes(Path.Combine(shotDir, tag + "_game.png"), t2.EncodeToPNG()); } catch { }
            UnityEngine.Object.Destroy(t2);
        }
    }

    body.sharedMaterials = origMats;
    UnityEngine.Object.Destroy(inst);

    File.WriteAllText(Path.Combine(projRoot, "Tools/reports/g_chroma.txt"), sb.ToString());
    Debug.Log("[g_chroma] done\n" + sb.ToString());
    yield return null;
}

return Body();
