using UnityEngine;
using InkWash.Player;

namespace InkWash.Effects
{
    /// <summary>
    /// 冲刺墨迹拖尾（第三十八轮 B1）。
    ///
    /// 为什么用 TrailRenderer 而不是粒子：拖尾要的是"身体划过空气留下一笔"——
    /// 是**连续的线**，不是一堆点；TrailRenderer 的宽度曲线（腰粗尾细）正好是
    /// 一笔淡墨的形状。移动时的其他时间不 emit，否则走路全程拖墨就成蜗牛了。
    ///
    /// 为什么运行时挂（RunManager.Awake AddComponent）：Player.prefab 不必为此
    /// 开一次"prefab 门禁双写"流程 —— 表现层组件、无序列化引用，代码装配即走。
    /// </summary>
    [DisallowMultipleComponent]
    public class DashInkTrail : MonoBehaviour
    {
        [Tooltip("拖尾寿命（秒）——淡墨 0.4s 左右刚好跟得上冲刺残影的视认窗口")]
        public float trailTime = 0.4f;
        [Tooltip("拖尾最宽处（米）")]
        public float width = 0.26f;

        private TrailRenderer _trail;
        private PlayerController _pc;

        private void Awake()
        {
            _pc = GetComponent<PlayerController>();
            if (_pc == null) _pc = GetComponentInParent<PlayerController>();

            var go = new GameObject("DashTrail");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, 0.9f, 0f);   // 腰部，跟衣摆高度走

            _trail = go.AddComponent<TrailRenderer>();
            _trail.time = trailTime;
            _trail.numCapVertices = 2;
            _trail.numCornerVertices = 2;
            _trail.minVertexDistance = 0.08f;
            _trail.alignment = LineAlignment.View;
            _trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _trail.receiveShadows = false;
            _trail.emitting = false;

            var w = new AnimationCurve();
            w.AddKey(0f, width);
            w.AddKey(1f, 0.02f);
            _trail.widthCurve = w;

            // 淡墨：起点半透明深灰墨，末端完全消失
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(new Color(0.10f, 0.11f, 0.14f), 0f),
                        new GradientColorKey(new Color(0.22f, 0.23f, 0.27f), 1f) },
                new[] { new GradientAlphaKey(0.5f, 0f), new GradientAlphaKey(0f, 1f) });
            _trail.colorGradient = grad;

            var sh = Shader.Find("Sprites/Default");
            if (sh != null) _trail.material = new Material(sh);
        }

        private void Update()
        {
            if (_trail == null) return;
            // 只在冲刺那一段出墨；急停后残留的一小段由 trailTime 自然淡掉
            bool on = _pc != null && _pc.IsDashing;
            if (_trail.emitting != on) _trail.emitting = on;
        }
    }
}
