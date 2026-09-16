using System;
using System.Reflection;
using System.Text;
using UnityEngine;

// en_view：敌人为什么看不见 —— 一次问到底：
//   ① 敌人实例在哪（世界坐标、包围盒、是否 active）
//   ② 相机在哪、朝向、FOV
//   ③ 敌人在不在相机视锥里（WorldToViewportPoint）
//   ④ 遮挡：相机→敌人 之间有没有别的东西
public class en_view : MonoBehaviour
{
    Component _showcase; MethodInfo _mPlay;
    Camera _cam;
    readonly StringBuilder _sb = new StringBuilder();
    float _t; int _step;

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
        if (_showcase == null) { Debug.Log("en_view ✗ 没有 ActionShowcase"); enabled = false; return; }
        _mPlay = _showcase.GetType().GetMethod("PlayForCapture", BindingFlags.Public | BindingFlags.Instance);
    }

    void Update()
    {
        _t += Time.deltaTime;

        // 2.0 s 时播"墨徒 待机"（index 8）
        if (_step == 0 && _t > 2.0f)
        {
            _step = 1;
            _mPlay.Invoke(_showcase, new object[] { 8 });
            _sb.AppendLine(">>> 播条目 8（墨徒 待机）");
        }
        // 4.5 s 时取证
        if (_step == 1 && _t > 4.5f)
        {
            _step = 2;
            Snapshot();
            Debug.Log(_sb.ToString());
            try { System.IO.File.WriteAllText("D:/Unity Project/InkWash/Tools/reports/en_view.txt", _sb.ToString()); } catch { }
            enabled = false;
        }
    }

    void Snapshot()
    {
        var cam = _cam;
        _sb.AppendLine("");
        _sb.AppendLine("相机: pos=" + cam.transform.position.ToString("F2")
            + " rot=" + cam.transform.eulerAngles.ToString("F1") + " fov=" + cam.fieldOfView
            + " mask=" + Convert.ToString(cam.cullingMask, 2) + " far=" + cam.farClipPlane);

        var player = GameObject.Find("Player");
        if (player != null)
        {
            var vp = cam.WorldToViewportPoint(player.transform.position);
            _sb.AppendLine("玩家: pos=" + player.transform.position.ToString("F2")
                + " viewport=(" + vp.x.ToString("F2") + "," + vp.y.ToString("F2") + "," + vp.z.ToString("F2") + ")"
                + " active=" + player.activeInHierarchy);
        }

        // 找所有 Showcase_Actor_*
        var roots = UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects();
        bool any = false;
        foreach (var r in roots)
        {
            if (!r.name.StartsWith("Showcase_Actor")) continue;
            any = true;
            var rends = r.GetComponentsInChildren<Renderer>(true);
            Bounds b = new Bounds();
            bool has = false;
            foreach (var rd in rends)
            {
                if (rd == null) continue;
                if (!has) { b = rd.bounds; has = true; } else b.Encapsulate(rd.bounds);
            }
            var vp = cam.WorldToViewportPoint(has ? b.center : r.transform.position);
            _sb.AppendLine("演员 " + r.name + ": pos=" + r.transform.position.ToString("F2")
                + " bounds中心=" + (has ? b.center.ToString("F2") : "-")
                + " size=" + (has ? b.size.ToString("F2") : "-")
                + " 渲染器=" + rends.Length
                + " active=" + r.activeInHierarchy
                + "\n        viewport=(" + vp.x.ToString("F2") + "," + vp.y.ToString("F2") + "," + vp.z.ToString("F2") + ")"
                + "  在画面内=" + (vp.z > 0 && vp.x > 0 && vp.x < 1 && vp.y > 0 && vp.y < 1));

            // 层 / 材质
            var smrs = r.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (smrs.Length > 0)
            {
                var s0 = smrs[0];
                _sb.AppendLine("        层=" + s0.gameObject.layer + "  shader="
                    + (s0.sharedMaterial != null && s0.sharedMaterial.shader != null ? s0.sharedMaterial.shader.name : "<null>")
                    + "  enabled=" + s0.enabled + "  visible=" + s0.isVisible);
            }
        }
        if (!any) _sb.AppendLine("★ 场上没有任何 Showcase_Actor_* 对象！");
    }
}

var g = new GameObject("en_view");
g.AddComponent<en_view>();
return "EN_VIEW_STARTED";
