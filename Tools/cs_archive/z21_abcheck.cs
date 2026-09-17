// z21_abcheck.cs —— A/B 验证（Play 模式，不用 AssetDatabase）
//
// 思路：水墨材质已经挂在 prefab 上；同时用「场景里预先放好的一个 PBR 版 Orge 实例」做对照。
// 这个 PBR 实例由编辑器侧 (z21a_prepare) 预备，命名 Z_Orge_PBR。
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

var sb = new StringBuilder();
string projRoot = Path.GetDirectoryName(Application.dataPath);
string shotDir = Path.Combine(projRoot, "Tools/screenshots/ziyuan");
Directory.CreateDirectory(shotDir);

const int LAYER = 31;
Camera cam;

Texture2D RenderFrame(int W, int H)
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

void Analyze(Texture2D t, out float satMean, out float lumMean, out float lumStd, out int count)
{
    var px = t.GetPixels32();
    double s = 0, l = 0, l2 = 0; int n = 0;
    for (int i = 0; i < px.Length; i++)
    {
        var c = px[i];
        if (Mathf.Abs(c.r - 232) < 12 && Mathf.Abs(c.g - 230) < 12 && Mathf.Abs(c.b - 222) < 14) continue;
        float r = c.r / 255f, g = c.g / 255f, b = c.b / 255f;
        float mx = Mathf.Max(r, Mathf.Max(g, b)), mn = Mathf.Min(r, Mathf.Min(g, b));
        float sat = mx <= 0.0001f ? 0f : (mx - mn) / mx;
        float lum = 0.2126f * r + 0.7152f * g + 0.0722f * b;
        s += sat; l += lum; l2 += lum * lum; n++;
    }
    count = n;
    if (n == 0) { satMean = lumMean = lumStd = 0; return; }
    satMean = (float)(s / n);
    lumMean = (float)(l / n);
    lumStd = (float)Math.Sqrt(Math.Max(0, l * 0 + (l2 / n - (l / n) * (l / n))));
}

void Shoot(int W, int H, Vector3 pos, Vector3 look, float fov, string tag, out Texture2D tex)
{
    cam.transform.position = pos;
    cam.transform.rotation = Quaternion.LookRotation((look - pos).normalized, Vector3.up);
    cam.fieldOfView = fov;
    tex = RenderFrame(W, H);
    if (tex == null) { sb.AppendLine("  !! 渲染失败 " + tag); return; }
    try { File.WriteAllBytes(Path.Combine(shotDir, tag + ".png"), tex.EncodeToPNG()); sb.AppendLine("  已存 " + tag + ".png"); }
    catch (Exception e) { sb.AppendLine("  !! 写盘失败 " + tag + " " + e.Message); }
}

Bounds VertexBounds(GameObject go)
{
    var b = new Bounds(); bool first = true;
    foreach (var r in go.GetComponentsInChildren<Renderer>())
    {
        Mesh m = null;
        var mf = r.GetComponent<MeshFilter>();
        if (mf != null) m = mf.sharedMesh;
        else { var sm = r as SkinnedMeshRenderer; if (sm != null) m = sm.sharedMesh; }
        if (m == null) continue;
        var xf = r.transform.localToWorldMatrix;
        var verts = m.vertices;
        for (int i = 0; i < verts.Length; i++)
        {
            var w = xf.MultiplyPoint3x4(verts[i]);
            if (first) { b = new Bounds(w, Vector3.zero); first = false; } else b.Encapsulate(w);
        }
    }
    return b;
}

