// a62_flicker_ab.cs —— a61 的对照实验（A/B）：
// a61 报「帧间差 = 0.000」，完美得可疑。0 有两种可能：
//   ① 地面真的静止（好消息）
//   ② 采样窗里根本没有地面（黑屏/纯色）—— 也会是 0
//
// 所以必须做对照：把 A48_Ground 那种共面地板**重新造一个**放回去，
// 如果帧间差**显著 > 0**，说明这套度量确实能抓到 z-fighting，
// 那么 a61 的 0 才是可信的。这是「度量本身要交叉验证」。
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

public class A62Probe : MonoBehaviour
{
    public enum Mode { Off, OnCoplanar, Off2, WithGroundRender }
    public Mode mode = Mode.Off;
    public string outPath;

    Camera _cam; RenderTexture _rt; Texture2D _tex;
    int _n; Color32[] _last;
    readonly List<float> _diffs = new List<float>();
    float _maxDiff;
    int _warmup = 25, _sample = 70;
    int _sx, _sy, _sw, _sh;
    StringBuilder _sb;
    GameObject _injected;
    float _lumaMean, _lumaMin, _lumaMax;

    void Start()
    {
        _sb = new StringBuilder();
        _cam = Camera.main != null ? Camera.main : FindObjectOfType<Camera>();
        foreach (var mb in _cam.GetComponents<MonoBehaviour>())
        {
            if (mb == null) continue;
            string tn = mb.GetType().Name;
            if (tn.Contains("ShowcaseCam") || tn.Contains("Brain") || tn.Contains("Follow"))
                DestroyImmediate(mb);
        }
        _cam.transform.position = new Vector3(0f, 3.0f, -9f);
        _cam.transform.LookAt(new Vector3(0f, 0f, 4f));
        _cam.fieldOfView = 45f;

        int disabled = 0;
        foreach (var g in FindObjectsOfType<GameObject>())
        {
            if (g == null || g.transform.IsChildOf(_cam.transform)) continue;
            bool moving = g.CompareTag("Player") || g.CompareTag("Enemy")
                       || g.GetComponent<Animator>() != null
                       || g.GetComponent<Rigidbody>() != null
                       || g.GetComponent<CharacterController>() != null;
            if (moving && g.activeSelf) { g.SetActive(false); disabled++; }
        }

        _sb.AppendLine($"========== a62 度量对照实验  mode={mode} ==========");
        _sb.AppendLine($"相机={_cam.transform.position:F2} fov={_cam.fieldOfView}  停用动态物={disabled}");

        // ★ 对照组：注入一块与 Environment/Ground 完全共面的地板 → 人为制造 z-fighting
        if (mode == Mode.OnCoplanar)
        {
            var src = GameObject.Find("Environment/Ground");
            Vector3 p = src != null ? src.transform.position : Vector3.zero;
            Vector3 s = src != null ? src.transform.lossyScale : Vector3.one;
            _injected = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _injected.name = "A62_Ground";     // 用 A<数字>_ 前缀，好清
            _injected.transform.position = new Vector3(p.x, p.y, p.z);
            _injected.transform.localScale = new Vector3(s.x, s.y, s.z);
            _sb.AppendLine($"★ 已注入共面地板 A62_Ground @ {_injected.transform.position:F3} scale={_injected.transform.localScale:F2}  ← 应与 Environment/Ground 100% 共面");
        }

        int W = 640, H = 360;
        _rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32);
        _tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
        _sx = Mathf.RoundToInt(W * 0.20f); _sy = Mathf.RoundToInt(H * 0.45f);
        _sw = Mathf.RoundToInt(W * 0.60f); _sh = Mathf.RoundToInt(H * 0.40f);
        _sb.AppendLine($"采样窗 x[{_sx},{_sx + _sw}] y[{_sy},{_sy + _sh}] ({_sw}x{_sh})");
        _sb.AppendLine($"预热 {_warmup} 采样 {_sample}");
    }

    void LateUpdate()
    {
        if (_n < _warmup) { _n++; return; }
        if (_diffs.Count >= _sample) return;

        if (_cam.targetTexture != _rt)
        {
            _cam.targetTexture = _rt;
            _cam.Render();
            RenderTexture.active = _rt;
            _tex.ReadPixels(new Rect(0, 0, _rt.width, _rt.height), 0, 0);
            _tex.Apply(false, false);
            RenderTexture.active = null;
            _cam.targetTexture = null;
        }

        var px = _tex.GetPixels32();
        var strip = new Color32[_sw * _sh];
        int k = 0; double lsum = 0;
        for (int y = _sy; y < _sy + _sh; y++)
            for (int x = _sx; x < _sx + _sw; x++)
            {
                var c = px[y * _rt.width + x];
                strip[k++] = c;
                lsum += (c.r * 77 + c.g * 150 + c.b * 29) >> 8;
            }
        // ★ 亮度体检：确认窗里真有东西（不是黑屏）
        float lm = (float)(lsum / strip.Length);
        if (_diffs.Count == 0) { _lumaMean = lm; _lumaMin = lm; _lumaMax = lm; }
        else { if (lm < _lumaMin) _lumaMin = lm; if (lm > _lumaMax) _lumaMax = lm; }
        _lumaMean = lm;

        if (_last != null)
        {
            double sum = 0;
            for (int i = 0; i < strip.Length; i++)
            {
                int lg = (_last[i].r * 77 + _last[i].g * 150 + _last[i].b * 29) >> 8;
                int cg = (strip[i].r * 77 + strip[i].g * 150 + strip[i].b * 29) >> 8;
                sum += Mathf.Abs(cg - lg);
            }
            float d = (float)(sum / strip.Length);
            _diffs.Add(d); if (d > _maxDiff) _maxDiff = d;
        }
        _last = strip;

        if (_diffs.Count >= _sample) Finish();
    }

    void Finish()
    {
        double sum = 0; foreach (var d in _diffs) sum += d;
        float avg = (float)(sum / _diffs.Count);
        var s = new List<float>(_diffs); s.Sort();
        float med = s[s.Count / 2];
        float p95 = s[Mathf.Clamp(Mathf.RoundToInt(s.Count * 0.95f), 0, s.Count - 1)];

        _sb.AppendLine();
        _sb.AppendLine("---------- 结果 ----------");
        _sb.AppendLine($"采样窗亮度 均值={_lumaMean:F1}  区间=[{_lumaMin:F1},{_lumaMax:F1}]   (若接近 0 说明是黑屏，度量无效)");
        _sb.AppendLine($"平均帧间差 = {avg:F3}");
        _sb.AppendLine($"中位帧间差 = {med:F3}");
        _sb.AppendLine($"95% 分位   = {p95:F3}");
        _sb.AppendLine($"最大帧间差 = {_maxDiff:F3}");

        _sb.AppendLine();
        if (_lumaMean < 5f)
            _sb.AppendLine("⇒ ✗ 采样窗是黑的 —— 这个 0 不说明任何问题，度量无效");
        else if (avg < 1.0f)
            _sb.AppendLine($"⇒ 窗内有内容(亮度{_lumaMean:F0})且帧间差 {avg:F3} < 1.0   ✓ 地面静止");
        else
            _sb.AppendLine($"⇒ 窗内有内容(亮度{_lumaMean:F0})但帧间差 {avg:F3} >= 1.0   ✗ 存在逐帧跳变（z-fighting）");

        File.WriteAllText(outPath, _sb.ToString());
        Debug.Log("[a62]\n" + _sb.ToString());
        if (_injected != null) DestroyImmediate(_injected);
        Destroy(gameObject);
    }
}

// ---- 顶层：装对照探针 ----
{
    var go = new GameObject("A62_Probe");
    var p = go.AddComponent<A62Probe>();
    p.mode = A62Probe.Mode.OnCoplanar;
    p.outPath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Application.dataPath), "Tools/reports/a62_flicker_ab.txt");
    Debug.Log("[a62] probe installed");
}
