using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

// ev_tune2.cs —— 第二遍定标：墨阶(_BandBias) × 红边色 二维扫描
//
// 第一遍落盘（_ChromaKeep 0.45 / _BandBias +0.10 / 红边 (0.35,0.03,0.02)）实测：
//   彩度 11.7→20.8、纯黑 30%→13%、红边像素 9~10%  —— 数达标，但【画面上偏亮偏灰白】：
//   纯黑只剩 7%(受光)，而主角是 24% ⇒ 当初那些深墨块被一起抬没了，
//   整体落进浅墨阶，反而显得"没颜色"。⇒ 需要在"保留墨力"与"接近主角明度"之间定一档。
//
// 本探针：样本 墨徒 + 墨山，二维扫描
//   行 = _BandBias ∈ {-0.08, 0.00, +0.10}（-0.08 保留最多墨力，+0.10 是当前落盘值）
//   列 = 红边色 ∈ {暗 R1 当前, 中 R2, 亮 R3}
// _ChromaKeep 固定 0.45。运行时 clone，不写磁盘。
public class ev_tune2 : MonoBehaviour
{
    const string ReportPath = "D:/Unity Project/InkWash/Tools/reports/ev_tune2.txt";
    const string ShotDir    = "D:/Unity Project/InkWash/Tools/screenshots/enemies";
    const int DiffTh = 6;

    static readonly float[] BBS = { -0.15f, 0.00f, 0.10f };
    static readonly string[] BBTAG = { "bb-0.15", "bb+0.00", "bb+0.10" };
    static readonly Color[] REDS =
    {
        new Color(0.35f, 0.030f, 0.020f, 1f),   // R1 暗朱砂（当前）
        new Color(0.48f, 0.035f, 0.022f, 1f),   // R2 中朱砂
        new Color(0.62f, 0.045f, 0.025f, 1f),   // R3 亮朱砂
    };
    static readonly string[] RTAG = { "R1", "R2", "R3" };

    static readonly string[,] Objs = { { "墨徒", "墨徒" }, { "墨山", "墨山" } };

    readonly StringBuilder _sb = new StringBuilder();
    Component _sc; Type _ty;
    MethodInfo _mPlay, _mLabel, _mStop;
    PropertyInfo _pCount;
    Camera _cam;
    Vector3 _camPos0, _camRot0; float _fov0;

    void Start() { StartCoroutine(Run()); }

