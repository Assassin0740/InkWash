// a63_flicker_ab2.cs —— 对照实验 v2：为什么 a62 没能复现 z-fighting？
//
// a62 注入了一个标准 Cube(44×0.5×44) 与 Environment/Ground 共面，帧间差仍是 0.000。
// 有两种解释：
//   ① 度量不敏感（看不到 z-fighting）—— 那 a61 的 0 就不可信
//   ② 注入物与原 A48_Ground 几何不同（原物是 80×1×80，顶面 y=0；我注入的是 44×0.5×44 顶面 y=0）
//      —— 顶面确实都是 y=0、都 100% 覆盖，所以共面条件成立
//
// 更根本的问题：**z-fighting 需要「两个不透明表面在深度上不可分辨」，
// 而 Unity 的深度测试用的是浮点深度，两个精确共面的表面在不同像素上会
// 因为三角形插值误差而**随机**胜出 —— 这才是逐帧跳变的来源。
// 但如果两个表面**完全等价**（同样的 mesh 维度、同样的三角形切分），
// 光栅化结果可能逐帧稳定 ⇒ 看不到闪。
// 原 A48_Ground 是 80×1×80 的 Cube（8 个顶点），Environment/Ground 是 44×0.5×44，
// 两者**尺寸不同** ⇒ 三角形边界、顶点插值路径都不同 ⇒ 这才抖起来。
//
// 本脚本：用**与原物完全一致的 80×1×80** 复现，验证度量能不能抓到。
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

public class A63Probe : MonoBehaviour
{
    public int variant = 0;   // 0=不注入  1=注入80x1x80(原始尺寸)  2=注入44x0.5x44
    public string outPath;

    Camera _cam; RenderTexture _rt; Texture2D _tex;
    int _n; Color32[] _last;
    readonly List<float> _diffs = new List<float>();
    float _maxDiff; int _warmup = 25, _sample = 70;
    int _sx, _sy, _sw, _sh; StringBuilder _sb;
    GameObject _inj;

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
        foreach (var g in FindObjectsOfType<GameObject>())
        {
            if (g == null || g.transform.IsChildOf(_cam.transform)) continue;
            bool mv = g.CompareTag("Player") || g.CompareTag("Enemy")
                   || g.GetComponent<Animator>() != null || g.GetComponent<Rigidbody>() != null
                   || g.GetComponent<CharacterController>() != null;
            if (mv && g.activeSelf) g.SetActive(false);
        }

        var src = GameObject.Find("Environment/Ground");
        _sb.AppendLine($"========== a63 对照 v2  variant={variant} ==========");
        _sb.AppendLine("Environment/Ground = " + (src != null
            ? $"pos={src.transform.position:F3} scale={src.transform.lossyScale:F3}" : "未找到"));

        if (variant == 1)
        {
            // ★ 原 A48_Ground 的尺寸：80 × 1 × 80，顶面 y=0 ⇒ 中心 y=-0.5
            _inj = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _inj.name = "A63_Ground";
            _inj.transform.position = new Vector3(0f, -0.5f, 0f);
            _inj.transform.localScale = new Vector3(80f, 1f, 80f);
            _sb.AppendLine("★ 注入 80x1x80（原 A48_Ground 尺寸）中心 y=-0.5 → 顶面 y=0");
        }
        else if (variant == 2)
        {
            _inj = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _inj.name = "A63_Ground";
            var lp = src != null ? src.transform.localPosition : Vector3.zero;
            var ls = src != null ? src.transform.localScale : new Vector3(44f, 0.5f, 44f);
            _inj.transform.position = lp;
            _inj.transform.localScale = ls;
            _sb.AppendLine($"★ 注入与 Environment/Ground 同尺寸 pos={lp:F3} scale={ls:F3}");
        }

        int W = 640, H = 360;
        _rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32);
        _tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
        _sx = Mathf.RoundToInt(W * 0.20f); _sy = Mathf.RoundToInt(H * 0.45f);
        _sw = Mathf.RoundToInt(W * 0.60f); _sh = Mathf.RoundToInt(H * 0.40f);
        _sb.AppendLine($"采样窗 x[{_sx},{_sx + _sw}] y[{_sy},{_sy + _sh}]");
    }

    void LateUpdate()
    {
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
        int k = 0;
        for (int y = _sy; y < _sy + _sh; y++)
            for (int x = _sx; x < _sx + _sw; x++)
                strip[k++] = px[y * _rt.width + x];
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
        float p95 = s[Mathf.Clamp(Mathf.RoundToInt(s.Count * 0.95f), 0, s.Count - 1)];
        _sb.AppendLine();
        _sb.AppendLine($"平均帧间差 = {avg:F3}   中位={s[s.Count / 2]:F3}   95%={p95:F3}   最大={_maxDiff:F3}");
        _sb.AppendLine(avg < 1f
            ? (variant == 0 ? "⇒ ✓ 未注入时静止（基线）" : "⇒ 度量未抓到 z-fighting —— 度量不敏感")
            : $"⇒ ✓ 度量抓到了逐帧跳变 {avg:F3} —— 度量有效");
        File.WriteAllText(outPath, _sb.ToString());
        Debug.Log("[a63]\n" + _sb.ToString());
        if (_inj != null) DestroyImmediate(_inj);
        Destroy(gameObject);
    }
}

// ---- 顶层：变体 1（原尺寸 80x1x80）----
{
    var go = new GameObject("A63_Probe");
    var p = go.AddComponent<A63Probe>();
    p.variant = 1;
    p.outPath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Application.dataPath), "Tools/reports/a63_flicker_ab2.txt");
    Debug.Log("[a63] probe installed variant=1");
}
