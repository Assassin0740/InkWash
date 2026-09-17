// q_verify2.cs —— 真机验证：游戏相机抽帧（持剑待机 + 跑步）
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

IEnumerator Body()
{
    var sb = new StringBuilder();
    string projRoot = Path.GetDirectoryName(Application.dataPath);
    string imgDir = Path.Combine(projRoot, "Tools/screenshots/verify2");
    Directory.CreateDirectory(imgDir);

    yield return null; yield return null;

    var go = GameObject.Find("Player");
    if (go == null) { Debug.LogError("no Player"); yield break; }
    var ctl = go.GetComponent<InkWash.Player.PlayerController>();
    var stance = go.GetComponentInChildren<InkWash.Player.CombatStance>(true);
    var anim = go.GetComponent<Animator>();
    var cc = go.GetComponent<CharacterController>();
    Vector3 startPos = go.transform.position;
    Quaternion startRot = go.transform.rotation;

    if (ctl != null) ctl.BeginInputOverride();
    if (stance != null) stance.ForceStance(true);
    float t = Time.time; while (Time.time - t < 0.8f) yield return null;

    var cam = Camera.main;
    int W = 480, H = 270, COLS = 4;
    var sheet = new Texture2D(W * COLS, H * 2, TextureFormat.RGB24, false);

    IEnumerator Shot(int row, int col)
    {
        var rt = RenderTexture.GetTemporary(W, H, 24);
        cam.targetTexture = rt;
        cam.Render();
        cam.targetTexture = null;
        var prev = RenderTexture.active; RenderTexture.active = rt;
        var tile = new Texture2D(W, H, TextureFormat.RGB24, false);
        tile.ReadPixels(new Rect(0, 0, W, H), 0, 0); tile.Apply();
        RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt);
        sheet.SetPixels(col * W, (1 - row) * H, W, H, tile.GetPixels());
        Object.Destroy(tile);
        yield return null;
    }

    // 第一行：持剑待机 4 帧（间隔 0.25s，覆盖 1 秒）
    sb.AppendLine("=== 持剑待机帧 ===");
    for (int i = 0; i < COLS; i++)
    {
        var si = anim.GetCurrentAnimatorStateInfo(0);
        sb.AppendLine(string.Format("  帧 {0}: 状态={1} nt={2:F2} 速度={3:F2} 片段={4}",
            i, si.IsName("Idle") ? "Idle" : si.shortNameHash.ToString(), si.normalizedTime, ctl != null ? ctl.CurrentSpeed : 0f,
            stance != null ? stance.CurrentIdleClipName : "?"));
        yield return Shot(0, i);
        float t2 = Time.time; while (Time.time - t2 < 0.25f) yield return null;
    }

    // 第二行：跑步 4 帧
    if (cc != null) cc.enabled = false; go.transform.position = startPos; go.transform.rotation = startRot; if (cc != null) cc.enabled = true;
    if (stance != null) stance.ForceStance(true);
    if (ctl != null) ctl.SetInjectedMove(new Vector2(0f, 1f), true);
    float t3 = Time.time; while (Time.time - t3 < 1.2f) yield return null;   // 先加起速
    sb.AppendLine("=== 跑步帧 ===");
    for (int i = 0; i < COLS; i++)
    {
        var si = anim.GetCurrentAnimatorStateInfo(0);
        sb.AppendLine(string.Format("  帧 {0}: 状态={1} nt={2:F2} 速度={3:F2}",
            i, si.IsName("Run") ? "Run" : si.shortNameHash.ToString(), si.normalizedTime, ctl != null ? ctl.CurrentSpeed : 0f));
        yield return Shot(1, i);
        float t4 = Time.time; while (Time.time - t4 < 0.10f) yield return null;
    }

    sheet.Apply();
    File.WriteAllBytes(Path.Combine(imgDir, "_ingame.png"), sheet.EncodeToPNG());
    Object.Destroy(sheet);

    if (ctl != null) { ctl.SetInjectedMove(Vector2.zero, false); ctl.EndInputOverride(); }
    if (stance != null) stance.ForceStance(false);
    if (cam != null)
    {
        var rig = cam.GetComponent<InkWash.CameraRig.ThirdPersonCamera>();
        if (rig != null) rig.SetMouseLookEnabled(true);
    }

    File.WriteAllText(Path.Combine(projRoot, "Tools/reports/q_verify2.txt"), sb.ToString());
    Debug.Log("[q_verify2] done");
    yield return null;
}

return Body();
