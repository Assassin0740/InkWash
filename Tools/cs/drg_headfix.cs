using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;

// drg_headfix.cs —— 第二十六轮：头锁定换基（_baseRel → _restRel）的**无假设** A/B
//
// 【为什么这个探针比上一个硬】
//   上一个探针（drg_headref）用**另实例化一份 Z_Dragon**当基准，然后跨两个实例比旋转 ——
//   两个实例的容器节点同名但层级不同（运行时容器是 `Showcase_Actor_23/Visual`），
//   跨实例比较会掺进一个未知的常量参考系偏差。
//
//   本探针改成：**把运行时那一份龙，就地强行摆回 FBX 原生姿态**（把每个节点的
//   localRotation / localPosition 从纯净实例拷回来），立刻渲染一帧。
//   ⇒ 基准帧与对比帧**同一个物件、同一台相机、同一套材质、同一光照、同一位置**，
//     唯一变量就是「姿态」。这是本项目能构造出的最强对照（坑 24：基准 vs 驱动，同机位）。
//
// 【三档】
//   rest = 强行回 FBX 原生姿态（用户认可的基准，即判据来源）
//   old  = headLockUseRestPose = false（锁**拉直之后**的 _baseRel ⇒ 第二十五轮的行为）
//   new  = headLockUseRestPose = true （锁**拉直之前**的 _restRel ⇒ 本轮修法）
//
// 【主判据（不需要任何跨实例假设）】
//   `err(state) = Angle( 容器相对旋转(_spine[Count-2]) , 同一个量在 rest 帧的值 )`
//   `_spine[Count-2] = drgon_025` 是**头簇挂点**，头簇（39 叶 + 鬃/须/角/颌）是刚性分支、
//   从不被驱动 ⇒ 这个角 = **头相对 FBX 姿态被拧了多少度**，且拆得出 俯仰/偏航/滚转。
//   自检：随机抽 8 枚头簇叶子骨，其**局部旋转**在三档之间必须**完全不变**
//   （否则"头姿态 = 挂点姿态"这条前提不成立）。
//
// 附带：三个相位 × 三档（证明新档与相位无关），并出图供眼睛裁决。

public class drg_headfix : MonoBehaviour
{
    const string DIR = "D:/Unity Project/InkWash/Tools/screenshots/enemies/H27";
    const string RP = "D:/Unity Project/InkWash/Tools/reports/drg_headfix.txt";
    const string KEY = "墨龙";
    const string MODEL_PREFAB = "Assets/_Project/Prefabs/Ziyuan/Z_Dragon.prefab";
    const int W = 900, H = 600;
    const float DRIVE_HZ = 0.45f;

    static readonly float[] PHASES = new float[] { 0.00f, 0.30f, 0.60f };
    static readonly BindingFlags BF = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;

    readonly StringBuilder _sb = new StringBuilder();

    Component _sc; Type _ty, _dt;
    MethodInfo _mPlay, _mLabel, _mStop;
    PropertyInfo _pCount;

    GameObject _go; object _drg; Behaviour _drgC;
    Transform _drgT, _modelRoot;
    System.Collections.IList _spine;
    GameObject _clone;
    Dictionary<Transform, Quaternion> _saveRot = new Dictionary<Transform, Quaternion>();
    Transform[] _mirror;              // 与 clone 一一对应的节点（null 表示无对应）
    int _mirrorN;
    Transform[] _headLeaves = new Transform[8];
    Transform _cloneCarrier;
    Transform[] _headLeafClone = new Transform[8];
    Quaternion[] _headLeafLocal = new Quaternion[8];
    int _NS;

    Camera _cam;
    int _shots;
    SkinnedMeshRenderer[] _smrs;
    Vector3 _focusRef; float _dist;

    Quaternion _carrierRestRel = Quaternion.identity;
    Vector3 _restFwd = Vector3.forward;

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
        var comp = _go.GetComponent(_dt);
        if (comp == null) { _sb.AppendLine("× actor 上没有 EnemyDragon"); yield return Done(); }
        _drg = comp; _drgC = comp as Behaviour; _drgT = comp.transform;

