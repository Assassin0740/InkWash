using UnityEngine;

namespace InkWash.CameraRig
{
    /// <summary>
    /// 第三人称跟随相机（自研），风格对标《绝区零》一类的高速动作游戏。
    ///
    /// 为什么不用 Cinemachine 现成组件：
    ///   项目初期用 Cinemachine 的 Transposer(WorldSpace) + Composer 搭了"跟位置不跟朝向"的相机，
    ///   目的是回避"相机跟着角色转 → 角色又按相机朝向走"的正反馈打转。但代价是相机被钉在世界坐标上，
    ///   要做近距离、可自由环绕、带速度感 FOV 的镜头，就得同时改偏移、改朝向、加后处理，
    ///   在 Cinemachine 的组件模型里反而更绕。这里改成自研：球坐标环绕 + 阻尼跟随，
    ///   控制权完全在手，也便于把算法写进论文。场景里的 Cinemachine 对象保留但已停用，随时可回退。
    ///
    /// 三个关键设计：
    /// 1. **鼠标控制偏航/俯仰**，而不是让相机跟着角色转。
    ///    这是回避正反馈的正确解法：输入参考系由玩家掌握，角色转向不会反过来带动相机。
    ///    若改成"相机自动对齐到移动方向 + 输入按相机解算"，按住平移键角色就会绕圈。
    /// 2. **跟随阻尼带提前量补偿**：阻尼会让镜头滞后 v*T，于是再加 v*T 的提前量。
    ///    稳态下两者抵消 —— 角色严格钉在画面中心，而起步/急停时仍有柔和的拖拽感。
    ///    只加阻尼不加提前量，跑起来角色会被甩到屏幕边缘；只加提前量不加阻尼，急停会硬切。
    /// 3. **FOV 随速度变化**：速度越快视野越广，制造速度感。由目标速度自动驱动，无需外部接线。
    /// </summary>
    [DisallowMultipleComponent]
    public class ThirdPersonCamera : MonoBehaviour
    {
        [Header("目标")]
        [Tooltip("跟随目标，留空则自动找场景里的 Player")]
        public Transform target;

        [Tooltip("环绕枢轴相对目标原点的偏移（一般取胸口/头部高度）")]
        public Vector3 pivotOffset = new Vector3(0f, 1.42f, 0f);

        [Header("轨道")]
        [Tooltip("相机到枢轴的距离（米）")]
        public float distance = 4.3f;

        [Tooltip("当前偏航角（度）。0 = 站在角色背后朝前看")]
        public float yaw = 0f;

        [Tooltip("当前俯仰角（度）。正值表示相机在目标上方往下看")]
        public float pitch = 14f;

        public float pitchMin = -22f;
        public float pitchMax = 55f;

        [Header("鼠标输入")]
        public bool mouseLookEnabled = true;
        public float yawSensitivity = 3.2f;
        public float pitchSensitivity = 2.0f;

        [Tooltip("按下此键时锁定鼠标（游戏内常态）。Esc 解锁，左键重新锁定")]
        public bool lockCursorOnStart = true;

        [Header("跟随（提前量补偿阻尼滞后）")]
        [Tooltip("位置阻尼时间常数。越大越柔，但瞬态拖拽越明显")]
        public float followDamp = 0.10f;

        [Tooltip("提前量系数。等于 followDamp 时，稳态下角色严格居中")]
        public float leadCompensation = 0.10f;

        [Header("动态 FOV（速度感）")]
        public float baseFov = 48f;

        [Tooltip("速度达到 fovSpeedMax 时额外增加的视野角度")]
        public float fovSpeedGain = 8f;

        public float fovSpeedMax = 12f;
        public float fovDamp = 0.15f;

        [Header("避障")]
        [Tooltip("挡在相机前的层。留空则不做避障")]
        public LayerMask collisionMask;

        public float collisionRadius = 0.25f;
        public float minDistance = 1.2f;

        [Tooltip("被遮挡时收镜的距离阻尼")]
        public float collisionDamp = 0.05f;

        // ---------------- 内部状态 ----------------
        private Vector3 _pivot;              // 平滑后的枢轴
        private Vector3 _pivotVel;
        private Vector3 _targetVel;          // 目标速度（平滑后，用于提前量与 FOV）
        private Vector3 _lastTargetPos;
        private bool _hasLastPos;
        private float _fov;
        private float _fovVel;
        private float _curDistance;
        private float _shakeTimer;
        private float _shakeDuration;
        private float _shakeAmplitude;

