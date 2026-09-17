// 把 Feng 渲染出来看：三视图（绑定姿势）+ 半身特写 + 自然站姿 + 与现主角同框对比
// 全程编辑器模式，不进出 Play。输出 Tools/screenshots/feng/
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

IEnumerator Body()
{
    var sb = new System.Text.StringBuilder();
    string projRoot = System.IO.Path.GetDirectoryName(Application.dataPath);
    string imgDir = System.IO.Path.Combine(projRoot, "Tools/screenshots/feng");
    System.IO.Directory.CreateDirectory(imgDir);

    var fbx = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Char_Feng/Fbx/Feng.fbx");
    if (fbx == null) { Debug.LogError("Feng.fbx 载入失败"); yield break; }
    var playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Player/Player.prefab");
    sb.AppendLine("Player.prefab = " + (playerPrefab != null ? "OK" : "NULL"));

    // ---- 光照 ----
    var lightGo = new GameObject("__ProbeLight");
    var dl = lightGo.AddComponent<Light>();
    dl.type = LightType.Directional; dl.intensity = 1.35f; dl.color = new Color(1f, 0.97f, 0.92f);
    lightGo.transform.rotation = Quaternion.Euler(42f, 32f, 0f);
    var fillGo = new GameObject("__ProbeFill");
    var fl = fillGo.AddComponent<Light>();
    fl.type = LightType.Directional; fl.intensity = 0.45f; fl.color = new Color(0.72f, 0.8f, 0.95f);
    fillGo.transform.rotation = Quaternion.Euler(-18f, -140f, 0f);
    var prevAmbMode = RenderSettings.ambientMode; var prevAmb = RenderSettings.ambientLight;
    RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
    RenderSettings.ambientLight = new Color(0.34f, 0.36f, 0.42f);

    var hidden = new List<Renderer>();
    foreach (var r in Object.FindObjectsOfType<Renderer>())
        if (r.enabled) { r.enabled = false; hidden.Add(r); }

    var camGo = new GameObject("__ProbeCam");
    var cam = camGo.AddComponent<Camera>();
    cam.clearFlags = CameraClearFlags.SolidColor;
    cam.backgroundColor = new Color(0.145f, 0.155f, 0.175f);
    cam.fieldOfView = 32f; cam.nearClipPlane = 0.01f; cam.farClipPlane = 100f; cam.enabled = false;

    var feng = Object.Instantiate(fbx);
    feng.name = "__Feng";
    feng.transform.position = Vector3.zero;
    feng.transform.rotation = Quaternion.identity;
    feng.transform.localScale = Vector3.one;

    float fMin = MeasureMin(feng), fMax = MeasureMax(feng), h = fMax - fMin;
    sb.AppendLine(string.Format("Feng 身高 = {0:F4}  (y {1:F4}..{2:F4})", h, fMin, fMax));
    var an = feng.GetComponentInChildren<Animator>();
    sb.AppendLine("Feng Animator = " + (an != null ? ("有，avatar=" + (an.avatar != null ? an.avatar.name : "NULL")) : "无"));

    // 取景：让整体身高在竖直方向留 12% 余量
    float DistFor(float height)
    {
        return (height * 1.16f) / (2f * Mathf.Tan(Mathf.Deg2Rad * cam.fieldOfView * 0.5f));
    }
    void Aim(Vector3 center, Vector3 dir, float dist)
    {
        Vector3 camPos = center + dir * dist;
        cam.transform.position = camPos;
        cam.transform.rotation = Quaternion.LookRotation(center - camPos, Vector3.up);
    }

    // ---- 三视图（绑定姿势）----
    int CW = 620, CH = 900;
    foreach (var v in new[] { "front", "side", "back" })
    {
        Vector3 center = new Vector3(0f, (fMin + fMax) * 0.5f, 0f);
        Vector3 dir = v == "front" ? Vector3.forward : (v == "side" ? Vector3.right : Vector3.back);
        Aim(center, dir, DistFor(h));
        ShootPNG(cam, CW, CH, System.IO.Path.Combine(imgDir, "feng_" + v + ".png"));
        yield return null;
    }

    // ---- 半身特写 ----
    {
        Vector3 center = new Vector3(0f, fMin + h * 0.84f, 0f);
        Aim(center, Vector3.forward, DistFor(h * 0.44f));
        ShootPNG(cam, 700, 700, System.IO.Path.Combine(imgDir, "feng_bust.png"));
        yield return null;
    }

    // ---- 自然站姿：把 Idle 片段采样到某一帧 ----
    var idle = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Char_Feng/Animation/Idle.anim");
    if (idle != null && an != null && an.avatar != null)
    {
        idle.SampleAnimation(feng, 0.65f);
        yield return null;
        float aMin = MeasureMin(feng), aMax = MeasureMax(feng);
        sb.AppendLine(string.Format("采样 Idle@0.65s 后身高 = {0:F4}", aMax - aMin));
        Aim(new Vector3(0f, (aMin + aMax) * 0.5f, 0f), Vector3.forward, DistFor(aMax - aMin));
        ShootPNG(cam, CW, CH, System.IO.Path.Combine(imgDir, "feng_idlepose.png"));
        yield return null;
        Aim(new Vector3(0f, (aMin + aMax) * 0.5f, 0f), (Vector3.forward + Vector3.right * 0.7f).normalized, DistFor(aMax - aMin));
        ShootPNG(cam, CW, CH, System.IO.Path.Combine(imgDir, "feng_idlepose45.png"));
        yield return null;
        // 复位到绑定姿势
        feng.transform.position = Vector3.zero;
    }
    else sb.AppendLine("跳过 Idle 采样（clip 或 avatar 缺失）");

    // ---- 与现主角同框 ----
    if (playerPrefab != null)
    {
        var pl = (GameObject)PrefabUtility.InstantiatePrefab(playerPrefab);
        pl.name = "__PlayerCompare";
        foreach (var mb in pl.GetComponentsInChildren<MonoBehaviour>(true)) if (mb != null) mb.enabled = false;
        var pRoot = pl.transform;
        pRoot.rotation = Quaternion.identity;
        float pMin = MeasureMin(pl), pMax = MeasureMax(pl), pH = pMax - pMin;
        sb.AppendLine(string.Format("Player 身高 = {0:F4}  (y {1:F4}..{2:F4})   需要放大倍数 = {3:F4}", pH, pMin, pMax, pH / h));

        var vis = pRoot.Find("Visual");
        if (vis != null) sb.AppendLine(string.Format("Player/Visual localScale={0} localPos={1}", vis.localScale.ToString("F3"), vis.localPosition.ToString("F4")));

        // (a) 真实比例同框
        feng.transform.position = new Vector3(-0.62f, -fMin, 0f);
        pRoot.position = new Vector3(0.62f, -pMin, 0f);
        {
            float top = Mathf.Max(pMax, feng.transform.position.y + fMax);
            Vector3 center = new Vector3(0f, top * 0.5f, 0f);
            cam.fieldOfView = 30f;
            Aim(center, Vector3.forward, Mathf.Max(pH, h) * 1.75f);
            ShootPNG(cam, 1000, 760, System.IO.Path.Combine(imgDir, "feng_vs_player_raw.png"));
            yield return null;
        }

        // (b) Feng 放大到 Player 身高后的同框（这才是最终形态）
        float k = pH / h;
        feng.transform.localScale = new Vector3(k, k, k);
        feng.transform.position = new Vector3(-0.62f, 0f, 0f);
        // 重新对齐脚底
        feng.transform.position = new Vector3(-0.62f, -MeasureMin(feng), 0f);
        {
            float top = Mathf.Max(pMax, MeasureMax(feng));
            Vector3 center = new Vector3(0f, top * 0.5f, 0f);
            cam.fieldOfView = 30f;
            Aim(center, Vector3.forward, pH * 1.9f);
            ShootPNG(cam, 1000, 760, System.IO.Path.Combine(imgDir, "feng_vs_player_scaled.png"));
            yield return null;
            // 侧面同框（看厚度/朝向是否一致）
            Aim(center, (Vector3.forward * 0.35f + Vector3.right).normalized, pH * 1.9f);
            ShootPNG(cam, 1000, 760, System.IO.Path.Combine(imgDir, "feng_vs_player_scaled_side.png"));
            yield return null;
        }
        Object.DestroyImmediate(pl);
    }
    else sb.AppendLine("Player.prefab 未找到，跳过同框对比");

    Object.DestroyImmediate(feng);
    Object.DestroyImmediate(camGo);
    Object.DestroyImmediate(lightGo);
    Object.DestroyImmediate(fillGo);
    foreach (var r in hidden) if (r != null) r.enabled = true;
    RenderSettings.ambientMode = prevAmbMode; RenderSettings.ambientLight = prevAmb;

    System.IO.File.WriteAllText(System.IO.Path.Combine(projRoot, "Tools/reports/f_shot.txt"), sb.ToString());
    Debug.Log(sb.ToString());
    yield return null;
}

float MeasureMin(GameObject go)
{
    float mn = float.MaxValue;
    foreach (var r in go.GetComponentsInChildren<Renderer>(true))
    { if (r is TrailRenderer) continue; mn = Mathf.Min(mn, r.bounds.min.y); }
    return mn;
}
float MeasureMax(GameObject go)
{
    float mx = float.MinValue;
    foreach (var r in go.GetComponentsInChildren<Renderer>(true))
    { if (r is TrailRenderer) continue; mx = Mathf.Max(mx, r.bounds.max.y); }
    return mx;
}
void ShootPNG(Camera c, int w, int hgt, string path)
{
    var rt = RenderTexture.GetTemporary(w, hgt, 24);
    c.targetTexture = rt; c.Render(); c.targetTexture = null;
    var prev = RenderTexture.active; RenderTexture.active = rt;
    var snap = new Texture2D(w, hgt, TextureFormat.RGB24, false);
    snap.ReadPixels(new Rect(0, 0, w, hgt), 0, 0); snap.Apply();
    RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt);
    System.IO.File.WriteAllBytes(path, snap.EncodeToPNG());
    Object.DestroyImmediate(snap);
}

return Body();
