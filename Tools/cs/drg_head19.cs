using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;

// drg_head19.cs —— 第十九轮：头部俯仰**细档扫描**出图，交用户肉眼定档
//
// 为什么重扫：第十八轮把 headAlignPitchDeg 定成 30°，**扫描区间起点就是 20°**，
//   而出图 p20 / p30 / p40 三档**全是仰着的** ⇒ 有用区间根本不在扫描范围里。
//   用户看 v3 录像的原话：「头抬得这么高要干嘛啊？？」
//
// ★ 尺子也换了。第十八轮用「头簇 PCA 第一主轴」当尺子得出"轴朝下 30.2°⇒ 抬头 30°"，
//   但 `base_h_side` / `base_h_q` 显示**基准姿态的头本来就是朝前的** ——
//   PCA 主轴量的是"颈-颅整块质量"的走向，被颅顶 + 颈根一起拉偏，**不等于吻部朝向**。
//   ⇒ 本轮不再拿 PCA 当决策依据：**出图，由用户肉眼定档**（这才是审美判断该有的样子）。
//
// 出图条件（保证六档可比）：
//   · 原地悬停（hoverStationary=true）⇒ 位置与朝向锁定，只有头角度在变
//   · 关省电循环（enablePerformanceCycle=false）⇒ 幅度恒定
//   · **相机只算一次**（以 pitch=0 时的头簇中心为准）六档共用 ⇒ 档与档之间的位移就是头真的动了
//   · **每档等到同一个行波相位**（frac(swimWaveFreq·t) ≈ 0.30）再拍 ⇒ 脖子曲率一致

public class drg_head19 : MonoBehaviour
{
    const string DIR = "D:/Unity Project/InkWash/Tools/screenshots/enemies/H19";
    const string RP = "D:/Unity Project/InkWash/Tools/reports/drg_head19.txt";
    const string KEY = "墨龙";

    static readonly float[] PITCH = new float[] { 0f, 5f, 10f, 15f, 20f, 30f };
    const float TARGET_PHASE = 0.30f;
    const float DRIVE_HZ = 0.45f;

    static readonly BindingFlags BF = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;

    readonly StringBuilder _sb = new StringBuilder();
    Component _sc; Type _ty, _dt;
    MethodInfo _mPlay, _mLabel, _mStop;
    PropertyInfo _pCount;
    Camera _camSide, _camQ;
    GameObject _go;
    Transform _drgT, _headPivot;
    System.Collections.IList _spine;
    Vector3 _center;
    float _dist, _distQ;
    int _NS;

    void Start() { StartCoroutine(Run()); }

    IEnumerator Run()
    {
        yield return null; yield return null;
        // ★ 必须钉死帧步长：否则 WaitPhase 的相位容差可能永远踩不到
        //   （帧步长 > 容差 ⇒ 每帧都跳过目标相位，240 帧后放弃、六档相位各不相同）
        Time.captureFramerate = 30;

        Directory.CreateDirectory(DIR);
        foreach (var f in Directory.GetFiles(DIR, "*.png")) { try { File.Delete(f); } catch { } }

        foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (_ty == null) { var t1 = a.GetType("InkWash.DebugTools.ActionShowcase"); if (t1 != null) _ty = t1; }
            if (_dt == null) { var t2 = a.GetType("InkWash.Enemies.EnemyDragon"); if (t2 != null) _dt = t2; }
        }
        if (_ty == null || _dt == null) { _sb.AppendLine("× 类型缺失"); yield return Done(); }

        foreach (var o in UnityEngine.Object.FindObjectsOfType(_ty, true)) { _sc = o as Component; if (_sc != null) break; }
        if (_sc == null) { _sb.AppendLine("× 场景里没有 ActionShowcase（先跑 q_open_scene.cs）"); yield return Done(); }

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

