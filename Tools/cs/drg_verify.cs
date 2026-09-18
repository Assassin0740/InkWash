using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;

// drg_verify.cs —— 墨龙「运动核」改造验收 + 录像
//
// 用户诉求：「龙的这个移动，不像是真的龙，很僵硬，没有按照 S 形去扭动，不像蛇」
// 定案参数：1.5 个波 · 35°/节 · 垂直 46% · 0.27 Hz · 头增益 0.55 · 路径蛇形 30° ×3 波
//
// ■ 判据（都是"对这次改动敏感"的量，不是随手挑的）
//   ① 身体弯了多少 —— 取**链相对「尾→头弦」的最大垂距**。
//      不能用「每节 localRotation 绕角」：驱动写的是世界旋转，行波下相邻节几乎同相，
//      局部绕角会互相抵消 ⇒ 实测只有真值的 1/5（本项目踩过，见坑表）。
//      弦垂距是几何量，跟旋转怎么写无关。
//   ② 立体度 —— 链的 Y 跨度（垂直）与 XZ 跨度之比。旧值近似纯水平 ⇒ 垂直跨度≈0。
//   ③ 路径是不是蛇形 —— 记录 actor 的 XZ 轨迹，算「到玩家的半径」的波动幅度。
//      纯绕圈 ⇒ 半径波动≈0；蛇形前进 ⇒ 半径明显起伏。
//   ④ 保险丝 —— 身长（尾→头距离）必须仍在 7~9 m。链若被拧到自交/塌缩，这个会掉。
//
// ■ 录像
//   用 Time.captureFramerate = 30 把每帧步长钉死成 1/30 s ⇒ 录像与编辑器实际帧率无关。
//   相机跟随龙的位置、但**朝向固定在世界空间**（不锁龙的前方），这样能看清身体轮廓。
//   ★ 桥的 PNG 截图接口被禁用 ⇒ 自己 Camera.Render() + RenderTexture + EncodeToPNG。
//
// ★ 脚本形态：Codely 是 Roslyn script，必须有顶层语句；运行时探针要在末尾 new 一个宿主对象。

public class drg_verify : MonoBehaviour
{
    const string RP  = "D:/Unity Project/InkWash/Tools/reports/drg_verify.txt";
    const string SD  = "D:/Unity Project/InkWash/Tools/screenshots/enemies/DRV";
    const string KEY = "墨龙";

    const int FPS = 30;
    const int NF  = 240;      // 8.0 s —— 覆盖约 2.2 个身体波周期，且跨过「静默段」边界

    readonly StringBuilder _sb = new StringBuilder();
    Component _sc; Type _ty, _dt;
    MethodInfo _mPlay, _mLabel, _mStop;
    PropertyInfo _pCount;
    Camera _cam;
    GameObject _go;
    Transform _actor, _vroot;
    IList _spine, _baseRel;

    int _saved;

    static readonly BindingFlags BF = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;

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

        _mPlay  = _ty.GetMethod("PlayForCapture", BF);
        _mLabel = _ty.GetMethod("ItemLabel", BF);
        _mStop  = _ty.GetMethod("StopAutoClose", BF);
        _pCount = _ty.GetProperty("ItemCount", BF);

        int count = (int)_pCount.GetValue(_sc, null);
        int idx = -1;
        string found = "";
        for (int i = 0; i < count; i++)
        {
            string lb = (string)_mLabel.Invoke(_sc, new object[] { i });
            if (lb.Contains(KEY) && lb.Contains("盘旋")) { idx = i; found = lb; break; }
        }
        if (idx < 0) { _sb.AppendLine("× 没找到「墨龙 + 盘旋」条目"); yield return Done(); }

        _sb.AppendLine("=== drg_verify：墨龙运动核验收 ===");
        _sb.AppendLine("条目 idx=" + idx + "  label=" + found);
        _sb.AppendLine("captureFramerate = " + FPS + "   frames = " + NF);
        _sb.AppendLine();

        _mStop.Invoke(_sc, null);
        _mPlay.Invoke(_sc, new object[] { idx });
        for (int i = 0; i < 90; i++) yield return null;   // 等它起飞、进入盘旋

