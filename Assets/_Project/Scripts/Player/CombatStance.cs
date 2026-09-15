using System.Collections.Generic;
using UnityEngine;
using InkWash.Effects;

namespace InkWash.Player
{
    /// <summary>
    /// 战斗 / 非战斗「姿态」管理 —— 解决「平时站着应该是自然双手下垂」。
    ///
    /// 为什么需要它：剑身方向**固死**在右手骨骼上（物理上正确 —— 拳头握剑的几何关系是常量）。
    /// 而手臂自然下垂时**前臂是竖直的**，与它近似垂直的剑身只能**水平横伸**
    /// （实测与「竖直向下」夹角 91.6°）。只换 Idle 片段救不了：任何「垂手」片段 + 手里有剑 = 剑横着戳。
    /// ⇒ 非战斗时必须**把剑收起来**，同时把 Idle 换成真正的垂手片段。
    ///
    /// 两个状态：
    /// - **非战斗（默认）**：Idle 播 <see cref="relaxedIdleClip"/>（自然双手下垂），
    ///   武器挂到 <see cref="backSocket"/>（背部）；握拳补丁关闭。
    /// - **战斗**：Idle 播控制器里原本的持剑待机，武器握在右手；握拳补丁开启。
    ///
    /// 进入战斗：触发攻击（<see cref="PlayerController.SwingStarted"/>）或冲刺（<see cref="PlayerController.DashStarted"/>）。
    /// 退出战斗：连续 <see cref="combatExitDelay"/> 秒既没攻击也没冲刺，且已回到 Locomotion。
    ///
    /// 实现选择：用 <see cref="AnimatorOverrideController"/> 只覆盖 **Idle 这一个片段**的映射，
    /// **不改 AnimatorController 资产**。理由是新增状态要牵动所有进出转移的条件（回归面太大），
    /// 而这里要变的只是「Idle 播哪段」。
    ///
    /// 挂点约定沿用武器预制体的 socket 规则：**根节点保持 identity（根原点 = 挂载点）**。
    /// 因此搬运武器到哪个 socket，只要 <c>SetParent(socket, false)</c> 再归零局部变换即可 ——
    /// 本组件不需要额外记「挂点偏移 / 缩放」字段。
    /// </summary>
    [DefaultExecutionOrder(9000)]
    [DisallowMultipleComponent]
    public class CombatStance : MonoBehaviour
    {
        [Header("引用")]
        [Tooltip("玩家 Animator（留空自动取同物体）")]
        public Animator animator;

        [Tooltip("玩家控制器（用来订阅攻击 / 冲刺事件；留空自动取同物体）")]
        public PlayerController player;

        [Tooltip("刀光组件（用来拿正式武器实例；留空自动在子层级找）")]
        public SwordVfx swordVfx;

        [Tooltip("背部挂点：不拿剑时武器挂到这里。约定与武器预制体一致 —— 节点原点 = 握柄中点，节点 +Y = 指向剑尖")]
        public Transform backSocket;

        [Tooltip("握拳补丁：非战斗（手里没剑）时必须关掉，否则空手也攥着拳头")]
        public WeaponHandPose handPose;

        [Header("待机片段（两个姿态各一段）")]
        [Tooltip("非战斗（平时站着）播的片段，要求是「自然双手下垂」的徒手待机（本项目用 UAL1 的 Rig|Idle_Loop）。\n" +
                 "留空则不做动画切换，只做收剑。")]
        public AnimationClip relaxedIdleClip;

        [Tooltip("战斗（持剑）待机播的片段。留空则用控制器 Idle 状态槽位里原本挂的片段。\n" +
                 "★ 为什么要显式配置：旧实现靠「片段名 == \"Idle\"」去认槽位，一旦把控制器里的 Idle\n" +
                 "换成循环副本（名字变成 Feng_Idle_Loop），匹配就失效 —— 而且**整套战斗姿态会静默失效**\n" +
                 "（武器不挂手、不握拳），控制台一条报错都没有。")]
        public AnimationClip combatIdleClip;

        [Tooltip("控制器里 Idle 状态**原本挂的片段**（本项目 = Feng_Idle_Loop）。\n" +
                 "用来在 AnimatorOverrideController 的覆盖表里精确定位「Idle 槽位」那把钥匙。\n" +
                 "★ 留空也能跑（会退回按名字猜），但**配置上最稳**：按名字猜曾把名字里带 \"Idle\" 的\n" +
                 "跑步片段（Run01_IdleArm）误当 Idle 槽位，导致 Run 状态被覆盖成待机、且零报错。")]
        public AnimationClip idleSlotClip;

