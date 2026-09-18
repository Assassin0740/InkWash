using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;

// drg_headref.cs —— 第二十六轮：把「运行时头」与「FBX 原生基准姿态」直接对齐量 + 同相对视角出图
//
// 【缘起】用户看了 H25 的「不锁 / 锁定 pitch=0 / 锁定 pitch=-12」三档对照图后说
//   「这三个都是歪的，ABC，全是歪的」。
//   这是极强的逻辑约束：B/C 与 A 的**唯一差别**是「去掉摆动」⇒ 三档**共有**的那个东西才是病根。
//   共有物 = `StraightenSpineBase()` 造出来的**拉直基准**。
//
// 【机制】`StraightenSpineBase()`（EnemyDragon.cs:808）循环 `for (i = 0; i + 1 < Count; i++)`
//   会把 `_spine[Count-2]`（= 颈根）**也转过去**；而头簇（39 枚叶子骨 + 14 条鬃/须/角/颌）
//   是**刚性分支、挂在 `_spine[Count-2]` 下、从不被驱动** ⇒ 头的世界朝向
//   **完全等于** `_spine[Count-2].rotation` × 常数。所以拉直那一步就把头拧掉了，
//   之后无论 `headLockToBase` 开不开、`headAlignPitchDeg` 取几，都继承同一个歪基准。
//
// 【本探针】用户的判据来源是「FBX 精致姿态里头的相对位置和方向都是对的」⇒
//   直接实例化一份**纯净的 `Z_Dragon.prefab`**（无 EnemyDragon、无 Animator）当基准，
//   然后逐节比局部旋转 / 「相对模型容器」的旋转，并做**同相对视角**的出图对照。
//
// 判据：
//   ① `Δlocal[i]` = 运行时第 i 节与 FBX 基准**同节**的局部旋转差角（与层级无关，纯标量）
//   ② `Δrel[i]`  = 「相对模型容器」的旋转差 ⇒ 拆成 仰角/偏航/滚转 三个分量
//   ③ `Δstraight[i]` = 拉直对第 i 节的贡献（`_baseRel[i]` vs FBX 基准）⇒ 直接证伪/证实病根
//   ④ 出图：rest（基准）vs live（运行时），侧视 / 俯视 / 前视，
//      **相机各自用自己的模型容器朝向来摆** ⇒ 两张图描述同一个「体相对视角」，可直接叠看
//
// 手法：hoverStationary = true（位置/朝向冻结，蛇形波照跑）、钉 30 fps、相机只算一次。

public class drg_headref : MonoBehaviour
{
    const string DIR = "D:/Unity Project/InkWash/Tools/screenshots/enemies/H26";
    const string RP = "D:/Unity Project/InkWash/Tools/reports/drg_headref.txt";
    const string KEY = "墨龙";
    const string MODEL_PREFAB = "Assets/_Project/Prefabs/Ziyuan/Z_Dragon.prefab";
    const string CLONE_NAME = "PROBE_REST_REF";
    const int W = 900, H = 600;
    const int TAIL_N = 6;          // 报告末尾几节

    static readonly BindingFlags BF = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;

    readonly StringBuilder _sb = new StringBuilder();

    Component _sc; Type _ty, _dt;
    MethodInfo _mPlay, _mLabel, _mStop;
    PropertyInfo _pCount;

    GameObject _go; object _drg;
    Transform _drgT, _modelRoot;
    System.Collections.IList _spine, _baseRel, _baseSegLocal, _segLen;
    GameObject _clone; Transform _cloneRef;
    Transform[] _cloneBone;
    string _cloneNote = "";
    int _NS;

    Camera _cam;
    int _shots;

    void Start() { StartCoroutine(Run()); }

