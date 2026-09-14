using System.Collections.Generic;
using UnityEngine;

namespace InkWash.Player
{
    /// <summary>
    /// 全身贴地夹紧（问题 6「攻击时脚陷进地面」）。
    ///
    /// 为什么需要它 —— 实测数据（逐顶点 BakeMesh 取真实网格最低点，基准为角色正下方的
    /// 真实地面；当前角色 = Char_Feng，模型原点就在脚底，Visual.localScale = 2.213）：
    ///
    ///   状态      无IK最低   采用 stateOffsets.y
    ///   Idle       +0.0033     -0.0003
    ///   Walk       -0.0539     +0.0569
    ///   Run        +0.2324     -0.2294
    ///   Dash       +0.0071     -0.0041
    ///   Atk1       -0.0416     +0.0446
    ///   Atk1Rec    +0.0060     -0.0030
    ///   Atk2       -0.0464     +0.0494
    ///   Atk2Rec    +0.0008     +0.0022
    ///   Atk3       -0.0099     +0.0129
    ///
    /// 有正有负：Walk/攻击段髋压得低、脚顺着 FK 沉进地面（要抬），Run 整段被动画抬高（要压）。
    /// 哪一段偏多少，是"每段动画 × 当前骨架"的属性 —— 换模型或换动画包必须整表重测。
    ///
    /// ⚠ 单位：本表是**世界米**，照着填即可，不必按模型缩放手工折算 ——
    ///   输出通道是**模型容器（Animator 的直系子节点，本工程 = `Visual`）的 localPosition.y**，
    ///   其父节点（角色根）无缩放，所以 local Y 与世界米 1:1，天然与模型缩放解耦。
    ///   ⚠ 反例（本组件踩过）：不要改用 `anim.bodyPosition` —— 那是 Animator 局部空间，
    ///   模型带整体缩放 `s` 时世界效果是 `x · s²`（Feng 的 Visual.localScale=2.213 → 实测放大 4.90 倍）。
    ///
    /// 怎么量（踩过的坑）：**按归一化时间扫**，不要按"真实帧数"扫。
    ///   最早那版标定脚本 `anim.Play(state,0,0)` 之后数帧采样、并且硬性截断在 90 帧；
    ///   编辑器跑到 200+ fps 时 90 帧只够跑半个循环，Running_A（0.80s/循环）的落地相
    ///   整段被跳过，量出 0.0723 —— 比真值小 9cm，照它落地的偏移会让跑步整段沉进地里。
    ///   改成 nt=0..1 均匀取 121 点后，量与姿态体检的 0.1626 完全一致。
    ///
    /// ⚠ 这些偏移**绑死在"片段 × 骨架"上**：换过三次都得重测 ——
    ///   UAL1/UAL2/KiAnim 拼装时 Run 是 +0.049（偏移 -0.046）；换 KayKit Running_A 后
    ///   变 +0.1626（偏移 -0.160）；再换成 Char_Feng 后变 +0.2324（偏移 -0.2294）。
    ///   忘改 → 脚穿地或浮空，姿态体检里「落脚状态脚不穿地 / 不浮空」两条断言直接判未通过。
    ///   重测流程：Tools/cs/f_calib_footik.cs（按归一化时间扫）→ Tools/cs/f_apply_footik.cs（写入）。
    ///
    /// 两类毛病方向相反，分别治 —— 注意**两级补偿各管一段，不可重叠**：
    ///
    /// 1. **整段偏移** → <see cref="stateOffsets"/>（静态，每段一个常数）
    ///    动画把骨盆摆在角色原点上方（或下方）一定高度，偏的是"摆多少"这段常数，
    ///    解法就是给每段动画一个竖直基准偏移把它压回/抬回地面。
    ///    另：静态竖直抵消由预制体里的 Visual.localPosition.y 承担 —— 该值取"模型原点相对脚底的位置"。
    ///    KayKit Rogue_Hooded 的原点在脚底上方 0.122m（取 0.088）；Char_Feng 的原点就在脚底（取 0）。
    ///    换模型时这个值必须一起重定，剩下的残差才归本表处理。
    ///
    /// 2. **帧内穿地** → BodyLift（动态，只抬不压）
    ///    后摇/收招这类"髋压低"的片段，脚顺着 FK 一起沉下去，靠帧内抬升救。
    ///    ⚠ 它只补**静态偏移之后的残差**（need 里必须减掉 BodyBase）—— 这一条踩过坑，见 ApplyBodyOffset。
    ///
    /// 为什么量**真实网格最低点**，而不是脚踝/脚趾骨骼
    /// ------------------------------------------------
    /// 一开始用的是 Animator.GetIKPosition(AvatarIKGoal.Foot)（脚部 IK 锚点）。它对平脚
    /// 姿势够用，脚一勾起来就失效：实测 Atk3 nt=0.25 时锚点在 -0.082，而真实网格最低点在
    /// -0.191 —— 鞋底比锚点低 0.109m。补采 HumanBodyBones.Toes 骨也不行，趾骨在那一刻
    /// 仍比鞋底高 0.097m。骨骼点抓不住网格外表面，只有量网格本身才准。
    /// 代价是每帧一次 SkinnedMeshRenderer.BakeMesh（本角色约 8.5k 顶点），单角色完全可接受；
    /// 用复用的 Mesh + List 避免每帧 GC。
    ///
    /// 为什么只做"抬升"、不做"下压"
    /// ----------------------------
    /// 逐点钉地会把走路的**摆动相**（抬腿那半周期）也钉住 —— 脚粘在地上，走路就废了。
    /// 只抬不压则天然安全：抬升量只取正值，绝不下拽，所以跳跃/翻滚这类"脚本来就该离地"的
    /// 动作不受影响，斜坡上也成立（探的是最低点正下方的真实地面）。"整段偏高"这种静态偏差
    /// 由基准偏移处理，不需要按帧去压。
    ///
    /// 必须在 Animator 所在层开 IK Pass（见 Tools/cs/s2_build_animator.cs），
    /// 否则 OnAnimatorIK 根本不会被调用。
    /// </summary>
    [RequireComponent(typeof(Animator))]
    [DisallowMultipleComponent]
    public class FootIK : MonoBehaviour
    {
        [System.Serializable]
        public struct StateYOffset
        {
            [Tooltip("Animator layer0 的状态名")]
            public string state;
            [Tooltip("该状态的身体竖直基准偏移（米）。正 = 抬高；用来抵消该段动画自带的浮空")]
            public float y;
        }

