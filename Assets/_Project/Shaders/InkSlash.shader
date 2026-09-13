// 水墨刀光专用 Shader（URP）
//
// 为什么必须自研：URP 内置的 Unlit 着色器**完全不读顶点色** ——
// 它的 Attributes/Varyings 里根本没有 COLOR 语义（见
// Packages/com.unity.render-pipelines.universal/Shaders/UnlitForwardPass.hlsl）。
// 而本项目的刀光有两处依赖顶点色：
//   1) 拖尾（TrailRenderer）的墨色/透明度沿轨迹渐隐，靠的是顶点色
//   2) 弧光网格在两端"收笔"、由内圈到外圈渐隐，也靠顶点色 alpha
// 用 URP/Unlit 的结果是渐变全部失效，弧光变成一坨硬边黑块。
//
// 所以这里写一支最小的 Unlit + 顶点色 + 透明混合的着色器。
// 另外加了一点点"飞白"：用世界坐标上的哈希噪声在 alpha 上做高频扰动，
// 让边缘不是数学上完美的曲线，更接近毛笔的干笔触感（_FlyingWhite 控制强度）。
Shader "InkWash/InkSlash"
{
    Properties
    {
        [MainColor] _BaseColor ("Ink Color", Color) = (0.06, 0.06, 0.08, 0.92)
        [MainTexture] _BaseMap ("Base Map", 2D) = "white" {}
        _FlyingWhite ("Flying White (飞白)", Range(0, 1)) = 0.35
        _FlyingWhiteScale ("Flying White Scale", Range(1, 80)) = 26
        _Intensity ("Intensity", Range(0, 3)) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
            "PreviewType" = "Plane"
        }

        Pass
        {
            Name "InkSlashUnlit"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            Lighting Off

            HLSLPROGRAM
            #pragma vertex InkVert
            #pragma fragment InkFrag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                float4 color      : COLOR;      // ← URP/Unlit 缺的就是这一行
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float4 color      : COLOR;
                float3 positionWS : TEXCOORD1;
                float  fogCoord   : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _BaseMap_ST;
                float  _FlyingWhite;
                float  _FlyingWhiteScale;
                float  _Intensity;
            CBUFFER_END

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            Varyings InkVert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                VertexPositionInputs p = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = p.positionCS;
                output.positionWS = p.positionWS;
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.color = input.color;
                output.fogCoord = ComputeFogFactor(p.positionCS.z);
                return output;
            }

            // 便宜的三维哈希，用来做飞白的高频扰动
            float InkHash13(float3 p)
            {
                p = frac(p * 0.1031);
                p += dot(p, p.yzx + 33.33);
                return frac((p.x + p.y) * p.z);
            }

            half4 InkFrag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);

                half3 rgb = _BaseColor.rgb * tex.rgb * input.color.rgb * _Intensity;
                half  a   = _BaseColor.a   * tex.a   * input.color.a;

                // 飞白：沿线方向的高频噪声，越靠笔触边缘越明显
                // （用顶点色 alpha 当作"笔压"，笔压越低越容易干笔）
                if (_FlyingWhite > 0.001h)
                {
                    float n = InkHash13(input.positionWS * _FlyingWhiteScale);
                    half dry = lerp(1.0h, n, _FlyingWhite * (1.0h - saturate(input.color.a)));
                    a *= saturate(dry + 0.15h);
                }

                rgb = MixFog(rgb, input.fogCoord);
                return half4(rgb, a);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