    IEnumerator Run()
    {
        yield return null; yield return null;
        Time.captureFramerate = 30;
        Directory.CreateDirectory(DIR);

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

        _drg = _go.GetComponent(_dt);
        if (_drg == null) { _sb.AppendLine("× actor 上没有 EnemyDragon"); yield return Done(); }
        _drgT = ((Component)_drg).transform;

        var fSpine = _dt.GetField("_spine", BF);
        var fModelRoot = _dt.GetField("_modelRoot", BF);
        var fBaseRel = _dt.GetField("_baseRel", BF);
        var fBaseSeg = _dt.GetField("_baseSegLocal", BF);
        var fSegLen = _dt.GetField("_segLen", BF);
        var fStraight = _dt.GetField("straightenSpine", BF);
        var fLock = _dt.GetField("headLockToBase", BF);
        var fLinks = _dt.GetField("headAlignLinks", BF);
        var fPitch = _dt.GetField("headAlignPitchDeg", BF);
        var fYaw = _dt.GetField("headAlignYawDeg", BF);
        var fRoll = _dt.GetField("headAlignRollDeg", BF);
        var fHg = _dt.GetField("swimWaveHeadGain", BF);
        var fHover = _dt.GetField("hoverStationary", BF);
        var fPerf = _dt.GetField("enablePerformanceCycle", BF);
        if (fSpine == null || fModelRoot == null || fBaseRel == null || fStraight == null || fLock == null)
        { _sb.AppendLine("× 字段缺失"); yield return Done(); }

        _spine = fSpine.GetValue(_drg) as System.Collections.IList;
        _modelRoot = fModelRoot.GetValue(_drg) as Transform;
        _baseRel = fBaseRel.GetValue(_drg) as System.Collections.IList;
        _baseSegLocal = fBaseSeg != null ? fBaseSeg.GetValue(_drg) as System.Collections.IList : null;
        _segLen = fSegLen != null ? fSegLen.GetValue(_drg) as System.Collections.IList : null;
        if (_spine == null || _modelRoot == null) { _sb.AppendLine("× _spine/_modelRoot 为空"); yield return Done(); }
        _NS = _spine.Count;

        _sb.AppendLine("=== drg_headref：运行时头 vs FBX 原生基准姿态（第二十六轮）===");
        _sb.AppendLine("条目 idx=" + idx + "  label=" + found);
        _sb.AppendLine("脊柱 " + _NS + " 节｜模型容器 = " + PathOf(_modelRoot, _drgT));
        _sb.AppendLine("straightenSpine=" + fStraight.GetValue(_drg)
                       + "｜headLockToBase=" + fLock.GetValue(_drg)
                       + "｜headAlignLinks=" + fLinks.GetValue(_drg)
                       + "｜headAlignPitch/Yaw/Roll=" + fPitch.GetValue(_drg) + "/" + fYaw.GetValue(_drg) + "/" + fRoll.GetValue(_drg)
                       + "｜swimWaveHeadGain=" + fHg.GetValue(_drg));
        _sb.AppendLine();

        // ── 冻结位置/朝向，只留姿态 ──
        if (fHover != null) fHover.SetValue(_drg, true);
        if (fPerf != null) fPerf.SetValue(_drg, false);
        for (int i = 0; i < 20; i++) yield return null;

        // ── 脊柱节名（终于把解剖结构打印出来）──
        _sb.AppendLine("── 脊柱节名（索引: 名字）──");
        for (int i = 0; i < _NS; i++)
        {
            var t = _spine[i] as Transform;
            if (i < 4 || i >= _NS - 8)
                _sb.AppendLine("   [" + i.ToString("D2") + "] " + (t != null ? t.name : "null"));
            else if (i == 4) _sb.AppendLine("   ...");
        }
        _sb.AppendLine();

        // ── 造纯净基准：实例化 Z_Dragon.prefab，屏蔽一切脚本 ──
        var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(MODEL_PREFAB);
        if (prefab == null) { _sb.AppendLine("× 载不到 " + MODEL_PREFAB); yield return Done(); }
        _clone = UnityEngine.Object.Instantiate(prefab);
        _clone.name = CLONE_NAME;
        _clone.transform.position = _modelRoot.position + new Vector3(600f, 0f, 600f);
        int dis = 0;
        foreach (var mb in _clone.GetComponentsInChildren<MonoBehaviour>(true))
        { if (mb != null) { mb.enabled = false; dis++; } }
        foreach (var an in _clone.GetComponentsInChildren<Animator>(true))
        { if (an != null) { an.enabled = false; dis++; } }
        yield return null; yield return null;

        var embedded = _clone.GetComponentInChildren(_dt, true);
        _cloneNote = "纯净基准实例：已屏蔽 " + dis + " 个脚本/Animator ｜ 内含 EnemyDragon = "
                     + (embedded != null ? "★有（基准被污染！）" : "无 ✅");

        // 找到与 _modelRoot 同名的节点当参考系
        _cloneRef = FindDeep(_clone.transform, _modelRoot.name);
        if (_cloneRef == null) { _cloneRef = _clone.transform; _cloneNote += " ｜ 未找到同名容器，改用实例根"; }

        _cloneBone = new Transform[_NS];
        int matched = 0;
        for (int i = 0; i < _NS; i++)
        {
            var t = _spine[i] as Transform;
            if (t == null) continue;
            _cloneBone[i] = FindDeep(_clone.transform, t.name);
            if (_cloneBone[i] != null) matched++;
        }
        _sb.AppendLine(_cloneNote);
        _sb.AppendLine("基准骨架匹配：容器 " + _modelRoot.name + " → " + (_cloneRef != null ? _cloneRef.name : "?")
                       + "｜脊柱节同名匹配 " + matched + "/" + _NS);
        _sb.AppendLine();

        // ── 逐节对齐量 ──
        _sb.AppendLine("── 逐节对齐（基准 = 纯净 Z_Dragon 静止姿态；角度单位 度）──");
        _sb.AppendLine("   节    名字            Δlocal   Δrel    Δ仰角   Δ偏航   Δ滚转   Δstraight   ΔsegDir");
        for (int i = Mathf.Max(0, _NS - TAIL_N); i < _NS; i++)
        {
            var lt = _spine[i] as Transform;
            var rt = _cloneBone != null && i < _cloneBone.Length ? _cloneBone[i] : null;
            if (lt == null || rt == null) { _sb.AppendLine("   [" + i + "] 缺对照，跳过"); continue; }

            Quaternion dLocal = lt.localRotation * Quaternion.Inverse(rt.localRotation);

            Quaternion liveRel = Quaternion.Inverse(_modelRoot.rotation) * lt.rotation;
            Quaternion restRel = Quaternion.Inverse(_cloneRef.rotation) * rt.rotation;
            Quaternion err = liveRel * Quaternion.Inverse(restRel);

            Vector3 eL = liveRel * Vector3.forward, eR = restRel * Vector3.forward;
            float dElev = Mathf.Asin(Mathf.Clamp(eL.normalized.y, -1f, 1f)) * Mathf.Rad2Deg
                        - Mathf.Asin(Mathf.Clamp(eR.normalized.y, -1f, 1f)) * Mathf.Rad2Deg;
            float dYaw = Mathf.Atan2(eL.normalized.x, eL.normalized.z) * Mathf.Rad2Deg
                       - Mathf.Atan2(eR.normalized.x, eR.normalized.z) * Mathf.Rad2Deg;
            float dRoll = RollDelta(liveRel, restRel, eR);

            float dStraight = 0f;
            if (_baseRel != null && i < _baseRel.Count && _baseRel[i] is Quaternion)
                dStraight = Quaternion.Angle((Quaternion)_baseRel[i], restRel);

            float dSeg = 0f;
            if (_baseSegLocal != null && i < _baseSegLocal.Count && _baseSegLocal[i] is Vector3
                && i + 1 < _NS && _cloneBone[i + 1] != null)
            {
                Vector3 liveSeg = ((Vector3)_baseSegLocal[i]).normalized;
                Vector3 restSeg = (_cloneRef.worldToLocalMatrix.MultiplyPoint3x4(_cloneBone[i + 1].position)
                                 - _cloneRef.worldToLocalMatrix.MultiplyPoint3x4(rt.position)).normalized;
                dSeg = Vector3.Angle(liveSeg, restSeg);
            }

            _sb.AppendLine("   [" + i.ToString("D2") + "] " + Pad(lt.name, 16)
                           + Pad(Quaternion.Angle(lt.localRotation, rt.localRotation).ToString("F2"), 9)
                           + Pad(Quaternion.Angle(liveRel, restRel).ToString("F2"), 8)
                           + Pad(dElev.ToString("+0.00;-0.00"), 8)
                           + Pad(dYaw.ToString("+0.00;-0.00"), 8)
                           + Pad(dRoll.ToString("+0.00;-0.00"), 8)
                           + Pad(dStraight.ToString("F2"), 12)
                           + dSeg.ToString("F2"));
        }
        _sb.AppendLine();
        _sb.AppendLine("★ 读法：");
        _sb.AppendLine("  · Δlocal = 运行时与基准**同节局部旋转**的差角 ⇒ 与层级无关的纯标量，最干净。");
        _sb.AppendLine("  · Δrel   = 「相对模型容器」旋转差 ⇒ 拆成 Δ仰角/Δ偏航/Δ滚转（正=逆时针/上抬）。");
        _sb.AppendLine("  · Δstraight = **拉直**把该节转了多少度（`_baseRel[i]` 对基准）。");
        _sb.AppendLine("    ⇒ 若 `_spine[Count-2]` 的 Δstraight ≠ 0，就证明「拉直把颈根转了」，头必然歪。");
        _sb.AppendLine("  · ΔsegDir = 该节的基准段方向（拉直后）与 FBX 原方向的夹角。");
        _sb.AppendLine();

        // ── 出图：rest vs live，同「体相对」三视角 ──
        _cam = MakeCam("PROBE_CAM_H26");
        _sb.AppendLine("── 出图（体相对视角：相机用**各自模型容器**的前/右/上来摆，两图可直接叠看）──");
        ShootPair("head", HeadFocus(), 3.0f);
        ShootPair("wide", BodyFocus(), 9.0f);

        if (_cam != null) { if (_cam.targetTexture != null) UnityEngine.Object.Destroy(_cam.targetTexture); UnityEngine.Object.Destroy(_cam.gameObject); }
        if (_clone != null) UnityEngine.Object.Destroy(_clone);
        yield return Done();
    }

