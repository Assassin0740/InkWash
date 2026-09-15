using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace InkWash.Rendering
{
    /// <summary>
    /// 飞白墨线 RendererFeature：在**透明物体画完之后**插入全屏 Pass，
    /// 用深度 + 法线做边缘提取，把"墨线"压到画面颜色上。
    ///
    /// 时机选择（<see cref="RenderPassEvent.AfterRenderingTransparents"/>）：
    /// 刀光/墨弹是透明物体。描边若在它们**之前**画，透明物体会盖住墨线，
    /// 表现是"角色有边、刀光没边"。放到透明之后，整幅画统一描一次边。
    ///
    /// 参数刻意做成 public 字段：运行时的参数面板直接改字段，
    /// 由 Execute 每帧同步进材质 —— 面板不需要认识材质。
    /// </summary>
    public class InkEdgeFeature : ScriptableRendererFeature
    {
        [System.Serializable]
        public class Settings
        {
            public bool enabled = true;
            public Color edgeColor = new Color(0.04f, 0.045f, 0.06f, 1f);
            [Range(0.1f, 60f)] public float depthSensitivity = 22f;
            /// <summary>深度死区：把"掠射角地面"这种平缓深度斜坡压掉（见 shader 头注 b）。</summary>
            [Range(0f, 1f)] public float depthBias = 0.30f;
            [Range(0.1f, 4f)] public float normalSensitivity = 0.9f;
            [Range(0f, 1f)] public float normalBias = 0.06f;
            [Range(0.5f, 4f)] public float lineThickness = 1f;
            [Range(0f, 3f)] public float lineStrength = 1.6f;

            [Header("飞白")]
            public Texture2D dryBrushTex;
            [Range(0f, 64f)] public float dryBrushScale = 7f;
            [Range(0f, 1f)] public float dryBrushStrength = 0.72f;
            [Range(0f, 1f)] public float dryBrushBias = 0.34f;

            [Header("距离")]
            [Range(5f, 200f)] public float distanceFade = 46f;
            [Range(0.5f, 8f)] public float distanceFadeSharp = 2f;

            /// <summary>
            /// 全量浅拷贝。**必须留在 Settings 类内部**，不能像原来那样写在 Registry 里 ——
            /// 上一版把拷贝写在 Registry，加 depthBias/normalBias 这两个字段时就漏了，
            /// 症状极其隐蔽：面板只要动**任何**滑块，运行时覆盖对象里的死区就成了 0，
            /// 于是"掠射角地面被误判成轮廓"的老毛病静默复发，而面板上那个滑块看着还是 0.3。
            /// 放在本类里，加字段的人就在这个类里，容易看到旁边还有一份拷贝要同步。
            /// （验收脚本另用反射逐字段比对，把"漏拷"变成会失败的断言。）
            /// </summary>
            public Settings Clone()
            {
                return new Settings
                {
                    enabled = enabled,
                    edgeColor = edgeColor,
                    depthSensitivity = depthSensitivity,
                    depthBias = depthBias,
                    normalSensitivity = normalSensitivity,
                    normalBias = normalBias,
                    lineThickness = lineThickness,
                    lineStrength = lineStrength,
                    dryBrushTex = dryBrushTex,
                    dryBrushScale = dryBrushScale,
                    dryBrushStrength = dryBrushStrength,
                    dryBrushBias = dryBrushBias,
                    distanceFade = distanceFade,
                    distanceFadeSharp = distanceFadeSharp,
                };
            }
        }

        public Settings settings = new Settings();
        /// <summary>总开关：关掉 = 回到"没有墨线"的画面（四阶段对比图 ②→③ 就是切它）。</summary>
        public bool inkEdgesOn = true;

        private static readonly int IdEdgeColor = Shader.PropertyToID("_EdgeColor");
        private static readonly int IdDepthSens = Shader.PropertyToID("_DepthSensitivity");
        private static readonly int IdDepthBias = Shader.PropertyToID("_DepthBias");
        private static readonly int IdNormalSens = Shader.PropertyToID("_NormalSensitivity");
        private static readonly int IdNormalBias = Shader.PropertyToID("_NormalBias");
        private static readonly int IdThickness = Shader.PropertyToID("_LineThickness");
        private static readonly int IdStrength = Shader.PropertyToID("_LineStrength");
        private static readonly int IdDryTex = Shader.PropertyToID("_DryBrushTex");
        private static readonly int IdDryScale = Shader.PropertyToID("_DryBrushScale");
        private static readonly int IdDryStrength = Shader.PropertyToID("_DryBrushStrength");
        private static readonly int IdDryBias = Shader.PropertyToID("_DryBrushBias");
        private static readonly int IdFade = Shader.PropertyToID("_DistanceFade");
        private static readonly int IdFadeSharp = Shader.PropertyToID("_DistanceFadeSharp");
        private static readonly int IdTexel = Shader.PropertyToID("_InkTexelSize");

        private InkFullScreenPass _pass;
        private Material _material;

        public override void Create()
        {
            InkStyleRegistry.Edge = this;
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            // 开关走注册表：资产上的 inkEdgesOn 只是**默认值**，面板可以在运行时覆盖它，
            // 且覆盖不会写回资产（详见 InkStyleRegistry 的注释）。
            if (!settings.enabled || !InkStyleRegistry.EdgeEnabled) return;
            var type = renderingData.cameraData.cameraType;
            if (type != CameraType.Game && type != CameraType.SceneView) return;
            if (!EnsureMaterial()) return;

            if (_pass == null)
            {
                _pass = new InkFullScreenPass("InkWash.InkEdge", _material, PushParams,
                    ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Normal,
                    RenderPassEvent.AfterRenderingTransparents);
            }
            _pass.renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
            renderer.EnqueuePass(_pass);
        }

        private void PushParams(Material m)
        {
            var s = InkStyleRegistry.EdgeSettings;
            if (s == null) return;
            m.SetColor(IdEdgeColor, s.edgeColor);
            m.SetFloat(IdDepthSens, s.depthSensitivity);
            m.SetFloat(IdDepthBias, s.depthBias);
            m.SetFloat(IdNormalSens, s.normalSensitivity);
            m.SetFloat(IdNormalBias, s.normalBias);
            m.SetFloat(IdThickness, s.lineThickness);
            m.SetFloat(IdStrength, s.lineStrength);
            if (s.dryBrushTex != null) m.SetTexture(IdDryTex, s.dryBrushTex);
            m.SetFloat(IdDryScale, s.dryBrushScale);
            m.SetFloat(IdDryStrength, s.dryBrushStrength);
            m.SetFloat(IdDryBias, s.dryBrushBias);
            m.SetFloat(IdFade, s.distanceFade);
            m.SetFloat(IdFadeSharp, s.distanceFadeSharp);

            // 纹素尺寸显式传：Blitter 不会自动填 _BlitTexture_TexelSize（见 shader 头注 c）
            int w = Mathf.Max(1, Screen.width), h = Mathf.Max(1, Screen.height);
            m.SetVector(IdTexel, new Vector4(1f / w, 1f / h, w, h));
        }

        /// <summary>返回是否可用。**不可用必须吵** —— 静默跳过的表现是"画面少了描边但什么都不报"。</summary>
        private bool EnsureMaterial()
        {
            if (_material != null) return true;
            var sh = Shader.Find("Hidden/InkWash/InkEdge");
            if (sh == null)
            {
                Debug.LogError("[InkEdgeFeature] 找不到 shader: Hidden/InkWash/InkEdge");
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
            if (InkStyleRegistry.Edge == this) InkStyleRegistry.Edge = null;
        }
    }
}