        var fSpine = _dt.GetField("_spine", BF);
        var fModelRoot = _dt.GetField("_modelRoot", BF);
        var fStraight = _dt.GetField("straightenSpine", BF);
        var fLock = _dt.GetField("headLockToBase", BF);
        var fUseRest = _dt.GetField("headLockUseRestPose", BF);
        var fLinks = _dt.GetField("headAlignLinks", BF);
        var fPitch = _dt.GetField("headAlignPitchDeg", BF);
        var fHover = _dt.GetField("hoverStationary", BF);
        var fPerf = _dt.GetField("enablePerformanceCycle", BF);
        if (fSpine == null || fModelRoot == null || fLock == null || fUseRest == null)
        { _sb.AppendLine("× 字段缺失（headLockUseRestPose 还没编译进去？）"); yield return Done(); }

        _spine = fSpine.GetValue(_drg) as System.Collections.IList;
        _modelRoot = fModelRoot.GetValue(_drg) as Transform;
        if (_spine == null || _modelRoot == null) { _sb.AppendLine("× _spine/_modelRoot 为空"); yield return Done(); }
        _NS = _spine.Count;

        _sb.AppendLine("=== drg_headfix：头锁定换基（_baseRel → _restRel）无假设 A/B（第二十六轮）===");
        _sb.AppendLine("条目 idx=" + idx + "  label=" + found);
        _sb.AppendLine("脊柱 " + _NS + " 节｜容器 = " + _modelRoot.name
                       + "｜straightenSpine=" + fStraight.GetValue(_drg)
                       + "｜headLockToBase=" + fLock.GetValue(_drg)
                       + "｜headLockUseRestPose=" + fUseRest.GetValue(_drg)
                       + "｜headAlignLinks=" + fLinks.GetValue(_drg)
                       + "｜headAlignPitch=" + fPitch.GetValue(_drg));
        _sb.AppendLine("★ hoverStationary=true（位置/朝向冻结，行波照跑）｜钉 30 fps｜侧视相机只算一次");
        _sb.AppendLine();

        if (fHover != null) fHover.SetValue(_drg, true);
        if (fPerf != null) fPerf.SetValue(_drg, false);
        for (int i = 0; i < 20; i++) yield return null;

        // ── 造纯净基准 + 建「节点 ↔ 节点」镜像表 ──
        var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(MODEL_PREFAB);
        if (prefab == null) { _sb.AppendLine("× 载不到 " + MODEL_PREFAB); yield return Done(); }
        _clone = UnityEngine.Object.Instantiate(prefab);
        _clone.name = "PROBE_REST_REF";
        _clone.transform.position = _modelRoot.position + new Vector3(600f, 0f, 600f);
        foreach (var mb in _clone.GetComponentsInChildren<MonoBehaviour>(true)) if (mb != null) mb.enabled = false;
        foreach (var an in _clone.GetComponentsInChildren<Animator>(true)) if (an != null) an.enabled = false;

        // ★ 只对 **drgon_* 骨骼** 做基准姿态回写：
        //   容器节点（Visual / Model / Object_xxx）一个都不能碰 ——
        //   第一版连 localPosition 一起回写了，结果模型被推出画面（rest 出图全空）。
        //   同名歧义也会导致同一节点被写两次，必须显式检出。
        var all = _modelRoot.GetComponentsInChildren<Transform>(true);
        var map = new Dictionary<string, Transform>();
        var dup = new HashSet<string>();
        foreach (var t in _clone.GetComponentsInChildren<Transform>(true))
        {
            if (!t.name.StartsWith("drgon_")) continue;
            if (map.ContainsKey(t.name)) dup.Add(t.name); else map[t.name] = t;
        }

        _mirror = new Transform[all.Length];
        int noName = 0, ambiguous = 0;
        for (int i = 0; i < all.Length; i++)
        {
            if (!all[i].name.StartsWith("drgon_")) continue;
            Transform r;
            if (dup.Contains(all[i].name)) { ambiguous++; continue; }
            if (map.TryGetValue(all[i].name, out r)) _mirror[i] = r; else noName++;
            if (_mirror[i] != null) _mirrorN++;
        }
        // ★ 关掉 updateWhenOffscreen=false 的坑：它会用上一次蒙皮算出的包围盒做视锥剔除，
        //   姿态把网格搬走之后就被整个剔掉（出图全空）。
        _smrs = _modelRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        for (int i = 0; i < _smrs.Length; i++) if (_smrs[i] != null) _smrs[i].updateWhenOffscreen = true;

        _sb.AppendLine("镜像表：运行时骨骼节点 " + all.Length + " 个，其中 drgon_* 命中 " + _mirrorN
                       + " （缺对照 " + noName + " / 同名歧义 " + ambiguous + "）"
                       + "（基准实例内含 EnemyDragon = " + (_clone.GetComponentInChildren(_dt, true) != null ? "有" : "无") + "）");