    System.Collections.IEnumerator Run()
    {
        yield return null; yield return null;

        foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
        { var t = a.GetType("InkWash.DebugTools.ActionShowcase"); if (t != null) { _ty = t; break; } }
        if (_ty == null) { _sb.AppendLine("x 找不到 ActionShowcase"); yield return Done(); }
        foreach (var o in UnityEngine.Object.FindObjectsOfType(_ty, true)) { _sc = o as Component; if (_sc != null) break; }
        if (_sc == null) { _sb.AppendLine("x 场景里没有 ActionShowcase"); yield return Done(); }

        _mPlay  = _ty.GetMethod("PlayForCapture", BindingFlags.Public | BindingFlags.Instance);
        _mLabel = _ty.GetMethod("ItemLabel",     BindingFlags.Public | BindingFlags.Instance);
        _mStop  = _ty.GetMethod("StopAutoClose", BindingFlags.Public | BindingFlags.Instance);
        _pCount = _ty.GetProperty("ItemCount",   BindingFlags.Public | BindingFlags.Instance);
        _mStop.Invoke(_sc, null);

        _cam = Camera.main;
        _camPos0 = _cam.transform.position; _camRot0 = _cam.transform.eulerAngles; _fov0 = _cam.fieldOfView;

        int count = (int)_pCount.GetValue(_sc, null);
        _sb.AppendLine("===== ev_tune2 —— 墨阶 × 红边色 二维扫描 =====");
        _sb.AppendLine("_ChromaKeep 固定 0.45；行=_BandBias，列=红边色。运行时 clone，不写磁盘。");
        _sb.AppendLine("参照：主角 受光 彩度 15.9 / 纯黑 24% / 亮度 106");
        _sb.AppendLine();

        for (int oi = 0; oi < Objs.GetLength(0); oi++)
        {
            string want = Objs[oi, 0], disp = Objs[oi, 1];
            int idx = -1;
            for (int i = 0; i < count; i++)
            {
                string lb = (string)_mLabel.Invoke(_sc, new object[] { i });
                if (lb.Contains(want)) { idx = i; break; }
            }
            if (idx < 0) { _sb.AppendLine("── " + disp + "：面板无此条目"); continue; }

            _mPlay.Invoke(_sc, new object[] { idx });
            yield return new WaitForSeconds(2.5f);

            var actor = FindActor(idx);
            if (actor == null) { _sb.AppendLine("── " + disp + "：找不到演员"); continue; }

            var rs = new List<Renderer>(actor.GetComponentsInChildren<Renderer>(true));
            var map = new Dictionary<Material, Material>();
            foreach (var r in rs)
            {
                var sm = r.sharedMaterial;
                if (sm == null) continue;
                Material inst;
                if (!map.ContainsKey(sm)) { inst = new Material(sm); inst.name = sm.name + "_T2"; map[sm] = inst; }
                else inst = map[sm];
                r.sharedMaterial = inst;
            }
            if (map.Count == 0) { _sb.AppendLine("── " + disp + "：无材质"); continue; }

            var b = Bounds0(actor);
            float halfH = Mathf.Max(b.extents.y, 0.05f);
            float dist = halfH / (0.50f * Mathf.Tan(_cam.fieldOfView * 0.5f * Mathf.Deg2Rad));

            _sb.AppendLine("── " + disp + "  idx=" + idx + "  材质=" + map.Count + " 份  取景 dist=" + dist.ToString("F2"));
            _sb.AppendLine();

            // 墨徒做全 3×3；墨山只做 bb 三档（红边取 R2），省时间
            bool full = (disp == "墨徒");

            for (int bi = 0; bi < BBS.Length; bi++)
            {
                _sb.AppendLine("   【" + BBTAG[bi] + "】_BandBias=" + BBS[bi].ToString("F2"));
                for (int ri = 0; ri < REDS.Length; ri++)
                {
                    if (!full && ri != 1) continue;

                    foreach (var kv in map)
                    {
                        if (kv.Value.HasProperty("_ChromaKeep"))   kv.Value.SetFloat("_ChromaKeep", 0.45f);
                        if (kv.Value.HasProperty("_BandBias"))     kv.Value.SetFloat("_BandBias", BBS[bi]);
                        if (kv.Value.HasProperty("_OutlineWidth")) kv.Value.SetFloat("_OutlineWidth", 0.018f);
                        if (kv.Value.HasProperty("_OutlineColor")) kv.Value.SetColor("_OutlineColor", REDS[ri]);
                    }

                    // ★ 每帧都重设相机：演示场自己的脚本会在 Update 里把相机拉回默认视角，
                    //   只在循环外设一次 ⇒ 只有第一帧对焦，后面全飘。此处每帧按当前包围盒压回去。
                    var bb2 = Bounds0(actor);
                    float hh2 = Mathf.Max(bb2.extents.y, 0.05f);
                    float d2 = hh2 / (0.50f * Mathf.Tan(_cam.fieldOfView * 0.5f * Mathf.Deg2Rad));
                    Vector3 dir = new Vector3(Mathf.Sin(0f), 0.22f, Mathf.Cos(0f)).normalized;
                    _cam.transform.position = bb2.center + dir * d2;
                    _cam.transform.LookAt(bb2.center);

                    Snap(disp, rs, BBTAG[bi] + "_" + RTAG[ri]);
                    yield return null;
                }
            }
            _sb.AppendLine();
        }

        yield return Done();
    }

