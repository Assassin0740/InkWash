using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;

// q_long3.cs —— 可靠判据：骨链每节相对【基准】的真实偏移角 + 头尾相对位移
//   为什么不用 localRotation 的绕角：驱动写的是【世界旋转】= rootRot·R(yaw,pitch)·_baseRel[i]，
//   而行波下相邻节几乎同相（步进仅 15°）⇒ 局部相对旋转几乎抵消，绕角读不出摆动。
public class q_long3 : MonoBehaviour
{
    const string RP = "D:/Unity Project/InkWash/Tools/reports/q_long3.txt";
    const string SD = "D:/Unity Project/InkWash/Tools/screenshots/enemies";
    const string KEY = "墨龙";

    readonly StringBuilder _sb = new StringBuilder();
    Component _sc; Type _ty, _dt;
    MethodInfo _mPlay, _mLabel, _mStop;
    PropertyInfo _pCount;
    Camera _cam;
    GameObject _go;
    Transform _actor, _vroot;
    IList _spine, _baseRel;

    static readonly BindingFlags BF = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;

    void Start() { StartCoroutine(Run()); }

    System.Collections.IEnumerator Run()
    {
        yield return null; yield return null;

        foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (_ty == null) { var t1 = a.GetType("InkWash.DebugTools.ActionShowcase"); if (t1 != null) _ty = t1; }
            if (_dt == null) { var t2 = a.GetType("InkWash.Enemies.EnemyDragon"); if (t2 != null) _dt = t2; }
        }
        if (_ty == null || _dt == null) { _sb.AppendLine("× 类型缺失"); yield return Done(); }

        foreach (var o in UnityEngine.Object.FindObjectsOfType(_ty, true)) { _sc = o as Component; if (_sc != null) break; }
        _mPlay = _ty.GetMethod("PlayForCapture", BF);
        _mLabel = _ty.GetMethod("ItemLabel", BF);
        _mStop = _ty.GetMethod("StopAutoClose", BF);
        _pCount = _ty.GetProperty("ItemCount", BF);

        int count = (int)_pCount.GetValue(_sc, null);
        int idx = -1;
        for (int i = 0; i < count; i++)
        {
            string lb = (string)_mLabel.Invoke(_sc, new object[] { i });
            if (lb.Contains(KEY) && lb.Contains("盘旋")) { idx = i; break; }
        }
        if (idx < 0) { _sb.AppendLine("× 没找到盘旋条目"); yield return Done(); }
        _mStop.Invoke(_sc, null);
        _mPlay.Invoke(_sc, new object[] { idx });
        Flush();
        for (int i = 0; i < 60; i++) yield return null;

        _go = GameObject.Find("Showcase_Actor_" + idx);
        if (_go == null) { _sb.AppendLine("× 没找到 actor"); yield return Done(); }
        _actor = _go.transform;
        var drg = _go.GetComponent(_dt);

        _vroot = _dt.GetField("_modelRoot", BF).GetValue(drg) as Transform;
        _spine = _dt.GetField("_spine", BF).GetValue(drg) as IList;
        _baseRel = _dt.GetField("_baseRel", BF).GetValue(drg) as IList;

        _sb.AppendLine("_spine.Count    = " + (_spine == null ? -1 : _spine.Count));
        _sb.AppendLine("_baseRel.Count  = " + (_baseRel == null ? -1 : _baseRel.Count));
        _sb.AppendLine("_modelRoot      = " + (_vroot == null ? "NULL" : _vroot.name + "  localPos=" + _vroot.localPosition.ToString("F3")));
        _sb.AppendLine();

        _cam = MakeCam();

        int NF = 8;
        int NS = _spine == null ? 0 : _spine.Count;
        var offsetHist = new List<float[]>();   // 每节：相对基准的实际偏移角
        var headRel = new List<Vector3>();      // 头相对尾的世界偏移
        var bodyLen = new List<float>();

