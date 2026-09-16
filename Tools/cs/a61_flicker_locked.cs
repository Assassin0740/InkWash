// a61_flicker_locked.cs —— 锁定相机 + 逐帧像素对比，量化地面闪烁
//
// a58/a59 证明 A48_Ground 与 Environment/Ground 顶面 100% 共面（z-fighting），已删。
// 但 z-fighting 是「逐帧抖动」——必须：
//   ① 相机完全锁死（不然画面在动，任何逐帧差分都无意义）
//   ② 场景里没有任何会动的物体（不然分不清是物体动还是地面闪）
//   ③ 逐帧背靠背比同一块地面的像素
//
// ★ Codely --runtime 禁止阻塞调用（Thread.Sleep 等被静态检查拦），
//   所以长时间采样必须挂 MonoBehaviour 探针，在 Update() 里逐帧采。
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

public class A61Probe : MonoBehaviour
{
    public int   warmupFrames = 30;    // 先跑几帧让一切稳定
    public int   sampleFrames = 90;    // 采样帧数
    public string outPath;

    Camera _cam;
    int _n;
    RenderTexture _rt;
    Texture2D _tex;
    readonly List<Color32[]> _prevStrip = new List<Color32[]>();
    readonly List<float> _diffs = new List<float>();
    Color32[] _last = null;
    float _maxDiff = 0f;
    StringBuilder _sb;

    // 地面采样窗（屏幕坐标），避开 HUD 与任何物体
    int _sx, _sy, _sw, _sh;

    void Start()
    {
        _sb = new StringBuilder();
        _cam = Camera.main != null ? Camera.main : FindObjectOfType<Camera>();
        _sb.AppendLine("========== a61 锁定相机闪烁量化 ==========");

        // ① 相机锁死：移除一切抢相机的组件
        foreach (var mb in _cam.GetComponents<MonoBehaviour>())
        {
            if (mb == null) continue;
            string tn = mb.GetType().Name;
            if (tn.Contains("ShowcaseCam") || tn.Contains("Brain") || tn.Contains("Follow"))
            { DestroyImmediate(mb); _sb.AppendLine("移除抢相机组件: " + tn); }
        }
        _cam.transform.position = new Vector3(0f, 3.0f, -9f);
        _cam.transform.LookAt(new Vector3(0f, 0f, 4f));
        _cam.fieldOfView = 45f;

        // ② 场景里不许有会动的东西
        int disabled = 0;
        foreach (var g in FindObjectsOfType<GameObject>())
        {
            if (g == null || g.transform.IsChildOf(_cam.transform)) continue;
            // 玩家 / 敌人 / 墨弹 / 任何带 Animator 或 Rigidbody 的
            bool moving = g.CompareTag("Player") || g.CompareTag("Enemy")
                       || g.GetComponent<Animator>() != null
                       || g.GetComponent<Rigidbody>() != null
                       || g.GetComponent<CharacterController>() != null;
            if (moving && g.activeSelf) { g.SetActive(false); disabled++; }
        }
        _sb.AppendLine($"已停用会动的对象 {disabled} 个（保证画面只有静态地面）");
        _sb.AppendLine($"相机 = {_cam.transform.position:F2} → lookAt (0,0,4)  fov=45");

        // ③ 建采样 RT
        int W = 640, H = 360;
        _rt  = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32);
        _tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
        // 屏幕中下部一大片纯地面
        _sx = Mathf.RoundToInt(W * 0.20f); _sy = Mathf.RoundToInt(H * 0.45f);
        _sw = Mathf.RoundToInt(W * 0.60f); _sh = Mathf.RoundToInt(H * 0.40f);
        _sb.AppendLine($"采样窗 = x[{_sx},{_sx + _sw}] y[{_sy},{_sy + _sh}]  ({_sw}x{_sh} px)");
        _sb.AppendLine($"预热 {warmupFrames} 帧后开始采样 {sampleFrames} 帧");
        _sb.AppendLine("先跑帧...");
    }

    void LateUpdate()
    {
        if (_n < warmupFrames) { _n++; return; }
        if (_diffs.Count >= sampleFrames) return;

        if (_cam.targetTexture != _rt)
        {
            _cam.targetTexture = _rt;
            _cam.Render();                     // 背靠背渲染，绝不让出一帧
            RenderTexture.active = _rt;
            _tex.ReadPixels(new Rect(0, 0, _rt.width, _rt.height), 0, 0);
            _tex.Apply(false, false);
            RenderTexture.active = null;
            _cam.targetTexture = null;
        }

        var px = _tex.GetPixels32();
        var strip = new Color32[_sw * _sh];
        int k = 0;
        for (int y = _sy; y < _sy + _sh; y++)
            for (int x = _sx; x < _sx + _sw; x++)
                strip[k++] = px[y * _rt.width + x];

        if (_last != null)
        {
            // 逐像素灰度差的绝对值均值
            double sum = 0;
            for (int i = 0; i < strip.Length; i++)
            {
                int lg = (_last[i].r * 77 + _last[i].g * 150 + _last[i].b * 29) >> 8;
                int cg = (strip[i].r * 77 + strip[i].g * 150 + strip[i].b * 29) >> 8;
                sum += Mathf.Abs(cg - lg);
            }
            float d = (float)(sum / strip.Length);
            _diffs.Add(d);
            if (d > _maxDiff) _maxDiff = d;
        }
        _last = strip;

        if (_diffs.Count >= sampleFrames) Finish();
        else { if (_diffs.Count == 1) _sb.AppendLine($"采样进行中 首帧差={_diffs[0]:F3}"); }
    }

    void Finish()
    {
        double sum = 0; foreach (var d in _diffs) sum += d;
        float avg = (float)(sum / _diffs.Count);
        var sorted = new List<float>(_diffs); sorted.Sort();
        float med = sorted[sorted.Count / 2];
        float p95 = sorted[Mathf.Clamp(Mathf.RoundToInt(sorted.Count * 0.95f), 0, sorted.Count - 1)];

        _sb.AppendLine();
        _sb.AppendLine("========== 结果（相邻帧同像素灰度差） ==========");
        _sb.AppendLine($"平均帧间差 = {avg:F3}   (0~255 灰阶)");
        _sb.AppendLine($"中位帧间差 = {med:F3}");
        _sb.AppendLine($"95% 分位   = {p95:F3}");
        _sb.AppendLine($"最大帧间差 = {_maxDiff:F3}");
        _sb.AppendLine($"样本数     = {_diffs.Count}");
        _sb.AppendLine();
        // 判据：完全静止的场景，帧间差应≈0（只有 URP 抖动/后处理噪声，通常 < 1）
        if (avg < 1.0f)
            _sb.AppendLine($"⇒ 平均帧间差 {avg:F3} < 1.0   ✓ 地面像素逐帧一致，z-fighting 已消除");
        else if (avg < 3.0f)
            _sb.AppendLine($"⇒ 平均帧间差 {avg:F3} ∈ [1,3)  ~ 轻微残留");
        else
            _sb.AppendLine($"⇒ 平均帧间差 {avg:F3} >= 3.0   ✗ 地面仍在逐帧跳变");

        File.WriteAllText(outPath, _sb.ToString());
        Debug.Log("[a61]\n" + _sb.ToString());
        Destroy(gameObject);
    }
}

// ---- 顶层语句：装探针 ----
{
    var go = new GameObject("A61_Probe");
    var p = go.AddComponent<A61Probe>();
    p.outPath = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Tools/reports/a61_flicker.txt");
    Debug.Log("[a61] probe installed → 需要进 Play 模式才会跑");
}
