using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace InkWash.Rendering
{
    /// <summary>
    /// 水墨风格参数的运行时注册表。
    ///
    /// 为什么需要它：RendererFeature 是**渲染器资产上的对象**，运行时没有方便的公开入口
    /// （`m_RendererFeatures` 是私有的 SerializedProperty）。让每个 Feature 在 `Create()` 时
    /// 把自己登记进来，参数面板就能直接读写，而不必去反射渲染器资产。
    /// 这也让"关掉某一层看效果"变成一行赋值 —— 四阶段对比图靠的就是这个。
    ///
    /// ★ 运行时覆盖（Runtime / *On）：面板为什么不能直接改 Feature 上的字段 ——
    /// Feature 是**资产**，运行时改它 public 的序列化字段会让资产变脏，
    /// 于是"跑一次 Play 就把工程悄悄改了"（滑一次滑块，下次打开工程参数还在，
    /// 而且没人知道是谁改的）。所以面板的写入一律落在下面这几个**静态覆盖**上，
    /// Feature 取值时优先读覆盖、没有覆盖才读资产。资产永远保持不变。
    /// </summary>
    public static class InkStyleRegistry
    {
        public static InkEdgeFeature Edge;
        public static InkPaperFeature Paper;

        /// <summary>运行时参数覆盖；null = 用 Feature 资产上的值。</summary>
        public static InkEdgeFeature.Settings EdgeRuntime;
        public static InkPaperFeature.Settings PaperRuntime;

        /// <summary>运行时开关覆盖；null = 用 Feature 资产上的值。</summary>
        public static bool? EdgeOn;
        public static bool? PaperOn;

        /// <summary>调用方负责保证 Edge/Paper 不为 null 时再取，返回 null 表示"没有可用的参数"。</summary>
        public static InkEdgeFeature.Settings EdgeSettings =>
            EdgeRuntime != null ? EdgeRuntime : (Edge != null ? Edge.settings : null);

        public static InkPaperFeature.Settings PaperSettings =>
            PaperRuntime != null ? PaperRuntime : (Paper != null ? Paper.settings : null);

        public static bool EdgeEnabled => EdgeOn ?? (Edge != null && Edge.inkEdgesOn);
        public static bool PaperEnabled => PaperOn ?? (Paper != null && Paper.paperOn);

        /// <summary>面板第一次调参时才克隆资产参数，避免"只是跑了一遍没动过"也建一份。</summary>
        public static InkEdgeFeature.Settings EnsureEdgeRuntime()
        {
            if (EdgeRuntime == null && Edge != null)
                EdgeRuntime = CopyEdge(Edge.settings);
            return EdgeRuntime;
        }

        public static InkPaperFeature.Settings EnsurePaperRuntime()
        {
            if (PaperRuntime == null && Paper != null)
                PaperRuntime = CopyPaper(Paper.settings);
            return PaperRuntime;
        }

        // 手写浅拷贝而不是 MemberwiseClone：字段名改了编译器会当场报错，
        // 反射式拷贝只会在运行时悄悄漏掉新字段。
        private static InkEdgeFeature.Settings CopyEdge(InkEdgeFeature.Settings s) => new InkEdgeFeature.Settings
        {
            enabled = s.enabled,
            edgeColor = s.edgeColor,
            depthSensitivity = s.depthSensitivity,
            normalSensitivity = s.normalSensitivity,
            lineThickness = s.lineThickness,
            lineStrength = s.lineStrength,
            dryBrushTex = s.dryBrushTex,
            dryBrushScale = s.dryBrushScale,
            dryBrushStrength = s.dryBrushStrength,
            dryBrushBias = s.dryBrushBias,
            distanceFade = s.distanceFade,
            distanceFadeSharp = s.distanceFadeSharp,
        };

        private static InkPaperFeature.Settings CopyPaper(InkPaperFeature.Settings s) => new InkPaperFeature.Settings
        {
            enabled = s.enabled,
            paperTex = s.paperTex,
            paperTiling = s.paperTiling,
            paperStrength = s.paperStrength,
            paperContrast = s.paperContrast,
            grainStrength = s.grainStrength,
            paperTint = s.paperTint,
            tintStrength = s.tintStrength,
            vignette = s.vignette,
            vignetteSharp = s.vignetteSharp,
            inkDeepen = s.inkDeepen,
        };

        public static void Reset()
        {
            Edge = null;
            Paper = null;
            EdgeRuntime = null;
            PaperRuntime = null;
            EdgeOn = null;
            PaperOn = null;
        }

        /// <summary>方便验收脚本断言"渲染器资产里真的装配了这两个 Feature"。</summary>
        public static bool BothRegistered => Edge != null && Paper != null;
    }
}
