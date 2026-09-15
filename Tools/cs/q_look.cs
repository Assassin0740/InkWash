// q_look.cs —— 一张近景合成图：上排冲刺 4 相位，下排主角 + 全部怪物并排
// 用途：同时判断「冲刺动作是否合理」与「怪物丑/敌我区分度」两个诉求。
using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.Linq;
using UnityEngine;

public class QLook
{
    static StringBuilder _sb;
    static void L(string s) { _sb.AppendLine(s); }
    static Camera _cam;
    static string _dir;

    const int TW = 620, TH = 460;
    const int COLS = 4, ROWS = 2;

    public static string Run()
    {
        _sb = new StringBuilder();
        L("================ q_look ================");
        _dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../Tools/screenshots/look"));
        Directory.CreateDirectory(_dir);

        var pc = UnityEngine.Object.FindObjectOfType<InkWash.Player.PlayerController>();
        if (pc == null) { L("!! 没找到 PlayerController"); return Flush(); }
        var anim = pc.GetComponentInChildren<Animator>();

        // 收集敌人
        var enemies = new List<GameObject>();
        foreach (var t in UnityEngine.Object.FindObjectsOfType<Transform>())
        {
            if (t.parent != null) continue;
            string n = t.name;
            if (n.Contains("MoTu") || n.Contains("MoOu") || n.Contains("MoYan")) enemies.Add(t.gameObject);
        }
        enemies = enemies.OrderBy(e => e.name).ToList();
        L("  场景内敌人根对象 " + enemies.Count + " 个：");
        foreach (var e in enemies) L("    " + e.name + "  pos=" + e.transform.position.ToString("F2"));

        // 把主角与敌人排成一行，放到 z=-2.6
        var cc = pc.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;
        var lineup = new List<GameObject> { pc.gameObject };
        lineup.AddRange(enemies);
        float span = 1.35f;
        float x0 = -(lineup.Count - 1) * span * 0.5f;
        for (int i = 0; i < lineup.Count; i++)
        {
            var t = lineup[i].transform;
            t.position = new Vector3(x0 + i * span, 0.05f, -2.6f);
            t.rotation = Quaternion.Euler(0f, 180f, 0f);   // 面向镜头
        }
        if (cc != null) cc.enabled = true;

        var live = Camera.main;
        var go = new GameObject("RT_LookCam");
        _cam = go.AddComponent<Camera>();
        if (live != null)
        {
            _cam.fieldOfView = live.fieldOfView; _cam.nearClipPlane = live.nearClipPlane;
            _cam.farClipPlane = live.farClipPlane; _cam.cullingMask = live.cullingMask;
        }
        _cam.clearFlags = CameraClearFlags.SolidColor;
        _cam.backgroundColor = new Color(0.9f, 0.9f, 0.9f, 1f);
        _cam.enabled = false;

        int W = TW * COLS, H = TH * ROWS;
        var rt = RenderTexture.GetTemporary(W, H, 24, RenderTextureFormat.ARGB32);
        var prevPR = _cam.pixelRect;
        _cam.targetTexture = rt;
        anim.applyRootMotion = false;

        // ---- 上排：冲刺 4 相位（近景）----
        _cam.transform.position = new Vector3(2.9f, 1.25f, -2.6f);
        _cam.transform.LookAt(new Vector3(0f, 0.95f, -2.6f));
        float[] phases = { 0.05f, 0.28f, 0.55f, 0.85f };
        for (int i = 0; i < 4; i++)
        {
            anim.Play("Dash", 0, phases[i]);
            anim.Update(0.0001f);
            _cam.pixelRect = new Rect(i * TW, H - TH, TW, TH);
            _cam.Render();
        }

        // ---- 下排：主角 + 敌人 并排 ----
        // 先把主角切回 Idle，敌人各自播自己的 Idle
        anim.Play("Idle", 0, 0f);
        anim.Update(0.0001f);
        foreach (var e in enemies)
        {
            var ea = e.GetComponentInChildren<Animator>();
            if (ea != null)
            {
                var ids = ea.runtimeAnimatorController != null
                        ? ea.runtimeAnimatorController.animationClips.Select(c => c.name).ToArray()
                        : new string[0];
                string pick = ids.FirstOrDefault(s => s.ToLower().Contains("idle")) ?? (ids.Length > 0 ? ids[0] : null);
                if (pick != null) { ea.applyRootMotion = false; ea.Play(pick, 0, 0f); ea.Update(0.0001f); }
            }
        }
        _cam.transform.position = new Vector3(0f, 1.15f, -6.2f);
        _cam.transform.LookAt(new Vector3(0f, 0.92f, -2.6f));
        _cam.pixelRect = new Rect(0, 0, W, TH);
        _cam.Render();

        _cam.pixelRect = prevPR;
        _cam.targetTexture = null;
        var pa = RenderTexture.active; RenderTexture.active = rt;
        var sheet = new Texture2D(W, H, TextureFormat.RGBA32, false);
        sheet.ReadPixels(new Rect(0, 0, W, H), 0, 0); sheet.Apply();
        RenderTexture.active = pa; RenderTexture.ReleaseTemporary(rt);
        File.WriteAllBytes(Path.Combine(_dir, "hero_vs_enemy.png"), sheet.EncodeToPNG());
        UnityEngine.Object.Destroy(sheet);

        // 材质速览
        L("");
        L("---------- 主角 / 敌人 材质 ----------");
        var mats = new List<Material>();
        foreach (var g in lineup)
            foreach (var r in g.GetComponentsInChildren<Renderer>())
                if (r.sharedMaterial != null && r.sharedMaterial.shader.name.Contains("Ink") && !mats.Contains(r.sharedMaterial))
                    mats.Add(r.sharedMaterial);
        foreach (var m in mats)
            L("  " + m.name.PadRight(26) + " shader=" + m.shader.name
              + " _Bands=" + m.GetFloat("_Bands").ToString("F0")
              + " _BandBias=" + m.GetFloat("_BandBias").ToString("F3")
              + " _InkDensity=" + m.GetFloat("_InkDensity").ToString("F2")
              + " _OutlineWidth=" + m.GetFloat("_OutlineWidth").ToString("F3")
              + " _ChromaKeep=" + (m.HasProperty("_ChromaKeep") ? m.GetFloat("_ChromaKeep").ToString("F2") : "-"));

        UnityEngine.Object.Destroy(go);
        return Flush();
    }

    static string Flush()
    {
        L("");
        L("================ end ================");
        string outp = Path.GetFullPath(Path.Combine(Application.dataPath, "../Tools/reports/q_look.txt"));
        File.WriteAllText(outp, _sb.ToString());
        return "written: " + outp + "  (" + _sb.Length + " chars)";
    }
}

return QLook.Run();
