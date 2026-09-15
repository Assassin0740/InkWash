using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace InkWash.Rendering
{
    /// <summary>
    /// 宣纸底纹 RendererFeature：在**墨线之后**把整幅画"印"到宣纸上。
    ///
    /// 顺序为什么是"先描边、后上纸"：
    /// 纸纹是覆盖全画面的乘性底色，如果它先做、墨线后做，墨线会**压过纸纹**，
    /// 结果墨线是"浮在纸上"的纯色线条，看着像 UI。反过来先描边再上纸，
    /// 墨线会被纸纹一起吃进去（线条上也有纤维和深浅），才像画上去的。
    ///
    /// 所以这里用 <see cref="RenderPassEvent.AfterRenderingTransparents"/> + 1 排在墨线之后。
    /// </summary>
    public class InkPaperFeature : ScriptableRendererFeature
    {
        [System.Serializable]
        public class Settings
        {
            public bool enabled = true;
            public Texture2D paperTex;
            [Range(0.25f, 12f)] public float paperTiling = 2f;
            [Range(0f, 1f)] public float paperStrength = 0.55f;
            [Range(0.2f, 3f)] public float paperContrast = 1.10f;
            [Range(0f, 1f)] public float grainStrength = 0.30f;
            public Color paperTint = new Color(0.98f, 0.972f, 0.95f, 1f);
            [Range(0f, 1f)] public float tintStrength = 0.12f;
            [Range(0f, 1f)] public float vignette = 0.18f;
            [Range(0.5f, 6f)] public float vignetteSharp = 2.4f;
            [Range(0f, 1f)] public float inkDeepen = 0.20f;

            /// <summary>全量浅拷贝。理由与 <see cref="InkEdgeFeature.Settings.Clone"/> 完全一致：
            /// 拷贝必须与字段在同一个类里，否则加字段时必然漏。验收会用反射逐字段比对。</summary>
            public Settings Clone()
            {
                return new Settings
                {
                    enabled = enabled,
                    paperTex = paperTex,
                    paperTiling = paperTiling,
                    paperStrength = paperStrength,
                    paperContrast = paperContrast,
                    grainStrength = grainStrength,
                    paperTint = paperTint,
                    tintStrength = tintStrength,
                    vignette = vignette,
                    vignetteSharp = vignetteSharp,
                    inkDeepen = inkDeepen,
                };
            }
        }

        public Settings settings = new Settings();
        /// <summary>总开关：关掉 = 回到"没有纸"的画面（四阶段对比图 ③→④ 就是切它）。</summary>
        public bool paperOn = true;

        private static readonly int IdPaperTex = Shader.PropertyToID("_PaperTex");
        private static readonly int IdTiling = Shader.PropertyToID("_PaperTiling");
        private static readonly int IdStrength = Shader.PropertyToID("_PaperStrength");
        private static readonly int IdContrast = Shader.PropertyToID("_PaperContrast");
        private static readonly int IdGrain = Shader.PropertyToID("_GrainStrength");
        private static readonly int IdTint = Shader.PropertyToID("_PaperTint");
        private static readonly int IdTintStrength = Shader.PropertyToID("_TintStrength");
        private static readonly int IdVignette = Shader.PropertyToID("_Vignette");
        private static readonly int IdVignetteSharp = Shader.PropertyToID("_VignetteSharp");
        private static readonly int IdInkDeepen = Shader.PropertyToID("_InkDeepen");

        private InkFullScreenPass _pass;
        private Material _material;

        public override void Create()
        {
            InkStyleRegistry.Paper = this;
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            // 开关走注册表（资产上的 paperOn 只是默认值，面板可运行时覆盖且不写回资产）
            if (!settings.enabled || !InkStyleRegistry.PaperEnabled) return;
            var type = renderingData.cameraData.cameraType;
            if (type != CameraType.Game && type != CameraType.SceneView) return;
            if (!EnsureMaterial()) return;

            var evt = RenderPassEvent.AfterRenderingTransparents + 1;   // 必须排在墨线之后
            if (_pass == null)
                _pass = new InkFullScreenPass("InkWash.InkPaper", _material, PushParams,
                    ScriptableRenderPassInput.Color, evt);
            _pass.renderPassEvent = evt;
            renderer.EnqueuePass(_pass);
        }

        private void PushParams(Material m)
        {
            var s = InkStyleRegistry.PaperSettings;
            if (s == null) return;
            if (s.paperTex != null) m.SetTexture(IdPaperTex, s.paperTex);
            m.SetFloat(IdTiling, s.paperTiling);
            m.SetFloat(IdStrength, s.paperStrength);
            m.SetFloat(IdContrast, s.paperContrast);
            m.SetFloat(IdGrain, s.grainStrength);
            m.SetColor(IdTint, s.paperTint);
            m.SetFloat(IdTintStrength, s.tintStrength);
            m.SetFloat(IdVignette, s.vignette);
            m.SetFloat(IdVignetteSharp, s.vignetteSharp);
            m.SetFloat(IdInkDeepen, s.inkDeepen);
        }

        private bool EnsureMaterial()
        {
            if (_material != null) return true;
            var sh = Shader.Find("Hidden/InkWash/InkPaper");
            if (sh == null)
            {
                Debug.LogError("[InkPaperFeature] 找不到 shader: Hidden/InkWash/InkPaper");
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
            if (InkStyleRegistry.Paper == this) InkStyleRegistry.Paper = null;
        }
    }
}
