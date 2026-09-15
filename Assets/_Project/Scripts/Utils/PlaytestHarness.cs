using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using InkWash.Player;
using InkWash.CameraRig;
using InkWash.Effects;
using InkWash.Core;
using InkWash.Combat;
using InkWash.Enemies;
using InkWash.Roguelike;
using InkWash.Rendering;
using InkWash.UI;

namespace InkWash.Utils
{
    /// <summary>
    /// 自动化试玩台：在 Play 模式下用注入输入驱动角色，逐帧采样并生成验收报告。
    ///
    /// 为什么需要它：
    ///   本项目使用传统 Input Manager（未安装 Input System 包），自动化工具无法注入真实按键
    ///   —— 桥的键盘注入依赖 Input System 的新输入设备。因此改用 PlayerController 的输入注入入口，
    ///   做到"可重复、可回归"的自动验收，而不是靠人眼截图。
    ///
    /// 两个流程：
    ///   * <see cref="S1LocomotionFlow"/> —— Sprint 1：移动 + 相机（M1：跑动顺滑、镜头不抖）
    ///   * <see cref="S2CombatFlow"/>     —— Sprint 2：连击 + 取消窗口 + 特效 + 步伐同步（M2）
    ///   * <see cref="AudioSmokeCheck"/>  —— Sprint 2：音频链路（Resources 取片段 → AudioSource → isPlaying）
    ///   * <see cref="RunAll"/>           —— 一次进 Play 把两段都跑完
    ///
    /// 复用：每个 Sprint 的验收都往这里加一个 Flow 协程。报告写入 &lt;项目根&gt;/Tools/reports/。
    ///
    /// 度量口径上踩过的坑（已修，别重犯）：
    ///   1. 欧拉角不能直接取 max-min —— 0° 附近会回绕成 359.99°，必须先解卷绕。
    ///   2. 时长不能拿"末帧 deltaTime × 帧数"估算 —— 帧卡顿时会离谱，必须逐帧累加。
    ///   3. "抖不抖"不能用每帧位移衡量 —— 帧率一变数值就变。改用帧率无关的相机加速度 RMS，
    ///      并以屏幕空间稳定性（角色在视口内的漂移量）作为主判据。
    ///   4. 每阶段必须把角色复位到场地中央 —— 否则疾跑 2.5s 会撞上院墙，
    ///      测出来的"速度"其实是顶着墙的期望速度（第一版就是这么错的）。
    ///   5. 动画/指标要用**实际**速度而非期望速度 —— 两者在受阻时会分离。
    ///   6. "镜头抖不抖"不能用逐帧有限差分加速度判定 —— 卡顿帧（编辑器内录 MP4 时必然出现，
    ///      帧长 30~45ms vs 中位 5~7ms）会让 Δv/Δt 飙到几百 m/s²，把 RMS 拉爆，属度量假象。
    ///      主判据要用**屏幕漂移**：卡顿同时推移角色与相机，相对位置不变，
    ///      所以屏幕漂移天然与帧率、卡顿无关。加速度 RMS 保留为辅助诊断，
    ///      但必须剔除卡顿帧对之后再判定。
    ///   7. "有没有瞬移"不能用逐帧位移绝对值判 —— 必须除以该帧 dt 换成速度，
    ///      且要**剔除卡顿帧**（长帧位移天然更大）。真实瞬移是"正常帧里出现超大速度"。
    ///   8. 相机震动会污染 eulerAngles —— 震屏直接往 transform 上加随机旋转，
    ///      会让"偏航波动"飙到十几度，看着像镜头不稳。要看相机稳定性就读
    ///      ThirdPersonCamera 的**逻辑** yaw/pitch，而不是 transform.eulerAngles。
    /// </summary>
    public static class PlaytestHarness
    {
        private const string ReportDirRelative = "Tools/reports";

        /// <summary>每个阶段的起始位置（场地中央偏南，给前进方向留足空间）。</summary>
        private static readonly Vector3 StageAnchor = new Vector3(0f, 0.05f, -14f);

        /// <summary>复位后等待相机与动画收敛的时间（不计入采样）。</summary>
        private const float SettleSeconds = 0.7f;

        /// <summary>角色脚底到头顶的世界高度（实测 CharacterController.height = 1.92）。</summary>
        private const float CharacterHeight = 1.88f;

        /// <summary>
        /// 非冲刺状态下的"瞬移"判据（m/s）。合法速度上限：冲刺 14、三段前冲峰值 2.6×2/0.42≈12.4。
        /// 取 18 留出余量；真实的代码瞬移是"一帧吃掉整段位移"，速度会飙到上百。
        /// </summary>
        private const float TeleportSpeedThreshold = 18f;

        /// <summary>单帧位移超过这个值（非冲刺、非卡顿帧）就记一次可疑。</summary>
        private const float TeleportDisplacementThreshold = 0.60f;

        private static readonly string[] kBaseStateNames =
        {
            // 顺序即优先级。S2.1 起走路状态改名为 Walk（旧名 Move / Run 保留兼容，方便读历史报告）。
            // 漏掉新名字会让所有"走的是哪个状态"的断言看到 Other —— 那是最容易被误读成"功能坏了"的假失败。
            "Idle", "Walk", "Run", "Move", "Dash",
            "Atk1", "Atk1Rec", "Atk2", "Atk2Rec", "Atk3"
        };

        // ==================================================================
        // 数据结构
        // ==================================================================

        private struct Sample
        {
            public float dt;
            public Vector3 playerPos;
            public float playerY;
            public bool grounded;
            public Vector3 camPos;
            public float camYawRaw;
            public float camPitchRaw;
            public Vector2 viewport;
            public float speed;         // 实际速度
            public float desiredSpeed;  // 期望速度
            public string anim;
            public bool invincible;
            public bool dashing;
            public Vector3 camForward;
            public Vector3 camRight;
        }

        private class StageResult
        {
            public string name;
            public int samples;
            public float duration;
            public float distance;
            public float avgSpeed;
            public float maxSpeed;
            public float avgDesiredSpeed;
            public float steadySpeedAvg;     // 稳态窗口内的实际速度（与相机跟速同窗口，可直接比）
            public float blockedRatio;       // 受阻帧占比（期望>1 而实际<40%期望）
            public Vector3 netDisplacement;

            public Vector2 minViewport = new Vector2(float.MaxValue, float.MaxValue);
            public Vector2 maxViewport = new Vector2(float.MinValue, float.MinValue);

            public float camYawRange;
            public float camPitchRange;
            public float camSpeedAvg;
            public float camSpeedMax;
            public float camAccelRms;       // 原始值（含卡顿帧），仅作诊断
            public float camAccelRmsClean;  // 剔除卡顿帧对后的值，用于判定
            public int accelExcludedPairs;  // 被剔除的帧对数
            public int hitchFrames;
            public float maxFrameDt;

            public float playerYRange;
            public float groundedRatio;

            public float expectDot;
            public string animStates = "";
            public bool sawDash;
            public bool sawInvincible;
        }

        // ---------------- S2 战斗验收 ----------------

        private class CombatSample
        {
            public float t;              // 相对阶段起点（不含复位静置）
            public float dt;
            public Vector3 pos;
            public float speed;
            public float desiredSpeed;
            public Vector3 desiredVel;   // 期望速度向量（攻击前冲的积分源）
            public string anim;
            public ActionPhase phase;
            public int comboStep;
            public bool cancelWindow;
            public float motionSpeed;    // Animator 参数 MotionSpeed
            // 当前状态的动画片段时长。用来把「播放倍率」换算成真正的**步频**：
            // 步频 = 2 步/循环 ÷（片段时长 ÷ 播放倍率）。两段片段长度不同，只比倍率是错的。
            public float clipLen;
            public float upperWeight;    // 第 1 层及以后的**附加层总权重**（本版设计应为 0）
            public float torsoLeanDeg;   // 躯干倾角：正 = 前倾，负 = 后仰（问题 1「仰着身子走」的量化指标）
            public float bladeSpeed;     // 刀刃（右手骨挂点）当前线速度 m/s
            public float emitStartSpeed; // 本段挥砍里拖尾首次开始发射时的刀刃速度（-1 = 尚未发射）
            public int arcActive;        // 同屏存活的弧光实例数
            public int trailVerts;       // 拖尾顶点数
            public bool trailEmitting;
            public bool shaking;         // 相机是否正在震屏
            public bool attackReq;
            public bool dashReq;
            public Vector2 viewport;
            public float screenHeight;   // 角色在视口里的高度占比
            public float camDistance;
            public float camFov;
            public float camYaw;         // 逻辑偏航（不含震屏扰动）
            public float camPitch;
            public float phaseTime;      // PlayerController.PhaseTime
            public bool grounded;
        }

        /// <summary>单段连击的位移学证据 —— 用来证明"前冲是按曲线做出来的，不是凭空窜"。</summary>
        private class StepReport
        {
            public int step;
            public float onsetT;
            public float lungeClampDistance;  // 该段设定位移
            public float lungeDuration;       // 该段设定位移时长
            public float lungeFast;           // [起点, 起点+lungeDuration+0.05] 内的位移
            public float lungeTotal;          // [起点, 起点+0.75] 内的位移
            public float t90;                 // 位移达到 lungeTotal 的 90% 所需时间
            public float peakSpeed;           // 该窗口内最大帧速度（非冲刺）
            public float peakDesired;         // 该窗口内最大「期望速度」—— 前冲曲线是否真被施加
            public int attackFrames;          // 该窗口内处于 Attack 阶段的帧数
            public float onsetPhaseTime;      // 段位切到该段那一刻的 PhaseTime（应为 ~0）
        }

        private class CombatResult
        {
            public string name;
            public int samples;
            public float duration;
            public float distance;
            public Vector3 netDisplacement;
            public float avgSpeed;
            public float maxSpeed;
            public float playerYRange;
            public float groundedRatio;

            public Vector2 minViewport = new Vector2(float.MaxValue, float.MaxValue);
            public Vector2 maxViewport = new Vector2(float.MinValue, float.MinValue);
            public float screenHeightAvg;
            public int screenHeightSamples;

            public float camDistanceAvg, camDistanceMin = float.MaxValue, camDistanceMax;
            public float camFovAvg;
            public float camYawRange, camPitchRange;

            public float upperWeightAvg, upperWeightMin = float.MaxValue, upperWeightMax;
            public float motionSpeedAvg, motionSpeedMin = float.MaxValue, motionSpeedMax;
            public float impliedGroundSpeedAvg;
            public int impliedSpeedSamples;
            // 真实步频（步/秒）：跨片段可比。走路约 2.1、慢跑 2.6~2.9、冲刺 4.0~4.4 是人类的范围。
            public float stepsPerSecAvg;

            // 问题 1「仰着身子走」：躯干倾角（正前倾 / 负后仰）
            public float torsoLeanAvg, torsoLeanMin = float.MaxValue, torsoLeanMax = float.MinValue;
            // 问题 5「刀没速度就出刀光」：刀刃速度峰值 + 拖尾首次发射时的刀刃速度（取各次挥砍里的最小值）
            public float bladeSpeedPeak;
            public float emitStartSpeedMin = float.MaxValue;
            public int emitStartSamples;
            // 问题 3「没有跑步动作」：本阶段实际出现过的动画状态集合
            public string animStateSet = "";

            public int comboStepMax;
            public bool sawCancelWindow;
            public float cancelWindowFirstT = -1f;
            public string animStates = "";
            public string animTimeline = "";
            public bool sawAttack, sawDash, sawInvincible;

            public int swingDelta, arcSpawnDelta, shakeDelta;
            public int arcActivePeak;
            public int trailActiveFrames;
            public int trailVertexPeak;

            public int teleportFrames;
            public float maxCleanFrameSpeed;
            public float maxFrameDisplacement;
            public float maxRawFrameSpeed;
            public int hitchFrames;

            public List<StepReport> steps = new List<StepReport>();
        }

        private class TickIntent
        {
            public bool attack;
            public bool dash;
        }

        private delegate void CombatTick(PlayerController ctl, float t, int frame, TickIntent want);

        /// <summary>各阶段开始时的特效计数基线 —— 判定要用增量，不能用绝对值。</summary>
        private class VfxBaseline
        {
            public int swing, arc, shake;
        }

        /// <summary>把一长串参数收成一个句柄，避免 RunCombatStage 有十个参数。</summary>
        private class CombatContext
        {
            public PlayerController ctl;
            public Animator anim;
            public Camera cam;
            public ThirdPersonCamera rig;
            public SwordVfx vfx;
            public WeaponHandPose pose;
            /// <summary>
            /// 战斗全程观察到的**最大**握拳度（右 / 左）。尺度无关：中指指尖到腕 ÷ 食指根到小指根距离。
            /// 五指摊开 ≈ 2.2，握成拳 ≈ 1.2。用来抓「攻击时手是张开的」这个回归。
            /// </summary>
            public float maxFistRatioRight = -1f, maxFistRatioLeft = -1f;
            public GameObject playerGo;
            public int upperLayerIndex = 1;
            /// <summary>躯干倾角用的两根骨骼（髋、胸/头）。没有它们就没法量化「仰着身子走」。</summary>
            public Transform hips, chest;
            /// <summary>
            /// 躯干倾角**测量可用**。骨头解析失败时 TorsoLeanDeg 只能返回 0，
            /// 而断言是「&gt; -8°」—— 0 会无条件通过，于是测量死了却依然报"通过"。
            /// 换了 KayKit 骨架后就踩到过这个坑（Rig_Medium 没有 chest 骨），
            /// 所以断言必须一并看这个标志位，不可用就判失败而不是假通过。
            /// </summary>
            public bool torsoLeanAvailable;
        }

        // ==================================================================
        // 一次进 Play 跑完全部验收
        // ==================================================================

        public static IEnumerator RunAll()
        {
            yield return S1LocomotionFlow();
            yield return S2CombatFlow();
            yield return AudioSmokeCheck();
            Debug.Log("[PlaytestHarness] 全部验收流程结束");
        }

        /// <summary>只跑姿态体检（问题 1「仰着身子」/ 问题 6「脚穿地」的快速回归）。</summary>
        public static IEnumerator PoseScanFlow()
        {
            var ctx = ResolveContext();
            if (ctx == null) { WriteReport("S2_pose", "<错误> 场景里找不到 Player / Animator / Main Camera"); yield break; }
            yield return PoseAndGroundScan(ctx);
        }

