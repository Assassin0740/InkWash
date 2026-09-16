// a40_dragon_shader.cs —— 龙的 SMR 齐备但画面空白：把疑点逐个剃掉
//
// 已知（a38/a39 可信数据）：
//   · 网格顶点真值 = 779.8 × 4969.4 × 542.0（源单位，厘米级）
//   · smr.lossyScale = 0.001811 ⇒ 换算成米 ≈ 1.41 × 9.00 × 0.98 m
//   · smr.bounds 世界 = 6.044 × 5.871 × 2.588（含 AABB 散开）
//   · 骨架世界范围 = 4.38 × 1.64 × 4.27 m
//   ⇒ **资产是好的**（网格、蒙皮、骨骼、材质绑定、层级都正常），
//      但 a34/a37/a39 三轮渲染都只有参照方块。
//
// 本脚本要剃的疑点（一次一个，全部输出到报告）：
//   ① 【剔除】`Object_281` 的 smr.bounds 中心是否离相机太远 / 是否被视锥剔除
//   ② 【材质】M_Ink_Boss_Dragon 的 shader 真的能出像素吗（换 URP/Unlit 品红对照）
//   ③ 【几何】用 `MeshRenderer + MeshFilter` 直接挂同一条 Mesh 看能不能出图
//      （能出 ⇒ 是蒙皮/骨骼问题；不能出 ⇒ 是网格本身的问题）
//   ④ 【骨骼】把 skinned 的 bones 逐个列出来看有没有 null / 世界位置是否合理
//   ⑤ 【AABB】打印 smr.localBounds 与 mesh.bounds，确认 Unity 认为它有多大
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

var sb = new StringBuilder();
string root = Path.GetDirectoryName(Application.dataPath);
string shotDir = Path.Combine(root, "Tools/screenshots/dragon40");
Directory.CreateDirectory(shotDir);

sb.AppendLine("========== a40 龙渲染空白的逐个排除 ==========");

const string EnemyDir = "Assets/_Project/Prefabs/Enemies";
string outPath = EnemyDir + "/Z_Enemy_MoLong.prefab";
var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(outPath);
var mat = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/Materials/M_Ink_Boss_Dragon.mat");
sb.AppendLine("prefab = " + (prefab != null ? "ok" : "null"));
sb.AppendLine("mat    = " + (mat != null ? mat.name + " shader=" + (mat.shader != null ? mat.shader.name : "NULL") : "null"));

// 建临时场景
var sc = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

var gnd = GameObject.CreatePrimitive(PrimitiveType.Cube);
gnd.transform.position = new Vector3(0f, -0.05f, 0f);
gnd.transform.localScale = new Vector3(80f, 0.1f, 80f);

var lg = new GameObject("Sun");
var lt = lg.AddComponent<Light>(); lt.type = LightType.Directional; lt.intensity = 1.4f;
lg.transform.rotation = Quaternion.Euler(48f, -30f, 0f);

var camGo = new GameObject("Cam");
var cam = camGo.AddComponent<Camera>();
cam.clearFlags = CameraClearFlags.SolidColor;
cam.backgroundColor = new Color(0.93f, 0.93f, 0.91f, 1f);
cam.farClipPlane = 2000f;

var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
inst.transform.position = Vector3.zero;
inst.transform.rotation = Quaternion.identity;

