using System;
using System.Collections.Generic;
using InkWash.Combat;
using UnityEngine;
using UnityEngine.AI;

namespace InkWash.Enemies
{
    /// <summary>
    /// 龙的四种攻击（全部**程序驱动**，不依赖任何动画片段）。
    ///
    /// ============ 为什么必须程序驱动（先写清楚，免得后来者以为这是偷懒）============
    /// 1) 素材本身不给可用动作。龙的源 FBX 只有**一段 57.5 s** 的曲线，
    ///    而且它是 **Generic 骨架**（275 根骨、名字是 `drgon_03`/`drgon_0204` 这种无意义编号），
    ///    **不能走 Humanoid 重定向** —— 人类骨架的 avatar 对它一个字节都用不上。
    /// 2) 那 57.5 s 也不是"一套动作"，而是一条**连续的长镜头**：
    ///    骨架是一条**单链脊骨** `drgon_03 → drgon_04 → … → drgon_026`（24 节），
    ///    片段里每根骨的 `m_LocalPosition` 都是几百上千的**位置曲线**（不是旋转），
    ///    这是 Blender 导出时把世界位移烘进了局部位置 —— 想切出"甩尾"这种局部动作，
    ///    要先把位置曲线反解成旋转，成本远高于直接写程序。
    /// 3) 更重要：**这类长链生物的"甩尾/盘绕"本来就是物理化的程序运动**，
    ///    用一条**相位延迟的正弦链**驱动比手工 K 帧更自然，也更容易调参。
    ///
    /// ============ 驱动原理：相位延迟的正弦链（"鞭子")============
    /// 设脊骨第 i 节（i 从 0 起），给每节叠加一个旋转偏移：
    ///     offset_i(t) = A · sin(2π·f·t − i·φ)
    /// 其中 φ 是**每节之间的相位差**。φ 越大，波形沿脊柱"跑"得越快 ⇒ 越像鞭子抽动。
    /// 这是柔性体/鱼尾动画的经典做法，关键好处是**不需要关键帧、帧率无关、可实时改参数**。
    ///
    /// 四种动作在**同一套驱动**上换参数：
    ///   · 甩尾（TailSweep）：横向大振幅（Y 轴旋转）+ 小相位差 ⇒ 整条尾巴一起扫
    ///   · 撕咬（Bite）    ：头部若干节前俯（X 轴旋转）+ 整体前冲位移
    ///   · 龙息（Breath）  ：颈部抬起（X 轴负向）+ 张嘴（末端节）+ 生成墨弹
    ///   · 盘旋（Hover）   ：升空 + 环绕玩家 + 轻微游动（低频小振幅）
    ///
    /// ============ 与现有战斗系统的接法 ============
    /// 继承 <see cref="EnemyBase"/> 复用它**已经验收过**的状态机（Spawning/Idle/Chase/Attack/HitStun/Dead）、
    /// NavMeshAgent 移动、IDamageable、击退、血条、经验。只重写三处：
    ///   · <see cref="ChooseAttack"/>  —— 按距离/冷却选哪一种
    ///   · <see cref="AttackMovement"/> —— 每帧推进动作曲线（含位移）
    ///   · <see cref="PerformHit"/>     —— 在判定帧开 Hitbox 或生成墨弹
    ///
    /// ★ 不使用 Animator。龙没有 controller，`EnemyBase` 里的 `HasParam` 会全部返回 false，
    ///   所以那些 `SetTrigger` 调用是**安全空转**，不需要改基类（基类已经对 null Animator 做了保护）。
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    public class EnemyDragon : EnemyBase
    {
        public enum DragonAttack { TailSweep, Bite, Breath, HoverOrbit }

        [Header("骨骼链（运行时自动定位，也可手工指定）")]
        [Tooltip("脊骨链的根（通常是 _rootJoint 的第一个子节点）。留空则自动找。")]
        public Transform spineRoot;
        [Tooltip("链节数（从 spineRoot 往下取连续单链的节数）。0 = 自动推导")]
        public int spineLinkCount = 0;
        [Tooltip("驱动时从第几节开始（前几节动起来会让整个身体晃，通常跳过）")]
        public int driveStartIndex = 2;

        [Header("姿态基准：出生时把脊骨的本地旋转记下来，所有偏移都叠在它上面")]
        [Tooltip("模型容器的本地 Y 偏移（让龙趴/浮在合理高度）")]
        public float bodyLift = 0f;

        [Header("动作 · 甩尾")]
        public float sweepAmplitudeDeg = 34f;
        public float sweepFrequency = 1.15f;
        public float sweepPhaseStepDeg = 26f;
        public float sweepYawAssist = 22f;

        [Header("动作 · 撕咬")]
        public float biteAmplitudeDeg = 30f;
        public float biteFrequency = 1.9f;
        public float bitePhaseStepDeg = 34f;
        public float biteLungeDistance = 3.4f;
        public int biteHeadLinks = 5;

        [Header("动作 · 龙息")]
        public float breathAmplitudeDeg = 22f;
        public float breathFrequency = 1.5f;
        public float breathPhaseStepDeg = 18f;
        public int breathHeadLinks = 4;
        public GameObject breathProjectilePrefab;
        public float breathProjectileSpeed = 15f;
        public float breathProjectileDamage = 14f;
        public int breathProjectileCount = 3;
        public float breathSpreadDeg = 9f;

        [Header("动作 · 盘旋")]
        public float hoverHeight = 3.6f;
        public float hoverOrbitRadius = 11f;
        public float hoverOrbitSpeedDeg = 55f;
        [Tooltip("★ 沿链的弯曲振幅（度/节）。旧值 11 太小 ⇒ 实测每节只弯 0.2~9.7°，龙是僵的")]
        public float hoverAmplitudeDeg = 10f;
        [Tooltip("★ 游动频率（Hz）。真实蛇形游动是**低频**，旧值 0.55 偏快但可接受")]
        public float hoverFrequency = 0.45f;
        [Tooltip("★ 相位步进（度/节）。这是决定「波长」的关键：波长(节数) = 360 / 此值。旧值 40 ⇒ 每 9 节一个完整波（24 节里塞 2.7 个波，像搓衣板）；新值 15 ⇒ 约每 24 节一个完整波，即身体长 ≈ 一个波长 —— 这才是真实蛇/龙的游动")]
        public float hoverPhaseStepDeg = 15f;
        [Tooltip("★ 垂直面弯曲振幅（度/节）。真实游动不是纯水平摆尾，身体会上下起伏；只做水平摆动是「扁片在转」，加上这个才有立体游动感")]
        public float hoverPitchAmplitudeDeg = 1.5f;
        [Header("★ 拉直基准（覆盖骨架自带的 C/S 弯姿）")]
        [Tooltip("龙的骨架**原生姿态是一条大 C/S 曲线** —— 实测 dg_chain：折线总长 8.266 m，" +
                 "首尾直线只有 4.179 m，逐节弯角 7.9°~65.6°。\n" +
                 "把游动波叠在这条弯基准上，身体永远读不出「一条蛇」，只会是一段乱扭的绳子。\n" +
                 "开启后：运行时逐节把脊骨拉直成一条直线（+Z），再把结果重新记成基准姿态。\n" +
                 "算法已由 dg_straight 验证：拉直后逐节弯角 0.00°、折线总长 = 首尾直线 = 8.266 m")]
        public bool straightenSpine = true;

        // ★★ 包络的**方向**曾经是反的。实测两条独立证据确认：
        //    ① 尾尖 drgon_0226 的祖先链挂在 `_spine[0]` 下（dg_anc）⇒ `_spine[0]` 是根/尾侧；
        //    ② 原代码 DoDiveTell / DoStrikeBite 把"低头"施加在数组**末尾**，命名为 biteHeadLinks
        //       ⇒ `_spine[Count-1]` 是头侧。
        //    也就是说：i 小 = 尾/根侧，i 大 = 头侧。
        //    旧代码把 0.25 给了 i=0、1.35 给了 i=Count-1 ⇒ **把头放大 1.35 倍、把尾端压成 0.25**，
        //    正是"摆动很奇怪、头乱甩"的直接来源。
        //    真实蛇/鳗的游动是**头稳尾摆**（头要保持稳定以瞄准）。
        // ★ 字段换名是有意的：旧名在 prefab 里有序列化值，改 C# 默认值不生效（本项目硬规矩 #6）。
        //   换成新名字，prefab 里没有对应键 ⇒ C# 默认值直接生效。
        [Tooltip("链首（_spine[0] = 根/尾侧）的摆幅增益")]
        public float waveAmpRootGain = 1.30f;
        [Tooltip("链尾（_spine[Count-1] = 头侧）的摆幅增益。头要稳，所以小")]
        public float waveAmpHeadGain = 0.30f;

        [Header("攻击选择门槛")]
        [Tooltip("超过这个距离才有机会选龙息（远程）")]
        public float breathMinRange = 5.5f;
        [Tooltip("低于这个距离才有机会选撕咬")]
        public float biteMaxRange = 7.0f;
        [Tooltip("盘旋多久后俯冲回来（秒）")]
        public float hoverDuration = 4.5f;

        // ================= ★ 架构级新增：Circling / Diving =================
        // 策划案 §2 的核心：龙**默认在天上盘旋**，攻击是"从天上下来一趟再回去"。
        // 旧实现把四招平权塞进地面 Attack 状态 ⇒ 龙读起来就是个地面近战怪。
        [Header("★ 空中循环（策划案 §2）")]
        [Tooltip("不开 = 退回旧的『地面近战怪』行为。保留开关是为了能做 A/B 对照实验")]
        public bool aerialLoop = true;
        [Tooltip("盘旋→选招 的冷却下限/上限（秒）。区间随机，避免机械感")]
        public float circleCooldownMin = 1.2f;
        public float circleCooldownMax = 2.0f;
        [Tooltip("盘旋轨道中心跟随玩家的响应速度（0 = 不跟随，绕固定点）")]
        public float circleCenterFollow = 1.8f;
        [Tooltip("下潜预兆时长（秒）—— I5：玩家要看得懂才能反应")]
        public float diveTellDuration = 0.35f;
        [Tooltip("俯冲段时长（秒）")]
        public float diveDuration = 0.45f;
        [Tooltip("俯冲落点预判玩家的时间（秒）—— I3/T6：逼玩家变向，而不是追当前位")]
        public float divePredictSeconds = 0.35f;
        [Tooltip("撕咬落地后的滞留（秒）—— 0 = 咬完立刻拉起")]
        public float biteGroundHold = 0.12f;
        [Tooltip("落地扫尾在地面的时长（秒）")]
        public float sweepGroundDuration = 0.60f;
        [Tooltip("拉起回位时长（秒）")]
        public float recoverDuration = 0.58f;
        [Tooltip("盘旋期水平移动速度（m/s）")]
        public float circleMoveSpeed = 6.0f;

        // ================= ★ 巡游：波浪形前进 =================
        // 用户要求"波浪形的，左右蜿蜒前进"。旧实现是绕圆轨道（原地绕），没有"前进"。
        // 现在：龙沿自身前方**持续推进**，靠转向维持绕玩家的大圈。
        [Tooltip("巡游前进速度（m/s）—— 龙真正向前游的速度")]
        public float swimSpeed = 8.0f;
        [Tooltip("转向速率。越大转得越急；小 = 大弧线悠然巡游")]
        public float swimTurnRate = 1.1f;
        [Tooltip("半径偏差 → 向内/向外转向的增益（把龙拉回 hoverOrbitRadius）")]
        public float swimRadiusGain = 0.12f;
        [Tooltip("轨道角速度（°/s）。线速度 ≈ hoverOrbitRadius × 此值 × π/180")]
        public float orbitAngularSpeedDeg = 40f;
        [Tooltip("★ 轨迹的左右蛇行振幅（m）—— 这才是「蜿蜒前进」的可见量")]
        public float orbitLateralAmp = 2.6f;
        [Tooltip("★ 绕一圈里的蛇行波数。越大越碎；3 ≈ 明显的左右蜿蜒")]
        public float orbitLateralWaves = 3f;
        private float _orbitPhase;
        private Vector3 _swimDir = Vector3.forward;
        [Tooltip("俯冲期水平移动速度倍率（× circleMoveSpeed）")]
        public float diveSpeedMul = 2.6f;
        [Tooltip("I4：两次俯冲之间的最小间隔（秒）。低于 1.2 s 玩家没有喘息窗口")]
        public float diveMinInterval = 1.2f;

        [Header("★ 三阶段（策划案 §4）")]
        [Tooltip("P2 / P3 的血量阈值")]
        public float phase2AtRatio = 0.65f;
        public float phase3AtRatio = 0.30f;
        [Tooltip("各阶段的盘旋高度（m）")]
        public float hoverHeightP1 = 3.6f;
        public float hoverHeightP2 = 4.4f;
        public float hoverHeightP3 = 5.2f;
        [Tooltip("各阶段的俯冲冷却（秒）")]
        public float diveCooldownP1 = 2.4f;
        public float diveCooldownP2 = 1.8f;
        public float diveCooldownP3 = 1.2f;

        /// <summary>俯冲循环的子相位（只在 Attack 状态内推进）。</summary>
        public enum DivePhase { None, Tell, Dive, Strike, Recover }

        [Header("诊断（只读）")]
        [SerializeField] private string _currentAttackName = "-";
        [SerializeField] private int _tailSweepCount;
        [SerializeField] private int _biteCount;
        [SerializeField] private int _breathCount;
        [SerializeField] private int _hoverCount;
        [SerializeField] private int _spineLinksFound;
        [SerializeField] private bool _airborne;
        [SerializeField] private string _divePhaseName = "-";
        [SerializeField] private float _airborneRatio;      // 本场至今的空中帧占比（验收 A1）
        [SerializeField] private int _phase = 1;
        [SerializeField] private float _lastDiveEndTime = -99f;

        public string CurrentAttackName => _currentAttackName;
        public int TailSweepCount => _tailSweepCount;
        public int BiteCount => _biteCount;
        public int BreathCount => _breathCount;
        public int HoverCount => _hoverCount;
        public int SpineLinksFound => _spineLinksFound;
        public bool Airborne => _airborne;
        /// <summary>俯冲子相位名（验收用）。</summary>
        public string DivePhaseName => _divePhaseName;
        /// <summary>本场至今「龙身在地面以上 1.5 m」的帧占比（验收 A1，判据 ≥0.60）。</summary>
        public float AirborneRatio => _airborneRatio;
        /// <summary>当前阶段 1/2/3（验收 T7）。</summary>
        public int Phase => _phase;
        /// <summary>上一次俯冲**开始**的时刻（验收 A4）。</summary>
        public float LastDiveEndTime => _lastDiveEndTime;

        /// <summary>
        /// 验收用：当前帧脊骨链里**偏离出生姿态最大**的那一节的夹角（度）。
        ///
        /// 为什么需要它：光看 `CurrentAttackName` 只能证明"状态机说它在甩尾"，
        /// 证不了"骨头真的动了"。曾经出现过「招式名在报、模型纹丝不动」的假通过
        /// （`_modelRoot` 取错节点那次）。用角度读数才能证明动作真的落在了骨骼上。
        /// 只在验收探针里按需调用（每帧调是 O(24) 的 Quaternion.Angle，够便宜）。
        /// </summary>
        public float MaxSpineOffsetDeg
        {
            get
            {
                float mx = 0f;
                int n = Mathf.Min(_spine.Count, _baseRot.Count);
                for (int i = 0; i < n; i++)
                {
                    if (_spine[i] == null) continue;
                    mx = Mathf.Max(mx, Quaternion.Angle(_spine[i].localRotation, _baseRot[i]));
                }
                return mx;
            }
        }

        // ---- 运行时状态 ----
        private readonly List<Transform> _spine = new List<Transform>();
        private readonly List<Quaternion> _baseRot = new List<Quaternion>();
        private DragonAttack _attack;
        private float _orbitAngle;
        private Transform _modelRoot;
        private Vector3 _modelRootBaseLocalPos;
        private float _groundY;
        private bool _groundCaptured;

        // ★ 空中循环的运行时状态
        private DivePhase _dive = DivePhase.None;
        private Vector3 _diveTarget;            // 俯冲的预判落点（水平）
        private Vector3 _diveStart;             // 俯冲起点（水平）
        private float _airFrames, _totalFrames; // A1 统计
        private bool _circleHoldPose;           // 盘旋期是否已经摆好姿态
        private float _circleCd;                // 盘旋→选招的剩余冷却

        protected override void Awake()
        {
            base.Awake();
            ResolveSpine();
        }

        /// <summary>
        /// 定位脊骨单链。
        ///
        /// 为什么是"单链"而不是"所有子节点"：龙的骨架是**一条蛇形脊柱**（drgon_03→…→drgon_026），
        /// 每节通常只有一个子节点继续往下。若按广度优先收集会混进鳍/爪的分支，
        /// 那些骨头的旋转轴与脊柱不同，用同一条正弦驱动会让鳍乱翻。
        /// 所以这里只沿"第一个子节点"这条主链往下走。
        ///
        /// ★ 同时把每节的**出生姿态**记下来（`_baseRot`），因为 glTF 模型的骨骼往往自带
        ///   90° 的朝向修正（Z-up → Y-up）。若直接写 `localRotation = 偏移` 会把这个修正抹掉，
        ///   表现是"整条龙瞬间躺平/翻转" —— 而且不会有任何报错。所有驱动都必须是**叠加**。
        /// </summary>
        private void ResolveSpine()
        {
            _spine.Clear();
            _baseRot.Clear();

            Transform start = spineRoot;
            if (start == null)
            {
                // 自动找：从所有 SkinnedMeshRenderer 的根骨里挑深度最浅的那个
                var smrs = GetComponentsInChildren<SkinnedMeshRenderer>(true);
                foreach (var smr in smrs)
                {
                    if (smr.rootBone != null) { start = smr.rootBone; break; }
                }
                // 兜底：找 _rootJoint
                if (start == null)
                {
                    foreach (var t in GetComponentsInChildren<Transform>(true))
                        if (t.name == "_rootJoint") { start = t; break; }
                }
            }
            if (start == null) { _spineLinksFound = 0; return; }

            // 如果 start 是 _rootJoint（一个空容器），往下走一层再开始
            Transform cur = start;
            if (cur.childCount == 1 && cur.GetComponent<SkinnedMeshRenderer>() == null)
            {
                int guard = 0;
                while (cur.childCount == 1 && guard++ < 3)
                {
                    var only = cur.GetChild(0);
                    if (only.GetComponent<SkinnedMeshRenderer>() != null) break;
                    cur = only;
                }
            }

            int want = spineLinkCount > 0 ? spineLinkCount : 64;
            while (cur != null && _spine.Count < want)
            {
                bool isMesh = cur.GetComponent<SkinnedMeshRenderer>() != null;
                if (isMesh) break;
                _spine.Add(cur);
                _baseRot.Add(cur.localRotation);
                Transform next = null;
                for (int i = 0; i < cur.childCount; i++)
                {
                    var c = cur.GetChild(i);
                    if (c.GetComponent<SkinnedMeshRenderer>() != null) continue;
                    next = c; break;
                }
                cur = next;
            }

            _spineLinksFound = _spine.Count;

            // 找模型容器。
            //
            // ★ 这里曾经写的是 `_modelRoot = _spine[0].parent`，那是**错的**：
            //   `_spine[0]` 是 `drgon_03`（ResolveSpine 会跳过 _rootJoint 这层空容器），
            //   所以它的 parent 是 `_rootJoint` —— 一个夹在骨架里的空节点，
            //   改它的 localPosition 会被后面的脊骨驱动覆盖掉，表现是"升空完全没效果"。
            //
            // 正确做法：**从 SkinnedMeshRenderer 往上找**，第一个"非骨架容器"
            //   就是我们要抬的那个（典型层级 Visual/Model/Object_6/Object_281）。
            //   判据：它的名字不以 `drgon_` 开头，且它是整个渲染器的祖先。
            if (_spine.Count > 0)
            {
                var firstSmr = GetComponentInChildren<SkinnedMeshRenderer>(true);
                if (firstSmr != null)
                {
                    // 从渲染器往上爬，找"名字不是骨架节点"的最靠上的那个容器
                    Transform best = null;
                    var walker = firstSmr.transform.parent;
                    int guard = 0;
                    while (walker != null && walker != transform && guard++ < 20)
                    {
                        if (!walker.name.StartsWith("drgon_") && walker.name != "_rootJoint")
                            best = walker;
                        walker = walker.parent;
                    }
                    // 兜底：用 spine[0].parent
                    _modelRoot = best != null ? best : _spine[0].parent;
                    if (_modelRoot != null) _modelRootBaseLocalPos = _modelRoot.localPosition;
                }
                else _modelRoot = _spine[0].parent;
            }

            if (straightenSpine) StraightenSpineBase();
        }

        /// <summary>
        /// 把脊骨**拉直成一条直线**，并把结果重新记成基准姿态（`_baseRot`）。
        ///
        /// ★ 为什么必须做（实测 `dg_chain`）：龙的骨架原生姿态是一条大幅 C/S 曲线 ——
        ///   24 节折线总长 8.266 m，首尾直线只有 4.179 m，逐节弯角 7.9°~65.6°。
        ///   把游动波叠在这条弯基准上，身体永远读不出「一条蛇」，只会是一段乱扭的绳子。
        ///
        /// ★ 算法（已由 `dg_straight` 验证：拉直后逐节弯角 0.00°、折线总长 = 首尾直线 = 8.266 m）：
        ///   从链首到链尾**一次正序扫描**，每步把「本节点 → 下一节点」的段方向对齐到目标朝向。
        ///   之所以一遍就够：节点 i 的旋转只影响 i 之后的段，**不影响 i-1→i 这一段**
        ///   （那一段的端点位置由 i-1 决定），所以已对齐的段不会被后续步骤破坏。
        ///
        /// ★ 目标朝向取**模型容器的 +Z**（模型前方，朝向来自 `drgon_00`）。
        ///   拉直后 `_spine[0]`（尾/根侧）留在原处、`_spine[Count-1]`（头）伸向 +Z，
        ///   于是 `transform.LookRotation(前进方向)` 就是**头朝前**，不需要额外修正。
        ///
        /// ★ 不能靠"把 localRotation 清零"来实现 —— 骨骼自带 glTF 的 Z-up→Y-up 朝向修正，
        ///   清零会让整条龙躺平翻转（见 `ResolveSpine` 的注释）。必须做**方向对齐**。
        /// </summary>
        private void StraightenSpineBase()
        {
            if (_spine.Count < 2) return;

            Vector3 fwd = _modelRoot != null ? _modelRoot.forward : Vector3.forward;
            if (fwd.sqrMagnitude < 1e-8f) fwd = Vector3.forward;
            fwd.Normalize();

            for (int i = 0; i + 1 < _spine.Count; i++)
            {
                Vector3 d = _spine[i + 1].position - _spine[i].position;
                if (d.sqrMagnitude < 1e-10f) continue;      // 重合节点跳过（否则 FromToRotation 无定义）
                _spine[i].rotation = Quaternion.FromToRotation(d.normalized, fwd) * _spine[i].rotation;
            }

            // 拉直后的姿态成为新基准：之后所有偏移都叠在"直线"上
            for (int i = 0; i < _spine.Count; i++) _baseRot[i] = _spine[i].localRotation;
        }

        protected override void OnEnterState(EnemyState s)
        {
            base.OnEnterState(s);

            if (s == EnemyState.Attack)
            {
                // 攻击开始那一帧把链复位，避免上一次动作的残留姿态串味
                ApplySpineOffsetsRaw(0f, 0f, 0, 0f);
                _dive = DivePhase.None;
                _divePhaseName = "-";
                // I1/I2：攻击是"从天上下来一趟" —— 空中发起，确保在飞行态
                if (aerialLoop && !_airborne) BeginHover();
                _currentLift = PhaseHoverHeight;
            }
            else if (s == EnemyState.Idle || s == EnemyState.Chase || s == EnemyState.HitStun)
            {
                // 新架构下 Chase = 空中盘旋，**不许落地**（I1）；
                // 只有旧行为（aerialLoop=false）才回落地面
                if (!aerialLoop && _airborne) EndHover();
                if (s == EnemyState.Chase && aerialLoop) _circleCd = UnityEngine.Random.Range(circleCooldownMin, circleCooldownMax);
            }
        }

        /// <summary>
        /// ★ I1 的配套：**龙不需要"看见"玩家就该起飞**。
        ///
        /// 为什么必须覆写 `TickIdle`：基类的 `TickIdle` 靠 `CanSeePlayer()` 触发追击，
        /// 而龙是 Generic 骨架、朝向来自 `drgon_00`（模型在 +Z），
        /// `sightAngleDeg` 200° 的锥体只有 ±100°。**只要玩家站到它背后就不会被发现**，
        /// 于是它一直停在 `Idle`、`TickChase`（盘旋逻辑）永远轮不到 ——
        /// 表现是"龙站着不动、一次都不飞"，而且控制台干干净净。
        ///
        /// Boss 的正确心智是**"它在天上盯着战场"**，而不是"一只蹲在原地等猎物走进视野的狼"。
        /// 所以这里只做一件事：只要玩家在场上、且在感知距离内，直接进入盘旋。
        /// </summary>
        protected override void TickIdle()
        {
            if (!aerialLoop) { base.TickIdle(); return; }

            // ★ 再入距离同样不能用地面怪的 `loseSightRange`：龙一旦因为任何原因落到 Idle，
            //   若玩家在 20 m 外、而 loseSightRange 只有十几米，它就**再也起不来** ——
            //   实测踩到：龙停在 20 m 处、骨链复位成拉直基准（一根直杆）、5 张连拍 25 秒零位移。
            if (PlayerRef.Exists && _distanceToPlayer <= Mathf.Max(loseSightRange, hoverOrbitRadius * 2.5f))
                Enter(EnemyState.Chase);
        }

        // ---------------- 选招 ----------------
        /// <summary>
        /// 龙的出手门槛 = 撕咬的最大距离（7 m），**不是**基类默认的 `attackRange`(2 m)。
        ///
        /// 不覆写的话，实战表现是「BOSS 站在 6 m 外一动不动、什么招都不出」，
        /// 而它自己的四招全都按 2.5~7 m 分档设计 —— 整套招式设计被基类那 2 m 卡死。
        /// </summary>
        protected override float EffectiveAttackRange => Mathf.Max(attackRange, biteMaxRange);

        /// <summary>它想保持的距离：停在"刚进撕咬范围"处，别再贴上来了。</summary>
        protected override float PreferredRange => biteMaxRange * 0.85f;

        /// <summary>当前阶段的盘旋高度。</summary>
        private float PhaseHoverHeight
        {
            get
            {
                if (_phase >= 3) return hoverHeightP3;
                if (_phase == 2) return hoverHeightP2;
                return hoverHeightP1;
            }
        }

        private float PhaseDiveCooldown
        {
            get
            {
                if (_phase >= 3) return diveCooldownP3;
                if (_phase == 2) return diveCooldownP2;
                return diveCooldownP1;
            }
        }

        /// <summary>
        /// ★ 架构级覆写（策划案 §2 / I1）：**盘旋态取代地面追击**。
        ///
        /// 旧的 `TickChase` 让龙像近战怪一样贴地走向玩家，于是"平时不飞天"。
        /// 现在龙在 `Chase` 状态里做的事是：**保持在空中沿轨道盘旋**，到达选招条件时
        /// 进入 `Attack`（俯冲循环）。地面根本不是它的常态。
        /// </summary>
        protected override void TickChase()
        {
            if (!aerialLoop) { base.TickChase(); return; }

            if (!PlayerRef.Exists) { if (_airborne) EndHover(); return; }

            // 空中常驻：第一次进 Chase 就起飞
            if (!_airborne) BeginHover();

            float dist = _distanceToPlayer;
            // ★ 跟丢距离不能直接沿用地面怪的 `loseSightRange`：龙的**正常巡游半径**就有
            //   `hoverOrbitRadius`（11 m），用地面怪的距离阈值会把"正常绕圈"误判成"跟丢了" ⇒
            //   `EndHover()` 落地回 Idle，而 Idle 的再入条件是"玩家在感知距离内"——
            //   于是龙**永久卡在 Idle**：骨链复位成拉直基准（一根直杆）、不飞不动、控制台干净。
            //   实测踩到：龙停在离玩家 20 m 处、身体笔直、完全不动。
            if (dist > Mathf.Max(loseSightRange, hoverOrbitRadius * 2.5f)) { EndHover(); return; }

            TickCircling();

            // 选招条件：进入攻击距离 + 俯冲冷却好了（I4 由 diveMinInterval 兜底）
            // ★ 不判 CanSeePlayer：它在天上，视线锥是给地面怪的。够近就俯冲。
            _circleCd -= Time.deltaTime;
            bool cooldownOk = _cooldownTimer <= 0f && _circleCd <= 0f;
            // I4：距上次**俯冲**至少 diveMinInterval。注意第一次俯冲不受限（_lastDiveEndTime 初值 -99）
            bool intervalOk = Time.time - _lastDiveEndTime >= diveMinInterval;
            if (dist <= EffectiveAttackRange && cooldownOk && intervalOk)
            {
                Enter(EnemyState.Attack);
            }
        }

        /// <summary>盘旋：绕玩家转 + 跟随玩家 + 长链游动。这是龙的**常态**。</summary>
        private void TickCircling()
        {
            _currentLift = PhaseHoverHeight;

            if (PlayerRef.Exists)
            {
                // ── 巡游：**前进**，不是绕轨道 ──
                //
                // ★ 旧实现是"追一个圆轨道上的点"：龙被拉回固定的圆，头尾都贴着同一段圆弧，
                //   读起来是**原地绕**，压根没有"往前游"。用户的原话："这个移动没有做，
                //   要成波浪形的，左右蜿蜒前进"。
                //
                //   现在改成**把式（steering）**：龙始终沿自身前方前进，
                //   靠"转向"去维持一个绕玩家的大圈 —— 转向慢、半径大，
                //   所以观感是"贴着大弧线蜿蜒游过去"，而不是"被吸在圆上"。
                Vector3 center = PlayerRef.Position;
                // 蛇行相位：取与实际绕行角速度匹配的值（线速度/半径 ≈ 8/11 ≈ 41°/s）
                _orbitPhase += orbitAngularSpeedDeg * Mathf.Deg2Rad * Time.deltaTime;

                Vector3 radial = Flat(transform.position - center);
                if (radial.sqrMagnitude < 0.04f) radial = Flat(transform.forward);
                // ★★ 必须在归一化**之前**取真实距离！
                //   `radial.Normalize()` 之后 `radial.magnitude` **恒等于 1**，
                //   于是 `err = Mathf.Clamp(hoverOrbitRadius - 1, -6, 6)` 是个**常数 6**，
                //   方向修正 `radial * (6 * 0.12)` 退化成"恒定向外推、且不随距离衰减"
                //   ⇒ 正反馈发散。实测（dg_move）：龙以 8 m/s **直线**飞出，
                //   距玩家 28 → 40 m，`posDelta` 方向恒定 (-0.7, 0, -0.7)，
                //   随后超出感知距离 ⇒ EndHover() 落地 ⇒ 永久冻死（state 仍是 Chase）。
                float dReal = radial.magnitude;
                radial.Normalize();

                Vector3 tangent = Vector3.Cross(Vector3.up, radial).normalized;   // 轨道切向
                // ★ 半径偏差必须**限幅**：不限幅时"离得远 ⇒ 修正分量 ∝ 距离"会正反馈，
                //   龙被猛推向玩家、冲过去又甩出更远，最后飞出 loseSightRange ⇒
                //   EndHover() 落地回 Idle（骨链复位成拉直基准 = 一根直杆，实测踩到）。
                //   限幅后修正永远只是切向的一个小偏置，龙平滑地绕圈。
                float d = dReal;
                float err = Mathf.Clamp(hoverOrbitRadius - d, -6f, 6f);
                // 软绳：跑得太远时加强回收，避免"慢慢悠悠绕出去"
                float pull = d > hoverOrbitRadius * 1.5f ? 2.5f : 1f;
                // ★ 蛇行：把「目标方向」本身左右摆（沿 radial = 垂直于前进方向），
                //   这样走出来的**轨迹**才是波浪。只让身体摆而路径是直线，
                //   观感仍然是"直着飘" —— 用户要的是"左右蜿蜒**前进**"，两者都要。
                float lateral = orbitLateralAmp * Mathf.Sin(_orbitPhase * orbitLateralWaves) * 0.12f;
                Vector3 wantDir = (tangent + radial * (lateral + err * swimRadiusGain * pull)).normalized;

                // ★ 帧率无关转向。旧写法 `Mathf.Clamp01(turnRate * dt)` 在 dt=0.005 s（200 fps）时
                //   每帧只转 0.55%，转 90° 要 2 秒、期间飞出 16 m
                //   ⇒ 永远追不上持续变化的目标方向，只能直线飘。
                //   `1 - exp(-k·dt)` 才是帧率无关的同一时间常数。
                float turn = 1f - Mathf.Exp(-swimTurnRate * Time.deltaTime);
                _swimDir = Vector3.Slerp(_swimDir, wantDir, turn).normalized;
                if (_swimDir.sqrMagnitude > 1e-6f)
                {
                    // ★ 只写一次 rotation。旧代码连写两遍（先切线、再"看玩家"），
                    //   两个 Slerp 抢同一个 transform.rotation ⇒ 身体朝向和实际前进方向不一致，
                    //   看着像"斜着飘"。
                    transform.rotation = Quaternion.LookRotation(_swimDir, Vector3.up);
                }
                MoveHorizontal(_swimDir * swimSpeed * Time.deltaTime);
            }

            // 长链游动。
            //
            // ★ 这里以前写的是 `ApplySpineOffsetsRaw(yaw, 0f, _spine.Count, 40f)`，三个问题：
            //   ① 振幅只有 11° ⇒ 实测每节只弯 0.2~9.7°，整条龙读起来是**僵的**；
            //   ② 相位步进 40° ⇒ 每 9 节一个完整波，8.27 m 的身子里塞进 2.7 个波，
            //      看起来像"搓衣板"而不是"蛇在游"（真游动的波长 ≈ 一个身长）；
            //   ③ pitch 传 0 ⇒ 纯水平摆动，是"扁片在转"，没有立体感。
            //   现在改成：相位步进 15°（≈ 一个身长一个波）+ 垂直面小幅起伏 + 头稳尾摆包络。
            //   ★ 传的是**相位**（不是 sin 之后的值）—— 这里曾经把 `sin(2πft)` 的结果当振幅传进去，
            //     而 ApplySpineOffsetsRaw 内部又乘一次 sin，两个正弦相乘 ⇒ 振幅被压扁。
            float phase = 2f * Mathf.PI * hoverFrequency * Time.time;
            ApplySpineOffsetsRaw(hoverAmplitudeDeg, hoverPitchAmplitudeDeg, _spine.Count,
                                 hoverPhaseStepDeg, waveAmpRootGain, waveAmpHeadGain, phase);
        }

        /// <summary>盘旋高度由 Update 里的 MoveTowards 负责；这里只保证目标高度正确。</summary>
        private float DesiredHoverY => PhaseHoverHeight;

        protected override void ChooseAttack()
        {
            // ★ 验收专用：强制下一招（见 ForceNextAttackForTest）
            if (_forcedNextAttack.HasValue)
            {
                _attack = _forcedNextAttack.Value;
                _forcedNextAttack = null;
                ApplyAttackTiming();
                return;
            }

            // ★ 新架构（策划案 §3）：从**空中**发起的招为主，扫尾是唯一的"落地招"。
            //   权重按阶段变化（§4 表格）。
            float d = _distanceToPlayer;
            bool canBite = d <= biteMaxRange && d >= 2.5f;
            bool canBreath = d >= breathMinRange;

            float wBite = 1.2f, wTail = 1.0f, wBreath = 1.0f;
            if (_phase == 2) { wBite = 1.5f; wTail = 1.0f; wBreath = 1.2f; }
            else if (_phase >= 3) { wBite = 1.8f; wTail = 0.8f; wBreath = 1.4f; }

            if (!canBite) wBite = 0f;
            if (!canBreath) wBreath = 0f;

            float sum = wBite + wTail + wBreath;
            if (sum <= 0f) _attack = DragonAttack.Bite;
            else
            {
                float r = UnityEngine.Random.value * sum;
                if (r < wBite) _attack = DragonAttack.Bite;
                else if (r < wBite + wTail) _attack = DragonAttack.TailSweep;
                else _attack = DragonAttack.Breath;
            }

            ApplyAttackTiming();
        }

        /// <summary>
        /// 时间参数按招重算。**必须重算**：基类的冷却=windup+active+recover+cd，
        /// 而四招的长度差很多，沿用一套会立刻串味。
        ///
        /// ★ 新架构下时间轴被显式拆成 **预兆 → 俯冲 → 打击 → 拉起**（策划案 §3.2），
        ///   所以 windup/active/recover 不再是"随手拍的"，而是这四个相位之和。
        /// </summary>
        private void ApplyAttackTiming()
        {
            switch (_attack)
            {
                case DragonAttack.Bite:
                    // 预兆 0.35 + 俯冲 0.45 + 咬 0.22 + 拉起 0.58 ≈ 1.60 s
                    attackWindup = diveTellDuration + diveDuration;
                    attackActive = 0.22f;
                    attackRecover = biteGroundHold + recoverDuration;
                    _currentAttackName = "俯冲撕咬"; _biteCount++;
                    break;

                case DragonAttack.TailSweep:
                    // 俯冲段相同，落地后多做一次横扫
                    attackWindup = diveTellDuration + diveDuration;
                    attackActive = 0.30f;
                    attackRecover = sweepGroundDuration + recoverDuration;
                    _currentAttackName = "落地扫尾"; _tailSweepCount++;
                    break;

                case DragonAttack.Breath:
                    // 悬停吐息：**不下降高度**
                    attackWindup = 0.62f; attackActive = 0.34f; attackRecover = 0.70f;
                    _currentAttackName = "悬停吐息"; _breathCount++;
                    break;

                case DragonAttack.HoverOrbit:
                    attackWindup = hoverDuration * 0.45f; attackActive = hoverDuration * 0.30f;
                    attackRecover = hoverDuration * 0.25f;
                    _currentAttackName = "盘旋"; _hoverCount++;
                    break;
            }
            _cooldownTimer = attackWindup + attackActive + attackRecover + PhaseDiveCooldown;
        }

        private DragonAttack? _forcedNextAttack;

        /// <summary>
        /// 验收专用：强制下一次攻击使用指定招式（<paramref name="name"/> 取
        /// "TailSweep" / "Bite" / "Breath" / "HoverOrbit"）。
        /// 只在**下一次**选招时生效一次，之后自动恢复加权随机。
        /// </summary>
        public void ForceNextAttackForTest(string name)
        {
            switch (name)
            {
                case "TailSweep": _forcedNextAttack = DragonAttack.TailSweep; break;
                case "Bite": _forcedNextAttack = DragonAttack.Bite; break;
                case "Breath": _forcedNextAttack = DragonAttack.Breath; break;
                case "HoverOrbit": _forcedNextAttack = DragonAttack.HoverOrbit; break;
                default: _forcedNextAttack = null; break;
            }
        }

        /// <summary>验收专用：把状态机复位回追击（清招式、退盘旋、复位脊骨）。</summary>
        public void EndAttackForTest()
        {
            if (_airborne) EndHover();
            for (int i = 0; i < _spine.Count && i < _baseRot.Count; i++)
                if (_spine[i] != null) _spine[i].localRotation = _baseRot[i];
            _currentAttackName = "-";
            _cooldownTimer = 0f;
        }

        // ---------------- 每帧动作 ----------------
        protected override void AttackMovement(float t, float hitAt)
        {
            switch (_attack)
            {
                case DragonAttack.Bite:
                    DoDiveBite(t);
                    break;
                case DragonAttack.TailSweep:
                    DoDiveSweep(t);
                    break;
                case DragonAttack.Breath:
                    DoBreathHover(t);
                    break;
                case DragonAttack.HoverOrbit:
                    DoHoverOrbit(t);
                    break;
            }

            // 身体高度由 MoveLiftTo 统一管（它把"想飞多高"翻译成容器 localPosition.y）
            UpdateBodyLift();
        }

        /// <summary>
        /// 把"想飞多高"写进模型容器的 `localPosition.y`。
        ///
        /// ★ 为什么只走容器 `localPosition.y`：`anim.bodyPosition` 会被 `localScale²` 缩放
        ///   （本项目硬规矩 #7 的同类坑），而容器 localPosition 是干净的。
        /// </summary>
        private void UpdateBodyLift()
        {
            if (_modelRoot == null) return;
            float want = _modelRootBaseLocalPos.y + bodyLift + (_airborne ? _currentLift : 0f);

            // ★ 上升与下降要用**不同**的速率：
            //   - 下降（俯冲/落地）必须快，慢了就读不出"扑下来咬一下"的冲击感；
            //   - 上升（拉起/起飞）用稍慢的固定速度，才有"重新飞回去"的从容。
            //   ★ 关键修复：起飞瞬间 `_currentLift` 一步跳到 hoverHeight，而这里若用
            //     `|差值| × 12 × dt` 的指数逼近，从 0 → 3.6 m 要 **1.5 s** 才到位
            //     （实测 rootY 在 3.36 s 起标 air=True，却到 4.9 s 才升到 0.35+3.4），
            //     期间龙**贴在地面**却被算作"已经在天上" —— 表现就是"它没飞起来"。
            //   改成"指数逼近 + 恒定最低速度"取较大者，起飞 0.35 s 内到位。
            float cur = _modelRoot.localPosition.y;
            float diff = want - cur;
            float step = Mathf.Max(
                Mathf.Abs(diff) * 12f * Time.deltaTime,          // 指数逼近（末段收敛平滑）
                liftSpeed * Time.deltaTime);                     // 恒定速度下限（保证起飞不拖）
            _modelRoot.localPosition = new Vector3(
                _modelRoot.localPosition.x,
                Mathf.MoveTowards(cur, want, step),
                _modelRoot.localPosition.z);
        }

        private float _currentLift;

        /// <summary>高度变化的恒定速度下限（m/s）——保证起飞/拉起不会"贴地爬"。</summary>
        public float liftSpeed = 14f;

        /// <summary>水平位移（优先走 agent，agent 不可用就写 transform）。</summary>
        private void MoveHorizontal(Vector3 delta)
        {
            delta.y = 0f;
            if (_agent != null && _agent.enabled && _agent.isOnNavMesh) _agent.Move(delta);
            else transform.position += delta;
        }

        private static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

        /// <summary>
        /// 俯冲循环（策划案 §3.2）—— **这就是用户点名的"冲下来撕咬一下再飞回去"**。
        ///
        /// 相位：Tell(0.35 预兆) → Dive(0.45 俯冲) → Strike(0.22 咬/扫) → Recover(0.58 拉起)
        /// I3：俯冲是**位移**——水平真的朝预判落点走，高度用 ease-in 落下来。
        /// I5：Tell 段有可见的收拢预兆（俯角 + 身体收紧），玩家看得懂才有得躲。
        /// I2：无论哪一招，末尾都必须回到 hoverHeight（由 _airborne + _currentLift 保证）。
        /// </summary>
        private void DoDiveBite(float t)
        {
            float tellEnd = diveTellDuration;
            float diveEnd = tellEnd + diveDuration;
            float strikeEnd = diveEnd + attackActive;
            float total = Mathf.Max(0.01f, attackWindup + attackActive + attackRecover);

            // ---- 相位推进 ----
            if (t < tellEnd) SetPhase(DivePhase.Tell);
            else if (t < diveEnd)
            {
                if (_dive != DivePhase.Dive) BeginDive();
                SetPhase(DivePhase.Dive);
            }
            else if (t < strikeEnd) SetPhase(DivePhase.Strike);
            else SetPhase(DivePhase.Recover);

            switch (_dive)
            {
                case DivePhase.Tell:
                    DoDiveTell(t / Mathf.Max(0.01f, tellEnd));
                    break;
                case DivePhase.Dive:
                    DoDiveTravel(Mathf.InverseLerp(tellEnd, diveEnd, t));
                    break;
                case DivePhase.Strike:
                    DoStrikeBite(Mathf.InverseLerp(diveEnd, strikeEnd, t));
                    break;
                case DivePhase.Recover:
                    DoRecover(Mathf.InverseLerp(strikeEnd, total, t));
                    break;
            }
        }

        /// <summary>落地扫尾：与撕咬共用俯冲段，区别在"落地后做一次横扫"。</summary>
        private void DoDiveSweep(float t)
        {
            float tellEnd = diveTellDuration;
            float diveEnd = tellEnd + diveDuration;
            float strikeEnd = diveEnd + attackActive;
            float groundEnd = strikeEnd + sweepGroundDuration;
            float total = Mathf.Max(0.01f, attackWindup + attackActive + attackRecover);

            if (t < tellEnd) SetPhase(DivePhase.Tell);
            else if (t < diveEnd) { if (_dive != DivePhase.Dive) BeginDive(); SetPhase(DivePhase.Dive); }
            else if (t < groundEnd) SetPhase(DivePhase.Strike);
            else SetPhase(DivePhase.Recover);

            switch (_dive)
            {
                case DivePhase.Tell: DoDiveTell(t / Mathf.Max(0.01f, tellEnd)); break;
                case DivePhase.Dive: DoDiveTravel(Mathf.InverseLerp(tellEnd, diveEnd, t)); break;
                case DivePhase.Strike: DoStrikeSweep(Mathf.InverseLerp(diveEnd, groundEnd, t)); break;
                case DivePhase.Recover: DoRecover(Mathf.InverseLerp(groundEnd, total, t)); break;
            }
        }

        private void SetPhase(DivePhase p)
        {
            if (_dive == p) return;
            _dive = p;
            _divePhaseName = p.ToString();
        }

        /// <summary>预兆（I5）：身体收紧 + 俯角出现，**可读**地告诉玩家"要来了"。</summary>
        private void DoDiveTell(float u)
        {
            _currentLift = Mathf.Lerp(DesiredHoverY, DesiredHoverY + 0.35f, u);   // 微微上抬蓄力
            // 头颈下压俯角（越靠末端越明显）
            float pitch = 26f * u;
            int start = Mathf.Max(0, _spine.Count - biteHeadLinks);
            for (int i = 0; i < _spine.Count; i++)
            {
                if (i < start) { _spine[i].localRotation = _baseRot[i]; continue; }
                int k = i - start;
                _spine[i].localRotation = _baseRot[i] * Quaternion.Euler(pitch * (1f + 0.12f * k), 0f, 0f);
            }
            FacePlayer(Time.deltaTime);
        }

        /// <summary>俯冲（I3）：水平真的朝预判落点走；竖直用 ease-in 落下来（重力感）。</summary>
        private void BeginDive()
        {
            _diveStart = transform.position;
            _diveTarget = ComputeDiveTarget();
            // A4/I4：**按"俯冲开始"计间隔**。
            //   原先记在 Recover 进入时，而 Recover 只占收招的一半 ⇒ 量出来的间隔偏小、
            //   判据形同虚设（实测 0.36 s 也过）。判定时刻才是玩家真正感知到的压迫节奏。
            float gap = Time.time - _lastDiveEndTime;
            _minObservedDiveGap = Mathf.Min(_minObservedDiveGap, gap);
            _lastDiveEndTime = Time.time;
        }

        private float _minObservedDiveGap = 999f;
        /// <summary>本场观测到的最小俯冲间隔（验收 A4，判据 ≥1.2 s）。</summary>
        public float MinObservedDiveGap => _minObservedDiveGap;

        /// <summary>T6：落点 = 玩家当前位置 + 玩家速度 × 0.35 s（预判，逼玩家变向）。</summary>
        private Vector3 ComputeDiveTarget()
        {
            if (!PlayerRef.Exists) return transform.position;
            Vector3 p = PlayerRef.Position;
            Vector3 v = Vector3.zero;
            var velProp = typeof(PlayerRef).GetProperty("Velocity",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            if (velProp != null) v = (Vector3)velProp.GetValue(null);
            return p + Flat(v) * divePredictSeconds;
        }

        private void DoDiveTravel(float u)
        {
            // 水平：起点 → 预判落点（匀速）
            Vector3 want = Vector3.Lerp(_diveStart, _diveTarget, u);
            Vector3 delta = Flat(want) - Flat(transform.position);
            float speed = circleMoveSpeed * diveSpeedMul;
            Vector3 step = delta.sqrMagnitude > 1e-6f
                ? delta.normalized * Mathf.Min(delta.magnitude, speed * Time.deltaTime)
                : Vector3.zero;
            MoveHorizontal(step);

            // 竖直：ease-in 落向打击高度（重力感）——**不落到地面**（见 strikeLift 注释）
            float g = u * u;
            _currentLift = Mathf.Lerp(DesiredHoverY + 0.35f, strikeLift, g);

            // 朝向落点
            if (Flat(delta).sqrMagnitude > 1e-4f)
                transform.rotation = Quaternion.Slerp(transform.rotation,
                    Quaternion.LookRotation(Flat(delta).normalized, Vector3.up), 8f * Time.deltaTime);
        }

        /// <summary>咬击：头颈前俯咬合（判定由 PerformHit 开 hitbox）。</summary>
        private void DoStrikeBite(float u)
        {
            _currentLift = strikeLift;
            float snap = Mathf.Sin(Mathf.PI * Mathf.Clamp01(u * 1.4f));   // 快速咬一下
            float pitch = -biteAmplitudeDeg * (1f - snap * 0.5f);
            int start = Mathf.Max(0, _spine.Count - biteHeadLinks);
            for (int i = 0; i < _spine.Count; i++)
            {
                if (i < start) { _spine[i].localRotation = _baseRot[i]; continue; }
                int k = i - start;
                _spine[i].localRotation = _baseRot[i] * Quaternion.Euler(pitch * (1f + 0.15f * k), 0f, 0f);
            }
        }

        /// <summary>
        /// 打击相位的最低高度（m）。
        ///
        /// ★ 这个值是**手感开关**，别随手改：
        ///   - 太小（旧值 0.12）⇒ 龙整个趴到地上，读起来是"地面怪扑一下"，
        ///     而且它 6 m 长的身子会**插进地面**，与"从天上冲下来咬一口"的
        ///     设计定位（策划案 §1）矛盾；
        ///   - 太大 ⇒ 咬不到人，玩家觉得"它没在打我"。
        ///   1.05 m 是"龙头够得着人、龙腹仍明显离地"的折中（主角身高约 1.75 m）。
        /// </summary>
        public float strikeLift = 1.05f;

        /// <summary>扫尾：贴地横扫，24 节脊骨自根向梢传播。</summary>
        private void DoStrikeSweep(float u)
        {
            _currentLift = sweepLift;
            float phase = 2f * Mathf.PI * sweepFrequency * (u * sweepGroundDuration);
            // ★ 传 phase 而不是 sin(phase)：内部会再取一次 sin，两个正弦相乘会把振幅压扁
            ApplySpineOffsetsRaw(sweepAmplitudeDeg, 0f, _spine.Count,
                                 sweepPhaseStepDeg, 1f, 1f, phase);
            transform.Rotate(Vector3.up, sweepYawAssist * Mathf.Sin(phase) * Time.deltaTime * 3f, Space.World);
        }

        /// <summary>扫尾的最低高度（m）。扫尾是"落地招"，比撕咬更低，但不触地。</summary>
        public float sweepLift = 0.65f;

        /// <summary>拉起回位（I2）：升回盘旋高度，姿态归位。循环在这里闭合。</summary>
        private void DoRecover(float u)
        {
            _currentLift = Mathf.Lerp(strikeLift, DesiredHoverY, Mathf.SmoothStep(0f, 1f, u));
            float pitch = -18f * (1f - u);      // 头颈上抬的余韵
            int start = Mathf.Max(0, _spine.Count - biteHeadLinks);
            for (int i = 0; i < _spine.Count; i++)
            {
                if (i < start) { _spine[i].localRotation = _baseRot[i]; continue; }
                _spine[i].localRotation = _baseRot[i] * Quaternion.Euler(0f, 0f, pitch);
            }
        }

        /// <summary>
        /// 悬停吐息（策划案 §3.4）：**不下降高度**。
        /// 玩家看到它悬着不动吐弹，就知道"这一轮它不下来，我可以打它"。
        /// </summary>
        private void DoBreathHover(float t)
        {
            _currentLift = DesiredHoverY;       // ★ 保持高度，与撕咬/扫尾明确区分
            _divePhaseName = "Hover";

            float total = Mathf.Max(0.01f, attackWindup + attackActive + attackRecover);
            float u = Mathf.Clamp01(t / total);
            float rise;
            if (u < 0.45f) rise = Mathf.SmoothStep(0f, 1f, u / 0.45f);
            else if (u < 0.72f) rise = 1f;
            else rise = Mathf.SmoothStep(1f, 0f, (u - 0.72f) / 0.28f);

            float pitch = -breathAmplitudeDeg * rise;
            int start = Mathf.Max(0, _spine.Count - breathHeadLinks);
            for (int i = 0; i < _spine.Count; i++)
            {
                if (i < start) { _spine[i].localRotation = _baseRot[i]; continue; }
                int k = i - start;
                _spine[i].localRotation = _baseRot[i] * Quaternion.Euler(pitch * (1f + 0.2f * k), 0f, 0f);
            }

            float phase = 2f * Mathf.PI * breathFrequency * t;
            float sway = 8f * rise * Mathf.Sin(phase);
            for (int i = 0; i < Mathf.Min(_spine.Count, start); i++)
                _spine[i].localRotation = _baseRot[i] * Quaternion.Euler(0f, sway * (i / (float)Mathf.Max(1, start)), 0f);

            FacePlayer(Time.deltaTime * 1.5f);
        }

        /// <summary>
        /// 每帧维护。
        ///
        /// ★ 为什么必须重写：龙很大（6 m）+ NavMeshAgent 半径 1.4，
        ///   一旦 agent 因为"生成点离开导航网格"而建不起来（桥会 Warn：
        ///   `Failed to create agent because it is not close enough to the NavMesh`），
        ///   龙会**自由落体** —— 实测掉到 -41895 m，肉眼看不见，而且不报错。
        ///   基类只在自己管移动的地方写 transform，没有"锁地"这一层，所以这里补上。
        /// </summary>
        protected override void Update()
        {
            base.Update();

            bool agentOk = _agent != null && _agent.enabled && _agent.isOnNavMesh;

            if (!_groundCaptured)
            {
                if (agentOk || _agent == null || !_agent.enabled)
                {
                    _groundY = transform.position.y;
                    _groundCaptured = true;
                }
            }
            else
            {
                if (!_airborne && !agentOk)
                {
                    var p = transform.position;
                    if (Mathf.Abs(p.y - _groundY) > 0.05f) { p.y = _groundY; transform.position = p; }
                }
                else if (agentOk) _groundY = transform.position.y;
            }

            // A1 统计：龙身是否在地面以上 1.5 m（验收判据 ≥0.60）
            _totalFrames++;
            if (_modelRoot != null && _modelRoot.position.y > _groundY + 1.5f) _airFrames++;
            _airborneRatio = _totalFrames > 0f ? _airFrames / _totalFrames : 0f;

            // 阶段推进（T7）
            UpdatePhase();
        }

        /// <summary>三阶段推进（策划案 §4）：越往后越"不落地"（更高、更急）。</summary>
        private void UpdatePhase()
        {
            if (!aerialLoop) return;
            float r = HealthRatio;
            int want = r > phase2AtRatio ? 1 : (r > phase3AtRatio ? 2 : 3);
            if (want == _phase) return;
            _phase = want;
            // 阶段演出的最小表达：盘旋高度立刻抬升（脊骨长啸幅度留给后续迭代）
            _currentLift = PhaseHoverHeight;
        }

        /// <summary>盘旋（旧路径：作为 Attack 里的"盘旋"招，legacy）。</summary>
        private void DoHoverOrbit(float t)
        {
            if (!_airborne) BeginHover();
            _currentLift = DesiredHoverY;
            _divePhaseName = "Circling";

            if (PlayerRef.Exists)
            {
                Vector3 center = PlayerRef.Position;
                _orbitAngle += hoverOrbitSpeedDeg * Time.deltaTime;
                float rad = _orbitAngle * Mathf.Deg2Rad;
                Vector3 want = center + new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad)) * hoverOrbitRadius;
                Vector3 delta = want - transform.position; delta.y = 0f;
                MoveHorizontal(delta * Time.deltaTime * 2.2f);

                Vector3 tangent = new Vector3(Mathf.Cos(rad), 0f, -Mathf.Sin(rad));
                if (tangent.sqrMagnitude > 1e-6f)
                    transform.rotation = Quaternion.Slerp(transform.rotation,
                        Quaternion.LookRotation(tangent, Vector3.up), 4f * Time.deltaTime);
            }

            float phase = 2f * Mathf.PI * hoverFrequency * t;
            // ★ 同样：传相位，不传 sin 后的值
            ApplySpineOffsetsRaw(hoverAmplitudeDeg, hoverPitchAmplitudeDeg, _spine.Count,
                                 hoverPhaseStepDeg, waveAmpRootGain, waveAmpHeadGain, phase);
        }