        // 头簇叶子骨抽样（验证「头姿态 = 挂点姿态」这条前提）
        var carrier = _spine[_NS - 2] as Transform;
        int got = 0;
        var stack = new Stack<Transform>();
        stack.Push(carrier);
        while (stack.Count > 0 && got < 8)
        {
            var t = stack.Pop();
            if (t != carrier && t.childCount == 0) { _headLeaves[got++] = t; }
            for (int i = 0; i < t.childCount; i++) stack.Push(t.GetChild(i));
        }
        _cloneCarrier = map.ContainsKey(carrier.name) ? map[carrier.name] : null;
        int leafMapped = 0;
        for (int i = 0; i < _headLeaves.Length; i++)
        {
            if (_headLeaves[i] == null) continue;
            Transform cm;
            if (map.TryGetValue(_headLeaves[i].name, out cm)) { _headLeafClone[i] = cm; leafMapped++; }
        }
        _sb.AppendLine("头簇叶子骨抽样 " + got + " 枚（基准实例里找到同名 " + leafMapped + " 枚）：" + NamesOf(_headLeaves, got));
        _sb.AppendLine();

        // ── 相机：头近景 ──
        _cam = MakeCam("PROBE_CAM_H27");
        _focusRef = carrier.position + FlatN(_modelRoot.forward) * 0.45f;
        _dist = 3.0f;

        // ── rest 档：强行回 FBX 姿态 ──
        _sb.AppendLine("── 主判据：头载体 `_spine[" + (_NS - 2) + "]`（" + carrier.name + "，头簇挂点）"
                       + "容器相对旋转 相对 rest 帧的偏差 ──");
        _sb.AppendLine("   档位            相位     总偏差    俯仰    偏航    滚转   头簇局部旋转自检");
        Snapshot();
        // ★ 出 rest 图前**停掉 EnemyDragon**：驱动若在渲染前把骨骼写回去，基准帧就是假的。
        _drgC.enabled = false;
        // ★★ 关键：`SkinnedMeshRenderer` 蒙皮在 **PostLateUpdate** 统一算，
        //   手动 `Camera.Render()` 读到的是**上一帧**算好的蒙皮网格。
        //   同帧改完骨骼立刻渲染 ⇒ 网格还是旧的（第一版就是这么报废的）。
        //   所以必须：按一帧 → 再按一帧 → 第三帧才渲染。
        ApplyRest();
        yield return null;
        ApplyRest();
        yield return null;
        ApplyRest();
        Quaternion carrierRest = Quaternion.Inverse(_modelRoot.rotation) * carrier.rotation;
        _carrierRestRel = carrierRest;
        _restFwd = carrierRest * Vector3.forward;
        Row("rest(FBX)", -1f, carrierRest, true);
        ShootAll("rest");
        _drgC.enabled = true;
        RestoreSnapshot();
        yield return null;
        yield return null;

        // ── old / new 两档 × 三相位 ──
        bool[] modes = new bool[] { false, true };
        string[] tags = new string[] { "old", "new" };
        for (int m = 0; m < 2; m++)
        {
            fUseRest.SetValue(_drg, modes[m]);
            yield return null;
            for (int k = 0; k < PHASES.Length; k++)
            {
                yield return WaitPhase(PHASES[k]);
                Quaternion rel = Quaternion.Inverse(_modelRoot.rotation) * carrier.rotation;
                Row(tags[m], PHASES[k], rel, false);
                if (m == 0 || k == 0) ShootAll(tags[m] + "_ph" + ((int)(PHASES[k] * 100)).ToString("D3"));
            }
        }

        // ── 闭环验证：新锁 + headAlignPitchDeg = 0 ⇒ 应当 **0.00°**（与 FBX 一模一样）──
        //   因为 headExtra = Euler(-pitch, yaw, roll)；三个角全 0 时 headExtra = 单位
        //   ⇒ 头载体旋转恒等于基准。若实测不是 0，说明「12.00° = headExtra」这条推理是错的。
        fUseRest.SetValue(_drg, true);
        fPitch.SetValue(_drg, 0f);
        yield return null;
        yield return WaitPhase(PHASES[0]);
        {
            Quaternion rz = Quaternion.Inverse(_modelRoot.rotation) * carrier.rotation;
            Row("new+pitch0", PHASES[0], rz, false);
        }
        ShootAll("new0_ph000");
        fPitch.SetValue(_drg, -12f);