// ---------- ④ 骨骼清单 ----------
sb.AppendLine();
sb.AppendLine("---- ④ 骨骼清单 ----");
var allSmr = inst.GetComponentsInChildren<SkinnedMeshRenderer>(true);
sb.AppendLine("  SkinnedMeshRenderer 数 = " + allSmr.Length);
foreach (var smr in allSmr)
{
    int total = smr.bones != null ? smr.bones.Length : -1;
    int nn = 0; int bad = 0;
    Vector3 mn = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
    Vector3 mx = new Vector3(float.MinValue, float.MinValue, float.MinValue);
    if (smr.bones != null)
        foreach (var b in smr.bones)
        {
            if (b == null) { bad++; continue; }
            nn++;
            var p = b.position;
            if (float.IsNaN(p.x) || float.IsInfinity(p.x) || Mathf.Abs(p.x) > 1e5f
                || Mathf.Abs(p.y) > 1e5f || Mathf.Abs(p.z) > 1e5f) continue;
            mn = Vector3.Min(mn, p); mx = Vector3.Max(mx, p);
        }
    sb.AppendLine("  " + smr.name + ": bones=" + total + " 非null=" + nn + " null=" + bad);
    sb.AppendLine("     rootBone=" + (smr.rootBone != null ? smr.rootBone.name : "NULL"));
    sb.AppendLine("     smr.lossyScale=" + smr.transform.lossyScale.ToString("F5"));
    sb.AppendLine("     smr.bounds 世界 center=" + smr.bounds.center.ToString("F3") + " size=" + smr.bounds.size.ToString("F3"));
    sb.AppendLine("     smr.localBounds center=" + smr.localBounds.center.ToString("F3") + " size=" + smr.localBounds.size.ToString("F3"));
    sb.AppendLine("     mesh.bounds size=" + smr.sharedMesh.bounds.size.ToString("F2"));
    if (nn > 0) sb.AppendLine("     骨骼世界范围 [" + mn.ToString("F3") + "] ~ [" + mx.ToString("F3") + "]");
    sb.AppendLine("     updateWhenOffscreen=" + smr.updateWhenOffscreen + "  quality=" + smr.quality);
    sb.AppendLine("     enabled=" + smr.enabled + "  activeInHierarchy=" + smr.gameObject.activeInHierarchy);
    sb.AppendLine("     layer=" + smr.gameObject.layer);
    var ms = smr.sharedMaterials;
    sb.AppendLine("     materials=" + ms.Length + " → " + (ms.Length > 0 && ms[0] != null ? ms[0].name : "NULL"));
    if (ms.Length > 0 && ms[0] != null && ms[0].shader != null)
        sb.AppendLine("     shader=" + ms[0].shader.name + " passCount=" + ms[0].shader.passCount
                      + " isSupported=" + ms[0].shader.isSupported);
    // submesh 与顶点
    sb.AppendLine("     vertexCount=" + smr.sharedMesh.vertexCount + " subMeshCount=" + smr.sharedMesh.subMeshCount);
    for (int i = 0; i < smr.sharedMesh.subMeshCount; i++)
        sb.AppendLine("       subMesh[" + i + "] indexCount=" + smr.sharedMesh.GetIndexCount(i));
}

// ---------- 相机 & 截图 ----------
void Shoot(string name, Vector3 pos, Vector3 look, float fov)
{
    camGo.transform.position = pos;
    camGo.transform.LookAt(look);
    cam.fieldOfView = fov;
    var rt = new RenderTexture(900, 700, 24, RenderTextureFormat.ARGB32);
    rt.antiAliasing = 2;
    cam.targetTexture = rt;
    cam.Render();
    RenderTexture.active = rt;
    var t = new Texture2D(900, 700, TextureFormat.RGB24, false);
    t.ReadPixels(new Rect(0, 0, 900, 700), 0, 0);
    t.Apply();
    RenderTexture.active = null;
    cam.targetTexture = null;
    File.WriteAllBytes(Path.Combine(shotDir, name + ".png"), t.EncodeToPNG());
    UnityEngine.Object.DestroyImmediate(t);
    rt.Release(); UnityEngine.Object.DestroyImmediate(rt);
    sb.AppendLine("  已存 " + name + ".png  (相机 " + pos.ToString("F1") + " → " + look.ToString("F1") + " fov" + fov.ToString("F0") + ")");
}

// 先在原位拍一张
sb.AppendLine();
sb.AppendLine("---- 基线拍摄（原样，材质 = M_Ink_Boss_Dragon）----");
Shoot("B0_原位_侧", new Vector3(-14f, 6f, 0f), new Vector3(0f, 1.5f, 0f), 40f);
Shoot("B1_原位_俯", new Vector3(0.01f, 18f, 0.01f), Vector3.zero, 46f);

