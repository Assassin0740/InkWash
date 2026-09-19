using System;
using UnityEngine;
using InkWash.Combat;
using InkWash.Effects;

namespace InkWash.Player
{
    /// <summary>
    /// 墨爆 —— 主角第一个主动技能（用户反馈「主角只能平A，很枯燥」）。
    ///
    /// 设计定位：**冷却制 AOE 爆发**，与三段连击（近身单体）+ 重击（K）+ 冲刺（Space/RMB）
    /// 互补 —— 被围时按 R 一次性掀开。不做资源条：这局的核心循环是"清房 → 升级三选一"，
    /// 冷却已经足够形成"什么时候放"的决策点，再加蓝条只会多一个要盯的 UI。
    ///
    /// 三个刻意的取舍：
    /// 1) **无施法动画**：程序化水墨风格的动作集是外购重定向的，没有"施法"片段；
    ///    瞬发 + 大幅水墨表现（墨花三环 + 溅墨 + 震屏 + FOV 冲击 + 顿帧）足以撑住打击感，
    ///    比硬套一个挥剑动画更像"技能"。
    /// 2) **伤害走既有 DamageInfo/TakeDamage 通道**：击退、硬直、顿帧、吸血（Lifesteal 按
    ///    命中数回血）全部免费继承，墨爆命中也会给玩家回血 —— 与"近战吸血"的自洽语义。
    /// 3) **VFX 走 InkHitVfx 静态入口**（项目纪律：受击方不认识特效实现，池全场共用）。
    /// </summary>
    [DisallowMultipleComponent]
    public class PlayerActiveSkill : MonoBehaviour
    {
        [Header("输入")]
        [Tooltip("施放键。Q/E 已被相机占用、K=重击、Space/RMB=冲刺、LMB/J=连击，R 空闲")]
        public KeyCode castKey = KeyCode.R;

        [Header("数值")]
        [Tooltip("冷却（秒）。8s ≈ 清一小波怪的时间，'每间房至少能放一次'的节奏")]
        public float cooldown = 8f;
        [Tooltip("伤害半径（米）。以玩家脚下为圆心，只比水平面距离；竖直方向另设 3m 容差（挡飞天龙俯冲下来吃不到的尴尬）")]
        public float radius = 5f;
        public float damage = 40f;
        [Tooltip("径向击退速度（m/s）。掀开而非击飞 —— 太大会把怪推出 NavMesh")]
        public float knockback = 8f;
        public float hitStun = 0.6f;
        [Tooltip("命中顿帧（秒）。范围技的顿帧要比单砍(0.085)深一点才压得住画面的爆发量")]
        public float hitStopDuration = 0.12f;

        [Header("表现")]
        public float shakeAmplitude = 0.22f;
        public float shakeDuration = 0.32f;
        [Tooltip("FOV 冲击（度）。正数 = 镜头被'推'出去，配合范围爆发的冲击感")]
        public float fovPunchDeg = 6f;
        [Tooltip("墨花环数（从内到外）")]
        public int bloomRings = 3;
        [Tooltip("每环墨花数")]
        public int bloomsPerRing = 10;

        [Header("诊断（只读）")]
        [SerializeField] private float _cooldownLeft;
        [SerializeField] private int _castCount;
        [SerializeField] private int _lastHitCount;

        public float CooldownLeft => _cooldownLeft;
        public float CooldownRatio => cooldown > 0f ? Mathf.Clamp01(_cooldownLeft / cooldown) : 0f;
        public bool IsReady => _cooldownLeft <= 0f;
        public int CastCount => _castCount;
        public int LastHitCount => _lastHitCount;
        public float Radius => radius;

        /// <summary>施放成功时抛（命中数）。验收与 HUD 用。</summary>
        public event Action<int> Cast;

        private InkWash.CameraRig.ThirdPersonCamera _cam;

        private void Update()
        {
            if (_cooldownLeft > 0f)
                _cooldownLeft = Mathf.Max(0f, _cooldownLeft - Time.deltaTime);

            if (Time.timeScale <= 0f) return;               // 选卡/结算冻结（Update 本就不走，双保险）
            if (!Input.GetKeyDown(castKey)) return;

            // 主菜单/过门前不响应（防误触）
            var rm = InkWash.Roguelike.RunManager.Instance;
            if (rm != null && rm.State != InkWash.Roguelike.RunState.Playing) return;

            TryCast();
        }

        /// <summary>施放（也供验收脚本直调）。冷却中返回 false。</summary>
        public bool TryCast()
        {
            if (_cooldownLeft > 0f) return false;
            _cooldownLeft = cooldown;
            _castCount++;
            _lastHitCount = 0;
            // ★ 施放音（第三十七轮）：重挥起手 + 低音高，"蓄力爆发"的第一声
            InkWash.Audio.AudioManager.Play("Swing_Heavy", 1f, 0.85f, 0.9f);

            Vector3 c = transform.position + Vector3.up * 0.1f;

            // ---- 伤害：半径内所有活敌 ----
            foreach (var e in UnityEngine.Object.FindObjectsOfType<Enemies.EnemyBase>())
            {
                if (e == null || !e.IsAlive) continue;
                Vector3 ep = e.Transform.position;
                Vector3 flat = ep - c; flat.y = 0f;
                if (flat.magnitude > radius || Mathf.Abs(ep.y - c.y) > 3f) continue;

                Vector3 dir = flat.sqrMagnitude > 1e-4f ? flat.normalized : transform.forward;
                var info = new DamageInfo
                {
                    amount = damage,
                    sourceFaction = Faction.Player,
                    hitDirection = dir,
                    knockback = knockback,
                    hitStun = hitStun,
                    hitStop = hitStopDuration,
                };
                if (e.TakeDamage(info))
                {
                    _lastHitCount++;
                    InkHitVfx.Spawn(ep + Vector3.up * 0.8f, dir, 1.4f);
                }
            }

            // ---- 墨花三环：由内向外绽开（外环最大最淡），黄金角散点避免机械感 ----
            int n = Mathf.Max(1, bloomsPerRing);
            for (int ring = 1; ring <= Mathf.Max(1, bloomRings); ring++)
            {
                float t = (float)ring / Mathf.Max(1, bloomRings);
                float r = radius * t;
                float scale = Mathf.Lerp(1.2f, 0.45f, t);     // 内环大而实，外环小而淡
                for (int i = 0; i < n; i++)
                {
                    float a = i * 137.5f * Mathf.Deg2Rad + ring * 0.7f;
                    Vector3 p = c + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * r;
                    InkHitVfx.SpawnGround(p, scale);
                }
            }

            // ---- 镜头反馈 ----
            if (_cam == null) _cam = UnityEngine.Object.FindObjectOfType<InkWash.CameraRig.ThirdPersonCamera>();
            if (_cam != null)
            {
                _cam.Shake(shakeAmplitude, shakeDuration);
                _cam.FovPunch(fovPunchDeg, 0.2f);
            }

            try { Cast?.Invoke(_lastHitCount); }
            catch (Exception ex) { Debug.LogError("[PlayerActiveSkill] Cast 订阅者抛异常（已隔离）：" + ex); }
            return true;
        }
    }
}
