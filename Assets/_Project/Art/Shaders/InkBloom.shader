// InkBloom.shader —— 墨晕扩散后处理（Hidden：只给 RendererFeature 用）
//
// 论文 · E4。这是三层里唯一"反物理"的一层，也正因如此最需要在论文里讲清楚：
//
//   **它长得像 Bloom，但机理与 Bloom 相反。**
//   图形学的 Bloom 模拟的是光学现象：**亮的**东西把光溢到周围（镜头/视网膜的散射），
//   所以阈值取"亮度"、结果是**加法**（画面变亮）。
//   水墨的"墨晕"则是**纸纤维吸水**导致的墨向外渗：
//     · 参与晕开的是**暗部**（墨黑），亮部（留白）根本不渗 —— 阈值取"暗度"（1 − luma）；
//     · 结果是**减法**（把周围压暗），而不是加法发光；
//     · 渗出的墨**离得越远越淡**，且衰减比高斯更快（纸吸饱了就停），
//       所以用两级半径取 max 而不是多级高斯相加 —— 相加会让中间调整体发灰，
//       那正是"脏"，而不是"润"。
//   照抄 Bloom 会得到一个发光的水墨画，方向是反的。
//
// 与 E2/E3 的顺序（见 InkStyleRegistry 头注）：墨线（笔）→ 宣纸（纸）→ 墨晕（渗）。
// 墨晕必须最后 —— 否则渗开的墨会被后画的墨线重新切出硬边，看起来像"描边外又套了一圈描边"。
//
// 三个实现细节：
//   a) 环形采样而不是可分离高斯：晕开的形状基本各向同性，但**纸纤维有方向性**，
//      所以对采样角度加一点按像素块哈希的抖动（_Jitter）。抖动幅度必须很小（<0.25 弧度），
//      大了会变成"放射状拉丝"，看起来像镜头脏而不像纸。
//   b) 两级半径取 max：近场浓、远场淡。取 max 保住了"近场不因远场而变淡"。
//   c) 暗部本身不能因为晕开而变亮：最后一步是**乘性压暗**不是加法，
//      否则画面中央会浮起一层灰雾。
Shader "Hidden/InkWash/InkBloom"
{
    Properties
    {
        _InkColor ("晕的墨色", Color) = (0.10, 0.11, 0.14, 1)
        _Threshold ("暗度阈值（只晕比这更暗的地方）", Range(0, 1)) = 0.35
        _Strength ("晕的强度", Range(0, 2)) = 0.55
        _Radius1 ("近场半径（像素）", Range(0.5, 24)) = 3.0
        _Radius2 ("远场半径（像素）", Range(1, 64)) = 9.0
        _FarWeight ("远场权重", Range(0, 1)) = 0.55
        _Jitter ("纸纤维抖动（弧度）", Range(0, 0.6)) = 0.12
        _TintAmount ("晕的墨色掺入量", Range(0, 1)) = 0.35
        _Veil ("整体墨雾（0 = 只晕暗部边缘）", Range(0, 1)) = 0.0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZTest Always ZWrite Off Cull Off

        Pass
        {
            Name "InkBloom"

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _InkColor;
                half  _Threshold;
                half  _Strength;
                half  _Radius1;
                half  _Radius2;
                half  _FarWeight;
                half  _Jitter;
                half  _TintAmount;
                half  _Veil;
            CBUFFER_END

            float4 _InkTexelSize;      // xy = 1/宽高，zw = 宽高（由 C# 每帧传）

            // 不能写 `float Hash21(float2 p) => ...` —— D3D11 走 FXC，不认表达式体函数
            float Hash21(float2 p)
            {
                return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
            }

            /// 输入的"可晕量"：只有比阈值更暗的像素才参与扩散，且归一化到 0..1
            half InkAmount(float2 uv)
            {
                half3 c = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).rgb;
                half luma = dot(c, half3(0.299h, 0.587h, 0.114h));
                return saturate((1.0h - luma - _Threshold) / max(1.0h - _Threshold, 0.001h));
            }

            /// 环形采样的扩散场。两圈 × 8 方向，两圈角度错开 1/16 圈避免出现放射状条纹。
            half SpreadField(float2 uv, float radiusPx, float angleOffset)
            {
                half acc = 0.0h;
                float2 step = _InkTexelSize.xy * radiusPx;

                [unroll]
                for (int ring = 0; ring < 2; ring++)
                {
                    float r = (ring == 0) ? 1.0 : 1.85;
                    half w = (ring == 0) ? 1.0h : 0.62h;

                    [unroll]
                    for (int i = 0; i < 8; i++)
                    {
                        float a = (i * 0.125 + ring * 0.0625) * 6.2831853 + angleOffset;
                        float2 dir = float2(cos(a), sin(a)) * step * r;
                        acc += InkAmount(uv + dir) * w;
                    }
                }
                return acc / 12.96h;    // 8 × (1.0 + 0.62)
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;

                half3 scene = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).rgb;

                // 纸纤维的方向抖动：按 4×4 像素块哈希，同一块内方向一致
                // （逐像素哈希会得到椒盐噪声，看起来像坏点；按块才像纤维）
                float2 px = uv * _InkTexelSize.zw;
                float jitter = (Hash21(floor(px * 0.25)) - 0.5) * _Jitter * 2.0;

                half near = SpreadField(uv, _Radius1, jitter);
                half far = SpreadField(uv, _Radius2, jitter + 0.4);
                // 取 max：近场浓、远场淡，且近场不会因为远场被拉淡
                half field = max(near, far * _FarWeight);
                field = saturate(field + _Veil);

                half amount = saturate(field * _Strength);
                if (amount <= 0.0015h) return half4(scene, 1.0h);

                // ★ 乘性压暗（不是加法）：加法会让暗部整体浮起一层灰雾，
                //   那是"脏"，而水墨要的是"润" —— 墨渗开之后，周围是被染暗的。
                half3 darkened = scene * (1.0h - amount);
                // 再掺一点墨色，让晕有"色相"而不只是"变黑"
                darkened = lerp(darkened, darkened * _InkColor.rgb * 2.0h, amount * _TintAmount);

                return half4(darkened, 1.0h);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
