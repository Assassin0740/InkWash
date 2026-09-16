// a64_flicker_final.cs —— z-fighting 的正确度量：相机必须"微动"
//
// 【a61~a63 的教训】
// a61 锁死相机 → 帧间差 0.000
// a62/a63 锁死相机 + 注入共面地板 → 还是 0.000
// ⇒ 结论：**相机完全静止时，深度相等的两个表面每帧光栅化输入完全相同，
//    tie-break 是确定性的 ⇒ 结构上看不到闪。**
//    z-fighting 的可见性来自**亚像素级的相机/物体运动**让每帧的插值误差不同。
//    所以「锁死相机」的度量从原理上就测不到它 —— 这是 a61 那次 0.000 的真正原因，
//    不是地面没问题，而是**度量设计错了**。
//
// 【正确设计】
//   相机保持一个**极小的、确定性的抖动**（模拟真实操作时的微动），
//   然后对比两种配置的帧间差 —— 这才是能区分「有共面/无共面」的对照。
//   判据是**相对比值**，不是绝对值。
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

public class A64Probe : MonoBehaviour
{
    public bool injectCoplanar = false;
    public string outPath;

    Camera _cam; RenderTexture _rt; Texture2D _tex;
    int _n; Color32[] _last;
    readonly List<float> _diffs = new List<float>();
    float _maxDiff; int _warmup = 25, _sample = 70;
    int _sx, _sy, _sw, _sh; StringBuilder _sb;
    GameObject _inj;
    Vector3 _camBase;
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
        _camBase = new Vector3(0f, 3.0f, -9f);
        _cam.transform.position = _camBase;
        _cam.transform.LookAt(new Vector3(0f, 0f, 4f));
        _cam.fieldOfView = 45f;
        foreach (var g in FindObjectsOfType<GameObject>())
        {
            if (g == null || g.transform.IsChildOf(_cam.transform)) continue;
            bool mv = g.CompareTag("Player") || g.CompareTag("Enemy")
                   || g.GetComponent<Animator>() != null || g.GetComponent<Rigidbody>() != null
                   || g.GetComponent<CharacterController>() != null;
            if (mv && g.activeSelf) g.SetActive(false);
        }
        if (injectCoplanar)
        {
            _inj = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _inj.name = "A64_Ground";
            _inj.transform.position = new Vector3(0f, -0.5f, 0f);
            _inj.transform.localScale = new Vector3(80f, 1f, 80f);   // 顶面 y=0，与 Environment/Ground 共面
        }
        _sb.AppendLine($"========== a64 微动相机对照  injectCoplanar={injectCoplanar} ==========");
        _sb.AppendLine("相机基线 = (0,3,-9) fov=45，附加 ±0.004 m 侧向抖动（模拟真实微动）");
        _sb.AppendLine("采样窗 = 中下部地面");

        int W = 640, H = 360;
        _rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32);
        _tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
        _sx = Mathf.RoundToInt(W * 0.20f); _sy = Mathf.RoundToInt(H * 0.50f);
        _sw = Mathf.RoundToInt(W * 0.60f); _sh = Mathf.RoundToInt(H * 0.35f);
    }

    void LateUpdate()
    {
        // ★ 关键：每帧给相机一个亚像素级位移（±0.004 m ≈ 远处 <1 px，
        //   足以让深度插值每帧走不同路径，暴露 z-fighting）
        float t = Time.frameCount * 0.7f;
        _cam.transform.position = _camBase + new Vector3(
            Mathf.Sin(t) * 0.004f, Mathf.Cos(t * 1.3f) * 0.003f, 0f);

        if (_n < _warmup) { _n++; return; }
        if (_diffs.Count >= _sample) return;
        if (_cam.targetTexture != _rt)
        {
            _cam.targetTexture = _rt; _cam.Render();
            RenderTexture.active = _rt;
            _tex.ReadPixels(new Rect(0, 0, _rt.width, _rt.height), 0, 0);
            _tex.Apply(false, false);
            RenderTexture.active = null; _cam.targetTexture = null;
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
        float lm = (float)(lsum / strip.Length);
        if (_diffs.Count == 0) { _lumaMean = _lumaMin = _lumaMax = lm; }
        else { if (lm < _lumaMin) _lumaMin = lm; if (lm > _lumaMax) _lumaMax = lm; _lumaMean = lm; }

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
        _sb.AppendLine();
        _sb.AppendLine($"窗内亮度 均值={_lumaMean:F1} 区间=[{_lumaMin:F1},{_lumaMax:F1}]");
        _sb.AppendLine($"平均帧间差 = {avg:F3}  中位={s[s.Count / 2]:F3}  95%={s[Mathf.Clamp(Mathf.RoundToInt(s.Count * 0.95f), 0, s.Count - 1)]:F3}  最大={_maxDiff:F3}");
        File.WriteAllText(outPath, _sb.ToString());
        Debug.Log("[a64]\n" + _sb.ToString());
        if (_inj != null) DestroyImmediate(_inj);
        Destroy(gameObject);
    }
}

// ---- 顶层：注入共面地板（对照组 B）----
{
    var go = new GameObject("A64_Probe");
    var p = go.AddComponent<A64Probe>();
    p.injectCoplanar = true;
    p.outPath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Application.dataPath), "Tools/reports/a64_flicker_coplanar.txt");
    Debug.Log("[a64] probe installed (WITH coplanar)");
}