        [Header("开关")]
        public bool enableIk = true;

        [Tooltip("这些状态不做『帧内贴地』：翻滚这类动作身体本来就贴地滚，按常规夹地面会把身体顶起来。" +
                 "注意基准偏移(stateOffsets)在这些状态下**依然生效**")]
        public string[] noGroundFixStates = { "Dash" };

        [Tooltip("竖直偏移的输出节点。留空则自动取 Animator 下最靠上的蒙皮网格祖先（模型容器，本工程 = Visual）。\n" +
                 "⚠ 不要改成用 anim.bodyPosition 写偏移：那是 Animator 局部空间，模型带整体缩放时\n" +
                 "世界效果会变成**缩放倍率的平方**（Feng 的 Visual.localScale=2.213 实测放大 4.90 倍），\n" +
                 "标定值直接失真、帧内抬升的反馈环增益也跟着 ×4.9 而震荡 —— 这个坑本组件已经踩过一次。")]
        public Transform bodyOffsetTarget;

        [Header("贴地")]
        [Tooltip("网格最低点与地面的目标间隙（米）。0 = 正好落在踏面上")]
        public float groundClearance = 0.005f;

        [Tooltip("从最低点上方多高开始向下探地面")]
        public float probeUp = 0.5f;

        [Tooltip("向下探测的最大距离。超过这个深度就认为「没地面信息」，不做修正")]
        public float probeDown = 1.5f;

        [Tooltip("哪些层算地面")]
        public LayerMask groundMask = ~0;

        [Header("平滑")]
        [Tooltip("抬升量的收敛速度（越大越硬）。太小会跟不上快速下蹲，太大会看到弹跳")]
        public float liftSmooth = 14f;

