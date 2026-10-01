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
    [Header("Top speed peripheral blur")]
    [SerializeField] private bool enablePeripheralBlur = true;
    [Tooltip("빗자루 기본 최고속도 대비 추가 블러가 시작되는 실제 전진 속도 비율입니다.")]
    [SerializeField, Range(0f, 0.99f)] private float peripheralBlurStartSpeedRatio = 0.75f;
    [Tooltip("최고속도에서 가장자리 배경의 모션 블러 길이를 추가합니다. 0이면 추가 효과를 끕니다.")]
    [SerializeField, Range(0f, 4f)] private float peripheralBlurStrength = 2f;
    [Tooltip("단계별 반경 사용 시 3단계 값, 끄면 모든 단계의 고정 반경입니다. 기존 저장값을 유지합니다.")]
    [InspectorName("Inner Radius Stage 3 / Fixed")]
    [SerializeField, Range(0f, 0.9f)] private float peripheralBlurInnerRadius = 0.45f;
    [Tooltip("현재 속도 단계에 따라 중앙 보호 반경을 바꿉니다. 실제 속도에 따른 블러 강도/시작 조건은 별개로 유지됩니다.")]
    [SerializeField] private bool useSpeedStageInnerRadius = true;
    [Tooltip("0단계 및 후진 선택 중의 반경입니다. 후진 블러를 새로 켜지는 않습니다.")]
    [SerializeField, Range(0f, 0.9f)] private float innerRadiusStage0 = 0.85f;
    [SerializeField, Range(0f, 0.9f)] private float innerRadiusStage1 = 0.75f;
    [SerializeField, Range(0f, 0.9f)] private float innerRadiusStage2 = 0.60f;
    [Tooltip("실제 부스트 중에는 단계별/고정 반경보다 부스트 반경을 우선 적용합니다.")]
    [SerializeField] private bool useBoostInnerRadius = true;
    [SerializeField, Range(0f, 0.9f)] private float innerRadiusBoost = 0.30f;
    [Tooltip("단계 변경 시 반경이 목표값에 도달하는 반응 속도입니다. 클수록 빠르게 전환합니다.")]
    [SerializeField, Min(0.01f)] private float innerRadiusTransitionSpeed = 5f;
    [Tooltip("추가 블러의 최대 샘플 이동 거리(화면 UV). 과도한 늘어짐을 제한합니다.")]
    [SerializeField, Range(0.01f, 0.2f)] private float peripheralBlurMaxDistance = 0.08f;
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
    private float peripheralWeight;
    private float currentInnerRadius;
    private bool hasInnerRadius;
    public bool HasPeripheralBlur => isActiveAndEnabled && enablePeripheralBlur &&
        peripheralWeight > 0.001f && peripheralBlurStrength > 0f;
    public Vector4 PeripheralBlurParameters => new Vector4(
        Mathf.Clamp(peripheralBlurStrength, 0f, 4f) * peripheralWeight,
        Mathf.Clamp(currentInnerRadius, 0f, 0.9f),
        Mathf.Clamp(peripheralBlurMaxDistance, 0.01f, 0.2f), 0f);
    private VolumeProfile previousProfile;
    private VolumeProfile runtimeProfile;
    private MotionBlur motionBlur;
    private Vignette altitudeVignette;
    private Vignette originalAltitudeVignette;
    private ColorAdjustments altitudeColor;
    private ColorAdjustments originalAltitudeColor;
    private Player altitudePlayer;
    private bool altitudeBlackoutLatched;
    private float altitudeFullBlack;
    private Player protectedPlayer;
    private float nextRendererRefresh;
    private readonly List<Renderer> playerRenderers = new();
    private readonly Dictionary<Renderer, MotionVectorGenerationMode> originalMotionModes = new();
    private const float MaxExtendedBlurIntensity = 3f;

    private MotionBlurMode EffectiveBlurMode => keepLocalPlayerSharp
        ? MotionBlurMode.CameraAndObjects : motionBlurMode;

    private void OnEnable()
    {
        hasInnerRadius = false;
        currentInnerRadius = Mathf.Clamp(peripheralBlurInnerRadius, 0f, 0.9f);
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
        if (!runtimeProfile.TryGet(out altitudeVignette))
            altitudeVignette = runtimeProfile.Add<Vignette>();
        if (!runtimeProfile.TryGet(out altitudeColor))
            altitudeColor = runtimeProfile.Add<ColorAdjustments>();
        originalAltitudeVignette = Instantiate(altitudeVignette);
        originalAltitudeColor = Instantiate(altitudeColor);
        speedVolume.profile = runtimeProfile;
    }


    private void LateUpdate()
    {
        Player player = Player.LocalPlayer;
        BattleManager battle = BattleManager.Instance;
        UpdateAltitudeEffects(player, battle);
        bool canShow = viewCamera.enabled && battle != null && battle.IsGameplayActive &&
            !CombatPresentation.MenuOpen && player != null && player.Object != null &&
            player.Object.IsValid && player.Object.HasInputAuthority && player.IsAlive;

        if (!canShow)
        {
            peripheralWeight = 0f;
            hasInnerRadius = false;
            RestorePlayerMotionVectors();
            // Do not carry an FOV/blur tail into portraits, respawn or the menu.
            ApplyWeight(0f);
            return;
        }

        UpdatePlayerMotionVectors(player);
        UpdateInnerRadius(player.CurrentSpeedStage, player.IsBoosting);

        // Extra peripheral blur follows real forward speed; selecting stage 3 at rest
        // does not trigger it. Boost reaches the same capped maximum, never an overload.
        float speedBlend = Mathf.InverseLerp(Mathf.Clamp(peripheralBlurStartSpeedRatio, 0f, 0.99f),
            1f, player.ForwardCruiseSpeedRatio);
        float peripheralTarget = enablePeripheralBlur ? Mathf.SmoothStep(0f, 1f, speedBlend) : 0f;
        peripheralWeight = Mathf.Lerp(peripheralWeight, peripheralTarget,
            1f - Mathf.Exp(-Mathf.Max(0.01f, transitionSpeed) * Time.unscaledDeltaTime));
        if (Mathf.Abs(peripheralWeight - peripheralTarget) < 0.001f) peripheralWeight = peripheralTarget;

        // Stage selection is intentional; this does not wait for actual max speed.
        // Reverse stage -3 and somebody else's stage never activate the effect.
        float target = player.CurrentSpeedStage == 3 ? 1f : 0f;
        float weight = Mathf.Lerp(effectWeight, target,
            1f - Mathf.Exp(-Mathf.Max(0.01f, transitionSpeed) * Time.unscaledDeltaTime));
        if (Mathf.Abs(weight - target) < 0.001f)
            weight = target;
        ApplyWeight(weight);
    }

    private void UpdateInnerRadius(int speedStage, bool isBoosting)
    {
        float targetRadius = peripheralBlurInnerRadius;
        if (useSpeedStageInnerRadius)
        {
            targetRadius = Mathf.Clamp(speedStage, 0, 3) switch
            {
                0 => innerRadiusStage0,
                1 => innerRadiusStage1,
                2 => innerRadiusStage2,
                _ => peripheralBlurInnerRadius
            };
        }
        // Follow actual boost state: release, mana exhaustion and stun end the override.
        if (useBoostInnerRadius && isBoosting)
            targetRadius = innerRadiusBoost;
        targetRadius = Mathf.Clamp(targetRadius, 0f, 0.9f);
        // Do not carry the previous player's/phase's radius into a new presentation.
        currentInnerRadius = !hasInnerRadius ? targetRadius : Mathf.Lerp(currentInnerRadius, targetRadius,
            1f - Mathf.Exp(-Mathf.Max(0.01f, innerRadiusTransitionSpeed) * Time.unscaledDeltaTime));
        if (Mathf.Abs(currentInnerRadius - targetRadius) < 0.001f) currentInnerRadius = targetRadius;
        hasInnerRadius = true;
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
            windLines.SetPresentation(showWindLines ? weight * windOpacity * (1f - altitudeFullBlack) : 0f,
                windColor, windLineCount, windSpeed, windMinWidth, windMaxWidth);
    }

    private void UpdateAltitudeEffects(Player player, BattleManager battle)
    {
        float darkness = 0f;
        if (battle == null || !battle.IsGameplayActive || NetworkGameManager.Instance == null ||
            !NetworkGameManager.Instance.IsMatching)
        {
            altitudePlayer = null;
            altitudeBlackoutLatched = false;
        }
        else if (player != null && player.Object != null && player.Object.IsValid && player.Object.HasInputAuthority)
        {
            if (altitudePlayer != player)
            {
                // A new network Player is spawned on respawn. Clear the previous death blackout.
                altitudePlayer = player;
                altitudeBlackoutLatched = false;
            }
            if (player.DiedFromAltitude)
                altitudeBlackoutLatched = true;
            MapBoundaryTable table = battle.MapBoundary;
            if (player.IsAlive && table != null)
                darkness = table.GetAltitudeDarkness(player.transform.position.y);
        }
        // Keep black even between despawning the dead object and spawning its replacement.
        if (altitudeBlackoutLatched)
            darkness = 1f;
        float edgeWeight = Mathf.SmoothStep(0f, 1f, darkness);
        altitudeFullBlack = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.8f, 1f, darkness));

        if (altitudeVignette == null || altitudeColor == null)
            return;
        RestoreVolumeComponent(altitudeVignette, originalAltitudeVignette);
        RestoreVolumeComponent(altitudeColor, originalAltitudeColor);
        if (darkness <= 0f)
            return;

        // Reuse the existing private runtime profile; no auto-generated UI or asset mutation.
        altitudeVignette.active = true;
        altitudeVignette.color.Override(Color.Lerp(originalAltitudeVignette.color.value, Color.black, edgeWeight));
        altitudeVignette.intensity.Override(Mathf.Lerp(originalAltitudeVignette.intensity.value, 1f, edgeWeight));
        altitudeVignette.smoothness.Override(Mathf.Lerp(originalAltitudeVignette.smoothness.value, 1f, edgeWeight));
        if (altitudeFullBlack > 0f)
        {
            altitudeColor.active = true;
            altitudeColor.colorFilter.Override(Color.Lerp(originalAltitudeColor.colorFilter.value, Color.black, altitudeFullBlack));
        }
    }

    private static void RestoreVolumeComponent(VolumeComponent target, VolumeComponent original)
    {
        target.active = original.active;
        for (int i = 0; i < target.parameters.Count; i++)
        {
            target.parameters[i].SetValue(original.parameters[i]);
            target.parameters[i].overrideState = original.parameters[i].overrideState;
        }
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
        peripheralWeight = 0f;
        hasInnerRadius = false;
        altitudePlayer = null;
        altitudeBlackoutLatched = false;
        altitudeFullBlack = 0f;
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
        if (originalAltitudeVignette != null) Destroy(originalAltitudeVignette);
        if (originalAltitudeColor != null) Destroy(originalAltitudeColor);
        originalAltitudeVignette = null;
        originalAltitudeColor = null;
        altitudeVignette = null;
        altitudeColor = null;
    }
}
