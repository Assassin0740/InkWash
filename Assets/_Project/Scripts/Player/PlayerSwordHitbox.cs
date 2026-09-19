using InkWash.Combat;
using InkWash.Effects;
using InkWash.Roguelike;
using UnityEngine;

namespace InkWash.Player
{
    /// <summary>
    /// 把「玩家的挥砍」翻译成判定：监听 <see cref="PlayerController.HitMoment"/>，
    /// 在那一瞬把判定胶囊对齐到**剑身实际所在的位置**，然后开一个很短的窗口。
    ///
    /// 两个关键取舍：
    /// 1) **触发时机用代码时间（`comboHitTime`）而不是 Animator 事件** —— 事件要在动画窗口里
    ///    手工打点，改片段就失效；代码时间可以跟着 `AnimatorState.speed` 的倍率一起重算，
    ///    且验收里能直接断言"第几步、第几秒命中"。
    /// 2) **剑身端点用渲染体 AABB 反算，不用任何"锚点"字段** —— 本项目实测过：
    ///    组件里那个"刀光锚点"其实就是手骨，拿它当剑轴会量出假数据。渲染几何是唯一不会撒谎的东西。
    ///
    /// 执行顺序刻意设为负数：本组件的 <c>Update</c> 必须在 <see cref="Hitbox"/> 之前跑，
    /// 保证 Hitbox 采样时用的是**这一帧**的剑身位置（见下文 <c>Update</c> 的注释）。
    /// </summary>
    [DefaultExecutionOrder(-50)]
    [RequireComponent(typeof(Hitbox))]
    public class PlayerSwordHitbox : MonoBehaviour
    {
        public PlayerController player;
        public SwordVfx vfx;

        [Header("按连段步数（1/2/3）配置")]
        public float[] damage = { 16f, 20f, 32f };
        public float[] knockback = { 2.6f, 3.2f, 6.0f };
        public float[] hitStun = { 0.26f, 0.30f, 0.55f };
        [Tooltip("卡肉顿帧时长。0.055 太短（实测打龙像没打到），加长到接近 3 帧（60fps）")]
        public float hitStopDuration = 0.085f;

        [Header("命中反馈（打中敌人才给，挥空不给）")]
        [Tooltip("命中震屏幅度（度级）")]
        public float hitShakeAmplitude = 0.10f;
        public float hitShakeDuration = 0.14f;
        [Tooltip("命中 FOV 冲击（度）。视野瞬间被'推一下'再收回")]
        public float hitFovPunch = 4.5f;
        public float hitFovPunchDuration = 0.14f;

        [Header("判定体")]
        [Tooltip("判定半径（m）。挥砍是弧线，用胶囊扫线段近似")]
        public float radius = 0.42f;
        [Tooltip("窗口时长。只覆盖挥砍瞬间，避免站桩时误伤")]
        public float window = 0.14f;

        [Header("属性加成（Roguelike，留空自动取同物体）")]
        [Tooltip("伤害倍率 / 暴击 / 击退 / 吸血都从这里读。为 null 时行为与 Sprint 4 完全一致（全部倍率 = 1）")]
        public PlayerStats stats;
        [Tooltip("生命组件（吸血要回血）")]
        public InkWash.Player.PlayerHealth health;

        [Header("诊断（只读）")]
        [SerializeField] private int _hitMomentCount;
        [SerializeField] private float _lastBladeLength;
        [SerializeField] private float _lastDamage;
        [SerializeField] private int _critCount;
        [SerializeField] private int _newHitsLastFrame;
        [SerializeField] private float _totalHealed;
        [SerializeField] private float _maxNominalDamage;

