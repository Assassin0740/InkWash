// q_dash_verify.cs —— 冲刺新动画的验证出图（接图：4 相位画进同一张 RT）
using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using UnityEngine;

public class QDashVerify
{
    static StringBuilder _sb;
    static void L(string s) { _sb.AppendLine(s); }
    static Camera _cam;
    static string _dir;

    const int TW = 640, TH = 360;
    const int COLS = 4, ROWS = 1;

    public static string Run()
    {
        _sb = new StringBuilder();
        L("================ q_dash_verify ================");
        _dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../Tools/screenshots/dash"));
        Directory.CreateDirectory(_dir);

        var pc = UnityEngine.Object.FindObjectOfType<InkWash.Player.PlayerController>();
        if (pc == null) { L("!! 没找到 PlayerController（要在 Play 里跑）"); return Flush(); }
        var anim = pc.GetComponentInChildren<Animator>();
        if (anim == null) { L("!! 没有 Animator"); return Flush(); }

        L("  Dash 状态当前片段：");
        var ctrl = anim.runtimeAnimatorController;
        foreach (var c in ctrl.animationClips) L("    " + c.name + "  len=" + c.length.ToString("F3"));

        var cc = pc.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;
        pc.transform.position = new Vector3(0f, 0.05f, 0f);
        pc.transform.rotation = Quaternion.identity;
        if (cc != null) cc.enabled = true;

        var live = Camera.main;
        var go = new GameObject("RT_DashVer");
        _cam = go.AddComponent<Camera>();
        if (live != null)
        {
            _cam.fieldOfView = live.fieldOfView; _cam.nearClipPlane = live.nearClipPlane;
            _cam.farClipPlane = live.farClipPlane; _cam.cullingMask = live.cullingMask;
        }
        _cam.clearFlags = CameraClearFlags.SolidColor;
        _cam.backgroundColor = new Color(0.88f, 0.88f, 0.88f, 1f);
        _cam.enabled = false;
        _cam.transform.position = new Vector3(5.6f, 1.5f, 0f);
        _cam.transform.LookAt(new Vector3(0f, 0.95f, 0f));

        anim.applyRootMotion = false;
        int W = TW * COLS, H = TH * ROWS;
        var rt = RenderTexture.GetTemporary(W, H, 24, RenderTextureFormat.ARGB32);
        var prevPR = _cam.pixelRect;
        _cam.targetTexture = rt;

        float[] phases = { 0.05f, 0.30f, 0.55f, 0.85f };
        var notes = new List<string>();
        for (int i = 0; i < phases.Length; i++)
        {
            anim.Play("Dash", 0, phases[i]);
            anim.Update(0.0001f);
            _cam.pixelRect = new Rect(i * TW, 0, TW, TH);
            _cam.Render();
            var hips = anim.GetBoneTransform(HumanBodyBones.Hips);
            var head = anim.GetBoneTransform(HumanBodyBones.Head);
            var rh = anim.GetBoneTransform(HumanBodyBones.RightHand);
            notes.Add(string.Format("  格{0} t={1:F2}  hips={2}  头Y={3:F3}  右手Y={4:F3}",
                i, phases[i], hips.position.ToString("F3"), head.position.y, rh.position.y));
        }
        _cam.pixelRect = prevPR;
        _cam.targetTexture = null;

        var pa = RenderTexture.active; RenderTexture.active = rt;
        var sheet = new Texture2D(W, H, TextureFormat.RGBA32, false);
        sheet.ReadPixels(new Rect(0, 0, W, H), 0, 0); sheet.Apply();
        RenderTexture.active = pa; RenderTexture.ReleaseTemporary(rt);
        File.WriteAllBytes(Path.Combine(_dir, "dash_after.png"), sheet.EncodeToPNG());
        UnityEngine.Object.Destroy(sheet);
        L("");
        L("---------- 分格 ----------");
        foreach (var n in notes) L(n);

        UnityEngine.Object.Destroy(go);
        return Flush();
    }

    static string Flush()
    {
        L("");
        L("================ end ================");
        string outp = Path.GetFullPath(Path.Combine(Application.dataPath, "../Tools/reports/q_dash_verify.txt"));
        File.WriteAllText(outp, _sb.ToString());
        return "written: " + outp + "  (" + _sb.Length + " chars)";
    }
}

return QDashVerify.Run();
