using System;
using System.Reflection;
using System.Text;
using UnityEngine;

// en_pix：把相机**手动摆到敌人正前方**出图，绕开演示场相机。
//   若这样能看到 ⇒ 是取景/遮挡问题；
//   若还是看不到 ⇒ 是渲染问题（材质/剔除/后处理）。
public class en_pix : MonoBehaviour
{
    Component _showcase; MethodInfo _mPlay;
    Camera _cam;
    readonly StringBuilder _sb = new StringBuilder();
    float _t; int _step;
    GameObject _target;

    void Start()
    {
        _cam = Camera.main;
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            var t = asm.GetType("InkWash.DebugTools.ActionShowcase");
            if (t == null) continue;
            foreach (var c in FindObjectsOfType(t)) { _showcase = c as Component; break; }
            if (_showcase != null) break;
        }
        if (_showcase == null) { Debug.Log("en_pix ✗"); enabled = false; return; }
        _mPlay = _showcase.GetType().GetMethod("PlayForCapture", BindingFlags.Public | BindingFlags.Instance);
    }

    void Update()
    {
        _t += Time.deltaTime;
        if (_step == 0 && _t > 2.0f)
        {
            _step = 1;
            _mPlay.Invoke(_showcase, new object[] { 8 });
        }
        if (_step == 1 && _t > 5.0f)
        {
            _step = 2;
            var roots = UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects();
            foreach (var r in roots) if (r.name.StartsWith("Showcase_Actor")) { _target = r; break; }
            if (_target == null) { Debug.Log("en_pix ✗ 没有演员"); enabled = false; return; }
            StartCoroutine(Shoot());
        }
    }

    System.Collections.IEnumerator Shoot()
    {
        // 把相机摆在敌人正前方 4 m、高 1.5 m，正对它
        var b = Bounds0(_target);
        Vector3 look = b.center;
        _cam.transform.position = look + new Vector3(0f, 0.6f, -4.5f);
        _cam.transform.LookAt(look);
        yield return new WaitForEndOfFrame();
        yield return new WaitForEndOfFrame();

        int w = Screen.width, h = Screen.height;
        var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
        var prev = _cam.targetTexture;
        _cam.targetTexture = rt; _cam.Render(); _cam.targetTexture = prev;
        RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
        RenderTexture.active = null;
        var png = tex.EncodeToPNG();
        System.IO.File.WriteAllBytes("D:/Unity Project/InkWash/Tools/screenshots/en_direct.png", png);
        Destroy(tex); Destroy(rt);

        // 同时报告像素统计
        _sb.AppendLine("直拍 " + _target.name + "  bounds=" + b.center.ToString("F2") + " size=" + b.size.ToString("F2"));

        // 检查材质
        foreach (var rd in _target.GetComponentsInChildren<Renderer>(true))
        {
            if (rd == null) continue;
            var m = rd.sharedMaterial;
            if (m == null) { _sb.AppendLine("  " + rd.name + " <无材质>"); continue; }
            _sb.AppendLine("  " + rd.name + " shader=" + (m.shader != null ? m.shader.name : "<null>")
                + " renderQueue=" + m.renderQueue
                + " 颜色=" + (m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor").ToString("F3") : "-")
                + " zwrite=" + (m.HasProperty("_ZWrite") ? m.GetFloat("_ZWrite").ToString("F0") : "-"));
        }
        Debug.Log(_sb.ToString());
        try { System.IO.File.WriteAllText("D:/Unity Project/InkWash/Tools/reports/en_pix.txt", _sb.ToString()); } catch { }
        enabled = false;
    }

    static Bounds Bounds0(GameObject go)
    {
        var rs = go.GetComponentsInChildren<Renderer>(true);
        bool has = false; Bounds b = new Bounds();
        foreach (var r in rs) { if (r == null) continue; if (!has) { b = r.bounds; has = true; } else b.Encapsulate(r.bounds); }
        return b;
    }
}

var g = new GameObject("en_pix");
g.AddComponent<en_pix>();
return "EN_PIX_STARTED";