// ---------- ② 换 URP/Unlit 品红对照 ----------
sb.AppendLine();
sb.AppendLine("---- ② 换 URP/Unlit 品红（把 shader 从疑点里剃掉）----");
var unlit = Shader.Find("Universal Render Pipeline/Unlit");
sb.AppendLine("  Shader.Find(URP/Unlit) = " + (unlit != null ? "ok" : "NULL"));
Material magenta = null;
if (unlit != null)
{
    magenta = new Material(unlit);
    magenta.SetColor("_BaseColor", new Color(0.95f, 0.1f, 0.75f, 1f));
    if (magenta.HasProperty("_BaseMap")) magenta.SetTexture("_BaseMap", null);
    foreach (var smr in allSmr)
    {
        var arr = new Material[smr.sharedMaterials.Length];
        for (int i = 0; i < arr.Length; i++) arr[i] = magenta;
        smr.sharedMaterials = arr;
    }
    Shoot("C0_品红_侧", new Vector3(-14f, 6f, 0f), new Vector3(0f, 1.5f, 0f), 40f);
    Shoot("C1_品红_俯", new Vector3(0.01f, 18f, 0.01f), Vector3.zero, 46f);
}

// ---------- ③ 用静态 MeshRenderer 挂同一条 Mesh ----------
sb.AppendLine();
sb.AppendLine("---- ③ 静态 MeshRenderer 挂同一 Mesh（剔除蒙皮/骨骼）----");
{
    var src = allSmr.Length > 0 ? allSmr[0].sharedMesh : null;
    if (src != null)
    {
        var go = new GameObject("StaticProbe");
        var mf = go.AddComponent<MeshFilter>();
        mf.sharedMesh = src;
        var mr = go.AddComponent<MeshRenderer>();
        // 用顶点真值把静态网格缩到可见尺度（源单位是厘米级）
        float k = 0.001811f;
        go.transform.localScale = Vector3.one * k;
        go.transform.position = new Vector3(6f, 4.5f, 0f);
        var m2 = magenta != null ? magenta : mat;
        if (m2 != null) mr.sharedMaterial = m2;
        sb.AppendLine("  StaticProbe: mesh=" + src.name + " vertexCount=" + src.vertexCount
                      + " scale=" + k + " 位置=" + go.transform.position.ToString("F1"));
        Shoot("D0_静态网格_侧", new Vector3(-14f, 6f, 0f), new Vector3(0f, 1.5f, 0f), 40f);
        Shoot("D1_静态网格_近", new Vector3(6f, 6f, -12f), new Vector3(6f, 4.5f, 0f), 40f);
        UnityEngine.Object.DestroyImmediate(go);
    }
    else sb.AppendLine("  ★ 没有可用 mesh");
}

// ---------- ① 把 SMR 搬到相机正前方并放大，排除"位置/尺度"疑点 ----------
sb.AppendLine();
sb.AppendLine("---- ① 复位到相机正前方 + 统一放大 10 倍（排除位置/尺度）----");
{
    inst.transform.position = Vector3.zero;
    inst.transform.rotation = Quaternion.identity;
    inst.transform.localScale = Vector3.one * 10f;
    foreach (var smr in allSmr)
    {
        smr.updateWhenOffscreen = true;   // 关掉"离屏不更新"
        if (magenta != null)
        {
            var arr = new Material[smr.sharedMaterials.Length];
            for (int i = 0; i < arr.Length; i++) arr[i] = magenta;
            smr.sharedMaterials = arr;
        }
        else if (mat != null)
        {
            var arr = new Material[smr.sharedMaterials.Length];
            for (int i = 0; i < arr.Length; i++) arr[i] = mat;
            smr.sharedMaterials = arr;
        }
    }
    foreach (var smr in allSmr)
        sb.AppendLine("  " + smr.name + " 放大后 bounds size=" + smr.bounds.size.ToString("F2")
                      + " center=" + smr.bounds.center.ToString("F2"));
    Shoot("E0_放大10x_侧", new Vector3(-40f, 14f, 0f), new Vector3(0f, 6f, 0f), 40f);
    Shoot("E1_放大10x_俯", new Vector3(0.01f, 45f, 0.01f), Vector3.zero, 46f);
    inst.transform.localScale = Vector3.one;
}

EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
EditorSceneManager.OpenScene("Assets/_Project/Scenes/Main.unity", OpenSceneMode.Single);

File.WriteAllText(Path.Combine(root, "Tools/reports/a40_dragon_shader.txt"), sb.ToString());
Debug.Log("[a40]\n" + sb.ToString());
