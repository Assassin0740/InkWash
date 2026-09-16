using System;
using System.Reflection;
using System.Text;
using UnityEngine;

// d19：A5 龙身彩度 + A6 敌我色相分离（策划院 §7）
// 做法：把龙与主角各用「整体换 URP/Unlit 品红」对比帧取掩码，
//      再在**原帧**上按掩码统计 HSV。掩码必须同 tick 背靠背 Render（一 yield 就错 1~2 px）。
public class d19_probe : MonoBehaviour
{
    Camera _cam;
    GameObject _dragon; Component _dEl;
    Transform _modelRoot;
    int _step; float _t;
    RenderTexture _rt;
    Texture2D _orig, _maskD, _maskP, _maskBoth;
    int _w, _h;

    void Start()
    {
        _cam = Camera.main;
        _w = Screen.width; _h = Screen.height;

        Type t = null;
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        { t = asm.GetType("InkWash.Enemies.EnemyDragon"); if (t != null) break; }
        if (t == null) { Debug.Log("d19 ✗ 类型"); return; }

        var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/_Project/Prefabs/Enemies/Z_Enemy_MoLong.prefab");
        if (prefab == null) { Debug.Log("d19 ✗ 预制体"); return; }

        var pt = FindType("InkWash.Combat.PlayerRef");
        Vector3 pp = Vector3.zero; GameObject player = null;
        if (pt != null)
        {
            var pm = pt.GetProperty("Position", BindingFlags.Public | BindingFlags.Static);
            if (pm != null) pp = (Vector3)pm.GetValue(null);
            // ★ PlayerRef.Instance 是 **public static 字段**（不是属性）
            var fi = pt.GetField("Instance", BindingFlags.Public | BindingFlags.Static);
            if (fi != null) { var tr = fi.GetValue(null) as Transform; if (tr != null) player = tr.root.gameObject; }
            if (player == null)
            {
                var fg = pt.GetField("GameObject", BindingFlags.Public | BindingFlags.Static);
                if (fg != null) player = fg.GetValue(null) as GameObject;
            }
        }

        var go = Instantiate(prefab, pp + new Vector3(5f, 0.05f, 3f), Quaternion.identity);
        go.name = "D19_MoLong";
        _dragon = go;
        _dEl = go.GetComponentInChildren(t, true);
        var fLoop = t.GetField("aerialLoop");
        if (fLoop != null) fLoop.SetValue(_dEl, true);

        _cam.transform.position = pp + new Vector3(-6f, 4.5f, -8f);
        _cam.transform.LookAt(pp + new Vector3(1.5f, 2.0f, 0.5f));

        _player = player;
        _rt = new RenderTexture(_w, _h, 24, RenderTextureFormat.ARGB32);
        _t = 0f;
        _step = 0;
        Debug.Log("d19 就位 w=" + _w + " h=" + _h + " player=" + (player != null ? player.name : "<null>"));
    }

    GameObject _player;

    void Update()
    {
        if (_cam == null) return;
        _t += Time.unscaledDeltaTime;
        if (_t < 4f) return;   // 等它起飞

        // ★ 追的必须是**模型容器**（_modelRoot），不是 SkinnedMeshRenderer 的 transform
        //   —— 后者是骨架节点，位置跟实际龙身差很远 ⇒ 相机会"追着空气"，
        //   出图时龙在画面外、掩码 0 px（d19 第一轮就踩了这个坑）。
        if (_modelRoot == null)
        {
            var fr = _dEl != null ? _dEl.GetType().GetField("_modelRoot", BindingFlags.NonPublic | BindingFlags.Instance) : null;
            if (fr != null) _modelRoot = fr.GetValue(_dEl) as Transform;
        }
        if (_modelRoot != null) _cam.transform.LookAt(_modelRoot.position + Vector3.up * 0.8f);

        if (_step == 0)
        {
            _step = 1;
            StartCoroutine(Capture());
        }
    }

