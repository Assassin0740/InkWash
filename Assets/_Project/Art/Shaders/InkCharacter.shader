// InkCharacter.shader —— 水墨角色着色器（多阶量化光照 + 飞白扰动 + 轮廓墨）
//
// 设计目标（论文 · E1）：
//   把「PBR 连续光照」替换成「毛笔式的离散墨阶」，并让**墨阶的边界不是一条直线**。
//   这是水墨风与普通卡通渲染最大的区别 —— 普通 toon 的明暗交界是一条干净的曲线，
//   而水墨的明暗交界是**一笔刷过去**留下的、毛糙且深浅不匀的边。
//
// 三个技术点：
//   ① **多阶量化**：NdotL 不直接当亮度，而是先落到 N 个离散阶上（默认 4 阶）。
//      阶数可调 —— 它是"写意程度"的旋钮：阶数少 = 大写意，阶数多 = 偏写实。
//   ② **飞白扰动**：用一张噪声图去**抖动量化阈值**，于是同一阶的分界线在不同位置
//      前后错开，形成"笔锋擦过纸面"的斑驳感（飞白 = 笔快、墨少，露白）。
//      关键是扰动加在 **NdotL 上**而不是加在最终颜色上：加在颜色上只会得到"脏"，
//      加在阈值上才会得到"笔触"。
//   ③ **轮廓墨**：Fresnel 边缘单独上一道墨色，让角色在复杂背景里仍有剪纸般的边界
//      （它和屏幕空间的飞白墨线是两层，一层在模型上、一层在画面上，叠起来才立得住）。
//
// 颜色取向：**暗部不是黑色，是墨色**（偏冷的靛青黑）；受光不是白色，是**宣纸色**（微暖的米白）。
// 直接 lerp 到纯黑纯白会让画面瞬间变成"3D 卡通"而不是"水墨"。
Shader "InkWash/InkCharacter"
{
    Properties
    {
        // 注意：本工程用**团结引擎**，其 ShaderLab 解析器在属性上遇到
        // [Header(...)] / [Space] 装饰器会直接报
        //   Parse error: syntax error, unexpected $undefined,
        //   expecting TVAL_ID or TVAL_VARREF at line N
        // 因而这里**一个装饰器都不用**，分组只靠注释。（纯 Inspector 观感问题，不影响功能）
        [MainTexture] _BaseMap ("基础贴图（漫反射）", 2D) = "white" {}
        [MainColor]   _BaseColor ("基础色", Color) = (1, 1, 1, 1)

        // ---- 墨分五色：与 InkSurface **共用同一套墨阶表** ----
        // 全画面只有一个调色板是水墨成立的硬条件：两套墨色叠在一起，
        // 角色会"贴"在背景上。改这里的值时必须同步改 InkSurface。
        _InkDark  ("焦墨（最暗、忌纯黑）", Color) = (0.06, 0.078, 0.132, 1)
        _InkMid   ("重墨（中间锚点）",     Color) = (0.265, 0.295, 0.375, 1)
        _InkLight ("清墨（近纸白）",       Color) = (0.988, 0.976, 0.940, 1)
        _LadderSkew ("中间两级的位置（小=浓墨区更宽）", Range(0.1, 0.9)) = 0.42
        _Bands ("墨阶数（少=大写意）", Range(1, 8)) = 4
        _BandSoftness ("阶间柔度", Range(0.001, 0.5)) = 0.06
        _BandBias ("基础墨阶偏移（负=整体更浓。★主体整体压向浓墨靠它、不靠光照）", Range(-0.8, 0.6)) = -0.40
        _InkDensity ("贴图信息量（越大越用贴图的明暗做形体）", Range(0, 1)) = 0.88

        // ---- ★ 增量 F：去色 ----
        // 病 5：底层贴图的色相泄漏。KayKit 骨骼是亮蓝灰、斗篷是橙 ——
        // 它们是画面上**唯一的高饱和色**，对比最强，于是抢走全部视线（M4b 1.5% vs 目标 ≤0.5%）。
        // 保留 8% 原色相：明暗关系全保留（骨头与袍子的明度差仍在、形态可辨），只消灭色相差。
        _ChromaKeep ("墨彩强度（0=纯墨、1=衣料本色透出、>1 更艳）", Range(0, 2)) = 0.45

        // ---- ★ 参照《大神》：**几何轮廓**（反向外扩壳），不是屏幕空间细线 ----
        // 为什么必须走几何：屏幕空间等宽细线本质是"边缘检测"，改来改去都是制图描边。
        // 几何壳能做三件屏幕空间做不到的事：
        //   ① 掠射边更粗：正对相机的面几乎不外扩 ⇒ 轮廓被"描"在结构转折处
        //   ② 距离补偿：线宽在屏幕空间恒定
        //   ③ 飞白断笔：与身上的纹理同源，整段收笔
        // 只加在角色/敌人/剑上：白盒是硬边盒子，逐面外扩会在棱角开裂，且地面不该有轮廓。
        _OutlineWidth ("轮廓宽度（世界米）", Range(0, 0.15)) = 0.055
        _OutlineColor ("轮廓墨色", Color) = (0.035, 0.04, 0.055, 1)
        _OutlineDistScale ("距离补偿（每米增量）", Range(0, 0.3)) = 0.06
        _OutlineFacing ("掠射加权（0=等宽、1=掠射边才粗）", Range(0, 1)) = 0.72
        _OutlineDry ("飞白断笔强度", Range(0, 1)) = 0.55
        _OutlineScale ("飞白尺度", Float) = 5

        // ---- Brush 飞白 ----
        _BrushTex ("飞白噪声（灰度图）", 2D) = "gray" {}
        _BrushScale ("飞白尺度（越大越细）", Float) = 26
        _BrushStrength ("飞白强度（0=光滑卡通）", Range(0, 1)) = 0.6
        _BrushUvFromWorld ("飞白按世界坐标（关=按 UV）", Range(0, 1)) = 1

        // ---- ★ v3 纹理管线：与 InkSurface **同一套纹理语言** ----
        // 角色与场景若用两套纹理，角色会"贴"在背景上（同两套墨色一样是致命的）。
        _GrainScale ("纸颗粒尺度（高频，越大越细）", Float) = 64
        _GrainAmp ("纸颗粒强度（直接调制颜色）", Range(0, 1)) = 0.18
        // ↑ 角色的颗粒**必须比场景弱**：同一个 1 cm 颗粒，在 2 m 高的角色身上
        //   映射到 4~6 px（读成"砂纸"），在地面上只映射到 2~3 px（读成"纸纹"）。
        //   计划 §5 风险表里的「重墨导致画面脏」说的就是这个，实测确认成立。
        _StrokeScale ("笔触尺度（中频皴法，越大越细）", Float) = 8.0
        _StrokeStretch ("笔触方向性拉伸比（1=各向同性=噪点）", Range(1, 12)) = 3.0
        _StrokeAmp ("笔触强度（阶内浓度调制）", Range(0, 1)) = 0.16
        _MottleScale ("积墨尺度（低频浓淡不均）", Float) = 0.55

        // ---- Rim 轮廓墨 ----
        _RimColor ("轮廓墨色", Color) = (0.08, 0.09, 0.12, 1)
        _RimPower ("轮廓收束（越大越窄）", Range(0.5, 12)) = 3.2
        _RimStrength ("轮廓强度", Range(0, 2)) = 0.85

        // ---- Specular 湿墨高光 ----
        _SpecBands ("高光阶数", Range(1, 6)) = 2
        _SpecStrength ("高光强度", Range(0, 2)) = 0.5
        _SpecSize ("高光收束", Range(1, 200)) = 48

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
        // 主前向：量化光照
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
            #pragma vertex InkVertex
            #pragma fragment InkFragment

            // 主光阴影的三种形态（屏幕空间阴影 / 级联 / 单级）
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            // SRP Batcher 要求所有材质属性集中在同一个 CBUFFER 里
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
                half   _ChromaKeep;
                float4 _BrushTex_ST;
                half   _BrushScale;
                half   _BrushStrength;
                half   _BrushUvFromWorld;
                half   _GrainScale;
                half   _GrainAmp;
                half   _StrokeScale;
                half   _StrokeStretch;
                half   _StrokeAmp;
                half   _MottleScale;
                half   _OutlineWidth;
                half4  _OutlineColor;
                half   _OutlineDistScale;
                half   _OutlineFacing;
                half   _OutlineDry;
                half   _OutlineScale;
                half   _ContactInk;
                half   _ContactHeight;
                half   _GroundY;
                half   _AerialFrom;
                half   _AerialTo;
                half   _AerialStrength;
                half4  _RimColor;
                half   _RimPower;
                half   _RimStrength;
                half   _SpecBands;
                half   _SpecStrength;
                half   _SpecSize;
            CBUFFER_END

            TEXTURE2D(_BaseMap);        SAMPLER(sampler_BaseMap);
            TEXTURE2D(_BrushTex);       SAMPLER(sampler_BrushTex);

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
                float3 positionOS : TEXCOORD3;
                float  fogFactor  : TEXCOORD4;
                float4 shadowCoord: TEXCOORD5;
                DECLARE_LIGHTMAP_OR_SH(lightmapUV, vertexSH, 6);
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings InkVertex(Attributes input)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                VertexPositionInputs pos = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs   nrm = GetVertexNormalInputs(input.normalOS);

                o.positionCS = pos.positionCS;
                o.positionWS = pos.positionWS;
                o.positionOS = input.positionOS.xyz;
                o.normalWS   = nrm.normalWS;
                o.uv         = TRANSFORM_TEX(input.uv, _BaseMap);
                o.fogFactor  = ComputeFogFactor(pos.positionCS.z);
                o.shadowCoord = GetShadowCoord(pos);
                OUTPUT_LIGHTMAP_UV(input.lightmapUV, unity_LightmapST, o.lightmapUV);
                OUTPUT_SH(nrm.normalWS, o.vertexSH);
                return o;
            }

            /// 把连续亮度压到 N 个离散阶上。
            /// 用 saturate 保证 1.0 时不会因为 floor 的边界而掉到少一阶（那是常见的"最亮处发灰"）。
            half Quantize(half x, half bands, half softness)
            {
                half b = max(1.0h, bands);
                half steps = b - 1.0h;
                // ★ 单阶 = **关闭量化**，而不是"全亮"。
                //   大面积平面（院墙、柱子）没有形体明暗，量化器唯一的输入就是
                //   _BrushStrength 抖动出来的噪声 —— 会被整阶放大成大块迷彩斑。
                //   返回 saturate(x) 让墨色随光照**连续**变化：墙因此成为一面
                //   受光的淡墨白墙，而不是一堵花墙。人物的形体明暗仍靠 _Bands>=2 量化。
                if (steps < 0.5h) return saturate(x);              // 单阶：全亮（纯平涂）
                half scaled = saturate(x) * steps;
                half lower  = floor(scaled);
                half frac   = scaled - lower;
                // 柔度：在阶的边界附近做一点点过渡，避免完全硬边（硬到极致会有锯齿抖动）
                half k = smoothstep(0.5h - softness * steps, 0.5h + softness * steps, frac);
                return saturate((lower + k) / steps);
            }

            /// ★ 墨分五色：归一化阶位置 → 墨色。与 InkSurface 里的实现**逐字相同** ——
            /// 这是刻意的：两处必须共用同一个调色板，任一处改了另一处要跟着改。
            /// （帽子函数分段线性，五段权重和恒为 1；详见 InkSurface.shader 的注释。）
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
            half3 SampleInkNoise(float3 posWS, half3 normalWS, float2 uv)
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
                // ★ 阈值必须与"屏幕采样率"挂钩，而不是拍脑袋的 1.8/0.4：
                //   fwidth ≈ 1 表示"一个像素正好跨一个噪声周期"，再高就是纯混叠。
                //   旧值要 fwidth>4.5 才淡出 —— 任何实用距离都够不到 ⇒ 远处纸颗粒
                //   混叠成均匀灰噪声（画面发脏、发灰）。新值在 fwidth=1 处全淡出。
                half grainFade = saturate(2.0h - fwidth(gp.x * _GrainScale) * 2.0h);
                grain = lerp(0.5h, grain, grainFade);

                // _BrushUvFromWorld=1（默认）用世界坐标三平面：纹理不随角色动作滑动
                // =0 时退回物体 UV：纹理贴着模型走（做"墨迹长在衣服上"时用）
                float2 sp = lerp(p, uv * 0.5h, 1.0h - _BrushUvFromWorld);
                half stroke = Fbm2(float2(sp.x / max(_StrokeStretch, 1.0h), sp.y) * _StrokeScale + 27.1h);
                half mottle = SAMPLE_TEXTURE2D(_BrushTex, sampler_BrushTex,
                                               p * _MottleScale * 0.21h + 3.7h).r;
                return half3(grain, stroke, mottle);
            }

            half4 InkFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                half3 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).rgb * _BaseColor.rgb;
                // ★ 明暗去色：贴图在这里只贡献**明暗**（衣纹、骨节的形体信息）。
                //   色相另存一份，到最终颜色处作为"墨彩"注入 —— 见下方 _ChromaKeep。
                half3 albedoHue = albedo;
                {
                    half grey = dot(albedo, half3(0.2126h, 0.7152h, 0.0722h));
                    albedo = lerp(half3(grey, grey, grey), albedo, 0.10h);
                }
                half3 normalWS = normalize(input.normalWS);
                half3 viewDirWS = normalize(GetWorldSpaceViewDir(input.positionWS));

                // ---- 光照输入 ----
                Light mainLight = GetMainLight(input.shadowCoord);
                half3 lightDir = mainLight.direction;
                half  atten    = mainLight.distanceAttenuation;

                half ndl = dot(normalWS, lightDir);
                // ★ 阴影是"额外的暗部来源"，直接折进明暗量；不单独乘一遍，
                //   否则会出现"阶已经分完了才被阴影压暗"的双重暗边。
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

                // 环境（SH）：给暗部一点点方向性，避免整片死平
                half3 sh = SampleSH(normalWS);
                half ambient = saturate(dot(sh, half3(0.333h, 0.333h, 0.333h)));

                // ---- 取三层噪声（纸颗粒 / 笔触 / 积墨）----
                half3 nz = SampleInkNoise(input.positionWS, normalWS, input.uv);

                // ① 抖阶边界：噪声必须加在 **saturate 之前**，否则被 Quantize 首句削掉。
                half jitter = ((nz.y - 0.5h) * 0.75h + (nz.x - 0.5h) * 0.40h) * _BrushStrength;
                // ★ v2：`_BandBias` 是"这个物体用几号墨"的显式分配。
                //   角色的墨色重不重，取决于这里，而不是取决于它有没有被太阳照到 ——
                //   后者会让"背光的角色才黑"，而水墨画里的主体黑是画家选的墨。
                half lit = saturate(lambert * 0.80h + ambient * 0.42h + _BandBias + jitter);
                half ramp = Quantize(lit, _Bands, _BandSoftness);

                // ② ★ 阶内浓度调制（新增）—— 同一墨阶内部有浓淡。
                //    ① 只在阶边界附近有效，而画面 80% 的面积是阶内部 ⇒ 正解在这里。
                //    幅度比场景小：角色是"写字的主体"，纹理过强会让轮廓读不出来。
                //    低频积墨权重从 0.15 降到 0.05：实测 0.15 时角色头骨上会出现
                //    0.3 m 量级的大块深斑，读起来是"发霉"而不是"墨"——
                //    因为同一个世界尺度噪声，在 2 m 高的角色身上映射成大斑，
                //    在地面上却只映射成细纹。**尺度必须按物体尺寸调**。
                ramp = saturate(ramp + (nz.y - 0.5h) * _StrokeAmp * 0.18h
                                     + (nz.z - 0.5h) * _StrokeAmp * 0.05h);

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
                // 与 InkSurface 同一张表。因此角色和场景的墨阶天然对得上，
                // 不会出现"角色是暖灰、场景是冷灰"这种两个调色板打架的情况。
                half3 color = InkLadderColor(ramp);

                // 贴图只提供**低幅度**的形体信息（衣纹、骨节）。它不再是"颜色来源"，
                // 只是让同阶内部有细节 —— 水墨的形体靠墨阶，不靠固有色。
                color *= lerp(half3(1,1,1), albedo, _InkDensity);

                // ★ 墨彩：中国画的黑白之外还有丹青 —— 赭石、花青、藤黄。
                //   贴图的"纯色相项"(albedo − luma) 是**零均值**的：乘回墨色只改色相、
                //   几乎不改明暗。于是形体（墨阶）与颜色（衣料本色）彻底解耦 ——
                //   这正是水墨人物"以墨立骨、以色辅之"的做法。
                //   系数 2.2 是过饱和回填：本作贴图彩度仅 0.135，不放大就看不出来。
                {
                    half gh = dot(albedoHue, half3(0.2126h, 0.7152h, 0.0722h));
                    half3 chroma = (albedoHue - gh) * _ChromaKeep * 2.2h;
                    color = saturate(color * (1.0h + chroma));
                }

                // ③ ★ 直接调制最终颜色（新增）—— 零均值噪声 ⇒ 均值≈1，不会把主体整体压暗。
                half texMod = (nz.x - 0.5h) * _GrainAmp + (nz.y - 0.5h) * _StrokeAmp * 0.20h;
                color *= (1.0h + texMod);

                // ---- 轮廓墨（Fresnel）：让角色在任何背景上都有"剪纸边" ----
                half fresnel = pow(saturate(1.0h - dot(normalWS, viewDirWS)), _RimPower);
                color = lerp(color, _RimColor.rgb, saturate(fresnel * _RimStrength));

                // ---- 湿墨高光：同样量化成几阶，避免出现连续的塑料反光 ----
                half3 halfDir = normalize(lightDir + viewDirWS);
                half spec = pow(saturate(dot(normalWS, halfDir)), _SpecSize);
                spec *= mainLight.shadowAttenuation * atten;
                half specStep = Quantize(spec, _SpecBands, 0.02h);
                // 只给"亮部"加高光：暗部加高光会变成塑料
                color += specStep * _SpecStrength * smoothstep(0.55h, 1.0h, ramp) * _InkLight.rgb;

                color = MixFog(color, input.fogFactor);
                return half4(color, 1.0h);
            }
            ENDHLSL
        }

        // ==================================================================
        // ★ 墨线轮廓（参照《大神》的几何外扩壳 / inverted hull）
        // ==================================================================
        // 渲染顺序：URP 按 ShaderTagId 分批绘制，`SRPDefaultUnlit` 这一批
        // **先于** `UniversalForward`。轮廓先写深度、物体再盖上去，天然正确，
        // 不需要改渲染队列，也不需要额外的 renderer feature。
        Pass
        {
            Name "InkOutline"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            Cull Front      // 只画背面 —— 外扩壳的背面正好落在物体轮廓之外
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex OutlineVertex
            #pragma fragment OutlineFragment

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4  _BaseColor;
                half4  _InkDark; half4 _InkMid; half4 _InkLight;
                half   _LadderSkew; half _Bands; half _BandSoftness; half _BandBias;
                half   _InkDensity;
                half   _ChromaKeep;
                float4 _BrushTex_ST; half _BrushScale; half _BrushStrength; half _BrushUvFromWorld;
                half   _GrainScale; half _GrainAmp; half _StrokeScale; half _StrokeStretch; half _StrokeAmp;
                half   _MottleScale;
                half   _OutlineWidth;
                half4  _OutlineColor;
                half   _OutlineDistScale;
                half   _OutlineFacing;
                half   _OutlineDry;
                half   _OutlineScale;
                half   _ContactInk; half _ContactHeight; half _GroundY;
                half   _AerialFrom; half   _AerialTo; half   _AerialStrength;
                half4  _RimColor; half _RimPower; half _RimStrength;
                half   _SpecBands; half _SpecStrength; half _SpecSize;
            CBUFFER_END

            struct OutlineAttrs { float4 positionOS : POSITION; float3 normalOS : NORMAL; };

            struct OutlineVary
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
            };

            // 与主着色**同源**的噪声（轮廓的断笔要跟身上的纹理由同一套噪声驱动，
            // 否则会出现"轮廓断了但身上没断"的错位感）
            half OHash21(float2 p)
            {
                p = frac(p * float2(123.34h, 345.45h));
                p += dot(p, p + 34.345h);
                return frac(p.x * p.y);
            }
            half OValueNoise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                f = f * f * (3.0h - 2.0h * f);
                half a = OHash21(i);
                half b = OHash21(i + float2(1.0h, 0.0h));
                half c = OHash21(i + float2(0.0h, 1.0h));
                half d = OHash21(i + float2(1.0h, 1.0h));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            OutlineVary OutlineVertex(OutlineAttrs v)
            {
                OutlineVary o;
                float3 posWS = TransformObjectToWorld(v.positionOS.xyz);
                float3 nrmWS = TransformObjectToWorldNormal(v.normalOS);
                float3 viewDir = normalize(GetWorldSpaceViewDir(posWS));

                // ① 掠射边更粗：正对相机的面几乎不外扩 ⇒ 轮廓被"描"在转折处。
                //    这是《大神》轮廓线的核心观感 —— 它不是等宽描边。
                //    pow(.,0.7) 提中段：让"半掠射"的过渡区也起笔，否则转折处会突然断细。
                half graze = saturate(1.0h - abs(dot(nrmWS, viewDir)));
                graze = pow(graze, 0.7h);
                half facing = lerp(1.0h, graze, _OutlineFacing);

                // ② 飞白断笔：低频噪声做长距离开断。
                //    ★ 采样点必须用**物体空间**而不是世界空间 —— 世界空间会让飞白图案
                //      随角色移动"流过"轮廓，走起来整条线在沸腾。
                //      物体空间（蒙皮前静置坐标）与体表是稳定映射，图案等于**画在皮肤上**。
                float2 np = v.positionOS.xy * _OutlineScale;
                half dry = OValueNoise(np);
                half dryMask = lerp(1.0h, saturate((dry - 0.28h) * 3.2h), _OutlineDry);

                // ③ 距离补偿：屏幕空间线宽恒定
                float dist = distance(posWS, _WorldSpaceCameraPos);
                half w = _OutlineWidth * (1.0h + dist * _OutlineDistScale);

                // ④ 积墨：墨在低处积（与 G-1 接地墨渍同一套"重力"逻辑）。
                //    复用既有的 _ContactInk/_ContactHeight/_GroundY，
                //    不新增 uniform —— 新增 uniform 要同步改 4 个 CBUFFER，是已知事故源。
                half pool = saturate(1.0h - (posWS.y - _GroundY) / max(_ContactHeight, 0.01h));
                w *= (1.0h + pool * _ContactInk);

                posWS += nrmWS * (w * max(facing, 0.12h) * dryMask);

                o.positionWS = posWS;
                o.positionCS = TransformWorldToHClip(posWS);
                return o;
            }

            half4 OutlineFragment(OutlineVary i) : SV_Target
            {
                return half4(_OutlineColor.rgb, 1.0h);
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
            // ★ 必须显式包含核心包的 CommonMaterial.hlsl：Shadows.hlsl 用到 LerpWhiteTo，
            //   但它自己只 include 了 Common.hlsl（LerpWhiteTo 在 CommonMaterial.hlsl 里）。
            //   官方 Lit.shader 之所以不报错，是因为它先 include 了 SurfaceInput.hlsl 顺带带进来。
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4  _BaseColor; half4 _InkDark; half4 _InkMid; half4 _InkLight;
                half   _LadderSkew; half _Bands; half _BandSoftness; half _BandBias; half _InkDensity;
                half   _ChromaKeep;
                float4 _BrushTex_ST; half _BrushScale; half _BrushStrength; half _BrushUvFromWorld;
                half   _GrainScale;
                half   _GrainAmp;
                half   _StrokeScale;
                half   _StrokeStretch;
                half   _StrokeAmp;
                half   _MottleScale;
                half   _OutlineWidth;
                half4  _OutlineColor;
                half   _OutlineDistScale;
                half   _OutlineFacing;
                half   _OutlineDry;
                half   _OutlineScale;
                half   _ContactInk;
                half   _ContactHeight;
                half   _GroundY;
                half   _AerialFrom;
                half   _AerialTo;
                half   _AerialStrength;
                half4  _RimColor; half _RimPower; half _RimStrength;
                half   _SpecBands; half _SpecStrength; half _SpecSize;
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
        // 深度 + 法线（给屏幕空间飞白墨线用；没有这个 Pass，描边就"看不见"角色）
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
                half   _ChromaKeep;
                float4 _BrushTex_ST; half _BrushScale; half _BrushStrength; half _BrushUvFromWorld;
                half   _GrainScale;
                half   _GrainAmp;
                half   _StrokeScale;
                half   _StrokeStretch;
                half   _StrokeAmp;
                half   _MottleScale;
                half   _OutlineWidth;
                half4  _OutlineColor;
                half   _OutlineDistScale;
                half   _OutlineFacing;
                half   _OutlineDry;
                half   _OutlineScale;
                half   _ContactInk;
                half   _ContactHeight;
                half   _GroundY;
                half   _AerialFrom;
                half   _AerialTo;
                half   _AerialStrength;
                half4  _RimColor; half _RimPower; half _RimStrength;
                half   _SpecBands; half _SpecStrength; half _SpecSize;
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
                // URP 的 _CameraNormalsTexture 存的是**世界空间**法线（RGB 编码在 0..1）
                half3 n = normalize(input.normalWS);
                return half4(n, 0.0h);
            }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Lit"
}
