// g-2：按用户机位渲染 + 逐物体排除法，定位"地上那条斜带"到底是什么
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using InkWash.CameraRig;

var sb = new StringBuilder();
string projRoot = Path.GetDirectoryName(Application.dataPath);
string shotDir = Path.Combine(projRoot, "Tools/screenshots/diag");
Directory.CreateDirectory(shotDir);

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

void Shoot(Camera cam, int W, int H, Vector3 pos, Vector3 euler, float fov, string tag)
{
    cam.transform.position = pos;
    cam.transform.rotation = Quaternion.Euler(euler);
    cam.fieldOfView = fov;
    var t = RenderFrame(cam, W, H);
    if (t == null) { sb.AppendLine("  !! 渲染失败 " + tag); return; }
    try { File.WriteAllBytes(Path.Combine(shotDir, tag + ".png"), t.EncodeToPNG()); } catch { }
    UnityEngine.Object.Destroy(t);
    sb.AppendLine("  已存 " + tag + ".png");
}

IEnumerator Body()
{
    int W = 1280, H = 720;
    yield return null; yield return null;

    var cam = Camera.main;
    sb.AppendLine("相机 = " + (cam == null ? "null" : cam.name));
    if (cam == null) { Debug.LogError("[g_shots] 无相机"); yield break; }

    // 停掉第三人称跟随，才能手工摆机位
    var tpc = UnityEngine.Object.FindObjectOfType<ThirdPersonCamera>();
    if (tpc != null) { tpc.enabled = false; sb.AppendLine("已停用 ThirdPersonCamera"); }

    // 先记下"玩家实机视角"（跟随刚停下时的那一帧）
    yield return null;
    var playPos = cam.transform.position; var playRot = cam.transform.eulerAngles; var playFov = cam.fieldOfView;
    Shoot(cam, W, H, playPos, playRot, playFov, "A0_玩家实机视角");
    sb.AppendLine("    玩家机位 " + playPos.ToString("F2") + " rot=" + playRot.ToString("F1") + " fov=" + playFov);

    // 用户截图机位
    var uPos = new Vector3(-7.43f, 3.59f, -0.31f);
    var uRot = new Vector3(25.4f, 79.1f, 0f);
    Shoot(cam, W, H, uPos, uRot, 48f, "A1_用户机位_全部");
    yield return null;

    // 同一帧内做排除法：逐组隐藏后立刻再渲（中间不 yield ⇒ 光照/姿势不变）
    var groups = new (string tag, string[] roots)[]
    {
        ("B_隐藏Gates石门", new[] { "Gates" }),
        ("C_隐藏Ground", new[] { "Environment/Ground" }),
        ("D_隐藏Arena台", new[] { "Environment/Arena" }),
        ("E_隐藏Walls院墙", new[] { "Environment/Wall_N", "Environment/Wall_S", "Environment/Wall_E", "Environment/Wall_W" }),
    };
    foreach (var g in groups)
    {
        var objs = new List<GameObject>();
        foreach (var p in g.roots)
        {
            var go = GameObject.Find(p);
            if (go == null) { sb.AppendLine("  ?? 找不到 " + p); continue; }
            objs.Add(go); go.SetActive(false);
        }
        Shoot(cam, W, H, uPos, uRot, 48f, g.tag);
        foreach (var go in objs) go.SetActive(true);
    }

    // 平视看那条带子 —— 如果它是一堵墙，贴地平视会变成竖直的面
    Shoot(cam, W, H, new Vector3(-2.0f, 1.5f, -0.31f), new Vector3(2f, 79.1f, 0f), 48f, "F_贴地平视");
    // 俯视全景：看场地整体布局
    Shoot(cam, W, H, new Vector3(0f, 46f, -10f), new Vector3(80f, 0f, 0f), 42f, "G_俯视全景");
    // 无门俯视：看清门的位置
    var gates = GameObject.Find("Gates");
    if (gates != null) gates.SetActive(false);
    Shoot(cam, W, H, new Vector3(0f, 46f, -10f), new Vector3(80f, 0f, 0f), 42f, "H_俯视全景_无Gates");
    if (gates != null) gates.SetActive(true);

    File.WriteAllText(Path.Combine(projRoot, "Tools/reports/g_shots.txt"), sb.ToString());
    Debug.Log("[g_shots] done\n" + sb.ToString());
    yield return null;
}

return Body();
