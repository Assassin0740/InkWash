using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;

// drg_rest.cs —— 墨龙「基准姿态 vs 驱动姿态」同机位对照（只读，跑完恢复原参数）
//
// 用户（第十八轮）四条：
//   ① 头还是歪的，他的头没有顺着那个脖子的位置
//   ② 爪子应该是往后的（他要往前飞）
//   ③ 尾巴末端是歪的，翘起来了，应该保持同一水平线的高度
//   ④ 动画应该是不断重复的
//
// ■ 为什么必须先做这个对照：
//   `drg_head` 把骨架 dump 出来了，同时暴露了上一轮判据的口径错误 ——
//   上一轮量的是「分支末端相对根节的 y **极差**」，尾巴是刚性的 ⇒ 极差恒 0 ⇒ 判"✅"，
//   可它**绝对值一直是 +0.5~0.8 m**（尾巴本来就翘着）。尺子量的是"动没动"，用户问的是"平不平"。
//   所以这一版把「静止基准姿态」和「驱动姿态」放同一机位拍，
//   直接回答：翘/歪 是**模型原生形状**，还是**我们驱动出来的**。
//
// ■ 怎么造"静止基准"：`swimWaveAmp = 0` ⇒ 曲线的 lat 恒 0 ⇒
//   `FromToRotation` 恒为单位四元数 ⇒ 脊骨回到**基准姿态**（= StraightenSpineBase 的结果）。
//   再把 `limbSwingDeg = 0` ⇒ 四肢也不叠摆动。合起来就是纯基准帧。
//
// ★ 纪律：不给颜色画记号、不留临时脚本、跑完把参数写回原值。

public class drg_rest : MonoBehaviour
{
    const string SD = "D:/Unity Project/InkWash/Tools/screenshots/enemies/HDR";
    const string RP = "D:/Unity Project/InkWash/Tools/reports/drg_rest.txt";
    const string KEY = "墨龙";

    readonly StringBuilder _sb = new StringBuilder();
    Component _sc; Type _ty, _dt;
    MethodInfo _mPlay, _mLabel, _mStop;
    PropertyInfo _pCount;
    Camera _cam;
    GameObject _go;
    Transform _drgT, _vroot;
    IList _spine, _limbRoots, _limbDriven, _limbAxisDot;
    FieldInfo _fAmp, _fWave, _fStat, _fLimbDeg, _fYaw;

    static readonly BindingFlags BF = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;

    int _saved;

    void Start() { StartCoroutine(Run()); }

    IEnumerator Run()
    {
        yield return null; yield return null;

        Directory.CreateDirectory(SD);
        foreach (var f in Directory.GetFiles(SD, "*.png")) { try { File.Delete(f); } catch { } }

        foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (_ty == null) { var t1 = a.GetType("InkWash.DebugTools.ActionShowcase"); if (t1 != null) _ty = t1; }
            if (_dt == null) { var t2 = a.GetType("InkWash.Enemies.EnemyDragon"); if (t2 != null) _dt = t2; }
        }
        if (_ty == null || _dt == null) { _sb.AppendLine("× 类型缺失"); yield return Done(); }

        foreach (var o in UnityEngine.Object.FindObjectsOfType(_ty, true)) { _sc = o as Component; if (_sc != null) break; }
        if (_sc == null) { _sb.AppendLine("× 场景里没有 ActionShowcase（当前不是 Showcase 场景？）"); yield return Done(); }

        _mPlay = _ty.GetMethod("PlayForCapture", BF);
        _mLabel = _ty.GetMethod("ItemLabel", BF);
        _mStop = _ty.GetMethod("StopAutoClose", BF);
        _pCount = _ty.GetProperty("ItemCount", BF);

        int count = (int)_pCount.GetValue(_sc, null);
        int idx = -1; string found = "";
        for (int i = 0; i < count; i++)
        {
            string lb = (string)_mLabel.Invoke(_sc, new object[] { i });
            if (lb.Contains(KEY) && lb.Contains("盘旋")) { idx = i; found = lb; break; }
        }
        if (idx < 0) { _sb.AppendLine("× 没找到「墨龙 + 盘旋」条目"); yield return Done(); }

        _sb.AppendLine("=== drg_rest：基准姿态 vs 驱动姿态（同机位）===");
        _sb.AppendLine("条目 idx=" + idx + "  label=" + found);
        _sb.AppendLine();

