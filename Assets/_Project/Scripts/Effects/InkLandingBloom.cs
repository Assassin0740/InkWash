using UnityEngine;
using InkWash.Player;

namespace InkWash.Effects
{
    /// <summary>
    /// 落地墨花：从空中落回地面（或冲刺急停）时，在脚下绽开一圈墨。
    ///
    /// 论文 · E5。这个效果回答的是"动作怎么在**环境**上留下痕迹"——
    /// 刀光是"武器划过空气"，溅墨是"打到东西上"，墨花是"人踩在地上"。
    /// 三者齐了，水墨动作的完整闭环才成立（E5 的三项正好对应）。
    ///
    /// 触发条件刻意不只看"是否落地"：
    ///   · 必须**有下落速度**（<see cref="minFallSpeed"/>）—— 否则走下小台阶、
    ///     甚至被 CharacterController 的贴地逻辑微调一下高度，都会溅出一朵墨花，变成"走路冒烟"。
    ///   · 加冷却（<see cref="cooldown"/>）—— 连续的小跳跃不该连着一片墨。
    ///
    /// 竖直速度是自己按位置差分算的，没有从 <see cref="PlayerController"/> 里取：
    /// 控制器对外只暴露水平速度（<c>CurrentSpeed</c>），竖直分量属于它的内部积分状态；
    /// 为一个装饰性效果去扩展控制器的公开面，不划算，而且会让"手感"与"表现"耦合。
    /// </summary>
    [DisallowMultipleComponent]
    public class InkLandingBloom : MonoBehaviour
    {
        [Tooltip("玩家控制器（留空自动取同物体）")]
        public PlayerController player;

        [Header("触发条件")]
        [Tooltip("下落速度达到多少（m/s）才算「砸」到地上")]
        public float minFallSpeed = 2.0f;
        [Tooltip("两次墨花之间的最小间隔")]
        public float cooldown = 0.35f;
        [Tooltip("冲刺急停也绽一朵（冲刺是常用动作，能让效果在录屏里被看到）")]
        public bool bloomOnDashEnd = true;

        [Header("尺寸")]
        [Tooltip("墨花半径倍率：轻落小、重落大")]
        public float radiusScale = 1f;
        public float minScale = 0.55f;
        public float maxScale = 1.6f;
        [Tooltip("多大下落速度算「重落」（用于插值尺寸）")]
        public float heavyFallSpeed = 9f;

        [Header("诊断（只读）")]
        [SerializeField] private int _bloomCount;
        [SerializeField] private int _dashBloomCount;
        [SerializeField] private int _skippedTooSoft;
        [SerializeField] private int _skippedCooldown;
        [SerializeField] private float _lastFallSpeed;
        [SerializeField] private float _lastScale = 1f;

        public int BloomCount => _bloomCount;
        public int DashBloomCount => _dashBloomCount;
        public int SkippedTooSoft => _skippedTooSoft;
        public int SkippedCooldown => _skippedCooldown;
        public float LastFallSpeed => _lastFallSpeed;
        public float LastScale => _lastScale;

        private float _lastY;
        private bool _airborne;
        private bool _wasDashing;
        private float _peakFallSpeed;
        private float _lastBloom = -99f;

        private void Awake()
        {
            if (player == null) player = GetComponent<PlayerController>();
            if (player == null) player = GetComponentInParent<PlayerController>();
            _lastY = transform.position.y;
        }

        private void Update()
        {
            if (player == null) return;

            float y = transform.position.y;
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            float vy = (y - _lastY) / dt;
            _lastY = y;

            // ---- 冲刺急停 ----
            bool dashing = player.IsDashing;
            if (bloomOnDashEnd && _wasDashing && !dashing)
                TryBloom(transform.position, 0.8f, false);
            _wasDashing = dashing;

            // ---- 落地 ----
            if (!player.IsGrounded)
            {
                _airborne = true;
                if (vy < 0f) _peakFallSpeed = Mathf.Max(_peakFallSpeed, -vy);
                return;
            }

            if (!_airborne) return;
            _airborne = false;
            _lastFallSpeed = _peakFallSpeed;
            float fall = _peakFallSpeed;
            _peakFallSpeed = 0f;

            if (fall < minFallSpeed) { _skippedTooSoft++; return; }
            float s = Mathf.Lerp(minScale, maxScale, Mathf.Clamp01(fall / Mathf.Max(1f, heavyFallSpeed)));
            TryBloom(transform.position, s, true);
        }

        private void TryBloom(Vector3 at, float scale, bool fromLanding)
        {
            if (Time.time - _lastBloom < cooldown) { _skippedCooldown++; return; }
            _lastBloom = Time.time;
            _lastScale = scale * radiusScale;

            if (fromLanding) _bloomCount++; else _dashBloomCount++;

            // 表现层不抛异常：InkHitVfx.SpawnGround 内部会处理"没有管理器/shader 找不到"的情况
            InkHitVfx.SpawnGround(new Vector3(at.x, GroundY(), at.z), _lastScale);
        }

        /// <summary>墨花必须贴在**地面**上，不能贴在玩家当前的 y 上 —— 落地瞬间玩家可能还在极短的过渡高度上。</summary>
        private float GroundY()
        {
            var cc = GetComponent<CharacterController>();
            if (cc != null && cc.isGrounded)
            {
                // CharacterController 的 center 相对 pivot，脚底 = pivot + (center.y - height/2) * scale
                float halfH = cc.height * 0.5f;
                return transform.position.y + (cc.center.y - halfH) * transform.lossyScale.y + 0.01f;
            }
            return transform.position.y + 0.01f;
        }

        public void ResetDiagnostics()
        {
            _bloomCount = 0;
            _dashBloomCount = 0;
            _skippedTooSoft = 0;
            _skippedCooldown = 0;
            _lastFallSpeed = 0f;
            _lastScale = 1f;
            _lastBloom = -99f;
            _airborne = false;
            _peakFallSpeed = 0f;
        }

        public string Describe()
        {
            return "落地墨花 " + _bloomCount + " 次（冲刺 " + _dashBloomCount + " 次）"
                 + "　因太轻跳过 " + _skippedTooSoft + "　因冷却跳过 " + _skippedCooldown
                 + "　最近下落速度 " + _lastFallSpeed.ToString("F2") + " m/s";
        }
    }
}
