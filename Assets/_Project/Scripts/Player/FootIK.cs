using System.Collections.Generic;
using UnityEngine;

namespace InkWash.Player
{
    /// <summary>
    /// 全身贴地夹紧（问题 6「攻击时脚陷进地面」）。
    ///
    /// 为什么需要它 —— 实测数据（逐顶点 BakeMesh 取真实网格最低点，基准为角色正下方的
    /// 真实地面；角色 = KayKit Rogue_Hooded）：
    ///
    ///   状态      无IK最低   采用 stateOffsets.y
    ///   Idle       +0.1159     -0.1129
    ///   Walk       +0.0798     -0.0733
    ///   Run        +0.1626     -0.1600
    ///   Dash       +0.0980     -0.0908
    ///   Atk1       +0.1070     -0.1069
    ///   Atk1Rec    +0.1160     -0.1122
    ///   Atk2       +0.1040     -0.0927
    ///   Atk2Rec    +0.1150     -0.1117
    ///   Atk3       +0.1080     -0.1049
    ///
    /// 表里**全为正**：这套动画的骨盆基准整体比角色原点高 8~16cm，不修就是整段浮空。
    /// 哪一段偏高多少，是每段动画自己的属性 —— 换动画包必须整表重测。
    ///
    /// 怎么量（踩过的坑）：**按归一化时间扫**，不要按"真实帧数"扫。
    ///   最早那版标定脚本 `anim.Play(state,0,0)` 之后数帧采样、并且硬性截断在 90 帧；
    ///   编辑器跑到 200+ fps 时 90 帧只够跑半个循环，Running_A（0.80s/循环）的落地相
    ///   整段被跳过，量出 0.0723 —— 比真值小 9cm，照它落地的偏移会让跑步整段沉进地里。
    ///   改成 nt=0..1 均匀取 121 点后，量与姿态体检的 0.1626 完全一致。
    ///
    /// ⚠ 这些偏移**绑死在片段上**：换成 KayKit 之前（UAL1 走跑 + UAL2 攻击 + KiAnim 跑步
    ///   三套拼装），Run 的无 IK 最低点是 +0.049，偏移是 -0.046；换成 KayKit Running_A 后
    ///   变成 +0.1626，偏移必须跟着改到 -0.160。忘改 → 脚穿地或浮空，
    ///   姿态体检里「落脚状态脚不穿地 / 不浮空」两条断言会直接判未通过。
    ///   重测流程：Tools/cs/s3_diag.cs（A 段细扫）→ Tools/cs/s3_apply_prefab_calib.cs（写入）。
    ///
    /// 两类毛病方向相反，分别治：
    ///
    /// 1. **整段偏移**（值全为正）—— 动画把骨盆摆在角色原点上方一定高度，这才是常态：
    ///    角色根在脚底，动画的髋部自然要抬到腿长的高度。偏的是"抬多少"这段常数，
    ///    解法就是给每段动画一个**竖直基准偏移**（<see cref="stateOffsets"/>，
    ///    由 Tools/cs/s3_apply_prefab_calib.cs 推送）把脚底压回地面。
    ///    另：KayKit 模型的原点还在脚底上方 0.122m，已由预制体里
    ///    Visual.localPosition.y = 0.088 先抵消掉一部分，剩下的残差归本表处理。
    ///
    /// 2. **帧内穿地**（最低点为负）—— 后摇/收招这类"髋压低"的片段，脚顺着 FK 一起沉下去。
    ///    解法见下面"为什么量网格而不是量骨骼"。
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
        [Tooltip("由 Tools/cs/s3_calib_footik.cs 量、Tools/cs/s3_apply_prefab_calib.cs 推送。\n" +
                 "值为『该段动画无 IK 时网格最低点相对地面』取负 —— 让最低点正好落到地面。\n" +
                 "换动画包（片段）后必须整表重测，否则脚会穿地或浮空。")]
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

        /// <summary>本帧探测到的穿地深度（米，未平滑）。0 = 没有穿地。</summary>
        public float RawPenetration { get; private set; }

        /// <summary>本帧是否拿到了有效的地面信息。</summary>
        public bool HasGroundInfo { get; private set; }

        // ---------------- 内部 ----------------
        private Animator _anim;
        private PlayerController _ctl;
        private Transform _selfRoot;

        private SkinnedMeshRenderer[] _smrs;
        private Mesh _bakeMesh;
        private readonly List<Vector3> _vbuf = new List<Vector3>(16384);
        private Vector3 _lowestWorld;

        private void Awake()
        {
            _anim = GetComponent<Animator>();
            _ctl = GetComponent<PlayerController>();
            _selfRoot = _ctl != null ? _ctl.transform : transform;

            _smrs = GetComponentsInChildren<SkinnedMeshRenderer>(true);
            _bakeMesh = new Mesh { name = "FootIK_Bake" };
            _bakeMesh.MarkDynamic();

            if (_anim != null && !_anim.isHuman)
                Debug.LogWarning("[FootIK] 只支持 Humanoid；当前 Avatar 不是 Humanoid，贴地不会生效");
        }

        private void OnDestroy()
        {
            if (_bakeMesh != null) Destroy(_bakeMesh);
        }

        private void OnDisable()
        {
            BodyLift = 0f;
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

            // 翻滚/腾空类状态：只保留基准偏移，不做帧内夹地
            if (IsNoGroundFix(CurrentState))
            {
                RawPenetration = 0f;
                HasGroundInfo = false;
                BodyLift = Mathf.MoveTowards(BodyLift, 0f, 3f * Time.deltaTime);
                ApplyBodyOffset();
                return;
            }

            // 量的是**还没加 BodyLift** 的姿势 —— ApplyBodyOffset 在本函数末尾才执行，
            // 而 bodyPosition 的写入只对当次评估生效、下一次评估会被动画本身重置，所以不会累积。
            Vector3 low = MeasureLowestMeshPoint();

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
            float need = (groundY + groundClearance) - low.y;
            RawPenetration = Mathf.Max(0f, need);

            float target = Mathf.Clamp(need, 0f, maxLift);
            // 指数收敛：帧率无关，且不会像 Lerp(固定系数) 那样在高帧率下变慢
            BodyLift = Mathf.Lerp(BodyLift, target, k);

            ApplyBodyOffset();
        }

        /// <summary>把「状态基准偏移 + 帧内抬升」加到动画的身体位置上（整体竖直平移，不动骨骼姿态）。</summary>
        private void ApplyBodyOffset()
        {
            float y = BodyBase + BodyLift;
            if (Mathf.Abs(y) < 1e-5f) return;
            Vector3 bp = _anim.bodyPosition;
            bp.y += y;
            _anim.bodyPosition = bp;
        }
    }
}
