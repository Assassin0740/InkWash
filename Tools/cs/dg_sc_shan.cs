using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEditor;

// dg_sc_shan.cs —— 在【演示场】里补量三个 Ziyuan 敌人（墨山/墨怪/墨骨）。
//
// 这三个不在演示场面板里（面板只有 墨徒/墨偶/墨魇），所以用**演示场自己的摆法**把它们放到舞台上：
//     pos = stageCenter + Vector3.up * 0.05f，rotation = identity
// —— 与 `ActionShowcase.Play()` 里 `Instantiate(item.prefab, stageCenter + Vector3.up * 0.05f, ...)` 完全一致。
// 相机仍然用演示场相机 `Camera.main`（Showcase_Camera），**不设任何背景色**。
//
// 判据与 dg_sc_vis v2 相同：同 tick 背靠背 Camera.Render()，演员渲染器开/关两帧逐像素求差，
// 且两帧都关掉演员投影 ⇒ 差集是纯几何像素。
public class dg_sc_shan : MonoBehaviour
{
    const string ReportPath = "D:/Unity Project/InkWash/Tools/reports/dg_sc_shan.txt";
    const string ShotDir    = "D:/Unity Project/InkWash/Tools/screenshots/scene";
    const int DiffTh = 6;

    static readonly string[] Names = { "Z_Enemy_MoShan", "Z_Enemy_MoGuai", "Z_Enemy_MoGu", "Z_Enemy_MoLong" };

    readonly StringBuilder _sb = new StringBuilder();
    Camera _cam;
    Vector3 _center;

    void Start() { StartCoroutine(Run()); }

    System.Collections.IEnumerator Run()
    {
        yield return null; yield return null;

        _cam = Camera.main;
        if (_cam == null) { _sb.AppendLine("× Camera.main 为空 —— 演示场没进 Play？"); yield return Done(); }

        // 演示场舞台中心：找 ActionShowcase 所在对象的位置（BuildStage 里 stageCenter = transform.position）
        _center = Vector3.zero;
        foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
        {
            var t = a.GetType("InkWash.DebugTools.ActionShowcase");
            if (t == null) continue;
            foreach (var o in UnityEngine.Object.FindObjectsOfType(t, true))
            { var c = o as Component; if (c != null) { _center = c.transform.position; break; } }
            break;
        }

        _sb.AppendLine("===== dg_sc_shan —— 演示场内的 Ziyuan 敌人（同 tick 差分帧，无哨兵底色） =====");
        _sb.AppendLine("相机 = " + _cam.name + "  摆位 = stageCenter" + _center.ToString("F2") + " + up*0.05（演示场 Play() 的同一摆法）");
        _sb.AppendLine("判据：同 tick 背靠背 Camera.Render()，渲染器开/关两帧逐像素求差（> " + DiffTh + "），两帧均关投影");
        _sb.AppendLine();

        foreach (var nm in Names)
        {
            string path = "Assets/_Project/Prefabs/Enemies/" + nm + ".prefab";
            var pf = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (pf == null) { _sb.AppendLine("── " + nm + "：prefab 加载失败 " + path); _sb.AppendLine(); continue; }

            var inst = Instantiate(pf, _center + Vector3.up * 0.05f, Quaternion.identity);
            inst.name = "dg_shan_" + nm;
            foreach (var ag in inst.GetComponentsInChildren<UnityEngine.AI.NavMeshAgent>(true)) ag.enabled = false;
            foreach (var mb in inst.GetComponentsInChildren<MonoBehaviour>(true)) mb.enabled = false;

            yield return new WaitForSeconds(1.2f);

            _sb.AppendLine("── " + nm);
            Measure(nm, inst);
            _sb.AppendLine();

            Destroy(inst);
            yield return null;
        }

        yield return Done();
    }

    void Measure(string nm, GameObject actor)
    {
        var rs = new List<Renderer>(actor.GetComponentsInChildren<Renderer>(true));
        var wasOn = new List<bool>();
        foreach (var r in rs) wasOn.Add(r.enabled);

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
                        + " mesh=" + (m != null ? (m.name + " vc=" + m.vertexCount) : "<null>");
            }
            else
            {
                var mf = r.GetComponent<MeshFilter>();
                extra = "  MR mesh=" + (mf != null && mf.sharedMesh != null ? (mf.sharedMesh.name + " vc=" + mf.sharedMesh.vertexCount) : "<null>");
            }
            _sb.AppendLine("     - " + r.GetType().Name + " " + r.name + extra);
        }
        _sb.AppendLine("     SMR 合计 " + smrN + "，其中骨骼引用全 null 的 " + smrNullBones);

        foreach (var r in rs) if (r != null) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        foreach (var r in rs) if (r != null) r.enabled = true;
        var texA = Render();

        foreach (var r in rs) if (r != null) r.enabled = false;
        var texB = Render();

        for (int k = 0; k < rs.Count; k++) if (rs[k] != null) rs[k].enabled = wasOn[k];

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
            File.WriteAllBytes(ShotDir + "/SH_" + nm + "_A_开.png", texA.EncodeToPNG());
            File.WriteAllBytes(ShotDir + "/SH_" + nm + "_B_关.png", texB.EncodeToPNG());
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

    System.Collections.IEnumerator Done()
    {
        yield return null;
        string s = _sb.ToString();
        Debug.Log(s);
        try { File.WriteAllText(ReportPath, s); } catch { }
        enabled = false;
    }
}

var g = new GameObject("dg_sc_shan");
g.AddComponent<dg_sc_shan>();
return "DG_SC_SHAN_STARTED";
