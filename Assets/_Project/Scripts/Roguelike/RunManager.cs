using System;
using System.Collections.Generic;
using UnityEngine;
using InkWash.Enemies;
using InkWash.Player;
using InkWash.UI;

namespace InkWash.Roguelike
{
    /// <summary>一局的宏观状态。迁移规则见 <see cref="RunManager.kLegalTransitions"/>。</summary>
    public enum RunState
    {
        /// <summary>主菜单（标题 + 开始）。<c>autoStartRun</c> 打开时会跳过它。</summary>
        MainMenu = 0,
        /// <summary>正常游玩：可移动、可战斗、波次在刷。</summary>
        Playing = 1,
        /// <summary>升级奖励：时间冻结，三选一。</summary>
        Reward = 2,
        /// <summary>玩家死亡。</summary>
        GameOver = 3,
        /// <summary>清完目标房间数。</summary>
        Victory = 4,
    }

    /// <summary>
    /// 一局游戏的状态机：主菜单 → 进局 → 战斗 → 奖励 → … → 结算。
    ///
    /// 为什么要写成"显式迁移表"而不是散落的 if：
    ///   散落的 if 到最后没人说得清"从 Reward 能不能直接到 Victory"，
    ///   而这个问题的答案直接决定玩家会不会遇到"选完技能直接通关"这种 bug。
    ///   迁移表把合法边写在一个地方，**非法迁移是能断言的对象**（<see cref="IllegalTransitionCount"/>），
    ///   而不是"希望它不会发生"。验收脚本会故意尝试一次非法迁移，确认被拒绝。
    ///
    /// 暂停的实现与一个必须避开的坑：
    ///   奖励界面用 <c>Time.timeScale = 0</c> 冻结。但 <see cref="InkWash.Combat.HitStop"/> 也改 timeScale，
    ///   而且它恢复时会写回"进入它之前的值"。若命中顿帧的 0.08s 里玩家升级、界面把 timeScale 设成 0，
    ///   顿帧倒计时结束就会把 timeScale 恢复成 1 —— **暂停被无声解除**，玩家可以边选技能边被砍。
    ///   所以进 Reward 前先 <c>HitStop.ForceEnd()</c> 把顿帧结清（见 <see cref="EnterReward"/>）。
    /// </summary>
    [DisallowMultipleComponent]
    public class RunManager : MonoBehaviour
    {
        public static RunManager Instance { get; private set; }

        [Header("引用")]
        public PlayerHealth playerHealth;
        public RoomController room;
        public WaveSpawner spawner;
        public SkillInventory inventory;
        public LevelSystem level;
        public SkillChoicePanel choicePanel;

        [Tooltip("主相机（震屏要在接管 timeScale 之前停掉）。留空自动找。")]
        public InkWash.CameraRig.ThirdPersonCamera cameraRig;

        [Header("技能池（全部可抽取的技能资产）")]
        public List<SkillData> skillPool = new List<SkillData>();

        [Header("一局配置")]
        [Tooltip("要清空几个房间才算通关")]
        public int roomsToClear = 3;
        [Tooltip("勾上则进入 Play 直接开始一局（验收与录屏用）。关掉才走主菜单。")]
        public bool autoStartRun = true;
        [Tooltip("清完一间房后隔多久开下一间（让玩家看清开门再走）")]
        public float nextRoomDelay = 2.0f;

        [Header("诊断（只读）")]
        [SerializeField] private RunState _state = RunState.MainMenu;
        [SerializeField] private float _stateEnterTime;
        [SerializeField] private int _transitionCount;
        [SerializeField] private int _illegalTransitionCount;
        [SerializeField] private int _roomIndex;
        [SerializeField] private int _rewardCount;
        [SerializeField] private int _spawnedInThisRun;
        [SerializeField] private string _lastIllegal = "（无）";
        [SerializeField] private int _lastDrawnCount;

        /// <summary>状态变更时抛（旧状态、新状态）。UI 与音量/音乐切换订阅它。</summary>
        public event Action<RunState, RunState> StateChanged;