        _mStop.Invoke(_sc, null);
        _mPlay.Invoke(_sc, new object[] { idx });
        for (int i = 0; i < 90; i++) yield return null;

        _go = GameObject.Find("Showcase_Actor_" + idx);
        if (_go == null) { _sb.AppendLine("× 没找到 actor 实例"); yield return Done(); }

        var drg = _go.GetComponent(_dt);
        _drgT = drg.transform;
        _spine = _dt.GetField("_spine", BF).GetValue(drg) as System.Collections.IList;
        var fPitch = _dt.GetField("headAlignPitchDeg", BF);
        if (_spine == null || fPitch == null) { _sb.AppendLine("× 字段缺失"); yield return Done(); }
        _NS = _spine.Count;
        _headPivot = _spine[_NS - 2] as Transform;

        _sb.AppendLine("=== drg_head19：头部俯仰细档扫描（第十九轮）===");
        _sb.AppendLine("条目 idx=" + idx + "  label=" + found);
        _sb.AppendLine("档位（度）=" + string.Join(", ", Array.ConvertAll(PITCH, x => x.ToString("F0"))));
        _sb.AppendLine("相机：**只算一次**（pitch=0 时以头簇中心为准），六档共用 ⇒ 档间位移就是头真的动了");
        _sb.AppendLine("相位：每档等到 frac(0.45·t) ≈ " + TARGET_PHASE.ToString("F2") + " 再拍");
        _sb.AppendLine();

        _dt.GetField("hoverStationary", BF).SetValue(drg, true);
        _dt.GetField("enablePerformanceCycle", BF).SetValue(drg, false);
        fPitch.SetValue(drg, 0f);
        for (int i = 0; i < 12; i++) yield return null;
        yield return WaitPhase();

        // 相机只算一次
        // ★ 头簇包围球必须**递归**取全部后代：只取 `_spine[Count-2]` 的直接子节点时，
        //   实测 q 取景把头切在画面右缘（鬃 / 须 / 角挂在更深的层级上，没被算进去）。
        var all = new System.Collections.Generic.List<Vector3>();
        CollectPos(_headPivot, all);
        _center = Vector3.zero;
        for (int i = 0; i < all.Count; i++) _center += all[i];
        _center /= Mathf.Max(1, all.Count);
        float rad = 0.25f;
        for (int i = 0; i < all.Count; i++) rad = Mathf.Max(rad, (all[i] - _center).magnitude);
        rad += 0.12f;
        var probe = MakeCam("PROBE_CAM_H19A");
        float k = rad / Mathf.Sin(probe.fieldOfView * 0.5f * Mathf.Deg2Rad);
        _dist = k * 1.02f;
        _distQ = k * 1.20f;
        Vector3 fwd = Flat(_drgT.forward).normalized;
        Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized;
        _camSide = probe;
        _camQ = MakeCam("PROBE_CAM_H19B");
        AimSide(); AimQ();
        _sb.AppendLine("头簇包围球：骨 " + all.Count + " 枚，中心 " + _center.ToString("F3")
                       + "，半径 " + rad.ToString("F3") + "，侧视距 " + _dist.ToString("F2")
                       + "，3/4 距 " + _distQ.ToString("F2"));
        _sb.AppendLine();

        _sb.AppendLine("档位      头簇骨数  头簇中心相对颈根(容器系)      仰角(相对容器 fwd)");
        foreach (float p in PITCH)
        {
            fPitch.SetValue(drg, p);
            yield return null;          // ★ 至少过一帧：改字段不会立刻改姿态（Update 才写入骨骼）
            yield return WaitPhase();

            int bones = 0; Vector3 acc = Vector3.zero;
            for (int i = 0; i < _headPivot.childCount; i++) { acc += _headPivot.GetChild(i).position; bones++; }
            acc /= Mathf.Max(1, bones);
            Vector3 rel = acc - _headPivot.position;
            float elev = Vector3.Angle(rel, fwd) - 90f;      // > 0 = 头簇在颈轴上方

            File.WriteAllBytes(DIR + "/h19_side_p" + ((int)p).ToString("D2") + ".png", Shot(_camSide));
            File.WriteAllBytes(DIR + "/h19_q_p" + ((int)p).ToString("D2") + ".png", Shot(_camQ));

            _sb.AppendLine("  " + Pad(p.ToString("F0") + "°", 8) + Pad(bones.ToString(), 10)
                           + Pad(rel.ToString("F3"), 30) + elev.ToString("F2") + "°");
        }

