using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

// dg_sc_vis.cs v2 —— 在【演示场 Showcase.unity】里量敌人可见性。
//
// ★ 修正两处上一轮的毛病：
//   ① 流程：不再自搭相机/灯光/地面、不再把 prefab 丢到 y=300 空中。
//      台子/相机/三盏灯/地面材质全部由 `ActionShowcase.BuildStage()` 提供（= 游戏同一套），
//      召唤走 `PlayForCapture(index)`，取景走演示场自己的 `Showcase_Camera`。
//   ② 度量：**同 tick 背靠背 `Camera.Render()`**。
//      v1 在两帧之间 `WaitForEndOfFrame()` ⇒ 追随相机会动、Idle 动画会走，
//      差出来的不只是演员（墨徒的差集包围盒满宽 x[0,1279] 就是串味的证据）。
//      并且先把演员的 shadowCastingMode 关掉 ⇒ 差集是**纯几何像素**，不含投影。
//
// 判据：演员像素占比 = (渲染器开 帧 与 渲染器关 帧 的逐像素差 > 阈值) 的像素数 / 总像素。
public class dg_sc_vis : MonoBehaviour
{
    const string ReportPath = "D:/Unity Project/InkWash/Tools/reports/dg_sc_vis.txt";
    const string ShotDir    = "D:/Unity Project/InkWash/Tools/screenshots/scene";
    const int DiffTh = 6;

    static readonly string[] Order = { "墨徒", "墨偶", "墨魇", "墨龙" };

    readonly StringBuilder _sb = new StringBuilder();
    Component _sc; Type _ty;
    MethodInfo _mPlay, _mLabel, _mStop;
    PropertyInfo _pCount;
    FieldInfo _fStatus;
    Camera _cam;

    void Start() { StartCoroutine(Run()); }

    System.Collections.IEnumerator Run()
    {
        yield return null; yield return null;

        foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
        { var t = a.GetType("InkWash.DebugTools.ActionShowcase"); if (t != null) { _ty = t; break; } }
        if (_ty == null) { _sb.AppendLine("× 找不到 ActionShowcase 类型"); yield return Done(); }

        foreach (var o in UnityEngine.Object.FindObjectsOfType(_ty, true)) { _sc = o as Component; if (_sc != null) break; }
        if (_sc == null) { _sb.AppendLine("× 场景里没有 ActionShowcase 实例 —— 演示场没进 Play？"); yield return Done(); }

        _mPlay   = _ty.GetMethod("PlayForCapture", BindingFlags.Public | BindingFlags.Instance);
        _mLabel  = _ty.GetMethod("ItemLabel",     BindingFlags.Public | BindingFlags.Instance);
        _mStop   = _ty.GetMethod("StopAutoClose", BindingFlags.Public | BindingFlags.Instance);
        _pCount  = _ty.GetProperty("ItemCount",   BindingFlags.Public | BindingFlags.Instance);
        _fStatus = _ty.GetField("_status", BindingFlags.NonPublic | BindingFlags.Instance);
        if (_mPlay == null || _mLabel == null || _pCount == null)
        { _sb.AppendLine("× 演示场缺公开接口"); yield return Done(); }

        _mStop.Invoke(_sc, null);

        _sb.AppendLine("===== dg_sc_vis v2 —— 演示场内敌人可见性（同 tick 差分帧） =====");
        _sb.AppendLine("台子/相机/灯光/地面材质 = ActionShowcase.BuildStage()（游戏同一套）");
        _sb.AppendLine("判据：同 tick 背靠背 Camera.Render()，演员渲染器【开】/【关】两帧逐像素求差，差值 > " + DiffTh);
        _sb.AppendLine("      且两帧均把演员 shadowCastingMode 设为 Off ⇒ 差集是纯几何像素，不含投影");
        _sb.AppendLine();

        int count = (int)_pCount.GetValue(_sc, null);
        _sb.AppendLine("演示场条目清单（共 " + count + " 条）：");
        var idx = new Dictionary<string, int>();
        for (int i = 0; i < count; i++)
        {
            string lb = (string)_mLabel.Invoke(_sc, new object[] { i });
            _sb.AppendLine("  [" + i.ToString("D2") + "] " + lb);
            foreach (var nk in Order)
            {
                if (!lb.Contains(nk)) continue;
                bool idle = lb.Contains("待机");
                if (!idx.ContainsKey(nk)) idx[nk] = i;                       // 首个同名条目兜底
                if (idle && !idx.ContainsKey(nk + "#idle")) idx[nk + "#idle"] = i;
            }
        }
        foreach (var nk in Order)
            if (idx.ContainsKey(nk + "#idle")) idx[nk] = idx[nk + "#idle"];
        _sb.AppendLine();

        _cam = Camera.main;
        if (_cam == null) { _sb.AppendLine("× Camera.main 为空"); yield return Done(); }
        _sb.AppendLine("演示场相机 = " + _cam.name + "  " + _cam.pixelWidth + "x" + _cam.pixelHeight
                       + "  ortho=" + _cam.orthographic + "  fov=" + _cam.fieldOfView.ToString("F1")
                       + "  pos=" + _cam.transform.position.ToString("F2"));
        _sb.AppendLine();

        foreach (var nk in Order)
        {
            if (!idx.ContainsKey(nk)) { _sb.AppendLine("── " + nk + "：演示场里没有该条目"); _sb.AppendLine(); continue; }
            int i = idx[nk];

            _mPlay.Invoke(_sc, new object[] { i });
            yield return new WaitForSeconds(2.5f);

            var actor = FindActor(i);
            if (actor == null)
            {
                _sb.AppendLine("── " + nk + "（条目 " + i + "）：找不到演员对象");
                _sb.AppendLine("     activeScene 根对象：" + RootNames());
                _sb.AppendLine("     演示场 _status = " + StatusText());
                _sb.AppendLine();
                continue;
            }

            _sb.AppendLine("── " + nk + "（条目 " + i + "，演员 " + actor.name
                           + " activeSelf=" + actor.activeSelf + " activeInHierarchy=" + actor.activeInHierarchy + "）");
            Measure(nk, actor);
            _sb.AppendLine();
        }

        yield return Done();
    }

