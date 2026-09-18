using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;

// q_long.cs —— 演示场召唤「墨龙」，跟踪 10 帧（约 6 s）：位置/朝向/围盒/内部状态
public class q_long : MonoBehaviour
{
    const string RP = "D:/Unity Project/InkWash/Tools/reports/q_long.txt";
    const string SD = "D:/Unity Project/InkWash/Tools/screenshots/enemies";
    const string KEY = "墨龙";

    readonly StringBuilder _sb = new StringBuilder();
    Component _sc; Type _ty;
    MethodInfo _mPlay, _mLabel, _mStop;
    PropertyInfo _pCount;
    Camera _cam;
    Transform _actor;

    void Start() { StartCoroutine(Run()); }

    System.Collections.IEnumerator Run()
    {
        yield return null; yield return null;

        foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
        { var t = a.GetType("InkWash.DebugTools.ActionShowcase"); if (t != null) { _ty = t; break; } }
        if (_ty == null) { _sb.AppendLine("× 找不到 ActionShowcase"); yield return Done(); }
        foreach (var o in UnityEngine.Object.FindObjectsOfType(_ty, true)) { _sc = o as Component; if (_sc != null) break; }
        if (_sc == null) { _sb.AppendLine("× 场景里没有 ActionShowcase 实例"); yield return Done(); }

        _mPlay = _ty.GetMethod("PlayForCapture", BindingFlags.Public | BindingFlags.Instance);
        _mLabel = _ty.GetMethod("ItemLabel", BindingFlags.Public | BindingFlags.Instance);
        _mStop = _ty.GetMethod("StopAutoClose", BindingFlags.Public | BindingFlags.Instance);
        _pCount = _ty.GetProperty("ItemCount", BindingFlags.Public | BindingFlags.Instance);
        _mStop.Invoke(_sc, null);

        int count = (int)_pCount.GetValue(_sc, null);
        int idx = -1;
        _sb.AppendLine("===== 演示场里含「龙」的条目 =====");
        for (int i = 0; i < count; i++)
        {
            string lb = (string)_mLabel.Invoke(_sc, new object[] { i });
            if (lb.Contains("龙")) { _sb.AppendLine("  [" + i + "] " + lb); if (idx < 0) idx = i; }
        }
        if (idx < 0) { _sb.AppendLine("× 没找到墨龙条目"); yield return Done(); }
        _sb.AppendLine("选中 [" + idx + "] " + (string)_mLabel.Invoke(_sc, new object[] { idx }));
        _sb.AppendLine();

        _mPlay.Invoke(_sc, new object[] { idx });
        Flush();
        for (int i = 0; i < 50; i++) yield return null;

        var go = GameObject.Find("Showcase_Actor_" + idx);
        if (go == null) { _sb.AppendLine("× 没找到 actor 实例"); yield return Done(); }
        _actor = go.transform;
        _sb.AppendLine("actor = " + go.name + "   pos=" + _actor.position.ToString("F2"));
        _sb.AppendLine();
        Flush();

        // ── EnemyDragon 内部状态 ──
        Type dt = null;
        try
        {
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            { var t = a.GetType("InkWash.Enemies.EnemyDragon"); if (t != null) { dt = t; break; } }
            if (dt != null)
            {
                var drg = go.GetComponent(dt);
                _sb.AppendLine("EnemyDragon = " + (drg == null ? "（没有！）" : "有"));
                if (drg != null)
                {
                    string[] flds = { "aerialLoop", "_airborne", "_currentLift", "_phase", "_perfResting",
                                      "_perfIndex", "spineRoot", "spineLinkCount", "driveStartIndex",
                                      "_modelRoot", "_orbitPhase", "orbitAngularSpeedDeg", "orbitLateralAmp" };
                    foreach (var fn in flds)
                    {
                        var f = dt.GetField(fn, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
                        if (f == null) { _sb.AppendLine("  " + fn.PadRight(22) + " = <无此字段>"); continue; }
                        object v = null;
                        try { v = f.GetValue(drg); } catch (Exception e1) { v = "<读取异常 " + e1.GetType().Name + ">"; }
                        _sb.AppendLine("  " + fn.PadRight(22) + " = " + (v == null ? "null" : v.ToString()));
                    }
                    var sr = dt.GetField("spineRoot", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
                    if (sr != null && sr.GetValue(drg) is Transform)
                    {
                        var t0 = (Transform)sr.GetValue(drg);
                        string p0 = t0.name; int depth = 0;
                        var c0 = t0;
                        while (c0.childCount > 0 && depth < 60) { c0 = c0.GetChild(0); depth++; }
                        _sb.AppendLine("   spineRoot 链: " + p0 + " → " + c0.name + "   实测深度=" + depth);
                    }
                    else _sb.AppendLine("   spineRoot = null（未定位到骨链！）");
                }
            }
            else _sb.AppendLine("× 找不到 EnemyDragon 类型");
        }
        catch (Exception ex) { _sb.AppendLine("× 内部状态段异常: " + ex.GetType().Name + " " + ex.Message); }
        _sb.AppendLine();
        Flush();

        // ── NavMesh 检查 ──
        try
        {
            var hit = new UnityEngine.AI.NavMeshHit();
            bool hasNM = UnityEngine.AI.NavMesh.SamplePosition(_actor.position, out hit, 8f, UnityEngine.AI.NavMesh.AllAreas);
            _sb.AppendLine("NavMesh.SamplePosition = " + hasNM
                + (hasNM ? "  nearest=" + hit.position.ToString("F2") + " dist=" + hit.distance.ToString("F2")
                         : "  ⇒ 该处完全没有 NavMesh（NavMeshAgent 建不起来）"));
            var ag = go.GetComponent<UnityEngine.AI.NavMeshAgent>();
            _sb.AppendLine("NavMeshAgent: " + (ag == null ? "无" : ("enabled=" + ag.enabled
                + " isOnNavMesh=" + ag.isOnNavMesh + " speed=" + ag.speed
                + " hasPath=" + ag.hasPath + " vel=" + ag.velocity.ToString("F2"))));
        }
        catch (Exception ex2) { _sb.AppendLine("× NavMesh 段异常: " + ex2.GetType().Name + " " + ex2.Message); }
        _sb.AppendLine();
        Flush();

        _cam = MakeCam();

        // ── 轨迹采样 ──
        int NF = 10;
        _sb.AppendLine("===== 轨迹（10 帧，每帧 ~0.6 s）=====");
        _sb.AppendLine("帧  世界位置                              Y     朝前                  围盒 size              位移");
        var prev = _actor.position;
        for (int f = 0; f < NF; f++)
        {
            try
            {
                var rds = go.GetComponentsInChildren<Renderer>(true);
                var b = rds[0].bounds;
                for (int i = 1; i < rds.Length; i++) b.Encapsulate(rds[i].bounds);
                float moved = Vector3.Distance(prev, _actor.position);
                prev = _actor.position;
                _sb.AppendLine(f.ToString("D2") + "  " + _actor.position.ToString("F2").PadRight(36)
                    + " " + _actor.position.y.ToString("F2").PadRight(6)
                    + " " + _actor.forward.ToString("F2").PadRight(22)
                    + " " + b.size.ToString("F2").PadRight(24)
                    + " " + moved.ToString("F3"));
                Snap(b, "L" + f);
            }
            catch (Exception ex) { _sb.AppendLine("  × 帧 " + f + " 异常: " + ex.GetType().Name + " " + ex.Message); }
            Flush();
            for (int k = 0; k < 30; k++) yield return null;
        }

        // ── 骨骼链弯曲量（末帧）──
        _sb.AppendLine();
        _sb.AppendLine("===== 骨链实际弯曲（相邻节夹角）=====");
        if (dt != null)
        {
            var drg2 = go.GetComponent(dt);
            var sr = dt.GetField("spineRoot", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
            if (sr != null && sr.GetValue(drg2) is Transform)
            {
                var chain = new List<Transform>();
                var c = (Transform)sr.GetValue(drg2);
                while (c != null && chain.Count < 40) { chain.Add(c); c = c.childCount > 0 ? c.GetChild(0) : null; }
                _sb.AppendLine("链长 = " + chain.Count);
                for (int i = 0; i + 1 < chain.Count; i++)
                {
                    float a = Vector3.Angle(chain[i].up, chain[i + 1].up);
                    _sb.AppendLine("  [" + i.ToString("D2") + "] " + chain[i].name.PadRight(16) + " → " + chain[i + 1].name.PadRight(16)
                        + "  夹角=" + a.ToString("F2") + "°");
                }
            }
        }

        UnityEngine.Object.Destroy(_cam.gameObject);
        yield return Done();
    }

    Camera MakeCam()
    {
        var g = new GameObject("PROBE_CAM_LONG");
        var c = g.AddComponent<Camera>();
        c.clearFlags = CameraClearFlags.SolidColor;
        c.backgroundColor = new Color(0.86f, 0.88f, 0.91f, 1f);
        c.fieldOfView = 45f;
        c.nearClipPlane = 0.05f;
        c.farClipPlane = 5000f;
        c.enabled = false;
        return c;
    }

    void Snap(Bounds b, string tag)
    {
        float rad = b.extents.magnitude;
        if (rad < 0.5f) rad = 0.5f;
        float dist = rad / Mathf.Sin(_cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.05f;
        _cam.transform.position = b.center + new Vector3(0.55f, 0.40f, 0.72f).normalized * dist;
        _cam.transform.LookAt(b.center);

        int W = 900, H = 620;
        var rt = new RenderTexture(W, H, 24);
        _cam.targetTexture = rt;
        _cam.Render();
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
        tex.Apply();
        File.WriteAllBytes(SD + "/QL_" + tag + ".png", tex.EncodeToPNG());
        RenderTexture.active = prev;
        UnityEngine.Object.Destroy(tex);
        UnityEngine.Object.Destroy(rt);
        _cam.targetTexture = null;
    }

    System.Collections.IEnumerator Done()
    {
        Flush();
        Debug.Log("[q_long] 报告已写入 " + RP);
        yield return null;
    }

    void Flush() { try { File.WriteAllText(RP, _sb.ToString()); } catch { } }
}

var __hostL = new GameObject("q_long");
__hostL.AddComponent<q_long>();
return "Q_LONG_STARTED";