        _sb.AppendLine();
        _sb.AppendLine("★ 选哪一档由人眼定：看 h19_side_p*.png / h19_q_p*.png。");
        _sb.AppendLine("  「仰角」这一列只作参考 —— 第十八轮就是被一个几何量（PCA 长轴）带偏的，");
        _sb.AppendLine("  头簇中心同样不等于吻部朝向，**别拿它当决策依据**。");

        if (_camSide != null) UnityEngine.Object.Destroy(_camSide.gameObject);
        if (_camQ != null) UnityEngine.Object.Destroy(_camQ.gameObject);
        yield return Done();
    }

    IEnumerator WaitPhase()
    {
        for (int i = 0; i < 240; i++)
        {
            float fr = Mathf.Repeat(DRIVE_HZ * Time.time, 1f);
            float d = Mathf.Abs(fr - TARGET_PHASE);
            if (d > 0.5f) d = 1f - d;
            if (d < 0.012f) yield break;
            yield return null;
        }
    }

    void AimSide()
    {
        Vector3 fwd = Flat(_drgT.forward).normalized;
        Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized;
        Vector3 dir = (right * 0.982f + Vector3.up * 0.135f + fwd * 0.13f).normalized;
        _camSide.transform.position = _center + dir * _dist;
        _camSide.transform.LookAt(_center, Vector3.up);
    }

    void AimQ()
    {
        Vector3 fwd = Flat(_drgT.forward).normalized;
        Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized;
        Vector3 dir = (-right * 0.40f + fwd * 0.74f + Vector3.up * 0.54f).normalized;
        _camQ.transform.position = _center + dir * _distQ;
        _camQ.transform.LookAt(_center, Vector3.up);
    }

    static void CollectPos(Transform t, System.Collections.Generic.List<Vector3> outp)
    {
        outp.Add(t.position);
        for (int i = 0; i < t.childCount; i++) CollectPos(t.GetChild(i), outp);
    }

    byte[] Shot(Camera cam)
    {
        int W = 900, H = 600;
        var rt = new RenderTexture(W, H, 24);
        cam.targetTexture = rt;
        cam.Render();
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
        tex.Apply();
        byte[] png = tex.EncodeToPNG();
        RenderTexture.active = prev;
        UnityEngine.Object.Destroy(tex);
        UnityEngine.Object.Destroy(rt);
        cam.targetTexture = null;
        return png;
    }

    Camera MakeCam(string name)
    {
        var g = new GameObject(name);
        var c = g.AddComponent<Camera>();
        c.clearFlags = CameraClearFlags.SolidColor;
        c.backgroundColor = new Color(0.86f, 0.88f, 0.91f, 1f);
        c.fieldOfView = 40f;
        c.nearClipPlane = 0.03f;
        c.farClipPlane = 5000f;
        c.enabled = false;
        return c;
    }

    static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
    static string Pad(string s, int n) { return s.Length >= n ? s : s + new string(' ', n - s.Length); }

    IEnumerator Done()
    {
        Time.captureFramerate = 0;
        try { File.WriteAllText(RP, _sb.ToString()); } catch { }
        Debug.Log("[drg_head19] 写入 " + RP);
        yield return null;
    }
}

var __hostH19 = new GameObject("drg_head19");
__hostH19.AddComponent<drg_head19>();
return "DRG_HEAD19_STARTED";
