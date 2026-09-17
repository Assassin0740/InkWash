using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

// dg_color.cs —— 查「敌人没有色彩」到底出在哪一环。
//
// 已知（磁盘参数）：
//   主角 M_Character_Ink  _ChromaKeep 0.98  _BandBias +0.15  _InkDensity 0.55
//   墨山 M_Ink_Enemy_MoShan _ChromaKeep 0.30  _BandBias -0.18  _InkDensity 0.50
//   墨徒 M_Ink_Enemy_MoGuai _ChromaKeep 0.28  _BandBias -0.16
//   墨骨/MoOu/MoTu/MoYan   _ChromaKeep 0.12~0.22  _BandBias -0.24~-0.42
//   ⇒ 色彩注入量差 3~8 倍、明暗偏移差 0.33（= 4 阶量化下 1.3 阶）
//
// 但画面上敌人是**纯黑剪影**，这超出 0.33 能解释的范围。三个待验假设：
//   H1 光照朝向：敌人背光 ⇒ lambert 低 ⇒ 落焦墨（主角面向相机 ⇒ 受光）
//   H2 屏幕占比太小 + 轮廓线是「屏幕属性」(0.014 ⇒ 约 5.8 px) ⇒ 轮廓把主体糊死
//   H3 贴图彩度本就低 ⇒ (albedoHue - luma) 接近 0，_ChromaKeep 再乘 0.28 就没了
//
// 本探针把三件事一次测掉：
//   ① 相机【推近】到对象占屏高 ~50% ⇒ 直接证伪/证实 H2
//   ② 绕对象【4 个方位】各拍一帧 ⇒ 直接看清 H1（受光面 vs 背光面）
//   ③ 统计对象实际画出的像素：平均 RGB / 平均彩度 / 亮度分布 / 量化直方图
//      ★ 彩度 = max(r,g,b) - min(r,g,b)，均值≈0 就是「没有色彩」的硬判据
public class dg_color : MonoBehaviour
{
    const string ReportPath = "D:/Unity Project/InkWash/Tools/reports/dg_color.txt";
    const string ShotDir    = "D:/Unity Project/InkWash/Tools/screenshots/color";
    const int DiffTh = 6;

    readonly StringBuilder _sb = new StringBuilder();
    Component _sc; Type _ty;
    MethodInfo _mPlay, _mLabel, _mStop;
    PropertyInfo _pCount;
    Camera _cam;
    Vector3 _camPos0, _camRot0; float _fov0; bool _camSaved;

    void Start() { StartCoroutine(Run()); }

