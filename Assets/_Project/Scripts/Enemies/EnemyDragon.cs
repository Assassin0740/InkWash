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
        public float hoverOrbitRadius = 6.5f;
        public float hoverOrbitSpeedDeg = 55f;
        public float hoverAmplitudeDeg = 11f;
        public float hoverFrequency = 0.55f;

        [Header("攻击选择门槛")]
        [Tooltip("超过这个距离才有机会选龙息（远程）")]
        public float breathMinRange = 5.5f;
        [Tooltip("低于这个距离才有机会选撕咬")]
        public float biteMaxRange = 7.0f;
        [Tooltip("盘旋多久后俯冲回来（秒）")]
        public float hoverDuration = 4.5f;

        [Header("诊断（只读）")]
        [SerializeField] private string _currentAttackName = "-";
        [SerializeField] private int _tailSweepCount;
        [SerializeField] private int _biteCount;
        [SerializeField] private int _breathCount;
        [SerializeField] private int _hoverCount;
        [SerializeField] private int _spineLinksFound;
        [SerializeField] private bool _airborne;

        public string CurrentAttackName => _currentAttackName;
        public int TailSweepCount => _tailSweepCount;
        public int BiteCount => _biteCount;
        public int BreathCount => _breathCount;
        public int HoverCount => _hoverCount;
        public int SpineLinksFound => _spineLinksFound;
        public bool Airborne => _airborne;

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
        }

        protected override void OnEnterState(EnemyState s)
        {
            base.OnEnterState(s);

            if (s == EnemyState.Attack)
            {
                // 攻击开始那一帧把链复位，避免上一次动作的残留姿态串味
                ApplySpineOffsets(0f, 0f, 0, 0f, Vector3.zero);
            }
            else if (s == EnemyState.Idle || s == EnemyState.Chase || s == EnemyState.HitStun)
            {
                if (_airborne && s != EnemyState.Attack) EndHover();
            }
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

        protected override void ChooseAttack()
        {
            // ★ 验收专用：如果被要求"下一招必须打这一招"，就跳过加权随机。
            //   为什么要有这条路：四招是**加权随机**出的，而验收要逐个确认
            //   "甩尾真的有横摆 / 撕咬真的有前冲 / 龙息真的吐弹 / 盘旋真的升空"。
            //   靠等随机，30 s 里可能一次盘旋都轮不到（实测 30 s 只出 760 帧盘旋）。
            //   只影响被显式置位的那一次，随后自动清空 ⇒ 不改变正常游玩行为。
            if (_forcedNextAttack.HasValue)
            {
                _attack = _forcedNextAttack.Value;
                _forcedNextAttack = null;
                ApplyAttackTiming();
                return;
            }

            float d = _distanceToPlayer;

            // 优先级：近 → 撕咬（咬得着）；中 → 甩尾（扫得到）；远 → 龙息 / 盘旋
            // ★ 用**加权随机**而不是硬阈值：龙是 Boss，四种动作轮着来才有看头；
            //   硬阈值会让"站在某个距离"永远只见到同一招。
            bool canBite = d <= biteMaxRange;
            bool canBreath = d >= breathMinRange;

            float wTail = 1.0f;
            float wBite = canBite ? 1.2f : 0f;
            float wBreath = canBreath ? 1.0f : 0f;
            float wHover = (d >= breathMinRange && !_airborne) ? 0.7f : 0f;

            float sum = wTail + wBite + wBreath + wHover;
            if (sum <= 0f) { _attack = DragonAttack.TailSweep; }
            else
            {
                float r = UnityEngine.Random.value * sum;
                if (r < wTail) _attack = DragonAttack.TailSweep;
                else if (r < wTail + wBite) _attack = DragonAttack.Bite;
                else if (r < wTail + wBite + wBreath) _attack = DragonAttack.Breath;
                else _attack = DragonAttack.HoverOrbit;
            }

            ApplyAttackTiming();
        }

        /// <summary>
        /// 时间参数按招重算。**必须重算**：基类的冷却=windup+active+recover+cd，
        /// 而四招的长度差很多（甩尾 0.9s / 盘旋 4.5s），沿用一套会立刻串味。
        /// </summary>
        private void ApplyAttackTiming()
        {
            switch (_attack)
            {
                case DragonAttack.TailSweep:
                    attackWindup = 0.42f; attackActive = 0.30f; attackRecover = 0.55f;
                    _currentAttackName = "甩尾"; _tailSweepCount++;
                    break;
                case DragonAttack.Bite:
                    attackWindup = 0.38f; attackActive = 0.22f; attackRecover = 0.62f;
                    _currentAttackName = "撕咬"; _biteCount++;
                    break;
                case DragonAttack.Breath:
                    attackWindup = 0.62f; attackActive = 0.34f; attackRecover = 0.70f;
                    _currentAttackName = "龙息"; _breathCount++;
                    break;
                case DragonAttack.HoverOrbit:
                    attackWindup = hoverDuration * 0.45f; attackActive = hoverDuration * 0.30f;
                    attackRecover = hoverDuration * 0.25f;
                    _currentAttackName = "盘旋"; _hoverCount++;
                    break;
            }
            _cooldownTimer = attackWindup + attackActive + attackRecover + attackCooldown;
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
                case DragonAttack.TailSweep:
                    DoTailSweep(t);
                    break;
                case DragonAttack.Bite:
                    DoBite(t);
                    break;
                case DragonAttack.Breath:
                    DoBreath(t);
                    break;
                case DragonAttack.HoverOrbit:
                    DoHoverOrbit(t);
                    break;
            }

            // 身体抬高（容器节点；走 localPosition 而不是 bodyPosition —— 后者会乘 localScale²）
            if (_modelRoot != null)
            {
                float lift = bodyLift + (_airborne ? hoverHeight : 0f);
                _modelRoot.localPosition = _modelRootBaseLocalPos + Vector3.up * lift;
            }
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
                // 盘旋要抬起来 ⇒ 飞行时不锁；其余时候咬住地面高度
                if (!_airborne && !agentOk)
                {
                    var p = transform.position;
                    if (Mathf.Abs(p.y - _groundY) > 0.05f) { p.y = _groundY; transform.position = p; }
                }
                else if (agentOk) _groundY = transform.position.y;
            }

            // 盘旋高度：把根节点抬到 groundY + hoverHeight（视觉上整条龙飞起来）
            if (_airborne && _modelRoot != null)
            {
                float curY = _modelRoot.localPosition.y;
                float want = _modelRootBaseLocalPos.y + bodyLift + hoverHeight;
                float step = Mathf.Min(Mathf.Abs(want - curY), hoverHeight * 2.2f * Time.deltaTime);
                _modelRoot.localPosition = new Vector3(
                    _modelRoot.localPosition.x,
                    Mathf.MoveTowards(curY, want, step),
                    _modelRoot.localPosition.z);
            }
        }

        /// <summary>甩尾：横向大振幅扫。整条尾巴同相扫过去 + 身体轻微偏航带动。</summary>
        private void DoTailSweep(float t)
        {
            // 振幅包络：起手收 → 中段最大 → 收招回正（用 sin(π·u) 得到平滑单峰）
            float u = Mathf.Clamp01(t / Mathf.Max(0.01f, attackWindup + attackActive + attackRecover));
            float env = Mathf.Sin(Mathf.PI * u);

            float phase = 2f * Mathf.PI * sweepFrequency * t;
            // 横向摆 = 绕 Y 轴（模型朝前时，Y 轴旋转就是左右扫）
            float yaw = sweepAmplitudeDeg * env * Mathf.Sin(phase);
            ApplySpineOffsets(yaw, 0f, _spine.Count, sweepPhaseStepDeg, Vector3.zero);

            // 身体跟着偏航一点，让"扫"有躯干参与感
            transform.Rotate(Vector3.up, sweepYawAssist * env * Mathf.Sin(phase) * Time.deltaTime, Space.World);
        }

        /// <summary>撕咬：头部几节前俯 + 整体前冲。</summary>
        private void DoBite(float t)
        {
            float total = Mathf.Max(0.01f, attackWindup + attackActive + attackRecover);
            float u = Mathf.Clamp01(t / total);

            // 前冲位移：在 windup 末尾到 active 之间冲出去，然后收回
            float lunge = 0f;
            if (u > 0.30f && u < 0.70f)
            {
                float k = Mathf.InverseLerp(0.30f, 0.45f, u);       // 冲出
                float k2 = Mathf.InverseLerp(0.60f, 0.70f, u);      // 收回
                lunge = (k - k2) * biteLungeDistance;
            }
            Vector3 fwd = transform.forward; fwd.y = 0f; fwd.Normalize();
            if (_agent != null && _agent.enabled) _agent.Move(fwd * lunge * Time.deltaTime * 8f);

            // 头部前俯：只驱动链尾的 biteHeadLinks 节，且越靠末端越用力（"甩头咬"）
            float env = Mathf.Sin(Mathf.PI * Mathf.Clamp01((u - 0.15f) / 0.6f));
            float phase = 2f * Mathf.PI * biteFrequency * t;
            float pitch = biteAmplitudeDeg * env * (0.6f + 0.4f * Mathf.Sin(phase));

            int start = Mathf.Max(0, _spine.Count - biteHeadLinks);
            for (int i = start; i < _spine.Count; i++)
            {
                int k = i - start;
                var rot = _baseRot[i] * Quaternion.Euler(pitch * (1f + 0.15f * k), 0f, 0f);
                _spine[i].localRotation = rot;
            }
        }

        /// <summary>龙息：颈胸抬起（蓄势）→ 喷吐（末端节张开 + 生成墨弹）。</summary>
        private void DoBreath(float t)
        {
            float total = Mathf.Max(0.01f, attackWindup + attackActive + attackRecover);
            float u = Mathf.Clamp01(t / total);

            // 蓄势：抬头；喷吐：保持；收招：放下
            float rise;
            if (u < 0.45f) rise = Mathf.SmoothStep(0f, 1f, u / 0.45f);
            else if (u < 0.72f) rise = 1f;
            else rise = Mathf.SmoothStep(1f, 0f, (u - 0.72f) / 0.28f);

            // 抬头 = 绕 X 负向（模型朝 +Z 时，X 负旋转让头向上）
            float pitch = -breathAmplitudeDeg * rise;
            int start = Mathf.Max(0, _spine.Count - breathHeadLinks);
            for (int i = 0; i < _spine.Count; i++)
            {
                float w = i >= start ? 1f : 0f;
                if (w <= 0f) { _spine[i].localRotation = _baseRot[i]; continue; }
                int k = i - start;
                _spine[i].localRotation = _baseRot[i] * Quaternion.Euler(pitch * (1f + 0.2f * k), 0f, 0f);
            }

            // 轻微游动（让蓄势不是死的）
            float phase = 2f * Mathf.PI * breathFrequency * t;
            float sway = 8f * rise * Mathf.Sin(phase);
            for (int i = 0; i < Mathf.Min(_spine.Count, start); i++)
                _spine[i].localRotation = _baseRot[i] * Quaternion.Euler(0f, sway * (i / (float)Mathf.Max(1, start)), 0f);
        }

        /// <summary>盘旋：升空 + 绕玩家转圈 + 低频游动。</summary>
        private void DoHoverOrbit(float t)
        {
            if (!_airborne) BeginHover();

            // 绕玩家转
            if (PlayerRef.Exists)
            {
                Vector3 center = PlayerRef.Position;
                _orbitAngle += hoverOrbitSpeedDeg * Time.deltaTime;
                float rad = _orbitAngle * Mathf.Deg2Rad;
                Vector3 want = center + new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad)) * hoverOrbitRadius;

                Vector3 delta = want - transform.position;
                delta.y = 0f;
                if (_agent != null && _agent.enabled) _agent.Move(delta * Time.deltaTime * 2.2f);
                else transform.position += delta * Time.deltaTime * 2.2f;

                // 朝向切线方向（飞行感）
                Vector3 tangent = new Vector3(Mathf.Cos(rad), 0f, -Mathf.Sin(rad));
                if (tangent.sqrMagnitude > 1e-6f)
                    transform.rotation = Quaternion.Slerp(transform.rotation,
                        Quaternion.LookRotation(tangent, Vector3.up), 4f * Time.deltaTime);
            }

            // 低频游动（整条链）
            float phase = 2f * Mathf.PI * hoverFrequency * t;
            float yaw = hoverAmplitudeDeg * Mathf.Sin(phase);
            ApplySpineOffsets(yaw, 0f, _spine.Count, 40f, Vector3.zero);
        }

        /// <summary>
        /// 通用脊骨驱动：给每节叠一个旋转偏移，**相位沿链递增**（这就是"鞭子"的来源）。
        ///
        ///   offset_i = Euler( pitchEff(i), yawEff(i), 0 )，其中
        ///   yawEff(i) = yawMax · sin(2πft − i·φ)
        ///
        /// `yawMax` 可以传 0 表示不驱动该轴。`links` 限制驱动几节（0/负 = 全部）。
        /// </summary>
        private void ApplySpineOffsets(float yawMaxDeg, float pitchMaxDeg, int links, float phaseStepDeg, Vector3 unused)
        {
            if (_spine.Count == 0) return;
            int n = links <= 0 ? _spine.Count : Mathf.Min(links, _spine.Count);
            float phase = 2f * Mathf.PI * sweepFrequency * Time.time;
            float step = phaseStepDeg * Mathf.Deg2Rad;

            for (int i = 0; i < n; i++)
            {
                float ph = phase - i * step;
                float yaw = yawMaxDeg * Mathf.Sin(ph);
                float pitch = pitchMaxDeg * Mathf.Sin(ph);
                _spine[i].localRotation = _baseRot[i] * Quaternion.Euler(pitch, yaw, 0f);
            }
            // 未驱动的节保持基准姿态
            for (int i = n; i < _spine.Count; i++) _spine[i].localRotation = _baseRot[i];
        }

        /// <summary>回到地面姿态（清所有链偏移 + 收 lift）。</summary>
        private void EndHover()
        {
            _airborne = false;
            for (int i = 0; i < _spine.Count; i++) _spine[i].localRotation = _baseRot[i];
            if (_modelRoot != null)
                _modelRoot.localPosition = _modelRootBaseLocalPos + Vector3.up * bodyLift;
        }

        private void BeginHover()
        {
            _airborne = true;
            _orbitAngle = 0f;
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
        protected override void PerformHit()
        {
            switch (_attack)
            {
                case DragonAttack.TailSweep:
                case DragonAttack.Bite:
                    if (_hitbox != null) _hitbox.Activate(Mathf.Max(0.08f, attackActive));
                    break;

                case DragonAttack.Breath:
                    SpawnBreath();
                    break;

                case DragonAttack.HoverOrbit:
                    // 盘旋期不造成伤害（它的意义是走位/喘口气），但落地瞬间会接一次俯冲
                    if (_hitbox != null) _hitbox.Activate(0.14f);
                    break;
            }
        }

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

            int cnt = Mathf.Max(1, breathProjectileCount);
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
