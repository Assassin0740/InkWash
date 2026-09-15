using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace InkWash.Rendering
{
    /// <summary>
    /// 水墨风格通用的全屏 Pass：**先把当前画面拷到一张临时纹理，再把它当作输入画回相机目标**。
    ///
    /// 为什么必须绕这一圈（这是 URP 全屏效果的硬约束）：
    /// 需要"读画面 + 改画面"的效果不能直接 src→src blit，同一个 RT 既当输入又当输出
    /// 在多数图形 API 上是未定义行为（会拿到半旧半新的数据）。
    /// URP 官方 <c>FullScreenPassRendererFeature</c> 的做法就是这一步拷贝，这里沿用它的写法：
    /// 拷贝 → 把拷贝设为 <c>_BlitTexture</c> → <c>DrawProcedural</c> 画回相机目标。
    /// </summary>
    public class InkFullScreenPass : ScriptableRenderPass
    {
        private static readonly MaterialPropertyBlock s_Block = new MaterialPropertyBlock();
        private static readonly Vector4 ScaleBias = new Vector4(1f, 1f, 0f, 0f);
        // URP 的 ShaderPropertyId 是 internal，外部程序集用不了；这两个名字是全局约定的，
        // 直接用 PropertyToID 取即可。
        private static readonly int _BlitTextureId = Shader.PropertyToID("_BlitTexture");
        private static readonly int _BlitScaleBiasId = Shader.PropertyToID("_BlitScaleBias");

        private readonly Material _material;
        private readonly Action<Material> _pushParams;
        private RTHandle _copied;

        public InkFullScreenPass(string passName, Material material, Action<Material> pushParams,
                                 ScriptableRenderPassInput input, RenderPassEvent passEvent)
        {
            profilingSampler = new ProfilingSampler(passName);
            _material = material;
            _pushParams = pushParams;
            renderPassEvent = passEvent;
            // Depth / Normal 必须**显式请求**，否则 _CameraDepthTexture / _CameraNormalsTexture
            // 里是空的 —— 而那是静默的：效果只是"变淡"，不报任何错。
            ConfigureInput(input);
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            ResetTarget();
            var desc = renderingData.cameraData.cameraTargetDescriptor;
            desc.msaaSamples = 1;                       // 拷贝图不需要 MSAA；留着会浪费带宽
            desc.depthBufferBits = (int)DepthBits.None;
            RenderingUtils.ReAllocateIfNeeded(ref _copied, desc, name: "_InkWashFullScreenCopy");
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (_material == null || _copied == null) return;

            ref var camData = ref renderingData.cameraData;
            var dst = camData.renderer.cameraColorTargetHandle;
            if (dst == null) return;

            // `renderingData.commandBuffer` 在 URP 14 里是 internal（同程序集才可用），
            // 所以外部 Feature 必须自己从池子里取一个 —— 这也是 URP 官方示例的写法。
            var cmd = CommandBufferPool.Get("InkWash.FullScreen");

            using (new ProfilingScope(cmd, profilingSampler))
            {
                _pushParams?.Invoke(_material);

                // 1) 把当前画面拷进临时图
                CoreUtils.SetRenderTarget(cmd, _copied);
                Blitter.BlitTexture(cmd, dst, ScaleBias, 0f, false);

                // 2) 以临时图为输入，把结果画回相机目标
                CoreUtils.SetRenderTarget(cmd, dst);
                s_Block.Clear();
                s_Block.SetTexture(_BlitTextureId, _copied);
                s_Block.SetVector(_BlitScaleBiasId, ScaleBias);
                cmd.DrawProcedural(Matrix4x4.identity, _material, 0, MeshTopology.Triangles, 3, 1, s_Block);
            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        public void Dispose()
        {
            _copied?.Release();
            _copied = null;
        }
    }
}
