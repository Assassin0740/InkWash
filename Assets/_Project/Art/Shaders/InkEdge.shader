// InkEdge.shader —— 飞白墨线（屏幕空间边缘提取，Hidden：只给 RendererFeature 用）
//
// 论文 · E2。与普通描边的区别在最后一步：
//   ① 深度梯度 → **轮廓**（物体/背景、物体/物体之间）
//   ② 法线梯度 → **结构折痕**（同一物体内部的转折：深度几乎不变，只有法线在变）
//   ③ 合成"墨线强度"
//   ④ ★ **飞白**：用噪声调制墨线的**浓淡**，低过阈值就断笔 ——
//      这就是毛笔"走笔快、墨少、露白"的飞白。等宽实线看起来像马克笔，加飞白才像毛笔。
//
// 三个必须处理的细节（都踩过）：
//   a) **天空要屏蔽法线**：`_CameraNormalsTexture` 在天空处是 (0,0,1)，如果不屏蔽，
//      地平线上会出现一条假的法线梯度线。但**不能**把"中心是天空"整片屏蔽 ——
//      角色剪影正是靠在天空一侧出线的，那才是我们最想要的墨线。
//      正解：法线项只在"两侧都是实体几何"时才计入；天空剪影交给深度项。
//   b) **深度用视角空间 Z，不用 raw depth，且用二阶差分**：raw 在透视下是倒数分布，
//      近处一点点差别就能让梯度爆炸。更致命的是**一阶差分在掠射角地面上恒为正** ——
//      地面本身就是一条深度斜坡，于是整片地面被判成轮廓（实测：地上长出一片假纸纹）。
//      正解是 Roberts 式的 |dL − dR|：斜坡被减掉，只有物体边界的"断崖"留得下来。
//      再除以本像素深度做归一化 —— 否则远处同一世界尺度的变化会被放大。
//   c) **纹素尺寸靠 C# 显式传入**：`_BlitTexture_TexelSize` 不会被 Blitter 自动填，
//      依赖它会在某些分辨率下静默拿到 0。
Shader "Hidden/InkWash/InkEdge"
{
    Properties
    {
        _EdgeColor ("墨线颜色", Color) = (0.04, 0.045, 0.06, 1)
        _DepthSensitivity ("深度灵敏度", Range(0.1, 60)) = 22
        _DepthBias ("深度死区（压掉掠射角面上的假线，取大值优先）", Range(0, 6)) = 2.0
        _NormalSensitivity ("法线灵敏度", Range(0.1, 4)) = 0.9
        _NormalBias ("法线死区", Range(0, 1)) = 0.06
        _LineThickness ("线宽（像素采样步长）", Range(0.5, 4)) = 1.0
        _LineStrength ("墨线强度", Range(0, 3)) = 1.6

        _DryBrushTex ("飞白噪声", 2D) = "gray" {}
        _DryBrushScale ("飞白尺度", Float) = 7
        _DryBrushStrength ("飞白强度（0=等宽实线）", Range(0, 1)) = 0.72
        _DryBrushBias ("断笔阈值", Range(0, 1)) = 0.34

        _DistanceFade ("远处淡出（米）", Float) = 46
        _DistanceFadeSharp ("淡出曲线", Range(0.5, 8)) = 2.0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZTest Always ZWrite Off Cull Off

        Pass
        {
            Name "InkEdge"

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _CAMERA_DEPTH_TEXTURE

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            // 采样器必须用 Unity 认得的内建名（sampler_LinearClamp 等，由 Blit.hlsl 引入）。
            // 自己起名 `sampler_SourceTex` 会报 "Unrecognized sampler ... does not match any texture"
            // —— 它只允许"与某个 texture 同名"或"内联名"两种。
            TEXTURE2D(_DryBrushTex); SAMPLER(sampler_DryBrushTex);

            CBUFFER_START(UnityPerMaterial)
                half4 _EdgeColor;
                half  _DepthSensitivity;
                half  _DepthBias;
                half  _NormalSensitivity;
                half  _NormalBias;
                half  _LineThickness;
                half  _LineStrength;
                float4 _DryBrushTex_ST;
                half  _DryBrushScale;
                half  _DryBrushStrength;
                half  _DryBrushBias;
                half  _DistanceFade;
                half  _DistanceFadeSharp;
            CBUFFER_END

            float4 _InkTexelSize;      // xy = 1/宽高，由 C# 每帧传入

            // 注意：**不能**用 `=>` 表达式体函数（`float F(float2 uv) => ...`）。
            // Unity 在 D3D11 上走的是 FXC，它不认这个语法，会报
            // "syntax error: unexpected token '='" 而且不指出是函数简写的问题。
            float EyeDepth(float2 uv)
            {
                return LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams);
            }

            /// ★ 不要再做 `*2-1` 解码。
            /// URP 的 DepthNormals 预pass 把法线写进 **R8G8B8A8_SNorm**（有符号归一化，
            /// 见 DepthNormalOnlyPass.GetGraphicsFormat），采样出来**已经是 [-1,1]**；
            /// 再解一次码相当于把法线拧成另一组方向 —— 不会报错，只会得到一堆错的折痕。
            /// （只有在延迟管线的 `_GBUFFER_NORMALS_OCT` 路径下才是 [0,1] 八面体编码，
            ///   而 URP 的 SampleSceneNormals 已经替我们处理了那个分支。）
            half3 SceneNormal(float2 uv)
            {
                return normalize(SampleSceneNormals(uv));
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                float2 texel = _InkTexelSize.xy * _LineThickness;

                float far = _ProjectionParams.z - 0.05;

                float dC = EyeDepth(uv);
                half3 nC = SceneNormal(uv);
                half isGeo = (dC < far) ? 1.0h : 0.0h;

                float dL = EyeDepth(uv + float2(-texel.x, 0));
                float dR = EyeDepth(uv + float2( texel.x, 0));
                float dU = EyeDepth(uv + float2(0,  texel.y));
                float dD = EyeDepth(uv + float2(0, -texel.y));

                // ---- ① 深度"断崖" ----
                //
                // ★★ 这里有一个**注释与实现不符**的老 bug，改之前务必读完 ★★
                //
                // 原注释声称 `|dL − dR|` 是"Roberts 式二阶差分，能把掠射角地面的斜坡减掉"。
                // **它是错的**：`|dL − dR|` 是**一阶**中心差分。
                //   在斜率 s 的线性斜坡上，dL = dC − s·h、dR = dC + s·h，
                //   于是 `|dL − dR| = 2·s·h` —— **正比于斜率，一点也不为零**。
                //   只有在"平坦平台"（s ≈ 0）上才为 0。
                // 后果（用户实测到的那条）：透视下斜率很大的**掠射角墙面与地面**
                //   整片被算成"轮廓"，画面上出现一道道**贯穿并指向灭点的斜线**
                //   —— 看着像工程图的三角剖分线。实测对照：把深度项置零，斜线全部消失
                //   （证据已归档到 Tools/screenshots/s6/：S6_edge_depth_on.png ↔ S6_edge_depth_off.png）。
                //
                // 处置：不改成真正的二阶差分 `|dL + dR − 2·dC|` —— 拉普拉斯在**阶跃边缘的中心处过零**，
                //   会把每条边画成**双线**（这是它的经典缺陷，靠飞白噪声遮不住）。
                //   改为**大幅抬高死区**：本场景里"物体 vs 物体"的边界几乎都有法线差（走 nEdge），
                //   "物体 vs 天空"有 sEdge，深度项本来只是兜"同一物体自遮挡"这一小块，
                //   为这点收益换来满屏斜线不值得。
                float invD = 1.0 / max(dC, 0.05);
                float depthJump = (abs(dL - dR) + abs(dU - dD)) * invD;
                half dEdge = saturate(depthJump * _DepthSensitivity - _DepthBias);

                // ---- ② 法线折痕：只在两侧都是实体时计入（见头注 a）----
                half3 nL = SceneNormal(uv + float2(-texel.x, 0));
                half3 nR = SceneNormal(uv + float2( texel.x, 0));
                half3 nU = SceneNormal(uv + float2(0,  texel.y));
                half3 nD = SceneNormal(uv + float2(0, -texel.y));
                half gL = (1.0h - saturate(dot(nC, nL))) * isGeo * ((dL < far) ? 1.0h : 0.0h);
                half gR = (1.0h - saturate(dot(nC, nR))) * isGeo * ((dR < far) ? 1.0h : 0.0h);
                half gU = (1.0h - saturate(dot(nC, nU))) * isGeo * ((dU < far) ? 1.0h : 0.0h);
                half gD = (1.0h - saturate(dot(nC, nD))) * isGeo * ((dD < far) ? 1.0h : 0.0h);
                half nEdge = saturate((gL + gR + gU + gD) * _NormalSensitivity - _NormalBias);

                // ---- ③ 天空剪影：中心是实体、对面是天空 ⇒ 必是轮廓 ----
                // 二阶差分对**薄物体**（剑刃、发簪）会失效 —— 两侧邻域都落进天空，差值互相抵消；
                // 这一项专门兜它，顺带把地平线画出来（那个是想要的）。
                half sEdge = isGeo * max(max((dL < far) ? 0.0h : 1.0h, (dR < far) ? 0.0h : 1.0h),
                                         max((dU < far) ? 0.0h : 1.0h, (dD < far) ? 0.0h : 1.0h));

                // 取 max 而不是相加：三项描述的是同一个边界，相加只会把强度翻三倍、噪点也翻三倍
                half edge = max(max(dEdge, nEdge), sEdge);

                // 远处淡出：墨越远越淡，同时压掉远处地面上的法线噪声
                half fade = pow(saturate(1.0h - dC / max(_DistanceFade, 1.0h)), _DistanceFadeSharp);
                edge *= fade;

                half3 scene = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).rgb;
                if (edge <= 0.0008h) return half4(scene, 1.0h);

                // ---- ★ 飞白：噪声只调**浓淡**，不做第二次压制 ----
                // 上一版是 `smoothstep(0.05,0.18,ink)` 之后再 `saturate(ink*_LineStrength)`，
                // 而飞白又先把中值削到 lerp(1, 0.45, 0.72) = 0.60 ⇒ 普通轮廓只剩约 3% 的混合量，
                // 表现就是"墨线看不见"（四阶段 ③−② 的像素差只有 0.010）。
                // 浓淡归浓淡、起笔归起笔 —— 别再串两道阈值。
                half dry = SAMPLE_TEXTURE2D(_DryBrushTex, sampler_DryBrushTex, uv * _DryBrushScale).r;
                half dryMask = smoothstep(_DryBrushBias - 0.25h, _DryBrushBias + 0.35h, dry);
                half presence = saturate(edge * _LineStrength);
                half ink = presence * lerp(1.0h, dryMask, _DryBrushStrength);
                ink = smoothstep(0.0h, 0.10h, ink);      // 只做软起笔（防止硬边锯齿）

                return half4(lerp(scene, _EdgeColor.rgb, saturate(ink)), 1.0h);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