        /// <summary>
        /// 通用脊骨驱动：给每节叠一个旋转偏移，**相位沿链递增**（这就是"鞭子"的来源）。
        ///
        ///   offset_i = Euler( pitchEff(i), yawEff(i), 0 )，其中
        ///   yawEff(i) = yawMax · env(i) · sin(phase − i·φ)
        ///
        /// ★★ `phase` 由**调用方**传入（弧度），不在这里用 `Time.time` 自己算。
        ///    这曾经是个隐蔽的 bug：调用方算好 `sin(hoverFrequency·t)` 传进来，
        ///    这里又乘一个 `sin(sweepFrequency·t)` ⇒ **两个正弦相乘**，
        ///    实际振幅被压成两个频率的差/和频拍，实测每节只弯 0.2~9.7°
        ///    （参数写的是 11°），整条龙读起来是僵的。相位所有权必须只有一处。
        ///
        /// ★ 为什么要有 `env`（沿链包络）：真实动物的游动是**头稳尾摆** ——
        ///   头要用来瞄准，摆幅必须小；摆幅沿链线性增大到尾部最大。
        ///   没有包络时整条链等幅摆动，读起来像一根"来回搓的棍子"。
        ///
        /// ★ 为什么相位步进是"波长"：`φ` 决定相邻节的相位差，
        ///   一个完整波长占 `360/φ` 节。**身体长度 ≈ 一个波长**才像真游动；
        ///   波长短（φ 大）会在身上塞进好几个波，看起来像搓衣板。
        /// </summary>
        private void ApplySpineOffsetsRaw(float yawMaxDeg, float pitchMaxDeg, int links, float phaseStepDeg)
        {
            ApplySpineOffsetsRaw(yawMaxDeg, pitchMaxDeg, links, phaseStepDeg, 1f, 1f,
                2f * Mathf.PI * sweepFrequency * Time.time);
        }

