using InkWash.CameraRig;
using InkWash.Combat;
using UnityEngine;

namespace InkWash.Player
{
    /// <summary>
    /// 主角受击的**表现层**：订阅 <see cref="PlayerHealth.Damaged"/>，驱动震屏（以及可选的 Animator 受击触发器）。
    ///
    /// ★ 为什么单独一个组件：<see cref="PlayerHealth"/> 的注释写明它刻意只管"生命数值"，
    ///   表现层一律走事件订阅。而 <c>Damaged</c> 事件此前是**零订阅者** ——
    ///   玩家挨打只有溅墨和顿帧，没有"被打到了"的体感。
    ///
    /// ★ 为什么这里不依赖模型：本工程主角可能还没接上蒙皮网格 / Avatar
    ///   （实测 SkinnedMeshRenderer=0、Animator.avatar=null），任何"骨骼/动画"式的
    ///   受击动作都无从驱动。震屏走的是相机，与模型无关 ⇒ 现在就能落地、能验收。
    ///   Animator 触发器做成**可选**：只有在控制器里真的存在该触发器时才 SetTrigger，
    ///   否则静默跳过（避免"参数不存在"刷警告，也避免以后加了动画还要改代码）。
    /// </summary>
    public class PlayerHitFeedback : MonoBehaviour
    {
        [Tooltip("受击时抛事件的生命组件（留空自动取同物体 / 父级）")]
        public PlayerHealth health;

        [Tooltip("相机（震屏）。留空自动取场景里的 ThirdPersonCamera。")]
        public ThirdPersonCamera cameraRig;

        [Header("动画（可选）")]
        [Tooltip("玩家 Animator。留空自动取子级。")]
        public Animator animator;
        [Tooltip("受击触发器名。**只有当控制器里真的存在这个 Trigger 时才会触发**，" +
                 "不存在则静默跳过（现在 Player.controller 里没有，等加了受击片段才会生效）。")]
        public string hurtTrigger = "Hurt";

        [Header("震屏")]
        [Tooltip("基础震屏幅度（度级，与 ThirdPersonCamera.Shake 同单位）")]
        public float shakeAmplitude = 0.12f;
        public float shakeDuration = 0.18f;
        [Tooltip("震屏强度随伤害缩放的参考伤害值；缩放结果被 clamp 到 [0.5, damageScaleMax]")]
        public float damageRef = 20f;
        public float damageScaleMax = 2f;

        /// <summary>已响应过的受击次数（供自动化验收断言"订阅者确实接到了"）。</summary>
        public int HitCount { get; private set; }

        private int _hurtHash;
        private bool _hurtHashValid;

        private void Awake() { Resolve(); }

        private void OnEnable()
        {
            Resolve();
            if (health != null) health.Damaged += OnDamaged;
        }

        private void OnDisable()
        {
            if (health != null) health.Damaged -= OnDamaged;
        }

        private void Resolve()
        {
            if (health == null) health = GetComponent<PlayerHealth>();
            if (health == null) health = GetComponentInParent<PlayerHealth>();
            if (cameraRig == null) cameraRig = FindObjectOfType<ThirdPersonCamera>();
            if (animator == null && health != null) animator = health.GetComponentInChildren<Animator>();

            // 只在控制器真实存在该 Trigger 时才启用 —— 否则 SetTrigger 会刷 "parameter does not exist"
            _hurtHashValid = false;
            if (animator == null || animator.runtimeAnimatorController == null) return;
            if (string.IsNullOrEmpty(hurtTrigger)) return;
            foreach (var p in animator.parameters)
            {
                if (p.type == AnimatorControllerParameterType.Trigger && p.name == hurtTrigger)
                {
                    _hurtHash = Animator.StringToHash(hurtTrigger);
                    _hurtHashValid = true;
                    break;
                }
            }
        }

        private void OnDamaged(DamageInfo info)
        {
            HitCount++;

            float scale = 1f;
            if (damageRef > 0f) scale = Mathf.Clamp(info.amount / damageRef, 0.5f, damageScaleMax);

            if (cameraRig != null) cameraRig.Shake(shakeAmplitude * scale, shakeDuration);
            if (_hurtHashValid && animator != null) animator.SetTrigger(_hurtHash);
        }

        /// <summary>验收用：把计数清零。</summary>
        public void ResetDiagnostics() { HitCount = 0; }
    }
}