IEnumerator Body()
{
    yield return null; yield return null;

    var rig = GameObject.Find("Z_Rig");
    if (rig == null) { Debug.LogError("[z21] 缺 Z_Rig"); yield break; }
    rig.transform.position = new Vector3(0f, 0.05f, 0f);

    void SetLayer(Transform t)
    {
        t.gameObject.layer = LAYER;
        for (int i = 0; i < t.childCount; i++) SetLayer(t.GetChild(i));
    }
    SetLayer(rig.transform);

    var camGO = GameObject.Find("Z_Cam");
    if (camGO == null) camGO = new GameObject("Z_Cam");
    cam = camGO.GetComponent<Camera>();
    if (cam == null) cam = camGO.AddComponent<Camera>();
    cam.clearFlags = CameraClearFlags.SolidColor;
    cam.backgroundColor = new Color(0.91f, 0.90f, 0.87f, 1f);
    cam.cullingMask = 1 << LAYER;
    cam.nearClipPlane = 0.03f;
    cam.farClipPlane = 500f;
    cam.enabled = false;

    var tpc = UnityEngine.Object.FindObjectOfType<InkWash.CameraRig.ThirdPersonCamera>();
    if (tpc != null) tpc.enabled = false;

    var lightGO = GameObject.Find("Z_Light");
    if (lightGO == null) lightGO = new GameObject("Z_Light");
    var lt = lightGO.GetComponent<Light>();
    if (lt == null) lt = lightGO.AddComponent<Light>();
    lt.type = LightType.Directional;
    lt.intensity = 1.15f;
    lt.color = new Color(1.0f, 0.98f, 0.94f);
    lt.cullingMask = 1 << LAYER;
    lightGO.transform.rotation = Quaternion.Euler(42f, -35f, 0f);

    var units = new List<GameObject>();
    for (int i = 0; i < rig.transform.childCount; i++)
        units.Add(rig.transform.GetChild(i).gameObject);
    var order = new[] { "Z_Orge", "Z_Orc", "Z_Undead", "Z_Dragon_LP", "Z_Dragon" };

    var dims = new Dictionary<string, Bounds>();
    foreach (var u in units)
    {
        u.transform.localPosition = Vector3.zero;
        u.transform.localRotation = Quaternion.identity;
        dims[u.name] = VertexBounds(u);
    }

    float cursor = 0f;
    foreach (var nm in order)
    {
        var u = units.Find(x => x.name == nm);
        if (u == null) continue;
        var bb = dims[nm];
        float w = bb.size.z > bb.size.x * 1.5f ? bb.size.z * 0.42f : bb.size.x;
        float half = Mathf.Max(w * 0.5f, 1.15f);
        cursor += half;
        u.transform.localPosition = new Vector3(cursor, -bb.min.y, 0f);
        cursor += half + 0.8f;
    }
    float totalW = cursor + 1f;
    yield return null;
    foreach (var nm in order)
    {
        var u = units.Find(x => x.name == nm);
        if (u != null) dims[nm] = VertexBounds(u);
    }
    var wc = rig.transform.position + new Vector3(totalW * 0.5f, 0f, 0f);

    // ---- A/B：水墨 Orge vs PBR Orge（PBR 版由 prepare 预置）----
    var orgeInk = units.Find(x => x.name == "Z_Orge");
    var orgePbr = GameObject.Find("Z_Orge_PBR");

    sb.AppendLine("=== A/B 验证 ===");
    sb.AppendLine($"水墨 Orge 存在 = {orgeInk != null}");
    sb.AppendLine($"PBR  Orge 存在 = {orgePbr != null}");

    if (orgeInk != null)
    {
        var ob = dims["Z_Orge"];
        var oc = ob.center;
        float od = Mathf.Max(ob.size.x, Mathf.Max(ob.size.y, ob.size.z)) * 1.5f + 1.2f;
        var oCam = oc + new Vector3(0f, ob.size.y * 0.12f, -od);

        Texture2D t;
        Shoot(760, 760, oCam, oc, 42f, "E1_Orge_水墨", out t);
        float s1, l1, st1; int n1;
        if (t != null) { Analyze(t, out s1, out l1, out st1, out n1); UnityEngine.Object.Destroy(t); }
        else { s1 = l1 = st1 = 0; n1 = 0; }

        if (orgePbr != null)
        {
            // PBR 实例也设到 Layer31 并放到同一位置附近
            void SetLayer2(Transform t2)
            {
                t2.gameObject.layer = LAYER;
                for (int i = 0; i < t2.childCount; i++) SetLayer2(t2.GetChild(i));
            }
            SetLayer2(orgePbr.transform);
            var pb = VertexBounds(orgePbr);
            orgePbr.transform.position = new Vector3(oc.x + 4.0f, -pb.min.y + 0.05f, oc.z);
            yield return null;
            var pb2 = VertexBounds(orgePbr);
            var pc = pb2.center;
            float pd = Mathf.Max(pb2.size.x, Mathf.Max(pb2.size.y, pb2.size.z)) * 1.5f + 1.2f;

            Texture2D t2;
            Shoot(760, 760, pc + new Vector3(0f, pb2.size.y * 0.12f, -pd), pc, 42f, "E2_Orge_原PBR", out t2);
            float s2, l2, st2; int n2;
            if (t2 != null) { Analyze(t2, out s2, out l2, out st2, out n2); UnityEngine.Object.Destroy(t2); }
            else { s2 = l2 = st2 = 0; n2 = 0; }

            sb.AppendLine();
            sb.AppendLine($"  水墨版: 彩度={s1:F4} 明度={l1:F4} 明度std={st1:F4} px={n1}");
            sb.AppendLine($"  PBR版 : 彩度={s2:F4} 明度={l2:F4} 明度std={st2:F4} px={n2}");
            sb.AppendLine($"  >> 彩度差 {s1 - s2:+0.0000;-0.0000}");
            sb.AppendLine($"  >> 明度差 {l1 - l2:+0.0000;-0.0000}");
        }
    }

    // ---- 最终全阵容大图 ----
    Texture2D tt;
    Shoot(1700, 680, wc + new Vector3(0f, 2.0f, -Mathf.Max(totalW * 0.58f, 14f)),
          wc + new Vector3(0f, 1.2f, 0f), 38f, "F1_全阵容_最终", out tt);
    if (tt != null) UnityEngine.Object.Destroy(tt);

    File.WriteAllText(Path.Combine(projRoot, "Tools/reports/z21_abcheck.txt"), sb.ToString());
    Debug.Log("[z21] done\n" + sb.ToString());
    yield return null;
}

return Body();
