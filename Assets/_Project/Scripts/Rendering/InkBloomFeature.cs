using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace InkWash.Rendering
{
    /// <summary>
    /// 墨晕扩散 RendererFeature（论文 E4）。机理与 Bloom 相反：**晕的是暗部、结果是压暗**，
    /// 详见 <c>InkBloom.shader</c> 的头注。
    ///
    /// 为什么单开一个 Feature 而不做进 E2/E3：
    ///   墨线依赖深度+法线、宣纸依赖时间（纸不动），墨晕只依赖颜色 —— 三者的输入、代价、
    ///   艺术意图都不同。合成一个 Pass 会导致"只想关掉墨晕"时连墨线一起没了，
    ///   论文要的四阶段对比图就没法拍。拆开后每一层都能独立开关，
    ///   这正是 §9.14「四层逐层像素差」那组数据成立的前提。
    ///
    /// 执行顺序：AfterRenderingTransparents + 2（必须排在墨线与宣纸之后，理由见 InkStyleRegistry）。
    /// </summary>
    public class InkBloomFeature : ScriptableRendererFeature
    {
        [System.Serializable]
        public class Settings
        {
            public bool enabled = true;
            public Color inkColor = new Color(0.10f, 0.11f, 0.14f, 1f);
            /// <summary>暗度阈值：只有比这更暗的像素参与扩散。0.35 意味着 luma &lt; 0.65。</summary>
            [Range(0f, 1f)] public float threshold = 0.35f;
            [Range(0f, 2f)] public float strength = 0.55f;
            [Range(0.5f, 24f)] public float radius1 = 3f;
            [Range(1f, 64f)] public float radius2 = 9f;
            [Range(0f, 1f)] public float farWeight = 0.55f;
            [Range(0f, 0.6f)] public float jitter = 0.12f;
            [Range(0f, 1f)] public float tintAmount = 0.35f;
            [Range(0f, 1f)] public float veil = 0f;

            /// <summary>全量浅拷贝。与 <see cref="InkEdgeFeature.Settings.Clone"/> 同一约定：
            /// 拷贝必须和字段待在同一个类里，否则加字段必漏。验收会用反射逐字段比对。</summary>
            public Settings Clone()
            {
                return new Settings
                {
                    enabled = enabled,
                    inkColor = inkColor,
                    threshold = threshold,
                    strength = strength,
                    radius1 = radius1,
                    radius2 = radius2,
                    farWeight = farWeight,
                    jitter = jitter,
                    tintAmount = tintAmount,
                    veil = veil,
                };
            }
        }

        public Settings settings = new Settings();
        /// <summary>总开关（资产上的默认值；运行时可用注册表覆盖）。</summary>
        public bool bloomOn = true;

        private static readonly int IdInkColor = Shader.PropertyToID("_InkColor");
        private static readonly int IdThreshold = Shader.PropertyToID("_Threshold");
        private static readonly int IdStrength = Shader.PropertyToID("_Strength");
        private static readonly int IdRadius1 = Shader.PropertyToID("_Radius1");
        private static readonly int IdRadius2 = Shader.PropertyToID("_Radius2");
        private static readonly int IdFarWeight = Shader.PropertyToID("_FarWeight");
        private static readonly int IdJitter = Shader.PropertyToID("_Jitter");
        private static readonly int IdTintAmount = Shader.PropertyToID("_TintAmount");
        private static readonly int IdVeil = Shader.PropertyToID("_Veil");
        private static readonly int IdTexel = Shader.PropertyToID("_InkTexelSize");

        private InkFullScreenPass _pass;
        private Material _material;

        public override void Create()
        {
            InkStyleRegistry.Bloom = this;
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (!settings.enabled || !InkStyleRegistry.BloomEnabled) return;
            var type = renderingData.cameraData.cameraType;
            if (type != CameraType.Game && type != CameraType.SceneView) return;
            if (!EnsureMaterial()) return;

            // +2：排在墨线（+0）与宣纸（+1）之后
            var evt = RenderPassEvent.AfterRenderingTransparents + 2;
            if (_pass == null)
            {
                _pass = new InkFullScreenPass("InkWash.InkBloom", _material, PushParams,
                    ScriptableRenderPassInput.None, evt);
            }
            _pass.renderPassEvent = evt;
            renderer.EnqueuePass(_pass);
        }

        private void PushParams(Material m)
        {
            var s = InkStyleRegistry.BloomSettings;
            if (s == null) return;
            m.SetColor(IdInkColor, s.inkColor);
            m.SetFloat(IdThreshold, s.threshold);
            m.SetFloat(IdStrength, s.strength);
            m.SetFloat(IdRadius1, s.radius1);
            m.SetFloat(IdRadius2, s.radius2);
            m.SetFloat(IdFarWeight, s.farWeight);
            m.SetFloat(IdJitter, s.jitter);
            m.SetFloat(IdTintAmount, s.tintAmount);
            m.SetFloat(IdVeil, s.veil);

            // 纹素尺寸显式传：Blitter 不会自动填 _BlitTexture_TexelSize
            int w = Mathf.Max(1, Screen.width), h = Mathf.Max(1, Screen.height);
            m.SetVector(IdTexel, new Vector4(1f / w, 1f / h, w, h));
        }

        private bool EnsureMaterial()
        {
            if (_material != null) return true;
            var sh = Shader.Find("Hidden/InkWash/InkBloom");
            if (sh == null)
            {
                Debug.LogError("[InkBloomFeature] 找不到 shader: Hidden/InkWash/InkBloom");
                return false;
            }
            _material = CoreUtils.CreateEngineMaterial(sh);
            _material.hideFlags = HideFlags.HideAndDontSave;
            return _material != null;
        }

        public void DisposePass()
        {
            _pass?.Dispose();
            _pass = null;
            CoreUtils.Destroy(_material);
            _material = null;
        }

        protected override void Dispose(bool disposing)
        {
            DisposePass();
            if (InkStyleRegistry.Bloom == this) InkStyleRegistry.Bloom = null;
        }
    }
}
