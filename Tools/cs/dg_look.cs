using System;
using System.Reflection;
using System.Text;
using UnityEngine;

// dg_look v2：拉直前后对比渲染。
//
// ★ v1 的坑：改完骨骼**立刻** `cam.Render()` ⇒ SkinnedMeshRenderer 还没重新蒙皮，
//   两张图一模一样（包围盒也没变）。必须**等一帧**让蒙皮更新，再渲染。
public class dg_look : MonoBehaviour
{
    GameObject _dragon; Component _comp; Type _ty;
    System.Collections.IList _spine;
    readonly StringBuilder _sb = new StringBuilder();
    Quaternion[] _orig;
    GameObject _viz;
    Camera _cam;
    int _phase; int _wait;

    void Awake()
    {
        // 清掉之前探针留下的残留物（dg_body 的珠子/相机/灯都还在场里）
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        foreach (var go in scene.GetRootGameObjects())
        {
            if (go == gameObject) continue;
            string nm = go.name;
            if (nm.StartsWith("dg_") || nm.StartsWith("Showcase_Actor") ||
                nm.StartsWith("sc_") || nm.Contains("Viz"))
                Destroy(go);
        }
    }

    void Start()
    {
        var pf = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/_Project/Prefabs/Enemies/Z_Enemy_MoLong.prefab");
        if (pf == null) { Debug.LogError("dg_look: 预制体没找到"); enabled = false; return; }

        _dragon = Instantiate(pf, Vector3.zero, Quaternion.identity);
        _dragon.name = "dg_look_Dragon";
        _dragon.transform.position = Vector3.zero;
        _dragon.transform.rotation = Quaternion.identity;

        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        { var t = asm.GetType("InkWash.Enemies.EnemyDragon"); if (t != null) { _ty = t; break; } }
        _comp = _dragon.GetComponentInChildren(_ty, true);
        var mb = _comp as MonoBehaviour; if (mb != null) mb.enabled = false;

        _spine = _ty.GetField("_spine", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(_comp) as System.Collections.IList;
        _orig = new Quaternion[_spine.Count];
        for (int i = 0; i < _spine.Count; i++) _orig[i] = (_spine[i] as Transform).localRotation;

        _viz = new GameObject("dg_look_Viz");
        var lg = new GameObject("L"); lg.transform.SetParent(_viz.transform, false);
        var lt = lg.AddComponent<Light>();
        lt.type = LightType.Directional; lt.intensity = 1.15f;
        lg.transform.rotation = Quaternion.Euler(50f, 25f, 0f);

        var cg = new GameObject("C"); cg.transform.SetParent(_viz.transform, false);
        _cam = cg.AddComponent<Camera>();
        _cam.orthographic = true;
        _cam.clearFlags = CameraClearFlags.SolidColor;
        _cam.backgroundColor = new Color(0.15f, 0.16f, 0.18f);
        _cam.nearClipPlane = 0.05f; _cam.farClipPlane = 300f;
    }

    void Update()
    {
        if (_wait > 0) { _wait--; return; }
        switch (_phase)
        {
            case 0:
                _sb.AppendLine("静止姿态（原始基准）");
                _sb.AppendLine("  包围盒 size = " + DragonBounds().size.ToString("F3"));
                Shoot("look_raw");
                _phase = 1;
                break;
            case 1:
                Restore();
                Straighten(Vector3.forward);
                _sb.AppendLine("已拉直（+Z 方向），等一帧让蒙皮更新…");
                _phase = 2; _wait = 2;
                break;
            case 2:
                _sb.AppendLine("拉直后包围盒 size = " + DragonBounds().size.ToString("F3"));
                Shoot("look_straight");
                _phase = 3;
                break;
            case 3:
                _sb.AppendLine();
                _sb.AppendLine("拉直后 `_spine` 链：");
                var M = _dragon.transform;
                for (int i = 0; i < _spine.Count; i++)
                {
                    var t = _spine[i] as Transform;
                    if (i % 6 == 0 || i == _spine.Count - 1)
                        _sb.AppendLine("   [" + i + "] " + t.name + "  " + M.InverseTransformPoint(t.position).ToString("F2"));
                }
                string s = _sb.ToString();
                Debug.Log(s);
                try { System.IO.File.WriteAllText("D:/Unity Project/InkWash/Tools/reports/dg_look.txt", s); } catch { }
                _phase = 4;
                enabled = false;
                break;
        }
    }

    Bounds DragonBounds()
    {
        var M = _dragon.transform;
        bool first = true; Bounds b = new Bounds(Vector3.zero, Vector3.zero);
        foreach (var r in _dragon.GetComponentsInChildren<Renderer>(true))
        {
            var c = M.InverseTransformPoint(r.bounds.center);
            var e = r.bounds.extents;
            if (first) { b = new Bounds(c, e * 2f); first = false; }
            else b.Encapsulate(new Bounds(c, e * 2f));
        }
        return b;
    }

    void Restore() { for (int i = 0; i < _spine.Count; i++) (_spine[i] as Transform).localRotation = _orig[i]; }

    void Straighten(Vector3 localF)
    {
        var M = _dragon.transform;
        Vector3 wf = M.TransformDirection(localF.normalized);
        for (int i = 0; i + 1 < _spine.Count; i++)
        {
            var a = _spine[i] as Transform;
            var b = _spine[i + 1] as Transform;
            Vector3 d = b.position - a.position;
            if (d.sqrMagnitude < 1e-10f) continue;
            a.rotation = Quaternion.FromToRotation(d.normalized, wf) * a.rotation;
        }
    }

    void Shoot(string file)
    {
        var b = DragonBounds();
        Vector3 c = b.center;
        float ext = Mathf.Max(b.size.x, b.size.y, b.size.z) * 1.05f;
        _cam.orthographicSize = ext * 0.62f;
        string dir = "D:/Unity Project/InkWash/Tools/screenshots/dragon";
        try { System.IO.Directory.CreateDirectory(dir); } catch { }

        Vector3[] dirs = { new Vector3(0f, 1f, -0.03f), new Vector3(1f, 0.03f, 0f) };
        string[] tag = { "top", "side" };
        var rt = new RenderTexture(1000, 1000, 24);
        _cam.targetTexture = rt;
        for (int k = 0; k < dirs.Length; k++)
        {
            Vector3 d = dirs[k].normalized;
            _cam.transform.position = c + d * 60f;
            _cam.transform.rotation = Quaternion.LookRotation(-d, Vector3.up);
            _cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(1000, 1000, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0f, 0f, 1000f, 1000f), 0, 0);
            tex.Apply();
            System.IO.File.WriteAllBytes(dir + "/" + file + "_" + tag[k] + ".png", tex.EncodeToPNG());
            Destroy(tex);
        }
        RenderTexture.active = null;
        _cam.targetTexture = null;
        Destroy(rt);
    }

    void OnDestroy()
    {
        if (_viz != null) Destroy(_viz);
        if (_dragon != null) Destroy(_dragon);
    }
}

var g = new GameObject("dg_look");
g.AddComponent<dg_look>();
return "DG_LOOK_STARTED";
