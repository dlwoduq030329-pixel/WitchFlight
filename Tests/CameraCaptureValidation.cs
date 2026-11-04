// Isolated Unity validation. CameraCaptureProduction contains the unchanged production
// SyncRenderSettings/PreparePostProcessing/RemoveThreatBlur/HasLiveScreenEffects methods.
using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public static class CameraCaptureValidation
{
    static int checks;
    static void Check(bool ok, string name) { if (!ok) throw new Exception(name); checks++; }
    public static void Run()
    {
        var renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
        var pipeline = UniversalRenderPipelineAsset.Create(renderer);
        GraphicsSettings.defaultRenderPipeline = pipeline;
        QualitySettings.renderPipeline = pipeline;
        if (!VolumeManager.instance.isInitialized) VolumeManager.instance.Initialize();
        var volume = new GameObject("Capture test volume").AddComponent<Volume>();
        volume.isGlobal = true; volume.priority = 10000;
        volume.sharedProfile = ScriptableObject.CreateInstance<VolumeProfile>();
        volume.sharedProfile.Add<MotionBlur>(true).intensity.value = .8f;
        volume.sharedProfile.Add<DepthOfField>(true).mode.value = DepthOfFieldMode.Gaussian;
        volume.sharedProfile.Add<Bloom>(true).intensity.value = 2f;
        volume.sharedProfile.Add<Tonemapping>(true).mode.value = TonemappingMode.ACES;
        volume.sharedProfile.Add<FilmGrain>(true).intensity.value = .5f;
        volume.sharedProfile.Add<ChromaticAberration>(true).intensity.value = .5f;
        var main = new GameObject("Main test").AddComponent<Camera>();
        var rear = new GameObject("Rear test").AddComponent<Camera>();
        main.enabled = rear.enabled = false; main.allowHDR = true;
        var mainData = main.GetUniversalAdditionalCameraData();
        var rearData = rear.GetUniversalAdditionalCameraData();
        mainData.renderPostProcessing = true;
        main.SetVolumeFrameworkUpdateMode(VolumeFrameworkUpdateMode.ViaScripting);
        main.UpdateVolumeStack(mainData);
        CameraCaptureProduction.SyncRenderSettings(main, rear, mainData, rearData);
        CameraCaptureProduction.PreparePostProcessing(rear, rearData);
        Check(mainData.volumeStack != rearData.volumeStack, "Separate main and rear stacks");
        Check(mainData.volumeStack.GetComponent<MotionBlur>().intensity.value > .7f, "Main blur untouched");
        Check(rearData.volumeStack.GetComponent<MotionBlur>().intensity.value == 0, "Rear blur removed");
        Check(rearData.volumeStack.GetComponent<DepthOfField>().mode.value == DepthOfFieldMode.Off, "Rear focus blur removed");
        Check(rearData.volumeStack.GetComponent<Bloom>().intensity.value == 2, "Spell bloom preserved");
        Check(rearData.volumeStack.GetComponent<Tonemapping>().mode.value == TonemappingMode.ACES, "Tonemapping preserved");
        Check(rear.allowHDR && rearData.requiresDepthTexture && rearData.requiresColorTexture, "HDR and effect textures preserved");
        Check(volume.sharedProfile.TryGet<MotionBlur>(out var authored) && authored.intensity.value == .8f, "Authored volume unchanged");
        CameraCaptureProduction.PreparePostProcessing(rear, rearData);
        Check(rearData.volumeStack.GetComponent<Bloom>().intensity.value == 2, "Repeated update retains bloom");

        var effects = new CameraCaptureProduction();
        Check(effects.HasLiveScreenEffects(), "Uninitialized effects conservatively capture");
        effects.screenCaptureParticlesReady = true;
        Check(!effects.HasLiveScreenEffects(), "Empty effects skip capture");
        var particles = new GameObject("Ripple test").AddComponent<ParticleSystem>();
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        effects.screenCaptureParticles.Add(particles);
        Check(!effects.HasLiveScreenEffects(), "Cleared particles idle");
        particles.Emit(1);
        Check(effects.HasLiveScreenEffects(), "New ripple wakes capture immediately");
        particles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        Check(effects.HasLiveScreenEffects(), "Emission stopped but living ripple still captured");
        particles.Clear();
        Check(!effects.HasLiveScreenEffects(), "Last ripple gone resumes idle");
        particles.Emit(1); particles.gameObject.SetActive(false);
        Check(!effects.HasLiveScreenEffects(), "Inactive particles skip capture");
        particles.gameObject.SetActive(true); particles.Emit(1);
        Check(effects.HasLiveScreenEffects(), "Reactivation wakes capture");
        Debug.Log("CAMERA_CAPTURE_VALIDATION_OK checks=" + checks);
    }
}
