using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

// Extra background motion blur only for cameras explicitly carrying SpeedCameraEffects.
// Runs before post-processing so altitude blackout/tonemapping remain authoritative.
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
            !camera.TryGetComponent(out SpeedCameraEffects effects) || !effects.HasPeripheralBlur)
            return;
        pass.Setup(material, effects.PeripheralBlurParameters);
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
        private Vector4 parameters;
        private RTHandle temporary;
        private static readonly int ParametersId = Shader.PropertyToID("_PeripheralBlur");
        private static readonly int MotionId = Shader.PropertyToID("_SpeedMotionTexture");
        private sealed class PassData
        {
            public TextureHandle source, motion;
            public Material material;
            public Vector4 parameters;
        }

        public void Setup(Material value, Vector4 settings)
        {
            material = value;
            parameters = settings;
            requiresIntermediateTexture = true;
            ConfigureInput(ScriptableRenderPassInput.Motion);
        }

        public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
        {
            var resources = frameData.Get<UniversalResourceData>();
            if (resources.isActiveTargetBackBuffer || !resources.motionVectorColor.IsValid()) return;
            var desc = graph.GetTextureDesc(resources.activeColorTexture);
            desc.name = "Peripheral speed blur";
            desc.clearBuffer = false;
            var destination = graph.CreateTexture(desc);
            using (var builder = graph.AddRasterRenderPass<PassData>("Peripheral speed blur", out var data))
            {
                data.source = resources.activeColorTexture;
                data.motion = resources.motionVectorColor;
                data.material = material;
                data.parameters = parameters;
                builder.UseTexture(data.source);
                builder.UseTexture(data.motion);
                builder.SetRenderAttachment(destination, 0);
                builder.SetRenderFunc((PassData d, RasterGraphContext context) =>
                {
                    // Configure per execution, not per camera setup; no cross-camera globals.
                    d.material.SetVector(ParametersId, d.parameters);
                    d.material.SetTexture(MotionId, (RTHandle)d.motion);
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
                TextureWrapMode.Clamp, name: "Peripheral speed blur");
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            var source = renderingData.cameraData.renderer.cameraColorTargetHandle;
            var cmd = CommandBufferPool.Get("Peripheral speed blur");
            material.SetVector(ParametersId, parameters);
            // In compatibility mode URP exposes the already-rendered motion texture globally.
            material.SetTexture(MotionId, Shader.GetGlobalTexture("_MotionVectorTexture"));
            Blitter.BlitCameraTexture(cmd, source, temporary, material, 0);
            Blitter.BlitCameraTexture(cmd, temporary, source);
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }
#pragma warning restore 618, 672

        public void Dispose() => temporary?.Release();
    }
}