        [Tooltip("单次最大抬升，防止探到异常地面时把人弹飞")]
        public float maxLift = 0.8f;

        [Header("每段动画的竖直基准偏移")]
        [Tooltip("由 Tools/cs/f_calib_footik.cs 量、Tools/cs/f_apply_footik.cs 推送。\n" +
                 "值为『该段动画无 IK 时网格最低点相对地面』取负（**世界米**）—— 让最低点正好落到地面。\n" +
                 "直接按世界米填即可，不必手工折算模型缩放（输出通道是模型容器的 localPosition.y，1:1）。\n" +
                 "换模型/换动画包（片段）后必须整表重测，否则脚会穿地或浮空。")]
        public StateYOffset[] stateOffsets;

        // ---------------- 只读探针（自动化验收用，勿删） ----------------

        /// <summary>当前状态名（layer0）。取不到时为 null。</summary>
        public string CurrentState { get; private set; }

        /// <summary>当前状态施加的竖直基准偏移（米）。</summary>
        public float BodyBase { get; private set; }

        /// <summary>当前施加的帧内动态抬升量（米）。</summary>
        public float BodyLift { get; private set; }

        /// <summary>本帧网格最低点的世界 Y（还没加 BodyLift）。</summary>
        public float LowestMeshY { get; private set; }

        /// <summary>本帧探测到的**残差**穿地深度（米，已扣掉静态基准偏移、未平滑）。0 = 没有穿地。</summary>
        public float RawPenetration { get; private set; }

        /// <summary>本帧是否拿到了有效的地面信息。</summary>
        public bool HasGroundInfo { get; private set; }

        /// <summary>当前实际写入模型容器的竖直偏移（**世界米**）。</summary>
        public float AppliedBodyOffset => _appliedOffset;

        /// <summary>竖直偏移的输出节点（诊断用）。</summary>
        public Transform OffsetTarget => _offsetTarget;

        // ---------------- 内部 ----------------
        private Animator _anim;
        private PlayerController _ctl;
        private Transform _selfRoot;

        private SkinnedMeshRenderer[] _smrs;
        private Mesh _bakeMesh;
        private readonly List<Vector3> _vbuf = new List<Vector3>(16384);
        private Vector3 _lowestWorld;

        // 竖直偏移的输出通道（见 <see cref="ApplyBodyOffset"/>）
        private Transform _offsetTarget;         // 模型容器节点（Animator 的直系子节点，例如 Visual）
        private Vector3 _baseLocalPos;           // 该节点在预制体里的原始 localPosition
        private float _offsetParentScale = 1f;   // 其父节点的 lossyScale.y（通常为 1）
        private float _appliedOffset;            // 上一帧实际写入的偏移（世界米）

        private void Awake()
        {
            _anim = GetComponent<Animator>();
            _ctl = GetComponent<PlayerController>();
            _selfRoot = _ctl != null ? _ctl.transform : transform;

            _smrs = GetComponentsInChildren<SkinnedMeshRenderer>(true);
            _bakeMesh = new Mesh { name = "FootIK_Bake" };
            _bakeMesh.MarkDynamic();

            ResolveOffsetTarget();

            if (_anim != null && !_anim.isHuman)
                Debug.LogWarning("[FootIK] 只支持 Humanoid；当前 Avatar 不是 Humanoid，贴地不会生效");
        }

        /// <summary>
        /// 选竖直偏移的输出节点：Animator 下**最靠上**的那个"蒙皮网格祖先"，
        /// 也就是模型容器（本工程是 Visual）。它必须是 Animator 的直系子节点，
        /// 这样它的 localPosition.y 的父节点无缩放 —— 世界效果 1:1。
        /// </summary>
        private void ResolveOffsetTarget()
        {
            if (bodyOffsetTarget != null) { _offsetTarget = bodyOffsetTarget; }
            else if (_smrs != null && _smrs.Length > 0 && _smrs[0] != null)
            {
                var t = _smrs[0].transform;
                while (t.parent != null && t.parent != transform) t = t.parent;
                if (t.parent == transform) _offsetTarget = t;
            }
            if (_offsetTarget == null)
            {
                Debug.LogWarning("[FootIK] 找不到竖直偏移的输出节点，贴地不会生效");
                return;
            }
            _baseLocalPos = _offsetTarget.localPosition;
            _offsetParentScale = _offsetTarget.parent != null && _offsetTarget.parent.lossyScale.y > 1e-4f
                ? _offsetTarget.parent.lossyScale.y : 1f;
        }

