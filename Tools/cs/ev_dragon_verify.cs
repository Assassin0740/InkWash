using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

// ev_dragon_verify.cs —— 墨龙朱砂描边验收
//
// 同一帧内连续 4 次 Camera.Render()（无 yield ⇒ Time 不动、Animator 不更新 ⇒ 几何完全一致）：
//   ① ghost  渲染器全关（背景）
//   ② off    渲染器开、_OutlineWidth≈0（无描边）
//   ③ old    _OutlineColor = (0.042,0.038,0.036) 暗褐（改前）
//   ④ new    _OutlineColor = (0.48,0.035,0.022)  朱砂（改后）
// 判据：新描边可见像素、红区像素（彩度>SatFloor 且色相 330~30°）应远高于旧暗褐。
public class ev_dragon_verify : MonoBehaviour
{
    const string ReportPath = "D:/Unity Project/InkWash/Tools/reports/ev_dragon_verify.txt";
    const string ShotDir    = "D:/Unity Project/InkWash/Tools/screenshots/enemies";
    const int DiffTh   = 6;
    const int SatFloor = 14;

    static readonly Color OLD_C = new Color(0.042f, 0.038f, 0.036f, 1f);
    static readonly Color NEW_C = new Color(0.48f, 0.035f, 0.022f, 1f);

    // far = 按包围球全收（看整体）；near = 按高度占屏 85%（看红边细节）
    static readonly string[] Tags = { "far_r0", "far_r180", "near_r0", "near_r180" };
    static readonly float[] Rots  = { 0f, 180f, 0f, 180f };
    static readonly float[] Occ   = { 0f, 0f, 0.85f, 0.85f };
    static readonly float[] Pit   = { 0.20f, 0.20f, 0.08f, 0.08f };

    readonly StringBuilder _sb = new StringBuilder();
    Component _sc; Type _ty;
    MethodInfo _mPlay, _mLabel, _mStop;
    PropertyInfo _pCount;
    Camera _cam;
    Vector3 _camPos0, _camRot0; float _fov0;
    readonly Dictionary<Material, Material> _map = new Dictionary<Material, Material>();

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
        if (_mPlay == null || _pCount == null) { _sb.AppendLine("x 演示场接口缺失"); yield return Done(); }
        _mStop.Invoke(_sc, null);

        _cam = Camera.main;
        if (_cam == null) { _sb.AppendLine("x Camera.main 为空"); yield return Done(); }
        _camPos0 = _cam.transform.position; _camRot0 = _cam.transform.eulerAngles; _fov0 = _cam.fieldOfView;

        int count = (int)_pCount.GetValue(_sc, null);
        int idx = -1;
        for (int i = 0; i < count; i++)
        {
            string lb = (string)_mLabel.Invoke(_sc, new object[] { i });
            if (lb != null && lb.Contains("墨龙")) { idx = i; break; }
        }

        _sb.AppendLine("===== ev_dragon_verify —— 墨龙朱砂描边验收 =====");
        _sb.AppendLine("同机位 / 同光照 / 同一帧内背靠背 Render（唯一变量 = _OutlineColor）");
        _sb.AppendLine("旧 = (0.042,0.038,0.036) 暗褐（改前）   新 = (0.48,0.035,0.022) 朱砂（已落盘）");
        _sb.AppendLine();

        if (idx < 0) { _sb.AppendLine("x 演示场面板无「墨龙」条目"); yield return Done(); }

        _mPlay.Invoke(_sc, new object[] { idx });
        yield return new WaitForSeconds(2.5f);

        var actor = FindActor(idx);
        if (actor == null) { _sb.AppendLine("x 找不到演员 Showcase_Actor_" + idx); yield return Done(); }

        var rs = new List<Renderer>(actor.GetComponentsInChildren<Renderer>(true));
        foreach (var r in rs)
        {
            var sm = r.sharedMaterial;
            if (sm == null) continue;
            if (!_map.ContainsKey(sm)) { var inst = new Material(sm); inst.name = sm.name + "_ED"; _map[sm] = inst; }
            r.sharedMaterial = _map[sm];
        }
        if (_map.Count == 0) { _sb.AppendLine("x 龙身上无材质"); yield return Done(); }

        _sb.AppendLine("条目 idx=" + idx + "  渲染器=" + rs.Count + "  材质=" + _map.Count + " 份");
        foreach (var kv in _map)
            _sb.AppendLine("   " + kv.Value.name.PadRight(26)
                + "  改前 _OutlineColor=(" + kv.Value.GetColor("_OutlineColor").r.ToString("F3") + ","
                + kv.Value.GetColor("_OutlineColor").g.ToString("F3") + ","
                + kv.Value.GetColor("_OutlineColor").b.ToString("F3") + ")"
                + "  _OutlineWidth=" + kv.Value.GetFloat("_OutlineWidth").ToString("F4"));
        _sb.AppendLine();