        /// <param name="headGain">链首的振幅增益（0~1 通常）。</param>
        /// <param name="tailGain">链尾的振幅增益（&gt;1 表示放大）。</param>
        /// <param name="phase">当前波相位（弧度）。**由调用方给**，见上面注释。</param>
        private void ApplySpineOffsetsRaw(float yawMaxDeg, float pitchMaxDeg, int links,
                                          float phaseStepDeg, float headGain, float tailGain,
                                          float phase)
        {
            if (_spine.Count == 0) return;
            int n = links <= 0 ? _spine.Count : Mathf.Min(links, _spine.Count);
            float step = phaseStepDeg * Mathf.Deg2Rad;

            for (int i = 0; i < n; i++)
            {
                float ph = phase - i * step;
                float env = Mathf.Lerp(headGain, tailGain, n <= 1 ? 0f : i / (float)(n - 1));

                // ★★ 弯曲轴实测（dg_axis，每节统一叠 15° 偏移，量"偏移旋转轴 ↔ 该节链方向"的夹角）：
                //     绕局部 X  → 夹角 164.5°（≈ 与链方向平行）⇒ **自转/扭转，根本不弯**
                //     绕局部 Y  → 夹角  76.8°（⊥ 链方向），末端位移 y=-0.70 ⇒ **弯曲，且在水平面**
                //     绕局部 Z  → 夹角  82.0°（⊥ 链方向），末端位移 y=+1.75 ⇒ **弯曲，且在垂直面**
                //   所以：Euler(pitch, yaw, 0) 里的那个 pitch 其实是**把头拧一圈**，
                //   这正是"摆动很奇怪、像麻花"的直接来源。正确写法是
                //     水平蛇形 → 绕局部 Y；垂直起伏 → 绕局部 Z。
                float yaw = yawMaxDeg * env * Mathf.Sin(ph);
                // ★ 垂直分量**相位错开 90°**：同相位会让每节沿 45° 斜向弯 ⇒ 整条龙拧成螺旋。
                //   错开后 = 水平行波 + 正交的垂直起伏，才是真实鳗/蛇的立体游动。
                float pitch = pitchMaxDeg * env * Mathf.Sin(ph + Mathf.PI * 0.5f);

                _spine[i].localRotation = _baseRot[i] *
                    (Quaternion.Euler(0f, yaw, 0f) * Quaternion.Euler(0f, 0f, pitch));
            }
            // 未驱动的节保持基准姿态
            for (int i = n; i < _spine.Count; i++) _spine[i].localRotation = _baseRot[i];
        }

