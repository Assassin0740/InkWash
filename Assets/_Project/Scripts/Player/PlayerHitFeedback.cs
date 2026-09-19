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

            if (cameraRig != null)
            {
                cameraRig.Shake(shakeAmplitude * scale, shakeDuration);
                // 被打中的 FOV 冲击（第三十二轮）：挨打也要有"画面被撞了一下"的体感
                cameraRig.FovPunch(3f * scale, 0.16f);
            }
            if (_hurtHashValid && animator != null) animator.SetTrigger(_hurtHash);

            // ★ 受击材质闪白（第三十五轮）：玩家也吃同一个水墨 shader，被打也要"白一下"。
            //   玩家的 SkinnedMeshRenderer 此前实测为 0（表现层不依赖模型的注释见类头）——
            //   Flash 在没有渲染器时是安全空转，以后接上模型自动生效，不用回头改这里。
            if (_hitFlash == null) _hitFlash = HitFlash.Ensure(gameObject);
            _hitFlash.Flash(scale);

            // ★ 受击后仰（第三十七轮）：本地动作库没有 CC 骨架的受击动画
            //   （KayKit 的 Hit_A 是 Rig_Medium 骨架，无法重定向给 Feng）——
            //   用程序化后仰代替：模型根绕 X 轴快速后仰 14° 再回弹（0.24s 正弦包络），
            //   与闪白/顿帧/震屏叠加成完整受击反应。死亡那一击不后仰（倒地动画接管）。
            if (health == null || health.Health > 0f) Recoil(scale);

            // ★ 受击音（第三十七轮）：音高随机微降，"挨了一下"更沉
            InkWash.Audio.AudioManager.Play("Hit_Flesh", 0.7f, 0.82f, 0.9f);
        }

        // ---- 受击后仰（程序化，第三十七轮）----
        private Coroutine _recoilRoutine;

        private void Recoil(float scale)
        {
            Transform t = animator != null ? animator.transform : transform;
            if (t == null) return;
            if (_recoilRoutine != null) StopCoroutine(_recoilRoutine);
            _recoilRoutine = StartCoroutine(RecoilRoutine(t, scale));
        }

        private System.Collections.IEnumerator RecoilRoutine(Transform t, float scale)
        {
            Quaternion baseRot = t.localRotation;
            const float dur = 0.24f;
            float e = 0f;
            while (e < 1f)
            {
                e = Mathf.Min(1f, e + Time.deltaTime / dur);
                float k = Mathf.Sin(e * Mathf.PI);              // 0→1→0：后仰再回弹
                t.localRotation = baseRot * Quaternion.Euler(-14f * Mathf.Clamp(scale, 0.5f, 2f) * k, 0f, 0f);
                yield return null;
            }
            t.localRotation = baseRot;
            _recoilRoutine = null;
        }

        private HitFlash _hitFlash;

        /// <summary>验收用：把计数清零。</summary>
        public void ResetDiagnostics() { HitCount = 0; }
    }
}