        /// <summary>当前实际距离（含避障收镜），供调试与验收使用。</summary>
        public float CurrentDistance => _curDistance;

        /// <summary>平滑后的目标速度大小，供调试与验收使用。</summary>
        public float TargetSpeed => _targetVel.magnitude;

        /// <summary>震屏是否正在进行（供自动化验收断言"打击反馈确实触发了"）。</summary>
        public bool IsShaking => _shakeTimer > 0f;

        /// <summary>当前视角偏航（度），供验收读取。</summary>
        public float Yaw => yaw;

        /// <summary>当前视角俯仰（度），供验收读取。</summary>
        public float Pitch => pitch;

        private void Awake()
        {
            _fov = baseFov;
            _curDistance = distance;

            if (target == null)
            {
                var go = GameObject.Find("Player");
                if (go != null) target = go.transform;
            }
        }

        private void OnEnable()
        {
            if (target != null)
            {
                _lastTargetPos = target.position;
                _hasLastPos = true;
            }
            _pivot = ComputeAimPoint();
            if (lockCursorOnStart) LockCursor(true);
        }

        private void OnDisable()
        {
            if (lockCursorOnStart) LockCursor(false);
        }

        private void Update()
        {
            HandleCursor();
            if (!mouseLookEnabled) return;

            // 鼠标轴本身就是帧间增量，不再乘 deltaTime
            yaw += Input.GetAxis("Mouse X") * yawSensitivity;
            pitch -= Input.GetAxis("Mouse Y") * pitchSensitivity;
            pitch = Mathf.Clamp(pitch, pitchMin, pitchMax);

            // 键盘兜底：没鼠标时也能转视角（也方便调试）
            if (Input.GetKey(KeyCode.Q)) yaw -= 90f * Time.deltaTime;
            if (Input.GetKey(KeyCode.E)) yaw += 90f * Time.deltaTime;
        }

        private void LateUpdate()
        {
            if (target == null) return;

            float dt = Time.deltaTime;

            // ---- 目标速度（平滑，供提前量与 FOV 使用） ----
            if (_hasLastPos && dt > 1e-5f)
            {
                Vector3 raw = (target.position - _lastTargetPos) / dt;
                _targetVel = Vector3.Lerp(_targetVel, raw, 1f - Mathf.Exp(-dt / 0.05f));
            }
            _lastTargetPos = target.position;
            _hasLastPos = true;

            // ---- 枢轴：带提前量的阻尼跟随 ----
            Vector3 aimPoint = ComputeAimPoint();
            Vector3 planarLead = new Vector3(_targetVel.x, 0f, _targetVel.z) * leadCompensation;
            Vector3 desiredPivot = aimPoint + planarLead;
            _pivot = Vector3.SmoothDamp(_pivot, desiredPivot, ref _pivotVel, followDamp, Mathf.Infinity, dt);

            // ---- 球坐标求相机位姿 ----
            Quaternion orbit = Quaternion.Euler(pitch, yaw, 0f);
            Vector3 back = orbit * Vector3.back;

            float wantDistance = ResolveDistance(aimPoint, back, dt);

            Vector3 desiredPos = _pivot + back * wantDistance;
            transform.position = desiredPos;
            transform.rotation = orbit;

            // ---- 震屏 ----
            // ★ 两个坑都在这一小段里，改之前先读完：
            //
            //  坑 1（用户实测到的那条）：**递减必须用 unscaledDeltaTime**。
            //    升级奖励界面会把 `Time.timeScale` 设成 0，于是 `Time.deltaTime == 0`。
            //    用 scaled 递减的话 `_shakeTimer` 永远减不到 0 ——
            //    表现就是"在攻击期间弹出技能选择后，摄像机一直震个不停"。
            //    注意震动是**直接写 transform**（不走 SmoothDamp），所以即使 dt=0 也照样每帧抖。
            //
            //  坑 2：暂停时画面上任何东西都不该动。哪怕用 unscaled 让它自然衰减，
            //    玩家在选技能的那半秒里仍会看到画面在抖 —— 那是在"UI 上震"。
            //    所以 timeScale <= 0 时直接把震动结清（不是暂停，是结束）。
            if (Time.timeScale <= 0f)
            {
                _shakeTimer = 0f;
                _shakeAmplitude = 0f;
            }
            else if (_shakeTimer > 0f)
            {
                _shakeTimer -= Mathf.Max(Time.unscaledDeltaTime, 1e-5f);
                float falloff = _shakeDuration > 0f ? Mathf.Clamp01(_shakeTimer / _shakeDuration) : 0f;
                float amp = _shakeAmplitude * falloff * falloff;
                transform.position += Random.insideUnitSphere * amp;
                transform.rotation = orbit * Quaternion.Euler(
                    Random.Range(-amp, amp) * 60f,
                    Random.Range(-amp, amp) * 60f,
                    Random.Range(-amp, amp) * 60f);
            }

            // ---- 动态 FOV ----
            float speed01 = Mathf.Clamp01(_targetVel.magnitude / Mathf.Max(fovSpeedMax, 0.01f));
            float wantFov = baseFov + fovSpeedGain * speed01;
            _fov = Mathf.SmoothDamp(_fov, wantFov, ref _fovVel, fovDamp, Mathf.Infinity, dt);

            var cam = GetComponent<Camera>();
            if (cam != null) cam.fieldOfView = _fov;
        }