        public RunState State => _state;
        public float StateEnterTime => _stateEnterTime;
        public int TransitionCount => _transitionCount;
        public int IllegalTransitionCount => _illegalTransitionCount;
        public int RoomIndex => _roomIndex;
        public int RewardCount => _rewardCount;
        public int SpawnedInThisRun => _spawnedInThisRun;
        public string LastIllegal => _lastIllegal;
        public int LastDrawnCount => _lastDrawnCount;
        public float StateAge => Time.unscaledTime - _stateEnterTime;
        /// <summary>本局是否已结束（结算界面显示中）。</summary>
        public bool IsRunOver => _state == RunState.GameOver || _state == RunState.Victory;

        /// <summary>迁移表。每一条都写清楚"为什么允许"，改动前先想清楚会不会破坏玩家体验。</summary>
        private static readonly Dictionary<RunState, RunState[]> kLegalTransitions =
            new Dictionary<RunState, RunState[]>
            {
                { RunState.MainMenu, new[] { RunState.Playing } },
                // Playing 是枢纽：升级 → Reward，清完 → Victory，死亡 → GameOver
                { RunState.Playing,  new[] { RunState.Reward, RunState.Victory, RunState.GameOver, RunState.MainMenu } },
                // 奖励只能回到战斗；**不允许**从奖励直接胜利 —— 那等于"选个技能就通关"
                { RunState.Reward,   new[] { RunState.Playing, RunState.GameOver } },
                // 结算后只能重开一局
                { RunState.GameOver, new[] { RunState.MainMenu, RunState.Playing } },
                { RunState.Victory,  new[] { RunState.MainMenu, RunState.Playing } },
            };

        public static bool IsLegal(RunState from, RunState to)
        {
            RunState[] allow;
            if (!kLegalTransitions.TryGetValue(from, out allow)) return false;
            for (int i = 0; i < allow.Length; i++) if (allow[i] == to) return true;
            return false;
        }

        private float _nextRoomTimer = -1f;
        private System.Random _rng;

        private void Awake()
        {
            Instance = this;
            _rng = new System.Random(level != null ? 20260915 : 20260915);
            _stateEnterTime = Time.unscaledTime;
        }

        private void OnEnable()
        {
            if (level != null) level.LeveledUp += OnLeveledUp;
            if (room != null) room.Cleared += OnRoomCleared;
            if (playerHealth != null) playerHealth.Died += OnPlayerDied;
        }

        private void OnDisable()
        {
            if (level != null) level.LeveledUp -= OnLeveledUp;
            if (room != null) room.Cleared -= OnRoomCleared;
            if (playerHealth != null) playerHealth.Died -= OnPlayerDied;
        }

        private void Start()
        {
            if (autoStartRun) StartRun();
            else SetState(RunState.MainMenu);
        }

        private void Update()
        {
            // 清完一间房 → 延迟后开下一间（或通关）。
            // ★ 倒计时只在战斗中走：Reward（timeScale=0、选卡中）不推进，等回到 Playing 再数
            //   —— 与 OnRoomCleared 的"无条件上膛"配套（见下）。
            if (_nextRoomTimer > 0f)
            {
                if (_state != RunState.Playing) return;
                _nextRoomTimer -= Time.unscaledDeltaTime;
                if (_nextRoomTimer <= 0f) AdvanceRoom();
            }
        }

        // ==================================================================
        //  流程动作
        // ==================================================================

        /// <summary>开一局（也用于重开）：复位一切，进入 Playing。</summary>
        public void StartRun()
        {
            ResetRunContents();
            SetState(RunState.Playing);

            if (spawner != null)
            {
                spawner.ResetForTest();
                spawner.Begin();
                _spawnedInThisRun = spawner.SpawnedCount;
            }
        }

        /// <summary>把玩家、房间、技能、经验全部复位。**重开一局的唯一入口**。</summary>
        public void ResetRunContents()
        {
            _roomIndex = 0;
            _rewardCount = 0;
            _spawnedInThisRun = 0;
            _nextRoomTimer = -1f;

            if (inventory != null) inventory.ResetAll();
            if (level != null) level.ResetAll();
            if (playerHealth != null) { playerHealth.ResetDiagnostics(); playerHealth.ResetHealth(); }
            if (choicePanel != null) choicePanel.Hide();
            if (room != null) room.ResetForTest();

            Time.timeScale = 1f;
            InkWash.Combat.HitStop.ForceEnd();
        }

        private void OnLeveledUp(int newLevel)
        {
            // 只有战斗中才弹奖励；已经结算/暂停中就把门票留在队列里，等回到 Playing 再消耗
            if (_state == RunState.Playing && level != null && level.PendingLevelUps > 0)
                EnterReward();
        }