        [Header("节奏")]
        [Tooltip("最后一次攻击 / 冲刺之后，静置多少秒收回武器、回到自然站立")]
        public float combatExitDelay = 6f;

        [Header("初始")]
        [Tooltip("勾选则一开局就是战斗姿态（持剑）。默认关闭 —— 平时站着应该是自然垂手。")]
        public bool startInCombat = false;

        // ---------------- 只读探针（验收报告直接打印）----------------

        /// <summary>当前是否处于战斗姿态。</summary>
        public bool InCombat { get; private set; }

        /// <summary>Idle 状态当前实际驱动的片段名（非战斗时应为垂手片段）。</summary>
        public string CurrentIdleClipName { get; private set; } = "<未就绪>";

        /// <summary>武器此刻挂在哪（"手:名称" / "背:名称"）。</summary>
        public string WeaponMountPath { get; private set; } = "<未就绪>";

        /// <summary>切入战斗姿态的累计次数。</summary>
        public int SwitchToCombatCount { get; private set; }

        /// <summary>切回非战斗姿态的累计次数。</summary>
        public int SwitchToRelaxedCount { get; private set; }

        /// <summary>自最后一次战斗输入（攻击 / 冲刺）起经过的秒数。</summary>
        public float SinceCombatInput { get; private set; }

        /// <summary>组件是否已完成初始化（武器实例与控制器都拿到了）。</summary>
        public bool IsReady => _ready;

        // ---------------- 内部 ----------------
        private AnimatorOverrideController _ovr;
        private AnimationClip _idleSlot;           // OverrideController 里 Idle 状态的槽位（必须由 GetOverrides 拿到的原始 key）
        private AnimationClip _combatIdleClip;     // 战斗待机实际播的片段
        private GameObject _weapon;
        private Transform _handParent;             // 武器原本的父节点（右手挂点）
        private bool _ready;

        private void Awake()
        {
            if (animator == null) animator = GetComponent<Animator>();
            if (player == null) player = GetComponent<PlayerController>();
            if (swordVfx == null) swordVfx = GetComponentInChildren<SwordVfx>(true);
            if (handPose == null) handPose = GetComponentInChildren<WeaponHandPose>(true);
        }

        private void OnEnable()
        {
            if (player == null) return;
            player.SwingStarted += OnSwingStarted;
            player.DashStarted += OnDashStarted;
        }

        private void OnDisable()
        {
            if (player == null) return;
            player.SwingStarted -= OnSwingStarted;
            player.DashStarted -= OnDashStarted;
        }

        private void Start()
        {
            InCombat = startInCombat;
            if (TryReady()) ApplyStance(InCombat, true);
        }

        private void OnDestroy()
        {
            if (animator != null && _ovr != null && ReferenceEquals(animator.runtimeAnimatorController, _ovr))
                animator.runtimeAnimatorController = _ovr.runtimeAnimatorController;
        }

        private void Update()
        {
            if (!_ready)
            {
                if (!TryReady()) return;
                ApplyStance(InCombat, true);
            }

            if (!InCombat) return;

            // 攻击 / 冲刺期间计时器清零；回到 Locomotion 才开始累加
            bool busy = player != null && player.Phase != ActionPhase.Locomotion;
            if (busy)
            {
                SinceCombatInput = 0f;
                return;
            }
            SinceCombatInput += Time.deltaTime;
            if (SinceCombatInput >= combatExitDelay) ApplyStance(false);
        }

        // ---------------- 对外 ----------------

        /// <summary>强制切姿态（演示 / 验收用）。</summary>
        public void ForceStance(bool combat)
        {
            SinceCombatInput = 0f;
            ApplyStance(combat, true);
        }

        // ---------------- 内部实现 ----------------

