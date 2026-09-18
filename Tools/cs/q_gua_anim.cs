using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;

// q_gua_anim.cs —— 演示场里召唤「墨徒」（= Z_Enemy_MoGuai），在动画运行中
//   ① 逐渲染器测 world bounds 位移 ⇒ 找出「悬空不动」的那一件
//   ② 出多帧图（自建相机，不受演示场追随相机干扰）
public class q_gua_anim : MonoBehaviour
{
    const string RP = "D:/Unity Project/InkWash/Tools/reports/q_gua_anim.txt";
    const string SD = "D:/Unity Project/InkWash/Tools/screenshots/enemies";
    const string KEY = "墨徒";

    readonly StringBuilder _sb = new StringBuilder();

    Component _sc; Type _ty;
    MethodInfo _mPlay, _mLabel, _mStop;
    PropertyInfo _pCount;
    Camera _cam;

    void Start() { StartCoroutine(Run()); }

    System.Collections.IEnumerator Run()
    {
        yield return null; yield return null;

        foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
        { var t = a.GetType("InkWash.DebugTools.ActionShowcase"); if (t != null) { _ty = t; break; } }
        if (_ty == null) { _sb.AppendLine("× 找不到 ActionShowcase"); yield return Done(); }

        foreach (var o in UnityEngine.Object.FindObjectsOfType(_ty, true)) { _sc = o as Component; if (_sc != null) break; }
        if (_sc == null) { _sb.AppendLine("× 场景里没有 ActionShowcase 实例"); yield return Done(); }

        _mPlay = _ty.GetMethod("PlayForCapture", BindingFlags.Public | BindingFlags.Instance);
        _mLabel = _ty.GetMethod("ItemLabel", BindingFlags.Public | BindingFlags.Instance);
        _mStop = _ty.GetMethod("StopAutoClose", BindingFlags.Public | BindingFlags.Instance);
        _pCount = _ty.GetProperty("ItemCount", BindingFlags.Public | BindingFlags.Instance);
        _mStop.Invoke(_sc, null);

        int count = (int)_pCount.GetValue(_sc, null);
        int idx = -1;
        for (int i = 0; i < count; i++)
        {
            string lb = (string)_mLabel.Invoke(_sc, new object[] { i });
            if (lb.Contains(KEY)) { idx = i; _sb.AppendLine("条目 [" + i + "] " + lb); break; }
        }
        if (idx < 0) { _sb.AppendLine("× 没找到「" + KEY + "」条目"); yield return Done(); }

        _mPlay.Invoke(_sc, new object[] { idx });
        for (int i = 0; i < 45; i++) yield return null;

        var actor = GameObject.Find("Showcase_Actor_" + idx);
        if (actor == null) { _sb.AppendLine("× 没找到 actor 实例"); yield return Done(); }
        _sb.AppendLine("actor = " + actor.name + "   worldPos=" + actor.transform.position.ToString("F2"));
        _sb.AppendLine();

        var rends = actor.GetComponentsInChildren<Renderer>(true);
        _sb.AppendLine("渲染器数 = " + rends.Length);

        // 强制更新 bounds —— updateWhenOffscreen=false 时离屏不刷新，会把「不动」测成假象
        foreach (var r in rends)
        {
            var s = r as SkinnedMeshRenderer;
            if (s != null) s.updateWhenOffscreen = true;
        }
        yield return null;

        _cam = MakeCam();

        // 找动画组件，确认动画真的在跑
        var anims = actor.GetComponentsInChildren<Animator>(true);
        if (anims.Length > 0)
        {
            var an = anims[0];
            _sb.AppendLine("Animator: controller=" + (an.runtimeAnimatorController == null ? "NULL" : an.runtimeAnimatorController.name)
                + " avatar=" + (an.avatar == null ? "NULL" : an.avatar.name)
                + " enabled=" + an.enabled + " speed=" + an.speed);
            if (an.runtimeAnimatorController != null)
            {
                var clips = an.runtimeAnimatorController.animationClips;
                _sb.Append("  片段(" + clips.Length + "): ");
                for (int i = 0; i < clips.Length && i < 12; i++) _sb.Append(clips[i].name + " ");
                _sb.AppendLine();
            }
        }
        else _sb.AppendLine("Animator: （无）");
        _sb.AppendLine();

        // ── 逐帧采样：记录每个渲染器的 world bounds center 轨迹 ──
        int NF = 6;
        var track = new List<Vector3[]>();
        var ntrack = new List<string[]>();
        for (int f = 0; f < NF; f++)
        {
            var pos = new Vector3[rends.Length];
            var nm = new string[rends.Length];
            for (int i = 0; i < rends.Length; i++) { pos[i] = rends[i].bounds.center; nm[i] = rends[i].name; }
            track.Add(pos);
            ntrack.Add(nm);
            Snap(rends, "t" + f);
            for (int k = 0; k < 20; k++) yield return null;   // ≈0.33 s
        }

        _sb.AppendLine("===== 各渲染器 world bounds 位移（" + NF + " 帧，约 " + (NF * 0.33f).ToString("F1") + " s）=====");
        _sb.AppendLine("名称".PadRight(34) + " 首帧位置                     总位移(m)   判定");
        for (int i = 0; i < rends.Length; i++)
        {
            float maxd = 0f;
            for (int f = 1; f < NF; f++)
            {
                float d = Vector3.Distance(track[0][i], track[f][i]);
                if (d > maxd) maxd = d;
            }
            string verdict = maxd < 0.02f ? "⚠ 完全不动" : (maxd < 0.10f ? "· 几乎不动" : "✓ 跟随");
            _sb.AppendLine(rends[i].name.PadRight(34) + " " + track[0][i].ToString("F2").PadRight(30)
                + " " + maxd.ToString("F3").PadRight(10) + " " + verdict);
        }
        _sb.AppendLine();

        // 骨骼采样：handslot / Axe Dummy 是否在动
        _sb.AppendLine("===== 关键骨骼世界位置（首帧 vs 末帧）=====");
        var allT = actor.GetComponentsInChildren<Transform>(true);
        string[] kn = { "handslot", "Axe", "Bip001 R Hand", "Bip001 L Hand", "Object_4", "Model", "Visual" };
        foreach (var t in allT)
        {
            foreach (var k in kn)
            {
                if (t.name.IndexOf(k, StringComparison.OrdinalIgnoreCase) < 0) continue;
                var p0 = t.position;
                yield return null;
                var p1b = t.position;
                _sb.AppendLine("  " + t.name.PadRight(28) + " pos=" + p0.ToString("F3") + "   子=" + t.childCount);
                break;
            }
        }

        UnityEngine.Object.Destroy(_cam.gameObject);
        yield return Done();
    }