        _go = GameObject.Find("Showcase_Actor_" + idx);
        if (_go == null) { _sb.AppendLine("× 没找到 actor 实例 Showcase_Actor_" + idx); yield return Done(); }
        _actor = _go.transform;

        var drg = _go.GetComponent(_dt);
        _vroot   = _dt.GetField("_modelRoot", BF).GetValue(drg) as Transform;
        _spine   = _dt.GetField("_spine", BF).GetValue(drg) as IList;
        _baseRel = _dt.GetField("_baseRel", BF).GetValue(drg) as IList;

        // 参数回读（证明 prefab 值确实生效）
        _sb.AppendLine("── 运行时参数回读 ──");
        string[] pf = { "hoverAmplitudeDeg", "hoverPhaseStepDeg", "hoverPitchAmplitudeDeg",
                        "hoverFrequency", "waveAmpRootGain", "waveAmpHeadGain",
                        "pathWaveAmpDeg", "pathWaveCount", "swimTurnRate",
                        "hoverHeightP1", "hoverHeightP2", "hoverHeightP3" };
        foreach (var n in pf)
        {
            var f = _dt.GetField(n, BF);
            _sb.AppendLine("   " + n.PadRight(24) + (f == null ? "<无>" : f.GetValue(drg).ToString()));
        }
        _sb.AppendLine();
        _sb.AppendLine("_spine.Count = " + (_spine == null ? -1 : _spine.Count)
                     + "   _baseRel.Count = " + (_baseRel == null ? -1 : _baseRel.Count)
                     + "   _modelRoot = " + (_vroot == null ? "NULL" : _vroot.name));
        _sb.AppendLine();

        _cam = MakeCam();

        int NS = _spine == null ? 0 : _spine.Count;
        var bend   = new List<float>();   // 相对尾→头弦的最大垂距 = 「弯了多少」
        var devH   = new List<float>();   // 弦垂距的**水平**分量（左右弯）
        var devV   = new List<float>();   // 弦垂距的**垂直**分量（上下起伏）
        var len    = new List<float>();   // 身长（保险丝）
        var ySpan  = new List<float>();   // 链 Y 跨度（垂直）
        var xzSpan = new List<float>();   // 链 XZ 跨度（水平）
        var radii  = new List<float>();   // actor 到玩家的水平距离（路径判据）
        var perLinkMin = new float[NS];
        var perLinkMax = new float[NS];
        for (int i = 0; i < NS; i++) { perLinkMin[i] = float.MaxValue; perLinkMax[i] = float.MinValue; }

        // 找玩家（算半径用）
        Transform player = null;
        foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
        {
            var t = a.GetType("InkWash.Player.PlayerController");
            if (t == null) continue;
            foreach (var o in UnityEngine.Object.FindObjectsOfType(t, true)) { player = (o as Component).transform; break; }
            if (player != null) break;
        }

        Time.captureFramerate = FPS;