    // ───────────────────────── 出图 ─────────────────────────

    Vector3 HeadFocus()
    {
        var t = _spine[_NS - 2] as Transform;
        Vector3 f = new Vector3(_modelRoot.forward.x, 0f, _modelRoot.forward.z).normalized;
        return t.position + f * 0.45f;
    }

    Vector3 BodyFocus()
    {
        Vector3 a = ((Transform)_spine[0]).position;
        Vector3 b = ((Transform)_spine[_NS - 1]).position;
        return (a + b) * 0.5f;
    }

    void ShootPair(string tag, Vector3 focusLive, float dist)
    {
        // live：用运行时容器朝向
        Shot(tag + "_live_side", focusLive, dist, _modelRoot, 0.10f, 0.99f, 0.07f);
        Shot(tag + "_live_top", focusLive, dist, _modelRoot, 0.05f, 0.15f, 0.99f);
        Shot(tag + "_live_front", focusLive, dist, _modelRoot, 0.99f, 0.12f, 0.06f);

        // rest：用基准容器朝向，焦点换到基准的同一节
        var t = _spine[_NS - 2] as Transform;
        var rt = _cloneBone != null && _NS - 2 < _cloneBone.Length ? _cloneBone[_NS - 2] : null;
        if (rt == null) { _sb.AppendLine("   × 基准缺颈根节，跳过 rest 出图"); return; }
        Vector3 rf = new Vector3(_cloneRef.forward.x, 0f, _cloneRef.forward.z).normalized;
        Vector3 focusRest = tag == "head" ? rt.position + rf * 0.45f : (_cloneBone[0].position + rt.position) * 0.5f;
        Shot(tag + "_rest_side", focusRest, dist, _cloneRef, 0.10f, 0.99f, 0.07f);
        Shot(tag + "_rest_top", focusRest, dist, _cloneRef, 0.05f, 0.15f, 0.99f);
        Shot(tag + "_rest_front", focusRest, dist, _cloneRef, 0.99f, 0.12f, 0.06f);
    }

