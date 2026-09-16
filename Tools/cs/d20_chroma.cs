using System;
using System.Reflection;
using System.Text;
using UnityEngine;

// d20：A5 龙身彩度 / A6 敌我色相 —— **换一个可靠的取掩码方法**。
//
// d19 的教训：给龙换 Unlit 品红，渲染器确实换了材质，但截出来的帧里龙**纹丝不动**
//   （仍是水墨色）⇒ 掩码 0 px。根因怀疑是 URP 的 SRP Batcher / 材质实例未刷新。
//
// 这里改用**独立图层**：把龙挪到 31 层，相机 cullingMask 只留 31 层 ⇒ 渲染出来
//   非黑即龙，黑底就是天然掩码，完全不依赖材质替换。
public class d20_probe : MonoBehaviour
{
    Camera _cam;
    GameObject _dragon, _player;
    Component _dEl;
    Transform _modelRoot;
    int _step; float _t;
    RenderTexture _rt; int _w, _h;
    Texture2D _orig, _maskD, _origForPlayer, _maskP;
    int _savedMask;
    int _savedLayer;

    void Start()
    {
        _cam = Camera.main;
        _w = Screen.width; _h = Screen.height;

        Type t = null;
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        { t = asm.GetType("InkWash.Enemies.EnemyDragon"); if (t != null) break; }
        if (t == null) { Debug.Log("d20 ✗ 类型"); return; }

        var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/_Project/Prefabs/Enemies/Z_Enemy_MoLong.prefab");
        if (prefab == null) { Debug.Log("d20 ✗ 预制体"); return; }

        var pt = FindType("InkWash.Combat.PlayerRef");
        Vector3 pp = Vector3.zero;
        if (pt != null)
        {
            var pm = pt.GetProperty("Position", BindingFlags.Public | BindingFlags.Static);
            if (pm != null) pp = (Vector3)pm.GetValue(null);
            var fi = pt.GetField("Instance", BindingFlags.Public | BindingFlags.Static);
            if (fi != null) { var tr = fi.GetValue(null) as Transform; if (tr != null) _player = tr.root.gameObject; }
        }

        var go = Instantiate(prefab, pp + new Vector3(5f, 0.05f, 3f), Quaternion.identity);
        go.name = "D20_MoLong";
        _dragon = go;
        _dEl = go.GetComponentInChildren(t, true);
        var fLoop = t.GetField("aerialLoop");
        if (fLoop != null) fLoop.SetValue(_dEl, true);

        _cam.transform.position = pp + new Vector3(-7f, 5.0f, -9f);
        _cam.transform.LookAt(pp + new Vector3(1.5f, 2.5f, 0.5f));

        _rt = new RenderTexture(_w, _h, 24, RenderTextureFormat.ARGB32);
        _savedMask = _cam.cullingMask;
        Debug.Log(string.Format("d20 就位 {0}x{1} player={2} camMask=0x{3:X}",
            _w, _h, _player != null ? _player.name : "<null>", _savedMask));
    }

    void Update()
    {
        if (_cam == null) return;
        _t += Time.unscaledDeltaTime;
        if (_t < 4.5f) return;

        if (_modelRoot == null && _dEl != null)
        {
            var fr = _dEl.GetType().GetField("_modelRoot", BindingFlags.NonPublic | BindingFlags.Instance);
            if (fr != null) _modelRoot = fr.GetValue(_dEl) as Transform;
            if (_modelRoot != null) _cam.transform.LookAt(_modelRoot.position + Vector3.up * 0.6f);
        }

        if (_step == 0) { _step = 1; StartCoroutine(Capture()); }
    }