        _sb.AppendLine();
        _sb.AppendLine("★ 读法：");
        _sb.AppendLine("  · 「总偏差」= 该帧头载体相对 rest(FBX) 帧被拧了多少度 ⇒ **0 = 与模型师傅摆的一模一样**。");
        _sb.AppendLine("  · old = `headLockUseRestPose: 0`（锁拉直后的 _baseRel，第二十五轮行为）。");
        _sb.AppendLine("  · new = `headLockUseRestPose: 1`（锁拉直前的 _restRel，本轮修法）。");
        _sb.AppendLine("  · 「头簇局部旋转自检」= 抽样的叶子骨局部旋转与 rest 帧的最大夹角；");
        _sb.AppendLine("    必须 ≈ 0 ⇒ 证明头是**刚性**的、「挂点姿态 = 头姿态」成立。");
        _sb.AppendLine("  · **new+pitch0 = 新锁 + headAlignPitchDeg=0 ⇒ 应为 0.00°**（完全回到 FBX 原生姿态）。");
        _sb.AppendLine("  · **头簇相对挂点 Δdir / Δrot**：逐叶比「相对挂点的方向」与朝向（对应用户口述判据），与渲染/相机全无关。");
        _sb.AppendLine("    ★ Δdir 是**归一化方向角**：均匀缩放不改变方向 ⇒ 两个实例 scale 不同也能比。");
        _sb.AppendLine("    ★ 头簇是刚性不驱动分支 ⇒ Δdir / Δrot 在**每一行**都必须 =0（含 rest 行）；");
        _sb.AppendLine("      它证明的是「锁定没有把头的形状拧变形」，**不是**分辨档位的判据 —— 分辨力在「总偏差」列。");
        _sb.AppendLine("  · **半径比** = |叶骨-挂点| 活体 / 基准（均值）。第一版这里报的是「Δpos 0.1935m」，");
        _sb.AppendLine("    恒定出现在**包括 rest 在内**的每一行 ⇒ 那是跨实例标尺差，不是姿态误差。");
        _sb.AppendLine("    ★ 来源已由 drg_scale26.cs 实测钉死：`Z_Enemy_MoLong.prefab` 根 localScale = 1.0000");
        _sb.AppendLine("      （骨骼 lossy 0.0024），`Z_Dragon.prefab` 根 localScale = 0.7546（骨骼 lossy 0.0018）");
        _sb.AppendLine("      ⇒ 1 / 0.7546 = **1.3252**，与上表半径比逐位相同。纯均匀缩放，与姿态无关；");
        _sb.AppendLine("      0.1935 m 也由此对上：0.1935 / 0.3252 = 0.595 m = 叶骨到挂点的平均距离。");
        _sb.AppendLine("  · rest 档只有一行（它与相位无关，本来就不该随相位变）。");
        _sb.AppendLine("  · 出图 " + _shots + " 张 → " + DIR);

