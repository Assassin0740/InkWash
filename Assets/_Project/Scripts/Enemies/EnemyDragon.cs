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
    public class EnemyDragon : EnemyBase, InkWash.Effects.IDragonSpineSource
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
        public float breathProjectileSpeed = 16f;
        public float breathProjectileDamage = 14f;
        public int breathProjectileCount = 3;
        public float breathSpreadDeg = 9f;

        [Header("动作 · 盘旋")]
        [Tooltip("★ 改本节任何数值都必须同时改 Z_Enemy_MoLong.prefab —— prefab 里序列化了同名键，\n" +
                 "  只改 C# 默认值不生效（本项目硬规矩 #6）。")]
        public float hoverHeight = 7.0f;
        public float hoverOrbitRadius = 11f;
        public float hoverOrbitSpeedDeg = 55f;
        [Tooltip("★★ 沿链的弯曲振幅（度/节）—— 「像不像真龙」的第一决定量。\n" +
                 "★ 它不等于「侧摆幅度」：链的侧向偏移 ≈ 振幅 × 波长 / (2π)。\n" +
                 "  16° ⇒ 侧摆仅 0.47 m，而身长 8.2 m ＝ **只有 5.7%** ⇒ 肉眼就是一根直棍。\n" +
                 "  35° ⇒ 侧摆约 1.4 m ＝ 身长的 17%，与真蛇（10~15%）、游戏里的龙（≈20%）同量级。\n" +
                 "★ 尾侧还会再乘 waveAmpRootGain(1.30) ⇒ 尾端实际约 45°/节。")]
        public float hoverAmplitudeDeg = 35f;
        [Tooltip("★ 游动频率（Hz）。真实蛇形游动是**低频**。\n" +
                 "★ 振幅从 16° 拉到 35° 之后，0.45 Hz 会显得急躁 ⇒ 降到 0.27\n" +
                 "  （约 3.7 s 一个周期），巨物感 / 重量感才出得来。")]
        public float hoverFrequency = 0.27f;
        [Tooltip("★★ 相位步进（度/节）—— 决定「波长」：波长(节数) = 360 / 此值。\n" +
                 "  15° ⇒ 24 节 ＝ **恰好一个整波**。此时正弯与负弯在积分上**精确抵消**，\n" +
                 "        头尾都回到中轴线上 ⇒ 身体读起来仍是「直」的，只是中段鼓一下。\n" +
                 "  22.5° ⇒ 每 16 节一个波、24 节 ＝ **1.5 个波** ⇒ 头尾朝相反方向，\n" +
                 "        才是有进有出的「S 形」，同时避开了整波的自我抵消。")]
        public float hoverPhaseStepDeg = 22.5f;
        [Tooltip("★★ 垂直面弯曲振幅（度/节）—— 决定「扁片 / 立体」。\n" +
                 "★ 真正决定观感的是**垂直与水平的振幅比**，不是相位：\n" +
                 "  旧值 1.5 / 16 ＝ 9% ⇒ 近乎纯水平摆动，每条骨都只在水平面里转，\n" +
                 "  整条龙就是一张「会摆的扁片」。\n" +
                 "  龙是三维游动物，垂直要占水平的 40~60%（16 / 35 ≈ 46%）。")]
        public float hoverPitchAmplitudeDeg = 16f;
        [Tooltip("★ 整体横向摆幅（m）—— 用户的「让它跟着正弦波移动」。" +
                 "只转骨骼时 i=0（尾/根侧）是支点、位移恒为 0（探针实测 0.000）⇒ 看着像「只有中间在动」。\n" +
                 "给模型容器叠一个横向正弦，整条龙（含尾端）才真的都在动。")]
        public float bodySwayAmp = 1f;
        [Tooltip("整体横向正弦的频率（Hz）。与 hoverFrequency 同频最自然（身体摆 = 路径摆）")]
        public float bodySwayFreq = 0.27f;
        // ── 四肢 / 分支骨 ──
        // 实测骨架：24 节脊柱单链之外还有 **77 个分支节点**（龙身共 280 个 Transform），
        //   其中 `脊柱[0] drgon_03` 下挂 3 条大链、`脊柱[13] drgon_016` 下挂 2 条、
        //   `脊柱[22] drgon_025` 下挂 **14 条**，`脊柱[1..10]` 各挂 1 根单骨（背鳍）。
        //   旧代码只驱动脊柱 ⇒ **四肢从头到尾没动过**，这就是"很虚假"的来源。
        //
        // ★★★ 第十七轮更正：「分支骨 = 四肢」这个假设**是错的**。
        //   探针 `drg_axis` 实测（报告 `Tools/reports/drg_axis_before.txt`）：
        //   19 条被驱动的分支里**只有 4 条是腿**，其余 15 条是**整条尾巴 + 头部装饰簇**：
        //     drgon_0206 父=脊柱[0]  子树 28 骨 / 伸展 **3.24 m** / 首子方向 (0.02, 0.11, −0.99)
        //                ⇒ 沿**身体轴**伸出的 3.24 m 长链 = **整条尾巴**
        //     drgon_054 … drgon_0138（共 **14 条**）父=脊柱[22] = **头饰簇**（鬃 / 须 / 角 / 颌）
        //   把 ±limbSwingDeg 绕容器 right 套到这 15 条上，就直接产生了用户这轮的反馈：
        //     · 尾巴绕**横轴**上下翻 ⇒ 末端上下走 ±3.24·sin25° ≈ ±1.37 m
        //       ⇒「尾巴不在同一个水平线上、单独摆动」
        //     · 头饰簇乱翻 ⇒ 头顶一片炸毛，看着就像「头歪了、跟落枕一样」
        //   ★ 同一次探针也证明**脊柱本身完全水平**：段方向 pitch 恒 0.00°、
        //     链高差 ySpread 恒 0.000 m、复形旋转轴偏离容器 up 0.00°（纯 yaw）。
        //     所以"歪"不是波形算错，而是**这 15 条非腿分支被当成腿驱动**。
        [Tooltip("★ 四肢摆动幅度（度）。0 = 关闭。旧值 14° 在 8 m 长的身子上读不出来（用户反馈：爪子没动）⇒ 25°")]
        public float limbSwingDeg = 25f;
        [Tooltip("（已废弃，不再读取）四肢相位现在跟随主行波 swimWaveFreq。保留仅为序列化兼容")]
        public float limbSwingFreq = 0.27f;
        [Tooltip("分支骨至少要有多少个后代才当成「肢体」来驱动 —— 用来滤掉背鳍那种单骨")]
        public int limbMinDescendants = 2;
        [Tooltip("相邻肢体之间的相位差（度），让四肢像划水一样依次摆动")]
        public float limbPhaseStepDeg = 60f;
        [Tooltip("★ 距**头端**这么多节以内的分支不划水（头饰簇：鬃 / 须 / 角 / 颌）。\n" +
                 "实测这条龙的 `脊柱[22]` 一个节就挂出 **14 条**分支，全按 ±limbSwingDeg 驱动\n" +
                 "⇒ 头顶炸毛，看起来像「头歪了、落枕」（用户第十七轮反馈）。\n" +
                 "2 = 排除 `脊柱[Count-2]` 与 `脊柱[Count-1]` 上的分支。")]
        public int limbHeadExcludeLinks = 2;
        [Tooltip("★ 分支首子方向与**身体轴（容器 +Z）**的 |dot| 达到此值 ⇒ 判为「沿身体轴延伸的分支」（**尾巴**），不划水。\n" +
                 "实测：尾巴 `drgon_0206` |dot| = 0.99（伸展 3.24 m）；四条腿只有 0.10 ~ 0.49。\n" +
                 "尾巴绕容器 right 摆 ±25° 会让末端上下走 ±1.37 m ⇒「尾巴不在同一水平线上」（用户反馈）。")]
        public float limbTailAxisDot = 0.80f;

        // ── 第十八轮：头 / 尾 / 四肢的**姿态校正** ──
        //
        // ★★ 为什么需要这一组（`drg_rest` 实测，报告 Tools/reports/drg_rest.txt）：
        //   把 `swimWaveAmp = 0` 打进去 —— 曲线的 lat 恒 0 ⇒ `FromToRotation` 恒单位四元数
        //   ⇒ 脊骨回到**纯基准姿态**；再和正常驱动**同机位**逐项对照，两组数字**逐位相同**：
        //     尾尖绝对抬升 Δy = 0.265 m（中段累计 0.799 m）
        //     头扇（26 枚叶子骨）均值方向 vs 颈段方向 = 52.43°
        //     四条腿首段 dot(容器 fwd) = 0.490 / 0.283 / 0.099 / −0.351（前三条在**往前伸**）
        //   ⇒ 这一轮用户说的「头歪 / 尾巴翘 / 爪子该往后」**一条都不是波形造成的**，
        //     而是**模型基准姿态**自带的：`StraightenSpineBase()` 只把脊柱链本身拉直，
        //     头簇与尾巴是挂在脊柱节点上的**刚性分支**，从来没有被校正过。
        //
        //   ⇒ 上一轮"已修好"判错的**真正原因是判据口径**：量的是"分支末端 y 的**极差**"，
        //     刚性分支的极差恒 0 ⇒ 判 ✅，而它的**绝对值**一直翘着 0.265 m。
        //     「量波动」≠「量位置」。见坑表条目 24。
        [Tooltip("★ 头部俯仰对齐（度，**抬头为正**）。只转末尾 `headAlignLinks` 节 ⇒ 头簇整块绕颈根转，脖子不动。\n" +
                 "★ 0 = **模型原生基准姿态**（≠「吻部水平」）：该姿态下实测**吻部低 3.1°**。\n" +
                 "★ 换算（第二十一轮，锁索引尺子实测 6 档）：**吻部仰角 = 0.9637 × 值 − 3.09°**（残差 < 0.1°）。\n" +
                 "★★ 但「吻部水平」≠「看着不抬头」（第二十二轮，用户第二次反馈「改后还是抬头」）：\n" +
                 "   那把尺子量的是「颈根后 0.15 m → 吻端」的**弦**，而龙头的颅骨/角整块长在这条弦**上方** " +
                 "⇒ 弦水平时眼睛仍然读作「头抬着」。⇒ 尺子有固定的**朝上偏置**，" +
                 "只可用于**相对档位比较**；绝对档位一律以「十档扫描出图 + 真实世界水平线 + 人眼」为准（坑表 36）。\n" +
                 "★★ 第二十六轮更正（本条**作废**，见坑表条目 48）：以上档位全部是在**拉直之后**的基准上对照的，" +
                 "而拉直把颈根（头簇挂点 `drgon_025`）拧了 **93.37°** ⇒ 那些「档位」建立在歪基准上。" +
                 "现在 `headLockUseRestPose` 默认锁 **FBX 原生姿态**，实测（`drg_headfix`，同一物件同机位）：" +
                 "pitch=−12 时头仍偏 **12.00°**（= headExtra 的角度）；pitch=0 时偏 **0.00°**、" +
                 "俯仰/偏航/滚转三列与 FBX 基准帧逐个相同 ⇒ **现值改为 0**。\n" +
                 "★ 旧记录（仅供追溯）：−12 出自第二十二轮十档扫描；更早的 30 出自第十八轮「头簇 PCA 第一主轴俯仰 30.2°」" +
                 "—— 那把尺子量的是**颈+颅的质量走向**，不是吻部指向，属坑表条目 29。\n" +
                 "★★ 第二十四轮更正（此前这里写的「头不会随行波俯仰、一个静态值就能定死」是**错的**）：\n" +
                 "   那是**旧驱动**（`ApplySpineOffsetsRaw`，绕世界 up 的纯偏航）时期的结论。\n" +
                 "   换成蛇形驱动 `ApplySerpentineSpine` 后，写入用的是\n" +
                 "   `FromToRotation(倾斜的基准段方向, 水平切向)` —— **最小旋转**在切向左右摆时会带出俯仰，\n" +
                 "   头簇刚性挂在 `spine[n-2]` 上只能跟着甩：`drg_headnod` 实测一个行波周期内\n" +
                 "   **吻部仰角极差 49.65°、偏航极差 13.42°**（用户第三次反馈「头没面向前方 / 歪了」）。\n" +
                 "   ⇒ 这个静态值只能定**平均档位**，摆动幅度要动 `swimWaveHeadGain`（见该字段）。")]
        // ★★ 第二十八轮·终局（2026-09-18）：三轮自动反解全部宣告失败——
        //   ① 骨轴对齐（roll −65.46 组）→ 颅骨网格仍上仰；② 叶骨质心 → 须骨/下颌骨带偏，颅骨下折 40-50°；
        //   ③ 图像伺服（顶轮廓/PCA）→ 双视角目视"过关"后用户实测：「头完全反过来了」。
        //   ★ 根因共性：所有自动量测都被须骨、蒙皮顶点或视角欺骗，**机器看不见审美**。
        //   拍板：**人眼在回路**——三值归零（= FBX 原生姿态），由用户按 F9 呼出
        //   调参窗口（本文件底部 HeadTunerWindow）实时拧滑条定终值，然后把数值粘回来落盘。
        //   与 Z_Enemy_MoLong.prefab 保持一致（门禁 0 不一致）。
        public float headAlignPitchDeg = 0f;
        [Tooltip("头部偏航对齐（度，绕容器 up；正 = 转向身体左侧）")]
        public float headAlignYawDeg = 0f;
        [Tooltip("头部滚转对齐（度，绕身体轴）")]
        public float headAlignRollDeg = 0f;
        [Tooltip("头部对齐作用在脊柱末尾几节上。实测头簇挂在 `_spine[Count-2]`，" +
                 "所以取 2 才覆盖到它（`Count-1` 是无几何的链尾骨）。")]
        public int headAlignLinks = 2;

        [Tooltip("★★ 头部锁定基准姿态（第二十五轮）：末尾 `headAlignLinks` 节**不跟随行波**，" +
                 "直接采用基准朝向 ⇒ 移动时头纹丝不动地朝前（= 模型原生姿态 + 上面三条对齐角）。\n" +
                 "★ 为什么必须锁：`FromToRotation(倾斜基准段, 水平切向)` 是**最小旋转**，" +
                 "切向左右摆时会把偏航**耦合出俯仰** ⇒ 头簇刚性挂在 `spine[n-2]` 上只能跟着甩" +
                 "（`drg_headnod` 实测一个行波周期内吻部仰角极差 49.65°、偏航 13.42°）。\n" +
                 "★ 与 `swimWaveHeadGain = 0` 的区别：那个把**头端一大段**的波都抽掉（脖子变直杆），" +
                 "这个只锁末尾 `headAlignLinks` 节 ⇒ **脖子照常摆动**，头绕颈根稳住。\n" +
                 "★ 关掉则回到「头跟着曲线切向走」的旧行为。")]
        public bool headLockToBase = true;

        [Tooltip("★★ 头锁定用哪份基准（第二十六轮，坑表 48）：\n" +
                 "true（默认、推荐）= **FBX 原生基准姿态**（拉直之前采的 `_restRel`）\n" +
                 "   ⇒ 头的方向与位置回到模型师傅摆的那个样子（用户：「这个 FBX 精致姿态里面头的位置就是对的，" +
                 "跟脖子的相对位置是对的，方向也是」）。\n" +
                 "false = 旧行为：锁拉直**之后**的 `_baseRel`。\n" +
                 "★ 为什么旧行为必定歪：`StraightenSpineBase()` 会把 `_spine[Count-2]`（颈根 = 头簇挂点）" +
                 "也转过去，实测 `drgon_025` 被转 **93.37°**、段方向偏 **66.56°**；" +
                 "而头簇是**刚性分支、从不被驱动** ⇒ 头的世界朝向完全取决于这一节。" +
                 "锁拉直后的基准 = 把头**永久钉在一个被转歪了 93° 的姿态**上。")]
        public bool headLockUseRestPose = true;

        [Tooltip("★★ 头部「随颈」权重（第二十七轮，坑表 50）：0 = 容器系绝对锁（上一轮行为），1 = 完全随颈。\n" +
                 "背景（用户：「现在确实是面对到位置了，但是脖子动的时候，头一直保持一个方向没有旋转，要面向过来」）：\n" +
                 "  上一轮换基把头**方向**修对了，但写点是 `rotation = rootRot · headExtra · _restRel[i]`\n" +
                 "  —— 这是**相对容器**的绝对锁：容器一转头就跟着转（所以「面向前方」那条成立），\n" +
                 "  可**脖子自己摆（行波）时头一动不动** ⇒ 头成了钉在颈根上的装饰，读起来「是死的」。\n" +
                 "★ 实测量级（drg_headfollow27，按 67 帧 = 整周期采样，captureFramerate 已钉）：\n" +
                 "  w=0 时头载体容器系偏航极差 **0.00°**（真的没动）；\n" +
                 "  w=1 时改成 `R_i = R_{i−1} · headExtra · (inv(_restRel[i−1]) · _restRel[i])`\n" +
                 "  即「与**父节**保持 FBX 原生局部角」⇒ 父节在哪头就在哪，脖子摆头跟着摆。\n" +
                 "★ w=0 逐位等于旧行为（可溯源）；中间值是两者的球面插值，只调「头甩多大」。\n" +
                 "★ 两条驱动路径（`ApplySpineOffsetsRaw` / `ApplySerpentineSpine`）必须同权重，\n" +
                 "  否则攻击相位一进一出头会「啪」地弹一下。")]
        public float headNeckFollowWeight = 1f;

        [Tooltip("★ 尾巴水平化：把整条尾链的段方向**压回水平面**（只保留偏航摆动）。\n" +
                 "实测（drg_head）尾巴逐节 pitch 从 −6° 一路爬到 −33°，末端抬升 0.265 m、" +
                 "中段累计 0.799 m ⇒ 用户「尾巴末端是歪的，翘起来了，应该保持同一水平线的高度」")]
        public bool tailLevel = true;
        [Tooltip("★ 尾巴 = 身体 sin 波形的**延续**（第十九轮）：把身体曲线的参数往 u<0 外推当作尾链中心线 ⇒ " +
                 "波长 / 相位 / 传播方向 / 接点切向**按构造相等**，不需要任何额外约束。关掉则回退旧版独立行波。" +
                 "旧版实测两处对不上：① 空间相位符号相反 ⇒ 尾波传播方向与身体相反（两条波对着撞）；" +
                 "② 接点切向横向分量 π·bodyAmp 对 tailWaveAmp（≈3.61 vs 0.35，差 10 倍）⇒ 身体尾端在摆、尾根几乎不动")]
        public bool tailFollowBodyWave = true;
        [Tooltip("延续模式的幅度增益（1 = 与身体同幅）。> 1 会逼近折返上限 " +
                 "（判据 amp·gain·6.02/Σ骨长 < 1，本龙 Σ骨长 8.266 m）")]
        public float tailFollowGain = 0.9f;
        [Tooltip("（仅 tailFollowBodyWave = false 时生效）尾巴水平摆幅（m）：尾根 0 → 尾尖最大")]
        public float tailWaveAmp = 0.35f;
        [Tooltip("（仅 tailFollowBodyWave = false 时生效）尾巴上铺几个波（相对尾长）")]
        public float tailWaveSpan = 0.5f;
        [Tooltip("（仅 tailFollowBodyWave = false 时生效）尾巴波相对身体行波的相位滞后（度）")]
        public float tailPhaseLagDeg = 0f;

        [Tooltip("★ 四肢后掠（度）：飞行时爪子该**往后收**，而不是像趴地那样往前摊。\n" +
                 "按左右侧自动定符号（h.x 的符号），并限幅在此角度内 ⇒ 绝不会把腿甩过身体中线。\n" +
                 "实测四条腿首段 dot(容器 fwd) = 0.49 / 0.28 / 0.10 / −0.35 ⇒ 前三条在往前伸。")]
        public float limbSweepBackDeg = 60f;

        // ── 头部引导 ──
        [Tooltip("★ 头部引导偏航上限（度）：转弯时头先转、身体再跟，别让头被身体拖着走。\n" +
                 "★ 实测：18° 会**长期顶满**（头一直歪着，比不引导更难看），收敛到 8°。")]
        public float headLeadYawDeg = 8f;
        [Tooltip("转向速率 → 头部偏航 的增益。\n" +
                 "★ 实测教训：0.35 会**一直顶满 18°**，头长期歪着比不引导更难看。\n" +
                 "  0.08 时直线巡航接近 0、只在真转弯时才甩头。")]
        public float headLeadGain = 0.03f;

        // ── 表演段：复现龙自带飞行动画（`Take 001`, 57.5 s）的结构 ──
        //
        // ★ 为什么是"复现"而不是"播动画"：龙在本工程是**程序驱动**的
        //   （`ActionShowcase` 明确写了「BOSS 墨龙：单独特判（它是程序驱动，不走 Animator）」），
        //   整套动作由状态机算出来。所以自带动画只能**翻译成参数**接进来。
        //
        // ★ 参数怎么来的：把 `Take 001` 逐 0.5 s 采样、按 7.2 s 分段量「质心速度 + 体展」，
        //   实测结构是 ——
        //     0.0– 7.2s  均速 194   质心 y 49~719     ⇒ 静默/趴卧
        //     7.2–14.4s  均速 2197  质心 y −4202~2586 ⇒ 大幅上下游动前进（体展 594→7181）
        //    14.4–28.8s  均速 ~330                   ⇒ 静默（**连续两段**）
        //    28.8–43.1s  均速 ~680                   ⇒ 中速移动
        //    43.1–50.3s  均速 2637                   ⇒ 大俯冲
        //    50.3–57.5s  均速 1280                   ⇒ 拉起
        //   即**「移动段 ↔ 静默段」交替**，且静默段常连续出现。
        //   归一后总行程 ≈ 水平 11.4 m × 垂直 11.0 m / 57.5 s。
        public bool enablePerformanceCycle = true;
        [Tooltip("『上下游动前进』每段持续秒数（自带动画实测 ≈7.2 s）")]
        public float advanceDuration = 7f;
        [Tooltip("『趴下静默』每段持续秒数（自带动画实测 ≈7.2 s）")]
        public float restDuration = 7f;
        // ★ 下面三个「静默段」默认值必须与 Z_Enemy_MoLong.prefab 上的序列化值逐位一致。
        //   第十四轮改这组值时只改了 prefab、漏改这里 ⇒ 演示场（走 prefab）验收通过，
        //   但源码里仍留着「静默段把身体完全拉直 + 贴地 0.35 m」的旧行为，
        //   组件一旦被 Reset / prefab 重建回退就会复活。检查工具：Tools/check_prefab_overrides.py
        [Tooltip("静默段驻留高度（m）——贴地但不落地。prefab 同步为 4.5")]
        public float restLift = 4.5f;
        [Tooltip("静默段脊骨波 / 四肢 / 横向摆 / 头部引导的振幅缩放（0 = 完全静止）。prefab 同步为 0.5")]
        public float restMotionScale = 0.5f;
        [Tooltip("静默段的水平推进速度缩放（0 = 原地悬停）。prefab 同步为 0.45")]
        public float restSpeedScale = 0.45f;
        [Tooltip("上下游动：模型容器**竖直**正弦振幅（m）。与 bodySwayAmp 同构，那条是左右、这条是上下")]
        public float bodyBobAmp = 1.1f;
        [Tooltip("上下游动频率（Hz）")]
        public float bodyBobFreq = 0.22f;

        /// <summary>当前是否处于静默段（供自动化验收断言）。</summary>
        public bool IsResting => _perfResting;
        /// <summary>表演段相位名（供自动化验收断言）。</summary>
        public string PerformancePhaseName => !enablePerformanceCycle ? "-" : (_perfResting ? "Rest" : "Advance");

        private bool _perfResting;
        private float _perfTimer = -1f;      // <0 = 尚未初始化（从「移动段」起算）

        // ★ 原地悬停时强制满幅度：静默段会把波形压到 0.5 倍、把高度拉到 4.5 m，
        //   调形态时这两个都是干扰 —— 波形一会儿大一会儿小，读数没有意义。
        private float PerfMotionScale => hoverStationary ? 1f
            : (!enablePerformanceCycle ? 1f : (_perfResting ? restMotionScale : 1f));
        private float PerfSpeedScale => !enablePerformanceCycle ? 1f : (_perfResting ? restSpeedScale : 1f);
        private float PerfBobScale => !enablePerformanceCycle ? 0f : (_perfResting ? 0f : 1f);

        /// <summary>
        /// 推进「上下游动前进 ↔ 趴下静默」的相位计时。
        ///
        /// ★ 只在盘旋（常态）里调用：攻击段（俯冲/撕咬/扫尾/吐息）各有自己的高度曲线，
        ///   被静默段打断会出现"俯冲到一半突然贴地"。
        /// </summary>
        private void TickPerformanceCycle()
        {
            if (!enablePerformanceCycle) { _perfResting = false; _perfTimer = -1f; return; }
            if (_perfTimer < 0f) { _perfResting = false; _perfTimer = Mathf.Max(0.1f, advanceDuration); }
            _perfTimer -= Time.deltaTime;
            if (_perfTimer > 0f) return;
            _perfResting = !_perfResting;
            _perfTimer = Mathf.Max(0.1f, _perfResting ? restDuration : advanceDuration);
        }

        // ── 阶段演出：仰头长啸（策划案 §4「阶段切换必须有演出」）──
        //
        // ★ 规格原文（`Docs/墨龙BOSS策划案.md:196`）：
        //   「整条龙**仰头长啸**（脊骨波幅度 **×2.5**，持续 **1.2 s**），
        //     高度抬升 + 墨晕扩散 —— 让玩家明确知道"它变强了"。」
        //   高度抬升由 `UpdatePhase` 写 `_currentLift = PhaseHoverHeight` 完成（P1/P2/P3 = 3.6/4.4/5.2 m）。
        [Tooltip("长啸持续秒数（策划案规格 1.2 s）")]
        public float roarDuration = 1.2f;
        [Tooltip("长啸期间脊骨波幅度的倍率（策划案规格 ×2.5）")]
        public float roarSpineGain = 2.5f;
        [Tooltip("长啸期间头颈上仰角度（度）——「仰头」那一下")]
        public float roarHeadRaiseDeg = 30f;
        [Tooltip("长啸影响尾部起算的骨骼节数（从脊骨末梢往前数）")]
        public int roarHeadLinks = 4;

        private float _roarTimer;
        /// <summary>是否正在长啸（供自动化验收断言）。</summary>
        public bool IsRoaring => _roarTimer > 0f;
        /// <summary>已触发长啸次数（供自动化验收断言）。</summary>
        public int RoarCount { get; private set; }

        private float RoarSpineGain => IsRoaring ? roarSpineGain : 1f;
        /// <summary>1 → 0 的进度（1 = 刚起啸）。</summary>
        private float RoarProgress01 => roarDuration > 0f ? Mathf.Clamp01(_roarTimer / roarDuration) : 0f;

        /// <summary>起啸：计时 + 墨晕扩散 + 打断静默段（否则啸被 restMotionScale 压掉）。</summary>
        private void BeginRoar()
        {
            _roarTimer = Mathf.Max(0.05f, roarDuration);
            RoarCount++;

            // 静默段里幅度被压到 6%，长啸会被吃掉 ⇒ 起啸即切回移动段
            if (_perfResting) { _perfResting = false; _perfTimer = Mathf.Max(0.1f, advanceDuration); }

            // 墨晕扩散：在头部炸一发强墨（表现"它变强了"）
            if (_spine.Count > 0 && _spine[_spine.Count - 1] != null)
                InkWash.Effects.InkHitVfx.Spawn(_spine[_spine.Count - 1].position, Vector3.up, roarSpineGain);
        }

        /// <summary>长啸计时。盘旋与攻击两条路径都要推，否则出招期间啸不完。</summary>
        private void TickRoar()
        {
            if (_roarTimer > 0f) _roarTimer -= Time.deltaTime;
        }

        /// <summary>
        /// 长啸期间给头颈叠一个上仰 —— 规格里的「仰头」。
        /// 与 `DoBreathHover` 同一套写法（绕**容器 right**，见 §3 的轴向教训）。
        /// 用 sin 包络让"仰起 → 回正"平滑，不硬切。
        /// </summary>
        private void ApplyRoarHeadRaise()
        {
            if (!IsRoaring || _spine.Count == 0) return;
            float u = Mathf.Sin(Mathf.PI * (1f - RoarProgress01));       // 0 → 1 → 0
            float pitch = -roarHeadRaiseDeg * u;                          // 负值 = 上仰
            Quaternion rootRot = _modelRoot != null ? _modelRoot.rotation : transform.rotation;
            int start = Mathf.Max(0, _spine.Count - roarHeadLinks);
            for (int i = start; i < _spine.Count; i++)
            {
                if (_spine[i] == null || i >= _baseRel.Count) continue;
                int k = i - start;
                _spine[i].rotation = rootRot
                    * Quaternion.AngleAxis(pitch * (1f + 0.2f * k), Vector3.right)
                    * _baseRel[i];
            }
        }

        private readonly List<Transform> _limbRoots = new List<Transform>();
        private readonly List<int> _limbParentIndex = new List<int>();
        private readonly List<Quaternion> _limbBaseRel = new List<Quaternion>();
        /// <summary>
        /// `_limbRoots[k]` 是否参与划水。分类规则见 `ResolveLimbs`（第十七轮加）。
        /// ★ 为什么必须有这张表：`_limbRoots` 是**几何收集**的产物，不是"四条腿"的语义列表 ——
        ///   实测 19 条里只有 4 条是腿，另有整条尾巴（3.24 m）和 14 条头饰簇。
        /// </summary>
        private readonly List<bool> _limbDriven = new List<bool>();
        /// <summary>`_limbRoots[k]` 首子方向与容器前向(+Z)的 |dot| —— 诊断用，判定"沿身体轴 ⇒ 尾巴"的依据。</summary>
        private readonly List<float> _limbAxisDot = new List<float>();
        /// <summary>`_limbDriven` 里为 true 的条数（探针回读用；0 表示分类过严）。</summary>
        private int _limbDrivenCount;
        /// <summary>`_limbRoots[k]` 首子方向在**容器局部系**里的基准值（第十八轮加）。
        /// 运行时据此算「这条腿要绕容器 up 转多少度才算往后」，与 `limbSweepBackDeg` 实时联动。</summary>
        private readonly List<Vector3> _limbBaseDirLocal = new List<Vector3>();

        // ── 尾巴链（第十八轮：尾巴不再是"刚性挂在脊柱[0] 上的分支"，而是可驱动的一条链）──
        private readonly List<Transform> _tailChain = new List<Transform>();
        private readonly List<Quaternion> _tailBaseRel = new List<Quaternion>();
        /// <summary>尾巴链的**基准局部旋转**（父节点系）—— `EndHover()` 复位用。</summary>
        private readonly List<Quaternion> _tailBaseLocalRot = new List<Quaternion>();
        private readonly List<Vector3> _tailBaseSegLocal = new List<Vector3>();
        private readonly List<float> _tailSegLen = new List<float>();
        private float _tailTotalLen;
        private Vector3[] _tailTan;
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
        [Tooltip("链尾（_spine[Count-1] = 头侧）的摆幅增益。头要稳，所以比尾小。\n" +
                 "★ 旧值 0.30 配 16° 振幅 ⇒ 头部只弯 4.8°，等于头基本不动、身体在它后面自己扭。\n" +
                 "  用户要「头也摆，但比尾小」⇒ 0.55（头约 19°、尾约 45°，差 2.4 倍）。")]
        public float waveAmpHeadGain = 0.55f;

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
        public float swimTurnRate = 1.5f;
        [Tooltip("半径偏差 → 向内/向外转向的增益（把龙拉回 hoverOrbitRadius）")]
        public float swimRadiusGain = 0.12f;
        [Tooltip("轨道角速度（°/s）。线速度 ≈ hoverOrbitRadius × 此值 × π/180")]
        public float orbitAngularSpeedDeg = 40f;
        [Tooltip("（已废弃，不再读取）旧实现把蛇行加在「进出」分量上 ⇒ 轨迹是花瓣形而非 S 形。仅为序列化兼容保留，默认值与 prefab 同为 0")]
        public float orbitLateralAmp = 0f;
        [Tooltip("（已废弃，不再读取）")]
        public float orbitLateralWaves = 3f;
        [Tooltip("（已废弃，不再读取）★ 摆航向属于「整体的移动贴合 sin」，用户已否掉 —— 蛇形现在由**身体形状**承担（见 swimWave* 系列）。仅为序列化兼容保留")]
        public float pathWaveAmpDeg = 0f;
        [Tooltip("（已废弃，不再读取）")]
        public float pathWaveCount = 3f;

        // ================= ★★ 蛇形游动：让**骨骼**排布成 sin 曲线 =================
        // 用户原话：「要让他的身体里面的骨骼贴合 sin，而不是整体的移动贴合 sin」。
        //
        // 旧做法是「给每节一个绕容器 up 的偏转角 yaw_i = A·sin(ph_i)」，两个根本毛病：
        //   ① 身体形状 = 偏转角的**积分** ⇒ 实际形状是 cos（与设定相位差 90°），
        //      相邻节只有 22.5° 相位差时积分还额外衰减 ⇒ 振幅读不出来；
        //   ② 它控制的是"每节朝哪"，位置靠父子链累积 ⇒ 形状畸变，无法保证是 sin。
        // 现在改成"先算曲线上的目标点、再让每节指向下一个目标点"（见 ApplySerpentineSpine）。
        [Tooltip("侧向摆幅（m）—— 链相对轴线的最大横向偏移（单侧）。真蛇 ≈ 身长 10~15%")]
        public float swimWaveAmp = 1.15f;
        [Tooltip("★ 身长内的完整波数。1.0 = 尾→头正好一个完整波（一个正弯 + 一个负弯 = 一个 S）。" +
                 "真蛇游动的波长 ≈ 1~2 倍身长 ⇒ W ∈ [0.5, 1.0]。越大越像波浪，越小越像一个弓")]
        public float swimWaveCount = 1.0f;
        [Tooltip("波沿身体传播的频率（Hz）—— 越大 = 像蛇推水越快")]
        public float swimWaveFreq = 0.45f;
        [Tooltip("★ 尾端包络增益（乘在 sin(πu) 上的线性渐变）。1.15 = 尾部略大于中段（真蛇尾巴最灵活）")]
        public float swimWaveTailGain = 1.15f;
        [Tooltip("★ 头端包络增益。1.0 = 与中段同幅。\n" +
                 "★★ 第二十四轮：这一格是**「移动时龙头乱甩」的总开关**，用户可直接在 Inspector 调。\n" +
                 "   机制：env(u) = sin(πu)·Lerp(tg, hg, u) ⇒ env'(1) = −π·hg。\n" +
                 "   hg > 0 ⇒ 头端切向随行波相位摆动 ⇒ 经 `FromToRotation(倾斜基准段, 切向)` 的\n" +
                 "   最小旋转放大成**仰角**摆动（不是纯偏航，见 headAlignPitchDeg 的注解）。\n" +
                 "   hg = 0 ⇒ env'(1) = 0 ⇒ 头端切向恒为轴向、**与相位无关** ⇒ 头不动。\n" +
                 "   实测（drg_headnod，hoverStationary + 三相位 + 吻部轴几何量）：\n" +
                 "     hg 1.00 → 仰角极差 **49.65°**、偏航极差 13.42°（现值，明显「歪 + 点头」）\n" +
                 "     hg 0.50 → 仰角极差 48.12°、偏航极差 21.81°（**更差**，别往中间调）\n" +
                 "     hg 0.00 → 仰角极差 **0.55°**、偏航极差 **0.74°**（≈ 完全定住）\n" +
                 "   ★ 代价：头端的身子摆幅随之减小（波在接近头处收敛成直杆）。\n" +
                 "     这是「头朝向稳定」与「头端随波」的取舍，属审美，由用户拍板。\n" +
                 "   ★ hg = 0 时吻部仍有约 −7.4° 的**静态**下俯 ⇒ 再用 `headAlignPitchDeg` 抬回来\n" +
                 "     （该尺子对值的斜率实测 ≈ 0.71 °/°，即想抬 7° 就把值往正向加约 10）。")]
        public float swimWaveHeadGain = 1.0f;
        [Tooltip("（已废弃，不再读取）旧包络指数 env = sin(πu)^n —— 两端归零 ⇒ 头和尾都不动。" +
                 "保留仅为序列化兼容，默认值与 prefab 同为 3")]
        public float swimWaveEnvPow = 3f;
        [Tooltip("★★ 原地悬停（**调形态专用**）：打开时龙不前进、不转向、不改朝向、高度恒定，" +
                 "并忽略静默段的幅度缩放 —— 把身体波形单独择出来看。" +
                 "用户要求「龙原地不动去做动作，先把这个动作做好了再动」⇒ 调蛇形期间保持开启。")]
        public bool hoverStationary = false;
        [Tooltip("原地悬停时，航线轴线的朝向（度，绕世界 Y）。0 = 用 prefab 摆好的朝向；" +
                 "90 = 原地右转 90°（方便从侧后方看清左右摆动）")]
        public float hoverStationaryYawDeg = 0f;
        private float _orbitPhase;
        private Vector3 _swimDir = Vector3.forward;
        /// <summary>原地悬停：进入盘旋那一刻的朝向基准 + 是否已初始化。</summary>
        private bool _stationaryInit;
        private Quaternion _stationaryBaseRot = Quaternion.identity;
        [Tooltip("俯冲期水平移动速度倍率（× circleMoveSpeed）")]
        public float diveSpeedMul = 2.6f;
        [Tooltip("I4：两次俯冲之间的最小间隔（秒）。低于 1.2 s 玩家没有喘息窗口")]
        public float diveMinInterval = 1.2f;

        [Header("★ 三阶段（策划案 §4）")]
        [Tooltip("P2 / P3 的血量阈值")]
        public float phase2AtRatio = 0.65f;
        public float phase3AtRatio = 0.30f;
        [Tooltip("各阶段的盘旋高度（m）")]
        public float hoverHeightP1 = 7.0f;
        public float hoverHeightP2 = 8.0f;
        public float hoverHeightP3 = 9.0f;
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

        /// <summary>
        /// ★★ 第二十六轮（坑表 48）：**拉直之前**的「FBX 原生基准姿态」，表达为模型容器局部系。
        ///
        /// 为什么必须单独存一份：`StraightenSpineBase()` 是**全局**姿态改写 ——
        ///   它把整条脊骨（含 `_spine[Count-2]` = 颈根 = 头簇挂点）逐节转成一条直线。
        ///   实测（`drg_headref`）：`_spine[22] = drgon_025` 被转了 **93.37°**、段方向偏 **66.56°**
        ///   （正是基准姿态里那个 65.6° 的颈折）。而头簇（39 枚叶子骨 + 14 条鬃/须/角/颌）
        ///   是**刚性分支、从不被驱动** ⇒ 头的世界朝向 **完全等于** `_spine[Count-2].rotation`
        ///   × 常数 ⇒ **拉直那一步就把头整块甩歪了**。
        ///
        /// 于是「头永远锁定在拉直后的姿态」= 锁定在一个歪姿态上 ⇒
        /// 用户看到的「不锁 / 锁 pitch=0 / 锁 pitch=−12 **三档全是歪的**」。
        ///
        /// ⇒ 头锁定要用的是**这一份**（FBX 原生），不是 `_baseRel`（拉直后）。
        ///   蛇形驱动仍必须用拉直后的 `_baseRel` / `_baseSegLocal`：那是「一条蛇」的前提。
        /// 长度 = `_spine.Count`。
        /// </summary>
        private readonly List<Quaternion> _restRel = new List<Quaternion>();

        // ★★ 蛇形游动（位置级 sin 贴合）用的三张表 —— 全部在基准姿态下采一次，之后只读。
        //
        // `_baseSegLocal[i]`：第 i 节 → 第 i+1 节的**段方向**，表达为模型容器局部系。
        //   为什么不能直接当作 `Vector3.forward`：那只有在 `straightenSpine = true`
        //   （拉直基准）时才成立。存成实测值就与"基准是不是直线"解耦，两种配置都对。
        //   长度 = `_spine.Count`（末节沿用前一个 —— 末节没有"下一节"，
        //   但它的旋转仍决定**头骨的朝向**，必须一起驱动）。
        private readonly List<Vector3> _baseSegLocal = new List<Vector3>();
        // `_segLen[i]`：第 i 节 → 第 i+1 节的**骨长**（世界单位）。刚性骨骼 ⇒ 恒定。
        //   长度 = `_spine.Count - 1`。
        private readonly List<float> _segLen = new List<float>();
        // 每帧复用的切向缓存（**不复用就会每帧 new 数组 ⇒ 稳态产生 GC**，项目有"0 B GC"要求）
        private Vector3[] _serpTan;

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

        /// <summary>销毁时显式关掉风暴特效（双保险：表现层 Update 里还有假空归正的兜底）。</summary>
        protected virtual void OnDestroy()
        {
            InkWash.Effects.DragonStormVfx.Shut();
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
            _restRel.Clear();

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

            // ★★ 第二十六轮（坑表 48）：**必须在拉直之前**采「FBX 原生基准姿态」。
            //   拉直会把颈根（头簇挂点）也转过去（实测 93.37°）⇒ 头被整块甩歪，
            //   而头锁定要的正是「用户认可的那个 FBX 姿态」。**顺序不能反**。
            CaptureRestRel();
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
            // 同一时刻采「容器空间段方向 + 骨长」——蛇形驱动要按骨长在曲线上推进，
            // 必须与 `_baseRel` 出自**同一次**基准姿态，否则两者描述的姿势不是同一条链。
            CaptureSegments();
        }

        /// <summary>
        /// 把**拉直之前**的脊骨姿态记成相对模型容器的 `_restRel` —— 即「FBX 原生基准姿态」。
        /// ★ 必须在 `StraightenSpineBase()` **之前**调用；只给「头锁定」用，不参与蛇形驱动。
        /// </summary>
        private void CaptureRestRel()
        {
            Quaternion rootRot = _modelRoot != null ? _modelRoot.rotation : transform.rotation;
            _restRel.Clear();
            for (int i = 0; i < _spine.Count; i++)
            {
                if (_spine[i] == null) { _restRel.Add(Quaternion.identity); continue; }
                _restRel.Add(Quaternion.Inverse(rootRot) * _spine[i].rotation);
            }
        }

        /// <summary>
        /// 采「容器空间段方向」与「逐节骨长」。基准姿态下随 `CaptureBaseRel()` 一起采一次。
        ///
        /// ★ 段方向必须存成**容器局部系**（和 `_baseRel` 同一参考系）：
        ///   驱动时要算 `FromToRotation(基准段方向, 曲线切向)`，
        ///   两个向量只有在同一参考系里才有意义。
        ///
        /// ★ 为什么不用 `Vector3.forward` 当基准段方向（那样能少存一张表）：
        ///   只有当 `straightenSpine = true`（基准被拉直成一条 +Z 直线）时它才成立。
        ///   实测值表与"基准是不是直线"解耦 —— 关掉拉直也照样对。
        ///
        /// ★ 末节沿用前一节：`_spine[Count-1]` 没有"下一节"，但它的旋转仍决定**头骨朝向**，
        ///   不驱动它头部就会僵在基准姿态。表长取 `_spine.Count`，与 `_baseRel` 对齐。
        /// </summary>
        private void CaptureSegments()
        {
            _baseSegLocal.Clear();
            _segLen.Clear();
            if (_spine.Count < 2) return;

            Quaternion rootRot = _modelRoot != null ? _modelRoot.rotation : transform.rotation;
            Quaternion invRoot = Quaternion.Inverse(rootRot);

            for (int i = 0; i + 1 < _spine.Count; i++)
            {
                if (_spine[i] == null || _spine[i + 1] == null)
                {
                    _baseSegLocal.Add(Vector3.forward);
                    _segLen.Add(0f);
                    continue;
                }
                Vector3 d = _spine[i + 1].position - _spine[i].position;
                _segLen.Add(d.magnitude);
                _baseSegLocal.Add(d.sqrMagnitude > 1e-12f ? invRoot * d.normalized : Vector3.forward);
            }
            if (_baseSegLocal.Count > 0) _baseSegLocal.Add(_baseSegLocal[_baseSegLocal.Count - 1]);
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
        /// 收集**脊柱之外**的分支骨，并**筛出真正的腿**。
        ///
        /// 为什么必须单独收集：`ResolveSpine()` 每层只取「第一个非 SMR 子节点」，
        /// 采到的是纯脊柱单链；四肢是**旁挂的分支**，旧代码从来没碰过它们 ⇒ 龙游动时爪子纹丝不动。
        /// 判据用「后代数量 ≥ limbMinDescendants」：背鳍那种单骨会被滤掉。
        ///
        /// ★★ 但「分支骨 = 四肢」这个假设是错的（第十七轮实测更正，见字段区）：
        ///   几何收集会连带把 **整条尾巴**（`drgon_0206`，3.24 m，沿身体轴）和
        ///   **14 条头饰簇**（父节点 `脊柱[22]`）一起收进来。它们一旦被当腿驱动：
        ///     · 尾巴绕横轴上下翻 ⇒ 末端上下走 ±1.37 m（用户："尾巴不在同一水平线上"）
        ///     · 头饰乱翻 ⇒ 头顶炸毛（用户："头歪了、跟落枕一样"）
        ///   所以这里再加**两条语义筛选**，两条都是尺度无关的：
        ///     ① `i >= 脊柱.Count - limbHeadExcludeLinks` ⇒ 头部若干节上的分支不驱动（头饰簇）；
        ///     ② 首子方向与容器 +Z 的 |dot| ≥ `limbTailAxisDot` ⇒ 沿身体轴延伸 ⇒ 是尾巴，不驱动。
        /// </summary>
        private void ResolveLimbs()
        {
            _limbRoots.Clear();
            _limbParentIndex.Clear();
            _limbBaseRel.Clear();
            _limbDriven.Clear();
            _limbAxisDot.Clear();
            _limbDrivenCount = 0;
            if (_spine.Count == 0) return;

            var inSpine = new HashSet<Transform>();
            for (int i = 0; i < _spine.Count; i++) if (_spine[i] != null) inSpine.Add(_spine[i]);

            Quaternion rootRot = _modelRoot != null ? _modelRoot.rotation : transform.rotation;
            Quaternion invRoot = Quaternion.Inverse(rootRot);
            int headStart = Mathf.Max(0, _spine.Count - Mathf.Max(0, limbHeadExcludeLinks));

            for (int i = 0; i < _spine.Count; i++)
            {
                var t = _spine[i];
                if (t == null) continue;
                for (int k = 0; k < t.childCount; k++)
                {
                    var c = t.GetChild(k);
                    if (c == null || inSpine.Contains(c)) continue;
                    if (CountDescendants(c) < limbMinDescendants) continue;

                    // ① 沿身体轴？—— 在**容器局部系**里身体轴就是 +Z，所以只需看方向的 z 分量。
                    Vector3 dirC = Vector3.zero;
                    if (c.childCount > 0) dirC = invRoot * (c.GetChild(0).position - c.position);
                    if (dirC.sqrMagnitude > 1e-12f) dirC.Normalize();
                    float axisDot = Mathf.Abs(dirC.z);
                    bool alongBody = axisDot >= limbTailAxisDot;

                    // ② 挂在头端若干节上？—— 头饰簇
                    bool headSide = i >= headStart;

                    _limbRoots.Add(c);
                    _limbParentIndex.Add(i);
                    _limbDriven.Add(!alongBody && !headSide);
                    _limbAxisDot.Add(axisDot);
                    if (!alongBody && !headSide) _limbDrivenCount++;
                }
            }

            // ★ 四肢的基准存 **localRotation**（相对父脊柱节点），不像脊柱那样存容器系世界旋转。
            //
            //   【更正记录 —— 这里曾经写过一段错误的理由，留着以防复发】
            //   我一度用「四肢末端在父节点局部系的位移极差」当判据，量出 930 m / 84 m，
            //   据此断言"四肢被父节点反向甩、疯狂乱甩"，并写了下面这版"修复"。
            //   **那个判据是错的**：`host.InverseTransformPoint(p)` 会把结果**除掉父节点的 scale**，
            //   而骨架根节点带缩放（模型单位→米，常见 0.01 量级）⇒ 真实 0.5 m 的摆动被放大成 ~84 m。
            //   改用**与 scale 无关的角度**复测：`Quaternion.Angle(base, current)`
            //   ⇒ 四肢相对父节点的最大姿态变化只有 **11.92° ~ 14.91°**（limbSwingDeg=9 时上限 2×9=18°），
            //   即四肢一直是"跟随父节点 + 叠自己的小摆动"，**根本没有乱甩**。
            //
            //   ⇒ 量骨骼运动**不要用带 scale 的局部坐标当位移判据**，用角度，或先归一化 scale。
            //   局部旋转这版仍然保留：它让四肢**跟着脊柱的起伏一起动**，比世界旋转更自然。
            for (int k = 0; k < _limbRoots.Count; k++)
                _limbBaseRel.Add(_limbRoots[k].localRotation);

            // ★ 第十八轮：后掠角要按"这条腿在容器水平面里朝哪"来算，所以把基准首子方向也存一份
            _limbBaseDirLocal.Clear();
            for (int k = 0; k < _limbRoots.Count; k++)
            {
                var b = _limbRoots[k];
                Vector3 d = Vector3.zero;
                if (b != null && b.childCount > 0) d = invRoot * (b.GetChild(0).position - b.position);
                if (d.sqrMagnitude > 1e-12f) d.Normalize();
                _limbBaseDirLocal.Add(d);
            }

            // ★ 第十八轮：把整条尾巴采出来（它从"刚性分支"升级为可驱动的链，见 ApplyTailLevel）
            CaptureTailChain(invRoot);
        }

        /// <summary>
        /// 采集**整条尾巴**（第十八轮）。
        ///
        /// ★ 为什么不再让尾巴当"刚性分支"：`drg_head` 实测尾巴逐节 pitch 从 −6° 爬到 −33°
        ///   （**基准姿态**自带，amp=0 时一样），末端绝对抬升 0.265 m、中段累计 0.799 m
        ///   ⇒ 用户「尾巴末端翘起来了，应该保持同一水平线的高度」。
        ///   冻成刚性只解决了"不单独摆动"，没解决"翘"。要压平就必须**逐节重写段方向**。
        ///
        /// ★ 选尾巴的判据：非驱动（`_limbDriven == false`）且 |首子方向 · 容器 +Z| ≥ `limbTailAxisDot`，
        ///   再取**伸展最长**的一条 —— 实测唯一满足的是 `drgon_0206`（伸展 3.24 m / 28 骨）。
        ///   头饰簇里也有两条 |z| ≥ 0.8 的（`drgon_061` / `drgon_0138`，伸展 0.24 / 0.69 m），
        ///   用"伸展最长"把它们排除掉。
        ///
        /// ★ 主轴链怎么挑：每层选「与**上一段方向**夹角最小」的子节点。
        ///   不选"离根最远" —— 尾巴中段会分出鳍，离根最远的往往是一条横向短鳍，
        ///   那样会把主轴拐上歧路（链会突然拐 90°）。
        /// </summary>
        private void CaptureTailChain(Quaternion invRoot)
        {
            _tailChain.Clear();
            _tailBaseRel.Clear();
            _tailBaseLocalRot.Clear();
            _tailBaseSegLocal.Clear();
            _tailSegLen.Clear();
            _tailTotalLen = 0f;

            Transform best = null; float bestReach = -1f;
            for (int k = 0; k < _limbRoots.Count && k < _limbDriven.Count; k++)
            {
                if (_limbDriven[k]) continue;
                if (k >= _limbAxisDot.Count || _limbAxisDot[k] < limbTailAxisDot) continue;
                var b = _limbRoots[k];
                if (b == null) continue;
                float reach = 0f;
                var stack = new Stack<Transform>();
                stack.Push(b);
                while (stack.Count > 0)
                {
                    var t = stack.Pop();
                    reach = Mathf.Max(reach, (t.position - b.position).magnitude);
                    for (int c = 0; c < t.childCount; c++) stack.Push(t.GetChild(c));
                }
                if (reach > bestReach) { bestReach = reach; best = b; }
            }
            if (best == null) return;

            var cur = best;
            _tailChain.Add(cur);
            Vector3 prevDir = cur.childCount > 0 ? (cur.GetChild(0).position - cur.position).normalized : Vector3.back;
            while (cur != null && cur.childCount > 0 && _tailChain.Count < 64)
            {
                Transform next = null; float bestDot = -2f;
                for (int c = 0; c < cur.childCount; c++)
                {
                    var ch = cur.GetChild(c);
                    Vector3 d = ch.position - cur.position;
                    if (d.sqrMagnitude < 1e-12f) continue;
                    float dot = Vector3.Dot(d.normalized, prevDir);
                    if (dot > bestDot) { bestDot = dot; next = ch; }
                }
                if (next == null) break;
                prevDir = (next.position - cur.position).normalized;
                _tailChain.Add(next);
                cur = next;
            }

            Quaternion rootRot = _modelRoot != null ? _modelRoot.rotation : transform.rotation;
            for (int i = 0; i < _tailChain.Count; i++)
            {
                var t = _tailChain[i];
                _tailBaseRel.Add(Quaternion.Inverse(rootRot) * t.rotation);
                _tailBaseLocalRot.Add(t.localRotation);
                if (i + 1 < _tailChain.Count)
                {
                    Vector3 d = _tailChain[i + 1].position - t.position;
                    _tailSegLen.Add(d.magnitude);
                    _tailBaseSegLocal.Add(d.sqrMagnitude > 1e-12f ? invRoot * d.normalized : Vector3.back);
                    _tailTotalLen += d.magnitude;
                }
            }
            // 末节沿用前一节（它没有"下一节"，但朝向仍要驱动）——与 CaptureSegments 同一口径
            if (_tailBaseSegLocal.Count > 0) _tailBaseSegLocal.Add(_tailBaseSegLocal[_tailBaseSegLocal.Count - 1]);
        }

        private static int CountDescendants(Transform t)
        {
            if (t == null) return 0;
            int n = 0;
            for (int i = 0; i < t.childCount; i++) n += 1 + CountDescendants(t.GetChild(i));
            return n;
        }

        /// <summary>
        /// 四肢划水：绕**容器 right**（世界侧向）前后划，相位跟随主行波、各肢依次错开。
        /// ★ 绝不能用骨骼自己的局部 `Vector3.right`：每条腿的局部轴朝向都不一样，
        ///   同一个角套上去，有的腿在前后划、有的其实在绕轴自转 ⇒ 读起来就是爪子没动。
        /// ★★ 只驱动 `_limbDriven[k]` 为 true 的分支 —— 尾巴与头饰簇必须保持**刚性**，
        ///   它们跟着父脊柱节点走就够了（这样尾巴自然躺在身体延长的水平线上，
        ///   头饰自然跟着头，不会自顾自地翻）。
        /// </summary>
        private void ApplyLimbMotion(float phase)
        {
            if (_limbRoots.Count == 0 || limbSwingDeg <= 0f) return;

            Quaternion rootRot = _modelRoot != null ? _modelRoot.rotation : transform.rotation;
            float step = limbPhaseStepDeg * Mathf.Deg2Rad;

            for (int k = 0; k < _limbRoots.Count && k < _limbBaseRel.Count; k++)
            {
                // ★ 非腿分支（整条尾巴 / 14 条头饰簇）不划水，见 ResolveLimbs 的筛选规则
                if (k < _limbDriven.Count && !_limbDriven[k]) continue;

                var b = _limbRoots[k];
                if (b == null) continue;

                // ★ 相位**跟随主行波**（用传进来的 phase，不再自己用 Time.time 起一个 0.27 Hz 的拍子）：
                //   四肢变成"身体波扫到它时才划一下"，而不是自顾自地摆。
                float ph = phase + (_limbParentIndex[k] + k) * step + (k % 2 == 0 ? 0f : Mathf.PI);
                float swing = limbSwingDeg * PerfMotionScale * Mathf.Sin(ph);

                // ★★ 轴换算到**父节点空间**（localRotation 的定义域）再左乘到基准上：
                //   世界侧向 → 父空间，然后绝对写入（无累积）。父节点转、腿跟着转，同时叠自己的划水角。
                Vector3 axisUp = Vector3.up, axisRight = Vector3.right;
                Transform par = b.parent;
                if (par != null)
                {
                    axisUp = par.InverseTransformDirection(rootRot * Vector3.up);
                    axisRight = par.InverseTransformDirection(rootRot * Vector3.right);
                }

                // ★★ 后掠（第十八轮）：飞行时爪子该**往后收**，不是像趴地那样往前摊。
                //   实测四条腿基准首段 dot(容器 fwd) = 0.49 / 0.28 / 0.10 / −0.35 —— 前三条在往前伸。
                //   算法：把首段投影到容器水平面，朝"侧向 + 向后"的目标方向转，**限幅**在
                //   `limbSweepBackDeg` 以内。限幅保证腿绝不会被甩过身体中线
                //   （裸符号旋转会：`drgon_0175` 的侧向分量只有 0.34，一转就把 x 翻号）。
                float sweep = 0f;
                if (limbSweepBackDeg > 0f && k < _limbBaseDirLocal.Count)
                {
                    Vector3 h = _limbBaseDirLocal[k];
                    h.y = 0f;
                    if (h.sqrMagnitude > 1e-6f)
                    {
                        h.Normalize();
                        var tgt = new Vector3(Mathf.Sign(h.x) * 0.70711f, 0f, -0.70711f);
                        sweep = Mathf.Clamp(Vector3.SignedAngle(h, tgt, Vector3.up),
                                            -limbSweepBackDeg, limbSweepBackDeg);
                    }
                }

                b.localRotation = Quaternion.AngleAxis(sweep, axisUp)
                                * Quaternion.AngleAxis(swing, axisRight)
                                * _limbBaseRel[k];
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
            TickPerformanceCycle();
            TickRoar();
            // 移动段回到相位巡航高度；静默段贴地驻留（对应自带动画的"趴卧"）
            // ★ 原地悬停时高度恒定：静默段会把龙从 7 m 拉到 4.5 m，调形态时是纯干扰。
            _currentLift = (hoverStationary || !_perfResting) ? PhaseHoverHeight : restLift;

            // ★★ 原地悬停（调形态模式）：朝向锁定、位置一动不动。
            //   用户要求「龙原地不动去做动作，先把这个动作做好了再动」。
            //   朝向用**进入盘旋那一刻**的朝向做基准，再叠 `hoverStationaryYawDeg`
            //   ⇒ 改取景角度不用动 prefab，也不用让龙自己转。
            if (hoverStationary)
            {
                if (!_stationaryInit)
                {
                    _stationaryInit = true;
                    _stationaryBaseRot = transform.rotation;
                }
                transform.rotation = Quaternion.Euler(0f, hoverStationaryYawDeg, 0f) * _stationaryBaseRot;
            }
            else _stationaryInit = false;

            if (PlayerRef.Exists && !hoverStationary)
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
                //
                // ★★ 旧写法：`tangent + radial * (orbitLateralAmp * sin(...) * 0.12f + err * gain)`
                //   有两个叠加的问题，实测轨迹接近一个圆（用户："这个移动没有做"）：
                //   ① 蛇行加在 **radial（离玩家的远近）** 分量上 ⇒ 走出来的是"忽远忽近的
                //      花瓣形"，不是"左右蜿蜒的 S 形"；
                //   ② 那个 `* 0.12f` 把 2.6 m 的标称摆幅压成 ±0.31 的方向偏置，
                //      而同一括号里 err 修正量级相近 ⇒ 蛇行被半径修正吃掉，肉眼看不见。
                //   ⇒ 现在改成**把航向绕竖直轴摆动**：摆的是"朝哪游"，轨迹才是蛇形。
                Vector3 baseDir = (tangent + radial * (err * swimRadiusGain * pull)).normalized;
                // ★★ 不再把航线**左右摆**（原 `pathWaveAmpDeg · sin(_orbitPhase · pathWaveCount)`）。
                //   用户已否掉这条路：「要让他的身体里面的骨骼贴合 sin，**而不是整体的移动贴合 sin**」。
                //   摆航向 = 让质心走 S 形轨迹，身体依旧是根直杆 ⇒ 正是"整体移动贴合 sin"。
                //   现在蛇形完全由**身体形状**承担（见 ApplySerpentineSpine），
                //   航线只负责"沿大弧线绕着玩家巡游"这一件事，越平顺越好。
                Vector3 wantDir = baseDir;

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
                // 静默段把推进速度压下来（复现自带动画"趴下不动"）
                MoveHorizontal(_swimDir * (swimSpeed * PerfSpeedScale) * Time.deltaTime);
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
            // 蛇形游动：把整条脊骨**排布到一条 sin 中心线上**（位置级贴合）。
            //
            // ★ 这一行替换掉了旧的 `ApplySpineOffsetsRaw(hoverAmplitudeDeg, hoverPitchAmplitudeDeg, ...)`。
            //   旧写法给的是"每节绕容器 up 的偏转角"，身体形状是它的**积分** ⇒ 实际是 cos、
            //   且相邻节相位差不够大时还会衰减。见 ApplySerpentineSpine 顶部的完整对照。
            // ★ 传的是**相位**（不是 sin 之后的值）：旧代码曾把 `sin(2πft)` 的结果当振幅传进来，
            //   驱动内部又乘一次 sin ⇒ 两个正弦相乘，振幅被压扁。
            float phase = 2f * Mathf.PI * swimWaveFreq * Time.time;
            // 静默段把波形压小（对应自带动画的"趴卧"段）；长啸期间脊骨幅度 ×2.5（策划案规格）
            float perf = PerfMotionScale * RoarSpineGain;
            ApplySerpentineSpine(perf, phase);

            // 「仰头」那一下叠在波形之上
            ApplyRoarHeadRaise();

            // ★ 不再调用 ApplyHeadSteer()：它是**事后左乘**（`add * _spine[i].rotation`），
            //   会把刚写好的 sin 形状揉歪 —— 与本轮"骨骼精确贴合 sin"的目标直接冲突。
            //   而"头领着走"的观感已由曲线本身给出：中心线的轴向就是容器前向，
            //   容器转向时头端切向跟着转（头端 env→0 ⇒ 头沿轴线，天然先转）。
            _headYaw = 0f;
            _prevSwimDir = _swimDir;
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
            TickRoar();
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
            // ★ 上下游动：模型容器的**竖直**正弦（对应自带动画 7.2–14.4 s 那段大幅上下）。
            //   与横向 `bodySwayAmp` 同构 —— 那条是"左右"、这条是"上下"，
            //   两条合起来才是用户要的"上下游动前进"。静默段归零。
            float bob = 0f;
            if (_airborne && bodyBobAmp > 0f)
                bob = bodyBobAmp * PerfBobScale * Mathf.Sin(2f * Mathf.PI * bodyBobFreq * Time.time);

            float want = _modelRootBaseLocalPos.y + bodyLift + (_airborne ? _currentLift : 0f) + bob;

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
                sway = bodySwayAmp * PerfMotionScale * Mathf.Sin(2f * Mathf.PI * bodySwayFreq * Time.time);

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
            // ★ 蓄势期也保持 S 形 + 整身下压蓄力（头侧 1.35 / 尾侧 0.45）。
            //   旧代码在这里什么都不做 ⇒ 身体从「预兆」起就已经僵住了。
            //   俯仰与游动波在**同一次绝对写入**里合成，见 ApplySpineOffsetsRaw 的 remarks。
            ApplySwimWave(diveSwimScale, 26f * u, 0.45f, 1.35f);
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
            // ── 水平：起点 → 预判落点（匀速）+ ★ 螺旋横摆 ──
            Vector3 want = Vector3.Lerp(_diveStart, _diveTarget, u);

            // ★★ 螺旋 / 蜿蜒俯冲（第二阶段定案）：
            //   旧实现是**直线**插值 ⇒ 读起来是「一根棍子戳下来」。
            //   现在在「起点→落点」这条主轴的水平法向上叠一个正弦摆动。
            //   摆幅取 sin(2π·turns·u)：u=0 与 u=1 都归零 ⇒ 与盘旋段 / 咬击段首尾无缝。
            Vector3 flatAxis = Flat(_diveTarget - _diveStart);
            if (diveSpiralAmp > 0.001f && flatAxis.sqrMagnitude > 0.01f)
            {
                Vector3 side = Vector3.Cross(Vector3.up, flatAxis.normalized);   // 主轴的水平右法向
                float sw = Mathf.Sin(2f * Mathf.PI * diveSpiralTurns * u);
                want += side * (diveSpiralAmp * sw);
            }

            Vector3 delta = Flat(want) - Flat(transform.position);
            float speed = circleMoveSpeed * diveSpeedMul;
            Vector3 step = delta.sqrMagnitude > 1e-6f
                ? delta.normalized * Mathf.Min(delta.magnitude, speed * Time.deltaTime)
                : Vector3.zero;
            MoveHorizontal(step);

            // ── 竖直：ease-in 落向打击高度（重力感）——**不落到地面**（见 strikeLift 注释）──
            float g = u * u;
            float lift = Mathf.Lerp(DesiredHoverY + 0.35f, strikeLift, g);
            // ★ 与横向**错开 90°**（cos）的竖直起伏，用 u·(1−u)·4 包络保证两端归零。
            //   横摆 + 纵摆相位差 90° 才读得出「螺旋」；同相位只会读成"之字形斜落"。
            if (diveSpiralVertAmp > 0.001f)
            {
                float envU = u * (1f - u) * 4f;
                lift += diveSpiralVertAmp * Mathf.Cos(2f * Mathf.PI * diveSpiralTurns * u) * envU;
            }
            _currentLift = lift;

            // ★ 俯冲全程保持游动波（用户原话：「时时刻刻都要遵循这个道理」）
            ApplySwimWave(diveSwimScale);

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

            // ★★ 整身扑咬（第二阶段定案）：
            //   旧实现只对 `_spine.Count - biteHeadLinks` 之后的几节施加俯仰 ——
            //   即**只有脖子在点头**，6 m 长的身子纹丝不动，读起来就是"很僵硬"。
            //   现在用**一条包络**铺开：尾 0.45 → 头 1.60 ⇒ 尾巴也跟着甩过来，整身"扑"下去，
            //   头颈那一段自然压得更狠（咬合的最后一口）。
            //   ★ 与游动波在同一次绝对写入里合成 —— 绝不能再事后左乘（会累积成 500°+）。
            ApplySwimWave(diveSwimScale, pitch, 0.45f, 1.6f);
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

        // ──────────────────────────────────────────────────────────────
        // ★★ 第二阶段（用户逐项定案）：螺旋/蜿蜒俯冲 + 整身扑咬
        //
        //   用户原话：「沿着 S 飞，然后在攻击的时候也要沿着 S 飞，**时时刻刻**都要遵循这个道理」。
        //   ⇒ 俯冲**不是**"把身体拉直了冲下去"。旧实现在 Tell/Dive/Strike/Recover 四个相位里
        //     **一次都没调用** `ApplySpineOffsetsRaw` ⇒ 身体在整段攻击里是硬的，
        //     只有头颈几节在动。这是"动作僵硬"的另一半来源（另一半见第十四轮运动核）。
        // ──────────────────────────────────────────────────────────────

        [Tooltip("★ 俯冲时的横向蛇形摆幅（m）。0 = 旧行为（直线冲）。")]
        public float diveSpiralAmp = 3.0f;

        [Tooltip("★ 俯冲全程的螺旋圈数。1.5 ≈ 明显地「拧」着下来。")]
        public float diveSpiralTurns = 1.5f;

        [Tooltip("★ 俯冲时的竖直起伏幅度（m）—— 与横向错开相位才读得出「螺旋」。")]
        public float diveSpiralVertAmp = 1.6f;

        [Tooltip("★ 俯冲 / 咬击 / 拉起时保留多少游动波（1 = 与高空巡游同幅）。")]
        public float diveSwimScale = 0.85f;

        /// <summary>
        /// ★★ 把「游动波」单独抽出来，供俯冲 / 咬击 / 拉起复用。
        /// 这是「时时刻刻沿 S 飞」的唯一落地点：只要相位用 `Time.time` 连续走，
        /// 各相位之间切换时波形就是连续的，不会"啪"地弹一下。
        /// </summary>
        private void ApplySwimWave(float scale, float extraPitchDeg = 0f,
                                   float extraRootGain = 1f, float extraHeadGain = 1f)
        {
            if (_spine.Count == 0) return;
            float phase = 2f * Mathf.PI * hoverFrequency * Time.time;
            ApplySpineOffsetsRaw(hoverAmplitudeDeg * scale, hoverPitchAmplitudeDeg * scale, _spine.Count,
                                 hoverPhaseStepDeg, waveAmpRootGain, waveAmpHeadGain, phase,
                                 extraPitchDeg, extraRootGain, extraHeadGain);
        }

        // ★ 这里曾有一个 `AddSpinePitch(deg, rootGain, headGain)` —— 它对每节 `rotation`
        //   **事后左乘**一个俯仰角。09-18 实测证明这个写法**必然把身体拧死**，故删除：
        //     Transform 是父子链，而 `rotation` setter 写的是**世界旋转** —— 父骨一转，
        //     所有子骨的世界旋转跟着变，于是"逐节左乘"就成了**累积**。
        //     实测 24 节累积 500°+，首尾直线 7.5 m → **3.3 m**，画面里龙蜷成一个圈。
        //   ⇒ 现在改走 `ApplySpineOffsetsRaw(..., extraPitchDeg, extraRootGain, extraHeadGain)`，
        //     与游动波在**同一次绝对写入**里合成（每帧从 `_baseRel` 重算 ⇒ 无累积）。

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
            // ★ 拉起全程保持 S 形 + 整身上抬的余韵（头侧 1.2 / 尾侧 0.4），把刚才「扑」下去的姿态收回。
            //   这一相位结束时控制权要交回 `TickCircling`，若这里绷直、下一帧又摆起来，
            //   画面上就是一次「抽搐」。
            ApplySwimWave(diveSwimScale, -18f * (1f - u), 0.4f, 1.2f);
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

            // ★ 风暴特效必须排在 `base.Update()`（状态机）与 `UpdateBodyLift()` **之后**：
            //   骨节的世界旋转是在状态机里写的、容器高度是 `UpdateBodyLift` 写的，
            //   特效要沿脊骨取点、要拿口部位置 ⇒ 早一帧取到的是上一帧的姿态，电弧会"黏在身后"。
            TickStorm();

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
            // 高度抬升（P1/P2/P3 = 3.6 / 4.4 / 5.2 m）
            _currentLift = PhaseHoverHeight;
            // ★ 阶段演出：仰头长啸（策划案 §4「阶段切换必须有演出」）
            BeginRoar();
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
        /// <param name="rootGain">链首 i=0（**尾 / 根侧**）的振幅增益。</param>
        /// <param name="headGain">链末 i=n-1（**头侧**）的振幅增益（&gt;1 表示放大）。</param>
        /// <param name="phase">当前波相位（弧度）。**由调用方给**；传 &lt;0 表示"用 sweepFrequency 自己算"。</param>
        /// <param name="extraPitchDeg">★★ 额外整身俯仰（度）——「整身扑咬 / 蓄势低头 / 拉起」用它。</param>
        /// <param name="extraRootGain">额外俯仰在链首（尾）的增益。</param>
        /// <param name="extraHeadGain">额外俯仰在链末（头）的增益。</param>
        /// <remarks>
        /// ★★ 额外俯仰**必须在这里合并、随同一次绝对写入落地**，绝不能事后对 `rotation` 再左乘一遍。
        ///
        ///   为什么（09-18 实测踩过）：这些 Transform 是**父子链**。事后左乘 = "读当前世界旋转再乘"，
        ///   而父骨一转，所有子骨的**世界旋转会跟着变** ⇒ 逐个乘下去就**累积**了：
        ///   实测 24 节 × 平均增益 0.9 ⇒ 累积 500° 以上，身体当场卷成一团，
        ///   首尾直线从 7.5 m 塌到 **3.3 m**（画面里就是"龙蜷成一个圈"）。
        ///   绝对式写入（`= rootRot · … · _baseRel[i]`）天然无累积，因为每帧都从基准重算。
        /// </remarks>
        private void ApplySpineOffsetsRaw(float yawMaxDeg, float pitchMaxDeg, int links, float phaseStepDeg,
                                          float rootGain = 1f, float headGain = 1f, float phase = -1f,
                                          float extraPitchDeg = 0f,
                                          float extraRootGain = 1f, float extraHeadGain = 1f)
        {
            if (phase < 0f) phase = 2f * Mathf.PI * sweepFrequency * Time.time;
            if (_spine.Count == 0) return;
            if (_baseRel.Count < _spine.Count) CaptureBaseRel();   // 防御：基准未就绪则补采
            int n = links <= 0 ? _spine.Count : Mathf.Min(links, _spine.Count);
            float step = phaseStepDeg * Mathf.Deg2Rad;
            bool alignHead = HeadAlignActive;
            Quaternion headExtra = HeadAlignExtra;
            int headFrom = HeadAlignFrom(n);
            bool lockHead = headLockToBase && headAlignLinks > 0;

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
                // ★★ 相位前的符号 = 行波的**传播方向**，不是随手写的。
                //   i 小 = 尾/根侧，i 大 = 头侧（见本文件 270 行附近的两条独立证据）。
                //   `phase - i·step` ⇒ 波峰随 t 增大从 i=0 流向 i=n-1 ＝ **尾→头**（反的）；
                //   `phase + i·step` ⇒ **头→尾**，才是真蛇/真龙（头先转向、身体再跟）。
                //   旧代码是减号 ⇒ 波形在动但"读不出生命力"，是用户说的"僵硬"来源之一。
                float ph = phase + i * step;
                float t = n <= 1 ? 0f : i / (float)(n - 1);
                float env = Mathf.Lerp(rootGain, headGain, t);      // i=0 尾侧 → i=n-1 头侧

                // 水平行波（左右蛇形）
                float yaw = yawMaxDeg * env * Mathf.Sin(ph);
                // ★ 垂直分量**相位错开 90°**：同相位会让每节沿 45° 斜向弯 ⇒ 整条龙拧成螺旋。
                //   错开后 = 水平行波 + 正交的垂直起伏，才是真实鳗/蛇的立体游动。
                float pitch = pitchMaxDeg * env * Mathf.Sin(ph + Mathf.PI * 0.5f);

                // ★ 写**世界旋转**（`rotation`）而非 `localRotation`：每节的角度是"相对基准的绝对角"，
                //   不受父节点影响 ⇒ 波形严格是 sin(phase − i·φ)，正是"行波"；
                //   逐节累加的弯曲由链式父子关系自然产生（与原来的"逐节增量"等价）。
                // ★ 额外整身俯仰（尾→头 线性包络）：与行波**在同一个绝对写入里**合成 ⇒ 无累积。
                float ex = extraPitchDeg * Mathf.Lerp(extraRootGain, extraHeadGain, t);
                // ★ 头部对齐（第十八轮）：与 ApplySerpentineSpine 用同一套规则 ——
                //   只作用在末尾 `headAlignLinks` 节上 ⇒ 头簇整块绕颈根转，脖子不受影响。
                //   两条驱动路径（本方法 / ApplySerpentineSpine）必须**行为一致**，
                //   否则攻击相位里一进一出，头会"啪"地弹一下。
                // ★★ 头锁定基准（与 ApplySerpentineSpine 同一逻辑）：两条驱动路径必须行为一致，
                //   否则攻击相位一进一出、头会「啪」地弹一下。
                if (lockHead && i >= headFrom)
                {
                    // ★★ 第二十六轮（坑表 48）：默认锁**拉直之前**的 FBX 原生姿态。
                    //   锁拉直后的 `_baseRel` 会把头永久钉在「被拉直转歪 93°」的姿态上 ——
                    //   这正是用户说的「三档全是歪的」。两条驱动路径必须选同一个源。
                    var src = (headLockUseRestPose && _restRel.Count == _spine.Count) ? _restRel : _baseRel;
                    // ★★ 第二十七轮（坑表 50）：**相对容器**的绝对锁 ⇒ 脖子摆、头不摆。
                    //   用户：「脖子动的时候，头一直保持一个方向没有旋转，要面向过来」。
                    //   改成「与父节保持 FBX 原生局部角」= 父节在哪头就在哪。
                    //   ★ `headExtra` 放在父节之后、局部偏置之前 ⇒ 仍是「相对父节抬/转头」。
                    //   ★ `hw = 0` 走原式（逐位等于旧行为，便于溯源与回退）。
                    Quaternion qAbs = rootRot * headExtra * src[i];
                    float hw = Mathf.Clamp01(headNeckFollowWeight);
                    if (hw <= 0f || i <= 0 || _spine[i - 1] == null)
                    {
                        _spine[i].rotation = qAbs;
                        continue;
                    }
                    Quaternion lRel = Quaternion.Inverse(src[i - 1]) * src[i];
                    Quaternion qRel = _spine[i - 1].rotation * headExtra * lRel;
                    _spine[i].rotation = hw >= 1f ? qRel : Quaternion.Slerp(qAbs, qRel, hw);
                    continue;
                }
                Quaternion q = Quaternion.AngleAxis(ex, Vector3.right)
                             * Quaternion.AngleAxis(yaw, Vector3.up)
                             * Quaternion.AngleAxis(pitch, Vector3.right);
                if (alignHead && i >= headFrom) q = headExtra * q;
                _spine[i].rotation = rootRot * q * _baseRel[i];
            }
            // 未驱动的节保持基准姿态
            for (int i = n; i < _spine.Count; i++) _spine[i].localRotation = _baseRot[i];

            ApplyTailLevel(phase);

            // 四肢（脊柱之外的分支骨）跟着一起摆 —— 否则只有身子在动、爪子钉着不动
            ApplyLimbMotion(phase);
        }

        // ==================================================================
        //  ★★ 蛇形游动：让**骨骼**排布成 sin 曲线（位置级贴合）
        // ==================================================================
        //
        // 【为什么要换掉旧做法】用户原话：「要让他的身体里面的骨骼贴合 sin，
        //   而不是整体的移动贴合 sin」。
        //   旧驱动（ApplySpineOffsetsRaw）给每节一个绕容器 up 的**偏转角**
        //   `yaw_i = A·sin(phase + i·step)`，靠父子链累积出形状。两个根本毛病：
        //     ① 侧向**位置** = 偏转角的**积分** ⇒ 真实形状是 cos（与设定相位差 90°），
        //        相邻节只差 22.5° 时积分还额外衰减 ⇒ 振幅被吃掉，形状也不是设定那条 sin；
        //     ② 它控制的是"每节朝哪"，位置完全靠累积 ⇒ 形状畸变，无法保证是 sin。
        //   现在改成：**先算曲线上的目标点，再让每节指向下一个目标点**。
        //
        // 【算法】
        //   ① 世界空间定义中心线（`origin` = 链根 = 尾侧，`fwd`/`right` = 容器前向/右向）：
        //        P(u) = origin + fwd·(u·axialLen) + right·(amp·env(u)·sin(2π·W·u + phase))
        //        env(u) = sin(πu)^p  ⇒ **u=0 与 u=1 处都为 0**
        //      ⚠⚠ 这条旧设计**已被用户实测否掉**：两端归零 ⇒ 端点的切向 = 纯轴向 ⇒
        //        第 0 节（尾）与第 23 节（头）几乎不转。用户的原话是
        //        「现在这个头和后面的爪子，还有尾巴都没有动呢」。
        //      ★ 更正一个曾经想当然的前提：「曲线起点必须落在链根上」是**不必要的**。
        //        驱动只用曲线的**方向**（相邻点的差），lat(0) 是否为 0 只会让整条曲线平移，
        //        不会改变任何一节的方向 ⇒ **包络两端可以任意非零**。
        //   ② 逐节按**累计骨长比例**取 u ⇒ u 恰好铺满 [0, 1]，波形在整条身长上完整展开。
        //      （旧版"弦长 = 骨长"在弯曲曲线上走不完 u：实测轴向跨度 6.85 m / 折线 8.27 m
        //        ⇒ 头段 17% 的波形被切掉，头端只到 u≈0.83。）
        //   ③ 旋转：`rotation = rootRot · FromToRotation(基准段方向, 切向) · _baseRel[i]`
        //      —— **绝对写入**，每帧从基准重算 ⇒ 无累积
        //      （对比：事后左乘会让 24 节累积 500°，身长 7.5→3.3 m，见 ApplySpineOffsetsRaw 的注释）。
        //
        // 【一个必须知道的几何上限】链是刚性直杆，曲线不能"拧"过某条线，否则会出现
        //   **折返**（点在 u 方向上的投影速度为负）⇒ 弦长反解不单调、形状自交。
        //   判据：`amp · max|d/du[env·sin]| < axialLen`（= 链总骨长）。
        //   在 W=1.25 / env=sin²πu 下该系数 ≈ 8.4 ⇒ amp < 总骨长/8.4 ≈ 0.98（本龙 8.27 m）。
        //   调大 swimWaveCount 会线性抬高这个系数 —— 改参数后务必用 `drg_serp` 复测"无折返"。

        /// <summary>中心线上参数 u 处的点（世界空间）。u 在 [0,1] 上对应「尾→头」的整条身长。</summary>
        private static Vector3 SerpPoint(Vector3 origin, Vector3 fwd, Vector3 right, float axialLen,
                                         float amp, float waveCount, float tailGain, float headGain,
                                         float phase, float u)
        {
            // ★★ 包络 = sin(πu) × 线性增益。指数**必须是 1**，这一条同时卡住了两个相反的约束：
            //
            //  【约束 A：端点的"方向"必须会变】—— 端点动不动，全看 env'(0) / env'(1) 是否恒 0：
            //    env = sin(πu)^n（n≥2）时 env'(0) = n·sin^{n-1}(0)·cos(0)·π = 0，**恒成立**
            //      ⇒ 曲线在 u=0 处的切向 = 纯轴向 ⇒ 第 0 节永远不转。用户反馈的
            //        「头和尾巴都没有动」正是这个，而且**与相位无关**，调幅度救不回来。
            //    env = sin(πu)^1 时 env'(0) = π ≠ 0
            //      ⇒ 端点切向 = atan(π·amp·sin(φ) / axialLen)，随相位来回摆 ⇒ 极差 ≠ 0。
            //
            //  【约束 B：端点的"位置"必须归零】—— 链的第 0 节**位置**被父级 `_rootJoint`
            //    钉死，所以链的实际横向形状 = 曲线横向值 − 曲线在起点的值。
            //    若 lat(0) ≠ 0（上一版把包络改成纯线性渐变，就是这种情况），这个差值在
            //    某些相位下会**全体同号** ⇒ 身体退化成单侧 C 形。
            //    实测证据（drg_form 段 B 帧 00，W=1.0、线性包络）：i=6..15 的 lat 全是负的，
            //    对应截图 dfm_0072 里那个"一条弧"的形状。
            //    env 两端归零 ⇒ lat(0) ≡ 0（与相位无关）⇒ 形状恒有正有负 ⇒ 始终是 S。
            //
            //  ⇒ 两条要求同时满足的唯一简单解就是 env = sin(πu)^1 × 增益。
            float uc = Mathf.Clamp01(u);
            float env = Mathf.Sin(Mathf.PI * uc) * Mathf.Lerp(tailGain, headGain, uc);
            float lat = amp * env * Mathf.Sin(2f * Mathf.PI * waveCount * u + phase);
            return origin + fwd * (u * axialLen) + right * lat;
        }

        // ★ `SolveSerpU`（在曲线上按"弦长 = 骨长"反解 u）已删除：
        //   它在弯曲曲线上走不完 u ∈ [0,1]（实测只到 0.83）⇒ 头段 17% 的波形被切掉，
        //   而且每帧 24 次迭代二分，纯开销。现在改用累计骨长比例直接取 u
        //   （见 ApplySerpentineSpine），一行代替一整段求解器。

        /// <summary>
        /// 蛇形游动的主驱动：把整条脊骨**排布到一条 sin 中心线上**。
        /// 姿态相关的取舍见本节顶部长注释。
        /// </summary>
        /// <param name="ampScale">整体幅度缩放（静默段 / 长啸用；1 = 正常）</param>
        /// <param name="phase">行波相位（弧度）—— 由 <see cref="swimWaveFreq"/> 与 Time.time 决定</param>
        private void ApplySerpentineSpine(float ampScale, float phase)
        {
            int n = _spine.Count;
            if (n < 3) return;
            if (_baseRel.Count < n) CaptureBaseRel();
            if (_baseSegLocal.Count < n || _segLen.Count < n - 1) CaptureSegments();
            if (_baseSegLocal.Count < n || _segLen.Count < n - 1) return;

            Vector3 origin = _spine[0] != null ? _spine[0].position : transform.position;
            // ★ 用**容器当前前向**而不是 `_swimDir`：本方法在 `transform.rotation = LookRotation(_swimDir)`
            //   **之后**调用，两者已一致；用 transform.forward 少一层耦合。
            Vector3 fwd = Flat(transform.forward);
            if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.forward;
            fwd.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized;

            float total = 0f;
            for (int i = 0; i < n - 1; i++) total += _segLen[i];
            if (total < 1e-3f) return;

            float amp = swimWaveAmp * ampScale;
            float W = Mathf.Max(0.05f, swimWaveCount);
            float tg = swimWaveTailGain;
            float hg = swimWaveHeadGain;

            Quaternion rootRot = _modelRoot != null ? _modelRoot.rotation : transform.rotation;
            Quaternion invRoot = Quaternion.Inverse(rootRot);

            if (_serpTan == null || _serpTan.Length < n) _serpTan = new Vector3[n];
            Vector3[] tan = _serpTan;

            // ── 逐节在曲线上取值，得到每节的**目标段方向** ──
            // ★★ 自变量改成「**累计骨长比例**」，不用旧版的「弦长 = 骨长」反解。
            //   旧版保证「本节点到上一点的直线距离 = 骨长」，这在**弯曲**的曲线上意味着
            //   每节要吃掉的弧长比轴向步长大 ⇒ 走完 Σ骨长 = 8.266 m 时参数只到 u ≈ 0.83
            //   （实测轴向跨度 6.85 m）⇒ 头段 17% 的波形被切掉。
            //   现在 u = 累计骨长 / 总骨长，恰好铺满 [0, 1]：波形在整条身长上完整展开。
            //   （链的位置仍由父子链按骨长累积 ⇒ 身长恒为 Σ骨长，不受取点方式影响。）
            float acc = 0f;
            Vector3 prev = SerpPoint(origin, fwd, right, total, amp, W, tg, hg, phase, 0f);
            for (int i = 0; i + 1 < n; i++)
            {
                acc += _segLen[i];
                float uh = total > 1e-6f ? Mathf.Clamp01(acc / total) : 0f;
                Vector3 p = SerpPoint(origin, fwd, right, total, amp, W, tg, hg, phase, uh);
                Vector3 d = p - prev;
                tan[i] = d.sqrMagnitude > 1e-12f ? d.normalized : fwd;
                prev = p;
            }
            tan[n - 1] = tan[n - 2];        // 末节没有下一节，沿用头段方向（头骨朝向仍要驱动）

            // ── 头部对齐（第十八轮）──
            // ★ 为什么作用在**末节**上一点就够：头簇（39 枚叶子骨 + 14 条鬃/须/角/颌）整块挂在
            //   `_spine[Count-2]` 下，而 `_spine[Count-1]` 是**无几何的链尾骨** ——
            //   所以旋转 `Count-2` ⇒ 头整块绕颈根转，**脖子本身一动不动**，
            //   不会像旧的 ApplyHeadSteer 那样"事后左乘把 sin 形状揉歪"。
            // ★ 用**整旋转**（`extra * q`）而不是只转目标方向：只转方向的话，
            //   `FromToRotation` 是最小旋转、绕骨轴的 roll 不受控 ⇒ 头会"转过去了但扭着"。
            Quaternion headExtra = Quaternion.Euler(-headAlignPitchDeg, headAlignYawDeg, headAlignRollDeg);
            bool alignHead = HeadAlignActive;
            int headFrom = HeadAlignFrom(n);

            // ── 绝对写入：每节从基准重算，无累积 ──
            // ★★ 第二十五轮「头锁定基准」（用户：「龙头要始终面向前方」）：
            //   末尾 `headAlignLinks` 节**不读曲线切向**，直接采用基准朝向
            //   ⇒ 跳过 `FromToRotation` ⇒ 头簇停在基准姿态、**与行波相位无关**。
            //   为什么必须锁：`FromToRotation(倾斜基准段, 水平切向)` 是最小旋转，
            //   切向左右摆时把偏航耦合出俯仰（`drg_headnod` 实测吻部仰角极差 49.65°）。
            bool lockHead = headLockToBase && headAlignLinks > 0;
            for (int i = 0; i < n; i++)
            {
                if (_spine[i] == null) continue;
                if (lockHead && i >= headFrom)
                {
                    // ★★ 第二十六轮（坑表 48）：默认锁**拉直之前**的 FBX 原生姿态。
                    //   锁拉直后的 `_baseRel` 会把头永久钉在「被拉直转歪 93°」的姿态上 ——
                    //   这正是用户说的「三档全是歪的」。两条驱动路径必须选同一个源。
                    var src = (headLockUseRestPose && _restRel.Count == _spine.Count) ? _restRel : _baseRel;
                    // ★★ 第二十七轮（坑表 50）：**相对容器**的绝对锁 ⇒ 脖子摆、头不摆。
                    //   用户：「脖子动的时候，头一直保持一个方向没有旋转，要面向过来」。
                    //   改成「与父节保持 FBX 原生局部角」= 父节在哪头就在哪。
                    //   ★ `headExtra` 放在父节之后、局部偏置之前 ⇒ 仍是「相对父节抬/转头」。
                    //   ★ `hw = 0` 走原式（逐位等于旧行为，便于溯源与回退）。
                    Quaternion qAbs = rootRot * headExtra * src[i];
                    float hw = Mathf.Clamp01(headNeckFollowWeight);
                    if (hw <= 0f || i <= 0 || _spine[i - 1] == null)
                    {
                        _spine[i].rotation = qAbs;
                        continue;
                    }
                    Quaternion lRel = Quaternion.Inverse(src[i - 1]) * src[i];
                    Quaternion qRel = _spine[i - 1].rotation * headExtra * lRel;
                    _spine[i].rotation = hw >= 1f ? qRel : Quaternion.Slerp(qAbs, qRel, hw);
                    continue;
                }
                Vector3 tLocal = invRoot * tan[i];
                Quaternion q = Quaternion.FromToRotation(_baseSegLocal[i], tLocal);
                if (alignHead && i >= headFrom) q = headExtra * q;
                _spine[i].rotation = rootRot * q * _baseRel[i];
            }

            // ★ 把脊柱曲线的**同一组参数**交给尾巴 ⇒ 尾巴是这条曲线往 u<0 的延长，不是第二条波
            ApplyTailLevel(phase, amp, W, tg, total);
            ApplyLimbMotion(phase);
        }

        /// <summary>头部对齐是否生效（三条角度全 0 或节数 ≤ 0 时视为关闭）。</summary>
        private bool HeadAlignActive =>
            headAlignLinks > 0 && (headAlignPitchDeg != 0f || headAlignYawDeg != 0f || headAlignRollDeg != 0f);

        /// <summary>
        /// 头部对齐的旋转（**容器局部系**）。
        /// `headAlignPitchDeg` 以「抬头为正」命名，而 Unity 的 `Euler.x > 0` 是**低头**，所以这里取负号。
        /// </summary>
        private Quaternion HeadAlignExtra =>
            Quaternion.Euler(-headAlignPitchDeg, headAlignYawDeg, headAlignRollDeg);

        /// <summary>头部对齐从第几节开始作用（链条末尾 `headAlignLinks` 节）。</summary>
        private int HeadAlignFrom(int n) => Mathf.Max(0, n - Mathf.Max(1, headAlignLinks));

        #region 龙头对齐调参面板（第二十八轮·终，F9 开关）

        // ★★ 应用户要求：「你给我一个操控的方便的调整数据的东西，我来帮你把头调整准确的位置」。
        //   自动反解三轮皆败（见 headAlignPitchDeg 注释），改为**人眼在回路**：
        //   Play 中按 F9 呼出窗口，滑条/数字框实时改 headAlign 三旋钮
        //   （HeadAlignExtra 每帧现算 ⇒ 改动当帧生效），调好后点「复制数值」粘给 Claude 落盘。
        //   ★ 慢动作按钮（0.1×/0.02×）：把盘旋放慢到近冻结，Update 仍在跑 ⇒ 滑条改动实时可见。
        private static bool _headTunerOn;
        private Rect _headTunerRect = new Rect(24f, 80f, 372f, 240f);

        protected virtual void OnGUI()
        {
            var e = Event.current;
            if (e != null && e.type == EventType.KeyDown && e.keyCode == KeyCode.F9)
            {
                _headTunerOn = !_headTunerOn;
                e.Use();
            }
            if (!_headTunerOn) return;
            _headTunerRect = GUILayout.Window(GetInstanceID(), _headTunerRect, HeadTunerWindow,
                "龙头对齐调参（相对脖子）· F9 开/关");
        }

        private void HeadTunerWindow(int id)
        {
            GUILayout.Label("拧到「鼻子顺着脖子朝外」为止；数字框可直接输入精确值。");
            headAlignPitchDeg = KnobRow("抬头 Pitch", headAlignPitchDeg, -90f, 90f, "正=抬头");
            headAlignYawDeg = KnobRow("偏航 Yaw", headAlignYawDeg, -180f, 180f, "正=转身体左侧");
            headAlignRollDeg = KnobRow("滚转 Roll", headAlignRollDeg, -180f, 180f, "绕身体轴");

            GUILayout.Space(6f);
            GUILayout.BeginHorizontal();
            GUILayout.Label("游戏速度", GUILayout.Width(62f));
            float cur = Time.timeScale;
            foreach (float sp in new[] { 1f, 0.1f, 0.02f })
            {
                bool on = Mathf.Abs(cur - sp) < 0.005f;
                var style = on ? GUI.skin.box : GUI.skin.button;
                if (GUILayout.Button(sp == 1f ? "1×" : sp.ToString("0.##") + "×", style)) Time.timeScale = sp;
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("归零（FBX 原生姿态）"))
            {
                headAlignPitchDeg = 0f; headAlignYawDeg = 0f; headAlignRollDeg = 0f;
            }
            if (GUILayout.Button("复制数值（粘给 Claude 落盘）"))
            {
                string s = string.Format("headAlign: pitch={0}, yaw={1}, roll={2}",
                    headAlignPitchDeg.ToString("F2"),
                    headAlignYawDeg.ToString("F2"),
                    headAlignRollDeg.ToString("F2"));
                GUIUtility.systemCopyBuffer = s;
                Debug.Log("[HeadTuner] " + s);
            }
            GUILayout.EndHorizontal();

            GUI.DragWindow();
        }

        /// <summary>一行旋钮：标签 + 滑条 + 数字框（非法输入保留原值）。</summary>
        private float KnobRow(string label, float v, float min, float max, string hint)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(78f));
            GUILayout.Label(hint, GUI.skin.box, GUILayout.Width(88f));
            v = GUILayout.HorizontalSlider(v, min, max);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Space(78f);
            string s = GUILayout.TextField(v.ToString("F1"), GUILayout.Width(64f));
            if (float.TryParse(s, out float parsed)) v = Mathf.Clamp(parsed, min, max);
            GUILayout.EndHorizontal();
            return v;
        }

        #endregion

        /// <summary>
        /// 尾巴水平化（第十八轮）。
        ///
        /// ★ 为什么要单独驱动尾巴：`drg_head` 实测尾巴**逐节 pitch 从 −6° 爬到 −33°**
        ///   （基准姿态自带，amp=0 时一样），末端绝对抬升 0.265 m、中段累计 0.799 m
        ///   ⇒ 用户「尾巴末端是歪的，翘起来了，应该保持同一水平线的高度」。
        ///   上一轮把尾巴整条冻成刚性分支，只解决了"不单独摆动"，**没解决"翘"**。
        ///
        /// ★ 做法与脊柱同源：在**水平面内**铺一条中心线，逐节取切线当目标段方向。
        ///   `back` 与 `right` 都水平 ⇒ 目标段方向**恒在水平面内** ⇒ 尾巴永远不离水平线；
        ///   同时横向叠一个与身体同相位的行波 ⇒ 不是一根死棍子（用户要「动画要不断重复」）。
        ///   横向包络用 `amp·u`（尾根 0 → 尾尖最大）：尾根为 0 才与身体**无缝相接**，
        ///   否则尾巴根部会相对身体突然横移一个常量。
        ///
        /// ★ 必须**绝对写入**（每帧从基准重算）—— 叠乘会累积，见坑表条目 16。
        ///
        /// ── 第十九轮：尾巴改成「身体 sin 波形的**延续**」──
        ///
        /// ★ 为什么必须这样改（用户反馈「尾巴还是没有跟上 sin 的」）：旧版给尾巴铺的是
        ///   **第二条独立的波** `amp·u·sin(2π·span·u + ph)`，它和身体的波之间没有任何几何约束，
        ///   实测两处对不上：
        ///     ① **传播方向相反**。身体是 `sin(2πWu+φ)`，定义域 u∈[0,1]（u=0 尾根 / u=1 头），
        ///        相位随时间增 ⇒ 波峰位置 u_c=(π/2−φ)/2πW **随时间减小** ⇒ 波往 u=0 走 = **头→尾**。
        ///        尾巴的 u 是「尾根→尾尖」，同一形式下波峰也往 u=0 走 = **尾尖→尾根**
        ///        ⇒ 两条波**对着撞**，尾巴读起来是自己在那儿抖，不是身体的波流过去。
        ///     ② **接点切向不连续**。把两段曲线在接点处的一阶导写出来（δ→0）：
        ///          身体段方向 ∝ δ·[ +fwd·T + right·π·amp·gain·sin φ ]      （T = 身体总骨长）
        ///          尾巴段方向 ∝ δ·[ −fwd·L + right·tailWaveAmp·sin φ ]      （L = 尾长）
        ///        横向分量之比 = π·amp·gain : tailWaveAmp = **3.61 : 0.35**（差 10 倍）
        ///        ⇒ 身体尾端在摆、尾根几乎不动 ⇒ 视觉上就是「尾巴没跟上」。
        ///   ⇒ 正解不是"再调两个数"，而是让尾巴中心线**就是身体的同一条曲线**：把身体曲线的
        ///      参数往 u < 0 外推（尾巴本来就在身体末端之外，曲线参数就该是负的）。
        ///      这样波长 / 相位 / 传播方向 / 接点切向**全部按构造相等**，无需任何额外约束。
        ///
        /// ★ 外推时 `sin(πu)` 在 u<0 上取**负值** —— 这个负号正是接点切向能连续的原因
        ///   （相当于自动反相）。**绝不能取绝对值**：取了绝对值接点切向就反号，又变回折角。
        /// </summary>
        /// <param name="bodyAmp">脊柱曲线的横向振幅（m）。为 0 时自动落回旧版独立行波
        /// （攻击相位走 `ApplySpineOffsetsRaw`，那条路上身体不是 sin 曲线，没有可延续的对象）。</param>
        private void ApplyTailLevel(float phase, float bodyAmp = 0f, float bodyWaveCount = 1f,
                                    float bodyGain = 1f, float bodyTotal = 0f)
        {
            int n = _tailChain.Count;
            if (!tailLevel || n < 2) return;
            if (_tailBaseRel.Count < n || _tailBaseSegLocal.Count < n || _tailSegLen.Count < n - 1) return;

            bool follow = tailFollowBodyWave && bodyAmp > 1e-6f && bodyTotal > 1e-3f
                          && _spine.Count > 0 && _spine[0] != null;

            // 与脊柱同源：都用**容器当前前向**（本方法在 ApplySerpentineSpine 里、LookRotation 之后调用）
            Vector3 fwd = Flat(transform.forward);
            if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.forward;
            fwd.Normalize();
            Vector3 back = -fwd;
            Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized;

            // ★ 延续模式必须与脊柱**同源**：同一个原点（`_spine[0]`）、同一个轴向、同一个总骨长。
            //   用 `_tailChain[0].position` 当原点会差一个骨长量级的偏移。
            //   （本方法只取**方向**，所以顶点整体平移不影响结果，但同源更不容易出错。）
            Vector3 origin = follow
                ? _spine[0].position
                : (_tailChain[0] != null ? _tailChain[0].position : transform.position);

            float L = Mathf.Max(1e-3f, _tailTotalLen);
            float ph = phase + tailPhaseLagDeg * Mathf.Deg2Rad;

            float amp = follow ? tailFollowGain * bodyAmp : tailWaveAmp;
            float span = follow ? bodyWaveCount : tailWaveSpan;
            float gain = follow ? bodyGain : 1f;
            float axial = follow ? bodyTotal : L;
            Vector3 axialDir = follow ? fwd : back;
            float uScale = follow ? -(L / bodyTotal) : 1f;   // 尾链 u → 曲线参数 u（负 = 外推）

            Quaternion rootRot = _modelRoot != null ? _modelRoot.rotation : transform.rotation;
            Quaternion invRoot = Quaternion.Inverse(rootRot);

            if (_tailTan == null || _tailTan.Length < n) _tailTan = new Vector3[n];
            Vector3[] tan = _tailTan;

            float acc = 0f;
            Vector3 prev = TailPoint(origin, axialDir, right, axial, amp, span, gain, ph, 0f, follow);
            for (int i = 0; i + 1 < n; i++)
            {
                acc += _tailSegLen[i];
                float u = Mathf.Clamp01(acc / L);
                Vector3 p = TailPoint(origin, axialDir, right, axial, amp, span, gain, ph, u * uScale, follow);
                Vector3 d = p - prev;
                tan[i] = d.sqrMagnitude > 1e-12f ? d.normalized : back;
                prev = p;
            }
            tan[n - 1] = tan[n - 2];

            for (int i = 0; i < n; i++)
            {
                if (_tailChain[i] == null) continue;
                Vector3 tLocal = invRoot * tan[i];
                Quaternion q = Quaternion.FromToRotation(_tailBaseSegLocal[i], tLocal);
                _tailChain[i].rotation = rootRot * q * _tailBaseRel[i];
            }
        }

        /// <summary>
        /// 尾巴中心线上参数 u 处的点（世界空间；u 在 [0,1] 上对应「尾根→尾尖」）。
        ///
        /// `follow = true`：**身体 sin 曲线往 u&lt;0 的外推** —— 与 `SerpPoint` 同一个公式，
        ///   只去掉 `u` 的 clamp（尾巴的曲线参数本来就是负的）。
        /// `follow = false`：旧版独立行波，空间项取**负**号（波峰向尾尖走，与身体同向）。
        /// </summary>
        private static Vector3 TailPoint(Vector3 origin, Vector3 axial, Vector3 right,
                                        float axialLen, float amp, float waveCount, float gain,
                                        float phase, float u, bool follow)
        {
            if (follow)
            {
                // u < 0 ⇒ sin(πu) < 0。这个负号是接点切向连续的关键，不能取绝对值。
                float env = Mathf.Sin(Mathf.PI * u) * gain;
                float latF = amp * env * Mathf.Sin(2f * Mathf.PI * waveCount * u + phase);
                return origin + axial * (u * axialLen) + right * latF;
            }
            float lat = amp * u * Mathf.Sin(-2f * Mathf.PI * waveCount * u + phase);
            return origin + axial * (u * axialLen) + right * lat;
        }

        /// <summary>回落到地面（只在旧行为 aerialLoop=false 时用到）。</summary>
        private void EndHover()
        {
            _airborne = false;
            _currentLift = 0f;
            for (int i = 0; i < _spine.Count; i++) _spine[i].localRotation = _baseRot[i];
            // ★ 尾巴也要一起复位（第十八轮起尾巴是**被驱动**的链；不复位就会留着一个扭曲姿态，
            //   下次起飞第一帧会看见它"弹"回基准）
            for (int i = 0; i < _tailChain.Count && i < _tailBaseLocalRot.Count; i++)
                if (_tailChain[i] != null) _tailChain[i].localRotation = _tailBaseLocalRot[i];
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
            // 原地悬停：每次进盘旋都重新取朝向基准（否则会沿用上一次的残留）
            _stationaryInit = false;
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

        // ══════════════════════════════════════════════════════════════════════
        //  「风暴特效」接口层（第二十三轮）
        //
        //  设计：`Docs/墨龙特效设计.md`。分两层 ——
        //    玩法层（这里）只回答"现在是什么姿态、每帧要不要张嘴喷"，
        //    表现层（`InkWash.Effects.DragonStormVfx`）自己决定"几条电弧、多浓的烟"。
        //  ⇒ **阈值与条数都不在龙这边**：龙改了招式不会连带把特效调参调歪，
        //    特效调参也不会碰玩法代码（`工程坑表` 一·「一个阈值被两个身份不同的对象共用迟早出事」）。
        //
        //  ★ 为什么只在 `Update()` 里**一处**驱动，而不是按设计文档 §三 那张表
        //    分散到 `DoDiveTell` / `DoDiveTravel` / `DoStrikeBite` … 里各写一行：
        //    分散写要保证"每条路径都到达、且都不重复"，而这张状态表有 8 行、
        //    还有 `_dive`/`_attack`/`IsRoaring` 三个维度交叉 —— 少写一处就静默漏特效。
        //    集中一处读**本帧的最终状态**（在 `base.Update()` 跑完状态机之后）既不会漏，
        //    也不会因为调用顺序不同而算出不同的值。文档 §三 那张表仍然是**唯一真相**，
        //    只是把"查表"这件事收敛到了一个 `switch` 里。
        // ══════════════════════════════════════════════════════════════════════

        [Header("风暴特效（青白细电弧 + 纯墨黑烟）")]
        [Tooltip("关掉就完全不画（调试用）。真值表见 Docs/墨龙特效设计.md §三")]
        public bool stormVfx = true;

        /// <summary>链节数（i 小 = 尾/根侧，i 大 = 头侧）。<see cref="InkWash.Effects.IDragonSpineSource"/></summary>
        public int SpineCount => _spine.Count;

        /// <summary>第 i 节的世界位置。越界夹取；链空时给 <c>transform.position</c>。</summary>
        public Vector3 GetSpinePosition(int i)
        {
            int n = _spine.Count;
            if (n == 0) return transform.position;
            var t = _spine[Mathf.Clamp(i, 0, n - 1)];
            return t != null ? t.position : transform.position;
        }

        /// <summary>
        /// 第 i 节"沿链前进"的单位方向（相邻节点差分）。
        /// ★ 用**差分**而不是 `transform.forward`：骨节的局部轴在拉直/驱动之后与容器轴
        ///   不再对齐（`工程坑表` 三·「不用骨骼局部轴做几何约定」），差分才永远指向下一节。
        /// </summary>
        public Vector3 GetSpineDirection(int i)
        {
            int n = _spine.Count;
            if (n < 2) return transform.forward;
            int a = Mathf.Clamp(i, 0, n - 2);
            var ta = _spine[a]; var tb = _spine[a + 1];
            if (ta == null || tb == null) return transform.forward;
            Vector3 d = tb.position - ta.position;
            return d.sqrMagnitude < 1e-8f ? transform.forward : d.normalized;
        }

        /// <summary>口部位置（喷息 / 喷烟的发射点）。与既有 <see cref="GetHeadPosition"/> 同口径。</summary>
        public Vector3 GetMouthPosition() { return GetHeadPosition(); }

        /// <summary>口部朝向（喷息主方向）。</summary>
        /// ★ 第二十八轮：改用「颈根(_spine[n-3]) → 鼻尖(_spine[n-1])」的**弦方向**，
        ///   不再用 `_spine[n-1].forward` 骨骼局部轴 ——
        ///   ① 坑表三·「不用骨骼局部轴做几何约定」：头部对齐旋钮（headAlign*）作用在骨骼
        ///      局部系上，骨轴朝向会随调参漂移（实测 knobs 调平后骨轴上仰 57.2°、段方向 −18.4°，
        ///      而视觉吻部水平 ⇒ 只有弦方向与所见一致）；
        ///   ② 弦方向随攻击俯仰自然跟踪（俯冲/拉起时颈根与鼻尖一起动）。
        public Vector3 GetMouthForward()
        {
            int n = _spine.Count;
            if (n < 2) return transform.forward;
            var a = _spine[n - 3] != null ? _spine[n - 3] : _spine[0];
            var b = _spine[n - 1];
            if (a == null || b == null) return transform.forward;
            Vector3 f = b.position - a.position;
            return f.sqrMagnitude < 1e-6f ? transform.forward : f.normalized;
        }

        /// <summary>本帧的姿态 → 特效姿态。真值表见 <c>Docs/墨龙特效设计.md</c> §三。</summary>
        private InkWash.Effects.DragonStormVfx.Pose CurrentStormPose(out float mouth01)
        {
            mouth01 = 0f;
            // 死亡 / 未起飞 ⇒ 全关。地面上的龙不该带电弧（设计文档 §三 最后一行）
            if (!stormVfx || !IsAlive || !_airborne) return InkWash.Effects.DragonStormVfx.Pose.Off;

            // 阶段演出的长啸：比常态亮一档（文档 §三 没这一行，是补的 —— 长啸本就该带电）
            if (IsRoaring) return InkWash.Effects.DragonStormVfx.Pose.Roar;

            switch (_attack)
            {
                case DragonAttack.Bite:
                case DragonAttack.TailSweep:
                    switch (_dive)
                    {
                        case DivePhase.Tell: return InkWash.Effects.DragonStormVfx.Pose.Tell;
                        case DivePhase.Dive: return InkWash.Effects.DragonStormVfx.Pose.Dive;
                        case DivePhase.Strike: return InkWash.Effects.DragonStormVfx.Pose.Strike;
                        case DivePhase.Recover: return InkWash.Effects.DragonStormVfx.Pose.Recover;
                    }
                    return InkWash.Effects.DragonStormVfx.Pose.Idle;

                case DragonAttack.Breath:
                    mouth01 = 1f;       // 吐息：口部向前喷烟（用户定案「只有口部向前喷」）
                    return InkWash.Effects.DragonStormVfx.Pose.Breath;

                case DragonAttack.HoverOrbit:
                    return InkWash.Effects.DragonStormVfx.Pose.Idle;
            }
            return InkWash.Effects.DragonStormVfx.Pose.Idle;
        }

        /// <summary>每帧一次：把姿态报给表现层（见上面的接口层说明）。</summary>
        private void TickStorm()
        {
            float mouth01;
            var pose = CurrentStormPose(out mouth01);
            InkWash.Effects.DragonStormVfx.Drive(this, pose, mouth01);
        }

        public override bool TakeDamage(DamageInfo info)
        {
            bool ok = base.TakeDamage(info);
            // 受击瞬时放电（设计文档 §三：8~10 条，0.25 s 后回落）。纯表现，与"改不改动作"无关。
            // 强度按"这一下占了最大生命的多少"归一 —— 重击更炸，蹭一下就只是噼啪。
            if (ok)
                InkWash.Effects.DragonStormVfx.Burst(
                    maxHealth > 0f ? Mathf.Clamp01(info.amount / (maxHealth * 0.08f)) : 1f);
            // 龙是 Boss：被打不改动作，但受击时**必须能被打断盘旋**，否则玩家打不到它
            if (ok && _airborne && UnityEngine.Random.value < 0.45f) EndHover();
            return ok;
        }

        protected override void OnDied()
        {
            base.OnDied();
            // 死亡 ⇒ 全套关闭（设计文档 §三 最后一行）。烟让它自己散，不硬收。
            InkWash.Effects.DragonStormVfx.Shut();
            EndHover();
        }
    }
}