    Camera MakeCam()
    {
        var g = new GameObject("PROBE_CAM_QGA");
        var c = g.AddComponent<Camera>();
        c.clearFlags = CameraClearFlags.SolidColor;
        c.backgroundColor = new Color(0.86f, 0.88f, 0.91f, 1f);
        c.fieldOfView = 40f;
        c.nearClipPlane = 0.05f;
        c.farClipPlane = 5000f;
        c.enabled = false;
        return c;
    }

    void Snap(Renderer[] rs, string tag)
    {
        var b = rs[0].bounds;
        for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
        float rad = b.extents.magnitude;
        if (rad < 0.01f) rad = 0.01f;
        float dist = rad / Mathf.Sin(_cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.08f;
        _cam.transform.position = b.center + new Vector3(0.72f, 0.24f, 0.72f).normalized * dist;
        _cam.transform.LookAt(b.center);

        int W = 900, H = 900;
        var rt = new RenderTexture(W, H, 24);
        _cam.targetTexture = rt;
        _cam.Render();
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
        tex.Apply();
        File.WriteAllBytes(SD + "/QGA_" + tag + ".png", tex.EncodeToPNG());
        RenderTexture.active = prev;
        UnityEngine.Object.Destroy(tex);
        UnityEngine.Object.Destroy(rt);
        _cam.targetTexture = null;
    }

    System.Collections.IEnumerator Done()
    {
        File.WriteAllText(RP, _sb.ToString());
        Debug.Log("[q_gua_anim] 报告已写入 " + RP);
        yield return null;
    }
}

var __host = new GameObject("q_gua_anim");
__host.AddComponent<q_gua_anim>();
return "Q_GUA_ANIM_STARTED";
