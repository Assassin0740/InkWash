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
            public GameObject playerGo;
            public int upperLayerIndex = 1;
            /// <summary>躯干倾角用的两根骨骼（髋、胸）。没有它们就没法量化「仰着身子走」。</summary>
            public Transform hips, chest;
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
                    walk.torsoLeanAvg > -8f,
                    "平均 " + F(walk.torsoLeanAvg) + "°  区间["
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
                    runC.torsoLeanAvg > -8f,
                    "平均 " + F(runC.torsoLeanAvg) + "°  区间["
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
                check("占位刀身已生成（不是空手挥空气）", ctx.vfx.HasPlaceholderBlade,
                    ctx.vfx.HasPlaceholderBlade ? "已生成" : "未生成");
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
                chk("问题 1 走路片段躯干不后仰（平均倾角 > -8°）", w.leanAvg > -8f,
                    "Walk 平均 " + F(w.leanAvg) + "°  区间[" + F(w.leanMin) + "~" + F(w.leanMax) + "]°");
            var rr = FindPose(rows, "Run");
            if (rr != null)
                chk("问题 1/3 跑步片段躯干不后仰（平均倾角 > -8°）", rr.leanAvg > -8f,
                    "Run 平均 " + F(rr.leanAvg) + "°  区间[" + F(rr.leanMin) + "~" + F(rr.leanMax) + "]°");

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
                vfx = playerGo.GetComponent<SwordVfx>()
            };
            if (ctx.ctl == null || ctx.anim == null || cam == null) return null;
            ctx.upperLayerIndex = FindLayerIndex(ctx.anim, "UpperBody");
            if (ctx.anim.isHuman)
            {
                ctx.hips = ctx.anim.GetBoneTransform(HumanBodyBones.Hips);
                ctx.chest = ctx.anim.GetBoneTransform(HumanBodyBones.Chest);
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
