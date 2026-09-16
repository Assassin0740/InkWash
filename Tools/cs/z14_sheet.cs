// z14_sheet.cs —— 对比图 v3：用独立 Layer + cullingMask 排除场景遮挡
//
// v2 失败原因：相机落点 z≈2 撞上 Arena 高台（±5）、z≈-0.2 撞石门 ⇒ 满屏墙体。
// 根因是我在「场地里找空地」而场地本来就挤。
//
// v3 方案：把镜头视野与场景解耦 ——
//   · 新建 Layer 31「ZPreview」
//   · Z_Rig 全部子物体设到该 Layer
//   · 相机 cullingMask 只留该 Layer ⇒ 背景干净，再也不受墙/台/门干扰
//   · 额外加一盏方向光（该 Layer 的光才能照到，但主光已在场景里，先试着复用）
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

const int PREVIEW_LAYER = 31;
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

void Shoot(int W, int H, Vector3 pos, Vector3 look, float fov, string tag)
{
    cam.transform.position = pos;
    cam.transform.rotation = Quaternion.LookRotation((look - pos).normalized, Vector3.up);
    cam.fieldOfView = fov;
    var t = RenderFrame(W, H);
    if (t == null) { sb.AppendLine("  !! 渲染失败 " + tag); return; }
    try { File.WriteAllBytes(Path.Combine(shotDir, tag + ".png"), t.EncodeToPNG()); sb.AppendLine("  已存 " + tag + ".png"); }
    catch (Exception e) { sb.AppendLine("  !! 写盘失败 " + tag + " " + e.Message); }
    UnityEngine.Object.Destroy(t);
}

Bounds VertexBounds(GameObject go)
{
    var b = new Bounds();
    bool first = true;
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
            if (first) { b = new Bounds(w, Vector3.zero); first = false; }
            else b.Encapsulate(w);
        }
    }
    return b;
}