        if (_cam != null) { if (_cam.targetTexture != null) UnityEngine.Object.Destroy(_cam.targetTexture); UnityEngine.Object.Destroy(_cam.gameObject); }
        if (_clone != null) UnityEngine.Object.Destroy(_clone);
        yield return Done();
    }

    // ───────────────────────── 测量 ─────────────────────────

    void Row(string tag, float phase, Quaternion rel, bool isRest)
    {
        var carrier = _spine[_NS - 2] as Transform;
        Quaternion d = rel * Quaternion.Inverse(_carrierRestRel);
        Vector3 v = d * _restFwd;
        float tot = Quaternion.Angle(rel, _carrierRestRel);
        float elev = Mathf.Asin(Mathf.Clamp(v.normalized.y, -1f, 1f)) * Mathf.Rad2Deg;
        float yaw = Mathf.Atan2(v.normalized.x, v.normalized.z) * Mathf.Rad2Deg;
        float roll = RollDelta(rel, _carrierRestRel, _restFwd);

        float hp, hr, hratio;
        HeadShapeVsRest(out hp, out hr, out hratio);
        float leafMax = 0f;
        for (int i = 0; i < _headLeaves.Length; i++)
        {
            if (_headLeaves[i] == null) continue;
            float a = Quaternion.Angle(_headLeaves[i].localRotation, _headLeafLocal[i]);
            if (a > leafMax) leafMax = a;
        }
        if (isRest) leafMax = 0f;

        _sb.AppendLine("   " + Pad(tag, 16)
                       + Pad(phase < 0f ? "-" : phase.ToString("F2"), 8)
                       + Pad(tot.ToString("F2") + "°", 9)
                       + Pad(elev.ToString("+0.0;-0.0"), 8)
                       + Pad(yaw.ToString("+0.0;-0.0"), 8)
                       + Pad(roll.ToString("+0.0;-0.0"), 8)
                       + Pad(isRest ? "（基准帧）" : leafMax.ToString("F4") + "°", 16)
                       + "头pos" + carrier.position.ToString("F1")
                       + " 头簇相对挂点 Δdir" + hp.ToString("F2") + "° Δrot" + hr.ToString("F3") + "° 半径比" + hratio.ToString("F4")
                       + " SMR" + (_smrs != null ? _smrs.Length : -1)
                       + " 包围盒中心" + (_smrs != null && _smrs.Length > 0 && _smrs[0] != null
                            ? _smrs[0].bounds.center.ToString("F1") : "-"));
    }

    static float RollDelta(Quaternion live, Quaternion rest, Vector3 fwdRef)
    {
        Vector3 f = live * fwdRef;
        if (f.sqrMagnitude < 1e-8f) return 0f;
        f.Normalize();
        Vector3 upL = live * Vector3.up; upL -= f * Vector3.Dot(upL, f);
        Vector3 upR = rest * Vector3.up; upR -= f * Vector3.Dot(upR, f);
        if (upL.sqrMagnitude < 1e-10f || upR.sqrMagnitude < 1e-10f) return 0f;
        upL.Normalize(); upR.Normalize();
        Vector3 r = Vector3.Cross(f, upR);
        return Mathf.Atan2(Vector3.Dot(upL, r), Vector3.Dot(upL, upR)) * Mathf.Rad2Deg;
    }

    // ───────────────────────── 强行回 FBX 姿态 ─────────────────────────

    void Snapshot()
    {
        var all = _modelRoot.GetComponentsInChildren<Transform>(true);
        _saveRot.Clear();
        for (int i = 0; i < all.Length; i++)
        {
            _saveRot[all[i]] = all[i].localRotation;
        }
        for (int i = 0; i < _headLeaves.Length; i++)
            if (_headLeaves[i] != null) _headLeafLocal[i] = _headLeaves[i].localRotation;
    }

    void ApplyRest()
    {
        var all = _modelRoot.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < all.Length; i++)
        {
            if (_mirror[i] == null) continue;
            all[i].localRotation = _mirror[i].localRotation;
            // ★ 绝不能写 localPosition：容器节点的 local 位置一旦被从另一个实例覆盖，
            //   模型会被整块推出画面（第一版就是这么掉进去的）。骨骼姿态只取决于旋转。
        }
    }

    void RestoreSnapshot()
    {
        foreach (var kv in _saveRot) if (kv.Key != null) kv.Key.localRotation = kv.Value;
    }

    /// <summary>
    /// 头簇**相对挂点**的几何 vs 纯净基准实例：返回最大**方向**偏差(°)、最大转角(°)、半径比。
    /// ★ 第二十六轮补：**方向**才是姿态判据，位置差不是 —— 见方法体内注释。
    /// ★ 这直接对应判据来源「跟脖子的相对位置是对的，方向也是」；
    ///   与渲染/相机/背景全都无关。
    /// </summary>
    void HeadShapeVsRest(out float maxDir, out float maxRot, out float rRatio)
    {
        maxDir = 0f; maxRot = 0f; rRatio = 0f; int m = 0; float sum = 0f;
        var carrier = _spine[_NS - 2] as Transform;
        if (carrier == null || _cloneCarrier == null) return;
        Quaternion ic = Quaternion.Inverse(carrier.rotation);
        Quaternion ir = Quaternion.Inverse(_cloneCarrier.rotation);
        for (int i = 0; i < _headLeaves.Length; i++)
        {
            var a = _headLeaves[i]; var b = _headLeafClone[i];
            if (a == null || b == null) continue;
            Vector3 pa = ic * (a.position - carrier.position);
            Vector3 pb = ir * (b.position - _cloneCarrier.position);
            // ★★ 第二十六轮补（尺子自检）：**必须归一化后再比**。
            //   直接取 |pa-pb| 会把两个实例的累计 scale 差算进来（FBX 厘米制，
            //   SMR.lossyScale ≈ 0.0024 量级）⇒ 实测该差值**恒为 0.1935 m**，
            //   连 rest 行自己都照读 0.1935 ⇒ 那是**标尺底噪**，不是姿态误差。
            //   而头簇是刚性、不驱动的分支 ⇒ 位置差在任何档位都不会变，
            //   这一列**天生不能分辨档位**。均匀缩放不改变「相对挂点」的方向，
            //   所以归一化后的方向差才是干净、可比的姿态判据。
            float la = pa.magnitude, lb = pb.magnitude;
            if (la > 1e-6f && lb > 1e-6f)
            {
                float dp = Vector3.Angle(pa, pb);
                if (dp > maxDir) maxDir = dp;
                sum += la / lb; m++;
            }
            float dr = Quaternion.Angle(ic * a.rotation, ir * b.rotation);
            if (dr > maxRot) maxRot = dr;
        }
        rRatio = m > 0 ? sum / m : 0f;
    }

    // ───────────────────────── 出图 ─────────────────────────

    /// <summary>
    /// 头簇叶子骨质心当视心。
    /// ★ 不能用「颈根 + 水平前向偏移」：基准姿态下颈是弯的，
    ///   水平偏移会把视心推进身体里（第一版近景就是这么报废的）。
    /// </summary>
    Vector3 HeadFocus()
    {
        Vector3 c = Vector3.zero; int n = 0;
        for (int i = 0; i < _headLeaves.Length; i++)
            if (_headLeaves[i] != null) { c += _headLeaves[i].position; n++; }
        if (n >= 3) return c / n;
        return (_spine[_NS - 2] as Transform).position;
    }

    /// <summary>整条身体的中点（动态，不管哪一档都在画面内）。</summary>
    Vector3 BodyFocus()
    {
        Vector3 a = ((Transform)_spine[0]).position;
        Vector3 b = ((Transform)_spine[_NS - 1]).position;
        return (a + b) * 0.5f;
    }

    void ShootAll(string tag)
    {
        Vector3 focus = HeadFocus();
        Shot("head_" + tag + "_side", focus, 3.6f, 0.10f, 0.99f, 0.07f);
        Shot("head_" + tag + "_top", focus, 3.6f, 0.05f, 0.15f, 0.99f);
        Shot("head_" + tag + "_front", focus, 3.6f, 0.99f, 0.12f, 0.06f);
        // ★ 补一张远景：即使近景取景出错，眼睛也能在远景里判姿态
        Shot("wide_" + tag + "_side", BodyFocus(), 14f, 0.05f, 0.99f, 0.06f);
    }

    void Shot(string name, Vector3 focus, float dist, float aFwd, float aRight, float aUp)
    {
        Vector3 fwd = FlatN(_modelRoot.forward);
        Vector3 up = _modelRoot.up.normalized;
        Vector3 right = Vector3.Cross(up, fwd).normalized;
        Vector3 dir = (fwd * aFwd + right * aRight + up * aUp).normalized;
        _cam.transform.position = focus + dir * dist;
        _cam.transform.LookAt(focus, up);
        File.WriteAllBytes(DIR + "/" + name + ".png", ShotBytes(_cam));
        _shots++;
    }

    static Vector3 FlatN(Vector3 v) { v.y = 0f; if (v.sqrMagnitude < 1e-8f) v = Vector3.forward; return v.normalized; }

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

    IEnumerator WaitPhase(float target)
    {
        for (int i = 0; i < 240; i++)
        {
            float fr = Mathf.Repeat(DRIVE_HZ * Time.time, 1f);
            float d = Mathf.Abs(fr - target);
            if (d > 0.5f) d = 1f - d;
            if (d < 0.012f) yield break;
            yield return null;
        }
    }

    static string NamesOf(Transform[] a, int n)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < n; i++) { if (i > 0) sb.Append(", "); sb.Append(a[i].name); }
        return sb.ToString();
    }

    static string Pad(string s, int n) { return s.Length >= n ? s : s + new string(' ', n - s.Length); }

    IEnumerator Done()
    {
        Time.captureFramerate = 0;
        try { File.WriteAllText(RP, _sb.ToString()); } catch { }
        Debug.Log("[drg_headfix] 写入 " + RP + "  出图 " + _shots + " 张");
        yield return null;
    }
}

var __hostH27 = new GameObject("drg_headfix");
__hostH27.AddComponent<drg_headfix>();
return "DRG_HEADFIX_STARTED";
