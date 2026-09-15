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

        // ---- Ink 墨色与阶数 ----
        _InkColor ("墨色（暗部，忌纯黑）", Color) = (0.055, 0.063, 0.086, 1)
        _PaperColor ("纸色（受光，忌纯白）", Color) = (0.847, 0.812, 0.741, 1)
        _Bands ("墨阶数（少=大写意）", Range(1, 8)) = 4
        _BandSoftness ("阶间柔度", Range(0.001, 0.5)) = 0.06
        _InkDensity ("墨的浓度（越大越留白，越接近纯纸色）", Range(0, 1)) = 0.75

        // ---- Brush 飞白 ----
        _BrushTex ("飞白噪声（灰度图）", 2D) = "gray" {}
        _BrushScale ("飞白尺度（越大越细）", Float) = 26
        _BrushStrength ("飞白强度（0=光滑卡通）", Range(0, 1)) = 0.6
        _BrushUvFromWorld ("飞白按世界坐标（关=按 UV）", Range(0, 1)) = 1

        // ---- Rim 轮廓墨 ----
        _RimColor ("轮廓墨色", Color) = (0.08, 0.09, 0.12, 1)
        _RimPower ("轮廓收束（越大越窄）", Range(0.5, 12)) = 3.2
        _RimStrength ("轮廓强度", Range(0, 2)) = 0.85

        // ---- Specular 湿墨高光 ----
        _SpecBands ("高光阶数", Range(1, 6)) = 2
        _SpecStrength ("高光强度", Range(0, 2)) = 0.5
        _SpecSize ("高光收束", Range(1, 200)) = 48
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
                half4  _InkColor;
                half4  _PaperColor;
                half   _Bands;
                half   _BandSoftness;
                half   _InkDensity;
                float4 _BrushTex_ST;
                half   _BrushScale;
                half   _BrushStrength;
                half   _BrushUvFromWorld;
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
                if (steps < 0.5h) return 1.0h;              // 单阶：全亮（纯平涂）
                half scaled = saturate(x) * steps;
                half lower  = floor(scaled);
                half frac   = scaled - lower;
                // 柔度：在阶的边界附近做一点点过渡，避免完全硬边（硬到极致会有锯齿抖动）
                half k = smoothstep(0.5h - softness * steps, 0.5h + softness * steps, frac);
                return saturate((lower + k) / steps);
            }

            /// 飞白噪声：叠两层不同尺度的哈希噪声，避免看出来是"一张贴图在滑动"
            half BrushNoise(float3 posWS, float2 uv)
            {
                float2 coord = lerp(uv * _BrushScale, posWS.xz * _BrushScale * 0.35h + posWS.y * _BrushScale * 0.31h, _BrushUvFromWorld);
                half a = SAMPLE_TEXTURE2D(_BrushTex, sampler_BrushTex, coord * 0.31h).r;
                half b = SAMPLE_TEXTURE2D(_BrushTex, sampler_BrushTex, coord * 0.73h + 0.37h).r;
                return saturate(a * 0.65h + b * 0.35h);
            }

            half4 InkFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                half3 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).rgb * _BaseColor.rgb;
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

                // ---- ★ 飞白：抖动**阈值**，不是抖动颜色 ----
                half noise = BrushNoise(input.positionWS, input.uv);
                half jitter = (noise - 0.5h) * _BrushStrength * 0.55h;
                half lit = saturate(lambert + ambient * 0.35h + jitter);
                half ramp = Quantize(lit, _Bands, _BandSoftness);

                // ---- 着色：墨色（暗） → 纸色×固有色的墨韵（亮） ----
                // ★ 固有色的掺入量直接由 _InkDensity 控制（浓度越高越接近纯纸色 = 留白）。
                //   上一版写成 `lerp(1, albedo, 1 - _InkDensity*0.35)` 之后**又乘了一次 albedo**，
                //   等于把贴图算成 albedo²：亮部整体发暗、而且无论怎么调色都"不像水墨"。
                half3 paperTint = _PaperColor.rgb * lerp(half3(1,1,1), albedo, saturate(1.0h - _InkDensity));
                half3 shadowColor = lerp(_InkColor.rgb, _InkColor.rgb * 0.85h + albedo * 0.18h, _InkDensity * 0.6h);
                half3 color = lerp(shadowColor, paperTint, ramp);

                // ---- 轮廓墨（Fresnel）：让角色在任何背景上都有"剪纸边" ----
                half fresnel = pow(saturate(1.0h - dot(normalWS, viewDirWS)), _RimPower);
                color = lerp(color, _RimColor.rgb, saturate(fresnel * _RimStrength));

                // ---- 湿墨高光：同样量化成几阶，避免出现连续的塑料反光 ----
                half3 halfDir = normalize(lightDir + viewDirWS);
                half spec = pow(saturate(dot(normalWS, halfDir)), _SpecSize);
                spec *= mainLight.shadowAttenuation * atten;
                half specStep = Quantize(spec, _SpecBands, 0.02h);
                // 只给"亮部"加高光：暗部加高光会变成塑料
                color += specStep * _SpecStrength * smoothstep(0.55h, 1.0h, ramp) * _PaperColor.rgb;

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
            // ★ 必须显式包含核心包的 CommonMaterial.hlsl：Shadows.hlsl 用到 LerpWhiteTo，
            //   但它自己只 include 了 Common.hlsl（LerpWhiteTo 在 CommonMaterial.hlsl 里）。
            //   官方 Lit.shader 之所以不报错，是因为它先 include 了 SurfaceInput.hlsl 顺带带进来。
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4  _BaseColor; half4 _InkColor; half4 _PaperColor;
                half   _Bands; half _BandSoftness; half _InkDensity;
                float4 _BrushTex_ST; half _BrushScale; half _BrushStrength; half _BrushUvFromWorld;
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
                half4  _BaseColor; half4 _InkColor; half4 _PaperColor;
                half   _Bands; half _BandSoftness; half _InkDensity;
                float4 _BrushTex_ST; half _BrushScale; half _BrushStrength; half _BrushUvFromWorld;
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
