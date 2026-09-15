// InkSky.shader —— 水墨天空盒
//
// 为什么必须换掉 Default-Skybox：它是**蓝色渐变**。一张水墨画里出现蓝天，
// 前面所有的纸白、墨线、飞白全部白搭 —— 天空占画面近一半像素，它是最强的风格信号。
//
// 水墨画里的"天"只有两件事：
//   ① **留白**：绝大部分是纸色，只有极淡的一点点上下层次（不是渐变到深蓝）
//   ② **远山**：贴着地平线的一道淡墨剪影。它同时解决了"白天空 + 白墙分不开"的问题 ——
//      没有远山，天空与远处的墙会糊成一片，空间感直接消失。
//
// 山的形状用三层不同频率的正弦按方位角叠加。**不用噪声贴图**：
//   天空盒是立方体贴到球面上的，贴图会因为立方体→球面的重映射在面接缝处出现明显的拉伸；
//   而正弦是方位角的连续函数，绕一圈天然接得上，山里也不会出现"贴图接缝"。
//
// 参数刻意做得很少：天空是配角，能调的地方越多越容易调坏。
Shader "InkWash/InkSky"
{
    Properties
    {
        _HorizonColor ("地平线（纸白）", Color) = (0.966, 0.963, 0.952, 1)
        _ZenithColor ("天顶（淡墨）", Color) = (0.862, 0.874, 0.888, 1)
        _GradientPow ("渐变收束（越大天顶越集中）", Range(0.3, 4)) = 1.30

        _MountainColor ("远山（淡墨）", Color) = (0.706, 0.730, 0.770, 1)
        _MountainStrength ("远山强度", Range(0, 1)) = 0.58
        _MountainBase ("远山基准高度", Range(-0.05, 0.35)) = 0.005
        _MountainHeight ("远山起伏", Range(0, 0.35)) = 0.135
        _MountainSharp ("山脊收束（越大越利）", Range(20, 500)) = 180

        _BrushTex ("飞白噪声", 2D) = "gray" {}
        _BrushScale ("飞白尺度", Float) = 6
        _DryStrength ("飞白强度（0=完全平涂）", Range(0, 1)) = 0.16
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Background"
            "RenderType" = "Background"
            "PreviewType" = "Skybox"
            "RenderPipeline" = "UniversalPipeline"
        }
        Cull Off
        ZWrite Off
        ZTest LEqual

        Pass
        {
            Name "InkSkyPass"

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex InkSkyVert
            #pragma fragment InkSkyFrag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4  _HorizonColor;
                half4  _ZenithColor;
                half   _GradientPow;
                half4  _MountainColor;
                half   _MountainStrength;
                half   _MountainBase;
                half   _MountainHeight;
                half   _MountainSharp;
                float4 _BrushTex_ST;
                half   _BrushScale;
                half   _DryStrength;
            CBUFFER_END

            TEXTURE2D(_BrushTex);  SAMPLER(sampler_BrushTex);

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 dirOS      : TEXCOORD0;
            };

            Varyings InkSkyVert(Attributes input)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                // 天空盒的顶点位置本身就是方向向量（立方体中心在原点）
                o.dirOS = input.positionOS.xyz;
                return o;
            }

            /// 一层山脊：方位角 → 山脊高度。三层正弦叠加，绕一圈处处连续（见头注）。
            half MountainMask(float az, half up, half heightScale)
            {
                half r = 0.0h;
                r += sin(az * 1.7h + 0.7h) * 0.50h;
                r += sin(az * 2.9h + 2.3h) * 0.28h;
                r += sin(az * 5.1h + 4.1h) * 0.14h;
                r = saturate(r * 0.5h + 0.5h);

                half ridgeY = (_MountainBase + r * _MountainHeight * heightScale);
                // up 低于山脊 → 山体（1）；高于山脊 → 天（0）
                return smoothstep(ridgeY, ridgeY - 1.0h / max(_MountainSharp, 1.0h), up);
            }

            half4 InkSkyFrag(Varyings input) : SV_Target
            {
                half3 dir = normalize(input.dirOS);
                half up = dir.y;

                // ---- ① 上下层次：地平线纸白 → 天顶淡墨 ----
                half3 col = lerp(_HorizonColor.rgb, _ZenithColor.rgb,
                                 pow(saturate(up), _GradientPow));

                // ---- ② 飞白：让天不是一块干净色（平涂会显得像塑料） ----
                half dry = SAMPLE_TEXTURE2D(_BrushTex, sampler_BrushTex,
                                            dir.xz * _BrushScale).r;
                col *= lerp(1.0h, 0.94h + 0.10h * dry, _DryStrength);

                // ---- ③ 远山：三层，越远越淡越高 ----
                float az = atan2(dir.x, dir.z);
                half nearM = MountainMask(az, up, 1.0h);
                half farM  = MountainMask(az * 1.63h + 2.10h, up, 1.55h);

                col = lerp(col, _MountainColor.rgb,
                           farM * _MountainStrength * 0.45h);
                col = lerp(col, _MountainColor.rgb * 0.90h,
                           nearM * _MountainStrength);

                return half4(col, 1.0h);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
