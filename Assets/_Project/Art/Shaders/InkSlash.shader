// InkSlash.shader —— 挥墨刀光（弧光扇面 + 刀锋拖尾共用）
//
// ★★ 为什么必须补上这个 Shader ★★
//   SwordVfx.CreateInkMaterial 的老代码是这么写的：
//       var shader = inkShader;                       // 序列化字段，预制体上是空的
//       if (shader == null) shader = Shader.Find("InkWash/InkSlash");
//       if (shader != null) custom = true;
//       if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");   // ← 实际走的是这条
//   而 `InkWash/InkSlash` **从来就没存在过**（Art/Shaders 下没有这个文件）。
//   于是刀光一直在用 **URP/Unlit** 兜底，它有一个致命问题：
//     **不支持顶点色**。而弧面的整个"笔锋"全靠顶点色（内外缘归零、中脊最浓、两端收笔，
//     见 SwordVfx.BuildArcMesh）。顶点色被丢弃后，整块面按材质 alpha 均匀铺满 ——
//     屏幕上是一块**剪影式的硬边几何色块**。用户原话「没有挥墨的那种效果感觉」。
//   本 Shader 就是补上这一环：让顶点色真正常与 alpha 运算。
//
// ★★ 飞白的两版教训（都记在这里，别再走回去）★★
//   第一版：用 `hash` 噪声 + **`clip(a - 阈值)`** 打洞。
//     结果屏幕上是一排**等距梳齿** —— 因为拉丝噪声频率极高（_StreakScale=44），
//     每个噪声格都被 clip 切成一个齿，整笔看起来像把梳子，不是墨。
//   第二版（当前）：**不做任何硬裁剪**，只做**低频、柔和**的浓度调制，
//     并且调制系数**只在 [0,1] 内往下压、绝不放大**。
//     理由：墨迹的干湿是"浓淡变化"，不是"有无洞"；硬裁剪一定会暴露采样网格的周期性。
//   同理，跨笔方向的软边交给**网格顶点色 + alpha 混合**去做（那是连续插值），
//   Shader 只负责在此基础上叠加一点有机的浓淡起伏。
//
// 【为什么噪声用 object space 坐标】
//   弧光实例的变换是"位置 + 滚转"，顶点在物体空间里稳定；用 `positionOS.xy` 做输入，
//   噪声就**粘在笔迹上**，不随角色/相机移动而游动（用世界坐标会像贴在镜头上的一层膜）。
//   TrailRenderer 的顶点同样在各自的物体空间里，一套代码对两者都成立。
//
// 【渲染状态】
//   Cull Off —— 弧面是单层薄片，正面朝向取决于滚转角；关背面剔除省得某一侧看不见。
//   ZWrite Off + Queue Transparent —— 半透明特效不该写深度，否则会把角色切出硬边。
Shader "InkWash/InkSlash"
{
    Properties
    {
        _BaseColor ("墨色", Color) = (0.045, 0.050, 0.068, 0.96)
        _FlyingWhite ("飞白强度（只压不抬，0 = 纯净平涂）", Range(0, 1)) = 0.22
        _NoiseScale ("浓淡起伏尺度（低频）", Float) = 5
        _StreakScale ("顺笔拉丝尺度（沿笔迹拉长）", Float) = 7
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "InkSlash"
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            Lighting Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float  _FlyingWhite;
                float  _NoiseScale;
                float  _StreakScale;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color      : COLOR;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float4 color       : COLOR;
                float2 os          : TEXCOORD0;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.color = IN.color;
                OUT.os = IN.positionOS.xy;
                return OUT;
            }

            float hash21(float2 p)
            {
                p = frac(p * float2(123.34, 345.45));
                p += dot(p, p + 34.345);
                return frac(p.x * p.y);
            }

            float vnoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = hash21(i);
                float b = hash21(i + float2(1.0, 0.0));
                float c = hash21(i + float2(0.0, 1.0));
                float d = hash21(i + float2(1.0, 1.0));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // ★ 顶点色参与 alpha —— 这是整个修复的核心。老代码用 URP/Unlit 时它被完全忽略，
                //   于是"内外缘归零 / 中脊最浓 / 两端收笔"这套笔锋全部失效。
                float a = _BaseColor.a * IN.color.a;

                float2 os = IN.os;

                // 顺笔拉丝：噪声坐标在**切向**略密、**径向**拉长 → 形成顺着走势的细长笔痕。
                // 尺度必须**低**（个位数）：高频会变成一层规则的格子，一眼假。
                float n1 = vnoise(os * float2(_StreakScale, _NoiseScale * 0.45));
                float n2 = vnoise(os * _NoiseScale + 17.3);
                float n = saturate(n1 * 0.6 + n2 * 0.6);       // ≈[0.15, 1]

                // 只往下压、不往上抬：墨的浓度可以变淡，但不能被噪声"点亮"。
                // 下限留 0.62，保证整笔始终是连续的墨，不会碎成洞。
                float ink = lerp(1.0, lerp(0.62, 1.0, n), _FlyingWhite);
                a *= ink;

                return half4(_BaseColor.rgb, saturate(a));
            }
            ENDHLSL
        }
    }

    FallBack Off
}
