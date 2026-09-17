using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEngine.Rendering;

// dg_sc_verify.cs v2 —— 在【演示场】里，用【演示场相机】逐个体检 Ziyuan 敌人到底画不画得出来。
//
// 这一版把上一版的四个尺子问题全修了：
//   ① 背景：不用任何哨兵底色（洋红曾把用户误导成"材质丢了"）——画面就是正常渲染结果。
//   ② 帧：同 tick **背靠背** `Camera.Render()`，中间不推进帧 ⇒ 相机与动画姿态完全一致；
//      两帧都关掉被测对象的投影 ⇒ 差集是**纯几何像素**。
//   ③ 取景：测之前把演示场相机**对准被测对象**，并报 `TestPlanesAABB` 结果
//      —— 0 像素不再可能是"根本没入镜"；相机位置也一起报出来（上一版方向算反，相机跑到地面以下被地板挡住）。
//   ④ **不搞一刀切禁 MonoBehaviour**：只禁 `NavMeshAgent`，把 `InkMaterialSwap` 留着
//      —— 一刀切会让水墨材质换不上，对象渲染成品红（= Unity 缺材质色），反而制造假象。
//      另外先把演示场自己的残留演员清掉、并停掉 ActionShowcase 的相机接管，保证画面干净。
public class dg_sc_verify : MonoBehaviour
{
    const string ReportPath = "D:/Unity Project/InkWash/Tools/reports/dg_sc_verify.txt";
    const string ShotDir    = "D:/Unity Project/InkWash/Tools/screenshots/scene";
    const int DiffTh = 6;

    static readonly string[] Names = { "Z_Enemy_MoShan", "Z_Enemy_MoGuai", "Z_Enemy_MoGu", "Z_Enemy_MoLong" };

    readonly StringBuilder _sb = new StringBuilder();
    Camera _cam;

    void Start() { StartCoroutine(Run()); }

    System.Collections.IEnumerator Run()
    {
        yield return null; yield return null;

        _cam = Camera.main;
        if (_cam == null) { _sb.AppendLine("× Camera.main 为空 —— 演示场没进 Play？"); yield return Done(); }

        CleanStage();

        _sb.AppendLine("===== dg_sc_verify v2 —— 演示场内的敌人可见性（正常相机 / 同 tick 差分帧 / 已对准） =====");
        _sb.AppendLine("相机 = " + _cam.name + "　台子·灯光·地面 = ActionShowcase.BuildStage()（游戏同一套）");
        _sb.AppendLine("判据：相机对准被测对象且 TestPlanesAABB 命中；渲染器开/关两帧逐像素差 > " + DiffTh + " 记为几何像素");
        _sb.AppendLine("只禁 NavMeshAgent，保留 InkMaterialSwap（否则水墨材质换不上，会渲成品红冒充缺材质）");
        _sb.AppendLine();

        foreach (var nm in Names)
        {
            var pf = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Enemies/" + nm + ".prefab");
            if (pf == null) { _sb.AppendLine("── " + nm + "：prefab 加载失败"); _sb.AppendLine(); continue; }

            var inst = Instantiate(pf, Vector3.up * 0.05f, Quaternion.identity);
            inst.name = "dgv_" + nm;
            foreach (var ag in inst.GetComponentsInChildren<UnityEngine.AI.NavMeshAgent>(true)) ag.enabled = false;
            yield return new WaitForSeconds(1.0f);

            _sb.AppendLine("── " + nm);
            var all = new List<Renderer>(inst.GetComponentsInChildren<Renderer>(true));
            var smrs = new List<Renderer>();
            var mrs  = new List<Renderer>();
            foreach (var r in all) { if (r is SkinnedMeshRenderer) smrs.Add(r); else mrs.Add(r); }

            int smrNullBones = 0;
            foreach (var r in smrs)
            {
                var s = (SkinnedMeshRenderer)r;
                var bs = s.bones; int nn = 0;
                if (bs != null) for (int k = 0; k < bs.Length; k++) if (bs[k] != null) nn++;
                if (nn == 0) smrNullBones++;
                _sb.AppendLine("     SMR " + r.name + "  bones=" + (bs == null ? 0 : bs.Length) + " 非null=" + nn
                               + " rootBone=" + (s.rootBone != null ? s.rootBone.name : "<null>")
                               + " mesh=" + (s.sharedMesh != null ? (s.sharedMesh.name + " vc=" + s.sharedMesh.vertexCount) : "<null>")
                               + " 材质=" + ShaderOf(r));
            }
            foreach (var r in mrs)
            {
                var mf = r.GetComponent<MeshFilter>();
                var m = mf != null ? mf.sharedMesh : null;
                _sb.AppendLine("     MR  " + r.name + "  mesh=" + (m != null ? (m.name + " vc=" + m.vertexCount) : "<null>")
                               + " 材质=" + ShaderOf(r));
            }
            _sb.AppendLine("     构成：SMR " + smrs.Count + "（骨骼全 null 的 " + smrNullBones + "）／ 普通 MeshRenderer " + mrs.Count);

            Aim(inst);

            Diff("全部（SMR+MR）", all, all, nm, "all");
            if (mrs.Count > 0)
            {
                Diff("只留 SMR", smrs, all, nm, "smr");
                Diff("只留 MeshRenderer", mrs, all, nm, "mr");
            }
            _sb.AppendLine();

            Destroy(inst);
            yield return null;
        }

        yield return Done();
    }

