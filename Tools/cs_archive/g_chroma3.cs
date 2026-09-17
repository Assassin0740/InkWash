// g-7：判定"材质实例是否真的被渲染" —— 改 _InkLight 为纯红 + 跨帧读回 instanceID
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
    int W = 700, H = 560;
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
    if (body == null) { Debug.LogError("[g_chroma3] 找不到角色本体"); yield break; }

    var walk = body.transform;
    while (walk.parent != null && walk.parent.name != "Player") walk = walk.parent;
    var fwd = walk.parent != null ? walk.parent.forward : Vector3.forward;
    fwd.y = 0f; if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward; fwd.Normalize();

    var mat = body.sharedMaterial;
    sb.AppendLine("原材质 = " + mat.name + "  id=" + mat.GetInstanceID()
                  + "  assetPath=" + UnityEditor.AssetDatabase.GetAssetPath(mat));
    sb.AppendLine("材质数组长度 = " + body.sharedMaterials.Length
                  + "  [" + string.Join(",", Array.ConvertAll(body.sharedMaterials, m => m == null ? "null" : m.name)) + "]");

    // 拍前先摆相机
    var pivot = body.bounds.center;
    var camPos = pivot + fwd * 3.0f + Vector3.up * 0.42f;
    cam.transform.position = camPos;
    cam.transform.rotation = Quaternion.LookRotation((pivot - camPos).normalized, Vector3.up);
    cam.fieldOfView = 40f;
    yield return null;

    var origMats = body.sharedMaterials;
    var inst = new Material(mat); inst.name = mat.name + "(C3)";
    var clone = (Material[])origMats.Clone();
    for (int i = 0; i < clone.Length; i++) if (clone[i] == mat) clone[i] = inst;
    body.sharedMaterials = clone;

    inst.SetColor("_InkLight", new Color(1f, 0f, 0f, 1f));
    int want = inst.GetInstanceID();
    yield return null; yield return null; yield return null;

    int got = body.sharedMaterial.GetInstanceID();
    sb.AppendLine();
    sb.AppendLine("设置 3 帧后 sharedMaterial.id = " + got + "　(期望 " + want + ")　"
                  + (got == want ? "✅ 未被覆盖" : "❌ 被别的代码换回去了"));
    sb.AppendLine("  _InkLight 读回 = " + body.sharedMaterial.GetColor("_InkLight").ToString("F2"));
    var t1 = RenderFrame(cam, W, H);
    if (t1 != null)
    {
        try { File.WriteAllBytes(Path.Combine(shotDir, "c3_red_inklight.png"), t1.EncodeToPNG()); } catch { }
        // 找画面中央的像素看是否变红
        var px = t1.GetPixels32();
        int red = 0, tot = 0;
        for (int y = (int)(H * 0.2f); y < (int)(H * 0.8f); y++)
            for (int x = (int)(W * 0.35f); x < (int)(W * 0.65f); x++)
            {
                var c = px[y * W + x]; tot++;
                if (c.r > c.g + 20 && c.r > c.b + 20) red++;
            }
        sb.AppendLine("  中央区域偏红像素 = " + red + " / " + tot + "  (" + (red * 100.0 / Mathf.Max(tot, 1)).ToString("F1") + "%)");
        UnityEngine.Object.Destroy(t1);
    }

    // 再测：直接把材质换成 URP/Unlit + _BaseMap，看贴图原色长什么样
    var probe = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
    probe.SetTexture("_BaseMap", mat.GetTexture("_BaseMap"));
    probe.SetColor("_BaseColor", Color.white);
    var clone2 = (Material[])origMats.Clone();
    for (int i = 0; i < clone2.Length; i++) if (clone2[i] == mat) clone2[i] = probe;
    body.sharedMaterials = clone2;
    yield return null; yield return null; yield return null;
    var t2 = RenderFrame(cam, W, H);
    if (t2 != null)
    {
        try { File.WriteAllBytes(Path.Combine(shotDir, "c3_raw_basecolor.png"), t2.EncodeToPNG()); } catch { }
        var px = t2.GetPixels32();
        double cs = 0; long n = 0;
        for (int y = (int)(H * 0.2f); y < (int)(H * 0.8f); y++)
            for (int x = (int)(W * 0.35f); x < (int)(W * 0.65f); x++)
            {
                var c = px[y * W + x];
                int mx = Mathf.Max(c.r, Mathf.Max(c.g, c.b)), mn = Mathf.Min(c.r, Mathf.Min(c.g, c.b));
                if (mx > 0) { cs += (mx - mn) / (float)mx; n++; }
            }
        sb.AppendLine("  贴图原色（URP/Unlit 直出）平均彩度 = " + (n > 0 ? (cs / n).ToString("F3") : "-"));
        UnityEngine.Object.Destroy(t2);
    }
    UnityEngine.Object.Destroy(probe);

    body.sharedMaterials = origMats;
    UnityEngine.Object.Destroy(inst);
    File.WriteAllText(Path.Combine(projRoot, "Tools/reports/g_chroma3.txt"), sb.ToString());
    Debug.Log("[g_chroma3] done\n" + sb.ToString());
    yield return null;
}

return Body();