        public int HitMomentCount => _hitMomentCount;
        public float LastBladeLength => _lastBladeLength;
        /// <summary>上一刀实际写入判定体的伤害（已含倍率与暴击）。验收用它算实测倍率。</summary>
        public float LastDamage => _lastDamage;
        public int CritCount => _critCount;
        /// <summary>上一帧新增的命中数（吸血按它结算）。</summary>
        public int NewHitsLastFrame => _newHitsLastFrame;
        public float TotalHealed => _totalHealed;
        /// <summary>不出暴击时的名义伤害（= 表值 × 伤害倍率），与 <see cref="LastDamage"/> 相除即实测暴击倍率。</summary>
        public float MaxNominalDamage => _maxNominalDamage;

        private Hitbox _hitbox;
        private float _windowTimer;
        private int _lastHitCount;
        private InkWash.CameraRig.ThirdPersonCamera _camRig;

        private InkWash.CameraRig.ThirdPersonCamera CameraRig
        {
            get
            {
                if (_camRig == null) _camRig = FindObjectOfType<InkWash.CameraRig.ThirdPersonCamera>();
                return _camRig;
            }
        }


        public void ResetDiagnostics()
        {
            _hitMomentCount = 0;
            _lastDamage = 0f;
            _critCount = 0;
            _newHitsLastFrame = 0;
            _totalHealed = 0f;
            _maxNominalDamage = 0f;
            _lastHitCount = 0;
            if (_hitbox != null) _hitbox.ResetDiagnostics();
        }

        private void Awake()
        {
            _hitbox = GetComponent<Hitbox>();
            _hitbox.ownerFaction = Faction.Player;
            _hitbox.owner = player != null ? player.gameObject : gameObject;
            _hitbox.radius = radius;
            _hitbox.oncePerTargetInWindow = true;

            // 属性池留空就自己找 —— 面板/预制体上少一个「忘了拉引用」的静默失败点
            if (stats == null) stats = GetComponentInParent<PlayerStats>();
            if (stats == null) stats = GetComponent<PlayerStats>();
            if (health == null) health = GetComponentInParent<InkWash.Player.PlayerHealth>();
            if (health == null) health = GetComponent<InkWash.Player.PlayerHealth>();

            DeactivateNow();
        }

        private void OnEnable()
        {
            if (player != null) player.HitMoment += OnHitMoment;
        }

        private void OnDisable()
        {
            if (player != null) player.HitMoment -= OnHitMoment;
        }

        private void OnHitMoment(int step)
        {
            if (_hitbox == null) return;
            _hitMomentCount++;

            int i = Mathf.Clamp(step - 1, 0, damage.Length - 1);

            // ★ 属性加成在这里消费（这是"越打越强"真正生效的那一行）。
            //   之所以在**每次挥砍时按当前属性重算**、而不是订阅 StatsChanged 缓存一份：
            //   挥砍期间玩家也可能升级（连击打断帧），缓存会落后；重算是幂等的，代价只有两次乘法。
            float dmg = damage[i];
            bool crit = false;
            if (stats != null) dmg = stats.RollDamage(damage[i], out crit);
            if (crit) _critCount++;
            _lastDamage = dmg;
            // 暴击倍数不方便反推时（没装暴击），名义伤害就是这一刀；验收用它算「实测/理论」比值
            if (!crit) _maxNominalDamage = dmg;

            _hitbox.damage = dmg;
            _hitbox.knockback = knockback[i] * (stats != null ? stats.KnockbackMultiplier : 1f);
            _hitbox.hitStun = hitStun[i];
            _hitbox.hitStop = hitStopDuration;
            _hitbox.radius = radius;

            AlignToBlade();
            _hitbox.Activate(window);
            _windowTimer = window;

            // 顿帧交给「真的打中了」才触发更准（见 EnemyBase.TakeDamage / PlayerHealth），
            // 但先手挥空也该有一点点"手感阻尼" —— 这里只给很短的，命中的顿帧由受击方再叠加。
        }