    void Shot(string name, Vector3 focus, float dist, Transform frame, float aFwd, float aRight, float aUp)
    {
        Vector3 fwd = FlatN(frame.forward);
        Vector3 up = frame.up.normalized;
        Vector3 right = Vector3.Cross(up, fwd).normalized;
        Vector3 dir = (fwd * aFwd + right * aRight + up * aUp).normalized;
        _cam.transform.position = focus + dir * dist;
        _cam.transform.LookAt(focus, up);
        File.WriteAllBytes(DIR + "/" + name + ".png", ShotBytes(_cam));
        _shots++;
    }

    static Vector3 FlatN(Vector3 v) { v.y = 0f; if (v.sqrMagnitude < 1e-8f) v = Vector3.forward; return v.normalized; }

    /// <summary>滚转差：把「up 在垂直于 forward 平面上的投影」做有符号夹角。</summary>
    static float RollDelta(Quaternion liveRel, Quaternion restRel, Vector3 fwdRef)
    {
        Vector3 f = liveRel * Vector3.forward;
        if (f.sqrMagnitude < 1e-8f) return 0f;
        f.Normalize();
        Vector3 uL = liveRel * Vector3.up; uL -= f * Vector3.Dot(uL, f);
        Vector3 uR = restRel * Vector3.up; uR -= f * Vector3.Dot(uR, f);
        if (uL.sqrMagnitude < 1e-10f || uR.sqrMagnitude < 1e-10f) return 0f;
        uL.Normalize(); uR.Normalize();
        Vector3 r = Vector3.Cross(f, uR);
        return Mathf.Atan2(Vector3.Dot(uL, r), Vector3.Dot(uL, uR)) * Mathf.Rad2Deg;
    }

