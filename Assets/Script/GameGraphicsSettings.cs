using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// A persistent local controller, including when Battle is launched without opening Settings first.
public sealed class GameGraphicsSettings : MonoBehaviour
{
    private const string Prefix = "WitchFlight.Graphics.v1.";
    private static GameGraphicsSettings instance;
    private static bool loaded;
    private static int displayMode, quality;
    private static bool postProcessing;
    public static event Action Changed;
    public static int DisplayMode { get { Load(); return displayMode; } }
    public static int Quality { get { Load(); return quality; } }
    public static bool PostProcessing { get { Load(); return postProcessing; } }

    private RenderPipelineAsset previousPipeline;
    private UniversalRenderPipelineAsset sourcePipeline, runtimePipeline;
    private float originalLodBias;
    private readonly Dictionary<UniversalAdditionalCameraData, bool> cameraStates = new();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { instance = null; loaded = false; Changed = null; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Initialize()
    {
        if (instance == null) new GameObject("Graphics settings (local)").AddComponent<GameGraphicsSettings>();
    }

    private static void Load()
    {
        if (loaded) return;
        displayMode = Mathf.Clamp(PlayerPrefs.GetInt(Prefix + "DisplayMode", Screen.fullScreen ? 0 : 1), 0, 1);
        quality = Mathf.Clamp(PlayerPrefs.GetInt(Prefix + "Quality", 0), 0, 3);
        postProcessing = PlayerPrefs.GetInt(Prefix + "PostProcessing", 1) != 0;
        loaded = true;
    }

    private void Awake()
    {
        if (instance != null && instance != this) { Destroy(gameObject); return; }
        instance = this;
        DontDestroyOnLoad(gameObject);
        Load();
        previousPipeline = QualitySettings.renderPipeline;
        sourcePipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        originalLodBias = QualitySettings.lodBias;
        if (sourcePipeline != null)
        {
            // Never edit the source asset: all four options share its authored renderers/features.
            runtimePipeline = Instantiate(sourcePipeline);
            runtimePipeline.name = sourcePipeline.name + " (local settings)";
            runtimePipeline.hideFlags = HideFlags.DontSave;
            QualitySettings.renderPipeline = runtimePipeline;
        }
        ApplyQuality();
        if (PlayerPrefs.HasKey(Prefix + "DisplayMode")) ApplyDisplayMode();
        RenderPipelineManager.beginContextRendering += BeginContext;
        RenderPipelineManager.beginCameraRendering += BeginCamera;
        RenderPipelineManager.endContextRendering += EndContext;
    }

    public static void SetDisplayMode(int value)
    {
        Initialize();
        displayMode = Mathf.Clamp(value, 0, 1);
        instance.ApplyDisplayMode();
        Save();
    }

    public static void SetQuality(int value)
    {
        Initialize();
        quality = Mathf.Clamp(value, 0, 3);
        instance.ApplyQuality();
        Save();
    }

    public static void SetPostProcessing(int value)
    {
        Initialize();
        postProcessing = value == 0; // Dropdown: enabled, disabled.
        Save();
    }

    private static void Save()
    {
        PlayerPrefs.SetInt(Prefix + "DisplayMode", displayMode);
        PlayerPrefs.SetInt(Prefix + "Quality", quality);
        PlayerPrefs.SetInt(Prefix + "PostProcessing", postProcessing ? 1 : 0);
        PlayerPrefs.Save();
        Changed?.Invoke();
    }

    private void ApplyDisplayMode()
    {
        // A Game view cannot change the desktop window mode. The same saved choice applies in builds.
#if !UNITY_EDITOR
        Resolution desktop = Screen.currentResolution;
        int desktopWidth = Mathf.Max(640, desktop.width), desktopHeight = Mathf.Max(480, desktop.height);
        if (displayMode == 0)
        {
            if (!Screen.fullScreen)
            {
                PlayerPrefs.SetInt(Prefix + "WindowWidth", Screen.width);
                PlayerPrefs.SetInt(Prefix + "WindowHeight", Screen.height);
            }
            Screen.SetResolution(desktopWidth, desktopHeight, FullScreenMode.FullScreenWindow);
        }
        else
        {
            int width = Mathf.Clamp(PlayerPrefs.GetInt(Prefix + "WindowWidth", Mathf.RoundToInt(desktopWidth * .8f)), 640, desktopWidth);
            int height = Mathf.Clamp(PlayerPrefs.GetInt(Prefix + "WindowHeight", Mathf.RoundToInt(desktopHeight * .8f)), 480, desktopHeight);
            Screen.SetResolution(width, height, FullScreenMode.Windowed);
        }
#endif
    }

    private void ApplyQuality()
    {
        if (runtimePipeline == null || sourcePipeline == null) return;
        GraphicsQualityPreset preset = GraphicsQualityPreset.ForIndex(quality);
        runtimePipeline.renderScale = sourcePipeline.renderScale * preset.renderScale;
        runtimePipeline.shadowDistance = sourcePipeline.shadowDistance * preset.shadowDistance;
        runtimePipeline.mainLightShadowmapResolution = Mathf.Min(sourcePipeline.mainLightShadowmapResolution, preset.shadowResolution);
        runtimePipeline.additionalLightsShadowmapResolution = Mathf.Min(sourcePipeline.additionalLightsShadowmapResolution, preset.shadowResolution);
        runtimePipeline.shadowCascadeCount = Mathf.Min(sourcePipeline.shadowCascadeCount, preset.cascades);
        runtimePipeline.maxAdditionalLightsCount = Mathf.Min(sourcePipeline.maxAdditionalLightsCount, preset.lights);
        QualitySettings.lodBias = originalLodBias * preset.lod;
    }

    private void BeginContext(ScriptableRenderContext context, List<Camera> cameras)
    {
        RestoreCameras();
        if (PostProcessing) return;
        // Apply before URP examines camera stacks, including overlays with authored post-processing.
        foreach (Camera camera in cameras) SuppressCamera(camera);
    }

    private void BeginCamera(ScriptableRenderContext context, Camera camera)
    {
        if (!PostProcessing) SuppressCamera(camera);
    }

    private void SuppressCamera(Camera camera)
    {
        if (camera == null || camera.cameraType != CameraType.Game ||
            !camera.TryGetComponent(out UniversalAdditionalCameraData data)) return;
        if (cameraStates.ContainsKey(data)) return;
        cameraStates.Add(data, data.renderPostProcessing);
        data.renderPostProcessing = false;
        if (data.renderType == CameraRenderType.Base && data.cameraStack != null)
            foreach (Camera overlay in data.cameraStack) SuppressCamera(overlay);
    }

    private void EndContext(ScriptableRenderContext context, List<Camera> cameras) => RestoreCameras();
    private void RestoreCameras()
    {
        foreach (var state in cameraStates) if (state.Key != null) state.Key.renderPostProcessing = state.Value;
        cameraStates.Clear();
    }

    private void OnDestroy()
    {
        if (instance != this) return;
        RenderPipelineManager.beginContextRendering -= BeginContext;
        RenderPipelineManager.beginCameraRendering -= BeginCamera;
        RenderPipelineManager.endContextRendering -= EndContext;
        RestoreCameras();
        if (runtimePipeline != null)
        {
            if (QualitySettings.renderPipeline == runtimePipeline) QualitySettings.renderPipeline = previousPipeline;
            QualitySettings.lodBias = originalLodBias;
            Destroy(runtimePipeline);
        }
        instance = null;
    }
}

public struct GraphicsQualityPreset
{
    public float renderScale, shadowDistance, lod;
    public int shadowResolution, cascades, lights;
    public static GraphicsQualityPreset ForIndex(int index)
    {
        switch (index)
        {
            case 1: return new() { renderScale = .9f, shadowDistance = .8f, lod = .85f, shadowResolution = 2048, cascades = 2, lights = 4 };
            case 2: return new() { renderScale = .8f, shadowDistance = .6f, lod = .65f, shadowResolution = 1024, cascades = 2, lights = 2 };
            case 3: return new() { renderScale = .7f, shadowDistance = .4f, lod = .5f, shadowResolution = 512, cascades = 1, lights = 1 };
            default: return new() { renderScale = 1f, shadowDistance = 1f, lod = 1f, shadowResolution = 8192, cascades = 4, lights = 8 };
        }
    }
}
