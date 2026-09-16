using System;
using System.IO;
using System.Reflection;
using UnityEngine;

// sc_grab：演示场的**截图器**（桥的 PNG 接口被禁用，这里自己 RenderTexture 落盘）
//
// ★★ 时间尺度是这里的关键：
//   演示场的动作是**分阶段演进**的（龙：0.8 s 摆位 → 开招 → 预兆 0.35 → 俯冲 0.45
//   → 打击 0.22 …）。若在 `Play()` 之后两帧就出图，拍到的是**开场那一瞬**，
//   所有条目看起来都一样（实测踩到：盘旋/撕咬/吐息三张图完全一致）。
//   所以每条目给足 `lead` 秒再拍，且**每条拍 3 张**（起始 / 中段 / 后段），
//   这样即使相位对不准，也总有一张落在动作高潮上。
public class sc_grab : MonoBehaviour
{
    Camera _cam;
    int _step; float _t;
    Component _showcase;
    MethodInfo _mPlay;
    string _dir;

    void Start()
    {
        _cam = Camera.main;
        if (_cam == null) { Debug.Log("sc_grab ✗ 没有主相机"); enabled = false; return; }

        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            var t = asm.GetType("InkWash.DebugTools.ActionShowcase");
            if (t != null) { _showcase = FindOne(t); if (_showcase != null) break; }
        }
        if (_showcase == null) { Debug.Log("sc_grab ✗ 场景里没有 ActionShowcase"); enabled = false; return; }

        _mPlay = _showcase.GetType().GetMethod("PlayForCapture", BindingFlags.Public | BindingFlags.Instance);
        _dir = "D:/Unity Project/InkWash/Tools/screenshots/showcase";
        Directory.CreateDirectory(_dir);
        Debug.Log("sc_grab 就位");
    }

    Component FindOne(Type t)
    {
        foreach (var c in FindObjectsOfType(t)) return c as Component;
        return null;
    }

    // 条目索引 → 拍几张、间隔多少、首次延迟多少
    class Job { public int idx; public float lead; public int count; public float gap;
                public Job(int i, float l, int c, float g) { idx = i; lead = l; count = c; gap = g; } }

    //  ★ 索引对照（CollectItems 的顺序）：
    //    0 Idle 1 Walk 2 Run 3 Dash 4 Atk1 5 Atk2 6 Atk3 7 Heavy
    //    8..10 墨徒(待机/追击/攻击)  11..13 墨偶  14..16 墨魇
    //    17 盘旋 18 撕咬 19 扫尾 20 吐息  21 溅墨 22 墨晕
    Job[] _jobs = {
        new Job(2,  1.6f, 2, 0.9f),   // 跑
        new Job(3,  0.6f, 3, 0.35f),  // 冲刺（位移快，要密拍）
        new Job(6,  0.5f, 3, 0.25f),  // 连招三段
        new Job(7,  0.5f, 3, 0.30f),  // 重击
        new Job( 8, 2.2f, 1, 0f),     // 墨徒 待机
        new Job( 9, 2.6f, 3, 1.0f),   // 墨徒 追击
        new Job(10, 2.0f, 3, 0.55f),  // 墨徒 攻击
        new Job(12, 2.6f, 2, 1.0f),   // 墨偶 追击
        new Job(13, 2.0f, 3, 0.55f),  // 墨偶 攻击
        new Job(17, 2.2f, 2, 1.6f),   // 龙 盘旋
        new Job(18, 2.4f, 4, 0.60f),  // 龙 俯冲撕咬（预兆→俯冲→咬→拉起）
        new Job(19, 2.4f, 4, 0.60f),  // 龙 俯冲扫尾
        new Job(20, 2.4f, 4, 0.60f),  // 龙 吐息
    };

    float _shotAt;       // 下一次出图时刻（本条目内相对时间）
    float _itemAt;       // 本条目开始时刻
    int _shot_i;         // 本条目已拍几张

    void Update()
    {
        _t += Time.unscaledDeltaTime;

        Job job = (_step < _jobs.Length) ? _jobs[_step] : null;

        if (job != null && (_step == 0 ? _t > 1.5f : _t > _itemAt + job.lead + job.count * job.gap + 0.6f))
        {
            if (_mPlay != null) _mPlay.Invoke(_showcase, new object[] { job.idx });
            _itemAt = _t; _shot_i = 0; _shotAt = _t + job.lead;
            Debug.Log("sc_grab ▶ 条目 " + job.idx + "（拍 " + job.count + " 张）");
            _step++;
        }

        if (job != null && _shot_i < job.count && _t >= _shotAt)
        {
            int n = job.idx * 100 + (_shot_i + 1);   // 文件名 sc_<条目>_<第几张>，不覆盖
            StartCoroutine(Shot(n));
            _shot_i++;
            _shotAt += job.gap;
        }

        if (_step >= _jobs.Length && _t > _itemAt + 6f)
        {
            Debug.Log("sc_grab 完成，共 " + _jobs.Length + " 个条目");
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
        File.WriteAllBytes(string.Format("{0}/sc_{1:000}.png", _dir, tag), tex.EncodeToPNG());
        Destroy(tex); Destroy(rt);
        Debug.Log("sc_grab 已出图 sc_" + tag + ".png");
    }
}

var g = new GameObject("sc_grab");
g.AddComponent<sc_grab>();
return "SC_GRAB_STARTED";
