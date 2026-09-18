using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;

// q_long2.cs —— 墨龙姿态精准诊断：骨链是否真的在驱动物理、模型容器是否真的抬起
public class q_long2 : MonoBehaviour
{
    const string RP = "D:/Unity Project/InkWash/Tools/reports/q_long2.txt";
    const string SD = "D:/Unity Project/InkWash/Tools/screenshots/enemies";
    const string KEY = "墨龙";

    readonly StringBuilder _sb = new StringBuilder();
    Component _sc; Type _ty, _dt;
    MethodInfo _mPlay, _mLabel, _mStop;
    PropertyInfo _pCount;
    Camera _cam;
    GameObject _go;
    Transform _actor, _vroot;

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
        if (_sc == null) { _sb.AppendLine("× 无 ActionShowcase"); yield return Done(); }

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
        for (int i = 0; i < 50; i++) yield return null;

        _go = GameObject.Find("Showcase_Actor_" + idx);
        if (_go == null) { _sb.AppendLine("× 没找到 actor"); yield return Done(); }
        _actor = _go.transform;

        var drg = _go.GetComponent(_dt);
        if (drg == null) { _sb.AppendLine("× 没有 EnemyDragon 组件"); yield return Done(); }

        _sb.AppendLine("===== EnemyDragon 运行态 =====");
        DumpF(drg, "spineLinkCount");
        DumpF(drg, "driveStartIndex");
        DumpF(drg, "_spineLinksFound");
        DumpF(drg, "_airborne");
        DumpF(drg, "_currentLift");
        DumpF(drg, "_phase");
        DumpF(drg, "hoverAmplitudeDeg");
        DumpF(drg, "hoverFrequency");
        DumpF(drg, "hoverPhaseStepDeg");
        DumpF(drg, "bodySwayAmp");
        DumpF(drg, "limbSwingDeg");
        DumpF(drg, "bodyLift");
        DumpF(drg, "_perfResting");
        DumpF(drg, "aerialLoop");
        _sb.AppendLine();

        // 模型容器
        var fMr = _dt.GetField("_modelRoot", BF);
        _vroot = fMr == null ? null : fMr.GetValue(drg) as Transform;
        if (_vroot != null)
            _sb.AppendLine("_modelRoot = " + _vroot.name + "   localPosition = " + _vroot.localPosition.ToString("F4")
                + "   worldY = " + _vroot.position.y.ToString("F3"));
        else _sb.AppendLine("_modelRoot = NULL（抬升无处施加！）");
        var fBase = _dt.GetField("_modelRootBaseLocalPos", BF);
        if (fBase != null) _sb.AppendLine("_modelRootBaseLocalPos = " + fBase.GetValue(drg));
        _sb.AppendLine();

        // 骨链
        var fSpine = _dt.GetField("_spine", BF);
        var spine = fSpine == null ? null : fSpine.GetValue(drg) as IList;
        _sb.AppendLine("_spine.Count = " + (spine == null ? -1 : spine.Count));
        if (spine != null && spine.Count > 0)
        {
            _sb.AppendLine("  节名: ");
            for (int i = 0; i < spine.Count; i++)
            {
                var t = spine[i] as Transform;
                _sb.Append("    [" + i.ToString("D2") + "] " + (t == null ? "NULL" : t.name));
                if (i % 4 == 3) _sb.AppendLine();
            }
            _sb.AppendLine();
        }
        _sb.AppendLine();
        Flush();

        _cam = MakeCam();

        // ── 侧视角多帧 + 骨链采样 ──
        int NF = 8;
        _sb.AppendLine("===== 姿态采样（8 帧 × ~0.55 s，侧视角）=====");
        var rotHist = new List<float[]>();
        for (int f = 0; f < NF; f++)
        {
            try
            {
                var rots = new float[spine == null ? 0 : spine.Count];
                if (spine != null)
                    for (int i = 0; i < spine.Count; i++)
                    {
                        var t = spine[i] as Transform;
                        rots[i] = t == null ? 0f : Quaternion.Angle(Quaternion.identity, t.localRotation);
                    }
                rotHist.Add(rots);
                _sb.AppendLine("帧 " + f + "  actorPos=" + _actor.position.ToString("F2")
                    + "  VisualWorldY=" + (_vroot == null ? 0 : _vroot.position.y).ToString("F3"));
                Snap("M" + f);
            }
            catch (Exception ex) { _sb.AppendLine("  × 帧 " + f + " 异常 " + ex.Message); }
            Flush();
            for (int k = 0; k < 33; k++) yield return null;
        }

