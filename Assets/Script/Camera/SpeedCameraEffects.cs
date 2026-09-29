using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Local presentation only. Never moves the camera or changes network movement.
[DisallowMultipleComponent]
[RequireComponent(typeof(Camera))]
[DefaultExecutionOrder(100)]
public sealed class SpeedCameraEffects : MonoBehaviour
{
    [SerializeField] private Volume speedVolume;
    [Header("Stage 3 effects")]
    [Tooltip("Keep Local Player Sharp가 켜져 있으면 캐릭터 제외를 위해 CameraAndObjects를 사용합니다.")]
    [SerializeField] private MotionBlurMode motionBlurMode = MotionBlurMode.CameraAndObjects;
    [Tooltip("내 캐릭터와 장비의 모션벡터를 0으로 만들어 블러에서 제외합니다.")]
    [SerializeField] private bool keepLocalPlayerSharp = true;
    [SerializeField, Range(0f, 1f)] private float motionBlurIntensity = 1f;
    [Tooltip("배경이 번지는 거리 배율. 1은 기본 URP 길이입니다. 캐릭터의 0 모션벡터는 유지됩니다.")]
    [SerializeField, Range(1f, 3f)] private float motionBlurDistanceMultiplier = 1.8f;
    [SerializeField] private MotionBlurQuality motionBlurQuality = MotionBlurQuality.High;
    [SerializeField, Range(0f, 0.2f)] private float motionBlurClamp = 0.12f;
    [SerializeField, Range(0f, 30f)] private float extraFieldOfView = 10f;
    [SerializeField, Min(0.01f)] private float transitionSpeed = 5f;
    [Header("Peripheral wind lines")]
    [Tooltip("직접 만든 Canvas의 SpeedWindLines를 연결합니다. 비워두면 바람 UI를 생성하지 않습니다.")]
    [SerializeField] private SpeedWindLines windLines;
    [SerializeField] private bool showWindLines = true;
    [SerializeField, Range(0f, 1f)] private float windOpacity = 0.3f;
    [SerializeField, Range(8, 96)] private int windLineCount = 36;
    [SerializeField, Min(0.01f)] private float windSpeed = 1.6f;
    [Tooltip("1080p 기준 바람 선의 최소 전체 두께(px).")]
    [SerializeField, Min(0.1f)] private float windMinWidth = 1f;
    [Tooltip("1080p 기준 바람 선의 최대 전체 두께(px). 선이 새로 나타날 때 랜덤 선택합니다.")]
    [SerializeField, Min(0.1f)] private float windMaxWidth = 6f;
    [SerializeField] private Color windColor = new(0.8f, 0.94f, 1f, 1f);

    private Camera viewCamera;
    private UniversalAdditionalCameraData cameraData;
    private bool originalPostProcessing;
    private CameraOverrideOption originalDepthOption;
    private VolumeFrameworkUpdateMode originalVolumeUpdateMode;
    private LayerMask originalVolumeLayerMask;
    private float originalFieldOfView;
    private float effectWeight;
    private VolumeProfile previousProfile;
    private VolumeProfile runtimeProfile;
    private MotionBlur motionBlur;
    private Player protectedPlayer;
    private float nextRendererRefresh;
    private readonly List<Renderer> playerRenderers = new();
    private readonly Dictionary<Renderer, MotionVectorGenerationMode> originalMotionModes = new();
    private const float MaxExtendedBlurIntensity = 3f;

    private MotionBlurMode EffectiveBlurMode => keepLocalPlayerSharp
        ? MotionBlurMode.CameraAndObjects : motionBlurMode;

    private void OnEnable()
    {
        viewCamera = GetComponent<Camera>();
        originalFieldOfView = viewCamera.fieldOfView;
        cameraData = viewCamera.GetUniversalAdditionalCameraData();
        originalPostProcessing = cameraData.renderPostProcessing;
        originalDepthOption = cameraData.requiresDepthOption;
        originalVolumeUpdateMode = viewCamera.GetVolumeFrameworkUpdateMode();
        originalVolumeLayerMask = cameraData.volumeLayerMask;
        cameraData.renderPostProcessing = true;
        // Keep depth available and refresh the Volume as the stage fades in/out.
        cameraData.requiresDepthOption = CameraOverrideOption.On;
        viewCamera.SetVolumeFrameworkUpdateMode(VolumeFrameworkUpdateMode.EveryFrame);
        if (speedVolume != null)
            cameraData.volumeLayerMask = originalVolumeLayerMask.value | (1 << speedVolume.gameObject.layer);
        CreateRuntimeProfile();
        ApplyWeight(0f);
    }