        /// <summary>回落到地面（只在旧行为 aerialLoop=false 时用到）。</summary>
        private void EndHover()
        {
            _airborne = false;
            _currentLift = 0f;
            for (int i = 0; i < _spine.Count; i++) _spine[i].localRotation = _baseRot[i];
            if (_modelRoot != null)
                _modelRoot.localPosition = _modelRootBaseLocalPos + Vector3.up * bodyLift;
        }

        /// <summary>
        /// 起飞进盘旋态。**注意 `_currentLift` 才是"飞多高"的唯一真相**，
        /// 它由 UpdateBodyLift 统一写进容器 localPosition.y（不再有一处写死 hoverHeight 的地方
        /// —— 旧代码在 Update 和 AttackMovement 里各写了一遍，阶段高度永远生效不了）。
        /// </summary>
        private void BeginHover()
        {
            _airborne = true;
            _orbitAngle = 0f;
            _currentLift = PhaseHoverHeight;
            // 巡游方向：起飞时顺着当前朝向，避免第一帧突然拧头
            _swimDir = Flat(transform.forward);
            if (_swimDir.sqrMagnitude < 1e-4f) _swimDir = Vector3.forward;
            _swimDir.Normalize();
        }

        /// <summary>
        /// 验收专用入口：强制进入盘旋。
        ///
        /// 为什么需要它：`BeginHover` 是私有方法，只有 `ChooseAttack` 挑到
        /// `DragonAttack.Hover` 时才会被调到 —— 而选招带随机权重，
        /// 自动化验收里"等它自己选到盘旋"既慢又不确定（曾经等 30 s 才轮到一次）。
        /// 这里给探针一个确定性的触发口，不改变任何运行期行为。
        /// </summary>
        public void ForceBeginHoverForTest() { BeginHover(); }

