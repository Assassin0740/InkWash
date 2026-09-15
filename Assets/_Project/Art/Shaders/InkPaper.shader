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
        _PaperStrength ("纸纹强度（0=不用纸）", Range(0, 1)) = 0.45
        _PaperContrast ("纸纹对比", Range(0.2, 3)) = 1.15
        _GrainStrength ("生宣颗粒", Range(0, 1)) = 0.18
        _PaperTint ("纸的底色", Color) = (0.949, 0.925, 0.867, 1)
        _TintStrength ("底色着染", Range(0, 1)) = 0.3
        _Vignette ("边缘压暗（纸的受潮）", Range(0, 1)) = 0.32
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

                // ---- ② 生宣颗粒：高频细点，靠"减去一点亮度"来吃墨 ----
                half grain = SAMPLE_TEXTURE2D(_PaperTex, sampler_PaperTex, uv * _PaperTiling * 11.0h).g;
                half grainFactor = 1.0h - (1.0h - grain) * _GrainStrength;

                half factor = lerp(1.0h, fibre, _PaperStrength) * grainFactor;

                half3 outCol = col * factor;

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
