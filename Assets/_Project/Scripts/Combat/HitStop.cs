using UnityEngine;

namespace InkWash.Combat
{
    /// <summary>
    /// 顿帧（hit stop）：命中瞬间把时间压慢几十毫秒，是"打击感"最廉价也最有效的一味。
    ///
    /// 为什么不用触发器/协程散在各处：顿帧是**全局**状态（Time.timeScale），
    /// 两处同时请求时必须取"更长的那个"而不是互相覆盖，否则连段命中会越打越短。
    /// 统一走这里，顺带给出可读诊断（RequestCount / ActiveTime），让验收能断言"确实顿帧了"。
    /// </summary>
    public static class HitStop
    {
        /// <summary>总开关。自动化验收里关掉，避免时间缩放干扰度量。</summary>
        public static bool Enabled = true;

        private static HitStopRunner _runner;

        public static void Request(float duration, float scale = 0.08f)
        {
            if (!Enabled || duration <= 0f) return;
            if (_runner == null)
            {
                var go = new GameObject("[HitStopRunner]");
                Object.DontDestroyOnLoad(go);
                _runner = go.AddComponent<HitStopRunner>();
            }
            _runner.Request(duration, scale);
        }

        public static bool IsActive => _runner != null && _runner.IsActive;
        public static int RequestCount => _runner != null ? _runner.Count : 0;

        public static void ResetDiagnostics()
        {
            if (_runner != null) _runner.ResetCount();
        }
    }

    public class HitStopRunner : MonoBehaviour
    {
        private float _timer;
        private float _scale = 0.08f;
        private float _savedTimeScale = 1f;
        private float _savedFixedDelta = 0.02f;

        public bool IsActive { get; private set; }
        public int Count { get; private set; }

        public void ResetCount() { Count = 0; }

        public void Request(float duration, float scale)
        {
            Count++;
            if (!IsActive)
            {
                _savedTimeScale = Time.timeScale;
                _savedFixedDelta = Time.fixedDeltaTime;
                IsActive = true;
            }
            _scale = Mathf.Clamp(scale, 0.02f, 1f);
            _timer = Mathf.Max(_timer, duration);   // 取更长的那个，不互相覆盖
            Time.timeScale = _scale;
            Time.fixedDeltaTime = _savedFixedDelta * _scale;
        }

        // 注意：必须用 unscaledDeltaTime —— 用 scaled 的话时间越慢、倒计时也越慢，会永远停不下来
        private void Update()
        {
            if (!IsActive) return;
            _timer -= Time.unscaledDeltaTime;
            if (_timer <= 0f) Restore();
        }

        private void Restore()
        {
            Time.timeScale = _savedTimeScale;
            Time.fixedDeltaTime = _savedFixedDelta;
            IsActive = false;
            _timer = 0f;
        }

        private void OnDisable() { if (IsActive) Restore(); }
        private void OnDestroy() { if (IsActive) Restore(); }
    }
}