    System.Collections.IEnumerator Capture()
    {
        yield return new WaitForEndOfFrame();

        // ① 正常全场景原帧（用于取**颜色**）
        _orig = Cap(_cam.cullingMask);
        if (_player != null) _origForPlayer = _orig;

        // ② 只渲染龙：全场景踢到 0 层，龙保留 31 层 —— 但这会改变"龙在哪层"的事实，
        //    所以要先记录再还原。
        int savedDragonLayer = _dragon.layer;
        SetLayerRecursive(_dragon, 31);
        yield return new WaitForEndOfFrame();
        _maskD = Cap(1 << 31);            // 只留 31 层 ⇒ 黑底白龙，天然掩码
        SetLayerRecursive(_dragon, savedDragonLayer);

        // ③ 只渲染主角
        if (_player != null)
        {
            int savedPLayer = _player.layer;
            SetLayerRecursive(_player, 31);
            yield return new WaitForEndOfFrame();
            _maskP = Cap(1 << 31);
            SetLayerRecursive(_player, savedPLayer);
        }

        _cam.cullingMask = _savedMask;
        Analyse();
        Dump();

        _step = 2;
        enabled = false;
    }

    void Analyse()
    {
        var sb = new StringBuilder();
        sb.AppendLine("[D20] ===== A5 龙身彩度 / A6 敌我色相（策划案 §7） =====");

        var mkD = BuildMask(_maskD, out int hitDRaw);
        float sD = Chroma(_orig, mkD, out float sD90, out int hitD);
        sb.AppendLine(string.Format("[D20] A5 龙身掩码原始命中={0}px  有效(去黑)={1}px", hitDRaw, hitD));
        sb.AppendLine(string.Format("[D20] A5 龙身彩度: 均值={0:F4}  90%分位={1:F4}", sD, sD90));
        if (hitD < 800) sb.AppendLine("[D20] A5 ✗ 度量失效（有效命中 < 800 px）—— 结果不可信");
        else sb.AppendLine(string.Format("[D20] A5 判据 彩度均值 < 0.10 ⇒ {0}", sD < 0.10f ? "✓ 通过" : "✗ 不过"));

        if (_maskP != null)
        {
            var mkP = BuildMask(_maskP, out int hitPRaw);
            float sP = Chroma(_orig, mkP, out float sP90, out int hitP);
            sb.AppendLine(string.Format("[D20] 对照·主角掩码原始命中={0}px  有效={1}px", hitPRaw, hitP));
            sb.AppendLine(string.Format("[D20] 对照·主角彩度: 均值={0:F4}  90%分位={1:F4}", sP, sP90));

            float hD = Hue(_orig, mkD), hP = Hue(_orig, mkP);
            float diff = Mathf.Abs(Mathf.DeltaAngle(hD * 360f, hP * 360f));
            sb.AppendLine(string.Format("[D20] A6 龙均色相={0:F1}°  主角均色相={1:F1}°  色相差={2:F1}°",
                hD * 360f, hP * 360f, diff));
            if (hitP < 800) sb.AppendLine("[D20] A6 ✗ 度量失效（主角有效命中不足）");
            else sb.AppendLine(string.Format("[D20] A6 判据 色相差 > 90° ⇒ {0}", diff > 90f ? "✓ 通过" : "✗ 不过"));
        }
        else sb.AppendLine("[D20] A6 跳过：主角引用为空");

        Debug.Log(sb.ToString());
    }

    void Dump()
    {
        string dir = "D:/Unity Project/InkWash/Tools/screenshots/d20";
        System.IO.Directory.CreateDirectory(dir);
        Save(_orig, dir + "/d20_orig.png");
        Save(_maskD, dir + "/d20_maskDragon.png");
        if (_maskP != null) Save(_maskP, dir + "/d20_maskPlayer.png");
        Debug.Log("d20 证据图落盘 " + dir);
    }

