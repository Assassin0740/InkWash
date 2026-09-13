using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using InkWash.Player;

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
    /// </summary>
    public static class PlaytestHarness
    {
        private const string ReportDirRelative = "Tools/reports";

        /// <summary>每个阶段的起始位置（场地中央偏南，给前进方向留足空间）。</summary>
        private static readonly Vector3 StageAnchor = new Vector3(0f, 0.05f, -14f);

        /// <summary>复位后等待相机与动画收敛的时间（不计入采样）。</summary>
        private const float SettleSeconds = 0.7f;

        // ------------------------------------------------------------------
        // 数据结构
        // ------------------------------------------------------------------

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

        // ------------------------------------------------------------------
        // 主流程：Sprint 1 移动与相机验收
        // ------------------------------------------------------------------

        public static IEnumerator S1LocomotionFlow()
        {
            var sb = new StringBuilder();
            var stages = new List<StageResult>();

            var playerGo = GameObject.Find("Player");
            if (playerGo == null) { WriteReport("<错误> 场景里找不到 Player 对象"); yield break; }

            var ctl = playerGo.GetComponent<PlayerController>();
            var anim = playerGo.GetComponentInChildren<Animator>();
            var cam = Camera.main;
            if (ctl == null || anim == null || cam == null)
            {
                WriteReport("<错误> 缺少 PlayerController / Animator / Main Camera");
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
                check("步行切到 Run 状态", walk.animStates.Contains("Run"), walk.animStates);
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

            WriteReport(sb.ToString());
        }

        // ------------------------------------------------------------------
        // 单阶段执行
        // ------------------------------------------------------------------

        private static IEnumerator RunStage(
            List<StageResult> sink, PlayerController ctl, Animator anim, Camera cam,
            GameObject playerGo, string name, float seconds, Vector2 move, bool runHeld, bool dashAtStart)
        {
            // 复位到锚点：CharacterController 必须先禁用，否则位置会被它回推
            var cc = playerGo.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            playerGo.transform.position = StageAnchor;
            playerGo.transform.rotation = Quaternion.identity;
            if (cc != null) cc.enabled = true;

            // 复位后静置，等相机阻尼与动画状态收敛 —— 这段不计入采样
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
            //   本协程在 Update 里采样，而 Cinemachine 在 LateUpdate 更新相机，
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

        // ------------------------------------------------------------------
        // 工具
        // ------------------------------------------------------------------

        private static string CurrentState(Animator anim)
        {
            var st = anim.GetCurrentAnimatorStateInfo(0);
            if (st.IsName("Idle")) return "Idle";
            if (st.IsName("Run")) return "Run";
            if (st.IsName("Dash")) return "Dash";
            return "Other";
        }

        private static StageResult Find(List<StageResult> list, string name)
        {
            foreach (var s in list) if (s.name == name) return s;
            return null;
        }

        private static string F(float v) => v.ToString("F3", CultureInfo.InvariantCulture);
        private static string N(float v) => v.ToString("0.##", CultureInfo.InvariantCulture);
        private static string V(Vector3 v) => "(" + F(v.x) + ", " + F(v.y) + ", " + F(v.z) + ")";

        private static void WriteReport(string text)
        {
            try
            {
                string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                string dir = Path.Combine(root, ReportDirRelative);
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "S1_latest.txt"), text, new UTF8Encoding(true));
                File.WriteAllText(Path.Combine(dir,
                    "S1_" + System.DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + ".txt"),
                    text, new UTF8Encoding(true));
                Debug.Log("[PlaytestHarness] 验收报告已写入 " + dir);
            }
            catch (System.Exception e)
            {
                Debug.LogError("[PlaytestHarness] 写报告失败: " + e.Message);
                Debug.Log("[PlaytestHarness] 报告内容:\n" + text);
            }
        }
    }
}
