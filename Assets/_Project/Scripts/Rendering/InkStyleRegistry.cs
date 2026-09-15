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
    ///
    /// 三层全屏效果的执行顺序（RenderPassEvent）：
    ///   墨线（AfterRenderingTransparents）→ 宣纸（+1）→ 墨晕（+2）。
    ///   顺序不是随手排的：墨线是"笔"，纸纹是"纸"，墨晕是"墨渗开" ——
    ///   渗开必须发生在笔和纸**之后**，否则晕开的墨会被后画的墨线重新切出硬边。
    /// </summary>
    public static class InkStyleRegistry
    {
        public static InkEdgeFeature Edge;
        public static InkPaperFeature Paper;
        public static InkBloomFeature Bloom;

        /// <summary>运行时参数覆盖；null = 用 Feature 资产上的值。</summary>
        public static InkEdgeFeature.Settings EdgeRuntime;
        public static InkPaperFeature.Settings PaperRuntime;
        public static InkBloomFeature.Settings BloomRuntime;

        /// <summary>运行时开关覆盖；null = 用 Feature 资产上的值。</summary>
        public static bool? EdgeOn;
        public static bool? PaperOn;
        public static bool? BloomOn;

        /// <summary>调用方负责保证 Feature 不为 null 时再取，返回 null 表示"没有可用的参数"。</summary>
        public static InkEdgeFeature.Settings EdgeSettings =>
            EdgeRuntime != null ? EdgeRuntime : (Edge != null ? Edge.settings : null);

        public static InkPaperFeature.Settings PaperSettings =>
            PaperRuntime != null ? PaperRuntime : (Paper != null ? Paper.settings : null);

        public static InkBloomFeature.Settings BloomSettings =>
            BloomRuntime != null ? BloomRuntime : (Bloom != null ? Bloom.settings : null);

        public static bool EdgeEnabled => EdgeOn ?? (Edge != null && Edge.inkEdgesOn);
        public static bool PaperEnabled => PaperOn ?? (Paper != null && Paper.paperOn);
        public static bool BloomEnabled => BloomOn ?? (Bloom != null && Bloom.bloomOn);

        // 面板第一次调参时才克隆资产参数，避免"只是跑了一遍没动过"也建一份。
        // ★ 克隆走 Settings 自己的 Clone()，不走注册表里手写的字段列表 ——
        //   上一版把拷贝写在注册表里，加字段时漏了两个，症状是"面板一动滑块，
        //   那两个字端就悄悄回到默认值"。详见 InkEdgeFeature.Settings.Clone 的注释。
        public static InkEdgeFeature.Settings EnsureEdgeRuntime()
        {
            if (EdgeRuntime == null && Edge != null)
                EdgeRuntime = Edge.settings.Clone();
            return EdgeRuntime;
        }

        public static InkPaperFeature.Settings EnsurePaperRuntime()
        {
            if (PaperRuntime == null && Paper != null)
                PaperRuntime = Paper.settings.Clone();
            return PaperRuntime;
        }

        public static InkBloomFeature.Settings EnsureBloomRuntime()
        {
            if (BloomRuntime == null && Bloom != null)
                BloomRuntime = Bloom.settings.Clone();
            return BloomRuntime;
        }

        public static void Reset()
        {
            Edge = null;
            Paper = null;
            Bloom = null;
            EdgeRuntime = null;
            PaperRuntime = null;
            BloomRuntime = null;
            EdgeOn = null;
            PaperOn = null;
            BloomOn = null;
        }

        /// <summary>方便验收脚本断言"渲染器资产里真的装配了这两个 Feature"。</summary>
        public static bool BothRegistered => Edge != null && Paper != null;

        /// <summary>三层（墨线 / 宣纸 / 墨晕）是否都已装配。</summary>
        public static bool AllRegistered => Edge != null && Paper != null && Bloom != null;

        /// <summary>一行式状态摘要（验收报告 / 面板标题用）。</summary>
        public static string Describe()
        {
            return "墨线 " + (EdgeEnabled ? "开" : "关")
                 + "　宣纸 " + (PaperEnabled ? "开" : "关")
                 + "　墨晕 " + (BloomEnabled ? "开" : "关");
        }
    }
}
