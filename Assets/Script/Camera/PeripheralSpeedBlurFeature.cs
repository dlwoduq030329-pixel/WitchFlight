using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

// Background motion blur and the independent, one-sided player blur are composited once.
// Before post-processing: death grayscale, altitude blackout and color grading still win.
public sealed class PeripheralSpeedBlurFeature : ScriptableRendererFeature
{
    [SerializeField] private Shader blurShader;
    private Material material;
    private BlurPass pass;

    public override void Create()
    {
        pass?.Dispose();
        CoreUtils.Destroy(material);
        if (blurShader == null) blurShader = Shader.Find("Hidden/WitchFlight/PeripheralSpeedBlur");
        material = blurShader != null ? CoreUtils.CreateEngineMaterial(blurShader) : null;
        pass = new BlurPass { renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing };
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        Camera camera = renderingData.cameraData.camera;
        if (material == null || camera.cameraType != CameraType.Game ||
            renderingData.cameraData.renderType != CameraRenderType.Base ||
            !camera.TryGetComponent(out SpeedCameraEffects effects) || !effects.isActiveAndEnabled || !effects.HasCustomBlur)
            return;
        effects.PlayerBlur?.Prepare(camera);
        pass.Setup(material, effects);
        renderer.EnqueuePass(pass);
    }

    protected override void Dispose(bool disposing)
    {
        pass?.Dispose();
        CoreUtils.Destroy(material);
    }

    private sealed class BlurPass : ScriptableRenderPass
    {
        private Material material;
        private SpeedCameraEffects effects;
        private RTHandle temporary, mask;
        private sealed class MaskData
        {
            public PlayerDirectionalBlur player;
            public Camera camera;
            public TextureHandle depth;
        }
        private sealed class PassData
        {
            public TextureHandle source, motion, mask, depth;
            public Material material;
            public Vector4 peripheral, background, trail, anchor, rect;
        }

        public void Setup(Material value, SpeedCameraEffects source)
        {
            material = value;
            effects = source;
            requiresIntermediateTexture = true;
            ConfigureInput(ScriptableRenderPassInput.Motion | ScriptableRenderPassInput.Depth);
        }

        private void SetParameters(Material value, Vector4 peripheral, Vector4 background,
            Vector4 trail, Vector4 anchor, Vector4 rect)
        {
            value.SetVector("_PeripheralBlur", peripheral);
            value.SetVector("_BackgroundBlur", background);
            value.SetVector("_PlayerTrail", trail);
            value.SetVector("_PlayerAnchor", anchor);
            value.SetVector("_PlayerRect", rect);
        }