        private void OnDestroy()
        {
            if (_bakeMesh != null) Destroy(_bakeMesh);
        }

        private void OnDisable()
        {
            BodyLift = 0f;
            _appliedOffset = 0f;
            if (_offsetTarget != null) _offsetTarget.localPosition = _baseLocalPos;
        }

        /// <summary>layer0 当前状态名。AnimatorStateInfo 不给名字，只能拿 shortNameHash 去表里反查。</summary>
        private string ResolveStateName()
        {
            if (_anim == null) return null;
            int h = _anim.GetCurrentAnimatorStateInfo(0).shortNameHash;

            if (noGroundFixStates != null)
                for (int i = 0; i < noGroundFixStates.Length; i++)
                    if (!string.IsNullOrEmpty(noGroundFixStates[i])
                        && Animator.StringToHash(noGroundFixStates[i]) == h) return noGroundFixStates[i];

            if (stateOffsets != null)
                for (int i = 0; i < stateOffsets.Length; i++)
                    if (!string.IsNullOrEmpty(stateOffsets[i].state)
                        && Animator.StringToHash(stateOffsets[i].state) == h) return stateOffsets[i].state;

            return null;
        }

        private bool IsNoGroundFix(string state)
        {
            if (string.IsNullOrEmpty(state) || noGroundFixStates == null) return false;
            for (int i = 0; i < noGroundFixStates.Length; i++) if (noGroundFixStates[i] == state) return true;
            return false;
        }

        private float ResolveStateOffset(string state)
        {
            if (string.IsNullOrEmpty(state) || stateOffsets == null) return 0f;
            for (int i = 0; i < stateOffsets.Length; i++)
                if (stateOffsets[i].state == state) return stateOffsets[i].y;
            return 0f;
        }

        /// <summary>量出当前姿势下**真实网格最低点**的世界坐标。用复用的 Mesh / List，避免每帧 GC。</summary>
        private Vector3 MeasureLowestMeshPoint()
        {
            if (_smrs == null || _smrs.Length == 0) return transform.position;
            Vector3 lowest = Vector3.zero;
            float lo = float.MaxValue;

            for (int k = 0; k < _smrs.Length; k++)
            {
                var smr = _smrs[k];
                if (smr == null || !smr.enabled) continue;
                smr.BakeMesh(_bakeMesh);
                _bakeMesh.GetVertices(_vbuf);
                var m = smr.transform.localToWorldMatrix;

                for (int i = 0; i < _vbuf.Count; i++)
                {
                    var v = _vbuf[i];
                    // 只需要世界 Y，省掉两次乘法
                    float y = m.m10 * v.x + m.m11 * v.y + m.m12 * v.z + m.m13;
                    if (y < lo) { lo = y; lowest = m.MultiplyPoint3x4(v); }
                }
            }
            _lowestWorld = lowest;
            LowestMeshY = lo;
            return lowest;
        }

        /// <summary>
        /// 在某点正下方找地面高度。用 RaycastAll 并剔掉角色自己的碰撞体；忽略 Trigger
        /// （攻击判定框悬在半空，一旦被当成地面，穿地判定就整体失真）。
        /// </summary>
        private bool ProbeGround(Vector3 worldPos, out float groundY)
        {
            groundY = 0f;
            var hits = Physics.RaycastAll(worldPos + Vector3.up * probeUp, Vector3.down,
                probeUp + probeDown, groundMask, QueryTriggerInteraction.Ignore);
            bool found = false;
            float best = float.MinValue;
            for (int i = 0; i < hits.Length; i++)
            {
                var tr = hits[i].collider.transform;
                if (_selfRoot != null && (tr == _selfRoot || tr.IsChildOf(_selfRoot))) continue;
                if (hits[i].point.y > best) { best = hits[i].point.y; found = true; }
            }
            if (!found) return false;
            groundY = best;
            return true;
        }