        for (int f = 0; f < NF; f++)
        {
            try
            {
                var off = new float[NS];
                Quaternion rootRot = _vroot != null ? _vroot.rotation : _actor.rotation;
                for (int i = 0; i < NS; i++)
                {
                    var t = _spine[i] as Transform;
                    var rel = (Quaternion)_baseRel[i];
                    if (t == null) { off[i] = 0f; continue; }
                    off[i] = Quaternion.Angle(t.rotation, rootRot * rel);
                }
                offsetHist.Add(off);

                if (NS > 0)
                {
                    var t0 = _spine[0] as Transform;
                    var tN = _spine[NS - 1] as Transform;
                    if (t0 != null && tN != null)
                    {
                        headRel.Add(tN.position - t0.position);
                        bodyLen.Add(Vector3.Distance(t0.position, tN.position));
                    }
                }
                _sb.AppendLine("帧 " + f + "  yaw实际偏移(节0/节6/节12/节23) = "
                    + (NS > 23 ? off[0].ToString("F1") + " / " + off[6].ToString("F1") + " / " + off[12].ToString("F1") + " / " + off[23].ToString("F1") : "-")
                    + "    |尾→头|=" + (bodyLen.Count > f ? bodyLen[f].ToString("F2") : "-"));
                Snap("N" + f);
            }
            catch (Exception ex) { _sb.AppendLine("  × 帧 " + f + " 异常 " + ex.Message); }
            Flush();
            for (int k = 0; k < 30; k++) yield return null;
        }

        _sb.AppendLine();
        _sb.AppendLine("===== 每节【实际偏移角】的帧间极差（度）=====");
        if (offsetHist.Count >= 2 && NS > 0)
        {
            int moving = 0;
            for (int i = 0; i < NS; i++)
            {
                float mn = float.MaxValue, mx = float.MinValue;
                for (int f = 0; f < offsetHist.Count; f++)
                { float v = offsetHist[f][i]; if (v < mn) mn = v; if (v > mx) mx = v; }
                float rng = mx - mn;
                if (rng > 3f) moving++;
                var t = _spine[i] as Transform;
                _sb.AppendLine("  [" + i.ToString("D2") + "] " + (t == null ? "NULL" : t.name).PadRight(14)
                    + " 最小=" + mn.ToString("F2").PadRight(8) + " 最大=" + mx.ToString("F2").PadRight(8)
                    + " 极差=" + rng.ToString("F2").PadRight(8) + (rng > 3f ? "✓" : "⚠"));
            }
            _sb.AppendLine("  摆动显著的节数 = " + moving + " / " + NS);
        }

        _sb.AppendLine();
        _sb.AppendLine("===== 头相对尾的偏移向量（世界）=====");
        for (int f = 0; f < headRel.Count; f++)
            _sb.AppendLine("  帧 " + f + "  Δ=" + headRel[f].ToString("F2")
                + "   长度=" + bodyLen[f].ToString("F2")
                + "   XZ摆幅=" + new Vector2(headRel[f].x, headRel[f].z).magnitude.ToString("F2"));

        UnityEngine.Object.Destroy(_cam.gameObject);
        yield return Done();
    }

    Camera MakeCam()
    {
        var g = new GameObject("PROBE_CAM_L3");
        var c = g.AddComponent<Camera>();
        c.clearFlags = CameraClearFlags.SolidColor;
        c.backgroundColor = new Color(0.86f, 0.88f, 0.91f, 1f);
        c.fieldOfView = 50f;
        c.nearClipPlane = 0.05f;
        c.farClipPlane = 5000f;
        c.enabled = false;
        return c;
    }

    // 俯视：从正上方看，蛇形最明显
    void Snap(string tag)
    {
        var rds = _go.GetComponentsInChildren<Renderer>(true);
        var b = rds[0].bounds;
        for (int i = 1; i < rds.Length; i++) b.Encapsulate(rds[i].bounds);
        float rad = b.extents.magnitude;
        if (rad < 0.5f) rad = 0.5f;
        float dist = rad / Mathf.Sin(_cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.02f;
        _cam.transform.position = b.center + (Vector3.up * 0.95f + _actor.forward * 0.25f).normalized * dist;
        _cam.transform.LookAt(b.center);
        _cam.transform.up = Vector3.forward;

        int W = 960, H = 620;
        var rt = new RenderTexture(W, H, 24);
        _cam.targetTexture = rt;
        _cam.Render();
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
        tex.Apply();
        File.WriteAllBytes(SD + "/QN_" + tag + ".png", tex.EncodeToPNG());
        RenderTexture.active = prev;
        UnityEngine.Object.Destroy(tex);
        UnityEngine.Object.Destroy(rt);
        _cam.targetTexture = null;
    }

    System.Collections.IEnumerator Done() { Flush(); Debug.Log("[q_long3] 写入 " + RP); yield return null; }
    void Flush() { try { File.WriteAllText(RP, _sb.ToString()); } catch { } }
}

var __hostL3 = new GameObject("q_long3");
__hostL3.AddComponent<q_long3>();
return "Q_LONG3_STARTED";