        for (int f = 0; f < NF; f++)
        {
            try
            {
                var pos = new Vector3[NS];
                Quaternion rootRot = _vroot != null ? _vroot.rotation : _actor.rotation;
                float ymin = float.MaxValue, ymax = float.MinValue;
                float xmin = float.MaxValue, xmax = float.MinValue, zmin = float.MaxValue, zmax = float.MinValue;

                for (int i = 0; i < NS; i++)
                {
                    var t = _spine[i] as Transform;
                    if (t == null) { pos[i] = Vector3.zero; continue; }
                    pos[i] = t.position;
                    if (pos[i].y < ymin) ymin = pos[i].y;
                    if (pos[i].y > ymax) ymax = pos[i].y;
                    if (pos[i].x < xmin) xmin = pos[i].x;
                    if (pos[i].x > xmax) xmax = pos[i].x;
                    if (pos[i].z < zmin) zmin = pos[i].z;
                    if (pos[i].z > zmax) zmax = pos[i].z;

                    var rel = (Quaternion)_baseRel[i];
                    float off = Quaternion.Angle(t.rotation, rootRot * rel);
                    if (off < perLinkMin[i]) perLinkMin[i] = off;
                    if (off > perLinkMax[i]) perLinkMax[i] = off;
                }

                // ① 弦垂距
                if (NS >= 3)
                {
                    Vector3 a0 = pos[0], b0 = pos[NS - 1];
                    Vector3 ab = b0 - a0;
                    float abLen = ab.magnitude;
                    float mx = 0f, mxH = 0f, mxV = 0f;
                    if (abLen > 1e-4f)
                    {
                        Vector3 u = ab / abLen;
                        for (int i = 1; i < NS - 1; i++)
                        {
                            Vector3 p = pos[i] - a0;
                            Vector3 dev = p - Vector3.Dot(p, u) * u;
                            float d = dev.magnitude;
                            if (d > mx) mx = d;
                            float dh = new Vector2(dev.x, dev.z).magnitude;   // 水平弯
                            float dv = Mathf.Abs(dev.y);                      // 垂直起伏
                            if (dh > mxH) mxH = dh;
                            if (dv > mxV) mxV = dv;
                        }
                    }
                    bend.Add(mx);
                    len.Add(abLen);
                    devH.Add(mxH);
                    devV.Add(mxV);
                }
                ySpan.Add(ymax - ymin);
                xzSpan.Add(Mathf.Sqrt((xmax - xmin) * (xmax - xmin) + (zmax - zmin) * (zmax - zmin)));
                radii.Add(player != null
                    ? new Vector2(_actor.position.x - player.position.x, _actor.position.z - player.position.z).magnitude
                    : -1f);

                if (f == 0 || f == NF / 2)
                    _sb.AppendLine("帧 " + f + "  弦垂距=" + bend[bend.Count - 1].ToString("F2") + " m"
                        + "  身长=" + len[len.Count - 1].ToString("F2") + " m"
                        + "  Y跨度=" + ySpan[ySpan.Count - 1].ToString("F2")
                        + "  XZ跨度=" + xzSpan[xzSpan.Count - 1].ToString("F2")
                        + "  半径=" + radii[radii.Count - 1].ToString("F2"));

                Snap(f);
            }
            catch (Exception ex) { _sb.AppendLine("  × 帧 " + f + " 异常 " + ex.Message); }

            Flush();
            yield return null;
        }

        Time.captureFramerate = 0;

        // ── 汇总 ──
        _sb.AppendLine();
        _sb.AppendLine("===== 汇总 =====");
        _sb.AppendLine("  弦垂距（身体弯了多少）  最小=" + Min(bend).ToString("F2") + "  最大=" + Max(bend).ToString("F2") + "  均值=" + Avg(bend).ToString("F2") + " m");
        _sb.AppendLine("  身长（保险丝 7~9 m）    最小=" + Min(len).ToString("F2") + "  最大=" + Max(len).ToString("F2") + " m");
        _sb.AppendLine("  Y 跨度（垂直）          最小=" + Min(ySpan).ToString("F2") + "  最大=" + Max(ySpan).ToString("F2") + "  均值=" + Avg(ySpan).ToString("F2") + " m");
        _sb.AppendLine("  XZ 跨度（水平）         最小=" + Min(xzSpan).ToString("F2") + "  最大=" + Max(xzSpan).ToString("F2") + "  均值=" + Avg(xzSpan).ToString("F2") + " m");
        // ★ 判据修正：**不能**用「Y跨度 / XZ跨度」。XZ 跨度主要由身长（8.2 m）贡献，
        //   跟摆动幅度无关 ⇒ 恒算出 ~0.1，读不出「扁片还是立体」。
        //   正确做法：把弦垂距**拆成水平/垂直分量**再比。
        float vz = Avg(devH) > 1e-4f ? Avg(devV) / Avg(devH) : 0f;
        _sb.AppendLine("  弦垂距·水平分量(左右)    均值=" + Avg(devH).ToString("F2") + " m");
        _sb.AppendLine("  弦垂距·垂直分量(上下)    均值=" + Avg(devV).ToString("F2") + " m");
        _sb.AppendLine("  ★ 垂直/水平 比          = " + vz.ToString("F2") + "   （旧值 1.5/35 ≈ 0.04 ⇒ 扁片；0.35~0.60 才是三维游动）");
        _sb.AppendLine("  半径（路径判据）        最小=" + Min(radii).ToString("F2") + "  最大=" + Max(radii).ToString("F2")
                      + "  波动=" + (Max(radii) - Min(radii)).ToString("F2") + " m");
        _sb.AppendLine();

