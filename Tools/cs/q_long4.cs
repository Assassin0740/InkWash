using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;

// q_long4.cs —— 依次播演示场 4 个墨龙条目，验证「四招是否真的不同」
//   记录每招的 _attack / _currentLift / Visual.localPosition.y / 四肢根数
public class q_long4 : MonoBehaviour
{
    const string RP = "D:/Unity Project/InkWash/Tools/reports/q_long4.txt";
    const string SD = "D:/Unity Project/InkWash/Tools/screenshots/enemies";

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
        foreach (var o in UnityEngine.Object.FindObjectsOfType(_ty, true)) { _sc = o as Component; if (_sc != null) break; }
        _mPlay = _ty.GetMethod("PlayForCapture", BF);
        _mLabel = _ty.GetMethod("ItemLabel", BF);
        _mStop = _ty.GetMethod("StopAutoClose", BF);
        _pCount = _ty.GetProperty("ItemCount", BF);
        _mStop.Invoke(_sc, null);

        int count = (int)_pCount.GetValue(_sc, null);
        var targets = new List<int>();
        var labels = new List<string>();
        for (int i = 0; i < count; i++)
        {
            string lb = (string)_mLabel.Invoke(_sc, new object[] { i });
            if (lb.Contains("墨龙"))
            {
                targets.Add(i);
                string shortName = lb.Substring(lb.LastIndexOf('/') + 1).Trim();
                labels.Add(shortName);
            }
        }
        _sb.AppendLine("墨龙条目 = " + targets.Count + " 个: " + string.Join(" | ", labels.ToArray()));
        _sb.AppendLine();

        _cam = MakeCam();

        for (int k = 0; k < targets.Count; k++)
        {
            int idx = targets[k];
            string tag = "P" + k;
            _sb.AppendLine("══════ [" + idx + "] " + labels[k] + " ══════");
            _mPlay.Invoke(_sc, new object[] { idx });
            Flush();
            for (int i = 0; i < 35; i++) yield return null;

            _go = GameObject.Find("Showcase_Actor_" + idx);
            if (_go == null) { _sb.AppendLine("  × 没找到 actor"); continue; }
            _actor = _go.transform;
            var drg = _go.GetComponent(_dt);
            if (drg == null) { _sb.AppendLine("  × 无 EnemyDragon"); continue; }

            _vroot = _dt.GetField("_modelRoot", BF).GetValue(drg) as Transform;
            var fLimb = _dt.GetField("_limbRoots", BF);
            var limbs = fLimb == null ? null : fLimb.GetValue(drg) as IList;
            var fAtk = _dt.GetField("_attack", BF);
            var fLift = _dt.GetField("_currentLift", BF);
            var fAir = _dt.GetField("_airborne", BF);

            _sb.AppendLine("  _limbRoots.Count = " + (limbs == null ? -1 : limbs.Count)
                + "    limbSwingDeg = " + _dt.GetField("limbSwingDeg", BF).GetValue(drg));

            // 采样：每 0.35 s 一次，共 8 次
            for (int f = 0; f < 8; f++)
            {
                string atk = fAtk == null ? "-" : fAtk.GetValue(drg).ToString();
                string lift = fLift == null ? "-" : ((float)fLift.GetValue(drg)).ToString("F2");
                string air = fAir == null ? "-" : fAir.GetValue(drg).ToString();
                float vy = _vroot == null ? 0f : _vroot.localPosition.y;
                _sb.AppendLine("    t" + f + "  招=" + atk.PadRight(14) + " 高度目标=" + lift.PadRight(6)
                    + " 实用高度=" + vy.ToString("F2").PadRight(6) + " airborne=" + air
                    + "  worldY=" + _actor.position.y.ToString("F2"));
                if (f % 2 == 0) Snap(tag + "_" + (f / 2));
                Flush();
                for (int q = 0; q < 21; q++) yield return null;
            }
            _sb.AppendLine();
        }

        UnityEngine.Object.Destroy(_cam.gameObject);
        yield return Done();
    }

    Camera MakeCam()
    {
        var g = new GameObject("PROBE_CAM_L4");
        var c = g.AddComponent<Camera>();
        c.clearFlags = CameraClearFlags.SolidColor;
        c.backgroundColor = new Color(0.86f, 0.88f, 0.91f, 1f);
        c.fieldOfView = 48f;
        c.nearClipPlane = 0.05f;
        c.farClipPlane = 5000f;
        c.enabled = false;
        return c;
    }

    void Snap(string tag)
    {
        var rds = _go.GetComponentsInChildren<Renderer>(true);
        var b = rds[0].bounds;
        for (int i = 1; i < rds.Length; i++) b.Encapsulate(rds[i].bounds);
        float rad = b.extents.magnitude;
        if (rad < 0.5f) rad = 0.5f;
        float dist = rad / Mathf.Sin(_cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.02f;
        // 3/4 侧上方：既能看出俯仰也能看出蛇形
        _cam.transform.position = b.center + new Vector3(0.55f, 0.52f, 0.66f).normalized * dist;
        _cam.transform.LookAt(b.center);

        int W = 800, H = 560;
        var rt = new RenderTexture(W, H, 24);
        _cam.targetTexture = rt;
        _cam.Render();
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
        tex.Apply();
        File.WriteAllBytes(SD + "/QA_" + tag + ".png", tex.EncodeToPNG());
        RenderTexture.active = prev;
        UnityEngine.Object.Destroy(tex);
        UnityEngine.Object.Destroy(rt);
        _cam.targetTexture = null;
    }

    System.Collections.IEnumerator Done() { Flush(); Debug.Log("[q_long4] 写入 " + RP); yield return null; }
    void Flush() { try { File.WriteAllText(RP, _sb.ToString()); } catch { } }
}

var __hostL4 = new GameObject("q_long4");
__hostL4.AddComponent<q_long4>();
return "Q_LONG4_STARTED";