        /// <summary>验收专用：读当前是否在盘旋（`_airborne` 是私有字段）。</summary>
        public bool IsAirborneForTest => _airborne;

        // ---------------- 判定 ----------------
        /// <summary>
        /// 判定在 `attackWindup` 那一帧触发（基类口径），而在新架构里
        /// `attackWindup = 预兆 + 俯冲` ⇒ **判定恰好发生在"冲到最底、开始咬"的那一刻**。
        /// 这不是巧合，是刻意对齐的（见 ApplyAttackTiming 的注释）。
        /// </summary>
        protected override void PerformHit()
        {
            switch (_attack)
            {
                case DragonAttack.Bite:
                case DragonAttack.TailSweep:
                    if (_hitbox != null) _hitbox.Activate(Mathf.Max(0.08f, attackActive));
                    break;

                case DragonAttack.Breath:
                    // 吐息发生在悬停期（识别 windup 中点更贴合"张嘴"的视觉）
                    SpawnBreath();
                    break;

                case DragonAttack.HoverOrbit:
                    // 盘旋期不造成伤害（它的意义是走位/喘口气）
                    break;
            }
        }

        /// <summary>
        /// 吐息墨弹数（P3 加强为 5 颗，见策划案 §4）。基类无此概念，这里自管。
        /// </summary>
        private int PhaseBreathCount => _phase >= 3 ? 5 : (_phase == 2 ? 4 : breathProjectileCount);

