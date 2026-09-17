using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEngine.Rendering;

// dg_sc_mogu.cs —— 拆开量 `Z_Enemy_MoGu`：它骨骼引用全 null（0/3）却仍能渲染，是唯一的反例。
//
// 猜测：它能画出来靠的是身上那 4 个**普通 `MeshRenderer`**（静态网格，不需要骨骼），
//       而它那 3 个 SMR 其实和 MoShan/MoGuai 一样，一个像素都没画。
// 验法（都在演示场、都用演示场相机、都不设背景色）：
//   A 档：SMR + MR 全开
//   B 档：只开 SMR（关掉全部 MeshRenderer）
//   C 档：只开 MR（关掉全部 SMR）
// 同 tick 背靠背求差 ⇒ 谁是画面的贡献者一目了然。
public class dg_sc_mogu : MonoBehaviour
{
    const string ReportPath = "D:/Unity Project/InkWash/Tools/reports/dg_sc_mogu.txt";
    const string ShotDir    = "D:/Unity Project/InkWash/Tools/screenshots/scene";
    const int DiffTh = 6;

    readonly StringBuilder _sb = new StringBuilder();
    Camera _cam;

    void Start() { StartCoroutine(Run()); }

    System.Collections.IEnumerator Run()
    {
        yield return null; yield return null;

        _cam = Camera.main;
        if (_cam == null) { _sb.AppendLine("× Camera.main 为空"); yield return Done(); }

        var pf = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Enemies/Z_Enemy_MoGu.prefab");
        if (pf == null) { _sb.AppendLine("× Z_Enemy_MoGu.prefab 加载失败"); yield return Done(); }

        var inst = Instantiate(pf, Vector3.up * 0.05f, Quaternion.identity);
        inst.name = "dg_mogu";
        foreach (var ag in inst.GetComponentsInChildren<UnityEngine.AI.NavMeshAgent>(true)) ag.enabled = false;
        foreach (var mb in inst.GetComponentsInChildren<MonoBehaviour>(true)) mb.enabled = false;
        yield return new WaitForSeconds(1.2f);

        var all = new List<Renderer>(inst.GetComponentsInChildren<Renderer>(true));
        var smrs = new List<Renderer>();
        var mrs  = new List<Renderer>();
        foreach (var r in all) { if (r is SkinnedMeshRenderer) smrs.Add(r); else mrs.Add(r); }

        _sb.AppendLine("===== dg_sc_mogu —— MoGu 反例拆解（演示场相机，同 tick 差分帧） =====");
        _sb.AppendLine("相机 = " + _cam.name);
        _sb.AppendLine("构成：SMR " + smrs.Count + " 个 / 普通 MeshRenderer " + mrs.Count + " 个");
        foreach (var r in mrs)
        {
            var mf = r.GetComponent<MeshFilter>();
            var m = mf != null ? mf.sharedMesh : null;
            _sb.AppendLine("   MR " + r.name + "  mesh=" + (m != null ? (m.name + " vc=" + m.vertexCount) : "<null>"));
        }
        _sb.AppendLine();

        foreach (var r in all) r.shadowCastingMode = ShadowCastingMode.Off;

        yield return null;
        _sb.AppendLine("档位说明：B 档 = 只留 SMR；C 档 = 只留 MeshRenderer");
        _sb.AppendLine();

        // A：全开 vs 全关
        Diff("A 全部（SMR+MR）", all, all, "A_all");
        yield return null;

        // B：只留 SMR —— 用「SMR 开 vs SMR 关」求差，MR 全程关掉
        Diff("B 只留 SMR", smrs, all, "B_smr");
        yield return null;

        // C：只留 MR —— 用「MR 开 vs MR 关」求差，SMR 全程关掉
        Diff("C 只留 MeshRenderer", mrs, all, "C_mr");
        yield return null;

        Destroy(inst);
        yield return Done();
    }

    // keepOn = 参与求差的渲染器；others = 全程强制关掉的那些
    void Diff(string tag, List<Renderer> keepOn, List<Renderer> all, string fileTag)
    {
        foreach (var r in all) if (r != null) r.enabled = false;

        foreach (var r in keepOn) if (r != null) r.enabled = true;
        var texA = Render();

        foreach (var r in keepOn) if (r != null) r.enabled = false;
        var texB = Render();

        foreach (var r in all) if (r != null) r.enabled = true;

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
        _sb.AppendLine("[" + tag + "] 纯几何像素 = " + diff + " / " + pa.Length + " = "
                       + (100f * diff / pa.Length).ToString("F3") + "%   最大通道差 = " + maxd);
        if (diff > 0)
            _sb.AppendLine("     屏内范围 x[" + minX + "," + maxX + "] y[" + minY + "," + maxY + "]"
                           + "  占屏 " + (100f * (maxX - minX + 1) / w).ToString("F1") + "% x "
                           + (100f * (maxY - minY + 1) / h).ToString("F1") + "%");

        Directory.CreateDirectory(ShotDir);
        try
        {
            File.WriteAllBytes(ShotDir + "/MG_" + fileTag + "_开.png", texA.EncodeToPNG());
            File.WriteAllBytes(ShotDir + "/MG_" + fileTag + "_关.png", texB.EncodeToPNG());
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

var g = new GameObject("dg_sc_mogu");
g.AddComponent<dg_sc_mogu>();
return "DG_SC_MOGU_STARTED";
