// z13_sheet.cs —— 新素材水墨化对比图 v2
//
// v1 的三个问题（已修）：
//   ① 相机距离按 size.z 算 —— 龙体长 9 m，相机直接落进龙身里 ⇒ 改成按「最大维度」算并留 1.8 倍余量
//   ② 全部旋转 180° —— 静态龙被转成竖立 ⇒ 改成不旋转，靠相机绕到正面
//   ③ Z_Orge 外面有黑方块 ⇒ 是 Icosphere 残留，在 prepare 阶段已删
//
// 产出：
//   A1 阵容正面 / A2 阵容侧面 / B1 逐个特写×5 / C1 Boss龙 / D1 俯视
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
        if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
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
    if (rig == null) { Debug.LogError("[z13] 缺 Z_Rig"); yield break; }

    // 把 Z_Rig 搬到场地中央的开阔地（院墙 ±22、石门 ±10、高台 ±5 ⇒ z=15 是墙外，改用 z=17 太远）
    // 实测：z=15 时向南看是院墙内侧，向北看是石门/院墙。改到 Arena 高台以南的空地。
    Vector3 origin = new Vector3(0f, 0.05f, 15f);
    rig.transform.position = origin;

    var camGO = GameObject.Find("Z_Cam");
    if (camGO == null) camGO = new GameObject("Z_Cam");
    cam = camGO.GetComponent<Camera>();
    if (cam == null) cam = camGO.AddComponent<Camera>();
    cam.clearFlags = CameraClearFlags.SolidColor;
    cam.backgroundColor = new Color(0.90f, 0.89f, 0.86f, 1f);
    cam.nearClipPlane = 0.03f;
    cam.farClipPlane = 400f;
    cam.enabled = false;

    var tpc = UnityEngine.Object.FindObjectOfType<InkWash.CameraRig.ThirdPersonCamera>();
    if (tpc != null) tpc.enabled = false;

    var units = new List<GameObject>();
    for (int i = 0; i < rig.transform.childCount; i++)
        units.Add(rig.transform.GetChild(i).gameObject);

    // 先全部归零，量出各自尺寸
    var dims = new Dictionary<string, Bounds>();
    foreach (var u in units)
    {
        u.transform.localPosition = Vector3.zero;
        u.transform.localRotation = Quaternion.identity;
        dims[u.name] = VertexBounds(u);
    }
    sb.AppendLine("=== 尺寸 ===");
    foreach (var kv in dims)
        sb.AppendLine($"  {kv.Key}: {kv.Value.size} foot={kv.Value.min.y:F3}");

    // 布局：沿 X 排开，间距按各自体宽 + 余量
    float cursor = 0f;
    var xpos = new Dictionary<string, float>();
    var order = new[] { "Z_Orge", "Z_Orc", "Z_Undead", "Z_Dragon_LP", "Z_Dragon" };
    foreach (var nm in order)
    {
        var u = units.Find(x => x.name == nm);
        if (u == null) continue;
        var bb = dims[nm];
        float w = Mathf.Max(bb.size.x, bb.size.z * 0.35f);   // 龙的体长沿 z，横向占地按 35% 算
        float half = Mathf.Max(w * 0.5f, 1.2f);
        cursor += half;
        xpos[nm] = cursor;
        u.transform.localPosition = new Vector3(cursor, -bb.min.y, 0f);
        cursor += half + 0.7f;
        sb.AppendLine($"  摆位 {nm} x={cursor - half:F2} foot修正={-bb.min.y:F3}");
    }
    float totalW = cursor;

    yield return null;

    // 重新量（已摆位）
    foreach (var nm in order)
    {
        var u = units.Find(x => x.name == nm);
        if (u != null) dims[nm] = VertexBounds(u);
    }

    var worldCenter = rig.transform.position + new Vector3(totalW * 0.5f, 0f, 0f);

    // ---- A1 阵容正面（沿 -Z 看向 +Z）----
    float camZ = -Mathf.Max(totalW * 0.62f, 13f);
    Shoot(1700, 620,
          worldCenter + new Vector3(0f, 1.9f, camZ),
          worldCenter + new Vector3(0f, 1.1f, 0f),
          40f, "A1_阵容_正面");

    // ---- A2 阵容侧面（从 +X 看）----
    Shoot(1700, 620,
          worldCenter + new Vector3(totalW * 0.62f, 2.4f, -1.0f),
          worldCenter + new Vector3(0f, 1.1f, 0f),
          40f, "A2_阵容_侧45");

    // ---- B 逐个特写 ----
    int k = 0;
    foreach (var nm in order)
    {
        var u = units.Find(x => x.name == nm);
        if (u == null) continue;
        var bb = dims[nm];
        var c = bb.center;
        float maxDim = Mathf.Max(bb.size.x, Mathf.Max(bb.size.y, bb.size.z));
        float dist = maxDim * 1.75f + 1.5f;
        k++;
        Shoot(960, 780,
              c + new Vector3(0f, maxDim * 0.15f, -dist),
              c,
              42f, $"B{k}_{nm}");
        sb.AppendLine($"  特写 {nm}: maxDim={maxDim:F2} dist={dist:F2}");
    }

    // ---- C1 Boss 龙 3/4 视角（龙体长沿 Z，要从斜上方看）----
    var dragon = units.Find(x => x.name == "Z_Dragon");
    if (dragon != null)
    {
        var bb = dims["Z_Dragon"];
        var c = bb.center;
        Shoot(1300, 760,
              c + new Vector3(-bb.size.x * 2.2f - 4f, 3.2f, -bb.size.z * 0.42f),
              c, 46f, "C1_Boss龙_斜前");
        Shoot(1300, 760,
              c + new Vector3(0f, bb.size.z * 0.62f, -bb.size.z * 0.28f),
              c, 46f, "C2_Boss龙_俯前");
    }

    // ---- D1 俯视全景 ----
    Shoot(1300, 900,
          worldCenter + new Vector3(0f, Mathf.Max(totalW * 0.72f, 18f), 0.5f),
          worldCenter,
          45f, "D1_俯视");

    File.WriteAllText(Path.Combine(projRoot, "Tools/reports/z13_sheet.txt"), sb.ToString());
    Debug.Log("[z13] done\n" + sb.ToString());
    yield return null;
}

return Body();