        /// <summary>喷墨：从嘴的位置朝玩家铺开扇形墨弹。</summary>
        private void SpawnBreath()
        {
            if (breathProjectilePrefab == null)
            {
                Debug.LogWarning("[EnemyDragon] 没配 breathProjectilePrefab —— 龙息只会张嘴不吐东西");
                return;
            }
            if (!PlayerRef.Exists) return;

            Vector3 origin = GetHeadPosition();
            Vector3 aim = PlayerRef.Position + Vector3.up * 0.9f - origin;
            if (aim.sqrMagnitude < 1e-4f) aim = transform.forward;
            aim.Normalize();

            int cnt = Mathf.Max(1, PhaseBreathCount);
            for (int i = 0; i < cnt; i++)
            {
                // 扇形展开：中间的直射，两侧偏开
                float k = cnt == 1 ? 0f : (i / (float)(cnt - 1) - 0.5f) * 2f;   // -1 .. 1
                Vector3 dir = Quaternion.AngleAxis(k * breathSpreadDeg, Vector3.up) * aim;

                var go = Instantiate(breathProjectilePrefab, origin, Quaternion.LookRotation(dir, Vector3.up));
                go.name = "DragonBreath_" + i;
                var proj = go.GetComponent<InkProjectile>();
                if (proj != null)
                {
                    proj.ownerFaction = Faction.Enemy;
                    proj.owner = gameObject;
                    proj.speed = breathProjectileSpeed;
                    proj.damage = breathProjectileDamage;
                    proj.Launch(dir);
                }
                else
                {
                    Debug.LogWarning("[EnemyDragon] 墨弹 prefab 上没有 InkProjectile 组件");
                }
            }
        }

        /// <summary>嘴的位置：取脊骨链最后一节再往前一点。</summary>
        public Vector3 GetHeadPosition()
        {
            if (_spine.Count == 0) return transform.position + transform.forward * 1.5f + Vector3.up * 1.2f;
            var last = _spine[_spine.Count - 1];
            Vector3 fwd = last.forward;
            if (fwd.sqrMagnitude < 1e-6f) fwd = transform.forward;
            return last.position + fwd.normalized * 0.6f;
        }

        /// <summary>尾端位置（甩尾判定用）。</summary>
        public Vector3 GetTailPosition()
        {
            if (_spine.Count == 0) return transform.position - transform.forward * 2f;
            return _spine[Mathf.Min(_spine.Count - 1, _spine.Count / 2)].position;
        }

        public override bool TakeDamage(DamageInfo info)
        {
            bool ok = base.TakeDamage(info);
            // 龙是 Boss：被打不改动作，但受击时**必须能被打断盘旋**，否则玩家打不到它
            if (ok && _airborne && UnityEngine.Random.value < 0.45f) EndHover();
            return ok;
        }

        protected override void OnDied()
        {
            base.OnDied();
            EndHover();
        }
    }
}