    byte[] ShotBytes(Camera cam)
    {
        if (cam.targetTexture == null) cam.targetTexture = new RenderTexture(W, H, 24);
        cam.Render();
        var prev = RenderTexture.active;
        RenderTexture.active = cam.targetTexture;
        var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
        tex.Apply();
        byte[] png = tex.EncodeToPNG();
        RenderTexture.active = prev;
        UnityEngine.Object.Destroy(tex);
        return png;
    }

    Camera MakeCam(string name)
    {
        var g = new GameObject(name);
        var c = g.AddComponent<Camera>();
        c.clearFlags = CameraClearFlags.SolidColor;
        c.backgroundColor = new Color(0.86f, 0.88f, 0.91f, 1f);
        c.fieldOfView = 35f;
        c.nearClipPlane = 0.03f;
        c.farClipPlane = 5000f;
        c.enabled = false;
        c.targetTexture = new RenderTexture(W, H, 24);
        return c;
    }

    // ───────────────────────── 小工具 ─────────────────────────

    static Transform FindDeep(Transform t, string name)
    {
        if (t == null) return null;
        if (t.name == name) return t;
        for (int i = 0; i < t.childCount; i++)
        {
            var r = FindDeep(t.GetChild(i), name);
            if (r != null) return r;
        }
        return null;
    }

    static string PathOf(Transform t, Transform stopAt)
    {
        if (t == null) return "null";
        var sb = new StringBuilder(t.name);
        var p = t.parent;
        int guard = 0;
        while (p != null && guard++ < 12) { sb.Insert(0, p.name + "/"); if (p == stopAt) break; p = p.parent; }
        return sb.ToString();
    }

    static string Pad(string s, int n) { return s.Length >= n ? s : s + new string(' ', n - s.Length); }

    IEnumerator Done()
    {
        Time.captureFramerate = 0;
        try { File.WriteAllText(RP, _sb.ToString()); } catch { }
        Debug.Log("[drg_headref] 写入 " + RP + "  出图 " + _shots + " 张");
        yield return null;
    }
}

var __hostH26 = new GameObject("drg_headref");
__hostH26.AddComponent<drg_headref>();
return "DRG_HEADREF_STARTED";