    System.Collections.IEnumerator Capture()
    {
        yield return new WaitForEndOfFrame();

        // 1) 原帧
        _orig = Cap();

        // 2) 龙 → 品红，取龙的掩码
        var dRend = _dragon.GetComponentsInChildren<Renderer>(true);
        var backup = SaveMats(dRend);
        Tint(dRend, new Color(1f, 0f, 1f));
        yield return new WaitForEndOfFrame();
        _maskD = Cap();
        RestoreMats(dRend, backup);

        // 3) 主角 → 品红，取主角掩码
        Renderer[] pRend = new Renderer[0];
        if (_player != null)
        {
            pRend = _player.GetComponentsInChildren<Renderer>(true);
            var pb = SaveMats(pRend);
            Tint(pRend, new Color(1f, 0f, 1f));
            yield return new WaitForEndOfFrame();
            _maskP = Cap();
            RestoreMats(pRend, pb);
        }

        Analyse();

        // ★ 落盘三张证据图：原帧 / 龙掩码 / 主角掩码。掩码失效时肉眼就能看出龙在不在画面里。
        string dir = "D:/Unity Project/InkWash/Tools/screenshots/d19";
        System.IO.Directory.CreateDirectory(dir);
        SavePng(_orig, dir + "/d19_orig.png");
        SavePng(_maskD, dir + "/d19_maskDragon.png");
        if (_maskP != null) SavePng(_maskP, dir + "/d19_maskPlayer.png");

        _step = 2;
        Debug.Log("d19 完成，证据图落盘 " + dir);
        enabled = false;
    }

    void Analyse()
    {
        var sb = new StringBuilder();
        sb.AppendLine("[D19] ===== A5 龙身彩度 / A6 敌我色相 (策划案 §7) =====");

        float sD = ChromaStats(_orig, _maskD, out float sD90, out float grayDRatio, out int hitD, out int rawD);
        sb.AppendLine(string.Format("[D19] A5 龙身掩码命中: 原始={0}px  有效={1}px", rawD, hitD));
        sb.AppendLine(string.Format("[D19] A5 龙身彩度: 均值={0:F4}  90%分位={1:F4}  近灰像素占比={2:P1}",
            sD, sD90, grayDRatio));
        if (hitD < 500)
            sb.AppendLine("[D19] A5 ✗ 度量失效（掩码命中不足 500 px）—— **结果不可信**，先修掩码");
        else
            sb.AppendLine(string.Format("[D19] A5 判据 彩度均值 < 0.10 ⇒ {0}", sD < 0.10f ? "✓ 通过" : "✗ 不过"));

        if (_maskP != null)
        {
            float sP = ChromaStats(_orig, _maskP, out float sP90, out float grayPRatio, out int hitP, out int rawP);
            sb.AppendLine(string.Format("[D19] 对照·主角掩码命中: 原始={0}px  有效={1}px", rawP, hitP));
            sb.AppendLine(string.Format("[D19] 对照·主角彩度: 均值={0:F4}  90%分位={1:F4}  近灰占比={2:P1}",
                sP, sP90, grayPRatio));

            float hueD = MeanHue(_orig, _maskD);
            float hueP = MeanHue(_orig, _maskP);
            float hueDiff = Mathf.Abs(Mathf.DeltaAngle(hueD * 360f, hueP * 360f));
            sb.AppendLine(string.Format("[D19] A6 龙均色相={0:F1}°  主角均色相={1:F1}°  色相差={2:F1}°",
                hueD * 360f, hueP * 360f, hueDiff));
            if (hitP < 500) sb.AppendLine("[D19] A6 ✗ 度量失效（主角掩码命中不足）");
            else sb.AppendLine(string.Format("[D19] A6 判据 色相差 > 90° ⇒ {0}", hueDiff > 90f ? "✓ 通过" : "✗ 不过"));
        }
        else sb.AppendLine("[D19] A6 跳过（主角引用为空）");

        Debug.Log(sb.ToString());
    }

    /// <summary>
    /// ★ 掩码统计 + **自检**。血泪教训（硬规矩 #3）：只报"彩度 0.0000 / 近灰 0.0%"
    ///   看着像满分，其实是**掩码一个像素都没匹配上**。所以这里必须先报
    ///   「掩码命中多少个像素」，命中数为 0 直接标 ✗ 度量失效，不许算通过。
    /// </summary>
    float ChromaStats(Texture2D tex, Texture2D mask, out float p90, out float grayRatio, out int hit, out int rawHit)
    {
        p90 = 0f; grayRatio = 0f; hit = 0; rawHit = 0;
        if (tex == null || mask == null) return 0f;
        var px = tex.GetPixels32();
        var mk = mask.GetPixels32();
        int n = Mathf.Min(px.Length, mk.Length);
        float sum = 0f; int cnt = 0, gray = 0;
        var list = new System.Collections.Generic.List<float>();
        for (int i = 0; i < n; i++)
        {
            // 品红判据放宽：URP/Unlit 出来的是 (255,0,255) 附近
            bool mag = mk[i].r > 120 && mk[i].b > 120 && mk[i].g < 140 && mk[i].r > mk[i].g + 40 && mk[i].b > mk[i].g + 40;
            if (!mag) continue;
            rawHit++;
            Color c = px[i];
            Color.RGBToHSV(c, out float h, out float s, out float v);
            if (v < 0.08f) continue;       // 纯黑无意义
            sum += s; cnt++; list.Add(s);
            if (s < 0.10f) gray++;
        }
        hit = cnt;
        if (cnt == 0) return 0f;
        list.Sort();
        p90 = list[Mathf.Clamp(Mathf.RoundToInt(list.Count * 0.9f), 0, list.Count - 1)];
        grayRatio = gray / (float)cnt;
        return sum / cnt;
    }