    System.Collections.IEnumerator Run()
    {
        yield return null; yield return null;

        foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
        { var t = a.GetType("InkWash.DebugTools.ActionShowcase"); if (t != null) { _ty = t; break; } }
        if (_ty == null) { _sb.AppendLine("x 找不到 ActionShowcase 类型"); yield return Done(); }
        foreach (var o in UnityEngine.Object.FindObjectsOfType(_ty, true)) { _sc = o as Component; if (_sc != null) break; }
        if (_sc == null) { _sb.AppendLine("x 场景里没有 ActionShowcase 实例"); yield return Done(); }

        _mPlay  = _ty.GetMethod("PlayForCapture", BindingFlags.Public | BindingFlags.Instance);
        _mLabel = _ty.GetMethod("ItemLabel",     BindingFlags.Public | BindingFlags.Instance);
        _mStop  = _ty.GetMethod("StopAutoClose", BindingFlags.Public | BindingFlags.Instance);
        _pCount = _ty.GetProperty("ItemCount",   BindingFlags.Public | BindingFlags.Instance);
        _mStop.Invoke(_sc, null);

        _cam = Camera.main;
        if (_cam == null) { _sb.AppendLine("x Camera.main 为空"); yield return Done(); }
        _camPos0 = _cam.transform.position; _camRot0 = _cam.transform.eulerAngles;
        _fov0 = _cam.fieldOfView; _camSaved = true;

        _sb.AppendLine("===== dg_color —— 敌人「没有色彩」定位 =====");
        _sb.AppendLine("场景 " + UnityEngine.SceneManagement.SceneManager.GetActiveScene().name
                       + " / 相机 " + _cam.name + " " + Screen.width + "x" + Screen.height
                       + " fov=" + _cam.fieldOfView.ToString("F1") + " ortho=" + _cam.orthographic);
        _sb.AppendLine("方法：相机推近到对象占屏高约 50%，绕对象 4 个方位各拍一帧（同 tick 差分帧取该对象画出的像素）");
        _sb.AppendLine("判据：彩度 = max(R,G,B)-min(R,G,B) 的均值；≈0 即该对象画出来的是灰/黑，没有色彩");
        _sb.AppendLine();

        // ── 条目索引：按名字找，不硬编码（面板加过条目）──
        int count = (int)_pCount.GetValue(_sc, null);
        _sb.AppendLine("演示场条目（共 " + count + "）：");
        var idx = new Dictionary<string, int>();
        for (int i = 0; i < count; i++)
        {
            string lb = (string)_mLabel.Invoke(_sc, new object[] { i });
            _sb.AppendLine("  [" + i.ToString("D2") + "] " + lb);
            foreach (var nk in new string[] { "墨山", "墨徒", "墨骨", "墨龙" })
                if (lb.Contains(nk) && !idx.ContainsKey(nk)) idx[nk] = i;
        }
        _sb.AppendLine();

        // ── 主角：场景常驻根对象 Player ──
        GameObject player = null;
        foreach (var root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
            if (root.name == "Player") { player = root; break; }
        if (player != null)
        {
            _sb.AppendLine("########## 对照组：主角 Player ##########");
            Shoot(player);
            _sb.AppendLine();
        }
        else _sb.AppendLine("! 场景里没有 Player 根对象，主角对照缺失");

        // ── 敌人 ──
        foreach (var nk in new string[] { "墨山", "墨徒", "墨骨" })
        {
            if (!idx.ContainsKey(nk)) { _sb.AppendLine("── " + nk + "：面板里没有该条目"); continue; }
            int i = idx[nk];
            _mPlay.Invoke(_sc, new object[] { i });
            yield return new WaitForSeconds(2.5f);

            var actor = FindActor(i);
            if (actor == null) { _sb.AppendLine("── " + nk + "（条目 " + i + "）：找不到演员"); _sb.AppendLine(); continue; }

            _sb.AppendLine("########## " + nk + "（条目 " + i + "，演员 " + actor.name + "）##########");
            Shoot(actor);
            _sb.AppendLine();
        }

        yield return Done();
    }

    // 绕对象 4 个方位，每个方位：相机推近 + 同 tick 差分帧 + 像素色彩统计
    void Shoot(GameObject go)
    {
        var rs = new List<Renderer>(go.GetComponentsInChildren<Renderer>(true));
        if (rs.Count == 0) { _sb.AppendLine("  没有渲染器"); return; }

        var b = Bounds0(go);
        _sb.AppendLine("  包围盒 center=" + b.center.ToString("F2") + " size=" + b.size.ToString("F2")
                       + "  渲染器 " + rs.Count + " 个");

        float[] az = new float[] { 0f, 90f, 180f, 270f };
        for (int k = 0; k < az.Length; k++)
        {
            float r = az[k] * Mathf.Deg2Rad;
            Vector3 dir = new Vector3(Mathf.Sin(r), 0.22f, Mathf.Cos(r)).normalized;   // 由对象指向相机
            float halfH = Mathf.Max(b.extents.y, 0.05f);
            float dist = halfH / (0.50f * Mathf.Tan(_cam.fieldOfView * 0.5f * Mathf.Deg2Rad));
            _cam.transform.position = b.center + dir * dist;
            _cam.transform.LookAt(b.center);

            Snap(go, rs, az[k].ToString("F0"));
        }

        // 恢复相机
        _cam.transform.position = _camPos0;
        _cam.transform.eulerAngles = _camRot0;
        _cam.fieldOfView = _fov0;
    }

    void Snap(GameObject go, List<Renderer> rs, string tag)
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

        long n = 0, sumR = 0, sumG = 0, sumB = 0, sumChroma = 0, sumLuma = 0;
        int minL = 999, maxL = -1;
        var hist = new Dictionary<int, int>();
        int minX = int.MaxValue, maxX = -1, minY = int.MaxValue, maxY = -1;

        for (int k = 0; k < pa.Length; k++)
        {
            int d = Mathf.Max(Mathf.Abs(pa[k].r - pb[k].r), Mathf.Max(Mathf.Abs(pa[k].g - pb[k].g), Mathf.Abs(pa[k].b - pb[k].b)));
            if (d <= DiffTh) continue;
            n++;
            int R = pa[k].r, G = pa[k].g, B = pa[k].b;
            sumR += R; sumG += G; sumB += B;
            int mx = Mathf.Max(R, Mathf.Max(G, B)), mn = Mathf.Min(R, Mathf.Min(G, B));
            sumChroma += mx - mn;
            int l = (int)(0.2126f * R + 0.7152f * G + 0.0722f * B);
            sumLuma += l;
            if (l < minL) minL = l; if (l > maxL) maxL = l;
            int key = ((R >> 4) << 8) | ((G >> 4) << 4) | (B >> 4);
            if (!hist.ContainsKey(key)) hist[key] = 0;
            hist[key]++;
            int x = k % w, y = k / w;
            if (x < minX) minX = x; if (x > maxX) maxX = x;
            if (y < minY) minY = y; if (y > maxY) maxY = y;
        }

        Directory.CreateDirectory(ShotDir);
        string fn = ShotDir + "/CL_" + go.name + "_" + tag + ".png";
        try { File.WriteAllBytes(fn, texA.EncodeToPNG()); } catch (Exception e) { _sb.AppendLine("    写图失败 " + e.Message); }

        if (n == 0)
        {
            _sb.AppendLine("  [方位 " + tag + "] 0 像素（相机没框住？）");
            Destroy(texA); Destroy(texB); return;
        }

        _sb.AppendLine("  [方位 " + tag + "°] 像素 " + n + " (" + (100f * n / pa.Length).ToString("F2") + "%)"
                       + "  屏内 x[" + minX + "," + maxX + "] y[" + minY + "," + maxY + "]"
                       + (100f * (maxX - minX + 1) / w).ToString("F0") + "%x" + (100f * (maxY - minY + 1) / h).ToString("F0") + "%");
        _sb.AppendLine("      平均色 RGB(" + (sumR / n) + "," + (sumG / n) + "," + (sumB / n) + ")"
                       + "  平均彩度 " + ((float)sumChroma / n).ToString("F1")
                       + "  亮度 mean " + (sumLuma / n) + " [" + minL + ".." + maxL + "]");

        // 量化色 top5（4 bit/通道）
        var keys = new List<int>(hist.Keys);
        keys.Sort(delegate (int a1, int b1) { return hist[b1].CompareTo(hist[a1]); });
        var s2 = new StringBuilder("      主要色 ");
        int shown = 0;
        foreach (var kk in keys)
        {
            int R = ((kk >> 8) & 15) * 17, G = ((kk >> 4) & 15) * 17, B = (kk & 15) * 17;
            s2.Append("RGB(" + R + "," + G + "," + B + ")=" + (100f * hist[kk] / n).ToString("F0") + "%  ");
            if (++shown >= 5) break;
        }
        _sb.AppendLine(s2.ToString());

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

    static GameObject FindActor(int i)
    {
        string want = "Showcase_Actor_" + i;
        GameObject best = null;
        foreach (var root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
            foreach (var tr in root.GetComponentsInChildren<Transform>(true))
            {
                if (tr.gameObject.name == want) return tr.gameObject;
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
        if (_camSaved && _cam != null)
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

var g = new GameObject("dg_color");
g.AddComponent<dg_color>();
return "DG_COLOR_STARTED";
