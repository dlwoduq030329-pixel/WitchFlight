using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;

namespace Suimono.Core
{
    // All legacy replacement-camera and OnRenderImage work is scheduled by URP.
    public sealed class SuimonoUrpFeature : ScriptableRendererFeature
    {
        [SerializeField] Shader underwaterShader;
        [SerializeField] Shader captureShader;
        Material underwater, capture;
        ImagePass waterPass, capturePass;
        MaskPass maskPass;
        WaterDepthPass waterDepthPass;
        public override void Create()
        {
            Dispose(true);
            if (!underwaterShader) underwaterShader=Shader.Find("Hidden/Suimono/UnderwaterURP");
            if (!captureShader) captureShader=Shader.Find("Hidden/Suimono/CaptureURP");
            if (underwaterShader) underwater=CoreUtils.CreateEngineMaterial(underwaterShader);
            if (captureShader) capture=CoreUtils.CreateEngineMaterial(captureShader);
            waterPass=new ImagePass("Suimono underwater and transitions");
            capturePass=new ImagePass("Suimono capture"); maskPass=new MaskPass();
            waterDepthPass=new WaterDepthPass();
        }
        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData data)
        {
            var cam=data.cameraData.camera;
            var tool=cam.GetComponent<cameraTools>();
            if(tool!=null && capture!=null) {
                string tag=tool.cameraType==suiCamToolType.normals ? "SuimonoNormals" : tool.cameraType==suiCamToolType.wakeEffects ? "SuimonoWake" : tool.cameraType==suiCamToolType.shorelineCapture ? "SuimonoShore" : null;
                if(tag!=null) {maskPass.tag=new ShaderTagId(tag);renderer.EnqueuePass(maskPass);return;}
                int index=-1;
                if(tool.cameraType==suiCamToolType.transparent || tool.cameraType==suiCamToolType.transparentCaustic) index=0;
                if(tool.cameraType==suiCamToolType.shorelineObject) {
                    var depth=cam.GetComponent<SuimonoCamera_depth>();
                    capture.SetVector("_ShoreDepth",depth!=null ? new Vector4(depth._sceneDepth,depth._shoreDepth,0,0) : new Vector4(14.5f,27.7f,0,0)); index=1;
                }
                var blur=cam.GetComponent<Suimono_DistanceBlur>();
                if(tool.cameraType==suiCamToolType.localReflection && blur!=null && blur.enabled && blur.blurAmt>0) {
                    capturePass.SetupBlur(capture,blur.blurAmt);renderer.EnqueuePass(capturePass);return;
                }
                if(index>=0) {capturePass.Setup(capture,index);renderer.EnqueuePass(capturePass);}
                return;
            }
            if(underwater==null || cam.cameraType!=CameraType.Game) return;
            var fog=cam.GetComponent<Suimono_UnderwaterFog>();
            var module=Object.FindFirstObjectByType<SuimonoModule>();
            if(fog==null || !fog.isActiveAndEnabled || module==null || !module.enableUnderwaterFX || module.setCameraComponent!=cam) return;
            if(!module.isUnderwater && !fog.UrpTransitionActive) return;
            underwater.SetTexture("_DistortTex",fog.distortTex);
            underwater.SetTexture("_DropsTex",fog.mask2Tex);
            underwater.SetColor("_WaterColor",fog.underwaterColor);
            underwater.SetVector("_WaterFog",new Vector4(fog.fogStart,Mathf.Max(fog.fogStart+.001f,fog.fogEnd),fog.lightFactor,module.isUnderwater ? 1 : 0));
            underwater.SetVector("_WaterDistort",new Vector4(fog.refractAmt,fog.refractScale,fog.refractSpd,fog.blurSpread));
            underwater.SetVector("_FogOptions",new Vector4(fog.distanceFog?1:0,fog.heightFog?1:0,fog.useRadialDistance?1:0,Mathf.Max(0,fog.startDistance)));
            underwater.SetVector("_HeightFog",new Vector4(fog.height,fog.heightDensity,Mathf.Max(.001f,fog.darkRange),fog.heightDepth));
            underwater.SetFloat("_WaterLevel",module.currentSurfaceLevel);
            underwater.SetColor("_WaterLight",module.setLight!=null ? module.setLight.color : Color.white);
            underwater.SetVector("_Transition",fog.UrpTransition);
            underwater.SetVector("_TransitionOptions",new Vector4(fog.transitionStrength,fog.showScreenMask?1:0,Mathf.Clamp(fog.iterations,0,8),0));
            if(module.isUnderwater)renderer.EnqueuePass(waterDepthPass);
            waterPass.Setup(underwater,0);renderer.EnqueuePass(waterPass);
        }
        protected override void Dispose(bool disposing) {
            waterPass?.Dispose();capturePass?.Dispose();waterDepthPass?.Dispose();CoreUtils.Destroy(underwater);CoreUtils.Destroy(capture);
        }
        sealed class WaterDepthPass:ScriptableRenderPass {
            static readonly int TextureId=Shader.PropertyToID("_SuimonoWaterDepth");
            static readonly ShaderTagId Tag=new ShaderTagId("SuimonoWaterDepth");
            RTHandle target;
            class PassData {public RendererListHandle list;}
            static Color ClearColor=>SystemInfo.usesReversedZBuffer?Color.black:Color.white;
            public WaterDepthPass(){renderPassEvent=(RenderPassEvent)((int)RenderPassEvent.BeforeRenderingPostProcessing-1);}
            public override void RecordRenderGraph(RenderGraph graph,ContextContainer frame) {
                var rendering=frame.Get<UniversalRenderingData>();var camera=frame.Get<UniversalCameraData>();
                var desc=new TextureDesc(camera.cameraTargetDescriptor.width,camera.cameraTargetDescriptor.height) {
                    name="Suimono water surface depth",colorFormat=UnityEngine.Experimental.Rendering.GraphicsFormat.R32_SFloat,
                    clearBuffer=true,clearColor=ClearColor,filterMode=FilterMode.Point
                };
                var color=graph.CreateTexture(desc);desc.name="Suimono water depth attachment";desc.colorFormat=UnityEngine.Experimental.Rendering.GraphicsFormat.None;desc.depthBufferBits=DepthBits.Depth24;
                var depth=graph.CreateTexture(desc);
                var drawing=new DrawingSettings(Tag,new SortingSettings(camera.camera){criteria=SortingCriteria.CommonOpaque});
                var filtering=new FilteringSettings(RenderQueueRange.all,camera.camera.cullingMask);
                using(var builder=graph.AddRasterRenderPass<PassData>("Suimono water surface depth",out var pass)) {
                    pass.list=graph.CreateRendererList(new RendererListParams(rendering.cullResults,drawing,filtering));builder.UseRendererList(pass.list);
                    builder.SetRenderAttachment(color,0,AccessFlags.Write);builder.SetRenderAttachmentDepth(depth,AccessFlags.Write);
                    builder.SetGlobalTextureAfterPass(color,TextureId);
                    builder.SetRenderFunc((PassData p,RasterGraphContext ctx)=>ctx.cmd.DrawRendererList(p.list));
                }
            }
#pragma warning disable 618,672
            public override void OnCameraSetup(CommandBuffer cmd,ref RenderingData data) {
                var desc=data.cameraData.cameraTargetDescriptor;desc.msaaSamples=1;desc.depthBufferBits=24;desc.colorFormat=RenderTextureFormat.RFloat;
                RenderingUtils.ReAllocateHandleIfNeeded(ref target,desc,FilterMode.Point,TextureWrapMode.Clamp,name:"Suimono water surface depth");
                ConfigureTarget(target);ConfigureClear(ClearFlag.All,ClearColor);
            }
            public override void Execute(ScriptableRenderContext context,ref RenderingData data) {
                var drawing=CreateDrawingSettings(Tag,ref data,SortingCriteria.CommonOpaque);var filtering=new FilteringSettings(RenderQueueRange.all,data.cameraData.camera.cullingMask);
                context.DrawRenderers(data.cullResults,ref drawing,ref filtering);var cmd=CommandBufferPool.Get();cmd.SetGlobalTexture(TextureId,target);context.ExecuteCommandBuffer(cmd);CommandBufferPool.Release(cmd);
            }
#pragma warning restore 618,672
            public void Dispose(){target?.Release();target=null;}
        }
        sealed class MaskPass:ScriptableRenderPass {
            public ShaderTagId tag;
            class PassData { public RendererListHandle list; }
            public MaskPass() {renderPassEvent=RenderPassEvent.AfterRenderingTransparents;}
            public override void RecordRenderGraph(RenderGraph graph,ContextContainer frame) {
                var resources=frame.Get<UniversalResourceData>();var rendering=frame.Get<UniversalRenderingData>();var camera=frame.Get<UniversalCameraData>();
                var drawing=new DrawingSettings(tag,new SortingSettings(camera.camera){criteria=SortingCriteria.CommonTransparent}) {perObjectData=PerObjectData.None};
                var filtering=new FilteringSettings(RenderQueueRange.all,camera.camera.cullingMask);
                using(var builder=graph.AddRasterRenderPass<PassData>("Suimono "+tag.name,out var pass)) {
                    pass.list=graph.CreateRendererList(new RendererListParams(rendering.cullResults,drawing,filtering));
                    builder.UseRendererList(pass.list);builder.SetRenderAttachment(resources.activeColorTexture,0,AccessFlags.Write);
                    builder.SetRenderAttachmentDepth(resources.activeDepthTexture,AccessFlags.Read);
                    builder.SetRenderFunc((PassData p,RasterGraphContext ctx)=>ctx.cmd.DrawRendererList(p.list));
                }
            }
#pragma warning disable 618,672
            public override void Execute(ScriptableRenderContext context,ref RenderingData data) {
                var drawing=CreateDrawingSettings(tag,ref data,SortingCriteria.CommonTransparent);
                var filtering=new FilteringSettings(RenderQueueRange.all,data.cameraData.camera.cullingMask);
                context.DrawRenderers(data.cullResults,ref drawing,ref filtering);
            }
#pragma warning restore 618,672
        }
        sealed class ImagePass:ScriptableRenderPass {
            class BlitData { public TextureHandle source; public Material material; public int index; }
            readonly string label; Material material;int index;RTHandle temporary,alternate;float blurAmount;
            public ImagePass(string name) {label=name;renderPassEvent=RenderPassEvent.BeforeRenderingPostProcessing;ConfigureInput(ScriptableRenderPassInput.Depth|ScriptableRenderPassInput.Color);}
            public void Setup(Material mat,int passIndex) {material=mat;index=passIndex;}
            public void SetupBlur(Material mat,float amount) {material=mat;index=2;blurAmount=Mathf.Clamp01(amount);}
            public override void RecordRenderGraph(RenderGraph graph,ContextContainer frame) {
                var resources=frame.Get<UniversalResourceData>();if(resources.isActiveTargetBackBuffer)return;
                var source=resources.activeColorTexture;var desc=graph.GetTextureDesc(source);desc.name=label;desc.clearBuffer=false;
                if(index==2) {
                    int steps=Mathf.FloorToInt(blurAmount*2);int divisor=1<<steps;
                    desc.width=Mathf.Max(1,desc.width/divisor);desc.height=Mathf.Max(1,desc.height/divisor);
                    var current=graph.CreateTexture(desc);
                    graph.AddBlitPass(new RenderGraphUtils.BlitMaterialParameters(source,current,material,3),label+" downsample");
                    for(int iteration=0;iteration<steps;iteration++)for(int axis=0;axis<2;axis++) {
                        var next=graph.CreateTexture(desc);var block=new MaterialPropertyBlock();block.SetFloat("_BlurRadius",blurAmount/divisor+iteration);
                        var parameters=new RenderGraphUtils.BlitMaterialParameters(current,next,material,axis==0?2:4) {propertyBlock=block};
                        graph.AddBlitPass(parameters,label+" gaussian");current=next;
                    }
                    resources.cameraColor=current;return;
                }
                var destination=graph.CreateTexture(desc);
                using(var builder=graph.AddRasterRenderPass<BlitData>(label,out var pass)) {
                    pass.source=source;pass.material=material;pass.index=index;
                    builder.UseTexture(source);builder.UseAllGlobalTextures(true);
                    if(resources.cameraDepthTexture.IsValid()) builder.UseTexture(resources.cameraDepthTexture);
                    builder.SetRenderAttachment(destination,0,AccessFlags.Write);
                    builder.SetRenderFunc((BlitData p,RasterGraphContext ctx)=>Blitter.BlitTexture(ctx.cmd,p.source,new Vector4(1,1,0,0),p.material,p.index));
                }
                resources.cameraColor=destination;
            }
#pragma warning disable 618,672
            public override void OnCameraSetup(CommandBuffer cmd,ref RenderingData data) {
                var desc=data.cameraData.cameraTargetDescriptor;desc.depthBufferBits=0;desc.msaaSamples=1;
                if(index==2){int divisor=1<<Mathf.FloorToInt(blurAmount*2);desc.width=Mathf.Max(1,desc.width/divisor);desc.height=Mathf.Max(1,desc.height/divisor);}
                RenderingUtils.ReAllocateHandleIfNeeded(ref temporary,desc,FilterMode.Bilinear,TextureWrapMode.Clamp,name:label);
                if(index==2)RenderingUtils.ReAllocateHandleIfNeeded(ref alternate,desc,FilterMode.Bilinear,TextureWrapMode.Clamp,name:label+" alternate");
            }
            public override void Execute(ScriptableRenderContext context,ref RenderingData data) {
                var cmd=CommandBufferPool.Get(label);var source=data.cameraData.renderer.cameraColorTargetHandle;
                if(index==2) {
                    int steps=Mathf.FloorToInt(blurAmount*2);Blitter.BlitCameraTexture(cmd,source,temporary,material,3);
                    for(int iteration=0;iteration<steps;iteration++) {
                        cmd.SetGlobalFloat("_BlurRadius",blurAmount/(1<<steps)+iteration);
                        Blitter.BlitCameraTexture(cmd,temporary,alternate,material,2);Blitter.BlitCameraTexture(cmd,alternate,temporary,material,4);
                    }
                    Blitter.BlitCameraTexture(cmd,temporary,source);context.ExecuteCommandBuffer(cmd);CommandBufferPool.Release(cmd);return;
                }
                Blitter.BlitCameraTexture(cmd,source,temporary,material,index);Blitter.BlitCameraTexture(cmd,temporary,source);
                context.ExecuteCommandBuffer(cmd);CommandBufferPool.Release(cmd);
            }
#pragma warning restore 618,672
            public void Dispose(){temporary?.Release();temporary=null;alternate?.Release();alternate=null;}
        }
    }
}