    /// <summary>
    /// ★ 从"只渲染目标"的帧里抠掩码。
    ///
    /// 踩过两次坑，别再犯：
    ///   ① 用 `r+g+b > 60` 判非黑 —— 天空盒是**亮灰**，整屏 94% 都过，
    ///      掩码变成"全屏"，A6 色相差被稀释到 6.8°（假失败）。
    ///   ② 天空是**均匀亮背景**、目标是**深色剪影** ⇒ 正解是取**暗度分位**：
    ///      亮度低于 该帧亮度中位数 − 余量 的像素才算目标。
    /// </summary>
    bool[] BuildMask(Texture2D onlyTarget, out int hit)
    {
        hit = 0;
        if (onlyTarget == null) return null;
        var px = onlyTarget.GetPixels32();
        int n = px.Length;
        var lum = new float[n];
        var sorted = new float[n];
        for (int i = 0; i < n; i++)
        {
            float l = (px[i].r * 0.2126f + px[i].g * 0.7152f + px[i].b * 0.0722f) / 255f;
            lum[i] = l; sorted[i] = l;
        }
        Array.Sort(sorted);
        float median = sorted[n / 2];
        // 目标（深色剪影）比背景暗 ⇒ 阈值取中位数 − 0.10
        float thr = median - 0.10f;
        var mask = new bool[n];
        for (int i = 0; i < n; i++)
        {
            mask[i] = lum[i] < thr;
            if (mask[i]) hit++;
        }
        Debug.Log(string.Format("d20 背景亮度中位={0:F3} 掩码阈值={1:F3} 命中={2}px ({3:P1})",
            median, thr, hit, hit / (float)n));
        return mask;
    }

    float Chroma(Texture2D tex, bool[] mask, out float p90, out int hit)
    {
        p90 = 0f; hit = 0;
        if (tex == null || mask == null) return 0f;
        var px = tex.GetPixels32();
        int n = Mathf.Min(px.Length, mask.Length);
        var list = new System.Collections.Generic.List<float>();
        float sum = 0f;
        for (int i = 0; i < n; i++)
        {
            if (!mask[i]) continue;
            Color c = px[i];
            Color.RGBToHSV(c, out float h, out float s, out float v);
            if (v < 0.08f) continue;
            sum += s; list.Add(s); hit++;
        }
        if (hit == 0) return 0f;
        list.Sort();
        p90 = list[Mathf.Clamp(Mathf.RoundToInt(list.Count * 0.9f), 0, list.Count - 1)];
        return sum / hit;
    }

    float Hue(Texture2D tex, bool[] mask)
    {
        if (tex == null || mask == null) return 0f;
        var px = tex.GetPixels32();
        int n = Mathf.Min(px.Length, mask.Length);
        float sx = 0f, sy = 0f; int cnt = 0;
        for (int i = 0; i < n; i++)
        {
            if (!mask[i]) continue;
            Color c = px[i];
            Color.RGBToHSV(c, out float h, out float s, out float v);
            if (s < 0.06f || v < 0.08f) continue;
            float a = h * 2f * Mathf.PI;
            sx += Mathf.Cos(a); sy += Mathf.Sin(a); cnt++;
        }
        if (cnt == 0 || (Mathf.Abs(sx) < 1e-6f && Mathf.Abs(sy) < 1e-6f)) return 0f;
        float mean = Mathf.Atan2(sy, sx) / (2f * Mathf.PI);
        if (mean < 0f) mean += 1f;
        return mean;
    }

    Texture2D Cap(int mask)
    {
        _cam.cullingMask = mask;
        var prev = _cam.targetTexture;
        _cam.targetTexture = _rt;
        _cam.Render();
        _cam.targetTexture = prev;
        RenderTexture.active = _rt;
        var tex = new Texture2D(_w, _h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, _w, _h), 0, 0);
        tex.Apply();
        RenderTexture.active = null;
        _cam.cullingMask = _savedMask;
        return tex;
    }

    void Save(Texture2D t, string p) { if (t != null) System.IO.File.WriteAllBytes(p, t.EncodeToPNG()); }

    static void SetLayerRecursive(GameObject go, int layer)
    {
        go.layer = layer;
        foreach (var tr in go.GetComponentsInChildren<Transform>(true)) tr.gameObject.layer = layer;
    }

    static Type FindType(string full)
    {
        var t = Type.GetType(full + ", Assembly-CSharp");
        if (t != null) return t;
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        { t = asm.GetType(full); if (t != null) return t; }
        return null;
    }
}

var g20 = new GameObject("D20_Probe");
g20.AddComponent<d20_probe>();
return "D20_STARTED";