    // 清掉演示场自己的残留演员，并停掉它对相机的接管（相机对象本身留着）
    void CleanStage()
    {
        int killed = 0;
        foreach (var root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
        {
            if (root == gameObject) continue;   // ★ 别把自己删了（探针自己就叫 dg_sc_verify）
            if (root.name.StartsWith("Showcase_Actor_") || root.name.StartsWith("dgv_") || root.name.StartsWith("dg_"))
            { Destroy(root); killed++; }
        }
        foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
        {
            var t = a.GetType("InkWash.DebugTools.ActionShowcase");
            if (t == null) continue;
            foreach (var o in UnityEngine.Object.FindObjectsOfType(t, true))
            { var c = o as MonoBehaviour; if (c != null) c.enabled = false; }
            break;
        }
        _sb.AppendLine("清场：销毁残留演员 " + killed + " 个；ActionShowcase 已停用（相机不再被它接管）");
    }

    static string ShaderOf(Renderer r)
    {
        var m = r.sharedMaterial;
        if (m == null) return "<无材质>";
        return m.shader != null ? m.shader.name : "<shader null>";
    }

    void Aim(GameObject go)
    {
        var b = Bounds0(go);
        Vector3 look = b.center;
        // dir 由对象指向相机：+y 抬高、−z 站到对象前面
        Vector3 dir = new Vector3(0.35f, 0.32f, -1f).normalized;
        float dist = Mathf.Clamp(b.extents.magnitude * 2.6f, 3.5f, 40f);
        _cam.transform.position = look + dir * dist;
        _cam.transform.LookAt(look);
        _sb.AppendLine("     对准：对象盒 center=" + look.ToString("F2") + " size=" + b.size.ToString("F2")
                       + "　相机 pos=" + _cam.transform.position.ToString("F2")
                       + " dist=" + dist.ToString("F2") + "　相机在地面之上=" + (_cam.transform.position.y > 0f));
    }

    void Diff(string tag, List<Renderer> subject, List<Renderer> all, string nm, string fileTag)
    {
        foreach (var r in all) if (r != null) r.shadowCastingMode = ShadowCastingMode.Off;
        foreach (var r in all) if (r != null) r.enabled = false;

        bool inView = false;
        if (subject.Count > 0 && subject[0] != null)
        {
            var host = subject[0].transform;
            while (host.parent != null) host = host.parent;
            var bb = Bounds0(host.gameObject);
            var planes = GeometryUtility.CalculateFrustumPlanes(_cam);
            inView = GeometryUtility.TestPlanesAABB(planes, bb);
        }

        // ★ 同 tick 背靠背：中间不 yield
        foreach (var r in subject) if (r != null) r.enabled = true;
        var texA = Render();

        foreach (var r in subject) if (r != null) r.enabled = false;
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
        _sb.AppendLine("     [" + tag + "] 在视锥内=" + inView
                       + "　纯几何像素 = " + diff + " / " + pa.Length + " = " + (100f * diff / pa.Length).ToString("F3") + "%"
                       + "　最大通道差 = " + maxd);
        if (diff > 0)
            _sb.AppendLine("           屏内 x[" + minX + "," + maxX + "] y[" + minY + "," + maxY + "]"
                           + " 占屏 " + (100f * (maxX - minX + 1) / w).ToString("F1") + "% x "
                           + (100f * (maxY - minY + 1) / h).ToString("F1") + "%　md5 相同=" + SameBytes(texA, texB));

        Directory.CreateDirectory(ShotDir);
        try
        {
            File.WriteAllBytes(ShotDir + "/SV_" + nm + "_" + fileTag + "_开.png", texA.EncodeToPNG());
            File.WriteAllBytes(ShotDir + "/SV_" + nm + "_" + fileTag + "_关.png", texB.EncodeToPNG());
        }
        catch (Exception e) { _sb.AppendLine("     写图失败：" + e.Message); }
        Destroy(texA); Destroy(texB);
    }

    static bool SameBytes(Texture2D a, Texture2D b)
    {
        var pa = a.GetPixels32(); var pb = b.GetPixels32();
        if (pa.Length != pb.Length) return false;
        for (int i = 0; i < pa.Length; i++)
            if (pa[i].r != pb[i].r || pa[i].g != pb[i].g || pa[i].b != pb[i].b) return false;
        return true;
    }

    static Bounds Bounds0(GameObject go)
    {
        if (go == null) return new Bounds();
        var rs = go.GetComponentsInChildren<Renderer>(true);
        bool has = false; Bounds b = new Bounds();
        foreach (var r in rs)
        {
            if (r == null) continue;
            if (!has) { b = r.bounds; has = true; } else b.Encapsulate(r.bounds);
        }
        return b;
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

var g = new GameObject("dg_sc_verify");
g.AddComponent<dg_sc_verify>();
return "DG_SC_VERIFY_V2_STARTED";