        private void OnRoomCleared()
        {
            // ★★ 第三十轮实测修复：房间清空与升级奖励存在**同帧竞态**。
            //   清完最后一波的那一刀如果正好触发升级，本回调到达时 _state 已是 Reward，
            //   旧代码 `if (_state != Playing) return;` 把这张**一次性**的门票直接扔掉
            //   —— RoomController._isCleared 已置 true，Cleared 不会再发第二次，
            //   _nextRoomTimer 永远不会上膛 ⇒ 房间推进永久卡死。
            //   实测（S5 验收 120 s 墙钟跑满）：12 只全清 / allClr=True / isCleared=True /
            //   _nextRoomTimer=-1 / room 0/3，且 Console 干净（又一个静默失败）。
            //   与 OnLeveledUp 的"门票留队列、等回到 Playing 再消耗"同一套纪律：
            //   这里**无条件**上膛，真正的倒数在 Update 里等回 Playing 再走。
            _nextRoomTimer = Mathf.Max(0.01f, nextRoomDelay);
        }

        private void AdvanceRoom()
        {
            _nextRoomTimer = -1f;
            _roomIndex++;

            if (_roomIndex >= roomsToClear)
            {
                // 通关同样进结算冻结（与 GameOver 同一表现语义）
                TakeOverTimeScale();
                Time.timeScale = 0f;
                SetState(RunState.Victory);
                return;
            }

            // 下一间：复位门与波次，重新开刷
            if (room != null) room.ResetForTest();
            if (spawner != null)
            {
                spawner.ResetForTest();
                spawner.Begin();
                _spawnedInThisRun = spawner.SpawnedCount;
            }
        }

        /// <summary>
        /// 接管 <c>Time.timeScale</c> 之前必须做的两件清理。**每个"要暂停/结算"的地方都要先调它。**
        ///
        /// ① <c>HitStop.ForceEnd()</c>：顿帧保存了进入时的 timeScale，倒计时结束会把它写回。
        ///    若在它活跃的 0.08 s 里外部把 timeScale 设成 0，顿帧一结束就把暂停**无声解除**。
        ///
        /// ② <c>StopShake()</c>：震屏直接写 <c>transform</c>，而它的倒计时此前用的是
        ///    <c>Time.deltaTime</c> —— timeScale = 0 时 `deltaTime == 0`，倒计时永远走不完。
        ///    表现就是**在攻击命中（顿帧+震屏）的同时弹出技能选择，画面会一直震**。
        ///    （相机侧已改成按 unscaled 递减并加 `timeScale &lt;= 0` 兜底，这里是显式再补一刀 ——
        ///    语义上"这一局的表现反馈已经结束了"，比依赖全局状态推断清楚。）
        /// </summary>
        private void TakeOverTimeScale()
        {
            InkWash.Combat.HitStop.ForceEnd();
            if (cameraRig == null) cameraRig = UnityEngine.Object.FindObjectOfType<InkWash.CameraRig.ThirdPersonCamera>();
            if (cameraRig != null) cameraRig.StopShake();
        }

        private void EnterReward()
        {
            if (choicePanel == null || skillPool == null || skillPool.Count == 0)
            {
                // 没有面板/没有技能资产时静默跳过，但要**消费掉门票**，
                // 否则每次击杀都会重试进入 Reward，日志被刷屏。
                if (level != null) level.ConsumePendingLevelUp();
                Debug.LogWarning("[RunManager] 升级了但没有可用的技能池/面板，已跳过奖励。");
                return;
            }

            // ★ 先把顿帧与震屏结清再暂停（否则顿帧恢复时会把 timeScale 写回 1，暂停被无声解除；
            //   震屏则因为倒计时吃 scaled deltaTime 而永远走不完 —— 见 TakeOverTimeScale）
            TakeOverTimeScale();

            var res = SkillPool.Draw(skillPool, inventory, choicePanel.optionCount, _rng);
            _lastDrawnCount = res.picked.Count;
            if (res.picked.Count == 0)
            {
                if (level != null) level.ConsumePendingLevelUp();
                Debug.LogWarning("[RunManager] 技能池已抽空（全部满级？），已跳过奖励。");
                return;
            }

            Time.timeScale = 0f;
            SetState(RunState.Reward);
            choicePanel.Show(res.picked, OnSkillChosen);
        }