        _mStop.Invoke(_sc, null);
        _mPlay.Invoke(_sc, new object[] { idx });
        for (int i = 0; i < 90; i++) yield return null;

        _go = GameObject.Find("Showcase_Actor_" + idx);
        if (_go == null) { _sb.AppendLine("× 没找到 actor 实例"); yield return Done(); }

        var drg = _go.GetComponent(_dt);
        _drgT = drg.transform;
        _vroot = _dt.GetField("_modelRoot", BF).GetValue(drg) as Transform;
        _spine = _dt.GetField("_spine", BF).GetValue(drg) as IList;
        _limbRoots = _dt.GetField("_limbRoots", BF).GetValue(drg) as IList;
        _limbDriven = _dt.GetField("_limbDriven", BF).GetValue(drg) as IList;
        _limbAxisDot = _dt.GetField("_limbAxisDot", BF).GetValue(drg) as IList;
        _fAmp = _dt.GetField("swimWaveAmp", BF);
        _fWave = _dt.GetField("swimWaveCount", BF);
        _fStat = _dt.GetField("hoverStationary", BF);
        _fLimbDeg = _dt.GetField("limbSwingDeg", BF);
        _fYaw = _dt.GetField("hoverStationaryYawDeg", BF);

        if (_spine == null || _spine.Count < 3) { _sb.AppendLine("× 脊柱节数 < 3"); yield return Done(); }
        int NS = _spine.Count;
        int NL = _limbRoots == null ? 0 : _limbRoots.Count;

        float amp0 = _fAmp != null ? (float)_fAmp.GetValue(drg) : 1.15f;
        float limb0 = _fLimbDeg != null ? (float)_fLimbDeg.GetValue(drg) : 25f;
        float wave0 = _fWave != null ? (float)_fWave.GetValue(drg) : 1f;

        if (_fStat != null) _fStat.SetValue(drg, true);
        if (_fYaw != null) _fYaw.SetValue(drg, 0f);
        if (_fWave != null) _fWave.SetValue(drg, 1.0f);
        for (int i = 0; i < 8; i++) yield return null;

        _sb.AppendLine("── 参数回读（原值，跑完写回）──");
        _sb.AppendLine("  swimWaveAmp  = " + amp0);
        _sb.AppendLine("  swimWaveCount= " + wave0 + "（本探针强制 1.0）");
        _sb.AppendLine("  limbSwingDeg = " + limb0);
        _sb.AppendLine();

        // ── A. 驱动姿态（原参数）──
        _sb.AppendLine("── A. 驱动姿态（amp=" + amp0 + "）──");
        yield return null; yield return null;
        SnapAll("drv");
        Describe(NS, NL, "驱动");

        // ── B. 基准姿态（amp=0, limb=0）──
        if (_fAmp != null) _fAmp.SetValue(drg, 0f);
        if (_fLimbDeg != null) _fLimbDeg.SetValue(drg, 0f);
        for (int i = 0; i < 10; i++) yield return null;

        _sb.AppendLine();
        _sb.AppendLine("── B. 基准姿态（amp=0 / limbSwing=0 ⇒ 曲线 lat≡0 ⇒ 纯 StraightenSpineBase 的结果）──");
        SnapAll("base");
        Describe(NS, NL, "基准");

        // ── 恢复 ──
        if (_fAmp != null) _fAmp.SetValue(drg, amp0);
        if (_fLimbDeg != null) _fLimbDeg.SetValue(drg, limb0);
        if (_fWave != null) _fWave.SetValue(drg, wave0);
        for (int i = 0; i < 4; i++) yield return null;

        _sb.AppendLine();
        _sb.AppendLine("PNG 帧数 = " + _saved
            + "（b_side / b_top / h_side / h_q / h_top，各 drv / base 两张）");

