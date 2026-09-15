// q_dash_shots.cs —— 冲刺候选动画的合成接图（多相位画进同一张图，一次看清）
//
// 手法：不动控制器拓扑，用 `AnimatorOverrideController` 把 **Dash 状态原本的片段**
// 换成候选片段再播 —— 这既是预览、也正好是最终要落地的改法。
// 用 `camera.pixelRect` 把 3 个候选 × 3 个相位画进同一个 RenderTexture，
// 只需读一张 PNG 就能判断哪个片段真正"向前冲"。
using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;

public class QDashShots
{
    static StringBuilder _sb;
    static void L(string s) { _sb.AppendLine(s); }
    static Camera _cam;
    static string _dir;

    const int TW = 480, TH = 300;
    const int COLS = 8, ROWS = 1;

    public static string Run()
    {
        _sb = new StringBuilder();
        L("================ q_dash_shots ================");
        _dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../Tools/screenshots/dash"));
        Directory.CreateDirectory(_dir);

        var pc = UnityEngine.Object.FindObjectOfType<InkWash.Player.PlayerController>();
        if (pc == null) { L("!! 没找到 PlayerController（要在 Play 里跑）"); return Flush(); }
        var anim = pc.GetComponentInChildren<Animator>();
        if (anim == null) { L("!! 没有 Animator"); return Flush(); }
        var baseCtrl = anim.runtimeAnimatorController;
        if (baseCtrl == null) { L("!! runtimeAnimatorController 为空"); return Flush(); }

        L("");
        L("---------- 控制器的全部片段 ----------");
        foreach (var c in baseCtrl.animationClips) L("  " + c.name + "  len=" + c.length.ToString("F3"));

        // Dash 状态原本用的片段（KayKit 的 Q 版翻滚）
        var origClip = baseCtrl.animationClips.FirstOrDefault(c => c.name.Contains("Dodge"))
                    ?? baseCtrl.animationClips.FirstOrDefault(c => c.name.Contains("Dash"));
        if (origClip == null) { L("!! 找不到 Dash 原片段（名字里没有 Dodge/Dash）"); return Flush(); }
        L("");
        L("  Dash 状态原片段 = " + origClip.name + "  len=" + origClip.length.ToString("F3"));

        var cc = pc.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;
        pc.transform.position = new Vector3(0f, 0.05f, 0f);
        pc.transform.rotation = Quaternion.identity;
        if (cc != null) cc.enabled = true;

        // 候选
        string ual2 = "Assets/ThirdParty/Quaternius/UniversalAnimationLibrary2/Unity";
        var all = LoadClips(ual2);
        L("");
        L("  UAL2 片段数 = " + all.Count);
        string[] want = { "Armature|Sword_Dash" };
        var picks = new List<AnimationClip>();
        foreach (var w in want)
        {
            var c = all.FirstOrDefault(x => x.name == w);
            if (c == null) { L("  !! 找不到 " + w); continue; }
            L("  候选 " + w + "  len=" + c.length.ToString("F3"));
            picks.Add(c);
        }
        if (picks.Count == 0) return Flush();

        var live = Camera.main;
        var go = new GameObject("RT_DashCam");
        _cam = go.AddComponent<Camera>();
        if (live != null)
        {
            _cam.fieldOfView = live.fieldOfView; _cam.nearClipPlane = live.nearClipPlane;
            _cam.farClipPlane = live.farClipPlane; _cam.cullingMask = live.cullingMask;
        }
        _cam.clearFlags = CameraClearFlags.SolidColor;
        _cam.backgroundColor = new Color(0.88f, 0.88f, 0.88f, 1f);
        _cam.enabled = false;
        _cam.transform.position = new Vector3(4.9f, 1.45f, 0f);
        _cam.transform.LookAt(new Vector3(0f, 0.95f, 0f));

        anim.applyRootMotion = false;

        int W = TW * COLS, H = TH * ROWS;
        var rt = RenderTexture.GetTemporary(W, H, 24, RenderTextureFormat.ARGB32);
        var prevRR = _cam.rect;
        var prevPR = _cam.pixelRect;
        _cam.targetTexture = rt;

        float[] phases = { 0.06f, 0.18f, 0.30f, 0.42f, 0.54f, 0.66f, 0.78f, 0.90f };
        var notes = new List<string>();
        int slot = 0;
        foreach (var c in picks)
        {
            var aoc = new AnimatorOverrideController(baseCtrl);
            aoc[origClip] = c;
            anim.runtimeAnimatorController = aoc;
            anim.Rebind();

            for (int pi = 0; pi < phases.Length && slot < COLS * ROWS; pi++)
            {
                float t = phases[pi];
                anim.Play("Dash", 0, t);
                anim.Update(0.0001f);

                int col = slot % COLS, row = slot / COLS;
                _cam.pixelRect = new Rect(col * TW, H - (row + 1) * TH, TW, TH);
                _cam.Render();
                var hips = anim.GetBoneTransform(HumanBodyBones.Hips);
                notes.Add(string.Format("  格{0}  [{1}]  t={2:F2}  hipsY={3:F3}  头Y={4:F3}",
                    slot, c.name, t, hips.position.y,
                    anim.GetBoneTransform(HumanBodyBones.Head).position.y));
                slot++;
                if (slot >= COLS * ROWS) break;
            }
            if (slot >= COLS * ROWS) break;
        }
        // 还原控制器
        anim.runtimeAnimatorController = baseCtrl;
        anim.Rebind();
        _cam.pixelRect = prevPR; _cam.rect = prevRR;
        _cam.targetTexture = null;

        var pa = RenderTexture.active; RenderTexture.active = rt;
        var sheet = new Texture2D(W, H, TextureFormat.RGBA32, false);
        sheet.ReadPixels(new Rect(0, 0, W, H), 0, 0); sheet.Apply();
        RenderTexture.active = pa; RenderTexture.ReleaseTemporary(rt);
        File.WriteAllBytes(Path.Combine(_dir, "dash_candidates.png"), sheet.EncodeToPNG());
        UnityEngine.Object.Destroy(sheet);

        L("");
        L("---------- 接图分格（左上→右下）----------");
        foreach (var n in notes) L(n);

        UnityEngine.Object.Destroy(go);
        return Flush();
    }

    static List<AnimationClip> LoadClips(string folder)
    {
        var result = new List<AnimationClip>();
        // ⚠ 不要写 `#if UNITY_EDITOR`：Codely 的 Roslyn 脚本**无论编辑器是否在跑，都拿不到这个符号**
        //   （脚本是按运行时程序集编的），于是整块被编掉、静默返回空列表 —— 本探针第一次跑就踩了，
        //   报告只有 588 字符、图根本没生成。`using UnityEditor;` 直接调就行（编辑模式下可用）。
        foreach (var g in AssetDatabase.FindAssets("t:Model", new[] { folder }))
        {
            string p = AssetDatabase.GUIDToAssetPath(g);
            foreach (var c in AssetDatabase.LoadAllAssetsAtPath(p).OfType<AnimationClip>())
                if (c != null && !result.Contains(c)) result.Add(c);
        }
        return result;
    }

    static string Flush()
    {
        L("");
        L("================ end ================");
        string outp = Path.GetFullPath(Path.Combine(Application.dataPath, "../Tools/reports/q_dash_shots.txt"));
        File.WriteAllText(outp, _sb.ToString());
        return "written: " + outp + "  (" + _sb.Length + " chars)";
    }
}

return QDashShots.Run();
