using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

// ev_hue.cs —— 「固有色保留」(_HueKeep) 定标验收
//
// 用户诉求：不要剥夺模型各部位自己的颜色（腿、身体、武器各不相同）。
// 做法：同一机位、同一光照、同一 tick，逐档改运行时 clone 材质的 _HueKeep（不写盘）。
// 判据（重点不是"更艳"，而是"颜色更多样"）：
//   · 平均彩度          —— 显色程度
//   · 有色像素占比       —— 身上有多少面积显示出颜色（彩度 > SatFloor）
//   · 色相直方图 12 桶   —— 主桶占比、有效色相数（≥5%）
//   · ★ 色相熵 H         —— 颜色多样性（0 = 单一色相；最大 log2(12)=3.585）
public class ev_hue : MonoBehaviour
{
    const string ReportPath = "D:/Unity Project/InkWash/Tools/reports/ev_hue.txt";
    const string ShotDir    = "D:/Unity Project/InkWash/Tools/screenshots/enemies";
    const int DiffTh   = 6;
    const int SatFloor = 14;

    static readonly string[] Objs  = { "墨徒", "墨偶", "墨魇", "墨山", "墨骨", "墨龙" };
    static readonly float[]  Keeps = { 0f, 0.5f, 1f };
    static readonly string[] KTag  = { "keep0.0", "keep0.5", "keep1.0" };

    static readonly string[] HueName =
        { "红", "橙", "黄", "黄绿", "绿", "青绿", "青", "蓝", "蓝紫", "紫", "品红", "粉红" };

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
        if (_mPlay == null || _pCount == null) { _sb.AppendLine("x 演示场接口缺失"); yield return Done(); }
        _mStop.Invoke(_sc, null);

        _cam = Camera.main;
        if (_cam == null) { _sb.AppendLine("x Camera.main 为空"); yield return Done(); }
        _camPos0 = _cam.transform.position; _camRot0 = _cam.transform.eulerAngles; _fov0 = _cam.fieldOfView;

        int count = (int)_pCount.GetValue(_sc, null);
        _sb.AppendLine("===== ev_hue —— 固有色保留 (@_HueKeep) 定标 =====");
        _sb.AppendLine("同机位 / 同光照 / 同 tick，唯一变量 = _HueKeep（0=旧行为，1=各部位本色全显）");
        _sb.AppendLine("判据重点 = 色相熵（颜色多样性）与有色像素占比，而非单纯更艳");
        _sb.AppendLine("_HueSat 固定 1.0；材质为运行时 clone，不写盘");
        _sb.AppendLine();

        foreach (string want in Objs)
        {
            int idx = -1;
            for (int i = 0; i < count; i++)
            {
                string lb = (string)_mLabel.Invoke(_sc, new object[] { i });
                if (lb != null && lb.Contains(want)) { idx = i; break; }
            }
            if (idx < 0) { _sb.AppendLine("── " + want + "：面板无此条目"); _sb.AppendLine(); continue; }

            _mPlay.Invoke(_sc, new object[] { idx });
            yield return new WaitForSeconds(2.2f);

            var actor = FindActor(idx);
            if (actor == null) { _sb.AppendLine("── " + want + "：找不到演员"); _sb.AppendLine(); continue; }

            var rs = new List<Renderer>(actor.GetComponentsInChildren<Renderer>(true));
            var map = new Dictionary<Material, Material>();
            var nm  = new Dictionary<Material, string>();
            foreach (var r in rs)
            {
                var sm = r.sharedMaterial;
                if (sm == null) continue;
                Material inst;
                if (!map.ContainsKey(sm)) { inst = new Material(sm); inst.name = sm.name + "_EH"; map[sm] = inst; nm[inst] = sm.name; }
                else inst = map[sm];
                r.sharedMaterial = inst;
            }
            if (map.Count == 0) { _sb.AppendLine("── " + want + "：无材质"); _sb.AppendLine(); continue; }

            var b = Bounds0(actor);
            float halfH = Mathf.Max(b.extents.y, 0.05f);
            float dist  = halfH / (0.50f * Mathf.Tan(_cam.fieldOfView * 0.5f * Mathf.Deg2Rad));

            _sb.AppendLine("── " + want + "  idx=" + idx + "  渲染器=" + rs.Count + "  材质=" + map.Count + " 份");
            foreach (var kv in map)
                _sb.AppendLine("     材质 " + nm[kv.Value].PadRight(26)
                    + "  _ChromaKeep=" + Fmt(kv.Value, "_ChromaKeep")
                    + "  _BandBias=" + Fmt(kv.Value, "_BandBias")
                    + "  _HueKeep=" + Fmt(kv.Value, "_HueKeep"));

            for (int ki = 0; ki < Keeps.Length; ki++)
            {
                foreach (var kv in map)
                {
                    if (kv.Value.HasProperty("_HueKeep")) kv.Value.SetFloat("_HueKeep", Keeps[ki]);
                    if (kv.Value.HasProperty("_HueSat"))  kv.Value.SetFloat("_HueSat", 1f);
                }

                float r0 = 0f;
                Vector3 dir = new Vector3(Mathf.Sin(r0), 0.22f, Mathf.Cos(r0)).normalized;
                _cam.transform.position = b.center + dir * dist;
                _cam.transform.LookAt(b.center);

                Snap(want, rs, KTag[ki], true);
                yield return null;
            }
            _sb.AppendLine();
        }