        // ── 骨链旋转的帧间变化 ──
        if (spine != null && spine.Count > 0 && rotHist.Count >= 2)
        {
            _sb.AppendLine();
            _sb.AppendLine("===== 各节 localRotation 绕角（度）的帧间极差 =====");
            _sb.AppendLine("  节             首帧    末帧    极差      判定");
            int moving = 0;
            for (int i = 0; i < spine.Count; i++)
            {
                float mn = float.MaxValue, mx = float.MinValue;
                for (int f = 0; f < rotHist.Count; f++)
                {
                    float v = rotHist[f][i];
                    if (v < mn) mn = v;
                    if (v > mx) mx = v;
                }
                float rng = mx - mn;
                if (rng > 0.5f) moving++;
                if (i < 26)
                {
                    var t = spine[i] as Transform;
                    _sb.AppendLine("  [" + i.ToString("D2") + "] " + (t == null ? "NULL" : t.name).PadRight(14)
                        + " " + rotHist[0][i].ToString("F2").PadRight(8)
                        + " " + rotHist[rotHist.Count - 1][i].ToString("F2").PadRight(8)
                        + " " + rng.ToString("F2").PadRight(9)
                        + (rng > 0.5f ? "✓ 在动" : "⚠ 静止"));
                }
            }
            _sb.AppendLine("  结论：在动的节数 = " + moving + " / " + spine.Count);
        }

        UnityEngine.Object.Destroy(_cam.gameObject);
        yield return Done();
    }

    void DumpF(object comp, string name)
    {
        var f = _dt.GetField(name, BF);
        if (f == null) { _sb.AppendLine("  " + name.PadRight(26) + " = <无此字段>"); return; }
        object v;
        try { v = f.GetValue(comp); } catch (Exception e) { v = "<异常 " + e.GetType().Name + ">"; }
        _sb.AppendLine("  " + name.PadRight(26) + " = " + (v == null ? "null" : v.ToString()));
    }

    Camera MakeCam()
    {
        var g = new GameObject("PROBE_CAM_L2");
        var c = g.AddComponent<Camera>();
        c.clearFlags = CameraClearFlags.SolidColor;
        c.backgroundColor = new Color(0.86f, 0.88f, 0.91f, 1f);
        c.fieldOfView = 45f;
        c.nearClipPlane = 0.05f;
        c.farClipPlane = 5000f;
        c.enabled = false;
        return c;
    }

    // 侧视角：从龙的右侧水平看，能看出蛇形
    void Snap(string tag)
    {
        var rds = _go.GetComponentsInChildren<Renderer>(true);
        var b = rds[0].bounds;
        for (int i = 1; i < rds.Length; i++) b.Encapsulate(rds[i].bounds);
        float rad = b.extents.magnitude;
        if (rad < 0.5f) rad = 0.5f;
        float dist = rad / Mathf.Sin(_cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.02f;
        Vector3 side = _actor.right;
        _cam.transform.position = b.center + (side * 0.98f + Vector3.up * 0.10f).normalized * dist;
        _cam.transform.LookAt(b.center);

        int W = 960, H = 620;
        var rt = new RenderTexture(W, H, 24);
        _cam.targetTexture = rt;
        _cam.Render();
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
        tex.Apply();
        File.WriteAllBytes(SD + "/QM_" + tag + ".png", tex.EncodeToPNG());
        RenderTexture.active = prev;
        UnityEngine.Object.Destroy(tex);
        UnityEngine.Object.Destroy(rt);
        _cam.targetTexture = null;
    }

    System.Collections.IEnumerator Done() { Flush(); Debug.Log("[q_long2] 写入 " + RP); yield return null; }
    void Flush() { try { File.WriteAllText(RP, _sb.ToString()); } catch { } }
}

var __hostL2 = new GameObject("q_long2");
__hostL2.AddComponent<q_long2>();
return "Q_LONG2_STARTED";