        _sb.AppendLine("===== 每节实际偏移角的极差（度）=====");
        int moving = 0;
        for (int i = 0; i < NS; i++)
        {
            float rng = perLinkMax[i] - perLinkMin[i];
            if (rng > 5f) moving++;
            if (i % 2 == 0 || i == NS - 1)
            {
                var t = _spine[i] as Transform;
                _sb.AppendLine("  [" + i.ToString("D2") + "] " + (t == null ? "NULL" : t.name).PadRight(14)
                    + " 极差=" + rng.ToString("F2").PadRight(8) + (rng > 5f ? "✓" : "⚠"));
            }
        }
        _sb.AppendLine("  摆动显著的节数 = " + moving + " / " + NS);
        _sb.AppendLine();
        _sb.AppendLine("PNG 帧数 = " + _saved + "  → " + SD);

        if (_cam != null) UnityEngine.Object.Destroy(_cam.gameObject);
        yield return Done();
    }

    static float Avg(List<float> v) { if (v.Count == 0) return 0f; float s = 0f; for (int i = 0; i < v.Count; i++) s += v[i]; return s / v.Count; }
    static float Min(List<float> v) { float m = float.MaxValue; for (int i = 0; i < v.Count; i++) if (v[i] < m) m = v[i]; return v.Count == 0 ? 0f : m; }
    static float Max(List<float> v) { float m = float.MinValue; for (int i = 0; i < v.Count; i++) if (v[i] > m) m = v[i]; return v.Count == 0 ? 0f : m; }

    Camera MakeCam()
    {
        var g = new GameObject("PROBE_CAM_DRG");
        var c = g.AddComponent<Camera>();
        c.clearFlags = CameraClearFlags.SolidColor;
        c.backgroundColor = new Color(0.86f, 0.88f, 0.91f, 1f);
        c.fieldOfView = 45f;
        c.nearClipPlane = 0.05f;
        c.farClipPlane = 5000f;
        c.enabled = false;
        return c;
    }

    void Snap(int f)
    {
        var rds = _go.GetComponentsInChildren<Renderer>(true);
        if (rds.Length == 0) return;
        var b = rds[0].bounds;
        for (int i = 1; i < rds.Length; i++) b.Encapsulate(rds[i].bounds);

        float rad = Mathf.Max(b.extents.magnitude, 1f);
        float dist = rad / Mathf.Sin(_cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.08f;

        // 相机跟随龙的**位置**，但取景方向固定在世界空间 ⇒ 能看清身体走向，不被龙的自转带着转。
        // ★ 仰角必须压低（0.20）：仰角高的时候"垂直起伏"会被投影压扁，看不出立体游动。
        Vector3 dir = new Vector3(0.62f, 0.20f, -0.76f).normalized;
        _cam.transform.position = b.center + dir * dist;
        _cam.transform.LookAt(b.center);

        int W = 960, H = 540;
        var rt = new RenderTexture(W, H, 24);
        _cam.targetTexture = rt;
        _cam.Render();
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
        tex.Apply();
        File.WriteAllBytes(SD + "/drv_" + f.ToString("D4") + ".png", tex.EncodeToPNG());
        RenderTexture.active = prev;
        UnityEngine.Object.Destroy(tex);
        UnityEngine.Object.Destroy(rt);
        _cam.targetTexture = null;
        _saved++;
    }

    IEnumerator Done()
    {
        Time.captureFramerate = 0;
        Flush();
        Debug.Log("[drg_verify] 写入 " + RP + "  帧数 " + _saved);
        yield return null;
    }

    void Flush() { try { File.WriteAllText(RP, _sb.ToString()); } catch { } }
}

var __hostDrg = new GameObject("drg_verify");
__hostDrg.AddComponent<drg_verify>();
return "DRG_VERIFY_STARTED";
