// InkSplash.shader —— 溅墨 / 墨花用的墨点面片（透明，程序化形状）
//
// 论文 · E5。为什么不用贴图：
//   溅墨要的是"每一滴都不一样" —— 用一张贴图只能靠随机旋转/缩放来制造变化，
//   旋转多了会被看出是同一张图（尤其墨点这种高对比形状）。
//   这里改用**角度域的噪声调制半径**（下颌线 r/wob(a)），每次生成给一个 different seed，
//   形状就真的不同；同时省掉一张会被放大的贴图（放大必糊）。
//
// 两个关键取舍：
//   a) **边缘要"实"**：水墨点在宣纸上是"积"出来的一坨，边缘比光晕锐利得多。
//      所以 smoothstep 的过渡带很窄（0.86→1.0），而不是普通粒子的柔和衰减 ——
//      柔边会看起来像"发光点"，那就成霓虹灯了。
//   b) **不做软粒子/深度写入**：ZWrite Off + 不采样深度，避免与"墨线 Pass"抢深度。
//      代价是穿墙时会露出来，但本项目相机不会贴墙，收益远大于代价。
Shader "InkWash/InkSplash"
{
    Properties
    {
        _Color ("墨色", Color) = (0.055, 0.062, 0.082, 1)
        _Alpha ("透明度", Range(0, 1)) = 1
        _Seed ("形状种子", Range(0, 32)) = 0
        _Irregular ("不规则度", Range(0, 1)) = 0.45
        _CoreHold ("墨心保持（越大越像积墨）", Range(0, 1)) = 0.55
        _DryEdge ("飞白边缘", Range(0, 1)) = 0.3
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent+20"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        ZTest LEqual
        Cull Off

        Pass
        {
            Name "InkSplash"

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half  _Alpha;
                half  _Seed;
                half  _Irregular;
                half  _CoreHold;
                half  _DryEdge;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            // FXC 不认表达式体函数（`=>`），一律写块体
            Varyings Vert(Attributes input)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.uv = input.uv;
                return o;
            }

            float Hash11(float x)
            {
                return frac(sin(x * 91.3458) * 47453.5453);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                // UV → 以中心为原点的 [-1,1] 圆域
                float2 p = input.uv * 2.0 - 1.0;
                float r = length(p);

                // 角度（0..1 归一化，避免 atan2 的符号跳变带来接缝）
                float ang = atan2(p.y, p.x);
                float an = ang * 0.1591549 + 0.5;     // /(2π)

                // ---- 形状：半径被角度噪声调制（3 个不同频率叠加 = 不规则的墨滴轮廓）----
                float s = _Seed;
                float wob = 1.0
                    + _Irregular * 0.34 * sin(ang * 3.0 + s * 2.1)
                    + _Irregular * 0.22 * sin(ang * 5.0 - s * 3.7 + 1.3)
                    + _Irregular * 0.13 * sin(ang * 8.0 + s * 5.9 + 2.7);

                float d = r / max(wob, 0.35);

                // ---- 墨点本体：核心保持 + 很窄的实边（见头注 a）----
                half core = 1.0h - smoothstep(_CoreHold, 1.0h, d);
                half body = 1.0h - smoothstep(0.86h, 1.0h, d);

                // ---- 飞白边缘：墨快干时边缘出现的断续露白 ----
                float streak = Hash11(floor(an * 26.0) + s * 7.3);
                half dry = lerp(1.0h, step(0.30h, streak), _DryEdge);
                half edgeZone = saturate((d - _CoreHold) / max(1.0h - _CoreHold, 0.001h));

                half a = saturate(body * lerp(1.0h, dry, edgeZone) + core * 0.25h);

                // 整体淡出（C# 每帧写 _Alpha），并保留一点"墨心"不让它整体消失得太干净
                a *= _Alpha;
                if (a <= 0.003h) discard;

                return half4(_Color.rgb, saturate(a) * _Color.a);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
