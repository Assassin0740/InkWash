// InkPaper.shader —— 宣纸底纹合成（Hidden：只给 RendererFeature 用）
//
// 论文 · E3。把整幅画面"印"在宣纸上，而不是给画面加一层噪点。三个成分：
//   ① **纸纤维**：两层不同尺度的噪声相乘 —— 单层看起来是"噪点"，两层才有纤维交织的质感
//   ② **颗粒**：高频细噪点，模拟生宣的粗糙吃墨
//   ③ **边缘压暗**：纸的边缘受潮会略深，也顺手把视线收进画面中心
//
// 关键取舍：**底纹按屏幕坐标采样，不随相机移动**。
// 如果按世界坐标贴，相机一动纸纹就跟着滑动，看起来像"世界表面脏"；
// 按屏幕坐标则像"整幅画印在同一张纸上"，这才是水墨画该有的观感。
Shader "Hidden/InkWash/InkPaper"
{
    Properties
    {
        _PaperTex ("宣纸底纹", 2D) = "white" {}
        _PaperTiling ("纸纹密度", Float) = 2.0
        _PaperStrength ("纸纹强度（0=不用纸）", Range(0, 1)) = 0.55
        _PaperContrast ("纸纹对比", Range(0.2, 3)) = 1.15
        _GrainStrength ("生宣颗粒", Range(0, 1)) = 0.30
        _PaperTint ("纸的底色", Color) = (0.949, 0.925, 0.867, 1)
        _TintStrength ("底色着染", Range(0, 1)) = 0.3
        _Vignette ("边缘压暗（纸的受潮）", Range(0, 1)) = 0.18
        _VignetteSharp ("压暗收束", Range(0.5, 6)) = 2.4
        _InkDeepen ("墨色加深（让黑更沉）", Range(0, 1)) = 0.25
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZTest Always ZWrite Off Cull Off

        Pass
        {
            Name "InkPaper"

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            TEXTURE2D(_PaperTex); SAMPLER(sampler_PaperTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _PaperTex_ST;
                half  _PaperTiling;
                half  _PaperStrength;
                half  _PaperContrast;
                half  _GrainStrength;
                half4 _PaperTint;
                half  _TintStrength;
                half  _Vignette;
                half  _VignetteSharp;
                half  _InkDeepen;
            CBUFFER_END

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                half3 col = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).rgb;

                // ---- ① 纸纤维：两层尺度相乘 ----
                half fibreA = SAMPLE_TEXTURE2D(_PaperTex, sampler_PaperTex, uv * _PaperTiling).r;
                half fibreB = SAMPLE_TEXTURE2D(_PaperTex, sampler_PaperTex, uv * _PaperTiling * 2.73h + 0.41h).r;
                half fibre = fibreA * 0.62h + fibreB * 0.38h;
                fibre = saturate((fibre - 0.5h) * _PaperContrast + 0.5h);

                // ---- ② 生宣颗粒：高频细点 ----
                half grain = SAMPLE_TEXTURE2D(_PaperTex, sampler_PaperTex, uv * _PaperTiling * 11.0h).g;

                // ★ 增量 D：把纸纹从「纯乘性压暗」改成「在**墨量域**双向调制」。
                //
                // 病 3（零和死结）：旧式 `factor = lerp(1, fibre, s) * grainFactor`，
                //   两个因子都 ≤ 1，**只有向下调制** ⇒ 想看见纸纹就必然整幅变暗。
                //   实测代价：均值 factor ≈ lerp(1, 0.5, 0.26) * (1-0.5*0.15) ≈ 0.80，
                //   也就是纸白被纸纹吃掉了整整 20% —— 这正是 M2 卡在 0.874 上不去的原因，
                //   也正是用户说的「不够白啊，有点发灰」。
                //   S6 那轮为了治灰把 paperStrength 从 0.45 砍到 0.26，代价是纸纹彻底看不见。
                //
                // 新式：在「墨量 d = 1 − col」上做双向调制。
                //   纸的纹理本来就是**纤维对墨的吸收差异**，所以在墨量域调制才是物理正确的。
                //   而且墨量域**没有 1.0 的天花板问题**（d 可以 >1 再被 saturate），
                //   因此亮部的向上调制不会被削平 ⇒ 均值守恒、纸白不塌。
                //   亮处 col=0.88(d=0.12)：mod=+0.25 → d=0.15 → col=0.85；
                //                        mod=−0.25 → d=0.09 → col=0.91。对称、均值不变。
                half mod = (fibre - 0.5h) * 2.0h * _PaperStrength      // [-1,1]，均值 0
                         + (grain - 0.5h) * 2.0h * _GrainStrength;

                // ★ 纸纹只作用于「纸」，不作用于「墨」。
                //   漏掉这一步的代价（实测）：墨量域是双向的，mod 为负时
                //   焦墨 d=0.915 → 0.686 ⇒ col 从 0.085 被抬到 0.31 —— 画面失去焦墨，
                //   M1 卡在 0.2 量级、M6 掉到 0.407。而物理上根本不该发生：
                //   **墨是吸光的**，纸纤维不可能"透出来把墨照亮"。
                //   掩膜取"当前亮度越接近纸白 → 纸纹越强"：
                //     col≈0.93（纸）→ mask≈0.89    col≈0.30 → mask≈0.0
                half maskLuma = dot(col, half3(0.2126h, 0.7152h, 0.0722h));
                half paperMask = saturate(1.0h - (1.0h - maskLuma) * 1.6h);
                half mod2 = mod * paperMask;

                half3 paperDark = saturate(1.0h - col);
                paperDark *= (1.0h + mod2);
                half3 outCol = 1.0h - saturate(paperDark);

                // ---- ③ 纸的底色：整体往米黄偏一点点（水墨画是画在纸上的，不是画在黑底上） ----
                outCol = lerp(outCol, outCol * _PaperTint.rgb, _TintStrength);

                // 墨色加深：把暗部压得更沉（生宣吃墨后黑得很实）
                half luma = dot(outCol, half3(0.2126h, 0.7152h, 0.0722h));
                outCol = lerp(outCol, outCol * (0.82h + 0.18h * saturate(luma * 2.0h)), _InkDeepen);

                // ---- 边缘压暗 ----
                float2 d = uv - 0.5;
                half v = saturate(1.0h - dot(d, d) * 2.6h);
                outCol *= lerp(1.0h, pow(v, _VignetteSharp), _Vignette);

                return half4(outCol, 1.0h);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
