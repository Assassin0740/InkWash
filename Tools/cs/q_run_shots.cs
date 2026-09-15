// q_run_shots.cs —— 跑步右臂还原后的验证：相位出图 + 双手/剑的世界位移
//
// 判据：
//   还原前 右手世界位移范围 ≈ 0（常数通道）而左手正常 ⇒ 一边甩、一边僵
//   还原后 右手应与左手同量级，且剑（挂在 BackSocket 上）随躯干起伏
using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using UnityEngine;

public class QRunShots
{
    static StringBuilder _sb;
    static void L(string s) { _sb.AppendLine(s); }
    static Camera _cam;
    static string _dir;

    static Texture2D RenderTex(int w, int h)
    {
        var rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32);
        var prev = _cam.targetTexture; _cam.targetTexture = rt; _cam.Render(); _cam.targetTexture = prev;
        var pa = RenderTexture.active; RenderTexture.active = rt;
        var t = new Texture2D(w, h, TextureFormat.RGBA32, false);
        t.ReadPixels(new Rect(0, 0, w, h), 0, 0); t.Apply();
        RenderTexture.active = pa; RenderTexture.ReleaseTemporary(rt);
        return t;
    }
    static Texture2D DownN(Texture2D hi, int f)
    {
        int w = hi.width / f, h = hi.height / f;
        var lo = new Texture2D(w, h, TextureFormat.RGBA32, false);
        var hc = hi.GetPixels(); var lc = new Color[w * h]; int hw = hi.width;
        for (int y = 0; y < h; y++)
            for (int xx = 0; xx < w; xx++)
            {
                Color s = Color.black;
                for (int j = 0; j < f; j++) for (int i = 0; i < f; i++) s += hc[(y * f + j) * hw + xx * f + i];
                lc[y * w + xx] = s / (f * f);
            }
        lo.SetPixels(lc); lo.Apply(); return lo;
    }

    public static string Run()
    {
        _sb = new StringBuilder();
        L("================ q_run_shots ================");
        _dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../Tools/screenshots/run"));
        Directory.CreateDirectory(_dir);

        var pc = UnityEngine.Object.FindObjectOfType<InkWash.Player.PlayerController>();
        if (pc == null) { L("!! 没找到 PlayerController（要在 Play 里跑）"); return Flush(); }
        var anim = pc.GetComponentInChildren<Animator>();
        if (anim == null) { L("!! 没有 Animator"); return Flush(); }

        // 站到空地上并冻结控制器干扰（本脚本同步执行，游戏循环被阻塞）
        var cc = pc.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;
        pc.transform.position = new Vector3(0f, 0.05f, -2.6f);
        pc.transform.rotation = Quaternion.identity;
        if (cc != null) cc.enabled = true;

        var sword = GameObject.Find("W_Sword");
        var rh = anim.GetBoneTransform(HumanBodyBones.RightHand);
        var lh = anim.GetBoneTransform(HumanBodyBones.LeftHand);
        var spine = anim.GetBoneTransform(HumanBodyBones.Spine);

        L("  剑=" + (sword == null ? "null" : sword.name + "  父=" + sword.transform.parent.name)
          + "  右手=" + (rh == null ? "null" : rh.name) + "  左手=" + (lh == null ? "null" : lh.name));

        // 相机：角色右侧视图
        var live = Camera.main;
        var go = new GameObject("RT_RunCam");
        _cam = go.AddComponent<Camera>();
        if (live != null)
        {
            _cam.fieldOfView = live.fieldOfView; _cam.nearClipPlane = live.nearClipPlane;
            _cam.farClipPlane = live.farClipPlane; _cam.cullingMask = live.cullingMask;
        }
        _cam.clearFlags = CameraClearFlags.Skybox; _cam.enabled = false;
        _cam.transform.position = new Vector3(4.6f, 1.55f, -2.6f);
        _cam.transform.LookAt(new Vector3(0f, 1.05f, -2.6f));

        // ---- 采样一整圈：位移范围 ----
        anim.applyRootMotion = false;
        Vector3 rMin = Vector3.zero, rMax = Vector3.zero, lMin = Vector3.zero, lMax = Vector3.zero,
                sMin = Vector3.zero, sMax = Vector3.zero, spMin = Vector3.zero, spMax = Vector3.zero;
        bool first = true;
        for (int i = 0; i <= 48; i++)
        {
            float t = i / 48f;
            anim.Play("Run", 0, t);
            anim.Update(0.0001f);
            var rp = rh.position; var lp = lh.position; var sp2 = spine.position;
            var swp = sword != null ? sword.transform.position : Vector3.zero;
            // 剑相对右手的偏移（脚/手不一致时能看出剑没跟着手）
            var off = rh.InverseTransformPoint(swp);
            if (first) { rMin = rMax = rp; lMin = lMax = lp; sMin = sMax = sp2; spMin = spMax = off; first = false; }
            else
            {
                rMin = Vector3.Min(rMin, rp); rMax = Vector3.Max(rMax, rp);
                lMin = Vector3.Min(lMin, lp); lMax = Vector3.Max(lMax, lp);
                sMin = Vector3.Min(sMin, sp2); sMax = Vector3.Max(sMax, sp2);
                spMin = Vector3.Min(spMin, off); spMax = Vector3.Max(spMax, off);
            }
        }
        L("");
        L("---------- 一整圈的世界位移范围（48 相位）----------");
        L("  右手  = " + (rMax - rMin).ToString("F4"));
        L("  左手  = " + (lMax - lMin).ToString("F4"));
        L("  腰骨  = " + (sMax - sMin).ToString("F4"));
        L("  剑    = " + (spMax - spMin).ToString("F4") + " （剑在右手局部系下的偏移；非 0 说明剑不在手上）");

        // ---- 相位出图 ----
        L("");
        L("---------- 相位出图 ----------");
        float[] phases = { 0f, 0.25f, 0.5f, 0.75f };
        foreach (var t in phases)
        {
            anim.Play("Run", 0, t);
            anim.Update(0.0001f);
            var raw = RenderTex(1600, 1000);
            var img = DownN(raw, 2); UnityEngine.Object.Destroy(raw);
            string nm = "run_p" + t.ToString("F2").Replace(".", "");
            File.WriteAllBytes(Path.Combine(_dir, nm + ".png"), img.EncodeToPNG());
            UnityEngine.Object.Destroy(img);
            L("  " + nm + "  右手y=" + rh.position.y.ToString("F3") + "  左手y=" + lh.position.y.ToString("F3")
              + "  剑y=" + (sword != null ? sword.transform.position.y.ToString("F3") : "-"));
        }

        UnityEngine.Object.Destroy(go);
        anim.Play("Run", 0, 0f);
        return Flush();
    }

    static string Flush()
    {
        L("");
        L("================ end ================");
        string outp = Path.GetFullPath(Path.Combine(Application.dataPath, "../Tools/reports/q_run_shots.txt"));
        File.WriteAllText(outp, _sb.ToString());
        return "written: " + outp + "  (" + _sb.Length + " chars)";
    }
}

return QRunShots.Run();