        private Vector3 ComputeAimPoint()
        {
            return target != null ? target.position + pivotOffset : transform.position;
        }

        /// <summary>距离：先按避障收镜，再平滑，避免贴墙时相机穿墙。</summary>
        private float ResolveDistance(Vector3 pivot, Vector3 back, float dt)
        {
            float want = distance;

            if (collisionMask.value != 0)
            {
                RaycastHit hit;
                if (Physics.SphereCast(pivot, collisionRadius, back, out hit, distance,
                        collisionMask, QueryTriggerInteraction.Ignore))
                    want = Mathf.Clamp(hit.distance - collisionRadius * 0.5f, minDistance, distance);
            }

            // 收镜要快（避免穿墙），放镜要慢（避免顿挫）
            float damp = want < _curDistance ? collisionDamp : 0.25f;
            _curDistance = Mathf.Lerp(_curDistance, want, 1f - Mathf.Exp(-dt / Mathf.Max(damp, 1e-4f)));
            return _curDistance;
        }

        // ------------------------------------------------------------------
        // 对外接口
        // ------------------------------------------------------------------

        /// <summary>震屏（供打击反馈调用）。</summary>
        public void Shake(float amplitude, float duration)
        {
            // 已有更强的震动时不打断
            if (amplitude < _shakeAmplitude && _shakeTimer > 0f) return;
            _shakeAmplitude = amplitude;
            _shakeDuration = Mathf.Max(duration, 1e-4f);
            _shakeTimer = _shakeDuration;
        }

        /// <summary>
        /// 立刻结束震屏（幅度一并清零）。
        ///
        /// 进奖励 / 结算这类**接管 timeScale** 的场合应当显式调用。
        /// 虽然 <see cref="LateUpdate"/> 里已经有 `timeScale &lt;= 0 → 结清` 的兜底，
        /// 但那是"靠全局状态兜"，而 <see cref="HitStop"/> 与奖励暂停都会短暂地在 0 与非 0 之间切换；
        /// 主动调用一次，语义就是明确的"这一局的表现已经结束了"。<br/>
        /// 幅度必须一起清零：只清 timer 的话，残留的大幅度会把下一个较弱的小震动挡掉
        /// （见 <see cref="Shake"/> 第一行的"更强的不打断"判据）。
        /// </summary>
        public void StopShake()
        {
            _shakeTimer = 0f;
            _shakeAmplitude = 0f;
        }

        /// <summary>把相机瞬移到目标背后（切换视角 / 重生时用）。</summary>
        public void SnapBehindTarget()
        {
            if (target == null) return;
            _pivot = ComputeAimPoint();
            _pivotVel = Vector3.zero;
            _targetVel = Vector3.zero;
        }

        /// <summary>忽略真实鼠标（自动化验收用）。</summary>
        public void SetMouseLookEnabled(bool on)
        {
            mouseLookEnabled = on;
        }

        private void HandleCursor()
        {
            if (!lockCursorOnStart) return;

            if (Input.GetKeyDown(KeyCode.Escape)) LockCursor(false);
            else if (Input.GetMouseButtonDown(0) && Cursor.lockState != CursorLockMode.Locked) LockCursor(true);
        }

        private static void LockCursor(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

        private void OnDrawGizmosSelected()
        {
            if (target == null) return;
            Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.9f);
            Gizmos.DrawWireSphere(ComputeAimPoint(), 0.12f);
            Gizmos.DrawLine(ComputeAimPoint(), transform.position);
        }
    }
}