        if (_cam != null) UnityEngine.Object.Destroy(_cam.gameObject);
        yield return Done();
    }

    /// <summary>量当前这一帧的"静态几何"：尾巴抬多高、头相对脖子偏多少、腿朝哪。</summary>
    void Describe(int NS, int NL, string tag)
    {
        Quaternion rootRot = _vroot.rotation;
        Quaternion invRoot = Quaternion.Inverse(rootRot);

        // 尾分支：绝对值（不是极差！）
        Transform tail = null;
        for (int k = 0; k < NL; k++)
        {
            float ad = _limbAxisDot != null && k < _limbAxisDot.Count ? (float)_limbAxisDot[k] : -1f;
            bool driven = _limbDriven != null && k < _limbDriven.Count && (bool)_limbDriven[k];
            if (!driven && ad >= 0.8f) { tail = _limbRoots[k] as Transform; break; }
        }
        if (tail != null)
        {
            Transform tip = tail;
            var stack = new Stack<Transform>(); stack.Push(tail);
            float best = -1f;
            while (stack.Count > 0)
            {
                var t = stack.Pop();
                float d = (t.position - tail.position).magnitude;
                if (d > best) { best = d; tip = t; }
                for (int c = 0; c < t.childCount; c++) stack.Push(t.GetChild(c));
            }
            float rootY = tail.parent != null ? tail.parent.position.y : tail.position.y;
            _sb.AppendLine("  [" + tag + "] 尾尖 = " + tip.name + "  离根节水平距 " + best.ToString("F2") + " m"
                + "  **绝对抬升 Δy = " + (tip.position.y - rootY).ToString("F3") + " m**"
                + "  （用户要 ≈ 0）");
        }

        // 头：用 S22 的叶子扇（头部正面的一圈骨）当"头的朝向"
        var headRoot = _spine[NS - 2] as Transform;
        var chainEnd = _spine[NS - 1] as Transform;
        if (headRoot != null)
        {
            Vector3 neckDir = chainEnd != null ? (chainEnd.position - headRoot.position).normalized : Vector3.forward;
            Vector3 acc = Vector3.zero; int n = 0;
            for (int i = 0; i < headRoot.childCount; i++)
            {
                var c = headRoot.GetChild(i);
                if (c == chainEnd) continue;
                if (c.childCount != 0) continue;        // 只看叶子（正面那一圈）
                acc += c.position - headRoot.position; n++;
            }
            Vector3 headDir = n > 0 ? (acc / n).normalized : neckDir;
            float ang = Vector3.Angle(headDir, neckDir);
            _sb.AppendLine("  [" + tag + "] 头扇 " + n + " 枚叶子骨的均值方向 vs 颈段方向 夹角 = "
                + ang.ToString("F2") + "°   头扇方向 " + F3(invRoot * headDir) + "  颈方向 " + F3(invRoot * neckDir)
                + "   头扇质心抬升 " + (n > 0 ? ((acc / n).y).ToString("F3") : "?") + " m");
        }

        // 尾巴/头的**滚转**：S00 与 S22 的 up 轴偏离容器 up 多少
        for (int i = 0; i < NS; i += 11)
        {
            var t = _spine[i] as Transform;
            if (t == null) continue;
            Vector3 upC = invRoot * t.up;
            float roll = Vector3.Angle(upC, Vector3.up);
            _sb.AppendLine("  [" + tag + "] 脊柱[" + i + "] " + t.name + " 的 up 偏离容器 up " + roll.ToString("F2")
                + "°   forward=" + F3(invRoot * t.forward));
        }
        if (NS > 22)
        {
            var t = _spine[NS - 2] as Transform;
            if (t != null)
            {
                Vector3 upC = invRoot * t.up;
                _sb.AppendLine("  [" + tag + "] 脊柱[" + (NS - 2) + "] " + t.name + " 的 up 偏离容器 up "
                    + Vector3.Angle(upC, Vector3.up).ToString("F2") + "°   forward=" + F3(invRoot * t.forward));
            }
        }

        // 腿
        for (int k = 0; k < NL; k++)
        {
            bool driven = _limbDriven != null && k < _limbDriven.Count && (bool)_limbDriven[k];
            if (!driven) continue;
            var b = _limbRoots[k] as Transform;
            if (b == null || b.childCount == 0) continue;
            Vector3 d = (invRoot * (b.GetChild(0).position - b.position)).normalized;
            float dotFwd = Vector3.Dot(d, Vector3.forward);
            _sb.AppendLine("  [" + tag + "] 腿 " + b.name + " 首段 " + F3(d)
                + "  dot(fwd) = " + dotFwd.ToString("F3") + " (" + (dotFwd < 0f ? "往后" : "往前") + ")"
                + "  dot(up) = " + Vector3.Dot(d, Vector3.up).ToString("F3"));
        }
    }

    void SnapAll(string tag)
    {
        Snap("b_side", tag); Snap("b_top", tag);
        Snap("h_side", tag); Snap("h_q", tag); Snap("h_top", tag);
    }

    void Snap(string view, string tag)
    {
        if (_cam == null) _cam = MakeCam();
        int NS = _spine.Count;
        var pos = new Vector3[NS];
        for (int i = 0; i < NS; i++) { var t = _spine[i] as Transform; pos[i] = t != null ? t.position : Vector3.zero; }

        Vector3 fwd = Flat(_drgT.forward).normalized;
        Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized;

        Vector3 center; float rad; Vector3 dir;

        if (view == "b_side")
        {
            center = Vector3.zero; for (int i = 0; i < NS; i++) center += pos[i]; center /= NS;
            rad = 0f; for (int i = 0; i < NS; i++) rad = Mathf.Max(rad, (pos[i] - center).magnitude);
            if (_limbRoots != null)
                for (int k = 0; k < _limbRoots.Count; k++)
                {
                    var b = _limbRoots[k] as Transform;
                    if (b != null) rad = Mathf.Max(rad, (b.position - center).magnitude + 3.4f);
                }
            dir = (right * 0.985f + Vector3.up * 0.10f + fwd * 0.14f).normalized;
        }
        else if (view == "b_top")
        {
            center = Vector3.zero; for (int i = 0; i < NS; i++) center += pos[i]; center /= NS;
            rad = 0f; for (int i = 0; i < NS; i++) rad = Mathf.Max(rad, (pos[i] - center).magnitude);
            if (_limbRoots != null)
                for (int k = 0; k < _limbRoots.Count; k++)
                {
                    var b = _limbRoots[k] as Transform;
                    if (b != null) rad = Mathf.Max(rad, (b.position - center).magnitude + 3.4f);
                }
            dir = (Vector3.up * 0.985f + fwd * 0.16f + right * 0.05f).normalized;
        }
        else
        {
            // 头：取最后 3 节 + 头扇的包围盒
            center = Vector3.zero; int c = 0;
            for (int i = NS - 3; i < NS; i++) { center += pos[i]; c++; }
            center /= c;
            rad = 1.15f;
            var headRoot = _spine[NS - 2] as Transform;
            if (headRoot != null)
                for (int i = 0; i < headRoot.childCount; i++)
                    rad = Mathf.Max(rad, (headRoot.GetChild(i).position - center).magnitude + 0.25f);

            if (view == "h_side") dir = (right * 0.97f + Vector3.up * 0.16f + fwd * 0.10f).normalized;
            else if (view == "h_top") dir = (Vector3.up * 0.96f + fwd * 0.24f + right * 0.10f).normalized;
            else dir = (-right * 0.52f + fwd * 0.66f + Vector3.up * 0.44f).normalized;
        }

        float dist = rad / Mathf.Sin(_cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.06f;
        _cam.transform.position = center + dir * dist;
        _cam.transform.LookAt(center, Vector3.up);

        int W = 1280, H = 720;
        var rt = new RenderTexture(W, H, 24);
        _cam.targetTexture = rt;
        _cam.Render();
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
        tex.Apply();
        File.WriteAllBytes(SD + "/" + tag + "_" + view + ".png", tex.EncodeToPNG());
        RenderTexture.active = prev;
        UnityEngine.Object.Destroy(tex);
        UnityEngine.Object.Destroy(rt);
        _cam.targetTexture = null;
        _saved++;
        try { File.WriteAllText(RP, _sb.ToString()); } catch { }
    }

    Camera MakeCam()
    {
        var g = new GameObject("PROBE_CAM_HDR");
        var c = g.AddComponent<Camera>();
        c.clearFlags = CameraClearFlags.SolidColor;
        c.backgroundColor = new Color(0.86f, 0.88f, 0.91f, 1f);
        c.fieldOfView = 40f;
        c.nearClipPlane = 0.05f;
        c.farClipPlane = 5000f;
        c.enabled = false;
        return c;
    }

    static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
    static string F3(Vector3 v) { return "(" + v.x.ToString("F3") + "," + v.y.ToString("F3") + "," + v.z.ToString("F3") + ")"; }

    IEnumerator Done()
    {
        Time.captureFramerate = 0;
        try { File.WriteAllText(RP, _sb.ToString()); } catch { }
        Debug.Log("[drg_rest] 写入 " + RP + "  帧数 " + _saved);
        yield return null;
    }
}

var __hostRest = new GameObject("drg_rest");
__hostRest.AddComponent<drg_rest>();
return "DRG_REST_STARTED";
