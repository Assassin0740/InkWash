// q_hero.cs —— 主角单独近景（前 / 后），包围盒只取**蒙皮渲染器**
//
// 上一版机位被带偏的原因：Player 身上还有弧光 VFX 的 MeshRenderer，
// 它的包围盒宽达 6.2m ⇒ `dist = 高 × 1.9` 与 `LookAt(中心)` 一起被带歪，
// 主角在画面里变得又小又偏。这里只统计 SkinnedMeshRenderer（真正的人物网格）。
using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.Linq;
using UnityEngine;

public class QHero
{
    static StringBuilder _sb;
    static void L(string s) { _sb.AppendLine(s); }
    static Camera _cam;
    static string _dir;

    const int TW = 560, TH = 760;
    const int COLS = 2, ROWS = 1;

    public static string Run()
    {
        _sb = new StringBuilder();
        L("================ q_hero ================");
        _dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../Tools/screenshots/look"));
        Directory.CreateDirectory(_dir);

        var pc = UnityEngine.Object.FindObjectOfType<InkWash.Player.PlayerController>();
        if (pc == null) { L("!! 没找到 PlayerController"); return Flush(); }
        var anim = pc.GetComponentInChildren<Animator>();

        var cc = pc.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;
        pc.transform.position = new Vector3(0f, 0.05f, 0f);
        if (cc != null) cc.enabled = true;

        // 只统计蒙皮网格
        var skinned = pc.GetComponentsInChildren<SkinnedMeshRenderer>().Where(r => r.enabled).ToArray();
        L("  蒙皮渲染器 " + skinned.Length + " 个：");
        foreach (var r in skinned)
            L("    " + r.name + "  tris=" + (r.sharedMesh != null ? r.sharedMesh.triangles.Length / 3 : 0)
              + "  mat=" + (r.sharedMaterial != null ? r.sharedMaterial.name : "null"));
        var b = skinned[0].bounds;
        foreach (var r in skinned) b.Encapsulate(r.bounds);
        L("  人物包围盒 高=" + b.size.y.ToString("F3") + " 宽=" + b.size.x.ToString("F3") + " 深=" + b.size.z.ToString("F3"));

        // 全身渲染器（含 VFX）对照
        var all = pc.GetComponentsInChildren<Renderer>().Where(r => r.enabled).ToArray();
        var ba = all[0].bounds; foreach (var r in all) ba.Encapsulate(r.bounds);
        L("  全身（含 VFX）包围盒 高=" + ba.size.y.ToString("F3") + " 宽=" + ba.size.x.ToString("F3")
          + " 深=" + ba.size.z.ToString("F3") + "  渲染器=" + all.Length);
        foreach (var r in all)
            L("    · " + r.name + "  bounds.size=" + r.bounds.size.ToString("F2"));

        if (anim != null) { anim.applyRootMotion = false; anim.Play("Idle", 0, 0f); anim.Update(0.0001f); }

        var live = Camera.main;
        var go = new GameObject("RT_HeroCam");
        _cam = go.AddComponent<Camera>();
        if (live != null)
        {
            _cam.nearClipPlane = live.nearClipPlane;
            _cam.farClipPlane = live.farClipPlane;
            _cam.cullingMask = live.cullingMask;
        }
        _cam.fieldOfView = 34f;
        _cam.clearFlags = CameraClearFlags.SolidColor;
        _cam.backgroundColor = new Color(0.93f, 0.93f, 0.93f, 1f);
        _cam.enabled = false;

        int W = TW * COLS, H = TH * ROWS;
        var rt = RenderTexture.GetTemporary(W, H, 24, RenderTextureFormat.ARGB32);
        var prevPR = _cam.pixelRect;
        _cam.targetTexture = rt;

        float hh = Mathf.Max(b.size.y, 1f);
        float dist = hh * 1.55f;                       // 用**人物**高度定机位
        Vector3 aim = new Vector3(b.center.x, b.center.y, b.center.z);
        // 前视：从 -z 看向 +z（角色默认面朝 +z? 两面各拍一张即可确定）
        _cam.transform.position = new Vector3(aim.x, aim.y + hh * 0.02f, aim.z - dist);
        _cam.transform.LookAt(aim);
        _cam.pixelRect = new Rect(0, 0, TW, TH);
        _cam.Render();
        _cam.transform.position = new Vector3(aim.x, aim.y + hh * 0.02f, aim.z + dist);
        _cam.transform.LookAt(aim);
        _cam.pixelRect = new Rect(TW, 0, TW, TH);
        _cam.Render();

        _cam.pixelRect = prevPR;
        _cam.targetTexture = null;
        var pa = RenderTexture.active; RenderTexture.active = rt;
        var sheet = new Texture2D(W, H, TextureFormat.RGBA32, false);
        sheet.ReadPixels(new Rect(0, 0, W, H), 0, 0); sheet.Apply();
        RenderTexture.active = pa; RenderTexture.ReleaseTemporary(rt);
        File.WriteAllBytes(Path.Combine(_dir, "hero_only.png"), sheet.EncodeToPNG());
        UnityEngine.Object.Destroy(sheet);
        L("  出图：格1 = 从 -Z 看，格2 = 从 +Z 看");

        UnityEngine.Object.Destroy(go);
        return Flush();
    }

    static string Flush()
    {
        L("");
        L("================ end ================");
        string outp = Path.GetFullPath(Path.Combine(Application.dataPath, "../Tools/reports/q_hero.txt"));
        File.WriteAllText(outp, _sb.ToString());
        return "written: " + outp + "  (" + _sb.Length + " chars)";
    }
}

return QHero.Run();