        /// <summary>
        /// 取消窗口诊断：单次攻击后逐帧记录「动画状态 / normalizedTime / 取消窗口 / 段位 / 阶段」。
        /// 断言只说"窗口没开"，不给原因；这个把状态机的归一化时间直接摊开，
        /// 一眼能看出是没进后摇状态、还是进去了但时间没走到窗口阈值。
        /// </summary>
        public static IEnumerator CancelWindowDiag()
        {
            var sb = new StringBuilder();
            var ctx = ResolveContext();
            if (ctx == null) { WriteReport("S2_cancel", "<错误> 场景里找不到 Player"); yield break; }

            var ctl = ctx.ctl;
            var anim = ctx.anim;
            string[] names = { "Idle", "Walk", "Run", "Move", "Dash", "Atk1", "Atk1Rec", "Atk2", "Atk2Rec", "Atk3" };

            sb.AppendLine("======================================================================");
            sb.AppendLine("取消窗口诊断（单次攻击 -> 后摇）");
            sb.AppendLine("生成时间 " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("======================================================================");
            sb.AppendLine("comboRecCancelStart = " + Join(ctl.comboRecCancelStart));
            sb.AppendLine("attackInputBuffer   = " + F(ctl.attackInputBuffer) + " s");
            sb.AppendLine("Atk1/Atk2/Atk3 片段时长 = " + Join(ctl.comboSwingDuration) + " s（逻辑侧）");
            sb.AppendLine();

            ResetPlayer(ctx.playerGo, ctl);
            // 关键：输入注入全部由 _overrideActive 门控（ReadInput 里 if (_overrideActive) 才读 _overrideMove/_overrideAttack）。
            // 不先 BeginInputOverride 的话，SetInjectedMove / RequestInjectedAttack 都会被 ReadInput 直接忽略，
            // 表现就是"攻击压根没触发、全程 Idle"。
            ctl.BeginInputOverride();
            ctl.SetInjectedMove(Vector2.zero, false);
            yield return null;
            yield return null;   // 让复位沉一帧，避免攻击触发和复位同帧相互打断

            ctl.RequestInjectedAttack();
            float t0 = Time.time;
            string lastKey = "";
            int frames = 0;
            while (Time.time - t0 < 2.6f)
            {
                frames++;
                var st = anim.GetCurrentAnimatorStateInfo(0);
                string nm = CurrentState(anim);
                string key = nm + "|" + ctl.IsCancelWindowOpen + "|" + ctl.ComboStep + "|" + ctl.Phase;
                if (key != lastKey)
                {
                    lastKey = key;
                    sb.AppendLine(string.Format("t={0,6:F3}  状态={1,-8} nt={2,6:F3}  取消窗口={3,-5}  段位={4}  阶段={5}  AttackStateSeen={6}",
                        Time.time - t0, nm, st.normalizedTime, ctl.IsCancelWindowOpen, ctl.ComboStep, ctl.Phase, ctl.AttackStateSeen));
                }
                yield return null;
            }
            sb.AppendLine("(观察 " + F(Time.time - t0) + " s / " + frames + " 帧)");

            WriteReport("S2_cancel", sb.ToString());
            ctl.EndInputOverride();
            ResetPlayer(ctx.playerGo, ctl);
        }

        private static string Join(float[] a)
        {
            if (a == null) return "(null)";
            var parts = new string[a.Length];
            for (int i = 0; i < a.Length; i++) parts[i] = F(a[i]);
            return string.Join(", ", parts);
        }

        // ==================================================================
        // 主流程：Sprint 1 移动与相机验收
        // ==================================================================

        public static IEnumerator S1LocomotionFlow()
        {
            var sb = new StringBuilder();
            var stages = new List<StageResult>();

            var playerGo = GameObject.Find("Player");
            if (playerGo == null) { WriteReport("S1", "<错误> 场景里找不到 Player 对象"); yield break; }

            var ctl = playerGo.GetComponent<PlayerController>();
            var anim = playerGo.GetComponentInChildren<Animator>();
            var cam = Camera.main;
            if (ctl == null || anim == null || cam == null)
            {
                WriteReport("S1", "<错误> 缺少 PlayerController / Animator / Main Camera");
                yield break;
            }

            Vector3 startPos = playerGo.transform.position;
            Quaternion startRot = playerGo.transform.rotation;

            float playT0 = Time.time;
            int playF0 = Time.frameCount;

            ctl.BeginInputOverride();

            yield return RunStage(stages, ctl, anim, cam, playerGo, "A 待机", 1.0f, Vector2.zero, false, false);
            yield return RunStage(stages, ctl, anim, cam, playerGo, "B 步行(前)", 2.5f, Vector2.up, false, false);
            yield return RunStage(stages, ctl, anim, cam, playerGo, "C 疾跑(前)", 2.5f, Vector2.up, true, false);
            yield return RunStage(stages, ctl, anim, cam, playerGo, "D 平移(左)", 1.5f, Vector2.left, false, false);
            yield return RunStage(stages, ctl, anim, cam, playerGo, "E 冲刺", 1.0f, Vector2.zero, false, true);
            yield return RunStage(stages, ctl, anim, cam, playerGo, "F 复位待机", 1.0f, Vector2.zero, false, false);

            ctl.EndInputOverride();

            playerGo.transform.position = startPos;
            playerGo.transform.rotation = startRot;

            float elapsed = Time.time - playT0;
            int frames = Time.frameCount - playF0;

            // ---------- 报告头 ----------
            sb.AppendLine("Sprint 1 自动化验收报告");
            sb.AppendLine("生成时间: " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            sb.AppendLine("Unity    : " + Application.unityVersion + "   平台: " + Application.platform);
            sb.AppendLine("分辨率   : " + Screen.width + "x" + Screen.height
                + "   本次实测平均帧率: " + F(frames / Mathf.Max(elapsed, 0.001f)) + " fps"
                + "   (" + frames + " 帧 / " + F(elapsed) + "s)");
            sb.AppendLine("测试约定 : 每阶段复位到 " + V(StageAnchor) + "，复位后静置 " + N(SettleSeconds) + "s 再采样");
            sb.AppendLine();
            sb.AppendLine("PlayerController 设定值:");
            sb.AppendLine("   walkSpeed=" + N(ctl.walkSpeed) + "  runSpeed=" + N(ctl.runSpeed)
                + "  acceleration=" + N(ctl.acceleration) + "  deceleration=" + N(ctl.deceleration));
            sb.AppendLine("   dashSpeed=" + N(ctl.dashSpeed) + "  dashDuration=" + N(ctl.dashDuration)
                + "  dashCooldown=" + N(ctl.dashCooldown));
            sb.AppendLine("   步伐同步: 参考速度 走=" + N(ctl.walkRefSpeed) + " 跑=" + N(ctl.runRefSpeed)
                + "  播放速度上限=" + N(ctl.motionSpeedMax));
            sb.AppendLine();

            sb.AppendLine("======================================================================");
            sb.AppendLine("分阶段数据");
            sb.AppendLine("======================================================================");
            foreach (var s in stages)
            {
                sb.AppendLine();
                sb.AppendLine("[" + s.name + "]  " + s.samples + " 帧 / " + F(s.duration) + "s");
                sb.AppendLine("   位移净距 : " + F(s.netDisplacement.magnitude) + " m   方向 " + V(s.netDisplacement));
                sb.AppendLine("   路径长度 : " + F(s.distance) + " m");
                sb.AppendLine("   实际速度 : 平均 " + F(s.avgSpeed) + "  峰值 " + F(s.maxSpeed) + " m/s");
                sb.AppendLine("   期望速度 : 平均 " + F(s.avgDesiredSpeed) + " m/s"
                    + (s.blockedRatio > 0.02f ? "   [!] 受阻帧占比 " + F(s.blockedRatio * 100f) + "%（实际速度被碰撞压制）" : ""));
                sb.AppendLine("   方向一致性: 实际位移·预期方向 点积 " + F(s.expectDot) + "  (1.0 = 完全一致)");
                sb.AppendLine("   屏幕位置 : x[" + F(s.minViewport.x) + "~" + F(s.maxViewport.x)
                    + "] y[" + F(s.minViewport.y) + "~" + F(s.maxViewport.y) + "]  0.5 = 正中"
                    + "  漂移 " + F(s.maxViewport.x - s.minViewport.x) + " x " + F(s.maxViewport.y - s.minViewport.y));
                sb.AppendLine("   相机朝向 : 偏航波动 " + F(s.camYawRange) + "°  俯仰波动 " + F(s.camPitchRange) + "°  (已解卷绕)");
                sb.AppendLine("   相机跟速 : 平均 " + F(s.camSpeedAvg) + "  峰值 " + F(s.camSpeedMax) + " m/s");
                sb.AppendLine("   镜头抖动 : 加速度 RMS " + F(s.camAccelRmsClean) + " m/s²  <== 判定值（已剔除卡顿帧对）");
                sb.AppendLine("              原始 " + F(s.camAccelRms) + " m/s²，剔除 " + s.accelExcludedPairs + " 对卡顿帧（仅诊断，不参与判定）");
                sb.AppendLine("   帧稳定性 : 卡顿帧 " + s.hitchFrames + " 个，最长帧 " + F(s.maxFrameDt * 1000f) + " ms");
                sb.AppendLine("   角色贴地 : Y 波动 " + F(s.playerYRange) + " m，着地帧占比 " + F(s.groundedRatio * 100f) + "%");
                sb.AppendLine("   动画状态 : " + s.animStates);
                if (s.sawDash) sb.AppendLine("   ★ 捕获到 Dash 状态");
                if (s.sawInvincible) sb.AppendLine("   ★ 捕获到无敌帧窗口");
            }

            // ---------- 判定 ----------
            sb.AppendLine();
            sb.AppendLine("======================================================================");
            sb.AppendLine("验收判定（M1：跑动顺滑、镜头不抖）");
            sb.AppendLine("======================================================================");

            var idle = Find(stages, "A 待机");
            var walk = Find(stages, "B 步行(前)");
            var run = Find(stages, "C 疾跑(前)");
            var strafe = Find(stages, "D 平移(左)");
            var dash = Find(stages, "E 冲刺");
            var back2idle = Find(stages, "F 复位待机");

            int pass = 0, fail = 0;
            System.Action<string, bool, string> check = (label, ok, detail) =>
            {
                if (ok) pass++; else fail++;
                sb.AppendLine("  " + (ok ? "[通过] " : "[未通过] ") + label + "   " + detail);
            };

            sb.AppendLine("-- 移动手感 --");
            if (walk != null)
                check("步行速度接近设定 " + N(ctl.walkSpeed),
                    Mathf.Abs(walk.steadySpeedAvg - ctl.walkSpeed) < ctl.walkSpeed * 0.2f,
                    "实测 " + F(walk.steadySpeedAvg));
            if (run != null)
                check("疾跑速度接近设定 " + N(ctl.runSpeed),
                    Mathf.Abs(run.steadySpeedAvg - ctl.runSpeed) < ctl.runSpeed * 0.2f,
                    "实测 " + F(run.steadySpeedAvg));
            if (run != null && walk != null)
                check("疾跑确实快于步行", run.steadySpeedAvg > walk.steadySpeedAvg * 1.15f,
                    F(run.steadySpeedAvg) + " vs " + F(walk.steadySpeedAvg));
            if (idle != null)
                check("待机无漂移", idle.distance < 0.05f, F(idle.distance) + " m");
            if (back2idle != null)
                check("冲刺后能回到静止", back2idle.avgSpeed < 0.1f, F(back2idle.avgSpeed) + " m/s");
            if (run != null)
                check("疾跑帧率 ≥ 55（编辑器内含录制开销）",
                    (run.samples / Mathf.Max(run.duration, 0.001f)) >= 55f,
                    F(run.samples / Mathf.Max(run.duration, 0.001f)) + " fps");

            sb.AppendLine();
            sb.AppendLine("-- 输入映射（相机相对） --");
            if (walk != null)
                check("前进 = 相机前方", walk.expectDot > 0.95f, "点积 " + F(walk.expectDot));
            if (strafe != null)
                check("左移 = 相机左方", strafe.expectDot > 0.95f, "点积 " + F(strafe.expectDot));

            sb.AppendLine();
            sb.AppendLine("-- 无碰撞阻挡（验证场地空间充足、未被院墙顶住） --");
            foreach (var s in stages)
                check("[" + s.name + "] 受阻帧占比 < 2%",
                    s.blockedRatio < 0.02f, F(s.blockedRatio * 100f) + "%");

            sb.AppendLine();
            sb.AppendLine("-- 相机稳定性 --");
            if (strafe != null)
                check("相机不随角色转向（平移阶段角色转了约 90°，相机偏航波动应极小）",
                    strafe.camYawRange < 8f, "波动 " + F(strafe.camYawRange) + "°");
            if (run != null)
                check("疾跑时角色锁定在画面内（屏幕漂移 < 0.08）—— 镜头不抖的主判据（卡顿无关）",
                    (run.maxViewport.x - run.minViewport.x) < 0.08f
                    && (run.maxViewport.y - run.minViewport.y) < 0.08f,
                    "x漂移 " + F(run.maxViewport.x - run.minViewport.x)
                    + " y漂移 " + F(run.maxViewport.y - run.minViewport.y));
            if (run != null)
                check("疾跑时相机加速度 RMS < 40 m/s²（已剔除卡顿帧对）",
                    run.camAccelRmsClean < 40f,
                    "RMS " + F(run.camAccelRmsClean)
                    + "（剔除 " + run.accelExcludedPairs + " 对卡顿帧，原始 " + F(run.camAccelRms) + "）");
            if (run != null)
                check("相机跟速与角色实际速度匹配（差 < 1.5 m/s）",
                    Mathf.Abs(run.camSpeedAvg - run.steadySpeedAvg) < 1.5f,
                    "相机 " + F(run.camSpeedAvg) + " vs 角色 " + F(run.steadySpeedAvg));

            sb.AppendLine();
            sb.AppendLine("-- 角色贴地 --");
            foreach (var s in stages)
                check("[" + s.name + "] 着地帧占比 ≥ 95%",
                    s.groundedRatio >= 0.95f, F(s.groundedRatio * 100f) + "%，Y 波动 " + F(s.playerYRange) + " m");

            sb.AppendLine();
            sb.AppendLine("-- 冲刺与动画 --");
            if (dash != null)
                check("冲刺产生显著位移（> 2m）", dash.netDisplacement.magnitude > 2f,
                    F(dash.netDisplacement.magnitude) + " m");
            if (dash != null)
                check("冲刺峰值速度达到设定 " + N(ctl.dashSpeed),
                    dash.maxSpeed > ctl.dashSpeed * 0.9f, F(dash.maxSpeed) + " m/s");
            if (dash != null)
                check("冲刺期间出现无敌帧", dash.sawInvincible, dash.sawInvincible ? "已捕获" : "未捕获");
            if (idle != null)
                check("待机播放 Idle 状态", idle.animStates.Contains("Idle"), idle.animStates);
            if (walk != null)
                // 状态名换过两轮：S1 期叫 Run，S2 期叫 Move，S2.1 起换成正经走路动画 Walk。
                // 三个名字都接受 —— 断言要证的是"步行时进的是移动状态"，不是"状态恰好叫什么"。
                check("步行切到移动状态（Walk；历史名 Move / Run）",
                    walk.animStates.Contains("Walk") || walk.animStates.Contains("Move")
                    || walk.animStates.Contains("Run"), walk.animStates);
            if (dash != null)
                check("冲刺切到 Dash 状态", dash.sawDash, dash.animStates);
            if (back2idle != null)
                check("停止后回到 Idle 状态", back2idle.animStates.EndsWith("Idle"), back2idle.animStates);

            sb.AppendLine();
            sb.AppendLine("----------------------------------------------------------------------");
            sb.AppendLine("结果: 通过 " + pass + " 项 / 未通过 " + fail + " 项");
            sb.AppendLine(fail == 0 ? ">>> M1 达成：跑动顺滑、镜头不抖" : ">>> 存在未通过项，需处理");
            sb.AppendLine("----------------------------------------------------------------------");

            int hitchTotal = 0;
            foreach (var s in stages) hitchTotal += s.hitchFrames;
            if (hitchTotal > 0)
                sb.AppendLine("备注: 本次共 " + hitchTotal + " 个卡顿帧（帧长 > 中位数 3 倍），"
                    + "主要来自编辑器内 MP4 录制开销；相机稳定性以屏幕空间漂移为判据，不受其影响。");

            WriteReport("S1", sb.ToString());
        }

        // ==================================================================
        // 主流程：Sprint 2 战斗手感验收
        // ==================================================================

        public static IEnumerator S2CombatFlow()
        {
            var sb = new StringBuilder();
            var stages = new List<CombatResult>();

            var ctx = ResolveContext();
            if (ctx == null) { WriteReport("S2", "<错误> 场景里找不到 Player / PlayerController / Animator / Main Camera"); yield break; }

            Vector3 startPos = ctx.playerGo.transform.position;
            Quaternion startRot = ctx.playerGo.transform.rotation;
            float startYaw = ctx.rig != null ? ctx.rig.yaw : 0f;
            float startPitch = ctx.rig != null ? ctx.rig.pitch : 0f;

            float playT0 = Time.time;
            int playF0 = Time.frameCount;

            ctx.ctl.BeginInputOverride();
            if (ctx.rig != null) ctx.rig.SetMouseLookEnabled(false);

            // ---- 姿态体检（问题 1 躯干后仰 / 问题 6 攻击时脚穿地）----
            // 必须放在所有战斗阶段之前：它会把角色"冻结"到每个动画状态的各个时刻去量，
            // 之后 ResetPlayer 复位，不影响后续采样。
            yield return PoseAndGroundScan(ctx);

            // ---- 移动 / 步伐同步 / 上下半身 ----
            yield return RunCombatStage(stages, ctx, "A 待机基线", 1.2f, Vector2.zero, false, null);
            yield return RunCombatStage(stages, ctx, "B 步行(前)", 2.5f, Vector2.up, false, null);
            yield return RunCombatStage(stages, ctx, "C 疾跑(前)", 2.5f, Vector2.up, true, null);

            // ---- 冲刺（翻滚）：问题 4 的专用段，顺带验证冲刺不再"挥着剑滑行" ----
            yield return RunCombatStage(stages, ctx, "I 冲刺(翻滚)", 1.6f, Vector2.up, true,
                (c, t, f, w) => { if (f == 1) w.dash = true; });

            // ---- 单段挥砍：特效 / 震屏 / 后摇 ----
            yield return RunCombatStage(stages, ctx, "D 单段挥砍", 1.6f, Vector2.zero, false,
                (c, t, f, w) => { if (f == 0) w.attack = true; });

            // ---- 三段连击：连打，看推进与总位移 ----
            float lastMash = -9f;
            yield return RunCombatStage(stages, ctx, "E 三段连击", 3.4f, Vector2.zero, false,
                (c, t, f, w) =>
                {
                    if (c.ComboStep >= 3) return;
                    bool canPress = c.Phase != ActionPhase.Attack || c.IsCancelWindowOpen;
                    if (canPress && t - lastMash > 0.10f) { lastMash = t; w.attack = true; }
                });

            // ---- 后摇中途再按（延迟 0.25s）：专测"后摇里再按会不会凭空位移" ----
            bool pressed2 = false;
            float windowAt = -1f;
            yield return RunCombatStage(stages, ctx, "F 后摇取消(延迟再按)", 2.2f, Vector2.zero, false,
                (c, t, f, w) =>
                {
                    if (f == 0) w.attack = true;
                    if (pressed2) return;
                    if (c.IsCancelWindowOpen && windowAt < 0f) windowAt = t;
                    if (windowAt >= 0f && t - windowAt >= 0.25f) { pressed2 = true; w.attack = true; }
                });

            // ---- 后摇中途冲刺取消 ----
            bool dashed2 = false;
            float windowAt2 = -1f;
            yield return RunCombatStage(stages, ctx, "G 后摇取消(冲刺)", 1.8f, Vector2.zero, false,
                (c, t, f, w) =>
                {
                    if (f == 0) w.attack = true;
                    if (dashed2) return;
                    if (c.IsCancelWindowOpen && windowAt2 < 0f) windowAt2 = t;
                    if (windowAt2 >= 0f && t - windowAt2 >= 0.06f) { dashed2 = true; w.dash = true; }
                });

            // ---- 收招静止：确认没有残留滑行 ----
            yield return RunCombatStage(stages, ctx, "H 收招静止", 1.6f, Vector2.zero, false, null);

            ctx.ctl.EndInputOverride();
            if (ctx.rig != null)
            {
                ctx.rig.SetMouseLookEnabled(true);
                ctx.rig.yaw = startYaw;
                ctx.rig.pitch = startPitch;
            }
            ctx.playerGo.transform.position = startPos;
            ctx.playerGo.transform.rotation = startRot;

            float elapsed = Time.time - playT0;
            int frames = Time.frameCount - playF0;

            // ---------- 报告头 ----------
            sb.AppendLine("Sprint 2 战斗手感自动化验收报告");
            sb.AppendLine("生成时间: " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            sb.AppendLine("Unity    : " + Application.unityVersion + "   平台: " + Application.platform);
            sb.AppendLine("分辨率   : " + Screen.width + "x" + Screen.height
                + "   本次实测平均帧率: " + F(frames / Mathf.Max(elapsed, 0.001f)) + " fps"
                + "   (" + frames + " 帧 / " + F(elapsed) + "s)");
            sb.AppendLine("测试约定 : 每阶段复位到 " + V(StageAnchor) + "，复位后静置 " + N(SettleSeconds) + "s 再采样");
            sb.AppendLine();

            var ctl = ctx.ctl;
            sb.AppendLine("PlayerController 设定值:");
            sb.AppendLine("   移动: walk=" + N(ctl.walkSpeed) + " run=" + N(ctl.runSpeed)
                + " accel=" + N(ctl.acceleration) + " decel=" + N(ctl.deceleration)
                + "  Walk<->Run 阈值 " + N(ctl.runAnimExitSpeed) + "~" + N(ctl.runAnimEnterSpeed));
            sb.AppendLine("   步伐同步: 参考速度 走=" + N(ctl.walkRefSpeed) + " 跑=" + N(ctl.runRefSpeed)
                + " 播放速度区间=[" + N(ctl.motionSpeedMin) + "~" + N(ctl.motionSpeedMax) + "]"
                + "   → 步行需 " + N(ctl.walkSpeed / ctl.walkRefSpeed)
                + " 倍，跑步需 " + N(ctl.runSpeed / ctl.runRefSpeed) + " 倍");
            sb.AppendLine("   冲刺: " + N(ctl.dashSpeed) + " m/s / " + N(ctl.dashDuration)
                + "s，无敌 " + N(ctl.dashInvincibleWindow) + "s，冷却 " + N(ctl.dashCooldown) + "s");
            sb.AppendLine("   三段连击位移 : [" + JoinFloats(ctl.comboLungeDistance) + "] m");
            sb.AppendLine("   三段位移时长 : [" + JoinFloats(ctl.comboLungeDuration) + "] s");
            sb.AppendLine("   三段挥砍时长 : [" + JoinFloats(ctl.comboSwingDuration) + "] s");
            sb.AppendLine("   三段命中时刻 : [" + JoinFloats(ctl.comboHitTime) + "] s");
            sb.AppendLine("   后摇取消起点 : [" + JoinFloats(ctl.comboRecCancelStart) + "] (归一化时间)");
            sb.AppendLine("   输入缓冲     : " + N(ctl.attackInputBuffer) + "s");
            sb.AppendLine();
            sb.AppendLine("相机 : " + (ctx.rig != null
                ? "distance=" + N(ctx.rig.distance) + "m  pitch=" + N(ctx.rig.pitch)
                  + "°  baseFov=" + N(ctx.rig.baseFov) + "  避障遮罩=" + ctx.rig.collisionMask.value
                : "(未挂 ThirdPersonCamera)"));
            sb.AppendLine("刀光 : " + (ctx.vfx != null
                ? "Shader=" + (ctx.vfx.inkShader != null ? ctx.vfx.inkShader.name : "(Shader.Find)")
                  + "  弧光半径=" + N(ctx.vfx.arcRadius) + " 弧长=" + N(ctx.vfx.arcSweepDeg)
                  + "° 寿命=" + N(ctx.vfx.arcLifetime) + "s  占位刀身=" + ctx.vfx.placeholderBlade
                : "(未挂 SwordVfx)"));
            sb.AppendLine();

            sb.AppendLine("======================================================================");
            sb.AppendLine("分阶段数据");
            sb.AppendLine("======================================================================");
            foreach (var s in stages) AppendCombatStage(sb, s, ctl);

            // ---------- 判定 ----------
            sb.AppendLine();
            sb.AppendLine("======================================================================");
            sb.AppendLine("验收判定（M2：连击有反馈、后摇可取消、镜头贴近、无滑步）");
            sb.AppendLine("======================================================================");

            var idle = FindC(stages, "A 待机基线");
            var walk = FindC(stages, "B 步行(前)");
            var runC = FindC(stages, "C 疾跑(前)");
            var one = FindC(stages, "D 单段挥砍");
            var combo = FindC(stages, "E 三段连击");
            var recancel = FindC(stages, "F 后摇取消(延迟再按)");
            var dashcancel = FindC(stages, "G 后摇取消(冲刺)");
            var settle = FindC(stages, "H 收招静止");
            var dashStage = FindC(stages, "I 冲刺(翻滚)");

            int pass = 0, fail = 0;
            System.Action<string, bool, string> check = (label, ok, detail) =>
            {
                if (ok) pass++; else fail++;
                sb.AppendLine("  " + (ok ? "[通过] " : "[未通过] ") + label + "   " + detail);
            };

            sb.AppendLine("-- 问题 5「移动没跟上脚步动画」：步伐同步 --");
            if (walk != null)
                check("[B 步行] 播放速度随实际速度变化，不是固定值",
                    walk.motionSpeedMax - walk.motionSpeedMin > 0.15f,
                    "播放速度跨度 " + F(walk.motionSpeedMin) + "~" + F(walk.motionSpeedMax));
            var locoNames = new string[] { "B 步行", "C 疾跑" };
            var locoResults = new CombatResult[] { walk, runC };
            // 走路 / 跑步是两段不同的片段、各自的地面速度不同，参考值必须分开核对。
            var locoRefs = new float[] { ctl.walkRefSpeed, ctl.runRefSpeed };
            for (int li = 0; li < locoNames.Length; li++)
            {
                var s = locoResults[li];
                if (s == null) continue;
                float refSpeed = Mathf.Max(locoRefs[li], 1e-3f);
                string tag = "[" + locoNames[li] + "]";
                check(tag + " 播放速度未被上限截断（截断 = 残余滑步）",
                    s.motionSpeedMax < ctl.motionSpeedMax * 0.98f,
                    "峰值播放速度 " + F(s.motionSpeedMax) + " / 上限 " + F(ctl.motionSpeedMax));
                check(tag + " 播放速度 = 实际速度 / 参考速度 " + N(refSpeed),
                    s.impliedSpeedSamples > 0 && Mathf.Abs(s.impliedGroundSpeedAvg - refSpeed) < refSpeed * 0.10f,
                    "反推片段速度 " + F(s.impliedGroundSpeedAvg) + " m/s（样本 " + s.impliedSpeedSamples + "）");
            }
            if (walk != null && runC != null)
                // 必须比**步频**，不能比 playback 倍率：走路/跑步是两段不同长度的片段，
                // 倍率之间没有可比性（走 1.42× 摊到 1.33s 上只有 2.1 步/秒，
                // 跑 0.94× 摊到 0.60s 上却是 3.1 步/秒）。这正是本文件 152 行注释说的那件事。
                check("疾跑步频明显快于步行",
                    runC.stepsPerSecAvg > walk.stepsPerSecAvg * 1.15f,
                    F(runC.stepsPerSecAvg) + " vs " + F(walk.stepsPerSecAvg) + " 步/秒");
            // 上界：步频才是"腿看起来自然与否"的判据。没有上界的话，把 runSpeed/walkSpeed
            // 往上调就会让腿再次"原地倒腾"，而这正是前两轮返工的原因。
            // 真人数据：走路 ~2.0、慢跑 2.6~2.9、冲刺 4.0~4.4 步/秒。
            if (walk != null)
                check("[B 步行] 步频未冲出人走区间（≤ 3.0 步/秒）",
                    walk.stepsPerSecAvg <= 3.0f,
                    F(walk.stepsPerSecAvg) + " 步/秒");
            if (runC != null)
                check("[C 疾跑] 步频未冲出真人跑步区间（≤ 4.4 步/秒）",
                    runC.stepsPerSecAvg <= 4.4f,
                    F(runC.stepsPerSecAvg) + " 步/秒");

            sb.AppendLine();
            sb.AppendLine("-- 问题 1「走路仰着身子走」+ 问题 3「没有跑步动作」：换了正经的走路/跑步片段 --");
            if (walk != null)
            {
                check("[B 步行] 躯干不后仰（倾角 > -8°，正=前倾 / 负=后仰）",
                    ctx.torsoLeanAvailable && walk.torsoLeanAvg > -8f,
                    (ctx.torsoLeanAvailable ? "" : "[测量不可用：髋/胸骨未解析到] ")
                    + "平均 " + F(walk.torsoLeanAvg) + "°  区间["
                    + F(walk.torsoLeanMin) + "~" + F(walk.torsoLeanMax) + "]°");
                check("[B 步行] 走的是 Walk 状态（不再拿「抱物走 Move」当走路）",
                    walk.animStateSet.Contains("Walk"),
                    "本阶段动画状态集合: " + (walk.animStateSet.Length > 0 ? walk.animStateSet : "(空)"));
            }
            if (runC != null)
            {
                check("[C 疾跑] 跑的是独立的 Run 状态（此前根本没有跑步片段）",
                    runC.animStateSet.Contains("Run"),
                    "本阶段动画状态集合: " + (runC.animStateSet.Length > 0 ? runC.animStateSet : "(空)"));
                check("[C 疾跑] 躯干不后仰（倾角 > -8°）",
                    ctx.torsoLeanAvailable && runC.torsoLeanAvg > -8f,
                    (ctx.torsoLeanAvailable ? "" : "[测量不可用：髋/胸骨未解析到] ")
                    + "平均 " + F(runC.torsoLeanAvg) + "°  区间["
                    + F(runC.torsoLeanMin) + "~" + F(runC.torsoLeanMax) + "]°");
            }
            if (combo != null)
                check("[E 连击] 连打期间没有附加层在改姿势（问题 2 的根因已移除）",
                    combo.upperWeightMax < 0.001f,
                    "最大附加层权重 " + F(combo.upperWeightMax) + "（图层数应为 1）");

            sb.AppendLine();
            sb.AppendLine("-- 问题 4「右键动作很奇怪」：冲刺换成翻滚，且与冲刺时长对齐 --");
            if (dashStage != null)
            {
                check("[I 冲刺] 冲刺期间动画状态是 Dash",
                    dashStage.animStateSet.Contains("Dash"),
                    "本阶段动画状态集合: " + (dashStage.animStateSet.Length > 0 ? dashStage.animStateSet : "(空)"));
                check("[I 冲刺] 冲刺阶段含位移且无瞬移",
                    dashStage.distance > 1.5f && dashStage.teleportFrames == 0,
                    "位移 " + F(dashStage.distance) + "m，瞬移帧 " + dashStage.teleportFrames);
            }

            sb.AppendLine();
            sb.AppendLine("-- 问题 5「刀还没速度就出刀光」：拖尾按刀刃速度门控 --");
            if (ctx.vfx != null)
            {
                check("拖尾门控阈值已配置（> 0 m/s）", ctx.vfx.trailMinSpeed > 0f,
                    "进入 " + N(ctx.vfx.trailMinSpeed) + " m/s，维持 " + N(ctx.vfx.trailHoldMinSpeed) + " m/s");
                if (one != null)
                    check("[D 单段挥砍] 拖尾开始发射时刀刃**已经有速度** (≥ 阈值 90%)",
                        one.emitStartSamples > 0 && one.emitStartSpeedMin >= ctx.vfx.trailMinSpeed * 0.9f,
                        one.emitStartSamples > 0
                            ? "起播刀刃速度 " + F(one.emitStartSpeedMin) + " m/s / 阈值 " + N(ctx.vfx.trailMinSpeed)
                              + "（刀刃峰值 " + F(one.bladeSpeedPeak) + " m/s）"
                            : "本阶段没有观测到拖尾发射");
            }

            sb.AppendLine();
            sb.AppendLine("-- 问题 3「摄像机离人太远」：镜头距离与构图 --");
            if (ctx.rig != null && walk != null)
                check("相机距离收到 " + N(ctx.rig.distance) + "m（S1 用的 Cinemachine FollowOffset 折合 " + N(15.6f) + "m）",
                    Mathf.Abs(walk.camDistanceAvg - ctx.rig.distance) < 0.6f,
                    "实测平均 " + F(walk.camDistanceAvg) + "m，区间 " + F(walk.camDistanceMin) + "~" + F(walk.camDistanceMax));
            if (walk != null)
                check("角色占屏高 30%~60%（近距离第三人称构图）",
                    walk.screenHeightAvg > 0.30f && walk.screenHeightAvg < 0.60f,
                    "占屏高 " + F(walk.screenHeightAvg * 100f) + "%（" + walk.screenHeightSamples + " 帧有效）");
            if (runC != null)
                check("疾跑时角色仍锁定在画面内（屏幕漂移 < 0.10）",
                    (runC.maxViewport.x - runC.minViewport.x) < 0.10f
                    && (runC.maxViewport.y - runC.minViewport.y) < 0.10f,
                    "x漂移 " + F(runC.maxViewport.x - runC.minViewport.x)
                    + " y漂移 " + F(runC.maxViewport.y - runC.minViewport.y));
            if (walk != null)
                check("相机不跟角色转向（步行阶段偏航波动 < 8°）",
                    walk.camYawRange < 8f && walk.camPitchRange < 8f,
                    "偏航 " + F(walk.camYawRange) + "° 俯仰 " + F(walk.camPitchRange) + "°");

            sb.AppendLine();
            sb.AppendLine("-- 问题 4「攻击没有特效」：刀光 / 拖尾 / 震屏 --");
            if (ctx.vfx != null)
                check("右手骨骼解析成功（拖尾有挂点）", ctx.vfx.HasBladeAnchor,
                    ctx.vfx.HasBladeAnchor ? "已解析" : "未解析");
            if (ctx.vfx != null)
                check("手上武器已就位（不是空手挥空气）", ctx.vfx.HasWeapon,
                    ctx.vfx.WeaponName);
            if (ctx.pose != null)
                check("攻击全程两手都握着（不是五指摊开）",
                    ctx.maxFistRatioRight > 0f && ctx.maxFistRatioRight < 1.7f
                    && ctx.maxFistRatioLeft > 0f && ctx.maxFistRatioLeft < 1.7f,
                    "攻击帧握拳度峰值 右 " + F(ctx.maxFistRatioRight) + " / 左 " + F(ctx.maxFistRatioLeft)
                    + "（五指摊开≈2.2，握成拳≈1.2；只统计攻击状态帧）");
            if (one != null)
            {
                check("[D 单段挥砍] 生成刀光弧光", one.arcSpawnDelta >= 1,
                    "弧光生成 " + one.arcSpawnDelta + " 次");
                check("[D 单段挥砍] 弧光在屏幕上真实存在过（同屏峰值 ≥ 1）", one.arcActivePeak >= 1,
                    "同屏峰值 " + one.arcActivePeak);
                check("[D 单段挥砍] 刀锋拖尾有实际顶点（> 0）", one.trailVertexPeak > 0,
                    "顶点峰值 " + one.trailVertexPeak + "，发射帧 " + one.trailActiveFrames);
                check("[D 单段挥砍] 命中时刻触发震屏", one.shakeDelta >= 1,
                    "震屏 " + one.shakeDelta + " 次");
            }
            if (combo != null)
                check("[E 三段连击] 三段都出了弧光", combo.arcSpawnDelta >= 3,
                    "弧光生成 " + combo.arcSpawnDelta + " 次 / 挥砍 " + combo.swingDelta + " 次");

            sb.AppendLine();
            sb.AppendLine("-- 问题 2「后摇太长 / 后摇里再按会凭空位移」：取消窗口 --");
            if (one != null)
                check("[D 单段挥砍] 挥砍后进入后摇状态 Atk1Rec",
                    one.animStates.Contains("Atk1Rec"), one.animStates);
            if (one != null)
                check("[D 单段挥砍] 后摇中开放取消窗口", one.sawCancelWindow,
                    one.sawCancelWindow ? "首次开启于 t=" + F(one.cancelWindowFirstT) + "s" : "未开启");
            if (combo != null)
                check("[E 三段连击] 连击推进到第 3 段", combo.comboStepMax >= 3,
                    "最高段位 " + combo.comboStepMax);
            if (combo != null)
                check("[E 三段连击] 出现 Atk2 / Atk3 状态",
                    combo.animStates.Contains("Atk2") && combo.animStates.Contains("Atk3"),
                    combo.animStates);
            if (recancel != null)
                check("[F 后摇延迟再按] 成功接上第 2 段", recancel.comboStepMax >= 2,
                    "最高段位 " + recancel.comboStepMax);
            if (recancel != null)
            {
                var st2 = FindStep(recancel, 2);
                check("[F 后摇延迟再按] 第 2 段位移落在设定值附近（无凭空位移）",
                    st2 != null && st2.lungeTotal > ctl.comboLungeDistance[1] * 0.5f
                    && st2.lungeTotal < ctl.comboLungeDistance[1] * 1.8f,
                    st2 != null
                        ? "实测 " + F(st2.lungeTotal) + "m / 设定 " + N(ctl.comboLungeDistance[1]) + "m"
                        : "未捕获到第 2 段起点");
                check("[F 后摇延迟再按] 第 2 段位移在设定时长内完成（不是瞬移）",
                    st2 != null && st2.t90 <= st2.lungeDuration + 0.20f,
                    st2 != null
                        ? "90% 位移耗时 " + F(st2.t90) + "s / 设定时长 " + N(st2.lungeDuration) + "s"
                        : "未捕获到第 2 段起点");
            }
            if (dashcancel != null)
                check("[G 后摇冲刺取消] 窗口内能接上冲刺", dashcancel.sawDash,
                    dashcancel.sawDash ? "已接上" : "未接上");

            sb.AppendLine();
            sb.AppendLine("-- 位移学（前冲是曲线积分出来的，不是滑行） --");
            if (combo != null && combo.steps.Count > 0)
            {
                foreach (var st in combo.steps)
                    check("[E 连击] 第 " + st.step + " 段位移 ≈ 设定 "
                        + N(st.lungeClampDistance) + "m（容差 ±60%）",
                        Mathf.Abs(st.lungeTotal - st.lungeClampDistance) < st.lungeClampDistance * 0.6f,
                        "实测 " + F(st.lungeTotal) + "m，其中设定时长内 " + F(st.lungeFast)
                        + "m，90% 位移耗时 " + F(st.t90) + "s / 设定 " + N(st.lungeDuration) + "s"
                        + "  [期望速度峰值 " + F(st.peakDesired) + "，Attack 帧 " + st.attackFrames
                        + "，起点 PhaseTime " + F(st.onsetPhaseTime) + "]");
            }
            else
            {
                check("[E 连击] 捕获到连击段位推进", false, "未捕获任何段位起点");
            }

            sb.AppendLine();
            sb.AppendLine("-- 无瞬移 / 无残留滑行 --");
            foreach (var s in stages)
                check("[" + s.name + "] 无位移尖峰（≥18 m/s）且无单帧位移 > 0.6m",
                    s.teleportFrames == 0 && s.maxFrameDisplacement < TeleportDisplacementThreshold,
                    "尖峰帧 " + s.teleportFrames + "，最大单帧位移 " + F(s.maxFrameDisplacement)
                    + "m，最大帧速度 " + F(s.maxCleanFrameSpeed) + " m/s");
            if (settle != null)
                check("[H 收招静止] 回到静止（无残留滑行）", settle.maxSpeed < 0.15f,
                    "峰值 " + F(settle.maxSpeed) + " m/s");
            if (settle != null)
                check("[H 收招静止] 状态回到 Idle", settle.animStates.EndsWith("Idle"), settle.animStates);
            if (idle != null)
                check("[A 待机基线] 待机无漂移", idle.netDisplacement.magnitude < 0.05f,
                    F(idle.netDisplacement.magnitude) + " m");

            sb.AppendLine();
            sb.AppendLine("----------------------------------------------------------------------");
            sb.AppendLine("本报告（战斗/移动手感）: 通过 " + pass + " 项 / 未通过 " + fail + " 项");
            sb.AppendLine("姿态体检（S2_pose_latest.txt）: 通过 " + PoseScanPass + " 项 / 未通过 " + PoseScanFail + " 项");
            int totalFail = fail + PoseScanFail;
            sb.AppendLine("合计未通过 " + totalFail + " 项");
            sb.AppendLine(totalFail == 0
                ? ">>> M2 达成：连击有反馈、后摇可取消、镜头贴近、无滑步，且走路/跑步/攻击姿态与贴地全部达标"
                : ">>> 存在未通过项，需处理");
            sb.AppendLine("----------------------------------------------------------------------");

            WriteReport("S2", sb.ToString());
        }

        // ==================================================================
        // 音频链路冒烟测试
        // ==================================================================

        /// <summary>
        /// 音频**不是**"跑起来没报错"就算接好了 —— AudioKit 的加载失败是异步吞掉的，
        /// 日志干净也可能一声不响（本项目就踩过：SupportOldQF 把默认加载器换成了 ResKit，
        /// Resources 明明取得到，AudioKit 却报 Failed to Create Res）。
        ///
        /// 所以这里查**运行时产物**，三件事都要成立才算通：
        ///   1. 每个曲目名都能被 Resources.Load 取到（路径与命名约定正确）；
        ///   2. AudioKit 真的建出了 AudioSource 并挂上了对应片段（它内部是
        ///      new GameObject(name).AddComponent&lt;AudioSource&gt;()，场景里找得到）；
        ///   3. Play() 之后该 AudioSource 的 isPlaying 为真（不是空转）。
        /// 另外验证静音开关能挡住播放、音量能落到 AudioSource.volume 上。
        /// </summary>
        public static IEnumerator AudioSmokeCheck()
        {
            var sb = new StringBuilder();
            int pass = 0, fail = 0;
            System.Action<string, bool, string> check = (label, ok, detail) =>
            {
                if (ok) pass++; else fail++;
                sb.AppendLine("  " + (ok ? "[通过] " : "[未通过] ") + label + "   " + detail);
            };

            sb.AppendLine("Sprint 2 音频链路冒烟测试");
            sb.AppendLine("生成时间: " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            sb.AppendLine("判据: 验证 AudioKit 的运行时产物（AudioSource / clip / isPlaying），而不是「没报错」。");
            sb.AppendLine();

            var sfx = new (string label, string name)[]
            {
                ("第1段挥砍", GameAudio.Sfx.SwingA),
                ("第2段挥砍", GameAudio.Sfx.SwingB),
                ("第3段挥砍", GameAudio.Sfx.SwingHeavy),
                ("冲刺",      GameAudio.Sfx.Dash),
                ("拔刀",      GameAudio.Sfx.Draw),
                ("硬目标命中", GameAudio.Sfx.HitBlade),
                ("软目标命中", GameAudio.Sfx.HitFlesh),
                ("脚步",      GameAudio.Sfx.Footstep),
                ("UI 点击",   GameAudio.Sfx.UiClick),
            };

            int clipOk = 0;
            sb.AppendLine("-- 1) 资源可加载（Resources.Load，与 AudioKit 默认加载器同路径） --");
            var missing = new List<string>();
            foreach (var x in sfx)
            {
                var c = Resources.Load<AudioClip>(x.name);
                if (c != null) clipOk++; else missing.Add(x.name);
            }
            var bgmClip = Resources.Load<AudioClip>(GameAudio.Bgm.ThemeMain);
            check("9 个战斗音效全部可取", clipOk == sfx.Length,
                clipOk + "/" + sfx.Length + (missing.Count > 0 ? "，缺: " + string.Join(", ", missing.ToArray()) : ""));
            check("BGM 可取（" + GameAudio.Bgm.ThemeMain + "）", bgmClip != null,
                bgmClip != null ? "时长 " + F(bgmClip.length) + "s" : "取不到");

            // 从干净状态开始：先停掉一切，并确保开关是开的
            GameAudio.StopAllSfx();
            GameAudio.MusicEnabled = true;
            GameAudio.SfxEnabled = true;
            GameAudio.SfxVolume = 1f;
            yield return null;

            sb.AppendLine();
            sb.AppendLine("-- 2) 音效播放产物（AudioKit 内部建 AudioSource） --");
            int playOk = 0;
            var details = new List<string>();
            foreach (var x in sfx)
            {
                GameAudio.StopAllSfx();
                yield return null;
                int before = CountPlayingAudioSources();
                GameAudio.PlaySfx(x.name, 1f, 1f);
                // AudioKit 默认「异步准备片段」，首次播放要多等 1~2 帧
                for (int i = 0; i < 6; i++) yield return null;

                var src = FindPlayingAudioSource();
                bool ok = src != null && src.clip != null && src.isPlaying;
                if (ok) playOk++;
                details.Add(x.label + (ok ? "✓" : "✗") + (src != null && src.clip != null ? "(" + src.clip.name + ")" : ""));
            }
            check("9 个音效都能真正播出去（isPlaying）", playOk == sfx.Length,
                playOk + "/" + sfx.Length + " —— " + string.Join("  ", details.ToArray()));

            sb.AppendLine();
            sb.AppendLine("-- 3) 音量 / 静音开关 --");
            GameAudio.StopAllSfx();
            yield return null;
            GameAudio.SfxVolume = 1f;
            GameAudio.PlaySfx(GameAudio.Sfx.SwingA, 1f, 1f);
            for (int i = 0; i < 6; i++) yield return null;
            var loud = FindPlayingAudioSource();
            check("SfxVolume=1 时 AudioSource.volume ≈ 1", loud != null && loud.volume > 0.9f,
                loud != null ? "volume " + F(loud.volume) + "，pitch " + F(loud.pitch) : "没有在播的 AudioSource");

            GameAudio.StopAllSfx();
            yield return null;
            GameAudio.SfxEnabled = false;
            int beforeMute = CountPlayingAudioSources();
            GameAudio.PlaySfx(GameAudio.Sfx.SwingA, 1f, 1f);
            for (int i = 0; i < 6; i++) yield return null;
            check("SfxEnabled=false 时不产生播放", CountPlayingAudioSources() <= beforeMute,
                "静音前后在播数量 " + beforeMute + " → " + CountPlayingAudioSources());
            GameAudio.SfxEnabled = true;

            sb.AppendLine();
            sb.AppendLine("-- 4) BGM --");
            GameAudio.StopMusic();
            yield return null;
            GameAudio.PlayMusic(GameAudio.Bgm.ThemeMain, true, 1f);
            for (int i = 0; i < 8; i++) yield return null;
            var music = FindLoopingPlayingAudioSource();
            check("BGM 在循环播放", music != null,
                music != null ? "clip " + (music.clip != null ? music.clip.name : "(空)") + " loop=" + music.loop : "未在播");
            GameAudio.StopMusic();
            yield return null;

            sb.AppendLine();
            sb.AppendLine("----------------------------------------------------------------------");
            sb.AppendLine("结果: 通过 " + pass + " 项 / 未通过 " + fail + " 项");
            sb.AppendLine(fail == 0
                ? ">>> 音频链路可用：片段可加载、AudioSource 真在播、开关与音量生效"
                : ">>> 音频链路存在未通过项，需处理");
            sb.AppendLine("----------------------------------------------------------------------");

            WriteReport("S2_audio", sb.ToString());
        }

        private static int CountPlayingAudioSources()
        {
            int n = 0;
            foreach (var a in UnityEngine.Object.FindObjectsOfType<AudioSource>(true))
                if (a.isPlaying) n++;
            return n;
        }

        private static AudioSource FindPlayingAudioSource()
        {
            foreach (var a in UnityEngine.Object.FindObjectsOfType<AudioSource>(true))
                if (a.isPlaying && a.clip != null) return a;
            return null;
        }

        private static AudioSource FindLoopingPlayingAudioSource()
        {
            foreach (var a in UnityEngine.Object.FindObjectsOfType<AudioSource>(true))
                if (a.isPlaying && a.loop) return a;
            return null;
        }

        // ==================================================================
        // 演示流程：只为录 MP4（不做判定，报告写去 Demo_latest.txt）
        // ==================================================================

        /// <summary>
        /// 演示片段。<paramref name="combat"/> = true 录战斗，false 录移动。
        /// 复用同一套注入通道，保证"录出来的"和"验收测的"是同一套输入。
        /// <paramref name="compact"/> = true 时压掉开场待机与"后摇再按"一段：
        /// 桥单次录 MP4 上限 15s，完整版含各段静置共约 17.8s 会被截尾。
        /// </summary>
        public static IEnumerator DemoFlow(bool combat, bool compact = false)
        {
            var ctx = ResolveContext();
            if (ctx == null) { Debug.LogError("[PlaytestHarness] DemoFlow: 场景上下文不完整"); yield break; }

            var sink = new List<CombatResult>();
            Vector3 startPos = ctx.playerGo.transform.position;
            Quaternion startRot = ctx.playerGo.transform.rotation;
            float startYaw = ctx.rig != null ? ctx.rig.yaw : 0f;
            float startPitch = ctx.rig != null ? ctx.rig.pitch : 0f;
            bool startLook = ctx.rig != null && ctx.rig.mouseLookEnabled;

            ctx.ctl.BeginInputOverride();
            if (ctx.rig != null)
            {
                ctx.rig.SetMouseLookEnabled(false);
                ctx.rig.yaw = DEMO_CAM_YAW;
                ctx.rig.pitch = DEMO_CAM_PITCH;
                ctx.rig.SnapBehindTarget();
            }

            if (combat)
            {
                yield return RunCombatStage(sink, ctx, "演示 待机", compact ? 0.8f : 1.0f, Vector2.zero, false, null);
                yield return RunCombatStage(sink, ctx, "演示 单段挥砍", compact ? 1.8f : 2.0f, Vector2.zero, false,
                    (c, t, f, w) => { if (f == 0) w.attack = true; });

                float last = -9f;
                yield return RunCombatStage(sink, ctx, "演示 三段连击", 4.6f, Vector2.zero, false,
                    (c, t, f, w) =>
                    {
                        if (c.ComboStep >= 3) return;
                        bool canPress = c.Phase != ActionPhase.Attack || c.IsCancelWindowOpen;
                        if (canPress && t - last > 0.10f) { last = t; w.attack = true; }
                    });

                if (!compact)
                {
                    bool p2 = false; float wAt = -1f;
                    yield return RunCombatStage(sink, ctx, "演示 后摇再按", 2.4f, Vector2.zero, false,
                        (c, t, f, w) =>
                        {
                            if (f == 0) w.attack = true;
                            if (p2) return;
                            if (c.IsCancelWindowOpen && wAt < 0f) wAt = t;
                            if (wAt >= 0f && t - wAt >= 0.25f) { p2 = true; w.attack = true; }
                        });
                }

                bool d2 = false; float wAt2 = -1f;
                yield return RunCombatStage(sink, ctx, "演示 后摇冲刺取消", compact ? 2.2f : 2.4f, Vector2.zero, false,
                    (c, t, f, w) =>
                    {
                        if (f == 0) w.attack = true;
                        if (d2) return;
                        if (c.IsCancelWindowOpen && wAt2 < 0f) wAt2 = t;
                        if (wAt2 >= 0f && t - wAt2 >= 0.06f) { d2 = true; w.dash = true; }
                    });

                yield return RunCombatStage(sink, ctx, "演示 收招", compact ? 0.8f : 1.2f, Vector2.zero, false, null);
            }
            else
            {
                yield return RunCombatStage(sink, ctx, "演示 待机", compact ? 0.6f : 1.2f, Vector2.zero, false, null);
                // 侧向行走：从相机看是左右侧面，上半身姿态与腿部动作同时可见
                yield return RunCombatStage(sink, ctx, "演示 步行(侧)", compact ? 2.6f : 3.0f, Vector2.right, false, null);
                yield return RunCombatStage(sink, ctx, "演示 疾跑(侧)", compact ? 2.2f : 2.6f, Vector2.left, true, null);
                if (!compact)
                    yield return RunCombatStage(sink, ctx, "演示 步行(前)", 2.4f, Vector2.up, false, null);
                yield return RunCombatStage(sink, ctx, "演示 疾跑(前)", compact ? 2.6f : 2.0f, Vector2.up, true, null);
                yield return RunCombatStage(sink, ctx, "演示 冲刺", 1.2f, Vector2.up, true,
                    (c, t, f, w) => { if (f == 1) w.dash = true; });
            }

            ctx.ctl.EndInputOverride();
            if (ctx.rig != null)
            {
                ctx.rig.yaw = startYaw;
                ctx.rig.pitch = startPitch;
                ctx.rig.SetMouseLookEnabled(startLook);
            }
            ctx.playerGo.transform.position = startPos;
            ctx.playerGo.transform.rotation = startRot;

            Debug.Log("[PlaytestHarness] 演示流程结束（" + (combat ? "战斗" : "移动") + "）");
        }

        private const float DEMO_CAM_YAW = 22f;
        private const float DEMO_CAM_PITCH = 8f;

        /// <summary>演示：移动段。</summary>
        public static IEnumerator DemoLocomotion() { return DemoFlow(false); }

        /// <summary>演示：移动段（紧凑版，专供单次 MP4 录制，总长约 11.4s &lt; 15s 上限）。</summary>
        public static IEnumerator DemoLocomotionCompact() { return DemoFlow(false, true); }

        /// <summary>演示：战斗段。</summary>
        public static IEnumerator DemoCombat() { return DemoFlow(true); }

        /// <summary>演示：战斗段（紧凑版，专供单次 MP4 录制，总长约 13.7s &lt; 15s 上限）。</summary>
        public static IEnumerator DemoCombatCompact() { return DemoFlow(true, true); }

        /// <summary>
        /// 演示：只跑（诊断「跑步扭腰」专用）。
        /// 后 3/4 机位是看髋/肩反向扭转最清楚的角度；正侧机位用来看腿的循环与躯干倾角。
        /// 两段都是纯直线跑，不带转向，避免把转向混进去。
        /// </summary>
        public static IEnumerator DemoRunOnly()
        {
            var ctx = ResolveContext();
            if (ctx == null) { Debug.LogError("[PlaytestHarness] DemoRunOnly: 场景上下文不完整"); yield break; }

            var sink = new List<CombatResult>();
            Vector3 startPos = ctx.playerGo.transform.position;
            Quaternion startRot = ctx.playerGo.transform.rotation;
            float startYaw = ctx.rig != null ? ctx.rig.yaw : 0f;
            float startPitch = ctx.rig != null ? ctx.rig.pitch : 0f;
            bool startLook = ctx.rig != null && ctx.rig.mouseLookEnabled;

            ctx.ctl.BeginInputOverride();
            if (ctx.rig != null)
            {
                ctx.rig.SetMouseLookEnabled(false);
                ctx.rig.yaw = DEMO_CAM_YAW;
                ctx.rig.pitch = DEMO_CAM_PITCH;
                ctx.rig.SnapBehindTarget();
            }

            yield return RunCombatStage(sink, ctx, "演示 跑步(后3/4)", 3.2f, Vector2.up, true, null);
            yield return RunCombatStage(sink, ctx, "演示 跑步(正侧)", 2.8f, Vector2.right, true, null);

            ctx.ctl.EndInputOverride();
            if (ctx.rig != null)
            {
                ctx.rig.yaw = startYaw;
                ctx.rig.pitch = startPitch;
                ctx.rig.SetMouseLookEnabled(startLook);
            }
            ctx.playerGo.transform.position = startPos;
            ctx.playerGo.transform.rotation = startRot;

            Debug.Log("[PlaytestHarness] 演示流程结束（只跑）");
        }

        // ==================================================================
        // S1 单阶段执行
        // ==================================================================

        private static IEnumerator RunStage(
            List<StageResult> sink, PlayerController ctl, Animator anim, Camera cam,
            GameObject playerGo, string name, float seconds, Vector2 move, bool runHeld, bool dashAtStart)
        {
            ResetPlayer(playerGo, ctl);
            ctl.SetInjectedMove(Vector2.zero, false);
            float settleEnd = Time.time + SettleSeconds;
            while (Time.time < settleEnd) yield return null;

            var samples = new List<Sample>(256);

            ctl.SetInjectedMove(move, runHeld);
            if (dashAtStart) ctl.RequestInjectedDash();

            float t0 = Time.time;
            while (Time.time - t0 < seconds)
            {
                samples.Add(new Sample
                {
                    dt = Time.deltaTime,
                    playerPos = playerGo.transform.position,
                    playerY = playerGo.transform.position.y,
                    grounded = ctl.IsGrounded,
                    camPos = cam.transform.position,
                    camYawRaw = cam.transform.eulerAngles.y,
                    camPitchRaw = cam.transform.eulerAngles.x,
                    viewport = cam.WorldToViewportPoint(playerGo.transform.position + Vector3.up * 0.9f),
                    speed = ctl.CurrentSpeed,
                    desiredSpeed = ctl.DesiredSpeed,
                    anim = CurrentState(anim),
                    invincible = ctl.IsInvincible,
                    dashing = ctl.IsDashing,
                    camForward = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized,
                    camRight = Vector3.ProjectOnPlane(cam.transform.right, Vector3.up).normalized
                });
                yield return null;
            }

            sink.Add(Analyze(name, samples, move));
        }

        // ==================================================================
        // 姿态体检：把每个动画状态冻结在各归一化时刻，量化「躯干倾角」与「脚是否穿地」
        // ==================================================================

        private class PoseScanRow
        {
            public string state;
            public float lowest = float.MaxValue;   // 贴地后 网格最低点相对地面的最小值（负 = 穿地）
            public float lowestNoIk = float.MaxValue; // 关掉 FootIK 时同一状态的最低点（看动画本体的高度）
            public float highestNoIk = float.MinValue; // 关掉 FootIK 时同一状态的最高点（判断"整体偏移"还是"真动态"）
            public float lowestAt;
            public float soleY = float.MaxValue;    // 最低那一刻的绝对世界高度（用来区分穿地 / 浮空）
            public float leanAvg;
            public float leanMin = float.MaxValue;
            public float leanMax = float.MinValue;
        }

        /// <summary>姿态体检的通过 / 未通过计数（供总汇总使用）。</summary>
        public static int PoseScanPass, PoseScanFail;

        /// <summary>
        /// 姿态体检（问题 1「走路仰着身子」/ 问题 6「攻击时脚陷进地面」）。
        ///
        /// 为什么单独做一次"冻结扫描"而不是在实时战斗里采样：
        ///  · 要判"脚有没有陷进地面"，骨骼关节点没用 —— 踝关节离脚底还有十几厘米，
        ///    必须用 <see cref="SkinnedMeshRenderer.BakeMesh"/> 取**当前姿势的真实网格最低点**；
        ///    BakeMesh 比较贵，每帧做会污染报告里的帧率指标。
        ///  · 要判"躯干后仰"，必须把同一段动画的各个时刻都看一眼 —— 战斗采样只会撞到
        ///    其中随机几帧，测不出"整段都后仰"。
        /// 冻结扫描期间会把 PlayerController 关掉，免得它一边推参数一边把状态机拽走；
        /// 结束后复位角色，后续阶段不受影响。
        /// </summary>
        private static IEnumerator PoseAndGroundScan(CombatContext ctx)
        {
            var sb = new StringBuilder();
            sb.AppendLine("======================================================================");
            sb.AppendLine("姿态体检（问题 1「仰着身子」/ 问题 6「攻击时脚陷进地面」）");
            sb.AppendLine("生成时间 " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("======================================================================");

            var anim = ctx.anim;
            var go = ctx.playerGo;
            var smrs = go.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var bake = new Mesh();

            bool ctlWasEnabled = ctx.ctl.enabled;
            ctx.ctl.enabled = false;    // 体检期间不让 PlayerController 抢动画

            // 体检期间让 FootIK 的抬升**瞬时到位**。
            // 它的指数平滑是为游戏内观感服务的（liftSmooth≈14 → 每帧只走 21%），
            // 而冻结扫描是"跳着"采样各个归一化时刻的，平滑会让抬升量永远追不上目标，
            // 量出来的穿地深度会明显偏大（实测偏大约 0.08m）。
            var footIk = go.GetComponent<FootIK>();
            float savedSmooth = footIk != null ? footIk.liftSmooth : 0f;
            bool savedIkEnabled = footIk != null && footIk.enableIk;
            if (footIk != null) footIk.liftSmooth = 1e6f;

            yield return null;

            var rows = new List<PoseScanRow>();
            string[] states = { "Idle", "Walk", "Run", "Dash", "Atk1", "Atk1Rec", "Atk2", "Atk2Rec", "Atk3" };

            // 两轮扫描：
            //   第 1 轮 关掉 FootIK —— 量「动画本体」把脚底放到了哪儿（区分是动画自带浮空还是 IK 推的）
            //   第 2 轮 打开 FootIK —— 量贴地修正后的结果
            // 只扫一遍的话，看到浮空根本无法判断该改动画还是改 IK 参数。
            for (int round = 0; round < 2; round++)
            {
                bool ikOn = round == 1;
                if (footIk != null) footIk.enableIk = ikOn;
                yield return null;

                foreach (var name in states)
                {
                    int hash = Animator.StringToHash(name);
                    if (!anim.HasState(0, hash)) continue;

                    PoseScanRow row;
                    if (ikOn)
                    {
                        row = FindPose(rows, name);
                        if (row == null) continue;
                    }
                    else
                    {
                        row = new PoseScanRow { state = name };
                    }

                    float leanSum = 0f; int leanN = 0;
                    const int steps = 20;

                    for (int i = 0; i <= steps; i++)
                    {
                        float nt = i / (float)steps;
                        anim.Play(hash, 0, nt);
                        anim.Update(1f / 60f);   // dt 必须 > 0，否则 FootIK 的平滑不会真正执行

                        float groundY = GroundYUnder(go.transform.position, go.transform);
                        float low = float.MaxValue;
                        for (int k = 0; k < smrs.Length; k++)
                        {
                            smrs[k].BakeMesh(bake);
                            var m = smrs[k].transform.localToWorldMatrix;
                            // 必须**逐顶点**取最低，不能拿 bake.bounds 的 8 个角点去变换：
                            // 渲染器变换带旋转/缩放时，包围盒角点变换后会"鼓"到真实网格之外，
                            // 量出的最低点比实际低 0.09m 量级（SuperHero_Male 实测 0.324 vs 0.415），
                            // 会把"其实浮空"误判成"穿地"。这是本体检早期版本最大的一个假数据来源。
                            var verts = bake.vertices;
                            for (int vi = 0; vi < verts.Length; vi++)
                                low = Mathf.Min(low, m.MultiplyPoint3x4(verts[vi]).y - groundY);
                        }

                        if (ikOn)
                        {
                            if (low < row.lowest) { row.lowest = low; row.lowestAt = nt; row.soleY = low + groundY; }

                            float lean = TorsoLeanDeg(ctx);
                            if (nt > 0.02f && nt < 0.98f) { leanSum += lean; leanN++; }
                            row.leanMin = Mathf.Min(row.leanMin, lean);
                            row.leanMax = Mathf.Max(row.leanMax, lean);
                        }
                        else
                        {
                            row.lowestNoIk = Mathf.Min(row.lowestNoIk, low);
                            row.highestNoIk = Mathf.Max(row.highestNoIk, low);
                        }
                    }

                    if (!ikOn)
                    {
                        if (row.lowestNoIk == float.MaxValue) row.lowestNoIk = 0f;
                        rows.Add(row);
                    }
                    else
                    {
                        row.leanAvg = leanN > 0 ? leanSum / leanN : 0f;
                        if (row.lowest == float.MaxValue) row.lowest = 0f;
                    }

                    // 每个状态让出一帧：BakeMesh 很贵，一口气做完会卡出一帧超长帧
                    yield return null;
                }
            }

            // ---- 复位 ----
            anim.Play(Animator.StringToHash("Idle"), 0, 0f);
            anim.Update(1f / 60f);
            UnityEngine.Object.Destroy(bake);
            if (footIk != null) { footIk.liftSmooth = savedSmooth; footIk.enableIk = savedIkEnabled; }
            ctx.ctl.enabled = ctlWasEnabled;
            ResetPlayer(go, ctx.ctl);

            // ---- 数据表 ----
            sb.AppendLine();
            sb.AppendLine("逐状态数据（网格最低点相对真实地面；负值 = 穿地，正值 = 浮空）");
            sb.AppendLine("----------------------------------------------------------------------");
            sb.AppendLine("  状态       无IK最低   无IK最高   贴地后最低点   绝对Y     最深时刻   躯干倾角平均   区间");
            foreach (var r in rows)
                sb.AppendLine(string.Format("  {0,-9} {1,8:F3} m {2,8:F3} m {3,11:F3} m {4,9:F3}    nt={5:F2}     {6,7:F1}°    [{7:F1}~{8:F1}]°",
                    r.state, r.lowestNoIk, r.highestNoIk, r.lowest, r.soleY, r.lowestAt, r.leanAvg, r.leanMin, r.leanMax));
            sb.AppendLine("  说明：无IK最低/最高 = 动画本体脚底的高度。两者都为正 → 整段动画被整体抬高；跨越 0 → 真有落地动作。");

            // ---- 判定 ----
            sb.AppendLine();
            sb.AppendLine("判定");
            sb.AppendLine("----------------------------------------------------------------------");
            int pass = 0, fail = 0;
            System.Action<string, bool, string> chk = (label, ok, detail) =>
            {
                if (ok) pass++; else fail++;
                sb.AppendLine("  " + (ok ? "[通过] " : "[未通过] ") + label + "   " + detail);
            };

            float worst = 0f; string worstState = "";
            foreach (var r in rows) { if (r.state == "Dash") continue; if (r.lowest < worst) { worst = r.lowest; worstState = r.state; } }
            chk("问题 6 落脚状态脚不穿地（最低点 ≥ -0.02m）", worst >= -0.02f,
                "最差 " + (worstState.Length > 0 ? worstState : "-") + " " + F(worst) + " m（FootIK 抬升补偿后应贴近 0）");

            // 反向的毛病同样要拦：脚踩不到地、整个人飘在半空。
            // 冲刺（翻滚）豁免 —— 那是腾空动作；Run 不能豁免，它整段的最低点也该触地（跑步有腾空相，但落地相必须落地）。
            float worstFloat = float.MinValue; string worstFloatState = "";
            foreach (var r in rows)
            {
                if (r.state == "Dash") continue;
                if (r.lowest > worstFloat) { worstFloat = r.lowest; worstFloatState = r.state; }
            }
            chk("问题 6b 落脚状态不浮空（最低点 ≤ +0.05m）", worstFloat <= 0.05f,
                "最飘 " + (worstFloatState.Length > 0 ? worstFloatState : "-") + " " + F(worstFloat) + " m");

            var dashRow = FindPose(rows, "Dash");
            if (dashRow != null)
                sb.AppendLine("  [参考] Dash(翻滚) 最低点 " + F(dashRow.lowest) + " m —— 单独判定：该状态" +
                    "豁免帧内贴地（脚本来就在空中），只靠静态基准偏移把它抬到不铲地，不进上面的断言");

            var w = FindPose(rows, "Walk");
            if (w != null)
                chk("问题 1 走路片段躯干不后仰（平均倾角 > -8°）",
                    ctx.torsoLeanAvailable && w.leanAvg > -8f,
                    (ctx.torsoLeanAvailable ? "" : "[测量不可用：髋/胸骨未解析到] ")
                    + "Walk 平均 " + F(w.leanAvg) + "°  区间[" + F(w.leanMin) + "~" + F(w.leanMax) + "]°");
            var rr = FindPose(rows, "Run");
            if (rr != null)
                chk("问题 1/3 跑步片段躯干不后仰（平均倾角 > -8°）",
                    ctx.torsoLeanAvailable && rr.leanAvg > -8f,
                    (ctx.torsoLeanAvailable ? "" : "[测量不可用：髋/胸骨未解析到] ")
                    + "Run 平均 " + F(rr.leanAvg) + "°  区间[" + F(rr.leanMin) + "~" + F(rr.leanMax) + "]°");

            foreach (var r in rows)
                if (r.state.StartsWith("Atk"))
                    sb.AppendLine("  [参考] " + r.state + " 躯干倾角平均 " + F(r.leanAvg) + "°（前冲挥砍本来就会前倾）");

            sb.AppendLine();
            sb.AppendLine("结果: " + pass + " 通过 / " + fail + " 未通过");
            PoseScanPass = pass; PoseScanFail = fail;
            WriteReport("S2_pose", sb.ToString());
        }

        private static PoseScanRow FindPose(List<PoseScanRow> rows, string state)
        {
            foreach (var r in rows) if (r.state == state) return r;
            return null;
        }

        /// <summary>角色正下方的地面高度（剔掉角色自己的碰撞体，并忽略 Trigger）。</summary>
        private static float GroundYUnder(Vector3 worldPos, Transform selfRoot)
        {
            // QueryTriggerInteraction.Ignore 不能省：默认会命中触发器碰撞体（攻击判定框一类），
            // 那些东西悬在半空，一旦被当成"地面"，穿地判定就整体失真。
            var hits = Physics.RaycastAll(worldPos + Vector3.up * 3f, Vector3.down, 10f, ~0,
                QueryTriggerInteraction.Ignore);
            float best = float.MinValue;
            for (int i = 0; i < hits.Length; i++)
            {
                var tr = hits[i].collider.transform;
                if (selfRoot != null && (tr == selfRoot || tr.IsChildOf(selfRoot))) continue;
                if (hits[i].point.y > best) best = hits[i].point.y;
            }
            return best > float.MinValue ? best : worldPos.y;
        }

        // ==================================================================
        // S2 单阶段执行
        // ==================================================================

        private static IEnumerator RunCombatStage(
            List<CombatResult> sink, CombatContext ctx, string name, float seconds,
            Vector2 move, bool runHeld, CombatTick tick)
        {
            var vfx = ctx.vfx;
            var bl = new VfxBaseline
            {
                swing = vfx != null ? vfx.SwingCount : 0,
                arc = vfx != null ? vfx.ArcSpawnCount : 0,
                shake = vfx != null ? vfx.ShakeTriggerCount : 0
            };

            ResetPlayer(ctx.playerGo, ctx.ctl);
            ctx.ctl.SetInjectedMove(Vector2.zero, false);
            float settleEnd = Time.time + SettleSeconds;
            while (Time.time < settleEnd) yield return null;

            var samples = new List<CombatSample>(512);
            ctx.ctl.SetInjectedMove(move, runHeld);

            float t0 = Time.time;
            int frame = 0;
            while (Time.time - t0 < seconds)
            {
                float t = Time.time - t0;

                var want = new TickIntent();
                if (tick != null) tick(ctx.ctl, t, frame, want);
                if (want.attack) ctx.ctl.RequestInjectedAttack();
                if (want.dash) ctx.ctl.RequestInjectedDash();

                samples.Add(CaptureCombatSample(ctx, t, want));
                frame++;
                yield return null;
            }

            sink.Add(AnalyzeCombat(name, samples, move, ctx, bl));
        }

        /// <summary>当前是否处于某个攻击状态。用于把「事件型指标」的采样窗口裁到事件边界。</summary>
        private static readonly string[] AttackStates = { "Atk1", "Atk1Rec", "Atk2", "Atk2Rec", "Atk3" };

        private static bool IsAttackState(Animator anim)
        {
            if (anim == null) return false;
            var st = anim.GetCurrentAnimatorStateInfo(0);
            for (int i = 0; i < AttackStates.Length; i++)
                if (st.IsName(AttackStates[i])) return true;
            return false;
        }

        private static CombatSample CaptureCombatSample(CombatContext ctx, float t, TickIntent want)
        {
            var cam = ctx.cam;
            var pos = ctx.playerGo.transform.position;
            var anim = ctx.anim;

            Vector3 foot = cam.WorldToViewportPoint(pos + Vector3.up * 0.05f);
            Vector3 head = cam.WorldToViewportPoint(pos + Vector3.up * CharacterHeight);
            float screenH = (foot.z > 0f && head.z > 0f) ? Mathf.Abs(head.y - foot.y) : -1f;

            // 相机稳定性看**逻辑** yaw/pitch：震屏是往 transform 直接加随机旋转的，
            // 读 eulerAngles 会把震屏误判成"镜头抖"（见类注释第 8 条）。
            float yaw = ctx.rig != null ? ctx.rig.Yaw : cam.transform.eulerAngles.y;
            float pitch = ctx.rig != null ? ctx.rig.Pitch : cam.transform.eulerAngles.x;

            // 握拳度峰值：摊开≈2.2 / 握拳≈1.2。
            // 两个条件都要：
            //   ① 只在「补丁这一帧确实跑过」时采信 —— 探针都是 LateUpdate 末尾写的，
            //      刚进战斗那一帧读到的还是上一帧的陈旧值（组件 disabled 时 CurledBoneCount 不再刷新）。
            //   ② **窗口裁到攻击状态**。判据名是「攻击全程两手都握着」，而采样是整个战斗 stage
            //      逐帧跑的：战斗中的跑步帧（KI Run01 的基础手姿势是半张开的）会把峰值抬到 1.70+，
            //      和「攻击时手张开」根本不是一回事（实测右手攻击帧恒为 1.547、跑步帧 1.701）。
            //      —— 与「事件型指标的窗口必须裁到事件边界」是同一条规矩，这里第二次踩。
            if (ctx.pose != null && ctx.pose.enabled && ctx.pose.CurledBoneCount > 0 && IsAttackState(anim))
            {
                if (ctx.pose.RightFistRatio > ctx.maxFistRatioRight) ctx.maxFistRatioRight = ctx.pose.RightFistRatio;
                if (ctx.pose.LeftFistRatio > ctx.maxFistRatioLeft) ctx.maxFistRatioLeft = ctx.pose.LeftFistRatio;
            }

            return new CombatSample
            {
                t = t,
                dt = Time.deltaTime,
                pos = pos,
                speed = ctx.ctl.CurrentSpeed,
                desiredSpeed = ctx.ctl.DesiredSpeed,
                desiredVel = ctx.ctl.DesiredVelocity,
                anim = CurrentState(anim),
                phase = ctx.ctl.Phase,
                comboStep = ctx.ctl.ComboStep,
                cancelWindow = ctx.ctl.IsCancelWindowOpen,
                motionSpeed = anim.GetFloat(HashMotionSpeed),
                clipLen = CurrentClipLength(anim),
                upperWeight = ExtraLayerWeight(anim),
                torsoLeanDeg = TorsoLeanDeg(ctx),
                bladeSpeed = ctx.vfx != null ? ctx.vfx.BladeSpeed : 0f,
                emitStartSpeed = ctx.vfx != null ? ctx.vfx.EmitStartBladeSpeed : -1f,
                arcActive = SwordVfx.ActiveArcCount,
                trailVerts = ctx.vfx != null ? ctx.vfx.TrailPositionCount : 0,
                trailEmitting = ctx.vfx != null && ctx.vfx.IsTrailEmitting,
                shaking = ctx.rig != null && ctx.rig.IsShaking,
                attackReq = want.attack,
                dashReq = want.dash,
                viewport = cam.WorldToViewportPoint(pos + Vector3.up * 0.9f),
                screenHeight = screenH,
                camDistance = ctx.rig != null ? ctx.rig.CurrentDistance : 0f,
                camFov = cam.fieldOfView,
                camYaw = yaw,
                camPitch = pitch,
                phaseTime = ctx.ctl.PhaseTime,
                grounded = ctx.ctl.IsGrounded
            };
        }

        // ==================================================================
        // 分析：S1
        // ==================================================================

        private static StageResult Analyze(string name, List<Sample> samples, Vector2 moveInput)
        {
            var r = new StageResult { name = name, samples = samples.Count };
            if (samples.Count < 3) return r;

            // ---- 时长：逐帧累加（不能用末帧 deltaTime × 帧数） ----
            float dur = 0f;
            for (int i = 1; i < samples.Count; i++) dur += samples[i].dt;
            r.duration = dur;

            float dist = 0f, speedSum = 0f, desiredSum = 0f, speedMax = 0f;
            float yMin = float.MaxValue, yMax = float.MinValue;
            int groundedN = 0, blockedN = 0;
            var states = new List<string>();

            for (int i = 0; i < samples.Count; i++)
            {
                var s = samples[i];
                speedSum += s.speed;
                desiredSum += s.desiredSpeed;
                speedMax = Mathf.Max(speedMax, s.speed);
                if (i > 0) dist += Vector3.Distance(s.playerPos, samples[i - 1].playerPos);
                if (!states.Contains(s.anim)) states.Add(s.anim);
                if (s.dashing) r.sawDash = true;
                if (s.invincible) r.sawInvincible = true;
                if (s.grounded) groundedN++;
                if (s.desiredSpeed > 1f && s.speed < s.desiredSpeed * 0.4f) blockedN++;
                yMin = Mathf.Min(yMin, s.playerY);
                yMax = Mathf.Max(yMax, s.playerY);
                r.minViewport = Vector2.Min(r.minViewport, s.viewport);
                r.maxViewport = Vector2.Max(r.maxViewport, s.viewport);
            }

            r.distance = dist;
            r.avgSpeed = speedSum / samples.Count;
            r.avgDesiredSpeed = desiredSum / samples.Count;
            r.maxSpeed = speedMax;
            r.blockedRatio = (float)blockedN / samples.Count;
            r.netDisplacement = samples[samples.Count - 1].playerPos - samples[0].playerPos;
            r.animStates = string.Join(" -> ", states.ToArray());
            r.playerYRange = yMax - yMin;
            r.groundedRatio = (float)groundedN / samples.Count;

            // ---- 角度：解卷绕后再取范围（避免 0° 附近的 359.99 假象） ----
            float yawMin = 0f, yawMax = 0f, pitMin = 0f, pitMax = 0f;
            float yawAcc = 0f, pitAcc = 0f;
            float prevYaw = samples[0].camYawRaw, prevPit = samples[0].camPitchRaw;
            for (int i = 1; i < samples.Count; i++)
            {
                yawAcc += Mathf.DeltaAngle(prevYaw, samples[i].camYawRaw);
                pitAcc += Mathf.DeltaAngle(prevPit, samples[i].camPitchRaw);
                prevYaw = samples[i].camYawRaw;
                prevPit = samples[i].camPitchRaw;
                yawMin = Mathf.Min(yawMin, yawAcc); yawMax = Mathf.Max(yawMax, yawAcc);
                pitMin = Mathf.Min(pitMin, pitAcc); pitMax = Mathf.Max(pitMax, pitAcc);
            }
            r.camYawRange = yawMax - yawMin;
            r.camPitchRange = pitMax - pitMin;

            // ---- 稳态窗口：丢弃前 40%（阻尼收敛期） ----
            int from = Mathf.FloorToInt(samples.Count * 0.4f);
            if (samples.Count - from < 8) from = Mathf.Max(0, samples.Count - 8);

            float ssSum = 0f; int ssN = 0;
            for (int i = from; i < samples.Count; i++) { ssSum += samples[i].speed; ssN++; }
            r.steadySpeedAvg = ssN > 0 ? ssSum / ssN : 0f;

            // ---- 相机速度 ----
            var vels = new List<Vector3>();
            var dts = new List<float>();
            for (int i = from + 1; i < samples.Count; i++)
            {
                float dt = Mathf.Max(samples[i].dt, 1e-5f);
                vels.Add((samples[i].camPos - samples[i - 1].camPos) / dt);
                dts.Add(dt);
            }

            float spdSum = 0f, spdMax = 0f;
            foreach (var v in vels) { spdSum += v.magnitude; spdMax = Mathf.Max(spdMax, v.magnitude); }
            r.camSpeedAvg = vels.Count > 0 ? spdSum / vels.Count : 0f;
            r.camSpeedMax = spdMax;

            // ---- 卡顿帧检测（必须先于加速度计算，加速度要用它的结果做过滤） ----
            var sortedDt = new List<float>(dts);
            sortedDt.Sort();
            float medianDt = sortedDt.Count > 0 ? sortedDt[sortedDt.Count / 2] : 1f / 60f;
            r.maxFrameDt = sortedDt.Count > 0 ? sortedDt[sortedDt.Count - 1] : 0f;
            int hitch = 0;
            foreach (var d in dts) if (d > medianDt * 3f) hitch++;
            r.hitchFrames = hitch;

            // ---- 相机加速度 ----
            // 坑（已修，别重犯）：逐帧有限差分算加速度会被 1/Δt 放大噪声。
            //   本协程在 Update 里采样，而 ThirdPersonCamera 在 LateUpdate 更新相机，
            //   平时两者节奏一致；一旦出现卡顿帧（编辑器内录 MP4 必然出现，
            //   帧长 30~45ms vs 中位 5~7ms），采样帧与相机更新帧数就对不上，
            //   相机会出现"某一帧没动、下一帧补上"的 Δv 尖刺，Δv/Δt 直接飙到几百 m/s²，
            //   把整段 RMS 拉爆 —— 这是度量假象，不是相机真的抖。
            //   对照实验（同一套相机运动）：不录制 RMS≈21，录制 RMS≈153；
            //   而屏幕漂移两种情况下都是 0.03（卡顿同时推移角色与相机，相对位置不变）。
            //   所以：判定只用"剔除卡顿帧对"后的 camAccelRmsClean，原始值仅作诊断输出。
            float accSq = 0f, accSqClean = 0f;
            int accN = 0, accNClean = 0, excludedPairs = 0;
            for (int i = 1; i < vels.Count; i++)
            {
                float dt = Mathf.Max(dts[i], 1e-5f);
                float a = ((vels[i] - vels[i - 1]) / dt).sqrMagnitude;
                accSq += a; accN++;

                if (dts[i] > medianDt * 3f || dts[i - 1] > medianDt * 3f)
                    excludedPairs++;
                else
                { accSqClean += a; accNClean++; }
            }
            r.camAccelRms = accN > 0 ? Mathf.Sqrt(accSq / accN) : 0f;
            r.camAccelRmsClean = accNClean > 0 ? Mathf.Sqrt(accSqClean / accNClean) : 0f;
            r.accelExcludedPairs = excludedPairs;

            // ---- 方向一致性 ----
            Vector3 expected = Vector3.zero;
            if (Mathf.Abs(moveInput.x) > 0.01f) expected += samples[0].camRight * Mathf.Sign(moveInput.x);
            if (Mathf.Abs(moveInput.y) > 0.01f) expected += samples[0].camForward * Mathf.Sign(moveInput.y);
            Vector3 actual = Vector3.ProjectOnPlane(r.netDisplacement, Vector3.up);
            r.expectDot = (expected.sqrMagnitude > 0.01f && actual.sqrMagnitude > 0.01f)
                ? Vector3.Dot(actual.normalized, expected.normalized)
                : 1f;

            return r;
        }

        // ==================================================================
        // 分析：S2
        // ==================================================================

        private static CombatResult AnalyzeCombat(
            string name, List<CombatSample> s, Vector2 moveInput, CombatContext ctx, VfxBaseline bl)
        {
            var r = new CombatResult { name = name, samples = s.Count };
            if (s.Count < 3) return r;

            float dur = 0f;
            for (int i = 1; i < s.Count; i++) dur += s[i].dt;
            r.duration = dur;

            float dist = 0f, speedSum = 0f, speedMax = 0f;
            float yMin = float.MaxValue, yMax = float.MinValue;
            int groundedN = 0, screenN = 0;
            float screenSum = 0f;
            var states = new List<string>();
            var timeline = new List<string>();

            for (int i = 0; i < s.Count; i++)
            {
                var x = s[i];
                speedSum += x.speed;
                speedMax = Mathf.Max(speedMax, x.speed);
                if (i > 0) dist += Vector3.Distance(x.pos, s[i - 1].pos);
                if (!states.Contains(x.anim)) { states.Add(x.anim); timeline.Add(x.anim + "@" + F(x.t)); }
                if (x.grounded) groundedN++;
                yMin = Mathf.Min(yMin, x.pos.y);
                yMax = Mathf.Max(yMax, x.pos.y);
                r.minViewport = Vector2.Min(r.minViewport, x.viewport);
                r.maxViewport = Vector2.Max(r.maxViewport, x.viewport);
                if (x.screenHeight >= 0f) { screenSum += x.screenHeight; screenN++; }
                if (x.phase == ActionPhase.Attack) r.sawAttack = true;
                if (x.phase == ActionPhase.Dash) r.sawDash = true;
                if (x.cancelWindow && r.cancelWindowFirstT < 0f) r.cancelWindowFirstT = x.t;
                if (x.cancelWindow) r.sawCancelWindow = true;
                r.comboStepMax = Mathf.Max(r.comboStepMax, x.comboStep);

                r.upperWeightMin = Mathf.Min(r.upperWeightMin, x.upperWeight);
                r.upperWeightMax = Mathf.Max(r.upperWeightMax, x.upperWeight);
                r.upperWeightAvg += x.upperWeight;

                r.motionSpeedMin = Mathf.Min(r.motionSpeedMin, x.motionSpeed);
                r.motionSpeedMax = Mathf.Max(r.motionSpeedMax, x.motionSpeed);

                // 躯干倾角（问题 1）与刀刃速度（问题 5）
                r.torsoLeanAvg += x.torsoLeanDeg;
                r.torsoLeanMin = Mathf.Min(r.torsoLeanMin, x.torsoLeanDeg);
                r.torsoLeanMax = Mathf.Max(r.torsoLeanMax, x.torsoLeanDeg);
                r.bladeSpeedPeak = Mathf.Max(r.bladeSpeedPeak, x.bladeSpeed);
                if (x.emitStartSpeed >= 0f)
                {
                    r.emitStartSpeedMin = Mathf.Min(r.emitStartSpeedMin, x.emitStartSpeed);
                    r.emitStartSamples++;
                }

                r.arcActivePeak = Mathf.Max(r.arcActivePeak, x.arcActive);
                r.trailVertexPeak = Mathf.Max(r.trailVertexPeak, x.trailVerts);
                if (x.trailEmitting) r.trailActiveFrames++;

                r.camDistanceAvg += x.camDistance;
                r.camDistanceMin = Mathf.Min(r.camDistanceMin, x.camDistance);
                r.camDistanceMax = Mathf.Max(r.camDistanceMax, x.camDistance);
                r.camFovAvg += x.camFov;
            }

            r.distance = dist;
            r.avgSpeed = speedSum / s.Count;
            r.maxSpeed = speedMax;
            r.playerYRange = yMax - yMin;
            r.groundedRatio = (float)groundedN / s.Count;
            r.netDisplacement = s[s.Count - 1].pos - s[0].pos;
            r.animStates = string.Join(" -> ", states.ToArray());
            states.Sort(System.StringComparer.Ordinal);
            r.animStateSet = string.Join(",", states.ToArray());
            r.animTimeline = string.Join("  ", timeline.ToArray());
            r.screenHeightSamples = screenN;
            r.screenHeightAvg = screenN > 0 ? screenSum / screenN : 0f;
            r.upperWeightAvg /= s.Count;
            r.torsoLeanAvg /= s.Count;
            r.camDistanceAvg /= s.Count;
            r.camFovAvg /= s.Count;

            // ---- 相机逻辑角度范围（解卷绕；不含震屏扰动） ----
            float yawAcc = 0f, pitAcc = 0f, yawMin = 0f, yawMax = 0f, pitMin = 0f, pitMax = 0f;
            float py = s[0].camYaw, pp = s[0].camPitch;
            for (int i = 1; i < s.Count; i++)
            {
                yawAcc += Mathf.DeltaAngle(py, s[i].camYaw);
                pitAcc += Mathf.DeltaAngle(pp, s[i].camPitch);
                py = s[i].camYaw; pp = s[i].camPitch;
                yawMin = Mathf.Min(yawMin, yawAcc); yawMax = Mathf.Max(yawMax, yawAcc);
                pitMin = Mathf.Min(pitMin, pitAcc); pitMax = Mathf.Max(pitMax, pitAcc);
            }
            r.camYawRange = yawMax - yawMin;
            r.camPitchRange = pitMax - pitMin;

            // ---- 卡顿帧 ----
            var dts = new List<float>(s.Count);
            for (int i = 1; i < s.Count; i++) dts.Add(s[i].dt);
            var sorted = new List<float>(dts);
            sorted.Sort();
            float median = sorted.Count > 0 ? sorted[sorted.Count / 2] : 1f / 60f;
            r.hitchFrames = 0;
            foreach (var d in dts) if (d > median * 3f) r.hitchFrames++;

            // ---- 帧速度 / 瞬移检测（剔除卡顿帧；冲刺帧另算） ----
            for (int i = 1; i < s.Count; i++)
            {
                float dt = Mathf.Max(s[i].dt, 1e-5f);
                Vector3 d = s[i].pos - s[i - 1].pos;
                d.y = 0f;
                float v = d.magnitude / dt;
                r.maxRawFrameSpeed = Mathf.Max(r.maxRawFrameSpeed, v);

                bool hitch = s[i].dt > median * 3f;
                if (hitch) continue;

                bool dashing = s[i].phase == ActionPhase.Dash || s[i - 1].phase == ActionPhase.Dash;
                if (dashing) continue;

                r.maxCleanFrameSpeed = Mathf.Max(r.maxCleanFrameSpeed, v);
                r.maxFrameDisplacement = Mathf.Max(r.maxFrameDisplacement, d.magnitude);
                if (v >= TeleportSpeedThreshold || d.magnitude >= TeleportDisplacementThreshold)
                    r.teleportFrames++;
            }

            // ---- 步伐同步：稳态窗口内反推片段地面速度 ----
            int from = Mathf.FloorToInt(s.Count * 0.6f);
            float impSum = 0f; int impN = 0, msSumN = 0; float msSum = 0f;
            float stepSum = 0f; int stepN = 0;
            for (int i = from; i < s.Count; i++)
            {
                msSum += s[i].motionSpeed; msSumN++;
                if (s[i].speed > 0.3f && s[i].motionSpeed > 0.1f)
                { impSum += s[i].speed / s[i].motionSpeed; impN++; }
                // 步频：循环里 2 步，循环时长 = 片段时长 / 播放倍率
                if (s[i].clipLen > 0.05f && s[i].motionSpeed > 0.05f)
                { stepSum += (2f / s[i].clipLen) * s[i].motionSpeed; stepN++; }
            }
            r.motionSpeedAvg = msSumN > 0 ? msSum / msSumN : 0f;
            r.impliedGroundSpeedAvg = impN > 0 ? impSum / impN : 0f;
            r.impliedSpeedSamples = impN;
            r.stepsPerSecAvg = stepN > 0 ? stepSum / stepN : 0f;

            // ---- 特效增量 ----
            if (ctx.vfx != null)
            {
                r.swingDelta = ctx.vfx.SwingCount - bl.swing;
                r.arcSpawnDelta = ctx.vfx.ArcSpawnCount - bl.arc;
                r.shakeDelta = ctx.vfx.ShakeTriggerCount - bl.shake;
            }

            // ---- 连击段位起点与位移学 ----
            // 先一次性收齐所有段位起点：每段的分析窗口必须**裁到下一段起点为止**，
            // 否则下一段的前冲会算进上一段的位移（实测会虚高 2~3 倍，把判定搅乱）。
            var onsets = new List<int>();
            int prevStep = s[0].comboStep;
            for (int i = 1; i < s.Count; i++)
            {
                if (s[i].comboStep > prevStep && s[i].comboStep >= 1) onsets.Add(i);
                prevStep = s[i].comboStep;
            }

            for (int k = 0; k < onsets.Count; k++)
            {
                int i = onsets[k];
                int step = s[i].comboStep;
                int endCap = (k + 1 < onsets.Count) ? onsets[k + 1] : s.Count;

                var sr = new StepReport
                {
                    step = step,
                    onsetT = s[i].t,
                    onsetPhaseTime = s[i].phaseTime,
                    lungeClampDistance = ctx.ctl.comboLungeDistance[
                        Mathf.Clamp(step - 1, 0, ctx.ctl.comboLungeDistance.Length - 1)],
                    lungeDuration = ctx.ctl.comboLungeDuration[
                        Mathf.Clamp(step - 1, 0, ctx.ctl.comboLungeDuration.Length - 1)]
                };

                int fastEnd = Mathf.Min(FirstIndexAfter(s, s[i].t + sr.lungeDuration + 0.05f), endCap);
                int fullEnd = Mathf.Min(FirstIndexAfter(s, s[i].t + 0.75f), endCap);

                float total = PathTo(s, i, fullEnd);
                float acc = 0f;
                for (int j = i + 1; j < fullEnd; j++)
                {
                    float seg = Planar(s[j].pos - s[j - 1].pos);
                    acc += seg;
                    if (total > 1e-4f && sr.t90 <= 0f && acc >= total * 0.9f) sr.t90 = s[j].t - s[i].t;

                    float dt = Mathf.Max(s[j].dt, 1e-5f);
                    if (s[j].phase != ActionPhase.Dash && s[j - 1].phase != ActionPhase.Dash && s[j].dt <= median * 3f)
                        sr.peakSpeed = Mathf.Max(sr.peakSpeed, seg / dt);

                    // 前冲曲线是否真被施加：看「期望速度」的峰值（含 y 剔除）
                    sr.peakDesired = Mathf.Max(sr.peakDesired,
                        new Vector2(s[j].desiredVel.x, s[j].desiredVel.z).magnitude);
                    if (s[j].phase == ActionPhase.Attack) sr.attackFrames++;
                }
                sr.lungeTotal = total;
                sr.lungeFast = PathTo(s, i, fastEnd);
                if (sr.t90 <= 0f && fullEnd > i + 1) sr.t90 = s[fullEnd - 1].t - s[i].t;
                r.steps.Add(sr);
            }

            return r;
        }

        /// <summary>[from+1, toExclusive) 区间的水平路径长度。</summary>
        private static float PathTo(List<CombatSample> s, int from, int toExclusive)
        {
            float acc = 0f;
            int hi = Mathf.Min(toExclusive, s.Count);
            for (int j = from + 1; j < hi; j++)
                acc += Planar(s[j].pos - s[j - 1].pos);
            return acc;
        }

        /// <summary>第一个 t &gt; t0 的下标；找不到返回样本数。</summary>
        private static int FirstIndexAfter(List<CombatSample> s, float t0)
        {
            for (int j = 0; j < s.Count; j++) if (s[j].t > t0) return j;
            return s.Count;
        }

        private static float Planar(Vector3 v) => new Vector2(v.x, v.z).magnitude;

        // ==================================================================
        // 公共工具
        // ==================================================================

        private static readonly int HashMotionSpeed = Animator.StringToHash("MotionSpeed");

        private static CombatContext ResolveContext()
        {
            var playerGo = GameObject.Find("Player");
            if (playerGo == null) return null;

            var cam = Camera.main;
            var ctx = new CombatContext
            {
                playerGo = playerGo,
                ctl = playerGo.GetComponent<PlayerController>(),
                anim = playerGo.GetComponentInChildren<Animator>(),
                cam = cam,
                rig = cam != null ? cam.GetComponent<ThirdPersonCamera>() : null,
                vfx = playerGo.GetComponent<SwordVfx>(),
                pose = playerGo.GetComponentInChildren<WeaponHandPose>(true)
            };
            if (ctx.ctl == null || ctx.anim == null || cam == null) return null;
            ctx.upperLayerIndex = FindLayerIndex(ctx.anim, "UpperBody");
            if (ctx.anim.isHuman)
            {
                ctx.hips = ctx.anim.GetBoneTransform(HumanBodyBones.Hips);
                // 降级链：KayKit 的 Rig_Medium 只有 hips/spine/head，**没有 chest**。
                // 直接取 Chest 会拿到 null，TorsoLeanDeg 便恒返回 0（断言假通过）。
                // 依次降到 UpperChest / Neck / Head —— 只要有一根在髋上方就能量躯干朝向。
                ctx.chest = ctx.anim.GetBoneTransform(HumanBodyBones.Chest);
                if (ctx.chest == null) ctx.chest = ctx.anim.GetBoneTransform(HumanBodyBones.UpperChest);
                if (ctx.chest == null) ctx.chest = ctx.anim.GetBoneTransform(HumanBodyBones.Neck);
                if (ctx.chest == null) ctx.chest = ctx.anim.GetBoneTransform(HumanBodyBones.Head);
                ctx.torsoLeanAvailable = ctx.hips != null && ctx.chest != null;
            }
            return ctx;
        }

        /// <summary>
        /// 第 1 层及以后的**附加层总权重**。
        /// 本版把 UpperBody 遮罩层整个删掉了（它会把攻击的上半身锁死在抱臂待机），
        /// 所以这个值应该恒为 0 —— 验收用它证明"不再有层在偷偷改姿势"。
        /// </summary>
        private static float ExtraLayerWeight(Animator anim)
        {
            float w = 0f;
            for (int i = 1; i < anim.layerCount; i++) w += anim.GetLayerWeight(i);
            return w;
        }

        /// <summary>
        /// 躯干倾角（度）：正 = 前倾，负 = 后仰。
        /// 取「髋 → 胸」方向的竖直夹角，绕角色右轴符号化 ——
        /// 这是「仰着身子走」唯一可靠的量化方式，光看骨骼位置是看不出来的。
        /// </summary>
        private static float TorsoLeanDeg(CombatContext ctx)
        {
            if (ctx.hips == null || ctx.chest == null) return 0f;

            Vector3 fwd = ctx.playerGo.transform.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
            fwd.Normalize();

            Vector3 spineFull = ctx.chest.position - ctx.hips.position;
            float alongFwd = Vector3.Dot(spineFull, fwd);
            float alongUp = spineFull.y;
            // atan2(前向分量, 竖直分量)：正 = 前倾，负 = 后仰
            return Mathf.Atan2(alongFwd, Mathf.Max(Mathf.Abs(alongUp), 1e-4f)) * Mathf.Rad2Deg;
        }

        /// <summary>
        /// 按名字找 Animator 层下标。找不到退化为 1 —— 不允许写死，层顺序会变。
        /// 只用运行时 API（Animator.layerCount / GetLayerName），不碰 UnityEditor.Animations，
        /// 否则这份运行脚本会把编辑器程序集拖进 Player 构建。
        /// </summary>
        private static int FindLayerIndex(Animator anim, string layerName)
        {
            for (int i = 0; i < anim.layerCount; i++)
                if (anim.GetLayerName(i) == layerName) return i;
            return anim.layerCount > 1 ? 1 : 0;
        }

        /// <summary>
        /// 把角色复位到锚点。
        /// CharacterController 必须先禁用，否则赋值位置会被它回推；
        /// 同时把状态机拉回移动阶段并清空速度 —— 否则上一段遗留的攻击后摇 / 残留滑行
        /// 会带进下一段的采样窗口，起点就不可比了。
        /// </summary>
        private static void ResetPlayer(GameObject playerGo, PlayerController ctl)
        {
            var cc = playerGo.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            playerGo.transform.position = StageAnchor;
            playerGo.transform.rotation = Quaternion.identity;
            if (cc != null) cc.enabled = true;

            if (ctl != null) ctl.ResetToLocomotion();
        }

        private static string CurrentState(Animator anim)
        {
            var st = anim.GetCurrentAnimatorStateInfo(0);
            for (int i = 0; i < kBaseStateNames.Length; i++)
                if (st.IsName(kBaseStateNames[i])) return kBaseStateNames[i];
            return "Other";
        }

        /// <summary>
        /// 当前状态的动画片段时长（秒）。用来把「播放倍率」换算成**真实步频**：
        /// 步频 = 2 步/循环 ÷（片段时长 ÷ 播放倍率）。
        /// 两段片段长度不同（走路 1.33s / 跑步 0.67s），只比倍率会把结论带偏。
        /// </summary>
        private static float CurrentClipLength(Animator anim)
        {
            var info = anim.GetCurrentAnimatorClipInfo(0);
            if (info == null || info.Length == 0 || info[0].clip == null) return 0f;
            return info[0].clip.length;
        }

        private static StageResult Find(List<StageResult> list, string name)
        {
            foreach (var s in list) if (s.name == name) return s;
            return null;
        }

        private static CombatResult FindC(List<CombatResult> list, string name)
        {
            foreach (var s in list) if (s.name == name) return s;
            return null;
        }

        private static StepReport FindStep(CombatResult r, int step)
        {
            foreach (var s in r.steps) if (s.step == step) return s;
            return null;
        }

        private static string JoinFloats(float[] a)
        {
            var parts = new string[a.Length];
            for (int i = 0; i < a.Length; i++) parts[i] = N(a[i]);
            return string.Join(", ", parts);
        }

        private static string F(float v) => v.ToString("F3", CultureInfo.InvariantCulture);
        private static string N(float v) => v.ToString("0.##", CultureInfo.InvariantCulture);
        private static string V(Vector3 v) => "(" + F(v.x) + ", " + F(v.y) + ", " + F(v.z) + ")";

        private static void AppendCombatStage(StringBuilder sb, CombatResult s, PlayerController ctl)
        {
            sb.AppendLine();
            sb.AppendLine("[" + s.name + "]  " + s.samples + " 帧 / " + F(s.duration) + "s");
            sb.AppendLine("   位移净距 : " + F(s.netDisplacement.magnitude) + " m   方向 " + V(s.netDisplacement)
                + "   路径长度 " + F(s.distance) + " m");
            sb.AppendLine("   实际速度 : 平均 " + F(s.avgSpeed) + "  峰值 " + F(s.maxSpeed) + " m/s");
            sb.AppendLine("   帧速度   : 最大 " + F(s.maxCleanFrameSpeed) + " m/s（剔除卡顿与冲刺帧）"
                + "  最大单帧位移 " + F(s.maxFrameDisplacement) + " m  尖峰帧 " + s.teleportFrames + " 个");
            sb.AppendLine("   角色贴地 : Y 波动 " + F(s.playerYRange) + " m，着地帧占比 " + F(s.groundedRatio * 100f) + "%");
            sb.AppendLine("   屏幕位置 : x[" + F(s.minViewport.x) + "~" + F(s.maxViewport.x)
                + "] y[" + F(s.minViewport.y) + "~" + F(s.maxViewport.y) + "]"
                + "  漂移 " + F(s.maxViewport.x - s.minViewport.x) + " x " + F(s.maxViewport.y - s.minViewport.y));
            sb.AppendLine("   占屏高度 : " + F(s.screenHeightAvg * 100f) + "%（" + s.screenHeightSamples + " 帧有效）");
            sb.AppendLine("   相机     : 距离 平均 " + F(s.camDistanceAvg)
                + " 区间[" + F(s.camDistanceMin) + "~" + F(s.camDistanceMax) + "] m"
                + "  FOV 平均 " + F(s.camFovAvg)
                + "  偏航波动 " + F(s.camYawRange) + "° 俯仰波动 " + F(s.camPitchRange) + "°");
            sb.AppendLine("   步伐同步 : MotionSpeed 区间 " + F(s.motionSpeedMin) + "~" + F(s.motionSpeedMax)
                + "  稳态平均 " + F(s.motionSpeedAvg)
                + "  反推片段速度 " + F(s.impliedGroundSpeedAvg) + " m/s（样本 "
                + s.impliedSpeedSamples + "，应等于该状态自己的参考速度）");
            sb.AppendLine("   躯干倾角 : 平均 " + F(s.torsoLeanAvg) + "°  区间["
                + F(s.torsoLeanMin) + "~" + F(s.torsoLeanMax) + "]°  (正=前倾 / 负=后仰)");
            sb.AppendLine("   刀刃速度 : 峰值 " + F(s.bladeSpeedPeak) + " m/s"
                + (s.emitStartSamples > 0
                    ? "  拖尾起播速度 " + F(s.emitStartSpeedMin) + " m/s（" + s.emitStartSamples + " 帧有值）"
                    : "  本阶段无拖尾发射"));
            sb.AppendLine("   动画状态 : " + (s.animStateSet.Length > 0 ? s.animStateSet : "(未识别)"));
            sb.AppendLine("   附加层   : 权重 平均 " + F(s.upperWeightAvg)
                + " 区间[" + F(s.upperWeightMin) + "~" + F(s.upperWeightMax) + "]（本版无附加层，应为 0）");
            sb.AppendLine("   连击     : 最高段位 " + s.comboStepMax
                + "  取消窗口 " + (s.sawCancelWindow ? "已开启(首开 t=" + F(s.cancelWindowFirstT) + "s)" : "未开启")
                + "  阶段 " + (s.sawAttack ? "含攻击" : "无攻击") + (s.sawDash ? " / 含冲刺" : ""));
            sb.AppendLine("   特效     : 挥砍 " + s.swingDelta + " 次  弧光生成 " + s.arcSpawnDelta
                + " 次  同屏弧光峰值 " + s.arcActivePeak
                + "  拖尾发射帧 " + s.trailActiveFrames + "  拖尾顶点峰值 " + s.trailVertexPeak
                + "  震屏 " + s.shakeDelta + " 次");
            sb.AppendLine("   帧稳定性 : 卡顿帧 " + s.hitchFrames + " 个，最大原始帧速度 " + F(s.maxRawFrameSpeed) + " m/s");
            sb.AppendLine("   动画状态 : " + s.animStates);
            sb.AppendLine("   状态时间线: " + s.animTimeline);
            if (s.steps.Count > 0)
            {
                sb.AppendLine("   连击位移学:");
                foreach (var st in s.steps)
                    sb.AppendLine(string.Format(
                        "     第 {0} 段 起点 t={1}s  设定 {2}m/{3}s  →  实测总位移 {4}m"
                        + "（设定时长内 {5}m）  90% 耗时 {6}s  峰值速度 {7} m/s"
                        + "  期望速度峰值 {8}  Attack 帧 {9}  起点 PhaseTime {10}",
                        st.step, F(st.onsetT), N(st.lungeClampDistance), N(st.lungeDuration),
                        F(st.lungeTotal), F(st.lungeFast), F(st.t90), F(st.peakSpeed),
                        F(st.peakDesired), st.attackFrames, F(st.onsetPhaseTime)));
            }
        }

        // ==================================================================
        // S3 敌人验收（M3：有对手）
        // ==================================================================

        private static int _m3Pass, _m3Fail;

        private static void M3(StringBuilder sb, bool ok, string label, string detail)
        {
            if (ok) _m3Pass++; else _m3Fail++;
            sb.AppendLine("[" + (ok ? "通过" : "未通过") + "] " + label
                          + (string.IsNullOrEmpty(detail) ? "" : "    " + detail));
        }

        private static void TeleportPlayer(GameObject go, Vector3 pos)
        {
            var cc = go.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            go.transform.position = pos;
            if (cc != null) cc.enabled = true;
        }

        /// <summary>把场上所有活着的敌人一次性打死（验收里推进波次用，不走动画时间）。</summary>
        private static int KillAllAlive()
        {
            int n = 0;
            foreach (var e in UnityEngine.Object.FindObjectsOfType<EnemyBase>())
            {
                if (e == null || !e.IsAlive) continue;
                var d = new DamageInfo
                {
                    amount = 100000f,
                    sourceFaction = Faction.Player,
                    hitDirection = Vector3.zero,
                    knockback = 0f,
                    hitStun = 0f,
                    hitStop = 0f,
                };
                e.TakeDamage(d);
                n++;
            }
            return n;
        }

        private static List<EnemyBase> AliveEnemies()
        {
            var list = new List<EnemyBase>();
            foreach (var e in UnityEngine.Object.FindObjectsOfType<EnemyBase>())
                if (e != null && e.IsAlive) list.Add(e);
            return list;
        }

        private static float MinDistanceTo(GameObject go, List<EnemyBase> list)
        {
            float best = float.MaxValue;
            foreach (var e in list)
            {
                if (e == null) continue;
                float d = Vector3.Distance(go.transform.position, e.transform.position);
                if (d < best) best = d;
            }
            return best;
        }

        /// <summary>
        /// Sprint 3 验收：NavMesh → 波次生成 → 追击收敛 → 敌人攻击玩家 → 无敌帧 →
        /// 玩家攻击敌人（掉血/硬直）→ 击杀推进波次 → 全清开门。
        ///
        /// 度量口径上的两条特别注意：
        /// 1) 全程 **关掉顿帧**（<see cref="HitStop.Enabled"/>）—— 时间缩放会让"等待 N 秒"和
        ///    "N 秒内走了多远"全部失真，且失真程度取决于命中次数（不可复现）。
        /// 2) "追击有效"的判据用**与玩家的距离收敛**，不用"敌人位移了多少"：
        ///    敌人可能被柱子卡住绕路，位移很大但没靠近。距离才是玩家真正感知到的东西。
        /// </summary>
        public static IEnumerator S3EnemyFlow()
        {
            var sb = new StringBuilder();
            _m3Pass = 0; _m3Fail = 0;

            var ctx = ResolveContext();
            if (ctx == null) { WriteReport("S3", "<错误> 场景里找不到 Player / PlayerController"); yield break; }

            var go = ctx.playerGo;
            var ctl = ctx.ctl;
            var health = go.GetComponent<PlayerHealth>();
            var stance = go.GetComponentInChildren<CombatStance>(true);
            var spawner = UnityEngine.Object.FindObjectOfType<WaveSpawner>();
            var room = UnityEngine.Object.FindObjectOfType<RoomController>();
            if (health == null) { WriteReport("S3", "<错误> Player 上没有 PlayerHealth（预制体没保存？）"); yield break; }
            if (spawner == null) { WriteReport("S3", "<错误> 场景里找不到 WaveSpawner"); yield break; }

            sb.AppendLine("======================================================================");
            sb.AppendLine("Sprint 3 验收 —— 敌人 AI 与关卡组织（M3：有对手）");
            sb.AppendLine("生成时间 " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("======================================================================");

            bool oldHitStop = HitStop.Enabled;
            HitStop.Enabled = false;                       // 见方法注释：顿帧会让所有时长失真

            ctl.BeginInputOverride();
            ctl.SetInjectedMove(Vector2.zero, false);
            if (stance != null) stance.combatExitDelay = 99999f;

            // ---------------- ① 环境 ----------------
            sb.AppendLine();
            sb.AppendLine("---- ① NavMesh 环境 ----");
            var tri = UnityEngine.AI.NavMesh.CalculateTriangulation();
            M3(sb, tri.indices.Length > 0, "NavMesh 已烘焙",
                "顶点 " + tri.vertices.Length + " / 三角 " + (tri.indices.Length / 3));
            int walkable = 0; int probe = 0;
            for (int i = 0; i < 8; i++)
            {
                float ang = i * Mathf.PI * 2f / 8f;
                var p = new Vector3(Mathf.Sin(ang) * 8f, 1f, Mathf.Cos(ang) * 8f);
                UnityEngine.AI.NavMeshHit nh;
                probe++;
                if (UnityEngine.AI.NavMesh.SamplePosition(p, out nh, 3f, UnityEngine.AI.NavMesh.AllAreas)) walkable++;
            }
            M3(sb, walkable == probe, "场地四周 8 个取样点全部可寻路", walkable + "/" + probe);

            // ---------------- ② 复位 + 生成第一波 ----------------
            sb.AppendLine();
            sb.AppendLine("---- ② 波次生成 ----");

            // 先把残留敌人清掉、把波次状态机**整个**复位。
            // 理由：`--runtime` 跑完不会自动退出 Play，第二个脚本完全可能落在同一个还活着的
            // 会话里；那时 spawner 已经 _allCleared，Begin() 直接 return，
            // 于是"一只怪都没刷出来"却零报错 —— 这种静默失败必须在源头挡住，
            // 不能指望每个调用者都记得先 stop。
            KillAllAlive();
            { float t = Time.time; while (Time.time - t < 0.3f) yield return null; }
            spawner.ResetForTest();
            EnemyBase.AliveCount = 0; EnemyBase.TotalKills = 0; EnemyBase.TotalSpawned = 0;
            if (room != null) room.ResetForTest();
            health.ResetHealth(); health.ResetDiagnostics(); health.ClearInvincibility();

            TeleportPlayer(go, new Vector3(0f, 0.45f, -3f));
            ctl.ResetToLocomotion();
            { float t = Time.time; while (Time.time - t < 0.9f) yield return null; }

            M3(sb, spawner.WaveCount == 3, "波次配置 = 3 波", "实际 " + spawner.WaveCount);
            spawner.Begin();

            { float t = Time.time; while (spawner.SpawnedCount < 3 && Time.time - t < 10f) yield return null; }
            M3(sb, spawner.SpawnedCount == 3, "第一波刷出 3 只墨徒", "实际 " + spawner.SpawnedCount);

            var alive = AliveEnemies();
            int onNav = 0;
            foreach (var e in alive)
            {
                var ag = e.GetComponent<UnityEngine.AI.NavMeshAgent>();
                if (ag != null && ag.enabled && ag.isOnNavMesh) onNav++;
            }
            M3(sb, alive.Count == 3 && onNav == 3, "3 只敌人全部落在导航网格上（可寻路）",
                onNav + "/" + alive.Count + "   生成失败次数 " + spawner.SpawnFailedCount);
            sb.AppendLine("   出生点（出生圈半径应 < 房间内净半径，否则会被门挡住视线）：");
            foreach (var e in alive)
                sb.AppendLine("     " + e.enemyName + " @ " + e.transform.position.ToString("F2")
                              + "   距玩家 " + F(e.DistanceToPlayer) + " m   朝向 " + e.transform.forward.ToString("F2"));

            // ---------------- ③ 追击收敛 ----------------
            sb.AppendLine();
            sb.AppendLine("---- ③ 追击 ----");
            float d0 = MinDistanceTo(go, alive);
            float minD = d0;
            int attackSeen = 0;
            { float t = Time.time; while (Time.time - t < 5.0f) yield return null; }
            alive = AliveEnemies();
            minD = Mathf.Min(minD, MinDistanceTo(go, alive));
            foreach (var e in alive) attackSeen += e.AttackCount;
            M3(sb, minD < d0 - 3f, "敌人主动接近玩家（距离收敛）",
                "起始最近 " + F(d0) + " m → 收敛到 " + F(minD) + " m");
            M3(sb, attackSeen > 0, "有敌人进入了攻击状态", "合计起手 " + attackSeen + " 次");
            sb.AppendLine("   敌人状态快照：");
            foreach (var e in alive)
                sb.AppendLine("     " + e.enemyName + "  状态=" + e.State + "  HP=" + F(e.Health)
                    + "  距玩家=" + F(e.DistanceToPlayer) + " m  起手=" + e.AttackCount);

            // ---------------- ④ 敌人打到玩家 ----------------
            sb.AppendLine();
            sb.AppendLine("---- ④ 敌人攻击 → 玩家掉血 ----");
            health.ResetHealth(); health.ResetDiagnostics(); health.ClearInvincibility();
            { float t = Time.time; while (health.DamageTakenCount == 0 && Time.time - t < 14f) yield return null; }
            M3(sb, health.DamageTakenCount > 0, "敌人的攻击确实打到了玩家",
                "受击 " + health.DamageTakenCount + " 次，剩余 HP " + F(health.Health) + " / " + F(health.maxHealth));
            M3(sb, health.Health < health.maxHealth, "玩家生命值下降", F(health.Health) + " < " + F(health.maxHealth));

            // ---------------- ⑤ 无敌帧 ----------------
            sb.AppendLine();
            sb.AppendLine("---- ⑤ 受击无敌帧 ----");
            health.ResetHealth(); health.ResetDiagnostics(); health.ClearInvincibility();
            var probeDmg = new DamageInfo { amount = 10f, sourceFaction = Faction.Enemy };
            bool r1 = health.TakeDamage(probeDmg);
            bool r2 = health.TakeDamage(probeDmg);
            M3(sb, r1 && !r2, "受击后短暂无敌：连续两次只吃一次伤害",
                "第一次=" + r1 + " 第二次=" + r2 + "  HP=" + F(health.Health)
                + "  被无敌挡下 " + health.DamageBlockedByIFrameCount + " 次");

            // ---------------- ⑥ 玩家攻击敌人（掉血 + 硬直 + 击退）----------------
            sb.AppendLine();
            sb.AppendLine("---- ⑥ 玩家攻击 → 敌人受击 ----");
            alive = AliveEnemies();
            EnemyBase target = null; float bestD = float.MaxValue;
            foreach (var e in alive)
            {
                float d = Vector3.Distance(go.transform.position, e.transform.position);
                if (d < bestD) { bestD = d; target = e; }
            }
            if (target == null)
            {
                M3(sb, false, "找一个活着的敌人来打", "场上没有活着的敌人");
            }
            else
            {
                // 贴到它面前 1.7 m 并面向它（不靠"走过去"—— 走路会引入路径不确定性）
                Vector3 to = target.transform.position - go.transform.position; to.y = 0f;
                if (to.sqrMagnitude < 1e-4f) to = Vector3.forward;
                to.Normalize();
                TeleportPlayer(go, target.transform.position - to * 1.7f + Vector3.up * 0.1f);
                go.transform.rotation = Quaternion.LookRotation(to, Vector3.up);
                ctl.ResetToLocomotion();
                if (stance != null) stance.ForceStance(true);   // 保证剑在手上（判定体要跟着剑走）
                { float t = Time.time; while (Time.time - t < 0.5f) yield return null; }

                float hp0 = target.Health;
                int dt0 = target.DamageTakenCount;
                bool sawStun = false;
                bool hitSeen = false;
                float knock = 0f;
                Vector3 p0 = target.transform.position;
                ctl.RequestInjectedAttack();
                float t0 = Time.time;
                float hitAt = -1f;
                while (Time.time - t0 < 1.6f)
                {
                    if (target == null || !target.IsAlive) break;
                    if (target.State == EnemyState.HitStun) sawStun = true;

                    if (target.DamageTakenCount > dt0)
                    {
                        hitSeen = true;
                        if (hitAt < 0f) hitAt = Time.time;
                    }
                    // ★ 击退位移必须**在命中之后继续采样一段时间**。
                    //   早期版本一见"掉血 + 硬直"就 break，量到的是"命中那一帧到下一帧"的位移
                    //   （实测 0.021 m）。名义击退 2.6 m/s、硬直 0.26 s，理论位移约 0.28 m ——
                    //   也就是说那条断言其实一直"通过"着，只是量的是一个几乎为零的假数字。
                    //   度量窗口必须覆盖事件（这里是击退过程），不能停在事件起点。
                    if (hitSeen) knock = Mathf.Max(knock, Vector3.Distance(p0, target.transform.position));
                    if (hitSeen && hitAt > 0f && Time.time - hitAt > 0.45f) break;
                    yield return null;
                }
                knock = Mathf.Max(knock, Vector3.Distance(p0, target.transform.position));

                bool hurt = target == null || target.Health < hp0;
                M3(sb, hurt, "玩家的剑打中了敌人（敌人掉血）",
                    "HP " + F(hp0) + " → " + F(target != null ? target.Health : 0f)
                    + "  受击次数 " + (target != null ? target.DamageTakenCount - dt0 : 0));
                M3(sb, sawStun, "敌人进入受击硬直", "观测到 HitStun = " + sawStun);
                M3(sb, knock > 0.10f, "敌人被击退（位置发生位移）",
                    "击退峰值位移 " + F(knock) + " m（名义 击退 2.6 m/s × 硬直 0.26 s ≈ 0.28 m）");

                var sh = go.transform.Find("SwordHitbox");
                var hb = sh != null ? sh.GetComponent<Hitbox>() : null;
                if (hb != null)
                {
                    M3(sb, hb.HitCount > 0, "玩家判定体记录到命中",
                        "开窗 " + hb.ActivationCount + " 次 / 命中 " + hb.HitCount + " 次");
                }
            }

            // ---------------- ⑦ 击杀推进波次 ----------------
            sb.AppendLine();
            sb.AppendLine("---- ⑦ 波次推进 ----");
            int killedNow = KillAllAlive();
            { float t = Time.time; while (spawner.WaveStartedCount < 2 && Time.time - t < 20f) yield return null; }
            M3(sb, spawner.WaveClearedCount >= 1, "第 1 波清空后判定通过",
                "清空 " + spawner.WaveClearedCount + " 波 / 已开 " + spawner.WaveStartedCount + " 波"
                + "  本次击杀 " + killedNow);
            { float t = Time.time; while (spawner.SpawnedCount < 8 && Time.time - t < 20f) yield return null; }
            M3(sb, spawner.SpawnedCount >= 8, "第 2 波按配置刷出（3 墨徒 + 2 墨偶）",
                "累计生成 " + spawner.SpawnedCount);
            var mixed = AliveEnemies();
            int ranged = 0; foreach (var e in mixed) if (e is EnemyRanged) ranged++;
            M3(sb, ranged > 0, "第 2 波里出现了远程敌人（墨偶）", "墨偶 " + ranged + " 只");
            if (ranged > 0)
            {
                // 远程敌人在玩家贴脸时应该后撤 —— 这是它区别于近战的唯一行为
                var r = mixed.Find(x => x is EnemyRanged);
                TeleportPlayer(go, r.transform.position + new Vector3(1.2f, 0.1f, 0f));
                { float t = Time.time; while (Time.time - t < 2.2f) yield return null; }
                var rg = r as EnemyRanged;
                M3(sb, rg != null && (rg.RetreatCount > 0 || rg.ProjectilesSpawned > 0),
                    "墨偶会走位/开火（远程行为生效）",
                    rg != null ? ("后撤 " + rg.RetreatCount + " 次 / 发射 " + rg.ProjectilesSpawned + " 发") : "-");
            }

            // ---------------- ⑧ 全清 + 开门 ----------------
            sb.AppendLine();
            sb.AppendLine("---- ⑧ 全清开门 ----");
            {
                float t = Time.time;
                int guard = 0;
                // guard 只是"活着别死循环"的保险丝，真正的上限是 120 s 的墙钟。
                // 早期版本写成 900 帧（≈15 s）会在 3 波还没刷完时就把循环掐掉，
                // 报出"全清失败"这种假故障 —— 帧数上限必须远大于最坏情况。
                while (!spawner.AllCleared && Time.time - t < 120f && guard++ < 20000)
                {
                    KillAllAlive();
                    yield return null;
                }
            }
            M3(sb, spawner.AllCleared, "三波全部清空", "已开 " + spawner.WaveStartedCount + " 波 / 清 " + spawner.WaveClearedCount + " 波");
            { float t = Time.time; while (Time.time - t < 2.0f) yield return null; }   // 等门沉下去
            if (room != null)
            {
                M3(sb, room.IsCleared, "房间控制器判定「已清空」", "IsCleared=" + room.IsCleared);
                M3(sb, room.GateProgress > 0.95f, "石门下沉完成（开门）",
                    "位移进度 " + F(room.GateProgress) + "，扇数 " + room.OpenedGateCount);
            }
            M3(sb, EnemyBase.TotalKills >= spawner.SpawnedCount && spawner.SpawnedCount >= 8,
                "击杀数 == 生成数（无漏算/幽灵敌人）",
                "击杀 " + EnemyBase.TotalKills + " / 生成 " + spawner.SpawnedCount);

            // ---------------- 收尾 ----------------
            sb.AppendLine();
            sb.AppendLine("---- 汇总 ----");
            sb.AppendLine("通过 " + _m3Pass + " / 未通过 " + _m3Fail);
            sb.AppendLine(_m3Fail == 0 ? ">>> M3 达成（3 种敌人经 NavMesh 追击并围攻，波次生成器可刷出多波）"
                                       : ">>> M3 未达成");

            KillAllAlive();
            ctl.SetInjectedMove(Vector2.zero, false);
            ctl.EndInputOverride();
            if (stance != null) stance.combatExitDelay = 6f;
            HitStop.Enabled = oldHitStop;
            WriteReport("S3", sb.ToString());
            Debug.Log("[PlaytestHarness] S3 结束：通过 " + _m3Pass + " / 未通过 " + _m3Fail);
        }

        // ==================================================================
        // Sprint 4：水墨风格（M4）
        // ==================================================================

        private static int _m4Pass, _m4Fail;

        private static void M4(StringBuilder sb, bool ok, string label, string detail)
        {
            if (ok) _m4Pass++; else _m4Fail++;
            sb.AppendLine("[" + (ok ? "通过" : "未通过") + "] " + label
                          + (string.IsNullOrEmpty(detail) ? "" : "    " + detail));
        }

        /// <summary>阶段截图统计（在给定矩形里取）。</summary>
        private struct Shot
        {
            public float mean;      // 平均亮度 0..1
            public float std;       // 亮度标准差
            public int distinct;    // 量化到 64 级后**出现过的**亮度级数
            public int n80;         // 覆盖 80% 像素所需的亮度级数（**别拿它当主判据**：64 桶上会卡边界，重跑就翻）
            public int dominant3;   // 占比 ≥3% 的亮度级数
            public float topShare;  // 最大那个亮度级占的像素比例
            public float cover4;    // 前 4 种亮度覆盖的像素比例 ← 判"4 阶量化"的主判据
            public Color32[] box;
        }

        private static float Lum(Color32 p) => (p.r * 0.299f + p.g * 0.587f + p.b * 0.114f) / 255f;

        /// <summary>
        /// 统计一个矩形区域。
        ///
        /// 主判据为什么是 <see cref="Shot.n80"/> 而不是"出现过多少级"（distinct）：
        ///   只要画面里有一点抗锯齿边缘、一点贴图噪声，"出现过"的亮度级就会铺满 64 级 ——
        ///   那个数**永远很大，永远证明不了什么**（第一次就是这么误判成"量化没生效"的）。
        ///   而"覆盖 80% 像素需要几个级"对少量噪声像素完全不敏感：
        ///   连续渐变要几十级才凑够 80%，4 阶量化只要 4 级左右。这才量得出来。
        /// </summary>
        private static Shot Analyse(Color32[] px, int w, int h, int x0, int x1, int y0, int y1)
        {
            x0 = Mathf.Clamp(x0, 0, w - 1); x1 = Mathf.Clamp(x1, x0 + 1, w);
            y0 = Mathf.Clamp(y0, 0, h - 1); y1 = Mathf.Clamp(y1, y0 + 1, h);
            int bw = x1 - x0, bh = y1 - y0;
            var box = new Color32[bw * bh];
            var hist = new int[64];
            double sum = 0, sum2 = 0;
            int k = 0;
            for (int y = y0; y < y1; y++)
                for (int x = x0; x < x1; x++)
                {
                    var c = px[y * w + x];
                    box[k++] = c;
                    float l = Lum(c);
                    sum += l; sum2 += l * l;
                    hist[Mathf.Clamp((int)(l * 64f), 0, 63)]++;
                }

            var s = new Shot();
            s.mean = (float)(sum / box.Length);
            s.std = Mathf.Sqrt(Mathf.Max(0f, (float)(sum2 / box.Length) - s.mean * s.mean));
            s.box = box;

            int total = box.Length, top = 0, dom = 0;
            for (int i = 0; i < 64; i++) if (hist[i] > 0) s.distinct++;
            for (int i = 0; i < 64; i++)
            {
                if (hist[i] > top) top = hist[i];
                if (hist[i] >= total * 0.03f) dom++;
            }
            s.topShare = (float)top / total;
            s.dominant3 = dom;

            var sorted = (int[])hist.Clone();
            System.Array.Sort(sorted);
            System.Array.Reverse(sorted);
            int acc = 0, n = 0;
            while (n < 64 && acc < total * 0.80f) { acc += sorted[n]; n++; }
            s.n80 = n;
            // 「前 4 种墨色覆盖了多少像素」：4 阶量化的**直接**表述 ——
            // 它就是"整幅画只剩 4 种墨色"这句话的数值形式，不经过任何二次阈值。
            int top4 = 0;
            for (int i = 0; i < 4 && i < 64; i++) top4 += sorted[i];
            s.cover4 = (float)top4 / total;
            return s;
        }

        /// <summary>由"该物体在屏幕上的占比"反推采样矩形（居中）。</summary>
        private static void BoxFromFrac(int w, int h, float fracW, float fracH, out int x0, out int x1, out int y0, out int y1)
        {
            int bw = Mathf.Clamp(Mathf.RoundToInt(fracW * w), 8, w);
            int bh = Mathf.Clamp(Mathf.RoundToInt(fracH * h), 8, h);
            x0 = (w - bw) / 2; x1 = x0 + bw;
            y0 = (h - bh) / 2; y1 = y0 + bh;
        }

        /// <summary>某个尺寸的物体在给定距离上占屏幕高度的比例（针孔相机）。</summary>
        private static float ScreenFrac(Camera cam, float worldSize, float distance, bool vertical, int w, int h)
        {
            float halfH = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * distance;
            float halfW = halfH * (float)w / h;
            return worldSize / (2f * (vertical ? halfH : halfW));
        }

        /// <summary>两张同机位图在中央框上的平均绝对亮度差。用来证明"这一层真的看得见"。</summary>
        private static float Diff(Shot a, Shot b)
        {
            if (a.box == null || b.box == null || a.box.Length != b.box.Length) return -1f;
            double acc = 0;
            for (int i = 0; i < a.box.Length; i++) acc += Mathf.Abs(Lum(a.box[i]) - Lum(b.box[i]));
            return (float)(acc / a.box.Length);
        }

        /// <summary>
        /// 把相机渲进一张临时 RT 再读回像素。
        ///
        /// 为什么不用 `ScreenCapture.CaptureScreenshotAsTexture`：
        ///   它抓的是 **Game View**，会把 IMGUI（参数面板）一起拍进去 ——
        ///   面板上全是文字与滑块，像素统计会被彻底带偏。
        ///   走 `targetTexture` 只过渲染管线，UI 天然不在图里，分辨率也由我们定死。
        /// </summary>
        private static IEnumerator CaptureToPixels(Camera cam, int w, int h,
                                                   System.Action<Texture2D, Color32[]> done)
        {
            var rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32);
            var prevTarget = cam.targetTexture;
            var prevActive = RenderTexture.active;

            cam.targetTexture = rt;
            yield return null; yield return null;   // 等一帧画完（这期间 TargetTexture 已生效）

            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();

            RenderTexture.active = prevActive;
            cam.targetTexture = prevTarget;
            RenderTexture.ReleaseTemporary(rt);

            var px = tex.GetPixels32();
            done(tex, px);
        }

        /// <summary>
        /// Sprint 4 验收：水墨量化光照 → 飞白墨线 → 宣纸底纹，四阶段逐层可见，且不影响性能。
        ///
        /// 度量口径上的一条关键做法：**截图期间把 `Time.timeScale` 置 0**。
        ///   四个阶段必须是"同一瞬间的同一幅画只换了画法" —— 只要有角色/敌人移动，
        ///   阶段间的像素差里就混进了运动，既证明不了风格，也随帧率漂。冻结时间之后，
        ///   差值是**纯风格差**，可复现。（注意：冻结后所有等待都必须按**帧**而不是按秒，
        ///   所以下面统一用 `yield return null`。）
        /// </summary>
        public static IEnumerator S4InkFlow()
        {
            var sb = new StringBuilder();
            _m4Pass = 0; _m4Fail = 0;

            var ctx = ResolveContext();
            if (ctx == null) { WriteReport("S4", "<错误> 场景里找不到 Player / PlayerController / Main Camera"); yield break; }

            var go = ctx.playerGo;
            var ctl = ctx.ctl;
            var cam = ctx.cam;
            var smr = go.GetComponentInChildren<SkinnedMeshRenderer>(true);
            var panel = UnityEngine.Object.FindObjectOfType<InkStylePanel>();
            var health = go.GetComponent<PlayerHealth>();

            sb.AppendLine("======================================================================");
            sb.AppendLine("Sprint 4 验收 —— 水墨风格与渲染管线扩展（M4：有墨味）");
            sb.AppendLine("生成时间 " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("======================================================================");

            bool oldHitStop = HitStop.Enabled;
            HitStop.Enabled = false;
            ctl.BeginInputOverride();
            ctl.SetInjectedMove(Vector2.zero, false);
            var stance = go.GetComponentInChildren<CombatStance>(true);
            if (stance != null) stance.combatExitDelay = 99999f;

            TeleportPlayer(go, new Vector3(0f, 0.45f, -3f));
            ctl.ResetToLocomotion();
            { float t = Time.time; while (Time.time - t < 0.9f) yield return null; }

            // ---------------- ① 着色器 ----------------
            sb.AppendLine();
            sb.AppendLine("---- ① 水墨着色器（编译通过才找得到）----");
            foreach (var sn in new[] { "InkWash/InkCharacter", "Hidden/InkWash/InkEdge", "Hidden/InkWash/InkPaper" })
            {
                var sh = Shader.Find(sn);
                M4(sb, sh != null, "着色器可解析 " + sn, sh != null ? "pass = " + sh.passCount : "Shader.Find 返回 null");
            }

            // ---------------- ② 主角材质 ----------------
            sb.AppendLine();
            sb.AppendLine("---- ② 主角水墨材质 ----");
            string matShader = (smr != null && smr.sharedMaterial != null) ? smr.sharedMaterial.shader.name : "<无>";
            M4(sb, matShader == "InkWash/InkCharacter", "主角当前材质用的是水墨着色器", "实际 = " + matShader);
            M4(sb, panel != null, "场景里存在参数面板 InkStylePanel",
                panel != null ? "调参材质 = " + (panel.inkMaterial != null ? panel.inkMaterial.name : "<无>") : "");
            M4(sb, panel != null && panel.inkMaterial != null, "面板已绑定可调的水墨材质", "");

            // ---------------- ③ RendererFeature 装配 ----------------
            sb.AppendLine();
            sb.AppendLine("---- ③ RendererFeature 装配 ----");
            M4(sb, InkStyleRegistry.BothRegistered, "两个全屏 Feature 已在运行时注册（= 渲染器资产里确实装了）",
                "Edge=" + (InkStyleRegistry.Edge != null) + "  Paper=" + (InkStyleRegistry.Paper != null));
            var es = InkStyleRegistry.EdgeSettings;
            var ps = InkStyleRegistry.PaperSettings;
            M4(sb, es != null && es.dryBrushTex != null, "墨线的飞白噪声贴图已接上",
                es != null && es.dryBrushTex != null ? es.dryBrushTex.name : "<无>");
            M4(sb, ps != null && ps.paperTex != null, "宣纸底纹贴图已接上",
                ps != null && ps.paperTex != null ? ps.paperTex.name : "<无>");

            // ---------------- ④ 敌人也换材质（运行时生成，靠组件自注册）----------------
            sb.AppendLine();
            sb.AppendLine("---- ④ 敌人材质与换材质登记 ----");
            var spawner = UnityEngine.Object.FindObjectOfType<WaveSpawner>();
            if (spawner == null) sb.AppendLine("    ** 场景里找不到 WaveSpawner，跳过本节");
            else
            {
                KillAllAlive();
                spawner.ResetForTest();
                EnemyBase.AliveCount = 0; EnemyBase.TotalKills = 0; EnemyBase.TotalSpawned = 0;
                { float t = Time.time; while (Time.time - t < 0.3f) yield return null; }
                spawner.Begin();
                { float t = Time.time; while (spawner.SpawnedCount < 3 && Time.time - t < 10f) yield return null; }

                var alive = AliveEnemies();
                int inkCount = 0;
                foreach (var e in alive)
                {
                    var r = e.GetComponentInChildren<SkinnedMeshRenderer>(true);
                    if (r != null && r.sharedMaterial != null && r.sharedMaterial.shader.name == "InkWash/InkCharacter") inkCount++;
                }
                M4(sb, alive.Count > 0 && inkCount == alive.Count, "刷出来的敌人全部使用水墨材质",
                    inkCount + "/" + alive.Count + " 只");
                M4(sb, InkStylePanel.RegisteredCount >= alive.Count + 1,
                    "换材质对象已登记（主角 + 敌人，运行时生成的敌人靠组件自注册）",
                    "登记 " + InkStylePanel.RegisteredCount + " 家（敌人 " + alive.Count + " 只）");

                // 阶段 1「白模」必须能把敌人也切回原 PBR 材质 —— 这一条同时验证
                // "记录原始材质"没被第二次施工覆盖成水墨材质（那样对照实验会静默失效）
                if (panel != null && alive.Count > 0)
                {
                    panel.ApplyStage(0);
                    yield return null; yield return null;
                    var r0 = alive[0].GetComponentInChildren<SkinnedMeshRenderer>(true);
                    string s0 = (r0 != null && r0.sharedMaterial != null) ? r0.sharedMaterial.shader.name : "<无>";
                    panel.ApplyStage(3);
                    yield return null; yield return null;
                    var r3 = alive[0].GetComponentInChildren<SkinnedMeshRenderer>(true);
                    string s3 = (r3 != null && r3.sharedMaterial != null) ? r3.sharedMaterial.shader.name : "<无>";
                    M4(sb, s0 != "InkWash/InkCharacter" && s3 == "InkWash/InkCharacter",
                        "切阶段 1 时敌人回到原 PBR 材质、切回阶段 4 又是水墨",
                        "阶段1 = " + s0 + " ｜ 阶段4 = " + s3);
                }
            }
            if (health != null) health.ResetHealth();

            // ---------------- ⑤ 四阶段截图（冻结时间 + 特写机位）----------------
            sb.AppendLine();
            sb.AppendLine("---- ⑤ 四阶段对照（截图期间 Time.timeScale = 0）----");

            string shotDir = Path.Combine(Path.GetFullPath(Path.Combine(Application.dataPath, "..")),
                                          "Tools/screenshots/s4");
            Directory.CreateDirectory(shotDir);

            // 特写机位：站在主角正前方 3.2 m、胸口高度看过去，角色大约占满画面高度的 6 成。
            // 拉这么近是为了让中央框里几乎只有角色本体 —— 量化是不是真的把连续光照压成了几阶，
            // 只有这样才量得出来（框里混进大面积地面会把差异摊平）。
            var rig = ctx.rig;
            bool savedRigOn = rig != null && rig.enabled;
            Vector3 savedCamPos = cam.transform.position;
            Quaternion savedCamRot = cam.transform.rotation;
            if (rig != null) rig.enabled = false;

            // 机位对准**身体中点**（不是胸口）：这样角色在画面里是上下居中的，
            // 采样框才能简单地按"居中矩形"给出来。
            const float BodyHeight = 2.20f, BodyWidth = 0.80f, ShotDist = 3.2f;
            Vector3 aim = go.transform.position + Vector3.up * 1.05f;
            Vector3 camPos = aim + go.transform.forward * ShotDist;
            cam.transform.position = camPos;
            cam.transform.rotation = Quaternion.LookRotation(aim - camPos, Vector3.up);
            { float t = Time.time; while (Time.time - t < 0.4f) yield return null; }

            const int W = 1280, H = 720;
            // 采样框按**解析投影**给：角色在 3.2 m 处占屏高 BodyHeight/(2·d·tan(fov/2))，
            // 宽同理。只在框里取像素，才能把"角色的变化"从"背景没变"里拎出来。
            int bx0, bx1, by0, by1;
            BoxFromFrac(W, H,
                ScreenFrac(cam, BodyWidth, ShotDist, false, W, H) * 1.15f,
                ScreenFrac(cam, BodyHeight, ShotDist, true, W, H) * 1.05f,
                out bx0, out bx1, out by0, out by1);
            sb.AppendLine("   机位 " + F(ShotDist) + " m ／ fov " + F(cam.fieldOfView)
                          + " ／ 采样框 " + (bx1 - bx0) + "×" + (by1 - by0) + " px（居中）");

            float oldScale = Time.timeScale;
            Time.timeScale = 0f;                     // 见方法注释：冻结时间，差值才是纯风格差
            yield return null; yield return null;

            var shots = new Shot[4];
            var matNames = new string[4];
            for (int s = 0; s < 4; s++)
            {
                if (panel != null) panel.ApplyStage(s);
                yield return null;
                yield return null;

                bool wantEdge = s >= 2, wantPaper = s >= 3;
                M4(sb, InkStyleRegistry.EdgeEnabled == wantEdge,
                    "阶段 " + (s + 1) + " 墨线开关", "期望 " + wantEdge + " 实际 " + InkStyleRegistry.EdgeEnabled);
                M4(sb, InkStyleRegistry.PaperEnabled == wantPaper,
                    "阶段 " + (s + 1) + " 宣纸开关", "期望 " + wantPaper + " 实际 " + InkStyleRegistry.PaperEnabled);

                var r = go.GetComponentInChildren<SkinnedMeshRenderer>(true);
                matNames[s] = (r != null && r.sharedMaterial != null) ? r.sharedMaterial.shader.name : "<无>";
                string want = s == 0 ? "Universal Render Pipeline/Lit" : "InkWash/InkCharacter";
                M4(sb, matNames[s] == want, "阶段 " + (s + 1) + " 主角材质", matNames[s]);

                int idx = s;
                yield return CaptureToPixels(cam, W, H, (tex, px) =>
                {
                    shots[idx] = Analyse(px, W, H, bx0, bx1, by0, by1);
                    // 写盘失败不能让协程死掉：此刻 Time.timeScale = 0，
                    // 协程一死就再也没人把它恢复回去，编辑器会直接僵住。
                    try
                    {
                        File.WriteAllBytes(Path.Combine(shotDir, "M4_stage" + (idx + 1) + ".png"), tex.EncodeToPNG());
                    }
                    catch (System.Exception ex)
                    {
                        Debug.LogWarning("[S4] 写截图失败: " + ex.Message);
                    }
                    UnityEngine.Object.Destroy(tex);
                });
                sb.AppendLine("   阶段 " + (s + 1) + "（" + InkStylePanel.StageName(s) + "）"
                              + "   平均亮度 " + F(shots[s].mean) + "   标准差 " + F(shots[s].std)
                              + "   出现级数 " + shots[s].distinct + "   80%级数 " + shots[s].n80);
            }
            Time.timeScale = oldScale;
            if (rig != null) rig.enabled = savedRigOn;
            cam.transform.position = savedCamPos;
            cam.transform.rotation = savedCamRot;
            sb.AppendLine("   截图写入 " + shotDir + "（M4_stage1..4.png，1280×720，无 UI）");

            float d10 = Diff(shots[1], shots[0]);
            float d21 = Diff(shots[2], shots[1]);
            float d32 = Diff(shots[3], shots[2]);
            float d30 = Diff(shots[3], shots[0]);
            sb.AppendLine("   逐层像素差（中央框平均绝对亮度差，0..1）：");
            sb.AppendLine("     ② 量化光照 − ① 白模 = " + F(d10));
            sb.AppendLine("     ③ 飞白墨线 − ②        = " + F(d21));
            sb.AppendLine("     ④ 宣纸底纹 − ③        = " + F(d32));
            sb.AppendLine("     ④ − ①（总变化）       = " + F(d30));
            M4(sb, d10 > 0.005f, "量化光照改变了画面（② vs ①）", "差值 " + F(d10));
            M4(sb, d21 > 0.005f, "飞白墨线可见（③ vs ②）", "差值 " + F(d21));
            M4(sb, d32 > 0.005f, "宣纸底纹可见（④ vs ③）", "差值 " + F(d32));
            M4(sb, d30 >= d21 && d30 >= d32, "四层叠加的总变化不小于任何单层", F(d30) + " ≥ " + F(Mathf.Max(d21, d32)));
            // ---- 「量化」不能拿上面这个框来判（一次度量翻车的记录）----
            // 这里刻意**不判定**，只把数字摊出来。两条原因，第一条就是这次的真凶：
            //   ① 本着色器在同一墨阶内**仍然乘了漫反射贴图**，所以"出现过多少亮度级"
            //      本来就不会塌缩 —— 那个数永远很大，拿它判量化只能得到假失败；
            //   ② 采样框里还有不随阶段变化的地面与墙，把差异进一步摊平。
            // 正确做法是在**白球**上量：无贴图、关飞白与轮廓，画面里就只剩量化本身。
            sb.AppendLine("   （参考，不作判定）角色区域：出现亮度级 "
                          + shots[0].distinct + " → " + shots[1].distinct + " → " + shots[2].distinct
                          + " → " + shots[3].distinct + "；覆盖 80% 像素所需级数 "
                          + shots[0].n80 + " / " + shots[1].n80 + " / " + shots[2].n80 + " / " + shots[3].n80);

            // ---------------- ⑤b 量化阶数实测（白球）----------------
            sb.AppendLine();
            sb.AppendLine("---- ⑤b 量化阶数实测（白球：无贴图、飞白=0、轮廓=0、高光=0）----");
            sb.AppendLine("   做法：球放在相机正前方 1.70 m；**关掉场景里所有灯、把环境光清成黑**，只留一盏");
            sb.AppendLine("   『顺镜头方向』的平行光 —— 球面正中的 N·L = 1.0，越靠边越暗，是一条受控的亮度斜坡。");
            sb.AppendLine("   为什么必须受控（两次翻车记录）：① 主光方向是场景定的，球正面完全可能背光，");
            sb.AppendLine("   那样无论几阶都只剩最暗一阶，「量出来全是 1 级」；");
            sb.AppendLine("   ② 第二版改用近距离点光（0.9 m / 强度 3.5），把球直接打爆成纯白 ——");
            sb.AppendLine("   PBR 对照组的平均亮度 0.957、单一级别占 85%，饱和的输入里根本不存在可分的阶。");
            sb.AppendLine("   那是**尺子坏了**，不是量化没生效。");
            sb.AppendLine("   判据：把 0..1 亮度切成 64 个桶，看『前 4 种墨色覆盖了百分之多少像素』—— 4 阶量化下应当接近");
            sb.AppendLine("   100%，因为整幅画本来就只剩 4 种墨色；再配『最大一桶占多少』做交叉验证。");
            sb.AppendLine("   另外把『阶间柔度』压到 0.01（默认 0.06）：软边是美术过渡，与量化本身无关，");
            sb.AppendLine("   留着它会有约 32% 的像素落在阶与阶之间，度量会把「阶」读成连续渐变。");

            var probe = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            probe.name = "InkProbeSphere";
            var probeCol = probe.GetComponent<Collider>();
            if (probeCol != null) UnityEngine.Object.Destroy(probeCol);
            const float ProbeD = 1.0f, ProbeDist = 1.70f;
            var probeMr = probe.GetComponent<MeshRenderer>();
            probe.transform.localScale = Vector3.one * ProbeD;
            probe.transform.position = cam.transform.position + cam.transform.forward * ProbeDist;

            // ---- 受控光照：存档 → 清空 → 只留一盏 headlight（跑完必须还原）----
            var savedLights = new System.Collections.Generic.List<Light>();
            var savedLightOn = new System.Collections.Generic.List<bool>();
            foreach (var L in UnityEngine.Object.FindObjectsOfType<Light>())
            {
                savedLights.Add(L); savedLightOn.Add(L.enabled); L.enabled = false;
            }
            var savedAmbMode = RenderSettings.ambientMode;
            var savedAmbColor = RenderSettings.ambientLight;
            var savedAmbIntensity = RenderSettings.ambientIntensity;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = Color.black;
            RenderSettings.ambientIntensity = 0f;
            DynamicGI.UpdateEnvironment();

            var probeLightGo = new GameObject("InkProbeLight");
            var probeLight = probeLightGo.AddComponent<Light>();
            probeLight.type = LightType.Directional;
            probeLight.intensity = 1.0f;
            probeLight.color = Color.white;
            probeLight.shadows = LightShadows.None;
            // 顺镜头照过去：球面正中 N·L = 1，边缘 0 → 一条干净的连续亮度斜坡，量化后必然成阶
            probeLightGo.transform.position = cam.transform.position;
            probeLightGo.transform.rotation = Quaternion.LookRotation(cam.transform.forward, cam.transform.up);

            int sx0, sx1, sy0, sy1;
            BoxFromFrac(W, H,
                ScreenFrac(cam, ProbeD, ProbeDist, false, W, H) * 0.90f,
                ScreenFrac(cam, ProbeD, ProbeDist, true, W, H) * 0.90f,
                out sx0, out sx1, out sy0, out sy1);

            var litShader = Shader.Find("Universal Render Pipeline/Lit");
            var inkShader = Shader.Find("InkWash/InkCharacter");
            float[] bandSet = { 2f, 4f, 8f };
            var probeShots = new Shot[bandSet.Length + 1];
            var probeNames = new string[bandSet.Length + 1];
            sb.AppendLine("   采样框 " + (sx1 - sx0) + "×" + (sy1 - sy0) + " px（居中）");

            // 用局部函数而不是 lambda：for 循环变量在 lambda 里是**同一个变量**，
            // 稍不留神就把三次结果写进同一个下标（写对了也读不出来哪里对）。
            var shotOne = probeShots; var nameOne = probeNames;
            for (int i = 0; i < bandSet.Length + 1; i++)
            {
                Material m;
                if (i == 0)
                {
                    if (litShader == null) { sb.AppendLine("   ** 找不到 URP/Lit，跳过 PBR 对照"); continue; }
                    m = new Material(litShader);
                    m.SetColor("_BaseColor", Color.white);
                    if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0f);
                    if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
                    nameOne[0] = "URP/Lit（PBR 对照）";
                    if (panel != null) panel.ApplyStage(0);       // 两个全屏 Feature 都关，画面里只有球
                }
                else
                {
                    if (inkShader == null) { sb.AppendLine("   ** 找不到水墨着色器，跳过阶数实测"); continue; }
                    m = new Material(inkShader);
                    m.SetFloat("_Bands", bandSet[i - 1]);
                    m.SetFloat("_BrushStrength", 0f);   // ★ 关飞白：否则抖动把阶边界抹开，量不到阶数
                    m.SetFloat("_RimStrength", 0f);
                    m.SetFloat("_SpecStrength", 0f);
                    // ★ 阶间柔度压到最小：它是**美术上的软边过渡**，与"有没有量化"是两件事。
                    //   留着默认 0.06 时，三个阶边界各摊开约 0.12 的亮度宽度 —— 按球面面积算
                    //   正好吃掉约 32% 的像素，于是"前 4 种墨色"只剩 75%，看着像量化没生效。
                    //   探针要测的是量化**本身**，所以把软边摘出去单独看。
                    m.SetFloat("_BandSoftness", 0.01f);
                    nameOne[i] = "水墨 " + bandSet[i - 1].ToString("0") + " 阶";
                    if (panel != null) panel.ApplyStage(1);       // 有量化、没有墨线/宣纸
                }
                probeMr.sharedMaterial = m;
                yield return null; yield return null;

                int idx = i;
                yield return CaptureToPixels(cam, W, H, (tex, px) =>
                {
                    shotOne[idx] = Analyse(px, W, H, sx0, sx1, sy0, sy1);
                    try
                    {
                        string fn = idx == 0 ? "M4_quant_pbr.png"
                                             : "M4_quant_bands" + bandSet[idx - 1].ToString("0") + ".png";
                        File.WriteAllBytes(Path.Combine(shotDir, fn), tex.EncodeToPNG());
                    }
                    catch (System.Exception ex) { Debug.LogWarning("[S4] 写图失败: " + ex.Message); }
                    UnityEngine.Object.Destroy(tex);
                });
                UnityEngine.Object.Destroy(m);
            }

            UnityEngine.Object.Destroy(probe);
            UnityEngine.Object.Destroy(probeLightGo);
            // ---- 还原受控光照前存档的场景光照（不还原会把后面的展示/性能段全拍黑）----
            for (int i = 0; i < savedLights.Count; i++)
                if (savedLights[i] != null) savedLights[i].enabled = savedLightOn[i];
            RenderSettings.ambientMode = savedAmbMode;
            RenderSettings.ambientLight = savedAmbColor;
            RenderSettings.ambientIntensity = savedAmbIntensity;
            DynamicGI.UpdateEnvironment();

            for (int i = 0; i < probeShots.Length; i++)
                sb.AppendLine("   " + probeNames[i] + "：平均亮度 " + F(probeShots[i].mean)
                              + "，出现过 " + probeShots[i].distinct + " 级"
                              + "，前 4 种墨色覆盖 " + F(probeShots[i].cover4 * 100f) + "%"
                              + "，最大一级占 " + F(probeShots[i].topShare * 100f) + "%"
                              + "（参考 n80 = " + probeShots[i].n80 + "）");

            bool probeOk = probeShots[0].n80 > 0 && probeShots[2].n80 > 0;
            if (probeOk)
            {
                // ★ 主判据是「前 4 种墨色覆盖了多少像素」，不是 n80。
                //   上一版用 n80（覆盖 80% 像素需要几个 64 桶），它恰好卡在 5/6 的边界上 ——
                //   同一份代码重跑一次就从 5 变 6、判定翻面。那是**度量自身在抖**，不是画面变了。
                //   "4 阶量化"要说的本来就是"整幅画只剩 4 种墨色"，直接量它。PBR 作为对照。
                M4(sb, probeShots[2].cover4 >= 0.90f,
                    "4 阶量化后前 4 种墨色覆盖 ≥ 90% 像素（= 画面只剩 4 种墨色）",
                    "PBR " + F(probeShots[0].cover4 * 100f) + "% → 4 阶 " + F(probeShots[2].cover4 * 100f) + "%");
                M4(sb, probeShots[2].topShare > probeShots[0].topShare,
                    "4 阶量化出现大片同亮度区域（最大一级占比高于 PBR）",
                    "PBR " + F(probeShots[0].topShare * 100f) + "% → 4 阶 " + F(probeShots[2].topShare * 100f) + "%");
                M4(sb, probeShots[1].cover4 >= probeShots[2].cover4 && probeShots[2].cover4 >= probeShots[3].cover4,
                    "墨阶数越少，前 4 种墨色的覆盖越集中（2 阶 ≥ 4 阶 ≥ 8 阶）",
                    F(probeShots[1].cover4 * 100f) + "% ≥ " + F(probeShots[2].cover4 * 100f) + "% ≥ "
                    + F(probeShots[3].cover4 * 100f) + "%");
            }
            else
            {
                M4(sb, false, "量化阶数实测取不到有效样本",
                    "PBR " + probeShots[0].n80 + " / 4 阶 " + probeShots[2].n80 + "（检查着色器与灯光）");
            }

            // ---------------- ⑥ 展示：四阶段轮播（供录屏）----------------
            sb.AppendLine();
            sb.AppendLine("---- ⑥ 展示轮播（4 阶段 × 4 s，横向慢走）----");
            ctl.SetInjectedMove(Vector2.zero, false);
            { float t = Time.time; while (Time.time - t < 0.4f) yield return null; }
            for (int s = 0; s < 4; s++)
            {
                if (panel != null) panel.ApplyStage(s);
                float t0 = Time.time;
                float dir = 1f;
                float flip = 0f;
                ctl.SetInjectedMove(new Vector2(dir, 0f), false);
                while (Time.time - t0 < 4.0f)
                {
                    if (Time.time - flip > 1.0f) { flip = Time.time; dir = -dir; }
                    ctl.SetInjectedMove(new Vector2(dir, 0f), false);
                    yield return null;
                }
            }
            ctl.SetInjectedMove(Vector2.zero, false);

            // ---------------- ⑦ 性能 ----------------
            sb.AppendLine();
            sb.AppendLine("---- ⑦ 性能（阶段 4 最重，面板已隐藏）----");
            if (panel != null) panel.ApplyStage(3);
            bool savedVisible = panel != null && panel.visible;
            if (panel != null) panel.visible = false;
            { float t = Time.time; while (Time.time - t < 0.6f) yield return null; }   // 预热
            int frames = 0;
            float elapsed = 0f;
            { float t0 = Time.unscaledTime; float t = Time.time;
              while (Time.time - t < 3.0f) { frames++; elapsed = Time.unscaledTime - t0; yield return null; } }
            float fps = elapsed > 0f ? frames / elapsed : 0f;
            if (panel != null) panel.visible = savedVisible;
            sb.AppendLine("   帧数 " + frames + " / 墙钟 " + F(elapsed) + " s  → " + F(fps) + " fps @ "
                          + Screen.width + "×" + Screen.height);
            M4(sb, fps >= 55f, "1080p 下帧率 ≥ 55", F(fps) + " fps（若本行在录屏运行中取得，会明显偏低，需无录屏复测）");

            // ---------------- 收尾 ----------------
            KillAllAlive();
            ctl.SetInjectedMove(Vector2.zero, false);
            ctl.EndInputOverride();
            if (stance != null) stance.combatExitDelay = 6f;
            HitStop.Enabled = oldHitStop;

            sb.AppendLine();
            sb.AppendLine("---- 汇总 ----");
            sb.AppendLine("通过 " + _m4Pass + " / 未通过 " + _m4Fail);
            sb.AppendLine(_m4Fail == 0
                ? ">>> M4 达成（多阶量化光照 + 飞白墨线 + 宣纸底纹三层可分别开关，四阶段逐层可见）"
                : ">>> M4 未达成");
            WriteReport("S4", sb.ToString());
            Debug.Log("[PlaytestHarness] S4 结束：通过 " + _m4Pass + " / 未通过 " + _m4Fail);
        }

        // ==================================================================
        //  Sprint 5 · 完整 Roguelike 循环 + 水墨特效（M5）
        // ==================================================================

        private static int _m5Pass, _m5Fail;

        private static void M5(StringBuilder sb, bool ok, string label, string detail)
        {
            if (ok) _m5Pass++; else _m5Fail++;
            sb.AppendLine("[" + (ok ? "通过" : "未通过") + "] " + label
                          + (string.IsNullOrEmpty(detail) ? "" : "    " + detail));
        }

        /// <summary>
        /// 按**未缩放**时间等待。
        /// 奖励界面（Reward）会把 Time.timeScale 置 0，那时用 Time.time 计时**永远等不到** ——
        /// 表现为验收脚本原地挂死，而且不报错（它只是"一直在等"）。
        /// 凡是可能跨越 Reward 状态的等待都必须走这个。
        /// </summary>
        private static IEnumerator WaitUnscaled(float seconds)
        {
            float t = Time.unscaledTime;
            while (Time.unscaledTime - t < seconds) yield return null;
        }

        private static string DescribeList(IReadOnlyList<SkillData> list)
        {
            if (list == null || list.Count == 0) return "（空）";
            var sb = new StringBuilder();
            for (int i = 0; i < list.Count; i++)
            {
                if (i > 0) sb.Append("、");
                if (list[i] != null) sb.Append(list[i].displayName);
            }
            return sb.ToString();
        }

        /// <summary>
        /// 反射逐字段比对两个同类型对象。用途：把「Clone 漏拷字段」变成**会失败的断言**。
        /// 这类漏拷的症状是"面板一调参数，某个看不出关联的字段就悄悄回默认值"，
        /// 靠 review 抓不到 —— 本项目已经因为漏拷 depthBias/normalBias 复发过一次假纸纹。
        /// </summary>
        private static int ComparePublicFields(object a, object b, StringBuilder diffs)
        {
            if (a == null || b == null) return -1;
            var ta = a.GetType();
            if (ta != b.GetType()) return -1;
            int bad = 0;
            foreach (var f in ta.GetFields(System.Reflection.BindingFlags.Public
                                           | System.Reflection.BindingFlags.Instance))
            {
                object va = f.GetValue(a);
                object vb = f.GetValue(b);
                bool same = (va == null || vb == null) ? ReferenceEquals(va, vb) : va.Equals(vb);
                if (!same)
                {
                    bad++;
                    if (diffs != null) diffs.AppendLine("      · " + f.Name + "：" + va + " → " + vb);
                }
            }
            return bad;
        }

        public static IEnumerator S5RoguelikeFlow()
        {
            var sb = new StringBuilder();
            _m5Pass = 0; _m5Fail = 0;

            var ctx = ResolveContext();
            if (ctx == null) { WriteReport("S5", "<错误> 场景里找不到 Player / PlayerController / Main Camera"); yield break; }

            var go = ctx.playerGo;
            var ctl = ctx.ctl;
            var cam = ctx.cam;
            var stance = go.GetComponentInChildren<CombatStance>(true);
            var stats = go.GetComponent<PlayerStats>();
            var inv = go.GetComponent<SkillInventory>();
            var level = go.GetComponent<LevelSystem>();
            var health = go.GetComponent<PlayerHealth>();
            var hitbox = go.GetComponentInChildren<PlayerSwordHitbox>(true);
            var landing = go.GetComponent<InkLandingBloom>();
            var run = UnityEngine.Object.FindObjectOfType<RunManager>();
            var choice = UnityEngine.Object.FindObjectOfType<SkillChoicePanel>();
            var inkv = UnityEngine.Object.FindObjectOfType<InkHitVfx>();
            var panel = UnityEngine.Object.FindObjectOfType<InkStylePanel>();
            var spawner = UnityEngine.Object.FindObjectOfType<WaveSpawner>();
            var room = UnityEngine.Object.FindObjectOfType<RoomController>();

            sb.AppendLine("======================================================================");
            sb.AppendLine("Sprint 5 验收 —— 完整 Roguelike 循环 + 水墨特效（M5：能完整跑完一局）");
            sb.AppendLine("生成时间 " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("======================================================================");

            var missing = new List<string>();
            if (stats == null) missing.Add("PlayerStats");
            if (inv == null) missing.Add("SkillInventory");
            if (level == null) missing.Add("LevelSystem");
            if (hitbox == null) missing.Add("PlayerSwordHitbox");
            if (run == null) missing.Add("RunManager");
            if (choice == null) missing.Add("SkillChoicePanel");
            if (inkv == null) missing.Add("InkHitVfx");
            if (landing == null) missing.Add("InkLandingBloom");
            if (spawner == null) missing.Add("WaveSpawner");
            if (missing.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("** 缺少组件：" + string.Join("、", missing.ToArray()));
                WriteReport("S5", sb.ToString());
                Debug.LogError("[PlaytestHarness] S5 缺件：" + string.Join("、", missing.ToArray()));
                yield break;
            }

            bool oldHitStop = HitStop.Enabled;
            HitStop.Enabled = false;                 // 顿帧会让所有时长失真

            if (stance != null) stance.combatExitDelay = 99999f;
            ctl.BeginInputOverride();
            ctl.SetInjectedMove(Vector2.zero, false);
            bool savedPanelVisible = panel != null && panel.visible;
            if (panel != null) panel.visible = false;

            // 验收期间把波次节奏调快（**只改运行时内存**，Play 退出后场景不会被保存）
            float savedInterval = spawner.spawnInterval;
            float savedNextRoom = run.nextRoomDelay;
            float[] savedDelays = null;
            spawner.spawnInterval = 0.06f;
            run.nextRoomDelay = 0.35f;
            if (spawner.waves != null)
            {
                savedDelays = new float[spawner.waves.Length];
                for (int i = 0; i < spawner.waves.Length; i++)
                {
                    if (spawner.waves[i] == null) continue;
                    savedDelays[i] = spawner.waves[i].delayBefore;
                    spawner.waves[i].delayBefore = 0.2f;
                }
            }

            // 复位到干净状态
            KillAllAlive();
            yield return WaitUnscaled(0.3f);
            inv.ResetAll(); level.ResetAll();
            health.ResetHealth(); health.ResetDiagnostics();
            run.ResetForTest();
            spawner.ResetForTest();
            if (room != null) room.ResetForTest();
            TeleportPlayer(go, new Vector3(0f, 0.45f, -3f));
            ctl.ResetToLocomotion();
            yield return WaitUnscaled(0.6f);

            // ==============================================================
            // ① 技能数据层与加权随机（D2）
            // ==============================================================
            sb.AppendLine();
            sb.AppendLine("---- ① 技能数据层与加权随机（D2） ----");
            var pool = run.skillPool;
            if (pool == null || pool.Count == 0)
            {
                M5(sb, false, "RunManager.skillPool 非空", "为空 —— 后面几节会跳过");
            }
            else
            {
                int common = 0, rare = 0, epic = 0;
                var ids = new HashSet<string>();
                int dup = 0;
                int zeroWeight = 0;
                foreach (var s in pool)
                {
                    if (s == null) continue;
                    if (!ids.Add(s.id)) dup++;
                    if (s.weight <= 0f) zeroWeight++;
                    if (s.rarity == SkillRarity.Epic) epic++;
                    else if (s.rarity == SkillRarity.Rare) rare++;
                    else common++;
                }
                M5(sb, pool.Count >= 6, "技能资产数量 ≥ 6", "实际 " + pool.Count + " 条");
                M5(sb, common > 0 && (rare + epic) > 0, "稀有度有层次",
                    "普通 " + common + " / 稀有 " + rare + " / 史诗 " + epic);
                M5(sb, dup == 0, "技能 id 唯一（层数与存档都以 id 为键）",
                    dup == 0 ? "无重复" : dup + " 条重复");
                M5(sb, zeroWeight == 0, "没有权重为 0 的「永远抽不到」技能", "异常 " + zeroWeight + " 条");

                var r1 = SkillPool.Draw(pool, null, 3, new System.Random(777));
                var r2 = SkillPool.Draw(pool, null, 3, new System.Random(777));
                bool same = r1.picked.Count == r2.picked.Count;
                if (same)
                    for (int i = 0; i < r1.picked.Count; i++)
                        if (r1.picked[i] != r2.picked[i]) same = false;
                M5(sb, same && r1.picked.Count > 0, "固定种子下抽取可复现（同种子两次结果相同）",
                    "抽到 " + DescribeList(r1.picked));

                var r3 = SkillPool.Draw(pool, null, 3, new System.Random(20260101));
                bool differs = r3.picked.Count != r1.picked.Count;
                if (!differs)
                    for (int i = 0; i < r3.picked.Count; i++)
                        if (r3.picked[i] != r1.picked[i]) differs = true;
                M5(sb, differs, "换种子得到不同组合（抽取确实依赖随机）",
                    "种子 20260101 → " + DescribeList(r3.picked));

                bool noDupInDraw = true;
                for (int round = 0; round < 200; round++)
                {
                    var rr = SkillPool.Draw(pool, null, 3, new System.Random(3000 + round));
                    for (int i = 0; i < rr.picked.Count; i++)
                        for (int j = i + 1; j < rr.picked.Count; j++)
                            if (rr.picked[i] == rr.picked[j]) noDupInDraw = false;
                }
                M5(sb, noDupInDraw, "同一轮三选一里不会出现两条一样的技能", "检查 200 轮");

                var commons = new List<SkillData>();
                foreach (var s in pool) if (s != null && s.rarity == SkillRarity.Common) commons.Add(s);
                if (commons.Count >= 2)
                {
                    var hi = commons[0];
                    var lo = commons[0];
                    foreach (var s in commons)
                    {
                        if (s.weight > hi.weight) hi = s;
                        if (s.weight < lo.weight) lo = s;
                    }
                    var tally = SkillPool.SampleDistribution(commons, 1200, 1, 4242);
                    int chi, clo;
                    tally.TryGetValue(hi.id, out chi);
                    tally.TryGetValue(lo.id, out clo);
                    M5(sb, chi > clo, "权重真的起作用（同稀有度内高权重被抽中的次数更多）",
                        hi.displayName + "(w=" + N(hi.weight) + ") " + chi + " 次  vs  "
                        + lo.displayName + "(w=" + N(lo.weight) + ") " + clo + " 次（各 1200 轮）");
                }
                else
                {
                    M5(sb, false, "同一稀有度至少 2 条技能才能验证权重", "只有 " + commons.Count + " 条普通");
                }

                var probe = pool[0];
                for (int i = 0; i < probe.maxStacks; i++) inv.Acquire(probe);
                bool filtered = true;
                for (int i = 0; i < 8; i++)
                {
                    var rr = SkillPool.Draw(pool, inv, Mathf.Min(3, pool.Count), new System.Random(1000 + i));
                    for (int k = 0; k < rr.picked.Count; k++) if (rr.picked[k] == probe) filtered = false;
                }
                M5(sb, filtered, "叠满 " + probe.maxStacks + " 层的技能不再进候选池",
                    probe.displayName + " 叠满后 8 次抽取均未出现");
                inv.ResetAll();
                M5(sb, Mathf.Abs(stats.DamageMultiplier - 1f) < 1e-4f,
                    "SkillInventory.ResetAll 会一并清掉 PlayerStats", "伤害×" + F(stats.DamageMultiplier));
            }

            // ==============================================================
            // ② 属性叠加（D4）—— 受控实验：直接投递修饰器，逐项量
            // ==============================================================
            sb.AppendLine();
            sb.AppendLine("---- ② 属性叠加与实测（D4） ----");
            stats.ResetAll();
            M5(sb, Mathf.Abs(stats.DamageMultiplier - 1f) < 1e-4f
                && Mathf.Abs(stats.MoveSpeedMultiplier - 1f) < 1e-4f,
                "未装技能时全部倍率 = 1（行为与 Sprint 4 逐位一致）", stats.Describe());

            stats.Add(StatKind.DamageBonus, 0.25f, "验收A");
            stats.Add(StatKind.DamageBonus, 0.25f, "验收B");
            M5(sb, Mathf.Abs(stats.DamageMultiplier - 1.5f) < 1e-4f,
                "伤害加成是**加法累加**（+25% 两层 = ×1.50，而非连乘的 ×1.5625）",
                "实测 ×" + F(stats.DamageMultiplier));

            stats.Add(StatKind.MoveSpeedBonus, 0.5f, "验收");
            M5(sb, Mathf.Abs(stats.MoveSpeedMultiplier - 1.5f) < 1e-4f, "移速加成生效",
                "×" + F(stats.MoveSpeedMultiplier));

            float hpBefore = health.EffectiveMaxHealth;
            stats.Add(StatKind.MaxHealthBonus, 40f, "验收");
            M5(sb, Mathf.Abs(health.EffectiveMaxHealth - (hpBefore + 40f)) < 1e-3f,
                "最大生命加成生效（有效上限 = 基值 + 加成）",
                F(hpBefore) + " → " + F(health.EffectiveMaxHealth));

            stats.Add(StatKind.AttackSpeedBonus, 1.0f, "验收");
            M5(sb, Mathf.Abs(stats.AttackSpeedMultiplier - 2f) < 1e-4f, "攻速加成生效（×2）",
                "×" + F(stats.AttackSpeedMultiplier));

            stats.Add(StatKind.CritChance, 1f, "验收");
            stats.Add(StatKind.CritDamage, 0.5f, "验收");
            M5(sb, Mathf.Abs(stats.CritChance - 1f) < 1e-4f && Mathf.Abs(stats.CritMultiplier - 2f) < 1e-4f,
                "暴击率/暴击伤害加成生效", "暴击率 100%　暴击倍率 ×" + F(stats.CritMultiplier));

            stats.Add(StatKind.DashCooldownCut, 0.5f, "验收");
            M5(sb, Mathf.Abs(stats.DashCooldownScale - 0.5f) < 1e-4f, "冲刺冷却缩短生效",
                "冷却×" + F(stats.DashCooldownScale));

            for (int i = 0; i < 40; i++) stats.Add(StatKind.DamageBonus, 0.5f, "压力");
            M5(sb, stats.DamageMultiplier <= 4.0001f, "伤害倍率被上限钳住（防叠加爆炸）",
                "叠 42 层后 ×" + F(stats.DamageMultiplier) + "（上限 ×" + N(1f + stats.maxDamageBonus) + "）");

            inv.ResetAll();
            stats.ResetAll();
            stats.Add(StatKind.DamageBonus, 0.5f, "实测");
            stats.SetSeed(20260915);
            stats.ForceNextCrit(false);

            spawner.ResetForTest();
            spawner.Begin();
            { float t = Time.time; while (spawner.SpawnedCount < 1 && Time.time - t < 8f) yield return null; }
            yield return WaitUnscaled(0.8f);

            var aliveNow = AliveEnemies();
            if (aliveNow.Count == 0)
            {
                M5(sb, false, "实测伤害：需要至少一只敌人", "刷不出敌人，本节跳过");
            }
            else
            {
                var e0 = aliveNow[0];
                Vector3 to = e0.transform.position - go.transform.position;
                to.y = 0f;
                if (to.sqrMagnitude < 1e-4f) to = Vector3.forward;
                to.Normalize();
                TeleportPlayer(go, e0.transform.position - to * 1.15f);
                ctl.ResetToLocomotion();
                yield return WaitUnscaled(0.5f);

                float before = hitbox.LastDamage;
                stats.ForceNextCrit(false);
                ctl.RequestInjectedAttack();
                yield return WaitUnscaled(1.0f);
                float after = hitbox.LastDamage;
                float expect = hitbox.damage[0] * stats.DamageMultiplier;
                M5(sb, after > before + 0.01f, "挥砍后判定体的伤害被刷新（属性真的写进了判定）",
                    F(before) + " → " + F(after));
                M5(sb, Mathf.Abs(after - expect) / Mathf.Max(0.01f, expect) < 0.03f,
                    "实测伤害 = 基础伤害 × 伤害倍率",
                    "基础 " + F(hitbox.damage[0]) + " ×" + F(stats.DamageMultiplier) + " = " + F(expect)
                    + "　实测 " + F(after));
            }

            stats.ForceNextCrit(true);
            float baseDmg = hitbox.damage[0] * stats.DamageMultiplier;
            bool critFlag = false;
            float critDmg = stats.RollDamage(hitbox.damage[0], out critFlag);
            float expectCrit = baseDmg * stats.CritMultiplier;
            M5(sb, critFlag && Mathf.Abs(critDmg - expectCrit) / Mathf.Max(0.01f, expectCrit) < 0.03f,
                "暴击伤害 = 名义伤害 × 暴击倍率",
                F(baseDmg) + " ×" + F(stats.CritMultiplier) + " = " + F(expectCrit) + "　实测 " + F(critDmg));

            stats.ResetAll();
            M5(sb, Mathf.Abs(stats.DamageMultiplier - 1f) < 1e-4f
                && Mathf.Abs(stats.MoveSpeedMultiplier - 1f) < 1e-4f
                && Mathf.Abs(stats.MaxHealthBonus) < 1e-4f,
                "PlayerStats.ResetAll 把全部属性清零", stats.Describe());
            health.ResetHealth();

            // ==============================================================
            // ③ 状态机与三选一（D1 + D3）
            // ==============================================================
            sb.AppendLine();
            sb.AppendLine("---- ③ 游戏状态机与三选一（D1 / D3） ----");
            KillAllAlive();
            yield return WaitUnscaled(0.3f);
            run.ResetForTest();
            M5(sb, run.State == RunState.MainMenu, "复位后状态是主菜单", run.Describe());

            bool rejected = !run.SetState(RunState.Reward) && run.State == RunState.MainMenu;
            M5(sb, rejected, "非法迁移被拒绝且状态不变（MainMenu → Reward）",
                "状态仍为 " + run.State + "；非法计数 " + run.IllegalTransitionCount);

            run.SetSeed(20260915);
            run.StartRun();
            M5(sb, run.State == RunState.Playing, "「开始一局」进入 Playing", "实际 " + run.State);

            level.GrantXp(level.XpToNext);
            yield return WaitUnscaled(0.3f);
            M5(sb, run.State == RunState.Reward, "升级后自动进入奖励状态", "实际 " + run.State);
            M5(sb, Mathf.Abs(Time.timeScale) < 1e-4f, "奖励界面上时间被冻结（timeScale = 0）",
                "timeScale = " + F(Time.timeScale));
            M5(sb, !HitStop.IsActive, "进奖励前先把顿帧结清（否则顿帧恢复会把暂停踩掉）",
                "HitStop.IsActive = " + HitStop.IsActive);

            int optCount = choice.Options != null ? choice.Options.Count : 0;
            M5(sb, choice.IsShowing && optCount >= 2, "三选一面板已弹出且带有候选",
                "候选 " + optCount + " 条：" + DescribeList(choice.Options));

            // ★ 非法迁移必须在**还停在 Reward 时**试。
            //   一次度量翻车的记录：这条原本写在 InjectChoice 之后，而选完技能状态已经回到 Playing，
            //   `Playing → Victory` 本来就是合法边 —— 于是测试自己把状态推成了 Victory，
            //   不仅本条判失败，连后面「玩家死亡 → GameOver」（OnPlayerDied 里 IsRunOver 直接 return）
            //   也一起失败。**先怀疑度量本身**：两条都正常，唯独这两条红。
            int illegalBefore = run.IllegalTransitionCount;
            bool rej2 = !run.SetState(RunState.Victory);
            M5(sb, run.State == RunState.Reward && rej2 && run.IllegalTransitionCount == illegalBefore + 1,
                "非法迁移被计数（Reward → Victory 不允许）",
                "状态仍为 " + run.State + "；非法计数 " + illegalBefore + " → " + run.IllegalTransitionCount);

            int stacksBefore = inv.AcquireCount;
            bool injected = choice.InjectChoice(0);
            yield return WaitUnscaled(0.25f);
            M5(sb, injected, "可以用程序化接口做出选择（InjectChoice —— 自动验收的唯一入口）",
                "选中第 1 张：" + choice.ChosenName);
            M5(sb, inv.AcquireCount == stacksBefore + 1, "选择真的写进了技能背包", "背包：" + inv.Describe());
            M5(sb, run.State == RunState.Playing, "选完自动回到 Playing", "实际 " + run.State);
            M5(sb, Mathf.Abs(Time.timeScale - 1f) < 1e-4f, "回到 Playing 后时间恢复",
                "timeScale = " + F(Time.timeScale));

            var lethal = new DamageInfo
            {
                amount = 100000f, sourceFaction = Faction.Enemy, hitDirection = Vector3.zero,
                knockback = 0f, hitStun = 0f, hitStop = 0f,
            };
            health.ClearInvincibility();
            health.TakeDamage(lethal);
            yield return WaitUnscaled(0.2f);
            M5(sb, run.State == RunState.GameOver, "玩家死亡后进入结算（GameOver）", "实际 " + run.State);
            M5(sb, Mathf.Abs(Time.timeScale - 1f) < 1e-4f, "结算不冻结时间（要能点「再来一局」）",
                "timeScale = " + F(Time.timeScale));
            health.ResetHealth();

            // ==============================================================
            // ④ E4 墨晕扩散
            // ==============================================================
            sb.AppendLine();
            sb.AppendLine("---- ④ E4 墨晕扩散后处理 ----");
            M5(sb, InkStyleRegistry.Bloom != null, "渲染器资产里装配了 InkBloomFeature",
                InkStyleRegistry.Bloom != null ? "已登记" : "**未登记**");
            M5(sb, InkStyleRegistry.AllRegistered, "三层全屏效果（墨线/宣纸/墨晕）全部登记",
                InkStyleRegistry.Describe());

            var diffsE = new StringBuilder();
            int badE = ComparePublicFields(
                InkStyleRegistry.Edge != null ? InkStyleRegistry.Edge.settings : null,
                InkStyleRegistry.Edge != null ? InkStyleRegistry.Edge.settings.Clone() : null, diffsE);
            M5(sb, badE == 0, "InkEdgeFeature.Settings.Clone() 逐字段一致（反射比对）",
                badE < 0 ? "缺对象" : (badE == 0 ? "无差异" : badE + " 个字段漏拷:\n" + diffsE));

            var diffsP = new StringBuilder();
            int badP = ComparePublicFields(
                InkStyleRegistry.Paper != null ? InkStyleRegistry.Paper.settings : null,
                InkStyleRegistry.Paper != null ? InkStyleRegistry.Paper.settings.Clone() : null, diffsP);
            M5(sb, badP == 0, "InkPaperFeature.Settings.Clone() 逐字段一致（反射比对）",
                badP < 0 ? "缺对象" : (badP == 0 ? "无差异" : badP + " 个字段漏拷:\n" + diffsP));

            var diffsB = new StringBuilder();
            int badB = ComparePublicFields(
                InkStyleRegistry.Bloom != null ? InkStyleRegistry.Bloom.settings : null,
                InkStyleRegistry.Bloom != null ? InkStyleRegistry.Bloom.settings.Clone() : null, diffsB);
            M5(sb, badB == 0, "InkBloomFeature.Settings.Clone() 逐字段一致（反射比对）",
                badB < 0 ? "缺对象" : (badB == 0 ? "无差异" : badB + " 个字段漏拷:\n" + diffsB));

            if (panel != null && InkStyleRegistry.Bloom != null)
            {
                int savedStage = panel.Stage;
                TeleportPlayer(go, new Vector3(0f, 0.45f, -3f));
                ctl.ResetToLocomotion();
                spawner.ResetForTest(); spawner.Begin();
                { float t = Time.time; while (spawner.SpawnedCount < 1 && Time.time - t < 8f) yield return null; }
                yield return WaitUnscaled(1.2f);

                const int W = 1280, H = 720;
                int bx0, bx1, by0, by1;
                BoxFromFrac(W, H, 0.85f, 0.9f, out bx0, out bx1, out by0, out by1);
                var shot3 = new Shot();
                var shot4 = new Shot();

                float savedScale = Time.timeScale;
                Time.timeScale = 0f;
                yield return null;

                panel.ApplyStage(3);
                for (int i = 0; i < 3; i++) yield return null;
                yield return CaptureToPixels(cam, W, H, (tex, px) =>
                {
                    shot3 = Analyse(px, W, H, bx0, bx1, by0, by1);
                    UnityEngine.Object.Destroy(tex);
                });

                panel.ApplyStage(4);
                for (int i = 0; i < 3; i++) yield return null;
                yield return CaptureToPixels(cam, W, H, (tex, px) =>
                {
                    shot4 = Analyse(px, W, H, bx0, bx1, by0, by1);
                    UnityEngine.Object.Destroy(tex);
                });

                Time.timeScale = savedScale;
                panel.ApplyStage(savedStage);

                float d43 = Diff(shot3, shot4);
                M5(sb, d43 >= 0.003f, "阶段 5（＋墨晕）相对阶段 4 有可见像素差",
                    "逐像素平均差 " + F(d43) + "（阶段4 平均亮度 " + F(shot3.mean) + " → 阶段5 " + F(shot4.mean) + "）");
                M5(sb, shot4.mean <= shot3.mean + 0.002f,
                    "墨晕把画面**压暗**而不是发亮（方向与图形学 Bloom 相反）",
                    "平均亮度 " + F(shot3.mean) + " → " + F(shot4.mean));
            }
            KillAllAlive();
            yield return WaitUnscaled(0.3f);

            // ==============================================================
            // ⑤ E5 水墨动作粒子
            // ==============================================================
            sb.AppendLine();
            sb.AppendLine("---- ⑤ E5 水墨动作粒子（溅墨 / 墨花）----");
            foreach (var sn in new[] { "Hidden/InkWash/InkBloom", "InkWash/InkSplash" })
            {
                var sh = Shader.Find(sn);
                M5(sb, sh != null, "着色器可解析 " + sn, sh != null ? "pass = " + sh.passCount : "Shader.Find 返回 null");
            }

            inkv.ClearAll();
            inkv.ResetDiagnostics();
            landing.ResetDiagnostics();
            int inkBefore = inkv.SpawnCount;

            spawner.ResetForTest(); spawner.Begin();
            { float t = Time.time; while (spawner.SpawnedCount < 1 && Time.time - t < 8f) yield return null; }
            yield return WaitUnscaled(1.0f);

            var targets = AliveEnemies();
            if (targets.Count == 0)
            {
                M5(sb, false, "溅墨：需要一只敌人", "刷不出敌人，本节跳过");
            }
            else
            {
                var e0 = targets[0];
                var hit = new DamageInfo
                {
                    amount = 6f,
                    hitPoint = e0.transform.position + Vector3.up * 1.0f,
                    hitDirection = (e0.transform.position - go.transform.position).normalized,
                    knockback = 0f, hitStun = 0f, hitStop = 0f,
                    sourceFaction = Faction.Player, source = go,
                };
                e0.TakeDamage(hit);
                yield return WaitUnscaled(0.15f);
                M5(sb, inkv.SpawnCount > inkBefore, "命中敌人会产生溅墨（受击方驱动）",
                    "溅墨 " + inkBefore + " → " + inkv.SpawnCount + " 次");
                M5(sb, inkv.PeakActive > 0, "墨点真的被激活参与渲染", "峰值活跃 " + inkv.PeakActive + " 个");
            }

            {
                int before2 = inkv.SpawnCount;
                var list2 = AliveEnemies();
                if (list2.Count > 0)
                {
                    var e1 = list2[0];
                    Vector3 to = e1.transform.position - go.transform.position; to.y = 0f;
                    if (to.sqrMagnitude < 1e-4f) to = Vector3.forward;
                    to.Normalize();
                    TeleportPlayer(go, e1.transform.position - to * 1.15f);
                    ctl.ResetToLocomotion();
                    yield return WaitUnscaled(0.5f);
                    for (int i = 0; i < 2; i++) { ctl.RequestInjectedAttack(); yield return WaitUnscaled(1.1f); }
                    M5(sb, inkv.SpawnCount > before2, "实战挥砍命中也会产生溅墨",
                        "溅墨 " + before2 + " → " + inkv.SpawnCount + " 次");
                }
            }

            {
                landing.ResetDiagnostics();
                int bloomBefore = landing.BloomCount;
                TeleportPlayer(go, new Vector3(0f, 2.6f, -3f));
                ctl.ResetToLocomotion();
                yield return WaitUnscaled(1.4f);
                M5(sb, landing.BloomCount > bloomBefore, "从高处落地会绽开墨花",
                    "墨花 " + bloomBefore + " → " + landing.BloomCount + " 次，最近下落速度 "
                    + F(landing.LastFallSpeed) + " m/s（阈值 " + F(landing.minFallSpeed) + "）");
                M5(sb, landing.LastScale > 0.3f, "墨花尺寸随下落速度缩放（重落更大）",
                    "本次倍率 " + F(landing.LastScale));

                int dashBefore = landing.DashBloomCount;
                TeleportPlayer(go, new Vector3(0f, 0.45f, -3f));
                ctl.ResetToLocomotion();
                yield return WaitUnscaled(0.5f);
                ctl.RequestInjectedDash();
                yield return WaitUnscaled(1.2f);
                M5(sb, landing.DashBloomCount > dashBefore,
                    "冲刺急停会产生墨花",
                    "冲刺墨花 " + dashBefore + " → " + landing.DashBloomCount
                    + "，因冷却跳过 " + landing.SkippedCooldown + " 次");
            }
            M5(sb, inkv.DroppedCount == 0, "墨点池没有溢出（池容量够用）",
                "池 " + inkv.poolSize + "　丢弃 " + inkv.DroppedCount + "　峰值 " + inkv.PeakActive);

            // ==============================================================
            // ⑥ 完整一局（M5 的出口条件）
            // ==============================================================
            sb.AppendLine();
            sb.AppendLine("---- ⑥ 完整跑完一局 ----");
            KillAllAlive();
            yield return WaitUnscaled(0.3f);
            var savedMax = health.maxHealth;
            health.maxHealth = 100000f;
            health.ResetHealth();

            inv.ResetAll(); level.ResetAll();
            run.ResetForTest();
            run.SetSeed(20260915);
            spawner.ResetForTest();
            if (room != null) room.ResetForTest();
            run.StartRun();
            yield return WaitUnscaled(0.3f);

            int rewards = 0;
            int guard = 0;
            float t0 = Time.unscaledTime;
            while (run.State != RunState.Victory && run.State != RunState.GameOver
                   && guard++ < 6000 && Time.unscaledTime - t0 < 120f)
            {
                if (run.State == RunState.Reward)
                {
                    yield return WaitUnscaled(0.2f);
                    if (choice.IsShowing)
                    {
                        int pick = rewards % Mathf.Max(1, choice.Options.Count);
                        if (choice.InjectChoice(pick)) rewards++;
                    }
                    continue;
                }

                KillAllAlive();
                yield return null;
            }

            sb.AppendLine("   房间 " + run.RoomIndex + "/" + run.roomsToClear
                          + "　升级/选择 " + rewards + " 次　迁移 " + run.TransitionCount + " 次"
                          + "　耗时 " + F(Time.unscaledTime - t0) + " s");
            M5(sb, run.State == RunState.Victory, "清空 " + run.roomsToClear + " 间房后通关（Victory）",
                "实际 " + run.State);
            M5(sb, run.RoomIndex >= run.roomsToClear, "房间推进到位", run.RoomIndex + " / " + run.roomsToClear);
            M5(sb, rewards >= 3, "一局里经历了多次三选一（技能可叠加成长）", "共 " + rewards + " 次选择");
            M5(sb, inv.AcquireCount >= 3, "选择都写进了背包", inv.Describe());
            M5(sb, stats.ModifierCount >= 3, "属性池收到了对应数量的修饰器",
                "修饰器 " + stats.ModifierCount + " 条　" + stats.Describe());
            M5(sb, run.IllegalTransitionCount == 0, "整局没有非法状态迁移", run.LastIllegal);

            health.maxHealth = savedMax;
            health.ResetHealth();

            // ==============================================================
            // ⑦ 性能
            // ==============================================================
            sb.AppendLine();
            sb.AppendLine("---- ⑦ 性能（Roguelike 全功能开启）----");
            int frames = 0;
            float elapsed = 0f;
            { float t1 = Time.unscaledTime; float t = Time.time;
              while (Time.time - t < 3.0f) { frames++; elapsed = Time.unscaledTime - t1; yield return null; } }
            float fps = elapsed > 0f ? frames / elapsed : 0f;
            sb.AppendLine("   帧数 " + frames + " / 墙钟 " + F(elapsed) + " s  → " + F(fps) + " fps @ "
                          + Screen.width + "×" + Screen.height);
            M5(sb, fps >= 55f, "1080p 下帧率 ≥ 55",
                F(fps) + " fps（若在录屏运行中取得会偏低，需无录屏复测）");

            // ---------------- 收尾 ----------------
            KillAllAlive();
            ctl.SetInjectedMove(Vector2.zero, false);
            ctl.EndInputOverride();
            if (stance != null) stance.combatExitDelay = 6f;
            if (panel != null) panel.visible = savedPanelVisible;
            if (choice != null) choice.Hide();
            spawner.spawnInterval = savedInterval;
            run.nextRoomDelay = savedNextRoom;
            if (savedDelays != null && spawner.waves != null)
                for (int i = 0; i < spawner.waves.Length && i < savedDelays.Length; i++)
                    if (spawner.waves[i] != null) spawner.waves[i].delayBefore = savedDelays[i];
            Time.timeScale = 1f;
            HitStop.Enabled = oldHitStop;

            sb.AppendLine();
            sb.AppendLine("---- 汇总 ----");
            sb.AppendLine("通过 " + _m5Pass + " / 未通过 " + _m5Fail);
            sb.AppendLine(_m5Fail == 0
                ? ">>> M5 达成（技能池/三选一/属性叠加/状态机齐备，能完整跑完一局；墨晕与水墨粒子已接入）"
                : ">>> M5 未达成");
            WriteReport("S5", sb.ToString());
            Debug.Log("[PlaytestHarness] S5 结束：通过 " + _m5Pass + " / 未通过 " + _m5Fail);
        }

        private static void WriteReport(string prefix, string text)
        {
            try
            {
                string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                string dir = Path.Combine(root, ReportDirRelative);
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, prefix + "_latest.txt"), text, new UTF8Encoding(true));
                File.WriteAllText(Path.Combine(dir,
                    prefix + "_" + System.DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + ".txt"),
                    text, new UTF8Encoding(true));
                Debug.Log("[PlaytestHarness] " + prefix + " 验收报告已写入 " + dir);
            }
            catch (System.Exception e)
            {
                Debug.LogError("[PlaytestHarness] 写报告失败: " + e.Message);
                Debug.Log("[PlaytestHarness] 报告内容:\n" + text);
            }
        }
    }
}
