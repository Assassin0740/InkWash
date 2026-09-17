// W_Sword 结构可视化 + 材质 A/B。
//
// 为什么要带"测量标尺"：护手/握柄的边界是我用"截面宽度突变"反推的，反推可能错。
// 在量出的每个 Y 边界处放一根彩色横杠，渲染出来就能直接看出标尺与解剖结构对不对得上
// —— 比再算一遍数字可靠得多。
//
// 材质 A/B：导入器生成的材质 _Metallic = 0 且 _Smoothness = 0.1035（哑光非金属），
// 观感是"塑料剑"。同机位再渲一张金属参数版做对照。
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

string OUT_IMG = "Tools/screenshots/sword_probe";

IEnumerator Body()
{
    var sb = new System.Text.StringBuilder();
    string projRoot = System.IO.Path.GetDirectoryName(Application.dataPath);
    string imgDir = System.IO.Path.Combine(projRoot, OUT_IMG);
    System.IO.Directory.CreateDirectory(imgDir);
    System.Action flush = () => System.IO.File.WriteAllText(
        System.IO.Path.Combine(projRoot, "Tools/reports/w_shot.txt"), sb.ToString());

    const string FBX = "Assets/W_Sword/W_Sword.FBX";

    // ---- 场景光照环境（金属材质强依赖环境反射，先确认有没有环境）----
    sb.AppendLine("========== 场景光照环境 ==========");
    sb.AppendLine("RenderSettings.skybox = " + (RenderSettings.skybox != null ? RenderSettings.skybox.name : "NULL"));
    sb.AppendLine("ambientMode = " + RenderSettings.ambientMode);
    sb.AppendLine("ambientLight = " + RenderSettings.ambientLight.ToString("F3"));
    sb.AppendLine("ambientIntensity = " + RenderSettings.ambientIntensity.ToString("F3"));
    sb.AppendLine("defaultReflectionMode = " + RenderSettings.defaultReflectionMode);
    sb.AppendLine("defaultReflectionResolution = " + RenderSettings.defaultReflectionResolution);
    var lights = Object.FindObjectsOfType<Light>();
    sb.AppendLine("场景灯光 " + lights.Length + " 盏：");
    foreach (var l in lights)
        sb.AppendLine(string.Format("  [{0}] {1}  dir={2:F3}  intensity={3:F3}  shadows={4}",
            l.type, l.name, l.transform.forward.ToString("F3"), l.intensity, l.shadows));
    var probes = Object.FindObjectsOfType<ReflectionProbe>();
    sb.AppendLine("反射探针 " + probes.Length + " 个");
    sb.AppendLine();

    var fbxAsset = AssetDatabase.LoadAssetAtPath<GameObject>(FBX);
    if (fbxAsset == null) { sb.AppendLine("[ERR] 加载不到 FBX"); flush(); yield break; }

    // 放到远处，避免框到竞技场
    Vector3 basePos = new Vector3(0f, 800f, 0f);
    var inst = (GameObject)PrefabUtility.InstantiatePrefab(fbxAsset);
    inst.transform.position = basePos;
    inst.transform.rotation = Quaternion.identity;
    inst.transform.localScale = Vector3.one;

    // ---- 测量标尺：在量出的 Y 边界处放彩色横杠 ----
    // 顺序与颜色在报告里给出图例
    var marks = new[]
    {
        new { y = -0.5988f, c = new Color(1f, 0.15f, 0.10f), n = "剑尖" },
        new { y =  0.1497f, c = new Color(1f, 0.85f, 0.00f), n = "剑身/护手 分界" },
        new { y =  0.3000f, c = new Color(0.10f, 0.90f, 0.25f), n = "护手/握柄 分界" },
        new { y =  0.5100f, c = new Color(0.10f, 0.80f, 1.00f), n = "握柄/剑首 分界" },
        new { y =  0.5988f, c = new Color(1f, 0.10f, 0.85f), n = "剑首端" },
    };
    sb.AppendLine("========== 标尺图例（横杠按模型本地 Y 放置，伸向 +X 一侧）==========");
    var markGos = new List<GameObject>();
    foreach (var mk in marks)
    {
        sb.AppendLine(string.Format("  color RGB({0:F2},{1:F2},{2:F2})  Y = {3:F4}   ← {4}",
            mk.c.r, mk.c.g, mk.c.b, mk.y, mk.n));

        var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
        g.name = "Mark_" + mk.n;
        Object.DestroyImmediate(g.GetComponent<Collider>());
        g.transform.SetParent(inst.transform, false);
        // 从 x=0.13 伸到 x=0.30，避免压住剑本身（护手半展 0.1162）
        g.transform.localPosition = new Vector3(0.215f, mk.y, 0f);
        g.transform.localScale = new Vector3(0.17f, 0.004f, 0.004f);
        var mr = g.GetComponent<MeshRenderer>();
        var m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        m.SetColor("_BaseColor", mk.c); m.SetColor("_Color", mk.c);
        mr.sharedMaterial = m;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        markGos.Add(g);
    }
    sb.AppendLine();

    var rend = inst.GetComponentInChildren<MeshRenderer>();
    var srcMat = rend != null ? rend.sharedMaterial : null;
    sb.AppendLine("原始材质 = " + (srcMat != null ? srcMat.name : "null"));

    // 金属版材质（A/B 用）
    Material metalMat = null;
    var sh = Shader.Find("Universal Render Pipeline/Lit");
    if (sh != null)
    {
        metalMat = new Material(sh) { name = "M_W_Sword_metal_test" };
        if (srcMat != null && srcMat.HasProperty("_BaseMap")) metalMat.SetTexture("_BaseMap", srcMat.GetTexture("_BaseMap"));
        if (srcMat != null && srcMat.HasProperty("_BumpMap")) metalMat.SetTexture("_BumpMap", srcMat.GetTexture("_BumpMap"));
        if (srcMat != null && srcMat.HasProperty("_BaseColor")) metalMat.SetColor("_BaseColor", srcMat.GetColor("_BaseColor"));
        if (srcMat != null && srcMat.HasProperty("_Color")) metalMat.SetColor("_Color", srcMat.GetColor("_Color"));
        metalMat.EnableKeyword("_NORMALMAP");
        metalMat.SetFloat("_Metallic", 0.65f);
        metalMat.SetFloat("_Smoothness", 0.52f);
        metalMat.SetFloat("_WorkflowMode", 1f);
    }

    // ---- 相机 ----
    var camGo = new GameObject("TmpSwordCam");
    var cam = camGo.AddComponent<Camera>();
    var mainCam = Camera.main;
    if (mainCam != null) cam.CopyFrom(mainCam);
    cam.tag = "Untagged";
    cam.enabled = false;
    cam.clearFlags = CameraClearFlags.SolidColor;
    cam.backgroundColor = new Color(0.16f, 0.17f, 0.20f);
    cam.allowHDR = false;

    Vector3 center = basePos;   // 模型原点在几何中心
    float halfLen = 0.5988f;

    // 侧视（沿 -Z 看）：X 水平 = 护手展向，能看清护手宽度
    cam.orthographic = true;
    cam.orthographicSize = halfLen * 1.12f;
    Shot(cam, imgDir, "sword_side", 420, 900, center, Vector3.forward * 6f);

    // 正视（沿 -X 看）：Z 水平 = 剑身厚向
    Shot(cam, imgDir, "sword_front", 420, 900, center, Vector3.right * 6f);

    // 护手+柄 局部特写（Y 0.05 .. 0.75）
    cam.orthographic = true;
    cam.orthographicSize = 0.36f;
    Shot(cam, imgDir, "sword_hilt", 600, 640, center + new Vector3(0f, 0.40f, 0f), Vector3.forward * 6f);
    Shot(cam, imgDir, "sword_hilt_front", 600, 640, center + new Vector3(0f, 0.40f, 0f), Vector3.right * 6f);

    // 剑身特写（Y -0.62 .. -0.2），看刃线
    cam.orthographicSize = 0.26f;
    Shot(cam, imgDir, "sword_blade", 600, 640, center + new Vector3(0f, -0.42f, 0f), Vector3.forward * 6f);

    // 3/4 透视全貌
    cam.orthographic = false;
    cam.fieldOfView = 32f;
    Shot(cam, imgDir, "sword_34", 520, 880, center, new Vector3(0.72f, 0.30f, 1f).normalized * 3.2f);

    // ---- 换金属材质再来一遍 ----
    if (metalMat != null && rend != null)
    {
        rend.sharedMaterial = metalMat;
        sb.AppendLine("金属材质参数：_Metallic=0.65  _Smoothness=0.52");
        Shot(cam, imgDir, "sword_side_metal", 420, 900, center, Vector3.forward * 6f);
        cam.fieldOfView = 32f; cam.orthographic = false;
        Shot(cam, imgDir, "sword_34_metal", 520, 880, center, new Vector3(0.72f, 0.30f, 1f).normalized * 3.2f);
        Shot(cam, imgDir, "sword_hilt_34_metal", 600, 640, center + new Vector3(0f, 0.40f, 0f), new Vector3(0.6f, 0.2f, 1f).normalized * 1.6f);
    }

    foreach (var g in markGos) Object.DestroyImmediate(g);
    Object.DestroyImmediate(camGo);
    Object.DestroyImmediate(inst);
    if (metalMat != null) Object.DestroyImmediate(metalMat);

    sb.AppendLine();
    sb.AppendLine("出图目录：" + imgDir);
    flush();
    Debug.Log(sb.ToString());
}

void Shot(Camera cam, string imgDir, string name, int W, int H, Vector3 center, Vector3 off)
{
    Vector3 camPos = center + off;
    cam.transform.position = camPos;
    cam.transform.rotation = Quaternion.LookRotation(center - camPos, Vector3.up);

    var rt = RenderTexture.GetTemporary(W, H, 24);
    cam.targetTexture = rt; cam.Render(); cam.targetTexture = null;
    var prev = RenderTexture.active;
    RenderTexture.active = rt;
    var snap = new Texture2D(W, H, TextureFormat.RGB24, false);
    snap.ReadPixels(new Rect(0, 0, W, H), 0, 0); snap.Apply();
    RenderTexture.active = prev;
    RenderTexture.ReleaseTemporary(rt);
    System.IO.File.WriteAllBytes(System.IO.Path.Combine(imgDir, name + ".png"), snap.EncodeToPNG());
    Object.DestroyImmediate(snap);
}

return Body();
