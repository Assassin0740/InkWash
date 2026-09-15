// InkSurface.shader —— 场景水墨着色器（墙 / 地面 / 柱子 / 平台）
//
// ============================================================================
// ★ v2 重写（2026-09-15）：从「给几何体上色」改成「给画面分墨阶」
// ============================================================================
// 为什么要重写：用户第三次反馈「水墨风还是有点奇怪、没有纹理、视觉效果特别差」。
// 前几轮一直在**加东西**（加材质、加提亮、加特效），画面就是不像水墨。
// 病根是**坐标系错了**：
//
//   旧模型：给每个物体一个墨色材质，让它受光后"看起来像墨"。
//   真实水墨：画面上的墨是**有限的、分层的**，由浓到淡排布；物体的明暗是这个
//             分层的结果，不是它的输入。
//
// 于是所有参数都在错误的坐标系里找最优值。实测证据（Tools/reports/d_metrics.txt）：
//   全屏亮度阶梯 5/20/50/80/95 = 0.577 / 0.745 / 0.810 / 0.830 / 0.854
//   —— **整个画面挤在 0.58~0.85 这条 0.28 宽的窄带里**，全图没有一个真正的黑。
//
// 两个结构性错误（旧版）：
//
//   1) `lit = lambert + ambient * 0.55`，而环境光被上一轮"去灰"推到 0.885。
//      代入 ⇒ `lit >= 0.487 + lambert`，轻松越过 1.0 被 saturate 削平 ⇒
//      Quantize 把绝大多数像素都落在最高阶 ⇒ 全屏纸白。
//      **这同时解释了"不够白"和"发灰"这对看似矛盾的现象**：
//      受光面被削平到 1.0（白得没层次），背光面卡在 0.5 左右的中灰（灰得没墨感）。
//
//   2) 噪声唯一去处是 jitter 加在 lit 上，而 Quantize 第一句就 saturate(x)。
//      lit 已经饱和在 1.0 ⇒ **噪声在整个受光区（画面 80% 面积）数学上等于不存在**。
//      这不是"纹理太弱"，是纹理被结构性消灭了 —— 调 _BrushStrength 当然没反应。
//
// v2 的解法：
//   · 环境光只占 0.28（不再由它决定明暗），让**法线朝向**真正决定墨阶。
//     「纸白由**受光**给，不由环境光平摊给」—— 这是本轮最重要的一条取向。
//   · Quantize 的输出改成**查墨阶表**（五个离散墨色），而不是连续插值两个颜色。
//   · 墨阶表**阶距非均匀**：淡墨区挤、浓墨区开（真实墨阶就是不等距的）。
//   · 每个物体给一个 `_BandBias`，让墙/柱/地面落在**不同的基础墨阶**上 ——
//     这是"画面有主次"的来源，也是把明度带宽撑开的主要手段。
//
// 与 InkCharacter 的分工不变：
//   · InkCharacter 是**主体**：墨色重，用来"写字"。
//   · InkSurface   是**纸**：整体纸白、只有淡墨阶，用来"留白"。
//   但"纸比墨多"不等于"全屏都是一个白" —— 留白要有对比才叫留白。
Shader "InkWash/InkSurface"
{
    Properties
    {
        // 注意：本工程用**团结引擎**，其 ShaderLab 解析器在属性上遇到
        // [Header(...)] / [Space] 装饰器会直接报 Parse error，因此一个都不用，分组只靠注释。
        [MainTexture] _BaseMap ("基础贴图（漫反射）", 2D) = "white" {}
        [MainColor]   _BaseColor ("基础色", Color) = (1, 1, 1, 1)

        // ---- 墨分五色：五级墨阶的颜色表（阶距刻意非均匀） ----
        // 「墨分五色」是水墨术语：焦 / 浓 / 重 / 淡 / 清。
        // 只暴露三个**锚点**（焦、重、清），中间两级由 _LadderSkew 派生 ——
        // 暴露五个色会让人调成"色阶图"，锚点法天然保住墨的连贯。
        // 注意属性显示名里不能出现英文逗号（团结引擎按逗号裸切参数），一律用「、」。
        _InkDark  ("焦墨（最暗、忌纯黑）", Color) = (0.075, 0.085, 0.12, 1)
        _InkMid   ("重墨（中间锚点）",     Color) = (0.30, 0.315, 0.35, 1)
        _InkLight ("清墨（近纸白）",       Color) = (0.98, 0.976, 0.955, 1)
        _LadderSkew ("中间两级的位置（小=浓墨区更宽）", Range(0.1, 0.9)) = 0.42
        _Bands ("墨阶数（少=大写意）", Range(1, 6)) = 5
        _BandSoftness ("阶间柔度（太大会失去笔阶感、太小出马赫带）", Range(0.001, 0.5)) = 0.08
        _BandBias ("基础墨阶偏移（负=整体更浓、用来给不同物体分主次）", Range(-0.6, 0.6)) = 0
        _InkDensity ("贴图信息量（越大越用贴图的明暗做形体）", Range(0, 1)) = 0.35

        // ---- 飞白 ----
        _BrushTex ("飞白噪声（灰度图）", 2D) = "gray" {}
        _BrushScale ("飞白尺度（越大越细）", Float) = 9
        _BrushStrength ("飞白强度", Range(0, 1)) = 0.5

        // ---- 积墨（世界坐标低频斑驳） ----
        _InkMottle ("积墨（地面斑驳）", Range(0, 1)) = 0.34
        _MottleScale ("积墨尺度", Float) = 0.35

        // ---- ★ v3 纹理管线：三层噪声（机制说明见下方 HLSL 注释） ----
        // 为什么改成程序化：T_InkBrushNoise 是 256² 的**低频平滑**图案
        // （相邻像素相关 0.994、1/8 降采样后 std 不变 ⇒ 内容频率约 32 个特征/整图），
        // 高尺度采样时被 GPU 的 mip 平均成常数 ⇒ 噪声贡献在数学上归零。
        _GrainScale ("纸颗粒尺度（高频，越大越细）", Float) = 96
        _GrainAmp ("纸颗粒强度（直接调制颜色）", Range(0, 1)) = 0.46
        _StrokeScale ("笔触尺度（中频皴法，越大越细）", Float) = 7.0
        _StrokeStretch ("笔触方向性拉伸比（1=各向同性=噪点）", Range(1, 12)) = 3.0
        _StrokeAmp ("笔触强度（阶内浓度调制）", Range(0, 1)) = 0.22

        // ---- 高度留白 ----
        _HeightFade ("高处留白强度", Range(0, 1)) = 0.18
        _HeightFrom ("留白起点高度", Float) = 1.0
        _HeightTo ("留白终点高度", Float) = 5.0

        // ---- 环境方向冷调 ----
        _AmbientTint ("环境色（略偏冷）", Color) = (0.9, 0.93, 1.0, 1)

        // ---- ★ 增量 G：接地墨渍 + 大气透视 ----
        // 病 6：没有接地与环境关系 —— 东西都"浮"在纸上，而且没有纵深。
        // 接地墨渍：离地面 _ContactHeight 米以内的面染一层淡墨。它是让物体"坐"在纸上的
        //   最省事手段，也对角色的脚生效（角色脚在 y≈0 ⇒ 脚下自然积墨）。
        // 大气透视：越远整体越推向清墨（远山淡墨）。参考图里的纵深就来自这里。
        _ContactInk ("接地墨渍强度", Range(0, 1)) = 0.30
        _ContactHeight ("接地墨渍高度（米）", Float) = 0.45
        _GroundY ("地面高度（世界 Y）", Float) = 0
        _AerialFrom ("大气透视起点距离（米）", Float) = 12
        _AerialTo ("大气透视终点距离（米）", Float) = 30
        _AerialStrength ("大气透视强度（远处推向清墨）", Range(0, 1)) = 0.55
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "UniversalMaterialType" = "Lit"
            "Queue" = "Geometry"
        }
        LOD 300

        // ==================================================================
        // 主前向
        // ==================================================================
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Cull Back
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex InkVert
            #pragma fragment InkFrag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4  _BaseColor;
                half4  _InkDark;
                half4  _InkMid;
                half4  _InkLight;
                half   _LadderSkew;
                half   _Bands;
                half   _BandSoftness;
                half   _BandBias;
                half   _InkDensity;
                float4 _BrushTex_ST;
                half   _BrushScale;
                half   _BrushStrength;
                half   _InkMottle;
                half   _MottleScale;
                half   _ContactInk;
                half   _ContactHeight;
                half   _GroundY;
                half   _AerialFrom;
                half   _AerialTo;
                half   _AerialStrength;
                half   _GrainScale;
                half   _GrainAmp;
                half   _StrokeScale;
                half   _StrokeStretch;
                half   _StrokeAmp;
                half   _HeightFade;
                half   _HeightFrom;
                half   _HeightTo;
                half4  _AmbientTint;
            CBUFFER_END

            TEXTURE2D(_BaseMap);   SAMPLER(sampler_BaseMap);
            TEXTURE2D(_BrushTex);  SAMPLER(sampler_BrushTex);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                float2 lightmapUV : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                half3  normalWS   : TEXCOORD2;
                float  fogFactor  : TEXCOORD4;
                float4 shadowCoord: TEXCOORD5;
                DECLARE_LIGHTMAP_OR_SH(lightmapUV, vertexSH, 6);
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings InkVert(Attributes input)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                VertexPositionInputs pos = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs   nrm = GetVertexNormalInputs(input.normalOS);

                o.positionCS  = pos.positionCS;
                o.positionWS  = pos.positionWS;
                o.normalWS    = nrm.normalWS;
                o.uv          = TRANSFORM_TEX(input.uv, _BaseMap);
                o.fogFactor   = ComputeFogFactor(pos.positionCS.z);
                o.shadowCoord = GetShadowCoord(pos);
                OUTPUT_LIGHTMAP_UV(input.lightmapUV, unity_LightmapST, o.lightmapUV);
                OUTPUT_SH(nrm.normalWS, o.vertexSH);
                return o;
            }

            /// 把连续亮度压到 N 个离散阶上。
            /// 阶数比角色更少（默认 3）—— 台阶越少，"一笔刷过"的味道越重。
            half Quantize(half x, half bands, half softness)
            {
                half b = max(1.0h, bands);
                half steps = b - 1.0h;
                if (steps < 0.5h) return 1.0h;
                half scaled = saturate(x) * steps;
                half lower  = floor(scaled);
                half frac   = scaled - lower;
                half k = smoothstep(0.5h - softness * steps, 0.5h + softness * steps, frac);
                return saturate((lower + k) / steps);
            }

            /// ★ 墨分五色：归一化阶位置 → 墨色。
            ///
            /// 为什么不连续插值两个颜色：那样得到的是"灰度渐变"，不是墨。
            /// 五色是**离散的几档**，而且真实墨阶**不等距** ——
            /// 淡墨区很挤（墨少、一点水就变浅），浓墨区很开。
            ///
            /// 只暴露三个锚点（焦 / 重 / 清），中间两级按 _LadderSkew 派生：
            ///   · 浓墨 = 焦→重 之间偏焦的一侧（默认落在焦墨起算的 23% 处）
            ///   · 淡墨 = 重→清 之间偏清的一侧（默认落在 52% 处）
            /// 用**帽子函数**做分段线性：五段权重和恒为 1，因此阶中心刚好取到纯色、
            /// 阶之间平滑过渡 —— 既保住"档"的感觉，又不产生硬边界。
            half3 InkLadderColor(half t)
            {
                t = saturate(t);
                half k = saturate(_LadderSkew);

                half3 c0 = _InkDark.rgb;                                         // 焦墨
                half3 c1 = lerp(_InkDark.rgb, _InkMid.rgb, k * 0.55h);            // 浓墨
                half3 c2 = _InkMid.rgb;                                          // 重墨
                half3 c3 = lerp(_InkMid.rgb, _InkLight.rgb, 0.35h + k * 0.40h);   // 淡墨
                half3 c4 = _InkLight.rgb;                                        // 清墨

                half w0 = 1.0h - saturate(t * 4.0h);
                half w1 = saturate(t * 4.0h)           - saturate((t - 0.25h) * 4.0h);
                half w2 = saturate((t - 0.25h) * 4.0h) - saturate((t - 0.50h) * 4.0h);
                half w3 = saturate((t - 0.50h) * 4.0h) - saturate((t - 0.75h) * 4.0h);
                half w4 = saturate((t - 0.75h) * 4.0h);
                return w0 * c0 + w1 * c1 + w2 * c2 + w3 * c3 + w4 * c4;
            }

            // ==============================================================
            // ★ v3 纹理管线：三层噪声（程序化，不走贴图）
            // ==============================================================
            // 为什么不继续用 _BrushTex：T_InkBrushNoise 是 256² 的**低频平滑**图案
            // （相邻像素相关 0.994，1/8 降采样后方差不变 ⇒ 内容频率约 32 个特征/整图）。
            // 它按 _BrushScale 采样时，屏幕取样率远超内容频率 ⇒ GPU 取到高 mip
            // ⇒ 平均成 mean≈0.483 的常数 ⇒ (n-0.5)≈-0.017 ⇒ **噪声贡献归零**。
            // 所以"纹理看不见"有两个并列的结构性原因：
            //   (a) 噪声被 Quantize 首句的 saturate 削掉（只在阶边界附近有残余）
            //   (b) 噪声被 mip 平均成常数（整个画面）
            // 程序化噪声没有 mip，频率/对比度/各向异性全部由我们直接控制。
            half Hash21(float2 p)
            {
                p = frac(p * float2(123.34h, 345.45h));
                p += dot(p, p + 34.345h);
                return frac(p.x * p.y);
            }

            /// 值噪声：格点哈希 + 平滑插值。
            /// 不用纯白噪声 —— 白噪声读起来是"电视雪花"，值噪声才有"纸"的织理。
            half ValueNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0h - 2.0h * f);
                half a = Hash21(i);
                half b = Hash21(i + float2(1.0h, 0.0h));
                half c = Hash21(i + float2(0.0h, 1.0h));
                half d = Hash21(i + float2(1.0h, 1.0h));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            /// 两层值噪声：单层有明显的方格感，两层才能读成"织理"
            half Fbm2(float2 p)
            {
                return saturate(ValueNoise(p) * 0.65h + ValueNoise(p * 2.17h + 11.3h) * 0.35h);
            }

            /// 三层纹理采样。三层各自有职责，不能混：
            ///   x = 纸颗粒  高频 + 轻微各向异性(1.6:1) —— 纸的纤维，**在纸白区也看得见**
            ///   y = 笔触    中频 + **方向性拉伸**          —— 皴法/笔痕，"一笔刷过"的方向感
            ///   z = 积墨    低频 各向同性                  —— 大块的浓淡不均，破平涂感
            ///
            /// **方向性拉伸是"像笔触"的关键**：中频各向同性噪声只会被读成"噪点/脏"。
            /// 做法是把横向坐标除以拉伸比 ⇒ 特征沿横向被拉长约 4.5 倍。
            ///
            /// 投影用**轻量三平面**（按法线主轴挑两个世界轴）：若一律用 xz，
            /// 竖直墙面会因为 z 沿墙不变而出现"竖直拉丝"，是肉眼可见的贴图错误。
            half3 SampleInkNoise(float3 posWS, half3 normalWS)
            {
                half3 an = abs(normalWS);
                float2 p;
                if (an.y >= an.x && an.y >= an.z)  p = posWS.xz;                  // 朝上 / 朝下
                else if (an.x >= an.z)             p = float2(posWS.z, posWS.y);  // 朝左右
                else                               p = posWS.xy;                  // 朝前后

                // 纸颗粒：轻微各向异性 —— 真实的纸纤维是短的丝，不是圆点
                float2 gp = float2(p.x * 0.62h, p.y);
                half grain = Fbm2(gp * _GrainScale);
                // 远处淡出高频颗粒：屏幕导数大 = 该像素跨了太多噪声周期 ⇒ 再高只会出摩尔纹
                half grainFade = saturate(1.80h - fwidth(gp.x * _GrainScale) * 0.40h);
                grain = lerp(0.5h, grain, grainFade);

                half stroke = Fbm2(float2(p.x / max(_StrokeStretch, 1.0h), p.y) * _StrokeScale + 27.1h);
                half mottle = SAMPLE_TEXTURE2D(_BrushTex, sampler_BrushTex,
                                               p * _MottleScale * 0.21h + 3.7h).r;
                return half3(grain, stroke, mottle);
            }

            half4 InkFrag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                half3 albedo  = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).rgb * _BaseColor.rgb;
                half3 normalWS = normalize(input.normalWS);

                // ---- 光照 ----
                Light mainLight = GetMainLight(input.shadowCoord);
                half atten = mainLight.distanceAttenuation;
                half ndl = dot(normalWS, mainLight.direction);
                half lambert = saturate(ndl) * saturate(mainLight.shadowAttenuation) * atten;

                #ifdef _ADDITIONAL_LIGHTS
                uint addCount = GetAdditionalLightsCount();
                for (uint i = 0u; i < addCount; ++i)
                {
                    Light l = GetAdditionalLight(i, input.positionWS);
                    half a = l.distanceAttenuation * l.shadowAttenuation;
                    lambert = saturate(lambert + saturate(dot(normalWS, l.direction)) * a);
                }
                #endif

                // 环境（SH）：大面积物体的暗部几乎完全靠它撑起来，
                // 场景环境光调暗一点，整个空间就直接"发灰"——所以这里给足权重。
                half3 sh = SampleSH(normalWS);
                half ambient = saturate(dot(sh, half3(0.333h, 0.333h, 0.333h)));

                // ---- 取三层噪声（纸颗粒 / 笔触 / 积墨）----
                half3 nz = SampleInkNoise(input.positionWS, normalWS);

                // ① 抖阶边界：噪声必须加在 **saturate 之前**。
                //    v1 的 jitter 加在已经饱和的 lit 上，被 Quantize 首句的 saturate 削掉
                //    ⇒ 受光区（画面 80% 面积）噪声数学上等于不存在。这一路修的就是它。
                half jitter = ((nz.y - 0.5h) * 0.75h + (nz.x - 0.5h) * 0.40h) * _BrushStrength;

                // ★ v2 核心公式：主光 0.80、环境光 0.40，再加上本物体的基础墨阶偏移。
                //   旧版是 `lambert + ambient*0.55` 且环境光 0.885 ⇒ 必然饱和。
                //   降环境光权重的两个目的：一是把明度带宽撑开，二是给 jitter 留出余量 ——
                //   **抖动必须在 saturate 之前有空间，否则噪声等于不存在**。
                //   为什么不是更低（0.28）：那样背光面只剩 ambient*0.28≈0.10，
                //   会被 saturate 压到 0 而整片落进焦墨 —— 一面墙变成纯黑死板。
                //   0.40 让背光面落在"浓墨"而不是"焦墨"：保住层次，又不失重。
                half lit = saturate(lambert * 0.80h + ambient * 0.40h + _BandBias + jitter);
                half ramp = Quantize(lit, _Bands, _BandSoftness);

                // ---- 高度留白：越高越推向清墨（远山淡墨） ----
                half h = saturate((input.positionWS.y - _HeightFrom) / max(_HeightTo - _HeightFrom, 0.01h));
                ramp = lerp(ramp, 1.0h, h * _HeightFade);

                // ② ★ 阶内浓度调制（新增）—— 让**同一墨阶内部**有浓淡，"墨才有呼吸"。
                //    为什么必须有这一路：① 只在阶边界附近有效 —— 远离边界的像素抖了也还在
                //    同一阶，Quantize 输出一模一样。而画面 80% 的面积是阶内部。
                //    S6 的"没有纹理"就是这一步缺失的直接后果。
                //    权重刻意压到 0.22：扫描实测 `*0.5` 时低频幅度会把亮部顶到墨阶表上界、
                //    被削平 ⇒ 等于给画面加低频压扩器，高频纹理反而掉 30%（E3 vs E2）。
                ramp = saturate(ramp + (nz.y - 0.5h) * _StrokeAmp * 0.22h
                                     + (nz.z - 0.5h) * _InkMottle  * 0.30h);

                // ---- ★ 增量 G-1 接地墨渍：离地越近越浓，让物体"坐"在纸上 ----
                //   ★ 必须乘 (1 - 朝上程度)：只按世界高度判会把**整块地面**也压暗
                //     （地面 y=0 ⇒ contact 恒为 1），那等于把留白亲手涂掉。
                //     乘上法线因子后：地面/平台顶面 contact=0（不受影响），
                //     墙脚、柱脚、角色的脚（法线接近水平或朝下）才积墨。
                half contact = saturate(1.0h - (input.positionWS.y - _GroundY) / max(_ContactHeight, 0.01h))
                             * (1.0h - saturate(normalWS.y));
                ramp = saturate(ramp - contact * _ContactInk);

                // ---- ★ 增量 G-2 大气透视：远处整体推向清墨（远山淡墨）----
                //   注意它和 _HeightFade 不是一回事：高度留白治"远山"，大气透视治"物距"。
                //   这也解释了参考图里的纵深：同一面墙，近处是墨、远处是纸。
                float camDist = distance(input.positionWS, _WorldSpaceCameraPos);
                half aerial = saturate((camDist - _AerialFrom) / max(_AerialTo - _AerialFrom, 0.01h));
                ramp = lerp(ramp, 1.0h, aerial * _AerialStrength);

                // ---- 着色：光照 → 墨阶位置 → 墨色 ----
                half3 color = InkLadderColor(ramp);

                // 贴图只提供**低幅度**的形体信息。水墨不靠固有色而靠墨阶；
                // 但完全没有贴图时，石头与木头会长得一模一样，所以留一条低权重通路。
                color *= lerp(half3(1,1,1), albedo, _InkDensity);

                // 环境方向冷调：让受环境光照亮的暗部带一点冷，避免整体发黄发闷
                color *= lerp(half3(1,1,1), _AmbientTint.rgb, (1.0h - ramp) * 0.35h);

                // ③ ★ 直接调制最终颜色（新增）—— 质感直接写在颜色上。
                //    乘的是**零均值**噪声，因此均值≈1 ⇒ 不会像 InkPaper 的纯乘性纸纹那样
                //    把整幅压暗（"想看见纸纹就必然变灰"的零和死结在那里，不在这里）。
                //    幅度控制在 ±0.05~0.10 亮度：小于 0.05 人眼读不到，大于 0.15 就读成"脏"。
                half texMod = (nz.x - 0.5h) * _GrainAmp + (nz.y - 0.5h) * _StrokeAmp * 0.22h;
                color *= (1.0h + texMod);

                // ---- 积墨：低洼/背光处一片片渗进去的深色 ----
                // 只作用在偏亮的阶上（清墨/淡墨）—— 暗部再加就糊了。
                half mottleMask = saturate(nz.z - 0.5h) * 2.0h * _InkMottle * (1.0h - ramp * 0.55h);
                color = lerp(color, _InkDark.rgb, mottleMask * 0.55h);

                color = MixFog(color, input.fogFactor);
                return half4(color, 1.0h);
            }
            ENDHLSL
        }

        // ==================================================================
        // 阴影投射
        // ==================================================================
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4  _BaseColor; half4 _InkDark; half4 _InkMid; half4 _InkLight;
                half   _LadderSkew; half _Bands; half _BandSoftness; half _BandBias; half _InkDensity;
                float4 _BrushTex_ST; half _BrushScale; half _BrushStrength;
                half   _InkMottle; half _MottleScale;
                half   _ContactInk;
                half   _ContactHeight;
                half   _GroundY;
                half   _AerialFrom;
                half   _AerialTo;
                half   _AerialStrength;
                half   _GrainScale;
                half   _GrainAmp;
                half   _StrokeScale;
                half   _StrokeStretch;
                half   _StrokeAmp;
                half   _HeightFade; half _HeightFrom; half _HeightTo;
                half4  _AmbientTint;
            CBUFFER_END

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings   { float4 positionCS : SV_POSITION; };

            Varyings ShadowPassVertex(Attributes input)
            {
                Varyings o;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 lightDirectionWS = normalize(_LightPosition - positionWS);
                #else
                    float3 lightDirectionWS = _LightDirection;
                #endif
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
                #if UNITY_REVERSED_Z
                    positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                o.positionCS = positionCS;
                return o;
            }

            half4 ShadowPassFragment(Varyings input) : SV_TARGET { return 0; }
            ENDHLSL
        }

        // ==================================================================
        // 深度 + 法线（屏幕空间飞白墨线需要它 —— 没有这个 Pass，描边看不见场景）
        // ==================================================================
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }

            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex DepthNormalsVertex
            #pragma fragment DepthNormalsFragment

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4  _BaseColor; half4 _InkDark; half4 _InkMid; half4 _InkLight;
                half   _LadderSkew; half _Bands; half _BandSoftness; half _BandBias; half _InkDensity;
                float4 _BrushTex_ST; half _BrushScale; half _BrushStrength;
                half   _InkMottle; half _MottleScale;
                half   _ContactInk;
                half   _ContactHeight;
                half   _GroundY;
                half   _AerialFrom;
                half   _AerialTo;
                half   _AerialStrength;
                half   _GrainScale;
                half   _GrainAmp;
                half   _StrokeScale;
                half   _StrokeStretch;
                half   _StrokeAmp;
                half   _HeightFade; half _HeightFrom; half _HeightTo;
                half4  _AmbientTint;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half3  normalWS   : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings DepthNormalsVertex(Attributes input)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return o;
            }

            half4 DepthNormalsFragment(Varyings input) : SV_TARGET
            {
                // URP 的 _CameraNormalsTexture 存的是世界空间法线
                half3 n = normalize(input.normalWS);
                return half4(n, 0.0h);
            }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Lit"
}