        private void OnAnimatorIK(int layerIndex)
        {
            if (layerIndex != 0) return;
            if (!enableIk || _anim == null || !_anim.isHuman) return;

            CurrentState = ResolveStateName();
            BodyBase = ResolveStateOffset(CurrentState);

            float k = 1f - Mathf.Exp(-liftSmooth * Mathf.Max(Time.deltaTime, 1e-4f));

            // 量到的是"含上一帧偏移"的姿势（偏移写在模型容器上、会一直保留到本帧），
            // 剥掉上一帧写入的量才是动画本体的最低点 —— 语义与旧版 LowestMeshY 一致。
            Vector3 low = MeasureLowestMeshPoint();
            low.y -= _appliedOffset;
            LowestMeshY = low.y;

            // 翻滚/腾空类状态：只保留基准偏移，不做帧内夹地
            if (IsNoGroundFix(CurrentState))
            {
                RawPenetration = 0f;
                HasGroundInfo = false;
                BodyLift = Mathf.MoveTowards(BodyLift, 0f, 3f * Time.deltaTime);
                ApplyBodyOffset();
                return;
            }

            float groundY;
            if (!ProbeGround(low, out groundY))
            {
                HasGroundInfo = false;
                RawPenetration = 0f;
                BodyLift = Mathf.Lerp(BodyLift, 0f, k);
                ApplyBodyOffset();
                return;
            }

            HasGroundInfo = true;
            // ⚠ 必须扣掉已施加的 BodyBase —— BodyLift 只负责补"静态偏移之后的残差"。
            //   踩过的坑（2026-09-14 换 Feng 后暴露）：不留神就用原始姿态算 need，
            //   于是 BodyBase 抬一次、BodyLift 又按同一个穿地量再抬一次，重复补偿 → 整段浮空。
            //   旧模型（KayKit）之所以没暴露，是因为它 9 段偏移全为负、原始最低点恒为正，
            //   need 恒 ≤ 0、BodyLift 始终为 0，重复补偿这一路根本没被走到。
            float need = (groundY + groundClearance) - low.y - BodyBase;
            RawPenetration = Mathf.Max(0f, need);

            // 指数收敛：帧率无关，且不会像 Lerp(固定系数) 那样在高帧率下变慢
            BodyLift = Mathf.Lerp(BodyLift, Mathf.Clamp(need, 0f, maxLift), k);

            ApplyBodyOffset();
        }

        /// <summary>
        /// 把「状态基准偏移 + 帧内抬升」作为**整体竖直平移**写到模型容器上（不动骨骼姿态）。
        ///
        /// 为什么写 <see cref="bodyOffsetTarget"/> 的 localPosition，而不是 anim.bodyPosition
        /// --------------------------------------------------------------------------
        /// 踩过的坑（2026-09-14 换主角成 Char_Feng 时暴露，代价是两轮"体检全红"）：
        ///   `anim.bodyPosition` 是 **Animator 局部空间**的量，模型带整体缩放时世界效果不等于名义值。
        ///   实测（Feng，Visual.localScale = 2.213）：写入 0.0257 实际抬起 0.1260 —— 放大 **4.90 倍**，
        ///   正好是 2.213²。同一通道上的 BodyLift 反馈增益也被放大 4.9 倍，于是必然过冲：
        ///     Walk 名义抬 0.057 结果飘 +0.134；Run 名义压 0.229 结果沉 -0.274。
        ///   而模型容器是 Animator 的**直系子节点**，父节点无缩放 → 它的 localPosition.y 就是世界米，
        ///   1:1、与模型缩放彻底解耦。标定表也因此可以直接按世界米填，不需要任何手工折算。
        /// </summary>
        private void ApplyBodyOffset()
        {
            if (_offsetTarget == null) return;
            _appliedOffset = BodyBase + BodyLift;
            var p = _baseLocalPos;
            p.y += _appliedOffset / _offsetParentScale;
            _offsetTarget.localPosition = p;
        }
    }
}
