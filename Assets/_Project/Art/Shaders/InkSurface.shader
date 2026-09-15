// InkSurface.shader —— 场景水墨着色器（墙 / 地面 / 柱子 / 平台）
//
// 为什么需要它（用户反馈"水墨风有点奇怪，像是普通积木玩偶一样，不够白啊，有点发灰"）：
//   场景里 14 个几何体此前全用 **URP/Lit + 灰白盒材质**（0.55~0.8 的中灰）。
//   灰 + 连续 PBR 明暗 + 干净棱边 = 一眼就是"积木盒子"，跟水墨没有任何关系。
//   角色再水墨也救不回来 —— **画面的中间调是被场景决定的**，人物只占几个百分点像素。
//
// 与 InkCharacter 的分工：
//   · InkCharacter 是**主体**：墨色重、有轮廓墨、有湿墨高光，用来"写字"。
//   · InkSurface   是**纸**：整体纸白、只有淡墨阶，用来"留白"。
//   水墨画里"纸比墨多"，所以场景必须比角色**淡得多** —— 这是本文件最重要的取向。
//
// 四个技术点：
//   ① **纸白基调**：受光色接近纯白（0.94），暗部是**淡墨**（0.42 冷灰蓝）而不是黑。
//      直接给暗部上黑会闷死整个空间；大面积深色不属于水墨画。
//   ② **少阶量化**（默认 3 阶）：阶数比角色更少 —— 台阶越少，"一笔刷过"的味道越重。
//      ⚠ 阶数与柔度必须一起调：柔度太小会在斜面上出现严重的马赫带（等值线），
//      地面这种大面积斜面尤其明显。
//   ③ **飞白 + 积墨**：两层世界空间噪声。
//      飞白抖动**阈值**（制造毛糙的墨阶边界），积墨抖动**颜色**（低洼处颜色略深）。
//      前者是"笔锋擦过"，后者是"时间久了渗进去的一片"，两者叠加才不像贴图在滑动。
//   ④ **高度留白**：越高的地方越淡，把墙的顶部推向纸白 —— 山水画的"远山淡墨"。
//      也是把封闭竞技场从"盒子"变成"雾中空间"的最省事手段。
Shader "InkWash/InkSurface"
{
    Properties
    {
        // 注意：本工程用**团结引擎**，其 ShaderLab 解析器在属性上遇到
        // [Header(...)] / [Space] 装饰器会直接报 Parse error，因此一个都不用，分组只靠注释。
        [MainTexture] _BaseMap ("基础贴图（漫反射）", 2D) = "white" {}
        [MainColor]   _BaseColor ("基础色", Color) = (1, 1, 1, 1)

        // ---- 墨与纸 ----
        _InkColor ("淡墨（暗部，忌纯黑）", Color) = (0.42, 0.44, 0.48, 1)
        _PaperColor ("纸白（受光）", Color) = (0.94, 0.93, 0.9, 1)
        _Bands ("墨阶数（少=大写意）", Range(1, 6)) = 3
        _BandSoftness ("阶间柔度", Range(0.001, 0.5)) = 0.14
        _InkDensity ("墨的浓度（越大越接近纯纸白）", Range(0, 1)) = 0.55

        // ---- 飞白 ----
        _BrushTex ("飞白噪声（灰度图）", 2D) = "gray" {}
        _BrushScale ("飞白尺度（越大越细）", Float) = 9
        _BrushStrength ("飞白强度", Range(0, 1)) = 0.5

        // ---- 积墨（世界坐标低频斑驳） ----
        _InkMottle ("积墨（地面斑驳）", Range(0, 1)) = 0.22
        _MottleScale ("积墨尺度", Float) = 0.35

        // ---- 高度留白 ----
        _HeightFade ("高处留白强度", Range(0, 1)) = 0.18
        _HeightFrom ("留白起点高度", Float) = 1.0
        _HeightTo ("留白终点高度", Float) = 5.0

        // ---- 环境方向冷调 ----
        _AmbientTint ("环境色（略偏冷）", Color) = (0.9, 0.93, 1.0, 1)
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
                half4  _InkColor;
                half4  _PaperColor;
                half   _Bands;
                half   _BandSoftness;
                half   _InkDensity;
                float4 _BrushTex_ST;
                half   _BrushScale;
                half   _BrushStrength;
                half   _InkMottle;
                half   _MottleScale;
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

            /// 两层不同尺度的哈希噪声。单层看得出来是"一张贴图在滑"，两层才有织理。
            half Noise2(float3 posWS, float2 uv, half scale, half offset)
            {
                float2 c = posWS.xz * scale + posWS.y * scale * 0.37h + offset;
                half a = SAMPLE_TEXTURE2D(_BrushTex, sampler_BrushTex, c * 0.61h).r;
                half b = SAMPLE_TEXTURE2D(_BrushTex, sampler_BrushTex, c * 1.43h + 0.31h).r;
                return saturate(a * 0.62h + b * 0.38h);
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

                // ---- 飞白：抖动**阈值**，不抖颜色（抖颜色只会得到"脏"） ----
                half brush = Noise2(input.positionWS, input.uv, _BrushScale * 0.35h, 0.0h);
                half jitter = (brush - 0.5h) * _BrushStrength * 0.5h;

                half lit = saturate(lambert + ambient * 0.55h + jitter);
                half ramp = Quantize(lit, _Bands, _BandSoftness);

                // ---- 高度留白：越高越推向纸白（远山淡墨） ----
                half h = saturate((input.positionWS.y - _HeightFrom) / max(_HeightTo - _HeightFrom, 0.01h));
                ramp = lerp(ramp, 1.0h, h * _HeightFade);

                // ---- 着色 ----
                half3 paperTint = _PaperColor.rgb * lerp(half3(1,1,1), albedo, saturate(1.0h - _InkDensity));
                half3 inkTint   = _InkColor.rgb * lerp(half3(1,1,1), albedo, 0.35h);
                half3 color = lerp(inkTint, paperTint, ramp);

                // 环境方向冷调：让受环境光照亮的暗部带一点冷，避免整体发黄发闷
                color *= lerp(half3(1,1,1), _AmbientTint.rgb, (1.0h - ramp) * 0.5h);

                // ---- 积墨：低洼/背光处一片片渗进去的深色 ----
                half mottle = Noise2(input.positionWS, input.uv, _MottleScale, 3.7h);
                half mottleMask = saturate(mottle - 0.5h) * 2.0h * _InkMottle * (1.0h - ramp * 0.55h);
                color = lerp(color, _InkColor.rgb * 1.15h, mottleMask);

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
                half4  _BaseColor; half4 _InkColor; half4 _PaperColor;
                half   _Bands; half _BandSoftness; half _InkDensity;
                float4 _BrushTex_ST; half _BrushScale; half _BrushStrength;
                half   _InkMottle; half _MottleScale;
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
                half4  _BaseColor; half4 _InkColor; half4 _PaperColor;
                half   _Bands; half _BandSoftness; half _InkDensity;
                float4 _BrushTex_ST; half _BrushScale; half _BrushStrength;
                half   _InkMottle; half _MottleScale;
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