        private void OnSkillChosen(SkillData picked)
        {
            if (inventory != null && picked != null) inventory.Acquire(picked);
            _rewardCount++;
            if (level != null) level.ConsumePendingLevelUp();

            Time.timeScale = 1f;
            SetState(RunState.Playing);

            // 一次爆多级（一刀清场）时会连着排队好几张门票：回到 Playing 后立刻消费下一张
            if (level != null && level.PendingLevelUps > 0) EnterReward();
        }

        private void OnPlayerDied()
        {
            if (IsRunOver) return;
            // 主菜单里死掉不算一局结束（实测踩到：MainMenu → GameOver 是非法迁移，
            // 会被迁移表拒掉并在 Console 里刷一条警告）。它只可能来自"还没开局就有东西能伤人"。
            if (_state == RunState.MainMenu) return;
            TakeOverTimeScale();
            // ★ 第三十一轮（用户实测反馈）：身陨后原来 timeScale=1 世界照跑——
            //   "显示身陨的时候游戏也没暂停"。现在结算即冻结（UI 用 unscaled 驱动，按钮照点）。
            //   验收口径同步改：S5「结算不冻结时间」判据改为「结算冻结时间」。
            Time.timeScale = 0f;
            SetState(RunState.GameOver);
        }

        // ==================================================================
        //  状态迁移（带合法性校验）
        // ==================================================================

        /// <summary>请求迁移。非法迁移**被拒绝并计数**，不抛异常 —— 抛异常会让一次误触发直接崩掉一局游戏。</summary>
        public bool SetState(RunState next)
        {
            if (next == _state) return false;

            if (!IsLegal(_state, next))
            {
                _illegalTransitionCount++;
                _lastIllegal = _state + " → " + next;
                Debug.LogWarning("[RunManager] 拒绝非法迁移：" + _lastIllegal);
                return false;
            }

            var prev = _state;
            _state = next;
            _stateEnterTime = Time.unscaledTime;
            _transitionCount++;

            if (StateChanged != null)
            {
                var handlers = StateChanged.GetInvocationList();
                for (int i = 0; i < handlers.Length; i++)
                {
                    try { ((Action<RunState, RunState>)handlers[i])(prev, next); }
                    catch (Exception e) { Debug.LogError("[RunManager] StateChanged 订阅者抛异常（已隔离）：" + e); }
                }
            }
            return true;
        }

        /// <summary>
        /// 验收复位：清空所有诊断并回到"未开局"。
        /// 注意**不要**在这里 StartRun —— 验收脚本常常要先断言"初始状态是 MainMenu"。
        /// </summary>
        public void ResetForTest()
        {
            TakeOverTimeScale();
            Time.timeScale = 1f;
            if (choicePanel != null) choicePanel.Hide();
            _state = RunState.MainMenu;
            _stateEnterTime = Time.unscaledTime;
            _transitionCount = 0;
            _illegalTransitionCount = 0;
            _lastIllegal = "（无）";
            _roomIndex = 0;
            _rewardCount = 0;
            _spawnedInThisRun = 0;
            _nextRoomTimer = -1f;
            _lastDrawnCount = 0;
            _rng = new System.Random(20260915);
        }

        /// <summary>把随机种子固定下来 —— 验收里"抽到哪三个"应当是可复现的。</summary>
        public void SetSeed(int seed) { _rng = new System.Random(seed); }

        public string Describe()
        {
            return _state + "（持续 " + StateAge.ToString("F1") + "s）"
                 + "  房间 " + _roomIndex + "/" + roomsToClear
                 + "  迁移 " + _transitionCount + " 次"
                 + "  非法迁移 " + _illegalTransitionCount + " 次"
                 + "  奖励 " + _rewardCount + " 次";
        }

        // ★★ 第三十一轮：主菜单 / 结算 UI 已迁到 UGUI（RunPresentation 运行时建 Canvas）。
        //   原 IMGUI 版本退役 —— 根因是 IMGUI 时代光标被锁死，"开始一局"根本点不了。
        //   UGUI 面板 + 状态驱动光标（MainMenu/Reward/GameOver/Victory 解锁，Playing 锁定）
        //   一并解决"三选一时鼠标动不了"。
    }
}