        yield return Done();
    }

    static string Fmt(Material m, string p)
    {
        if (!m.HasProperty(p)) return "<无>";
        return m.GetFloat(p).ToString("F3");
    }

    void Snap(string disp, List<Renderer> rs, string tag, bool saveImg)
    {
        var wasOn = new List<bool>(); var wasSh = new List<ShadowCastingMode>();
        foreach (var r in rs) { wasOn.Add(r.enabled); wasSh.Add(r.shadowCastingMode); }
        foreach (var r in rs) r.shadowCastingMode = ShadowCastingMode.Off;

        foreach (var r in rs) r.enabled = true;
        var texA = Render();
        foreach (var r in rs) r.enabled = false;
        var texB = Render();

        for (int k = 0; k < rs.Count; k++) { rs[k].enabled = wasOn[k]; rs[k].shadowCastingMode = wasSh[k]; }

        var pa = texA.GetPixels32(); var pb = texB.GetPixels32();
        long n = 0, sR = 0, sG = 0, sB = 0, sCh = 0, sLu = 0, black = 0, colored = 0;
        long[] bins = new long[12];
        for (int k = 0; k < pa.Length; k++)
        {
            int d = Mathf.Max(Mathf.Abs(pa[k].r - pb[k].r), Mathf.Max(Mathf.Abs(pa[k].g - pb[k].g), Mathf.Abs(pa[k].b - pb[k].b)));
            if (d <= DiffTh) continue;
            n++;
            int R = pa[k].r, G = pa[k].g, B = pa[k].b;
            sR += R; sG += G; sB += B;
            int mx = Mathf.Max(R, Mathf.Max(G, B)), mn = Mathf.Min(R, Mathf.Min(G, B));
            sCh += mx - mn;
            sLu += (int)(0.2126f * R + 0.7152f * G + 0.0722f * B);
            if (R < 8 && G < 8 && B < 8) black++;
            if (mx - mn > SatFloor)
            {
                colored++;
                float h = HueDeg(R, G, B);
                if (h >= 0f) bins[(int)(h / 30f) % 12]++;
            }
        }

        if (saveImg)
        {
            Directory.CreateDirectory(ShotDir);
            string fn = ShotDir + "/EH_" + disp + "_" + tag + "_" + texA.width + "x" + texA.height + ".png";
            try { File.WriteAllBytes(fn, texA.EncodeToPNG()); } catch (Exception e) { _sb.AppendLine("     写图失败 " + e.Message); }
        }

        if (n == 0) { _sb.AppendLine("     [" + tag + "] 0 像素"); Destroy(texA); Destroy(texB); return; }

        // 色相熵
        float H = 0f; int eff = 0; int topBin = -1; long topCnt = 0;
        for (int i = 0; i < 12; i++)
        {
            if (bins[i] <= 0) continue;
            float p = (float)bins[i] / Mathf.Max(colored, 1);
            H -= p * Mathf.Log(p, 2f);
            if (bins[i] >= colored * 0.05f) eff++;
            if (bins[i] > topCnt) { topCnt = bins[i]; topBin = i; }
        }

        _sb.AppendLine("     [" + tag.PadRight(8) + "] RGB(" + (sR / n).ToString().PadLeft(3) + ","
            + (sG / n).ToString().PadLeft(3) + "," + (sB / n).ToString().PadLeft(3) + ")"
            + "  彩度 " + ((float)sCh / n).ToString("F1").PadLeft(5)
            + "  有色 " + (100f * colored / n).ToString("F0").PadLeft(3) + "%"
            + "  纯黑 " + (100f * black / n).ToString("F0").PadLeft(3) + "%"
            + "  ★色相熵 " + H.ToString("F2")
            + "  有效色相 " + eff
            + "  主桶 " + (topBin < 0 ? "灰" : HueName[topBin]) + (topBin < 0 ? "" : (topBin * 30) + "-" + ((topBin + 1) * 30) + "°")
            + " " + (colored == 0 ? 0 : 100 * topCnt / colored) + "%"
            + (saveImg ? "   >>EH_" + disp + "_" + tag + ".png" : ""));

        if (colored > 0)
        {
            var bar = new StringBuilder("                色相分布 ");
            for (int i = 0; i < 12; i++)
            {
                int w = (int)Mathf.Round(bins[i] * 24f / colored);
                bar.Append("|" + new string('#', Mathf.Clamp(w, 0, 24)).PadRight(24, '.'));
            }
            _sb.AppendLine(bar.ToString());
        }
        Destroy(texA); Destroy(texB);
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

var g0 = new GameObject("ev_hue");
g0.AddComponent<ev_hue>();
return "EV_HUE_STARTED";
