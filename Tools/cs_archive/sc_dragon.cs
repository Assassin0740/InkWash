using System;
using System.IO;
using System.Reflection;
using UnityEngine;

// sc_dragon：只拍墨龙的四个条目，用来快速迭代龙的取景与相位。
public class sc_dragon : MonoBehaviour
{
    Camera _cam;
    int _step; float _t;
    Component _showcase;
    MethodInfo _mPlay;
    string _dir;

    void Start()
    {
        _cam = Camera.main;
        if (_cam == null) { Debug.Log("sc_dragon ✗ 没有主相机"); enabled = false; return; }
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            var t = asm.GetType("InkWash.DebugTools.ActionShowcase");
            if (t != null) { foreach (var c in FindObjectsOfType(t)) { _showcase = c as Component; break; } if (_showcase != null) break; }
        }
        if (_showcase == null) { Debug.Log("sc_dragon ✗ 没有 ActionShowcase"); enabled = false; return; }
        _mPlay = _showcase.GetType().GetMethod("PlayForCapture", BindingFlags.Public | BindingFlags.Instance);
        _dir = "D:/Unity Project/InkWash/Tools/screenshots/dragon";
        Directory.CreateDirectory(_dir);
        Debug.Log("sc_dragon 就位");
    }

    //  17 盘旋 / 18 撕咬 / 19 扫尾 / 20 吐息
    int[] _targets = { 17, 18, 19, 20 };
    float[] _lead = { 1.2f, 2.2f, 2.2f, 2.2f };
    int _shot_i;
    float _itemAt, _shotAt;

    void Update()
    {
        _t += Time.unscaledDeltaTime;

        if (_step < _targets.Length && (_step == 0 ? _t > 1.5f : _t > _itemAt + 5.2f))
        {
            if (_mPlay != null) _mPlay.Invoke(_showcase, new object[] { _targets[_step] });
            _itemAt = _t; _shot_i = 0;
            _shotAt = _t + _lead[_step];
            Debug.Log("sc_dragon ▶ 条目 " + _targets[_step]);
            _step++;
        }

        // 每条拍 5 张、间隔 0.55 s ⇒ 覆盖 2.2 s 的动作高潮
        if (_step > 0 && _step <= _targets.Length && _shot_i < 5 && _t >= _shotAt)
        {
            int idx = _targets[_step - 1];
            StartCoroutine(Shot(idx * 100 + (_shot_i + 1)));
            _shot_i++;
            _shotAt += 0.55f;
        }

        if (_step >= _targets.Length && _t > _itemAt + 5.6f)
        {
            Debug.Log("sc_dragon 完成");
            enabled = false;
        }
    }

    System.Collections.IEnumerator Shot(int tag)
    {
        yield return new WaitForEndOfFrame();
        yield return new WaitForEndOfFrame();
        int w = Screen.width, h = Screen.height;
        var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
        var prev = _cam.targetTexture;
        _cam.targetTexture = rt;
        _cam.Render();
        _cam.targetTexture = prev;
        RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        tex.Apply();
        RenderTexture.active = null;
        File.WriteAllBytes(string.Format("{0}/dg_{1:000}.png", _dir, tag), tex.EncodeToPNG());
        Destroy(tex); Destroy(rt);
        Debug.Log("sc_dragon 已出图 dg_" + tag + ".png");
    }
}

var g = new GameObject("sc_dragon");
g.AddComponent<sc_dragon>();
return "SC_DRAGON_STARTED";
