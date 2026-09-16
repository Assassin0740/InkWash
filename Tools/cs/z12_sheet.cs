// z12_sheet.cs —— 新素材水墨化对比图（Play 模式）
//
// 产出 4 张：
//   A_阵容对比    —— 5 个新素材一字排开（水墨化后），看风格统一度
//   B_比例对照    —— 新怪 vs 现有 KayKit 怪，看体型差异
//   C_Boss特写    —— 中国龙近景
//   D_俯视全景    —— 从上方看整体布局
//
// 手法要点：Camera.Render() 逐帧背靠背；不 yield（yield 会让姿态/光照错开）
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using InkWash.Rendering;

var sb = new StringBuilder();
string projRoot = Path.GetDirectoryName(Application.dataPath);
string shotDir = Path.Combine(projRoot, "Tools/screenshots/ziyuan");
Directory.CreateDirectory(shotDir);

const string PD = "Assets/_Project/Prefabs/Ziyuan";
const string EPD = "Assets/_Project/Prefabs/Enemies";

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

// 量顶点真值
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

    // 找一片空地：院子中央偏北（场地边界 院墙±22 石门±10 高台±5）
    Vector3 origin = new Vector3(0f, 0.05f, 15f);

    var stage = new GameObject("Z_Stage");
    stage.transform.position = origin;

    var camGO = new GameObject("Z_Cam");
    cam = camGO.AddComponent<Camera>();
    cam.clearFlags = CameraClearFlags.SolidColor;
    cam.backgroundColor = new Color(0.90f, 0.89f, 0.86f, 1f);
    cam.nearClipPlane = 0.05f;
    cam.farClipPlane = 300f;
    cam.enabled = false;

    var tpc = UnityEngine.Object.FindObjectOfType<InkWash.CameraRig.ThirdPersonCamera>();
    if (tpc != null) { tpc.enabled = false; sb.AppendLine("已停用第三人称相机"); }

    // ===== 实例化 5 个新素材 =====
    // Resource 路径没法用（不在 Resources 下）⇒ 从场景里已加载的 prefab 找？不行。
    // 走一个稳妥办法：把 prefab 路径交给一个静态注册表已经来不及，改用 GameObject.Find 不可靠。
    // ★ 真正的解法：这些 prefab 在 Play 模式无法用 AssetDatabase 加载。
    //   所以对比图必须**先由编辑器侧把 prefab 实例预置进场景**，Play 模式只管摆位拍照。
    //   这里就按这个约定：场景里已有 Z_Rig_* 容器，从中取。
    var rig = GameObject.Find("Z_Rig");
    if (rig == null)
    {
        sb.AppendLine("!! 场景里没有 Z_Rig 容器 —— 需要先跑 z12a_prepare（编辑器侧预置实例）");
        File.WriteAllText(Path.Combine(projRoot, "Tools/reports/z12_sheet.txt"), sb.ToString());
        Debug.LogError("[z12_sheet] 缺 Z_Rig");
        yield break;
    }
    sb.AppendLine("Z_Rig 已找到，子对象:");
    var units = new List<GameObject>();
    for (int i = 0; i < rig.transform.childCount; i++)
    {
        var c = rig.transform.GetChild(i).gameObject;
        units.Add(c);
        sb.AppendLine("   - " + c.name);
    }

    yield return null;

    // ===== A. 阵容对比：一字排开 =====
    float spacing = 4.0f;
    float x = -(units.Count - 1) * 0.5f * spacing;
    var placed = new List<(GameObject go, float h, float len)>();
    foreach (var u in units)
    {
        // 先归零位姿再量
        u.transform.position = Vector3.zero;
        u.transform.rotation = Quaternion.identity;
        var vb = VertexBounds(u);
        float foot = vb.min.y;
        float h = vb.size.y, len = vb.size.z;

        u.transform.position = origin + new Vector3(x, -foot, 0f);
        u.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
        placed.Add((u, h, len));
        sb.AppendLine($"  摆位 {u.name}: h={h:F2} len={len:F2} foot={foot:F3} x={x:F2}");
        x += spacing;
    }

    float totalW = (units.Count - 1) * spacing;

    // A: 正面全景
    var center = origin + new Vector3(0f, 1.3f, 0f);
    Shoot(1600, 640, center + new Vector3(0f, 1.8f, -totalW * 0.95f), center, 42f, "A1_阵容_水墨");

    // A2: 稍侧一点，看清厚度
    Shoot(1600, 640, center + new Vector3(-totalW * 0.5f, 2.6f, -totalW * 0.8f), center, 45f, "A2_阵容_侧");

    // ===== B. 比例对照：新怪 vs 现有 KayKit =====
    var kayNames = new[] { "Enemy_MoOu", "Enemy_MoTu", "Enemy_MoYan" };
    var kays = new List<GameObject>();
    float kx = 0f;
    foreach (var kn in kayNames)
    {
        var go = GameObject.Find(kn);
        if (go == null)
        {
            // 场景里没有就跳过
            sb.AppendLine("  (场景里没有 " + kn + ")");
            continue;
        }
        go.transform.position = origin + new Vector3(kx, 0.05f, 9f);
        kays.Add(go);
        kx += 2.0f;
    }
    sb.AppendLine($"KayKit 对照 {kays.Count} 个");
    yield return null;

    if (kays.Count > 0)
    {
        var kc = origin + new Vector3(kx * 0.5f - 1f, 1.0f, 9f);
        Shoot(1300, 560, kc + new Vector3(0f, 1.5f, -8.5f), kc, 45f, "B1_KayKit对照");
    }

    // ===== C. Boss 特写 =====
    var dragon = units.Find(u => u.name.Contains("Dragon") && !u.name.Contains("LP"));
    if (dragon != null)
    {
        var vb = VertexBounds(dragon);
        var c = vb.center;
        Shoot(1100, 700, c + new Vector3(0f, 1.6f, -vb.size.z * 0.75f), c, 48f, "C1_Boss龙_近景");
        Shoot(1100, 700, c + new Vector3(vb.size.z * 0.55f, 2.2f, -vb.size.z * 0.55f), c, 45f, "C2_Boss龙_斜侧");
    }

    // ===== D. 俯视全景 =====
    Shoot(1200, 800, origin + new Vector3(0f, 22f, 2f), origin + new Vector3(0f, 0f, 2f), 42f, "D1_俯视全景");

    File.WriteAllText(Path.Combine(projRoot, "Tools/reports/z12_sheet.txt"), sb.ToString());
    Debug.Log("[z12_sheet] done\n" + sb.ToString());
    yield return null;
}

return Body();
