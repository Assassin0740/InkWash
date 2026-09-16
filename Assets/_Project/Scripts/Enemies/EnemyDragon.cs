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
        [Tooltip("★ 沿链的弯曲振幅（度/节）。旧值 11 太小 ⇒ 实测每节只弯 0.2~9.7°，龙是僵的。\n" +
                 "16 = 按交接文档 §3.3 的建议值上调（修好轴向后再上调到 14~18 看观感）。\n" +
                 "★ 改这个值必须同时改 Z_Enemy_MoLong.prefab —— prefab 里序列化了同名键。")]
        public float hoverAmplitudeDeg = 16f;
        [Tooltip("★ 游动频率（Hz）。真实蛇形游动是**低频**，旧值 0.55 偏快但可接受")]
        public float hoverFrequency = 0.45f;
        [Tooltip("★ 相位步进（度/节）。这是决定「波长」的关键：波长(节数) = 360 / 此值。旧值 40 ⇒ 每 9 节一个完整波（24 节里塞 2.7 个波，像搓衣板）；新值 15 ⇒ 约每 24 节一个完整波，即身体长 ≈ 一个波长 —— 这才是真实蛇/龙的游动")]
        public float hoverPhaseStepDeg = 15f;
        [Tooltip("★ 垂直面弯曲振幅（度/节）。真实游动不是纯水平摆尾，身体会上下起伏；只做水平摆动是「扁片在转」，加上这个才有立体游动感")]
        public float hoverPitchAmplitudeDeg = 1.5f;
        [Tooltip("★ 整体横向摆幅（m）—— 用户的「让它跟着正弦波移动」。" +
                 "只转骨骼时 i=0（尾/根侧）是支点、位移恒为 0（探针实测 0.000）⇒ 看着像「只有中间在动」。\n" +
                 "给模型容器叠一个横向正弦，整条龙（含尾端）才真的都在动。")]
        public float bodySwayAmp = 1.6f;
        [Tooltip("整体横向正弦的频率（Hz）。与 hoverFrequency 同频最自然（身体摆 = 路径摆）")]
        public float bodySwayFreq = 0.45f;
        // ── 四肢 / 分支骨 ──
        // 实测骨架：24 节脊柱单链之外还有 **77 个分支节点**（龙身共 280 个 Transform），
        //   其中 `脊柱[0] drgon_03` 下挂 3 条大链（后代 27 / 19 / 19 根骨），
        //   `脊柱[1..10]` 各挂 1 根单骨（背鳍）。
        //   旧代码只驱动脊柱 ⇒ **四肢从头到尾没动过**，这就是"很虚假"的来源。
        [Tooltip("★ 四肢（脊柱之外的分支骨）摆动幅度（度）。0 = 关闭")]
        public float limbSwingDeg = 14f;
        [Tooltip("四肢摆动频率（Hz）")]
        public float limbSwingFreq = 0.45f;
        [Tooltip("分支骨至少要有多少个后代才当成「肢体」来驱动 —— 用来滤掉背鳍那种单骨")]
        public int limbMinDescendants = 2;
        [Tooltip("相邻肢体之间的相位差（度），让四肢像划水一样依次摆动")]
        public float limbPhaseStepDeg = 60f;
        // ── 头部引导 ──
        [Tooltip("★ 头部引导偏航上限（度）：转弯时头先转、身体再跟，别让头被身体拖着走。\n" +
                 "★ 实测：18° 会**长期顶满**（头一直歪着，比不引导更难看），收敛到 8°。")]
        public float headLeadYawDeg = 8f;
        [Tooltip("转向速率 → 头部偏航 的增益。\n" +
                 "★ 实测教训：0.35 会**一直顶满 18°**，头长期歪着比不引导更难看。\n" +
                 "  0.08 时直线巡航接近 0、只在真转弯时才甩头。")]
        public float headLeadGain = 0.03f;

        private readonly List<Transform> _limbRoots = new List<Transform>();
        private readonly List<int> _limbParentIndex = new List<int>();
        private readonly List<Quaternion> _limbBaseRel = new List<Quaternion>();
        private Vector3 _prevSwimDir = Vector3.forward;
        private float _headYaw;
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

        // ================= 判定盒（兜底 + 逐帧贴合骨链）=================
        // ★★ 先更正一条**我踩过的结论**（留在这里防复发）：
        //   `Z_Enemy_MoLong.prefab` 里**搜不到** `Hitbox` 的脚本 guid，也搜不到字面量 `damage: 22`，
        //   于是我曾误判"龙没有判定盒、撕咬 0 伤害"。**这是错的。**
        //   运行时实测（`GetComponents<Component>()`）：龙的**根节点**组件为
        //     Transform, NavMeshAgent, CapsuleCollider, Animator, **Hitbox**, InkMaterialSwap, EnemyDragon
        //   且 `radius = 1.2 / damage = 22 / pointA = (0,1,-1.5) / pointB = (0,1.2,2.5)` ——
        //   与策划案 §3.2「咬击半径 1.2 / 命中伤害 22 | 现有 prefab」**完全一致**。
        //
        //   为什么静态搜不到：**`Z_Enemy_MoLong` 是 prefab 变体**，`Hitbox` 声明在变体的**基**里，
        //   数值是变体用 `propertyPath: damage` + `value: 22` 这种**覆盖**语法写的。
        //   所以"按 guid 反 grep .meta/prefab 文本"这类判据对**变体**会给出假阴性。
        //   ⇒ 教训：判"组件在不在"要**实例化后看组件表**，不要只 grep 文本。
        //
        //   因此下面的 `EnsureHitbox()` 只是**兜底**（prefab 里已有 ⇒ 直接提前返回，不会执行到），
        //   真正生效的是 prefab 自己那个 Hitbox。它的参数**不要在代码里覆写** ——
        //   本项目硬规矩：prefab 序列化值优先，代码改默认值不生效。
        [Header("★ 判定盒兜底（prefab 已有则不生效；仅当 prefab 缺失时才自建）")]
        [Tooltip("兜底判定胶囊半径（m）。策划案 §3.2：1.2")]
        public float hitRadius = 1.2f;
        [Tooltip("兜底判定线段是否覆盖整条身体（含尾）")]
        public bool hitCoversWholeBody = true;
        public float biteDamageFallback = 22f;
        public float hitKnockback = 6f;
        public float hitStunSeconds = 0.4f;
        public float hitStopSeconds = 0.06f;

        private Hitbox _dragonHitbox;

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

        /// <summary>
        /// 拉直后的基准姿态，表达为**模型容器局部系**（不是每节自己的局部系）。
        ///
        /// ★★ 为什么必须这样存：`StraightenSpineBase()` 用 `FromToRotation(段方向, fwd)` 拉直，
        ///   那是**最小旋转** —— 它把每节对到直线上之后，**绕链方向的 roll 不受控、且逐节不同**。
        ///   于是"骨骼局部 Y"在拉直后不再指"上"：`localRotation * Euler(0, yaw, 0)`
        ///   实际变成了**上下摆**（实测垂直跨度 10.787 m，见 Tools/reports/dg_axis2.txt）。
        ///   换成容器局部系后，绕 (0,1,0) 转就**一定**是左右摆，与骨骼 roll 完全无关。
        /// </summary>
        private readonly List<Quaternion> _baseRel = new List<Quaternion>();

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
        private Vector3 _passDir = Vector3.forward;   // 俯冲方向（"穿过主角"沿它继续滑出）
        private float _airFrames, _totalFrames; // A1 统计
        private bool _circleHoldPose;           // 盘旋期是否已经摆好姿态
        private float _circleCd;                // 盘旋→选招的剩余冷却

        protected override void Awake()
        {
            base.Awake();
            ResolveSpine();
            EnsureHitbox();
            ResolveLimbs();      // 必须在 ResolveSpine（含拉直）之后：基准要取拉直后的姿态
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
            _baseRel.Clear();

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
            // ★ 不开拉直时也要采一次，保证 _baseRel 与 _spine 等长（驱动写世界旋转要靠它）。
            else CaptureBaseRel();
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
            if (_spine.Count < 2) { CaptureBaseRel(); return; }

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

            // ★ 额外记一份「相对模型容器」的基准旋转（形状描述，与容器朝向解耦）
            CaptureBaseRel();
        }

        /// <summary>
        /// 把当前脊骨姿态记成**相对模型容器**的基准旋转 `_baseRel`。
        ///
        /// ★★ 为什么不直接用骨骼局部旋转 `_baseRot` 当基准（本轮根因）：
        ///   `StraightenSpineBase()` 的 `FromToRotation(段方向, fwd)` 是**最小旋转**，
        ///   拉直后"骨骼局部 Y"不再指"上"、且逐节不同。用局部轴做弯曲，几何上就
        ///   不是"绕上轴左右摆"了 —— 实测垂直跨度 10.787 m、水平只有 2.123 m。
        ///   存成容器局部系后，写入时用
        ///     `rotation = _modelRoot.rotation * AngleAxis(角, 语义轴) * _baseRel[i]`
        ///   就能保证"绕 up ⇒ 一定左右、绕 right ⇒ 一定俯仰"。
        /// </summary>
        private void CaptureBaseRel()
        {
            Quaternion rootRot = _modelRoot != null ? _modelRoot.rotation : transform.rotation;
            _baseRel.Clear();
            for (int i = 0; i < _spine.Count; i++)
            {
                if (_spine[i] == null) { _baseRel.Add(Quaternion.identity); continue; }
                _baseRel.Add(Quaternion.Inverse(rootRot) * _spine[i].rotation);
            }
        }

        /// <summary>
        /// 运行时补一个跟着骨链走的判定盒（prefab 里没有，缘由见字段区注释）。
        /// `Hitbox` 是**纯脚本**：自己用 `Physics.OverlapCapsuleNonAlloc` 做线段胶囊扫描，
        /// 不需要 Collider、也不需要 Rigidbody ⇒ 建起来没有任何物理副作用。
        /// </summary>
        private void EnsureHitbox()
        {
            if (_hitbox != null) return;                        // 将来 prefab 补了就用它自己的
            if (_spine.Count == 0 || _modelRoot == null) return;
            if (_dragonHitbox != null) return;

            var host = new GameObject("DragonHitbox");
            host.transform.SetParent(_modelRoot, false);
            host.transform.localPosition = Vector3.zero;
            host.transform.localRotation = Quaternion.identity;
            host.transform.localScale = Vector3.one;

            _dragonHitbox = host.AddComponent<Hitbox>();
            _dragonHitbox.owner = gameObject;
            _dragonHitbox.ownerFaction = Faction.Enemy;
            _dragonHitbox.radius = hitRadius;
            _dragonHitbox.damage = biteDamageFallback;
            _dragonHitbox.knockback = hitKnockback;
            _dragonHitbox.hitStun = hitStunSeconds;
            _dragonHitbox.hitStop = hitStopSeconds;
            _dragonHitbox.oncePerTargetInWindow = true;
            _dragonHitbox.targetMask = ~0;
            _hitbox = _dragonHitbox;                            // 基类/PerformHit 走同一条路

            // 诊断用：让探针能确认"确实建起来了"
            _spineLinksFound = _spine.Count;
        }

        /// <summary>
        /// 每帧把判定线段贴到骨链上。
        ///
        /// 端点写的是**模型容器（`_modelRoot`）局部坐标**，而判定盒自己也挂在 `_modelRoot` 下、
        /// 本地变换是单位阵 ⇒ `Hitbox` 内部 `transform.TransformPoint(pointA/B)` 正好还原成世界坐标，
        /// 于是胶囊与身体严格重合（含蛇形波的摆动）。只在判定窗口开着时才同步，常态零开销。
        /// </summary>
        private void SyncHitboxToSpine()
        {
            if (_dragonHitbox == null || _modelRoot == null) return;
            if (!_dragonHitbox.IsActive) return;
            if (_spine.Count < 2) return;

            int last = _spine.Count - 1;
            int first = hitCoversWholeBody ? 0 : Mathf.Max(0, _spine.Count - biteHeadLinks);
            var a = _spine[first];
            var b = _spine[last];
            if (a == null || b == null) return;

            _dragonHitbox.pointA = _modelRoot.InverseTransformPoint(a.position);
            _dragonHitbox.pointB = _modelRoot.InverseTransformPoint(b.position);
        }

        /// <summary>
        /// 收集**脊柱之外**的分支骨（四肢 / 鳍 / 爪），并记下它们的容器系基准姿态。
        ///
        /// 为什么必须单独收集：`ResolveSpine()` 每层只取「第一个非 SMR 子节点」，
        /// 采到的是纯脊柱单链；四肢是**旁挂的分支**，旧代码从来没碰过它们 ⇒ 龙游动时爪子纹丝不动。
        /// 判据用「后代数量 ≥ limbMinDescendants」：背鳍那种单骨会被滤掉，只驱动真正的肢体链。
        /// </summary>
        private void ResolveLimbs()
        {
            _limbRoots.Clear();
            _limbParentIndex.Clear();
            _limbBaseRel.Clear();
            if (_spine.Count == 0) return;

            var inSpine = new HashSet<Transform>();
            for (int i = 0; i < _spine.Count; i++) if (_spine[i] != null) inSpine.Add(_spine[i]);

            for (int i = 0; i < _spine.Count; i++)
            {
                var t = _spine[i];
                if (t == null) continue;
                for (int k = 0; k < t.childCount; k++)
                {
                    var c = t.GetChild(k);
                    if (c == null || inSpine.Contains(c)) continue;
                    if (CountDescendants(c) < limbMinDescendants) continue;
                    _limbRoots.Add(c);
                    _limbParentIndex.Add(i);
                }
            }

            // ★ 四肢的基准必须存 **localRotation**（相对父脊柱节点），**不能**像脊柱那样存容器系世界旋转。
            //   原因（实测踩到）：四肢挂在脊柱节下面，脊柱每帧都在摆动；
            //   如果我们把四肢的**世界**旋转写死成"容器系基准 + 摆动"，
            //   那父节点一转，四肢相对父节点的局部姿态就被反向甩出去 ——
            //   实测末端摆幅 930 m（正常应 < 0.5 m），四肢疯狂乱甩。
            //   写成局部旋转后，四肢会**跟着父节点走**，再叠自己的摆动。
            for (int k = 0; k < _limbRoots.Count; k++)
                _limbBaseRel.Add(_limbRoots[k].localRotation);
        }

        private static int CountDescendants(Transform t)
        {
            if (t == null) return 0;
            int n = 0;
            for (int i = 0; i < t.childCount; i++) n += 1 + CountDescendants(t.GetChild(i));
            return n;
        }

        /// <summary>
        /// 四肢正弦摆动：绕**容器 right** 前后划（像划水/蹬腿），相位沿身体依次错开，
        /// 左右两侧（同一父节点的第 1、2 个分支）反相，避免"同手同脚"。
        /// </summary>
        private void ApplyLimbMotion(float phase)
        {
            if (_limbRoots.Count == 0 || limbSwingDeg <= 0f) return;

            float lp = 2f * Mathf.PI * limbSwingFreq * Time.time;
            float step = limbPhaseStepDeg * Mathf.Deg2Rad;

            for (int k = 0; k < _limbRoots.Count && k < _limbBaseRel.Count; k++)
            {
                var b = _limbRoots[k];
                if (b == null) continue;
                float ph = lp - _limbParentIndex[k] * step + (k % 2 == 0 ? 0f : Mathf.PI);
                float swing = limbSwingDeg * Mathf.Sin(ph);
                // ★ 局部旋转叠加：四肢跟着父脊柱节点走，再叠自己的前后划动
                b.localRotation = _limbBaseRel[k] * Quaternion.AngleAxis(swing, Vector3.right);
            }
        }

        /// <summary>
        /// 头部引导：转弯时给头颈叠一个**绕容器 up** 的偏航，越靠头端权重越大。
        ///
        /// 为什么要：真实的蛇/鳗是**头先转向、身体再扫过去**；旧实现只有 `transform.LookRotation(_swimDir)`
        /// 转整个根节点，头本身没有任何额外朝向 ⇒ 转弯时看着像"整条龙被硬掰过去"。
        /// 注意与"头要稳"不冲突：`waveAmpHeadGain` 管的是**抖动**，这里管的是**朝向**。
        /// </summary>
        private void ApplyHeadSteer()
        {
            if (_spine.Count == 0) return;
            if (Mathf.Abs(_headYaw) < 0.01f) return;

            Quaternion rootRot = _modelRoot != null ? _modelRoot.rotation : transform.rotation;
            int start = Mathf.Max(0, _spine.Count - biteHeadLinks);
            int span = Mathf.Max(1, _spine.Count - start);

            for (int i = start; i < _spine.Count; i++)
            {
                if (_spine[i] == null) continue;
                float w = (i - start + 1f) / span;                       // 越靠头越强
                // 在世界系里绕「容器 up」额外转一点，叠加到当前（波驱动后的）姿态上
                Quaternion add = rootRot * Quaternion.AngleAxis(_headYaw * w, Vector3.up)
                                 * Quaternion.Inverse(rootRot);
                _spine[i].rotation = add * _spine[i].rotation;
            }
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

            // ── 头部引导：按转向速率给头颈叠一个偏航，让头「领」着走 ──
            float dyaw = Vector3.SignedAngle(Flat(_prevSwimDir), Flat(_swimDir), Vector3.up);
            float rate = dyaw / Mathf.Max(1e-4f, Time.deltaTime);
            float wantYaw = Mathf.Clamp(rate * headLeadGain, -headLeadYawDeg, headLeadYawDeg);
            _headYaw = Mathf.Lerp(_headYaw, wantYaw, 1f - Mathf.Exp(-8f * Time.deltaTime));
            _prevSwimDir = _swimDir;
            ApplyHeadSteer();
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
            // ★ 整体横向正弦 —— 用户的「让它跟着正弦波移动」。
            //   为什么需要：脊骨驱动写的是**世界旋转**，i=0（尾/根侧）是支点，
            //   它的位置由父节点决定 ⇒ 位移恒为 0（探针实测逐节 X 跨度 i=0 → 0.000），
            //   观感就是「只有中段在扭，尾巴钉在原地」。给模型容器叠一层横向正弦，
            //   整条龙（含尾端）就真的都在动。
            //   基准必须用 _modelRootBaseLocalPos 而不是当前值 —— 否则每帧累加会漂走。
            float sway = 0f;
            if (_airborne && bodySwayAmp > 0f)
                sway = bodySwayAmp * Mathf.Sin(2f * Mathf.PI * bodySwayFreq * Time.time);

            _modelRoot.localPosition = new Vector3(
                _modelRootBaseLocalPos.x + sway,
                Mathf.MoveTowards(cur, want, step),
                _modelRootBaseLocalPos.z);
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
            // ★ 俯仰必须绕**容器 right**，不能用骨骼局部 X —— 拉直后局部 X 与链方向近乎平行，
            //   绕它转是"拧麻花"而不是"低头"（dg_axis 实测夹角 164.5°）。
            Quaternion rootRot = _modelRoot != null ? _modelRoot.rotation : transform.rotation;
            for (int i = 0; i < _spine.Count; i++)
            {
                if (i < start) { _spine[i].localRotation = _baseRot[i]; continue; }
                int k = i - start;
                _spine[i].rotation = rootRot
                    * Quaternion.AngleAxis(pitch * (1f + 0.12f * k), Vector3.right)
                    * _baseRel[i];
            }
            FacePlayer(Time.deltaTime);
        }

        /// <summary>俯冲（I3）：水平真的朝预判落点走；竖直用 ease-in 落下来（重力感）。</summary>
        private void BeginDive()
        {
            _diveStart = transform.position;
            _diveTarget = ComputeDiveTarget();

            // "穿过"用的方向：从俯冲起点指向落点（退化为自身前方时也安全）
            Vector3 pd = Flat(_diveTarget - _diveStart);
            _passDir = pd.sqrMagnitude > 0.01f ? pd.normalized : Flat(transform.forward).normalized;
            if (_passDir.sqrMagnitude < 0.01f) _passDir = Vector3.forward;
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

        /// <summary>
        /// "唰一下**穿过**主角"：打击 / 拉起相位沿俯冲方向继续前冲，速度随相位线性衰减到 0。
        ///
        /// 为什么必须有：一次俯冲的水平行程只够"龙头够到玩家"，龙随即**停在玩家身上**，
        /// 读起来是"俯冲到脚下咬一口"，而不是用户点名要的"冲过去、穿过去"（策划案 §3.2）。
        /// 让 Strike(0.22 s) + Recover(0.58 s) 沿同方向继续滑出，
        /// 总行程 ≈ 15.6×0.22 + 15.6/2×0.58 ≈ 8 m ≈ **一个身长**，
        /// 于是整条龙越过玩家、从另一侧冲出之后才拉起。
        /// </summary>
        private void PassThroughStep(float mul)
        {
            if (mul <= 0f) return;
            MoveHorizontal(_passDir * (circleMoveSpeed * diveSpeedMul * mul * Time.deltaTime));
        }

        /// <summary>咬击：头颈前俯咬合（判定由 PerformHit 开 hitbox）。</summary>
        private void DoStrikeBite(float u)
        {
            _currentLift = strikeLift;
            PassThroughStep(1f);                 // 穿过：判定帧之后继续沿俯冲方向滑出
            float snap = Mathf.Sin(Mathf.PI * Mathf.Clamp01(u * 1.4f));   // 快速咬一下
            float pitch = -biteAmplitudeDeg * (1f - snap * 0.5f);
            int start = Mathf.Max(0, _spine.Count - biteHeadLinks);
            Quaternion rootRot = _modelRoot != null ? _modelRoot.rotation : transform.rotation;
            for (int i = 0; i < _spine.Count; i++)
            {
                if (i < start) { _spine[i].localRotation = _baseRot[i]; continue; }
                int k = i - start;
                _spine[i].rotation = rootRot
                    * Quaternion.AngleAxis(pitch * (1f + 0.15f * k), Vector3.right)
                    * _baseRel[i];
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
            PassThroughStep(1f);                 // 穿过：与撕咬共用俯冲段，同样继续滑出
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
            PassThroughStep(1f - u);             // 穿过收尾：随拉起把前冲速度线性收掉
            float pitch = -18f * (1f - u);      // 头颈上抬的余韵
            int start = Mathf.Max(0, _spine.Count - biteHeadLinks);
            Quaternion rootRot = _modelRoot != null ? _modelRoot.rotation : transform.rotation;
            for (int i = 0; i < _spine.Count; i++)
            {
                if (i < start) { _spine[i].localRotation = _baseRot[i]; continue; }
                _spine[i].rotation = rootRot
                    * Quaternion.AngleAxis(pitch, Vector3.right)
                    * _baseRel[i];
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
            Quaternion rootRot = _modelRoot != null ? _modelRoot.rotation : transform.rotation;
            // 头颈抬起：绕**容器 right** 俯仰（局部轴在拉直后不再指横轴）
            for (int i = 0; i < _spine.Count; i++)
            {
                if (i < start) { _spine[i].localRotation = _baseRot[i]; continue; }
                int k = i - start;
                _spine[i].rotation = rootRot
                    * Quaternion.AngleAxis(pitch * (1f + 0.2f * k), Vector3.right)
                    * _baseRel[i];
            }

            float phase = 2f * Mathf.PI * breathFrequency * t;
            float sway = 8f * rise * Mathf.Sin(phase);
            // 尾段随吐息左右摆：绕**容器 up**（与蛇形同一"左右"约定）
            for (int i = 0; i < Mathf.Min(_spine.Count, start); i++)
                _spine[i].rotation = rootRot
                    * Quaternion.AngleAxis(sway * (i / (float)Mathf.Max(1, start)), Vector3.up)
                    * _baseRel[i];

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

            // ★★ 关键修复：把"想飞多高"（`_currentLift`）每帧写进容器 localPosition.y。
            //
            //   为什么必须放在这里、而不能只留在 `AttackMovement` 里：
            //   `UpdateBodyLift()` 原本**只有 `AttackMovement` 一个调用点**，而
            //   `AttackMovement` 只在 Attack 状态被 `EnemyBase.TickAttack` 调用（EnemyBase.cs:367）
            //   ⇒ 龙在常态 **Chase/盘旋** 期间根本没有人写高度，`TickCircling` 里那句
            //   `_currentLift = PhaseHoverHeight;` 等于「只设不用」。
            //   实测（演示场条目 17 盘旋）：`_modelRoot.position.y = 0.05 m` —— 龙是**贴着地面飞**的，
            //   与 I1「龙的默认态就是在天上盘旋」直接矛盾，也让 `ActionShowcase.DriveDragon`
            //   里「高度不要自己设，交给 `_currentLift`」的设计前提落空。
            //   （交接文档 §2.1 那句「实测首节世界 Y ≈ 4.43 m」是高度收口到 `_currentLift` **之前**量的。）
            //
            //   ★ 放在 `base.Update()` **之后**：Attack 态里 `AttackMovement` 已经写过一次，
            //     而 `UpdateBodyLift` 内部是幂等的 `MoveTowards(cur, want, step)`，重复调用无副作用；
            //     放这个位置还能让下面的 A1 统计（`_airborneRatio`）读到**本帧**的真实高度。
            UpdateBodyLift();

            // 判定线段每帧贴合骨链（只在判定窗口开着时才有开销）
            SyncHitboxToSpine();

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
            if (_baseRel.Count < _spine.Count) CaptureBaseRel();   // 防御：基准未就绪则补采
            int n = links <= 0 ? _spine.Count : Mathf.Min(links, _spine.Count);
            float step = phaseStepDeg * Mathf.Deg2Rad;

            // ★★ 弯曲轴：一律用**模型容器空间的语义轴**，绝不用骨骼局部轴。
            //
            //   历史坑（务必别再踩）：`dg_axis` 曾测得「绕局部 Y = 水平弯、绕局部 Z = 垂直弯」，
            //   那条结论是在**未拉直的大 C 形基准**上量的。加上 `StraightenSpineBase()` 之后
            //   （`FromToRotation` 的最小旋转带来**逐节不同的 roll**），"局部 Y"不再指"上" ——
            //   继续用局部轴的结果就是**上下蜿蜒**：`dg_axis2` 实测垂直跨度 10.787 m、
            //   水平只有 2.123 m。换成容器轴后同一探针测得水平 2.395 m、垂直 0.000 m。
            //
            //   ⇒ 绕容器 `up` 一定是左右；绕容器 `right` 一定是俯仰。与骨骼 roll 无关。
            Quaternion rootRot = _modelRoot != null ? _modelRoot.rotation : transform.rotation;

            for (int i = 0; i < n; i++)
            {
                float ph = phase - i * step;
                float env = Mathf.Lerp(headGain, tailGain, n <= 1 ? 0f : i / (float)(n - 1));

                // 水平行波（左右蛇形）
                float yaw = yawMaxDeg * env * Mathf.Sin(ph);
                // ★ 垂直分量**相位错开 90°**：同相位会让每节沿 45° 斜向弯 ⇒ 整条龙拧成螺旋。
                //   错开后 = 水平行波 + 正交的垂直起伏，才是真实鳗/蛇的立体游动。
                float pitch = pitchMaxDeg * env * Mathf.Sin(ph + Mathf.PI * 0.5f);

                // ★ 写**世界旋转**（`rotation`）而非 `localRotation`：每节的角度是"相对基准的绝对角"，
                //   不受父节点影响 ⇒ 波形严格是 sin(phase − i·φ)，正是"行波"；
                //   逐节累加的弯曲由链式父子关系自然产生（与原来的"逐节增量"等价）。
                _spine[i].rotation = rootRot
                                     * Quaternion.AngleAxis(yaw, Vector3.up)      // 左右
                                     * Quaternion.AngleAxis(pitch, Vector3.right) // 俯仰
                                     * _baseRel[i];
            }
            // 未驱动的节保持基准姿态
            for (int i = n; i < _spine.Count; i++) _spine[i].localRotation = _baseRot[i];

            // 四肢（脊柱之外的分支骨）跟着一起摆 —— 否则只有身子在动、爪子钉着不动
            ApplyLimbMotion(phase);
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

        /// <summary>
        /// 验收 / 演示专用：**直接推进到攻击态**（`ActionShowcase` 的 18/19/20 条目靠它开招）。
        ///
        /// ★★ 这个方法曾经**根本不存在**：`ActionShowcase.TickDragon` 用反射拿它 ——
        ///   `GetMethod("ForceEnterAttackForTest")` + `if (pEnter != null) pEnter.Invoke(...)`，
        ///   而 `EnemyDragon` 里从没有过这个名字 ⇒ `!= null` 把 null **静默吞掉**，
        ///   于是**演示场 18（俯冲撕咬）/ 19（俯冲扫尾）/ 20（吐息）三个条目永远不会开招**，
        ///   采样窗口里龙一直在盘旋。
        ///   （作者注释里"只调一次 ForceNextAttack 然后马上出图 ⇒ 三招出图完全一样"记录的就是这个症状，
        ///    当时被归因成"冷却没走完"。反射式 API 的这个坑：**名字写错不报错，只是永远不生效**。）
        ///   顺序要求：调用方要**先** `ForceNextAttackForTest(招名)`、**再**调本方法 —— 因为
        ///   `Enter(Attack)` 会触发 `ChooseAttack()` 消费那个"下一招"标记。
        /// </summary>
        public void ForceEnterAttackForTest() { Enter(EnemyState.Attack); }

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
                    // 伤害/击退/硬直**全部走 prefab**（龙的 Hitbox 是 prefab 自带，radius 1.2 / damage 22，
                    // 与策划案 §3.2 一致）。这里**不要**在代码里覆写：本项目硬规矩是 prefab 序列化值优先，
                    // 代码赋值会把策划在 Inspector 里调好的数值悄悄改掉。
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