    private void CreateRuntimeProfile()
    {
        if (speedVolume == null)
        {
            Debug.LogWarning("SpeedCameraEffects: assign Battle's post Volume for motion blur.", this);
            return;
        }

        // Deep-copy components, not just the profile's list. Inspector assets and
        // any pre-existing runtime profile are never mutated or destroyed here.
        previousProfile = speedVolume.HasInstantiatedProfile() ? speedVolume.profile : null;
        VolumeProfile source = previousProfile != null ? previousProfile : speedVolume.sharedProfile;
        runtimeProfile = ScriptableObject.CreateInstance<VolumeProfile>();
        runtimeProfile.name = "Stage 3 camera effects (runtime)";
        if (source != null)
            foreach (VolumeComponent component in source.components)
                if (component != null)
                    runtimeProfile.components.Add(Instantiate(component));

        if (!runtimeProfile.TryGet(out motionBlur))
            motionBlur = runtimeProfile.Add<MotionBlur>();
        motionBlur.active = true;
        // CameraOnly ignores renderer exclusion, so sharp-player mode must read
        // object motion vectors (zero on the local player's visible surfaces).
        motionBlur.mode.Override(EffectiveBlurMode);
        // URP 17 multiplies sample offsets by intensity. Extend only our private
        // runtime parameter, not the asset/package, to support longer streaks.
        // FloatParameter.Interp preserves this value in the blended Volume stack.
        motionBlur.intensity.max = MaxExtendedBlurIntensity;
        motionBlur.intensity.Override(0f);
        motionBlur.quality.Override(motionBlurQuality);
        motionBlur.clamp.Override(motionBlurClamp);
        speedVolume.profile = runtimeProfile;
    }


    private void LateUpdate()
    {
        Player player = Player.LocalPlayer;
        BattleManager battle = BattleManager.Instance;
        bool canShow = viewCamera.enabled && battle != null && battle.IsGameplayActive &&
            !CombatPresentation.MenuOpen && player != null && player.Object != null &&
            player.Object.IsValid && player.Object.HasInputAuthority && player.IsAlive;

        if (!canShow)
        {
            RestorePlayerMotionVectors();
            // Do not carry an FOV/blur tail into portraits, respawn or the menu.
            ApplyWeight(0f);
            return;
        }

        UpdatePlayerMotionVectors(player);

        // Stage selection is intentional; this does not wait for actual max speed.
        // Reverse stage -3 and somebody else's stage never activate the effect.
        float target = player.CurrentSpeedStage == 3 ? 1f : 0f;
        float weight = Mathf.Lerp(effectWeight, target,
            1f - Mathf.Exp(-Mathf.Max(0.01f, transitionSpeed) * Time.unscaledDeltaTime));
        if (Mathf.Abs(weight - target) < 0.001f)
            weight = target;
        ApplyWeight(weight);
    }

    private void ApplyWeight(float weight)
    {
        effectWeight = weight;
        viewCamera.fieldOfView = Mathf.Clamp(originalFieldOfView + extraFieldOfView * weight, 1f, 179f);
        if (motionBlur != null)
        {
            motionBlur.mode.Override(EffectiveBlurMode);
            float distanceScale = Mathf.Clamp(motionBlurDistanceMultiplier, 1f, MaxExtendedBlurIntensity);
            motionBlur.intensity.Override(Mathf.Clamp01(motionBlurIntensity) * distanceScale * weight);
            motionBlur.quality.Override(motionBlurQuality);
            motionBlur.clamp.Override(motionBlurClamp);
        }
        if (windLines != null)
            windLines.SetPresentation(showWindLines ? weight * windOpacity : 0f,
                windColor, windLineCount, windSpeed, windMinWidth, windMaxWidth);
    }

    private void UpdatePlayerMotionVectors(Player player)
    {
        if (!keepLocalPlayerSharp)
        {
            RestorePlayerMotionVectors();
            return;
        }
        if (protectedPlayer != player)
        {
            RestorePlayerMotionVectors();
            protectedPlayer = player;
        }
        if (Time.unscaledTime >= nextRendererRefresh)
        {
            nextRendererRefresh = Time.unscaledTime + 0.5f;
            // Include inactive equipment: switching existing hats/brooms is safe.
            // Reuse the list and occasionally discover any newly added renderers.
            player.GetComponentsInChildren(true, playerRenderers);
            foreach (Renderer renderer in playerRenderers)
                if ((renderer is MeshRenderer || renderer is SkinnedMeshRenderer) &&
                    !originalMotionModes.ContainsKey(renderer))
                    originalMotionModes.Add(renderer, renderer.motionVectorGenerationMode);
        }
        foreach (var entry in originalMotionModes)
            if (entry.Key != null)
                entry.Key.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
    }

    private void RestorePlayerMotionVectors()
    {
        foreach (var entry in originalMotionModes)
            if (entry.Key != null)
                entry.Key.motionVectorGenerationMode = entry.Value;
        originalMotionModes.Clear();
        playerRenderers.Clear();
        protectedPlayer = null;
        nextRendererRefresh = 0f;
    }

    private void OnDisable()
    {
        RestorePlayerMotionVectors();
        effectWeight = 0f;
        if (viewCamera != null)
            viewCamera.fieldOfView = originalFieldOfView;
        if (cameraData != null)
        {
            cameraData.renderPostProcessing = originalPostProcessing;
            cameraData.requiresDepthOption = originalDepthOption;
            cameraData.volumeLayerMask = originalVolumeLayerMask;
            if (viewCamera != null)
                viewCamera.SetVolumeFrameworkUpdateMode(originalVolumeUpdateMode);
        }
        if (windLines != null)
            windLines.SetPresentation(0f, windColor, windLineCount, windSpeed, windMinWidth, windMaxWidth);
        if (runtimeProfile != null)
        {
            if (speedVolume != null && speedVolume.HasInstantiatedProfile() && speedVolume.profile == runtimeProfile)
                speedVolume.profile = previousProfile;
            foreach (VolumeComponent component in runtimeProfile.components)
                if (component != null)
                    Destroy(component);
            Destroy(runtimeProfile);
        }
        runtimeProfile = null;
        previousProfile = null;
        motionBlur = null;
    }
}
