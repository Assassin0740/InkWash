using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

// ev_scan.cs —— 敌人墨色定标扫描（运行时 clone 材质，不写磁盘）
//
// 用户要求：敌人也要有颜色、接近主角；再加红色水墨边标识阵营。
//
// ★ 为什么不能直接把数字对齐主角：
//   _ChromaKeep 是【乘数】—— chroma = (albedoHue - gh) * _ChromaKeep * 2.2
//   效果取决于【各贴图自身彩度】。主角贴图近黑（低彩度）所以要 0.98 才够；
//   而 KayKit 系贴图本身饱和度不低，直接给 0.98 有可能过曝成彩虹
//   （龙当年就踩过：_ChromaKeep 0.45 → 过曝，不得不压到 0.10）。
//   ⇒ 必须先实测「哪一档渲出来的彩度最接近主角」。
//
// 本探针：主角（标杆）+ 五个普通敌人，逐个召唤。
//   _BandBias 统一抬到 +0.10（按《美术风格规范》§9.20：与主角 +0.15 差 0.05，
//   保留"比主角略沉"的层级，但不再塌进焦墨）
//   只扫 _ChromaKeep ∈ {基线(原值), 0.45, 0.70, 0.98}，
//   每档在【受光 0°】与【背光 180°】各拍一帧，同 tick 背靠背差分。
public class ev_scan : MonoBehaviour
{
    const string ReportPath = "D:/Unity Project/InkWash/Tools/reports/ev_scan.txt";
    const string ShotDir    = "D:/Unity Project/InkWash/Tools/screenshots/color";
    const int DiffTh = 6;
    const float BB_NEW = 0.10f;

    static readonly string[] Targets = { "主角", "墨徒", "墨偶", "墨魇", "墨山", "墨骨" };
    static readonly float[] CKS = { -1f, 0.45f, 0.70f, 0.98f };   // -1 = 基线（保留原值）
    static readonly float[] Degs = { 0f, 180f };

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
        if (_cam == null) { _sb.AppendLine("x Camera.main 为空"); yield return Done(); }
        _camPos0 = _cam.transform.position; _camRot0 = _cam.transform.eulerAngles; _fov0 = _cam.fieldOfView;

        int count = (int)_pCount.GetValue(_sc, null);

        _sb.AppendLine("===== ev_scan —— 敌人墨色定标扫描 =====");
        _sb.AppendLine("固定 _BandBias = " + BB_NEW.ToString("F2") + "（主角为 +0.15），只扫 _ChromaKeep");
        _sb.AppendLine("判据：平均彩度（max(R,G,B)-min(R,G,B) 的均值）——越接近主角越好；纯黑占比越小越好");
        _sb.AppendLine("运行时 clone 材质，不写磁盘。");
        _sb.AppendLine();
        _sb.AppendLine("面板条目（" + count + " 条）：");
        for (int i = 0; i < count; i++)
            _sb.AppendLine("   [" + i.ToString("D2") + "] " + (string)_mLabel.Invoke(_sc, new object[] { i }));
        _sb.AppendLine();

        foreach (string want in Targets)
        {
            int idx = -1;
            for (int i = 0; i < count; i++)
            {
                string lb = (string)_mLabel.Invoke(_sc, new object[] { i });
                if (lb.Contains(want)) { idx = i; break; }
            }
            if (idx < 0) { _sb.AppendLine("── " + want + "：面板里没有该条目，跳过"); _sb.AppendLine(); continue; }

            _mPlay.Invoke(_sc, new object[] { idx });
            yield return new WaitForSeconds(2.5f);

            var actor = FindActor(idx, want);
            if (actor == null) { _sb.AppendLine("── " + want + "：召唤后找不到演员，跳过"); _sb.AppendLine(); continue; }

            yield return ScanOne(want, idx, actor);
        }

