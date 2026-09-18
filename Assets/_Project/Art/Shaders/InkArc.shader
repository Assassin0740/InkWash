// InkArc.shader —— 青白细电弧带（墨龙特效 A「沿脊骨游走的电弧」与 B′「烟中电弧」共用）
//
// ═══════════════════════════════════════════════════════════════════════
//  ★★ 为什么**不用加色混合**（这是对 `Docs/墨龙特效设计.md` §五 的一次修正）
// ═══════════════════════════════════════════════════════════════════════
//  设计文档里写的是「电弧是加在墨色之上的光」+ HDR 乘子 1.6~2.4，也就是 `Blend One One`。
//  那条在**暗底**上完全成立。但本项目的背景是宣纸：
//      InkSky 地平线 (0.966, 0.963, 0.952)、天顶 (0.862, 0.874, 0.888)
//  ⇒ 近白底。加法在近白底上 **白 + 青白 = 仍然是白** ⇒ 屏幕上看不见任何东西。
//  而墨龙的脊骨只有 ~0.1 m 粗、电弧抖动幅度 0.06~0.16 m
//  ⇒ 一条电弧**总有一半伸到纸色背景上**，那半截会凭空消失，读起来像"电被切断了"。
//
//  所以改成 **小 alpha 混合 + 饱和青**：
//    · 辉光色取饱和青 (0.30, 0.78, 1.0)：在暗墨身子上是亮青，在纸白上是深青，两头都读得出；
//    · "白热"交给芯色（近白但**带青**，纯白在纸上同样会消失）+ Bloom，不靠加法。
//  ⇒ 代价是失去了"自发光叠加"的物理感，收益是**在真实的宣纸背景上成立**。
//    这就是本项目一贯的取舍顺序：先在真实背景上看得见，再谈质感。
//
// ═══════════════════════════════════════════════════════════════════════
//  弧带的"横截面"由 **uv.y** 表达（不是靠网格环数）
// ═══════════════════════════════════════════════════════════════════════
//  对照 `InkSlash.shader`：刀光的浓淡剖面用了 **5 条环**（顶点色表达），因为那是"笔锋"，
//  剖面形状是有讲究的、且法线/朝向固定。
//  电弧不一样：它是一条**正对相机**的细带，横截面只需要"中间白热 + 两侧青辉"两档，
//  用 `lerp(_GlowColor, _CoreColor, core)` 在片元里用 `uv.y` 直接算即可
//  ⇒ 网格只要 **2 条环**（v=0 / v=1），顶点数减半，而且**换宽度不用重建网格**（改 _CoreWidth 即可）。
//  这也是为什么 `uv` 必须在 C# 侧**显式写死**（u = 沿长度、v = 跨宽度 0/1），
//  而不是依赖 LineRenderer 自动生成的 UV —— 后者的朝向约定在 `alignment` 变化时会变，
//  一旦反了，整条电弧会变成"外亮内白"，静默错且不报错。
//
// 【贴图的选型（第二十三轮实测翻出来的）】
//   选的是 `ParticlePack/.../Fire & Explosion Effects/Textures/LightningTrail.tif`（128², RGB）。
//   它是一张**水平方向的柔和光条**：中间亮、上下柔、两端收尖 —— 与弧带"u 沿长度 / v 跨宽度"
//   的 UV 约定是**一比一**的，一张图同时给出横截面与两端收尖。
//   ✗ 原本打算用的 `Lightning.tif`（512²）**不能用**：它是 **2×2 图集**（四根完整的闪电），
//     当单张采样会在弧带上出现四根竖着的闪电 + 格线（实测）。详见 `Docs/工程坑表.md` 42。
//   ✗ 它同时还是 **RGB（无 alpha）** ⇒ 遮罩必须取 max(rgb, a)，不能只读 alpha。
//
// 【渲染状态】
//   ZWrite Off —— 半透明特效不写深度，否则会把龙身切出硬边。
//   Cull Off   —— 弧带是单层薄片，朝向取决于相机与切线的叉积，关剔除省得某一侧看不见。
//   Queue Transparent+30 —— 排在墨点(+20)之后，保证电弧压在溅墨之上。
Shader "InkWash/InkArc"
{
    Properties
    {
        _MainTex ("细丝遮罩（读 alpha；白=实心带）", 2D) = "white" {}
        _CoreColor ("芯色（白热，带青）", Color) = (0.90, 0.97, 1.0, 1)
        _GlowColor ("辉光色（青白）", Color) = (0.30, 0.78, 1.0, 1)
        _CoreWidth ("芯宽占比（弧带半宽的几分之几）", Range(0.02, 1.0)) = 0.32
        _Intensity ("强度（HDR 乘子）", Range(0.0, 6.0)) = 1.8
        _Alpha ("总透明", Range(0.0, 1.0)) = 1.0
        // ★ 深度测试模式做成**材质属性**，是为了能"运行时 A/B"：
        //   弧带锚点取在脊柱骨节（身体内部），如果被龙自己的不透明皮剔掉，
        //   "看不见"到底是"没画"还是"被挡住"从画面上分不出来 —— 把 ZTest 切成 Always
        //   跑一遍，两者立刻可分。默认 4 = LEqual（正常玩法用这个）。
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("深度测试（4=LEqual 8=Always）", Float) = 4
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent+30"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "InkArc"
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest [_ZTest]
            Cull Off
            Lighting Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            // ★ 全部 uniform 必须在这一个 CBUFFER 里、且顺序固定（SRP Batcher 的要求）。
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4  _CoreColor;
                half4  _GlowColor;
                half   _CoreWidth;
                half   _Intensity;
                half   _Alpha;
                half   _ZTest;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color      : COLOR;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                half4  color       : COLOR;
                float2 uv          : TEXCOORD0;
            };

            // FXC 不认表达式体函数（`=>`）——`InkSplash.shader` 里踩过，这里也一律写块体
            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.color = IN.color;
                OUT.uv = IN.uv;
                return OUT;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                // v：0 = 弧带中线，1 = 弧带边缘（C# 侧写死，见文件头）
                float v = saturate(abs(IN.uv.y * 2.0 - 1.0));

                // core：中线附近的"白热芯"；glow：整条带的横向衰减
                float core = 1.0 - smoothstep(0.0, max(_CoreWidth, 0.001), v);
                float glow = 1.0 - smoothstep(_CoreWidth, 1.0, v);

                // 细丝遮罩：**取 RGB 与 A 的最大值** —— 不能只读 alpha。
                // ★ 这是本轮实测踩到的：`LightningTrail.tif` / `Lightning.tif` 都是 **RGB（无 alpha）**
                //   的 TGA/TIF，Unity 导入后 alpha 恒为 1 ⇒ 只读 `.a` 的话整条弧带是均匀实心，
                //   贴图里那圈"中间亮、两端收尖"的柔和形状**完全被丢掉**，而且不报错。
                //   取最大值对两类贴图都成立：RGBA 的（如烟）走 alpha，RGB 的（如电）走亮度。
                //   贴图为空时是白图 ⇒ fil = 1 ⇒ 退化成一条干净的实心弧带（不是坏掉）。
                float4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv);
                float fil = max(max(tex.r, tex.g), max(tex.b, tex.a));

                half3 rgb = lerp(_GlowColor.rgb, _CoreColor.rgb, core) * _Intensity;

                // 两端收尖与逐条明暗都由顶点色 alpha 携带（C# 侧按 taper 写）
                half a = saturate(fil * IN.color.a * _Alpha) * glow;

                return half4(rgb, a);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
