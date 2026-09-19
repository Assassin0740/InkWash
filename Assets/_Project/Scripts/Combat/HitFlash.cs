using UnityEngine;

namespace InkWash.Combat
{
    /// <summary>
    /// 受击材质闪白：把 <see cref="InkWash"/> 水墨 shader 的 <c>_HitFlash</c> 顶到 1，
    /// 再按每秒衰减率收回 0 —— 整个人"白一下"，是打击感的材质层。
    ///
    /// 为什么走 <see cref="MaterialPropertyBlock"/> 而不是改材质：
    ///   全场敌人共用**同一份**水墨材质（InkMaterialForcer 统一派发），实例化材质
    ///   会制造一批 HideAndDontSave 的幽灵资产（且打破共享）；MPB 是**逐渲染器**
    ///   的着色器属性覆盖，不碰共享资产、随渲染器销毁自动回收 —— 是 Unity 官方
    ///   为"同一材质、不同参数"准备的通道。
    ///
    /// 为什么是组件而不是 EnemyBase 内嵌：
    ///   玩家也吃同一个 shader（PlayerHitFeedback 也要闪），龙是程序化驱动没有
    ///   Animator 受击片段 —— 闪白是**不依赖动作系统**的通用受击表现，做成独立
    ///   组件谁都能挂。EnemyBase / PlayerHitFeedback 在自己的受击路径上懒创建。
    /// </summary>
    public class HitFlash : MonoBehaviour
    {
        [Tooltip("闪白衰减速率（每秒）：1/该值 ≈ 可见时长。5 ⇒ 约 0.2 s，与顿帧 0.085 s + 硬直首段对齐")]
        public float decayPerSec = 5f;

        [Tooltip("默认闪白强度（0..1，1=完全纸白）")]
        public float strength = 1f;

        static readonly int HashHitFlash = Shader.PropertyToID("_HitFlash");

        Renderer[] _renderers;
        MaterialPropertyBlock _mpb;
        float _flash;

        void Awake()
        {
            Cache();
            _mpb = new MaterialPropertyBlock();
        }

        void Cache()
        {
            if (_renderers == null || _renderers.Length == 0)
                _renderers = GetComponentsInChildren<Renderer>(true);
        }

        /// <summary>触发一次闪白（重复调用取更大值，不叠加 —— 连击不该闪成频闪灯）。</summary>
        public void Flash(float scale = 1f)
        {
            _flash = Mathf.Max(_flash, Mathf.Clamp01(strength * scale));
            Cache();    // 懒创建的组件可能晚于子渲染器生成（龙的渲染器在骨骼下）
        }

        void Update()
        {
            if (_flash <= 0f) return;

            // ★ timeScale=0（身陨暂停/选卡）时 Update 仍会跑但 deltaTime=0 ——
            //   闪白就冻在半空。改用 unscaled：受击表现是"物理时间"的演出，
            //   不该跟着玩法时间流一起停。
            _flash = Mathf.Max(0f, _flash - decayPerSec * Time.unscaledDeltaTime);

            if (_mpb == null) _mpb = new MaterialPropertyBlock();
            for (int i = 0; i < _renderers.Length; i++)
            {
                var r = _renderers[i];
                if (r == null) continue;    // 死亡溶解可能拆走子渲染器
                r.GetPropertyBlock(_mpb);
                _mpb.SetFloat(HashHitFlash, _flash);
                r.SetPropertyBlock(_mpb);
            }

            // 收干净后把属性清零一次 —— 否则最后一次的残留值会永远留在 MPB 里
            if (_flash <= 0f)
            {
                for (int i = 0; i < _renderers.Length; i++)
                {
                    var r = _renderers[i];
                    if (r == null) continue;
                    r.GetPropertyBlock(_mpb);
                    _mpb.SetFloat(HashHitFlash, 0f);
                    r.SetPropertyBlock(_mpb);
                }
            }
        }

        /// <summary>拿不到就挂一个（幂等）。供受击路径懒创建。</summary>
        public static HitFlash Ensure(GameObject go)
        {
            if (go == null) return null;
            var f = go.GetComponent<HitFlash>();
            if (f == null) f = go.AddComponent<HitFlash>();
            return f;
        }
    }
}