        public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
        {
            var resources = frameData.Get<UniversalResourceData>();
            if (resources.isActiveTargetBackBuffer || !resources.motionVectorColor.IsValid() || !resources.cameraDepthTexture.IsValid()) return;
            var desc = graph.GetTextureDesc(resources.activeColorTexture);
            desc.name = "Player motion silhouette";
            desc.colorFormat = GraphicsFormat.R16G16_SFloat;
            desc.msaaSamples = MSAASamples.None;
            desc.depthBufferBits = DepthBits.None;
            desc.clearBuffer = true;
            desc.clearColor = Color.clear;
            var silhouette = graph.CreateTexture(desc);
            using (var builder = graph.AddRasterRenderPass<MaskData>("Player motion silhouette", out var data))
            {
                data.player = effects.PlayerBlur;
                data.camera = frameData.Get<UniversalCameraData>().camera;
                data.depth = resources.cameraDepthTexture;
                builder.UseTexture(data.depth);
                builder.SetRenderAttachment(silhouette, 0);
                builder.SetRenderFunc((MaskData d, RasterGraphContext context) =>
                {
                    if (d.player != null && d.player.NeedsMask)
                        d.player.DrawMask(context.cmd, d.camera, (RTHandle)d.depth);
                });
            }
            desc = graph.GetTextureDesc(resources.activeColorTexture);
            desc.name = "Separated speed motion blur";
            desc.clearBuffer = false;
            var destination = graph.CreateTexture(desc);
            using (var builder = graph.AddRasterRenderPass<PassData>("Separated speed motion blur", out var data))
            {
                data.source = resources.activeColorTexture;
                data.motion = resources.motionVectorColor;
                data.mask = silhouette;
                data.depth = resources.cameraDepthTexture;
                data.material = material;
                data.peripheral = effects.HasPeripheralBlur ? effects.PeripheralBlurParameters : Vector4.zero;
                data.background = effects.BackgroundBlurParameters;
                data.trail = effects.PlayerBlur?.Trail ?? Vector4.zero;
                data.anchor = effects.PlayerBlur?.Anchor ?? Vector4.zero;
                data.rect = effects.PlayerBlur?.ScreenRect ?? Vector4.zero;
                builder.UseTexture(data.source); builder.UseTexture(data.motion);
                builder.UseTexture(data.mask); builder.UseTexture(data.depth);
                builder.SetRenderAttachment(destination, 0);
                builder.SetRenderFunc((PassData d, RasterGraphContext context) =>
                {
                    SetParameters(d.material, d.peripheral, d.background, d.trail, d.anchor, d.rect);
                    d.material.SetTexture("_SpeedMotionTexture", (RTHandle)d.motion);
                    d.material.SetTexture("_PlayerMotionMask", (RTHandle)d.mask);
                    d.material.SetTexture("_SpeedSceneDepth", (RTHandle)d.depth);
                    Blitter.BlitTexture(context.cmd, d.source, new Vector4(1, 1, 0, 0), d.material, 0);
                });
            }
            resources.cameraColor = destination;
        }

#pragma warning disable 618, 672
        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            var desc = renderingData.cameraData.cameraTargetDescriptor;
            desc.depthBufferBits = 0;
            desc.msaaSamples = 1;
            RenderingUtils.ReAllocateHandleIfNeeded(ref temporary, desc, FilterMode.Bilinear,
                TextureWrapMode.Clamp, name: "Separated speed motion blur");
            desc.graphicsFormat = GraphicsFormat.R16G16_SFloat;
            RenderingUtils.ReAllocateHandleIfNeeded(ref mask, desc, FilterMode.Bilinear,
                TextureWrapMode.Clamp, name: "Player motion silhouette");
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            var source = renderingData.cameraData.renderer.cameraColorTargetHandle;
            var cmd = CommandBufferPool.Get("Separated speed motion blur");
            var depth = Shader.GetGlobalTexture("_CameraDepthTexture");
            CoreUtils.SetRenderTarget(cmd, mask, ClearFlag.Color, Color.clear);
            if (effects.PlayerBlur != null && effects.PlayerBlur.NeedsMask)
                effects.PlayerBlur.DrawMask(CommandBufferHelpers.GetRasterCommandBuffer(cmd), renderingData.cameraData.camera, depth);
            SetParameters(material, effects.HasPeripheralBlur ? effects.PeripheralBlurParameters : Vector4.zero,
                effects.BackgroundBlurParameters, effects.PlayerBlur?.Trail ?? Vector4.zero,
                effects.PlayerBlur?.Anchor ?? Vector4.zero, effects.PlayerBlur?.ScreenRect ?? Vector4.zero);
            material.SetTexture("_SpeedMotionTexture", Shader.GetGlobalTexture("_MotionVectorTexture"));
            material.SetTexture("_PlayerMotionMask", mask);
            material.SetTexture("_SpeedSceneDepth", depth);
            Blitter.BlitCameraTexture(cmd, source, temporary, material, 0);
            Blitter.BlitCameraTexture(cmd, temporary, source);
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }
#pragma warning restore 618, 672
        public void Dispose() { temporary?.Release(); mask?.Release(); }
    }
}