    void Snap(string disp, List<Renderer> rs, string tag)
    {
        var wasOn = new List<bool>(); var wasSh = new List<ShadowCastingMode>();
        foreach (var r in rs) { wasOn.Add(r.enabled); wasSh.Add(r.shadowCastingMode); }
        foreach (var r in rs) r.shadowCastingMode = ShadowCastingMode.Off;

        foreach (var r in rs) r.enabled = true;
        var texA = Render();
        foreach (var r in rs) r.enabled = false;
        var texB = Render();

        for (int k = 0; k < rs.Count; k++) { rs[k].enabled = wasOn[k]; rs[k].shadowCastingMode = wasSh[k]; }

        Directory.CreateDirectory(ShotDir);
        string fn = ShotDir + "/ET_" + disp + "_" + tag + ".png";
        try { File.WriteAllBytes(fn, texA.EncodeToPNG()); } catch (Exception e) { _sb.AppendLine("     写图失败 " + e.Message); }

        var pa = texA.GetPixels32(); var pb = texB.GetPixels32();
        long n = 0, sCh = 0, sLu = 0, black = 0, red = 0;
        for (int k = 0; k < pa.Length; k++)
        {
            int d = Mathf.Max(Mathf.Abs(pa[k].r - pb[k].r), Mathf.Max(Mathf.Abs(pa[k].g - pb[k].g), Mathf.Abs(pa[k].b - pb[k].b)));
            if (d <= DiffTh) continue;
            n++;
            int R = pa[k].r, G = pa[k].g, B = pa[k].b;
            sCh += Mathf.Max(R, Mathf.Max(G, B)) - Mathf.Min(R, Mathf.Min(G, B));
            sLu += (int)(0.2126f * R + 0.7152f * G + 0.0722f * B);
            if (R < 8 && G < 8 && B < 8) black++;
            if (R - G > 30 && R - B > 30) red++;
        }

        if (n == 0) { _sb.AppendLine("      " + tag + " : 0 像素"); Destroy(texA); Destroy(texB); return; }

        _sb.AppendLine("      " + tag.PadRight(14)
                       + " 彩度 " + ((float)sCh / n).ToString("F1").PadLeft(5)
                       + "  亮度 " + (sLu / n).ToString().PadLeft(3)
                       + "  纯黑 " + (100f * black / n).ToString("F0").PadLeft(3) + "%"
                       + "  红边 " + (100f * red / n).ToString("F1").PadLeft(5) + "%");
        Destroy(texA); Destroy(texB);
    }

    Texture2D Render()
    {
        int w = Mathf.Clamp(Screen.width, 320, 1600);
        int h = Mathf.Clamp(Screen.height, 180, 900);
        var rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32);
        var prev = _cam.targetTexture;
        _cam.targetTexture = rt;
        _cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        tex.Apply();
        _cam.targetTexture = prev;
        RenderTexture.active = null;
        RenderTexture.ReleaseTemporary(rt);
        return tex;
    }

    static GameObject FindActor(int i)
    {
        GameObject best = null;
        foreach (var root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
            foreach (var tr in root.GetComponentsInChildren<Transform>(true))
            {
                if (tr.gameObject.name == "Showcase_Actor_" + i) return tr.gameObject;
                if (best == null && tr.gameObject.name.StartsWith("Showcase_Actor_")) best = tr.gameObject;
            }
        return best;
    }

    static Bounds Bounds0(GameObject go)
    {
        var rs = go.GetComponentsInChildren<Renderer>(true);
        bool has = false; Bounds b = new Bounds();
        foreach (var r in rs)
        {
            if (r == null) continue;
            if (!has) { b = r.bounds; has = true; } else b.Encapsulate(r.bounds);
        }
        return b;
    }

    System.Collections.IEnumerator Done()
    {
        yield return null;
        if (_cam != null)
        {
            _cam.transform.position = _camPos0;
            _cam.transform.eulerAngles = _camRot0;
            _cam.fieldOfView = _fov0;
        }
        string s = _sb.ToString();
        Debug.Log(s);
        try { File.WriteAllText(ReportPath, s); } catch { }
        enabled = false;
    }
}

var g = new GameObject("ev_tune2");
g.AddComponent<ev_tune2>();
return "EV_TUNE2_STARTED";