        yield return Done();
    }

    System.Collections.IEnumerator ScanOne(string want, int idx, GameObject actor)
    {
        var rs = new List<Renderer>(actor.GetComponentsInChildren<Renderer>(true));
        var map = new Dictionary<Material, Material>();
        foreach (var r in rs)
        {
            var sm = r.sharedMaterial;
            if (sm == null) continue;
            Material inst;
            if (!map.ContainsKey(sm)) { inst = new Material(sm); inst.name = sm.name + "_EV"; map[sm] = inst; }
            else inst = map[sm];
            r.sharedMaterial = inst;
        }

        _sb.AppendLine("── " + want + "   panelIdx=" + idx + "  演员=" + actor.name
                       + "  渲染器=" + rs.Count + "  材质=" + map.Count + " 份");
        foreach (var kv in map)
            _sb.AppendLine("     源 " + kv.Key.name
                           + "   原 _ChromaKeep=" + Fmt(kv.Key, "_ChromaKeep")
                           + "  原 _BandBias=" + Fmt(kv.Key, "_BandBias"));

        var b = Bounds0(actor);
        if (b.size.sqrMagnitude < 1e-8f) { _sb.AppendLine("     包围盒为空，跳过"); _sb.AppendLine(); yield break; }
        _sb.AppendLine("     包围盒 center=" + b.center.ToString("F2") + " size=" + b.size.ToString("F2"));

        float halfH = Mathf.Max(b.extents.y, 0.05f);
        float dist = halfH / (0.50f * Mathf.Tan(_cam.fieldOfView * 0.5f * Mathf.Deg2Rad));

        foreach (float ck in CKS)
        {
            bool baseline = ck < 0f;
            foreach (var kv in map)
            {
                if (baseline) continue;                       // 基线：保留原值
                if (kv.Value.HasProperty("_ChromaKeep")) kv.Value.SetFloat("_ChromaKeep", ck);
                if (kv.Value.HasProperty("_BandBias"))   kv.Value.SetFloat("_BandBias", BB_NEW);
            }
            string tag = baseline ? "基线" : ck.ToString("F2");

            foreach (float deg in Degs)
            {
                float r = deg * Mathf.Deg2Rad;
                Vector3 dir = new Vector3(Mathf.Sin(r), 0.22f, Mathf.Cos(r)).normalized;
                _cam.transform.position = b.center + dir * dist;
                _cam.transform.LookAt(b.center);
                Snap(want, rs, tag, deg, baseline);
                yield return null;
            }
        }
        _sb.AppendLine();
        yield return null;
    }

    static string Fmt(Material m, string p)
    { return m.HasProperty(p) ? m.GetFloat(p).ToString("F2") : "<无>"; }

    void Snap(string want, List<Renderer> rs, string tag, float deg, bool baseline)
    {
        var wasOn = new List<bool>(); var wasSh = new List<ShadowCastingMode>();
        foreach (var r in rs) { wasOn.Add(r.enabled); wasSh.Add(r.shadowCastingMode); }
        foreach (var r in rs) r.shadowCastingMode = ShadowCastingMode.Off;

        foreach (var r in rs) r.enabled = true;
        var texA = Render();
        foreach (var r in rs) r.enabled = false;
        var texB = Render();

        for (int k = 0; k < rs.Count; k++) { rs[k].enabled = wasOn[k]; rs[k].shadowCastingMode = wasSh[k]; }

        int w = texA.width, h = texA.height;
        var pa = texA.GetPixels32(); var pb = texB.GetPixels32();
        long n = 0, sR = 0, sG = 0, sB = 0, sCh = 0, sLu = 0, black = 0;
        for (int k = 0; k < pa.Length; k++)
        {
            int d = Mathf.Max(Mathf.Abs(pa[k].r - pb[k].r), Mathf.Max(Mathf.Abs(pa[k].g - pb[k].g), Mathf.Abs(pa[k].b - pb[k].b)));
            if (d <= DiffTh) continue;
            n++;
            int R = pa[k].r, G = pa[k].g, B = pa[k].b;
            sR += R; sG += G; sB += B;
            sCh += Mathf.Max(R, Mathf.Max(G, B)) - Mathf.Min(R, Mathf.Min(G, B));
            sLu += (int)(0.2126f * R + 0.7152f * G + 0.0722f * B);
            if (R < 8 && G < 8 && B < 8) black++;
        }

        // 只给关键档存图（基线 + 0.98），控制体积
        if (baseline || tag == "0.98")
        {
            Directory.CreateDirectory(ShotDir);
            string fn = ShotDir + "/EV_" + want + "_" + tag + "_" + deg.ToString("F0") + ".png";
            try { File.WriteAllBytes(fn, texA.EncodeToPNG()); } catch (Exception e) { _sb.AppendLine("     写图失败 " + e.Message); }
        }

        if (n == 0)
        {
            _sb.AppendLine("     [" + deg.ToString("F0") + "°] _ChromaKeep=" + tag + "   0 像素");
            Destroy(texA); Destroy(texB); return;
        }

        _sb.AppendLine("     [" + deg.ToString("F0") + "°] _ChromaKeep=" + tag.PadRight(6)
                       + " 像素 " + n
                       + "  平均色 RGB(" + (sR / n) + "," + (sG / n) + "," + (sB / n) + ")"
                       + "  彩度 " + ((float)sCh / n).ToString("F1").PadLeft(5)
                       + "  亮度 " + (sLu / n).ToString().PadLeft(3)
                       + "  纯黑 " + (100f * black / n).ToString("F0").PadLeft(3) + "%");
        Destroy(texA); Destroy(texB);
    }

    Texture2D Render()
    {
        int w = Mathf.Clamp(Screen.width, 320, 1280);
        int h = Mathf.Clamp(Screen.height, 180, 720);
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

    static GameObject FindActor(int i, string want)
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

var g = new GameObject("ev_scan");
g.AddComponent<ev_scan>();
return "EV_SCAN_STARTED";