IEnumerator Body()
{
    yield return null; yield return null;

    var rig = GameObject.Find("Z_Rig");
    if (rig == null) { Debug.LogError("[z14] 缺 Z_Rig"); yield break; }

    // 场地中心放（Layer 隔离后不怕遮挡，用哪里都行）
    rig.transform.position = new Vector3(0f, 0.05f, 0f);

    // —— 把 Z_Rig 全体设到预览 Layer ——
    int n = 0;
    void SetLayer(Transform t)
    {
        t.gameObject.layer = PREVIEW_LAYER;
        n++;
        for (int i = 0; i < t.childCount; i++) SetLayer(t.GetChild(i));
    }
    SetLayer(rig.transform);
    sb.AppendLine($"设到 Layer {PREVIEW_LAYER} 的对象数 = {n}");

    // —— 相机 ——
    var camGO = GameObject.Find("Z_Cam");
    if (camGO == null) camGO = new GameObject("Z_Cam");
    cam = camGO.GetComponent<Camera>();
    if (cam == null) cam = camGO.AddComponent<Camera>();
    cam.clearFlags = CameraClearFlags.SolidColor;
    cam.backgroundColor = new Color(0.91f, 0.90f, 0.87f, 1f);
    cam.cullingMask = 1 << PREVIEW_LAYER;   // ★ 只看素材
    cam.nearClipPlane = 0.03f;
    cam.farClipPlane = 500f;
    cam.enabled = false;

    var tpc = UnityEngine.Object.FindObjectOfType<InkWash.CameraRig.ThirdPersonCamera>();
    if (tpc != null) tpc.enabled = false;

    // —— 补一盏灯：预览 Layer 的光 ——
    // 场景主光的 cullingMask 可能不含 Layer31 ⇒ 自己加一盏专属灯
    var lightGO = GameObject.Find("Z_Light");
    if (lightGO == null) lightGO = new GameObject("Z_Light");
    var lt = lightGO.GetComponent<Light>();
    if (lt == null) lt = lightGO.AddComponent<Light>();
    lt.type = LightType.Directional;
    lt.intensity = 1.15f;
    lt.color = new Color(1.0f, 0.98f, 0.94f);
    lt.cullingMask = 1 << PREVIEW_LAYER;
    lightGO.transform.rotation = Quaternion.Euler(42f, -35f, 0f);
    lightGO.transform.position = new Vector3(0f, 30f, 0f);
    sb.AppendLine("已加预览专用方向光");

    var units = new List<GameObject>();
    for (int i = 0; i < rig.transform.childCount; i++)
        units.Add(rig.transform.GetChild(i).gameObject);

    // —— 尺寸与摆位 ——
    var dims = new Dictionary<string, Bounds>();
    var order = new[] { "Z_Orge", "Z_Orc", "Z_Undead", "Z_Dragon_LP", "Z_Dragon" };
    foreach (var u in units)
    {
        u.transform.localPosition = Vector3.zero;
        u.transform.localRotation = Quaternion.identity;
        dims[u.name] = VertexBounds(u);
    }

    sb.AppendLine("=== 尺寸 ===");
    float cursor = 0f;
    foreach (var nm in order)
    {
        var u = units.Find(x => x.name == nm);
        if (u == null) continue;
        var bb = dims[nm];
        sb.AppendLine($"  {nm}: size=({bb.size.x:F2},{bb.size.y:F2},{bb.size.z:F2}) foot={bb.min.y:F3}");

        // 横向占位：龙的体长沿 Z，取 40% 作为视觉宽度
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
    sb.AppendLine($"总宽 totalW={totalW:F2} 中心={wc}");

    // ---- A1 阵容正面 ----
    Shoot(1700, 680,
          wc + new Vector3(0f, 2.0f, -Mathf.Max(totalW * 0.58f, 14f)),
          wc + new Vector3(0f, 1.2f, 0f),
          38f, "A1_阵容_正面");

    // ---- A2 阵容 3/4 ----
    Shoot(1700, 680,
          wc + new Vector3(totalW * 0.52f, 3.4f, -Mathf.Max(totalW * 0.48f, 12f)),
          wc + new Vector3(0f, 1.2f, 0f),
          40f, "A2_阵容_斜45");

    // ---- B 逐个特写 ----
    int k = 0;
    foreach (var nm in order)
    {
        var u = units.Find(x => x.name == nm);
        if (u == null) continue;
        var bb = dims[nm];
        var c = bb.center;
        float mx = Mathf.Max(bb.size.x, Mathf.Max(bb.size.y, bb.size.z));
        float dist = mx * 1.65f + 1.2f;
        k++;
        Shoot(960, 780, c + new Vector3(0f, mx * 0.18f, -dist), c, 42f, $"B{k}_{nm}");
        sb.AppendLine($"  特写 {nm} dist={dist:F2}");
    }

    // ---- C Boss 龙 ----
    var dragon = units.Find(x => x.name == "Z_Dragon");
    if (dragon != null)
    {
        var bb = dims["Z_Dragon"];
        var c = bb.center;
        Shoot(1300, 780, c + new Vector3(-bb.size.x * 1.6f - 4.5f, 2.8f, -bb.size.z * 0.38f), c, 48f, "C1_Boss龙_斜前");
        Shoot(1300, 780, c + new Vector3(0f, bb.size.z * 0.55f, -bb.size.z * 0.22f), c, 46f, "C2_Boss龙_俯前");
    }

    // ---- D 俯视 ----
    Shoot(1300, 950, wc + new Vector3(0f, Mathf.Max(totalW * 0.62f, 16f), -0.01f), wc, 44f, "D1_俯视");

    File.WriteAllText(Path.Combine(projRoot, "Tools/reports/z14_sheet.txt"), sb.ToString());
    Debug.Log("[z14] done\n" + sb.ToString());
    yield return null;
}

return Body();