    float MeanHue(Texture2D tex, Texture2D mask)
    {
        if (tex == null || mask == null) return 0f;
        var px = tex.GetPixels32();
        var mk = mask.GetPixels32();
        int n = Mathf.Min(px.Length, mk.Length);
        float sx = 0f, sy = 0f;
        for (int i = 0; i < n; i++)
        {
            if (!(mk[i].r > 160 && mk[i].b > 160 && mk[i].g < 90)) continue;
            Color c = px[i];
            Color.RGBToHSV(c, out float h, out float s, out float v);
            if (s < 0.06f || v < 0.08f) continue;
            float a = h * 2f * Mathf.PI;
            sx += Mathf.Cos(a); sy += Mathf.Sin(a);
        }
        if (Mathf.Abs(sx) < 1e-6f && Mathf.Abs(sy) < 1e-6f) return 0f;
        float mean = Mathf.Atan2(sy, sx) / (2f * Mathf.PI);
        if (mean < 0f) mean += 1f;
        return mean;
    }

    Texture2D Cap()
    {
        var prev = _cam.targetTexture;
        _cam.targetTexture = _rt;
        _cam.Render();
        _cam.targetTexture = prev;
        RenderTexture.active = _rt;
        var tex = new Texture2D(_w, _h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, _w, _h), 0, 0);
        tex.Apply();
        RenderTexture.active = null;
        return tex;
    }

    void SavePng(Texture2D t, string path)
    {
        if (t == null) return;
        System.IO.File.WriteAllBytes(path, t.EncodeToPNG());
    }

    Material[][] SaveMats(Renderer[] rs)
    {
        var b = new Material[rs.Length][];
        for (int i = 0; i < rs.Length; i++) b[i] = rs[i].sharedMaterials;
        return b;
    }
    void RestoreMats(Renderer[] rs, Material[][] b)
    {
        for (int i = 0; i < rs.Length; i++) rs[i].sharedMaterials = b[i];
    }    void Tint(Renderer[] rs, Color c)
    {
        var unlit = Shader.Find("Universal Render Pipeline/Unlit");
        if (unlit == null) unlit = Shader.Find("Unlit/Color");
        if (unlit == null) { Debug.LogError("d19 ✗ 找不到 Unlit shader，掩码必然失效"); return; }
        _tintMat = new Material(unlit);
        if (_tintMat.HasProperty("_BaseColor")) _tintMat.SetColor("_BaseColor", c);
        if (_tintMat.HasProperty("_Color")) _tintMat.SetColor("_Color", c);
        if (_tintMat.HasProperty("_Surface")) _tintMat.SetFloat("_Surface", 0f);   // Opaque
        _tintMat.color = c;

        int n = 0, mats = 0;
        foreach (var r in rs)
        {
            var arr = new Material[r.sharedMaterials.Length];
            for (int i = 0; i < arr.Length; i++) arr[i] = _tintMat;
            // ★ 用 `materials`（实例数组）而不是 `sharedMaterials`：
            //   sharedMaterials 会改到**资产**上，且 URP 的 SRP Batcher 会沿用旧批次，
            //   实测龙（SkinnedMeshRenderer）换了 sharedMaterials 但画面上**纹丝不动**，
            //   导致掩码 0~11 px 却看着像"统计通过"。materials 强制实例化，立刻生效。
            r.materials = arr;
            mats += arr.Length;
            n++;
            Debug.Log(string.Format("d19   · 渲染器[{0}] {1} / {2} / enabled={3} / active={4} / 倍率={5}",
                n, r.GetType().Name, r.name, r.enabled, r.gameObject.activeInHierarchy,
                r.transform.lossyScale.x.ToString("F3")));
        }
        Debug.Log(string.Format("d19 Tint 改了 {0} 个渲染器 / {1} 个材质槽（shader={2}）", n, mats, unlit.name));
    }

    private Material _tintMat;

    static Type FindType(string full)
    {
        var t = Type.GetType(full + ", Assembly-CSharp");
        if (t != null) return t;
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        { t = asm.GetType(full); if (t != null) return t; }
        return null;
    }
}

var g19 = new GameObject("D19_Probe");
g19.AddComponent<d19_probe>();
return "D19_STARTED";