        /// <summary>
        /// 窗口期内**每帧**把判定胶囊重新贴到剑身上。
        ///
        /// 为什么不能只在 HitMoment 那一帧对齐一次（这是修过的一个真缺陷）：
        /// 挥砍时剑在 0.3 s 内扫过 130°，只对齐一帧等于"在弧线中间截了一张照片"，
        /// 判定胶囊就**冻在那一个瞬间的位置**。实测过：玩家站在敌人正前方 1.7 m，
        /// 命中帧那张照片里剑正好指向玩家**右侧**，胶囊离敌人最近处差 0.45 m（判定半径 0.42）——
        /// 于是"明明砍中了却一点血都不掉"，而且只在某些站位复现。
        /// 每帧重新对齐后，胶囊会跟着剑一起扫过去，整段弧线都算命中。
        /// </summary>
        private void Update()
        {
            // 吸血结算：靠判定体的命中计数**增量**触发，而不是在 OnHitMoment 里猜
            // —— 这一刀到底打中没有，只有 Hitbox 知道（它拿 ClosestPoint 做过实际判定）。
            if (_hitbox != null)
            {
                int hits = _hitbox.HitCount;
                _newHitsLastFrame = hits - _lastHitCount;
                _lastHitCount = hits;

                if (_newHitsLastFrame > 0 && stats != null && stats.Lifesteal > 0f && health != null)
                {
                    // 按**名义伤害**回血，不看敌人的减伤倍率：精英的弹反减伤语义是"它扛住了"，
                    // 不是"你没打中"。按实际掉血回血会让吸血在精英身上几乎失效，手感上是反直觉的。
                    float heal = _lastDamage * _newHitsLastFrame * stats.Lifesteal;
                    if (heal > 0f) { health.Heal(heal); _totalHealed += heal; }
                }

                // ★ 命中方反馈（第三十二轮）：此前只有"玩家被打"才震屏（PlayerHitFeedback），
                //   "玩家打中"却什么都没有 —— 打人没手感，这正是用户说的"没有打击感"的另一半。
                //   放在命中**增量**处（而不是 OnHitMoment）：只有真的打中了才震，挥空不骗反馈。
                if (_newHitsLastFrame > 0)
                {
                    var rig = CameraRig;
                    if (rig != null)
                    {
                        rig.Shake(hitShakeAmplitude, hitShakeDuration);
                        rig.FovPunch(hitFovPunch, hitFovPunchDuration);
                    }
                }
            }

            if (_windowTimer <= 0f) return;
            _windowTimer -= Time.deltaTime;
            AlignToBlade();
        }

        /// <summary>把判定线段对齐到剑身：起点=握把（手骨），终点=离握把最远的那个包围盒角点。</summary>
        private void AlignToBlade()
        {
            Transform hand = player != null && player.animator != null
                ? player.animator.GetBoneTransform(HumanBodyBones.RightHand)
                : null;
            Vector3 grip = hand != null ? hand.position : transform.position;

            Vector3 tip = grip;
            var weapon = vfx != null ? vfx.WeaponInstance : null;
            if (weapon != null)
            {
                float best = -1f;
                foreach (var r in weapon.GetComponentsInChildren<Renderer>(true))
                {
                    if (r == null) continue;
                    var b = r.bounds;
                    for (int i = 0; i < 8; i++)
                    {
                        var c = new Vector3(
                            (i & 1) == 0 ? b.min.x : b.max.x,
                            (i & 2) == 0 ? b.min.y : b.max.y,
                            (i & 4) == 0 ? b.min.z : b.max.z);
                        float d = Vector3.Distance(c, grip);
                        if (d > best) { best = d; tip = c; }
                    }
                }
            }

            Vector3 dir = tip - grip;
            float len = dir.magnitude;
            if (len < 0.05f) { dir = transform.forward; len = 0.5f; }
            else dir /= len;
            _lastBladeLength = len;

            transform.position = grip;
            transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
            _hitbox.pointA = Vector3.zero;                    // 握把
            _hitbox.pointB = new Vector3(0f, 0f, Mathf.Max(0.2f, len * 0.9f));  // 剑尖略收一点
        }

        public void DeactivateNow() { if (_hitbox != null) _hitbox.Deactivate(); }
    }
}