    // 演员可能被改名/被停用 ⇒ 沿场景根扫全部 Transform（含未激活），
    // 不用 `GameObject.Find`（它看不到未激活对象），也不用泛型 `FindObjectsOfType<T>`（Codely 脚本宿主会报 CS1010）
    static GameObject FindActor(int i)
    {
        string want = "Showcase_Actor_" + i;
        GameObject best = null;
        foreach (var root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
            foreach (var tr in root.GetComponentsInChildren<Transform>(true))
            {
                var g = tr.gameObject;
                if (g.name == want) return g;
                if (best == null && g.name.StartsWith("Showcase_Actor_")) best = g;
            }
        return best;
    }

    static string RootNames()
    {
        var sb = new StringBuilder();
        foreach (var g in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
            sb.Append(g.name).Append(" | ");
        return sb.ToString();
    }

    string StatusText()
    {
        if (_fStatus == null) return "<拿不到 _status>";
        try { return "" + _fStatus.GetValue(_sc); } catch { return "<读取失败>"; }
    }

    void Measure(string nk, GameObject actor)
    {
        var rs = new List<Renderer>(actor.GetComponentsInChildren<Renderer>(true));
        var wasOn = new List<bool>();
        var wasShadow = new List<ShadowCastingMode>();
        foreach (var r in rs) { wasOn.Add(r.enabled); wasShadow.Add(r.shadowCastingMode); }

        int smrN = 0, smrNullBones = 0;
        foreach (var r in rs)
        {
            var s = r as SkinnedMeshRenderer;
            string extra;
            if (s != null)
            {
                smrN++;
                var bs = s.bones; int nn = 0;
                if (bs != null) for (int k = 0; k < bs.Length; k++) if (bs[k] != null) nn++;
                if (nn == 0) smrNullBones++;
                var m = s.sharedMesh;
                extra = "  SMR bones=" + (bs == null ? 0 : bs.Length) + " 非null=" + nn
                        + " rootBone=" + (s.rootBone != null ? s.rootBone.name : "<null>")
                        + " mesh=" + (m != null ? (m.name + " vc=" + m.vertexCount) : "<null>")
                        + " mat=" + (s.sharedMaterial != null ? s.sharedMaterial.shader.name : "<null>");
            }
            else
            {
                var mf = r.GetComponent<MeshFilter>();
                extra = "  MR mesh=" + (mf != null && mf.sharedMesh != null ? (mf.sharedMesh.name + " vc=" + mf.sharedMesh.vertexCount) : "<null>")
                        + " mat=" + (r.sharedMaterial != null ? r.sharedMaterial.shader.name : "<null>");
            }
            _sb.AppendLine("     - " + r.GetType().Name + " " + r.name + extra);
        }
        _sb.AppendLine("     SMR 合计 " + smrN + "，其中骨骼引用全 null 的 " + smrNullBones);
        var b = Bounds0(actor);
        _sb.AppendLine("     世界包围盒 center=" + b.center.ToString("F2") + " size=" + b.size.ToString("F2"));

        // 两帧都关掉演员投影 ⇒ 差集只含几何
        foreach (var r in rs) if (r != null) r.shadowCastingMode = ShadowCastingMode.Off;

        // ★ 同 tick 背靠背，中间不 yield ⇒ 相机与动画姿态完全一致
        foreach (var r in rs) if (r != null) r.enabled = true;
        var texA = Render();

        foreach (var r in rs) if (r != null) r.enabled = false;
        var texB = Render();

        for (int k = 0; k < rs.Count; k++)
        {
            if (rs[k] == null) continue;
            rs[k].enabled = wasOn[k];
            rs[k].shadowCastingMode = wasShadow[k];
        }

        int diff = 0, maxd = 0;
        int minX = int.MaxValue, maxX = -1, minY = int.MaxValue, maxY = -1;
        var pa = texA.GetPixels32(); var pb = texB.GetPixels32();
        int w = texA.width, h = texA.height;
        for (int k = 0; k < pa.Length; k++)
        {
            int d = Mathf.Max(Mathf.Abs(pa[k].r - pb[k].r), Mathf.Max(Mathf.Abs(pa[k].g - pb[k].g), Mathf.Abs(pa[k].b - pb[k].b)));
            if (d > maxd) maxd = d;
            if (d > DiffTh)
            {
                diff++;
                int x = k % w, y = k / w;
                if (x < minX) minX = x; if (x > maxX) maxX = x;
                if (y < minY) minY = y; if (y > maxY) maxY = y;
            }
        }
        _sb.AppendLine("     ▶ 纯几何像素 = " + diff + " / " + pa.Length + " = " + (100f * diff / pa.Length).ToString("F3") + "%"
                       + "   最大通道差 = " + maxd);
        if (diff > 0)
            _sb.AppendLine("       屏内范围 x[" + minX + "," + maxX + "] y[" + minY + "," + maxY + "]"
                           + "  占屏 " + (100f * (maxX - minX + 1) / w).ToString("F1") + "% x "
                           + (100f * (maxY - minY + 1) / h).ToString("F1") + "%");

        Directory.CreateDirectory(ShotDir);
        try
        {
            File.WriteAllBytes(ShotDir + "/SC_" + nk + "_A_开.png", texA.EncodeToPNG());
            File.WriteAllBytes(ShotDir + "/SC_" + nk + "_B_关.png", texB.EncodeToPNG());
        }
        catch (Exception e) { _sb.AppendLine("     写图失败：" + e.Message); }
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
        string s = _sb.ToString();
        Debug.Log(s);
        try { File.WriteAllText(ReportPath, s); } catch { }
        enabled = false;
    }
}

var g = new GameObject("dg_sc_vis");
g.AddComponent<dg_sc_vis>();
return "DG_SC_VIS_V2_STARTED";