        var b = Bounds0(actor);
        float halfH = Mathf.Max(b.extents.y, 0.05f);
        float halfFov = Mathf.Tan(_cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
        _sb.AppendLine("围盒 " + b.size.x.ToString("F2") + " x " + b.size.y.ToString("F2") + " x " + b.size.z.ToString("F2")
            + "   远机位 dist=" + (b.extents.magnitude / Mathf.Sin(_cam.fieldOfView * 0.5f * Mathf.Deg2Rad)).ToString("F2")
            + "   近机位 dist=" + (halfH / (0.85f * halfFov)).ToString("F2"));
        _sb.AppendLine();

        for (int ri = 0; ri < Tags.Length; ri++)
        {
            float r0 = Rots[ri] * Mathf.Deg2Rad;
            Vector3 dir = new Vector3(Mathf.Sin(r0), Pit[ri], Mathf.Cos(r0)).normalized;
            float d = Occ[ri] <= 0f
                ? b.extents.magnitude / Mathf.Sin(_cam.fieldOfView * 0.5f * Mathf.Deg2Rad)
                : halfH / (Occ[ri] * halfFov);
            _cam.transform.position = b.center + dir * d;
            _cam.transform.LookAt(b.center);

            var tGhost = Shot(rs, -1);
            var tOff   = Shot(rs, 0);
            var tOld   = Shot(rs, 1);
            var tNew   = Shot(rs, 2);

            Stat(Tags[ri], tGhost, tOff, tOld, tNew);
            Save(tNew, Tags[ri] + "_new");
            Save(tOld, Tags[ri] + "_old");

            Destroy(tGhost); Destroy(tOff); Destroy(tOld); Destroy(tNew);
            yield return null;
        }

        yield return Done();
    }

    Texture2D Shot(List<Renderer> rs, int mode)   // -1=ghost 0=off 1=old 2=new
    {
        foreach (var kv in _map)
        {
            var m = kv.Value;
            if (m.HasProperty("_OutlineWidth")) m.SetFloat("_OutlineWidth", mode <= 0 ? 0.0001f : 0.018f);
            if (m.HasProperty("_OutlineColor")) m.SetColor("_OutlineColor", mode == 2 ? NEW_C : OLD_C);
        }
        foreach (var r in rs)
        {
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.enabled = (mode >= 0);
        }
        return Render();
    }

    void Stat(string tag, Texture2D ghost, Texture2D off, Texture2D oldT, Texture2D newT)
    {
        var g = ghost.GetPixels32(); var o = off.GetPixels32();
        var l = oldT.GetPixels32();  var n = newT.GetPixels32();

        long geom = 0, dOld = 0, dNew = 0, redNew = 0, redOld = 0;
        long sR = 0, sG = 0, sB = 0;

        for (int k = 0; k < n.Length; k++)
        {
            if (MaxDiff(n[k], g[k]) <= DiffTh) continue;   // 背景
            geom++;
            sR += n[k].r; sG += n[k].g; sB += n[k].b;

            bool isNew = MaxDiff(n[k], o[k]) > DiffTh;
            bool isOld = MaxDiff(l[k], o[k]) > DiffTh;
            if (isNew) dNew++;
            if (isOld) dOld++;

            int mx = Mathf.Max(n[k].r, Mathf.Max(n[k].g, n[k].b));
            int mn = Mathf.Min(n[k].r, Mathf.Min(n[k].g, n[k].b));
            if (mx - mn > SatFloor)
            {
                float h = HueDeg(n[k].r, n[k].g, n[k].b);
                if (h >= 0f && (h < 35f || h >= 325f)) redNew++;
            }
            int mx2 = Mathf.Max(l[k].r, Mathf.Max(l[k].g, l[k].b));
            int mn2 = Mathf.Min(l[k].r, Mathf.Min(l[k].g, l[k].b));
            if (mx2 - mn2 > SatFloor)
            {
                float h2 = HueDeg(l[k].r, l[k].g, l[k].b);
                if (h2 >= 0f && (h2 < 35f || h2 >= 325f)) redOld++;
            }
        }

        if (geom == 0) { _sb.AppendLine("── " + tag + "：0 像素（龙不在画面内？）"); _sb.AppendLine(); return; }

        _sb.AppendLine("── " + tag);
        _sb.AppendLine("   对象像素 " + geom + "（占屏 " + (100f * geom / n.Length).ToString("F2") + "%）"
            + "   平均 RGB(" + (sR / geom) + "," + (sG / geom) + "," + (sB / geom) + ")");
        _sb.AppendLine("   描边可见像素   旧暗褐 " + Pct(dOld, geom) + "%  ->  新朱砂 " + Pct(dNew, geom) + "%"
            + "   （×" + (dOld == 0 ? "∞" : (dNew / (float)dOld).ToString("F1")) + "）");
        _sb.AppendLine("   红区像素(彩度>" + SatFloor + " 且色相 325~35°)   旧 " + Pct(redOld, geom) + "%  ->  新 "
            + Pct(redNew, geom) + "%");
        _sb.AppendLine();
    }

    static float Pct(long a, long b) { return b == 0 ? 0f : 100f * a / b; }

    static int MaxDiff(Color32 a, Color32 b)
    {
        return Mathf.Max(Mathf.Abs(a.r - b.r), Mathf.Max(Mathf.Abs(a.g - b.g), Mathf.Abs(a.b - b.b)));
    }

    static float HueDeg(int R, int G, int B)
    {
        float r = R / 255f, g = G / 255f, b = B / 255f;
        float mx = Mathf.Max(r, Mathf.Max(g, b)), mn = Mathf.Min(r, Mathf.Min(g, b));
        float d = mx - mn;
        if (d < 1e-5f) return -1f;
        float h;
        if (mx == r) h = ((g - b) / d) % 6f;
        else if (mx == g) h = (b - r) / d + 2f;
        else h = (r - g) / d + 4f;
        h *= 60f;
        if (h < 0f) h += 360f;
        return h;
    }

    void Save(Texture2D tex, string tag)
    {
        Directory.CreateDirectory(ShotDir);
        string fn = ShotDir + "/ED_" + tag + "_" + tex.width + "x" + tex.height + ".png";
        try { File.WriteAllBytes(fn, tex.EncodeToPNG()); } catch (Exception e) { _sb.AppendLine("     写图失败 " + e.Message); }
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

var g0 = new GameObject("ev_dragon_verify");
g0.AddComponent<ev_dragon_verify>();
return "EV_DRAGON_VERIFY_STARTED";
