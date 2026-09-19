using System.Collections.Generic;
using UnityEngine;

namespace InkWash.Effects
{
    /// <summary>
    /// 地面墨渍（第三十八轮 B4）：命中溅墨的"落到地上"那一半。
    ///
    /// 为什么需要：InkHitVfx 的墨花是"绽开→消失"的动效，地面不留痕迹 ——
    /// 打完一场房间干干净净，没有"这里发生过战斗"的记忆。墨渍补的就是这一层：
    /// 每次受击在脚下贴一小片 InkSplash 墨渍，几秒淡出。
    ///
    /// 为什么用 InkWash/InkSplash shader：程序化墨点形状、每次给不同 _Seed
    /// （形状真的不同），不用贴图也不会"看出是同一张图"。ZWrite Off 透明，
    /// 铺在地面上方 0.02m 防 z-fighting。
    ///
    /// 池上限：60 片，超了删最老的 —— 一场混战也不能让透明片无限堆积。
    /// </summary>
    [DisallowMultipleComponent]
    public class InkStain : MonoBehaviour
    {
        public static InkStain Instance { get; private set; }

        [Tooltip("同屏墨渍上限")]
        public int capacity = 60;
        [Tooltip("单片寿命（秒）")]
        public float life = 3.5f;
        [Tooltip("墨渍基础半径（米），受击强度会缩放")]
        public float baseRadius = 0.55f;

        private readonly List<Stain> _live = new List<Stain>();
        private Material _matTemplate;
        private static readonly int IdColor = Shader.PropertyToID("_Color");
        private static readonly int IdAlpha = Shader.PropertyToID("_Alpha");
        private static readonly int IdSeed = Shader.PropertyToID("_Seed");
        private static float _seedCursor;

        private class Stain
        {
            public GameObject go;
            public Material mat;
            public MeshRenderer mr;
            public float born;
            public float life;
            public float alpha0;
        }

        private void Awake()
        {
            Instance = this;
            var sh = Shader.Find("InkWash/InkSplash");
            if (sh != null)
            {
                _matTemplate = new Material(sh);
                _matTemplate.SetColor(IdColor, new Color(0.055f, 0.062f, 0.082f, 1f));
                _matTemplate.SetFloat(IdCore(), 0.5f);
            }
        }

        private static int IdCore() => Shader.PropertyToID("_CoreHold");

        private void OnDestroy() { if (Instance == this) Instance = null; }

        /// <summary>受击/开门时调用。内部吞掉一切异常与空引用（表现层不崩玩法链路）。</summary>
        public static void Spawn(Vector3 groundPos, float strength = 1f, float lifeSec = 0f)
        {
            var it = Instance;
            if (it == null || it._matTemplate == null) return;
            it.SpawnInternal(groundPos, strength, lifeSec > 0f ? lifeSec : it.life);
        }

        private void SpawnInternal(Vector3 groundPos, float strength, float lifeSec)
        {
            // 池满：删最老
            while (_live.Count >= capacity)
            {
                var oldest = _live[0];
                if (oldest.go != null) Destroy(oldest.go);
                if (oldest.mat != null) Destroy(oldest.mat);
                _live.RemoveAt(0);
            }

            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);                 // 墨渍不挡路
            go.name = "InkStain";
            go.transform.position = groundPos + Vector3.up * 0.02f;
            go.transform.rotation = Quaternion.Euler(90f, 0f, Random.value * 360f);
            float r = baseRadius * Mathf.Clamp(strength, 0.5f, 2.2f);
            go.transform.localScale = new Vector3(r * 2f, r * 2f, 1f);

            var mat = new Material(_matTemplate);
            mat.SetFloat(IdSeed, (int)(_seedCursor = (_seedCursor + 3.7f) % 32f));
            mat.SetFloat(IdAlpha, 0.8f);
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;

            _live.Add(new Stain { go = go, mat = mat, born = Time.unscaledTime, life = lifeSec, alpha0 = 0.8f });
        }

        private void Update()
        {
            float now = Time.unscaledTime;
            for (int i = _live.Count - 1; i >= 0; i--)
            {
                var s = _live[i];
                if (s.go == null) { _live.RemoveAt(i); continue; }
                float nd = (now - s.born) / Mathf.Max(0.1f, s.life);
                if (nd >= 1f)
                {
                    Destroy(s.go); Destroy(s.mat);
                    _live.RemoveAt(i);
                    continue;
                }
                // 前 60% 保持，后 40% 淡出（墨渍"干掉"而不是闪灭）
                float a = s.alpha0 * (nd < 0.6f ? 1f : 1f - (nd - 0.6f) / 0.4f);
                if (s.mat != null) s.mat.SetFloat(IdAlpha, a);
            }
        }
    }
}