        private bool TryReady()
        {
            if (_ready) return true;
            if (animator == null) return false;

            var ctrl = animator.runtimeAnimatorController;
            if (ctrl == null) return false;

            // 1. 建「只覆盖 Idle」的 override controller
            if (_ovr == null)
            {
                _ovr = ctrl as AnimatorOverrideController;
                if (_ovr == null) _ovr = new AnimatorOverrideController(ctrl);

                // 找 Idle 状态的**槽位**。注意必须用 GetOverrides 拿到的原始 key，
                // 否则 _ovr[key] = clip 这个索引器设不进去（静默无效）。
                //
                // ★ 为什么不只是 Contains("Idle")：控制器里 Idle 槽位挂的可能是循环副本
                // （Feng_Idle_Loop），所以要容错；但光用 Contains 会**误伤**名字里碰巧带 "Idle"
                // 的其它片段 —— 实测把烘焙的「持剑跑」命名为 Run01_IdleArm 后，
                // Run 状态被当成 Idle 槽位覆盖成了待机姿态（跑步时角色在待机！），且零报错。
                // 规则：含 "Idle" 且**不含任何别的动作关键词**才算 Idle 槽位。
                var pairs = new List<KeyValuePair<AnimationClip, AnimationClip>>();
                _ovr.GetOverrides(pairs);

                // ① 显式指定的槽位片段最优先（最稳，改名/重命名都不会误伤）
                if (idleSlotClip != null)
                {
                    foreach (var kv in pairs)
                        if (kv.Key != null && (kv.Key == idleSlotClip || kv.Key.name == idleSlotClip.name))
                        { _idleSlot = kv.Key; break; }
                }
                // ② 没有显式配置就按名字猜：含 Idle、且不含别的动作关键词
                if (_idleSlot == null)
                {
                    foreach (var kv in pairs)
                    {
                        if (kv.Key == null || !kv.Key.name.Contains("Idle")) continue;
                        if (ContainsOtherActionToken(kv.Key.name)) continue;
                        _idleSlot = kv.Key; break;
                    }
                }
                // ③ 实在找不到再放宽（尽量别走到这里）
                if (_idleSlot == null)
                {
                    foreach (var kv in pairs)
                        if (kv.Key != null && kv.Key.name.Contains("Idle")) { _idleSlot = kv.Key; break; }
                }
                if (_idleSlot == null) return false;   // 控制器里还没备好 Idle，下一帧再试

                // 战斗待机片段：显式配置优先，否则用槽位自身挂的那个
                _combatIdleClip = combatIdleClip != null ? combatIdleClip : _idleSlot;
            }

            // 2. 记下武器实例与它原本的父节点
            if (_weapon == null && swordVfx != null && swordVfx.WeaponInstance != null)
            {
                _weapon = swordVfx.WeaponInstance;
                _handParent = _weapon.transform.parent;
            }

            if (animator.runtimeAnimatorController != _ovr) animator.runtimeAnimatorController = _ovr;
            _ready = true;
            return true;
        }

        /// <summary>片段名里是否含「别的动作」关键词 —— 含则它不可能是 Idle 槽位。</summary>
        private static readonly string[] OtherActionTokens =
        {
            "Run", "Walk", "Atk", "Attack", "Sword", "Dash", "Dodge", "Jump", "Hit", "Death", "Roll", "Cast"
        };

        private static bool ContainsOtherActionToken(string clipName)
        {
            if (string.IsNullOrEmpty(clipName)) return true;
            for (int i = 0; i < OtherActionTokens.Length; i++)
                if (clipName.IndexOf(OtherActionTokens[i], System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        private void ApplyStance(bool combat, bool force = false)
        {
            if (!force && InCombat == combat) return;
            InCombat = combat;
            if (combat) SwitchToCombatCount++; else SwitchToRelaxedCount++;

            // ① Idle 片段：只改 ovr 里那一条映射
            if (_ovr != null && _idleSlot != null)
            {
                AnimationClip clip = combat
                    ? _combatIdleClip
                    : (relaxedIdleClip != null ? relaxedIdleClip : _combatIdleClip);
                _ovr[_idleSlot] = clip;
                CurrentIdleClipName = clip != null ? clip.name : "<null>";
            }

            // ② 武器挂点
            MountWeapon();

            // ③ 握拳补丁：手里没剑就别攥拳
            if (handPose != null) handPose.enabled = combat;
        }

        private void MountWeapon()
        {
            if (_weapon == null) return;
            Transform t = _weapon.transform;

            Transform target = InCombat ? _handParent : backSocket;
            if (target == null) { WeaponMountPath = "<挂点缺失>"; return; }

            // socket 约定：根节点保持 identity，归零局部变换即对齐到挂点
            t.SetParent(target, false);
            t.localPosition = Vector3.zero;
            t.localRotation = Quaternion.identity;
            t.localScale = Vector3.one;

            WeaponMountPath = (InCombat ? "手:" : "背:") + target.name;
        }

        private void OnSwingStarted(int step)
        {
            SinceCombatInput = 0f;
            if (!InCombat) ApplyStance(true);
        }

        private void OnDashStarted()
        {
            SinceCombatInput = 0f;
            if (!InCombat) ApplyStance(true);
        }
    }
}
