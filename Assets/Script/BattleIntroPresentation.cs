using System;
using System.Collections.Generic;
using Fusion;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

// Local-only presentation. Never instantiate a Player prefab or change its layers.
[DefaultExecutionOrder(50)]
[DisallowMultipleComponent]
public sealed class BattleIntroPresentation : MonoBehaviour
{
    private const int PortraitLayer = 31;

    [Header("Scene UI (assign your own; empty fields stay hidden)")]
    [SerializeField] private RawImage localPortrait;
    [SerializeField] private RawImage opponentPortrait;
    [SerializeField] private GameObject introPanel;
    [Tooltip("이름/장착 마법/프로필 UI. 비우면 Intro Panel 내부의 linkuserinfo를 사용합니다. 얼굴 RawImage는 기존 설정을 유지합니다.")]
    [SerializeField] private linkuserinfo introUserInfo;
    [SerializeField] private TMP_Text countdownText;
    [Tooltip("3·2·1 카운트다운 폰트. Battle 씬에는 DungGeunMo SDF를 연결합니다.")]
    [SerializeField] private TMP_FontAsset countdownFont;
    [SerializeField] private TMP_Text waitingText;
    [SerializeField] private TMP_Text soloText;

    [Header("Face framing")]
    [SerializeField, Range(128, 2048)] private int portraitResolution = 512;
    [SerializeField, Min(0.1f)] private float headDistance = 0.95f;
    [SerializeField, Min(0.05f)] private float portraitHalfHeight = 0.42f;
    [Tooltip("Offset from the head in the player's right/up/forward directions.")]
    [SerializeField] private Vector3 headOffset = new Vector3(0f, 0.08f, 0.06f);
    [SerializeField, Range(-60f, 60f)] private float portraitYaw;

    [Header("Presentation events")]
    [SerializeField] private UnityEvent onIntroStarted = new UnityEvent();
    [Tooltip("Runs whenever Intro ends, including a return to waiting if a player disconnects.")]
    [SerializeField] private UnityEvent onIntroEnded = new UnityEvent();

    

    private sealed class PortraitSnapshot
    {
        public Player Player;
        public GameObject Stage;
        public Camera Camera;
        public RenderTexture Texture;
        public readonly List<Mesh> OwnedMeshes = new List<Mesh>();
        public int RenderedFrame = -1;
        public bool IsReady => Texture != null && Texture.IsCreated() &&
                               RenderedFrame >= 0 && Time.frameCount > RenderedFrame;
    }

    private PortraitSnapshot localSnapshot;
    private PortraitSnapshot opponentSnapshot;
    private float nextPreparationTime;
    private BattleStartPhase displayedPhase;
    private bool hasDisplayedPhase;
    private bool reportedSnapshotFailure;
    private int displayedCountdown = -1;

    private void Awake()
    {
        SetUIPhase(BattleStartPhase.WaitingForPlayers);
    }

    private void OnEnable()
    {
        if (introUserInfo == null && introPanel != null)
            introUserInfo = introPanel.GetComponentInChildren<linkuserinfo>(true);
        RenderPipelineManager.endCameraRendering += OnCameraRendered;
        Camera.onPostRender += OnBuiltinCameraRendered;
        nextPreparationTime = 0f;
    }

    private void LateUpdate()
    {
        BattleManager battle = BattleManager.Instance;
        BattleStartPhase phase = battle != null ? battle.Phase : BattleStartPhase.WaitingForPlayers;
        if (!hasDisplayedPhase || displayedPhase != phase)
        {
            bool leavingIntro = hasDisplayedPhase && displayedPhase == BattleStartPhase.Intro;
            displayedPhase = phase;
            hasDisplayedPhase = true;
            SetUIPhase(phase);
            if (leavingIntro)
                onIntroEnded.Invoke();
            if (phase == BattleStartPhase.Intro)
                onIntroStarted.Invoke();
            if (phase != BattleStartPhase.WaitingForPlayers && phase != BattleStartPhase.Intro)
                ReleasePortraits();
        }

        if (phase == BattleStartPhase.Countdown && countdownText != null && battle != null)
        {
            int seconds = Mathf.Clamp(Mathf.CeilToInt(battle.PhaseRemainingSeconds), 1, 3);
            if (seconds != displayedCountdown)
            {
                countdownText.text = seconds.ToString();
                displayedCountdown = seconds;
            }
        }

        FreezeCompletedSnapshot(localSnapshot);
        FreezeCompletedSnapshot(opponentSnapshot);
        if (battle == null || (phase != BattleStartPhase.WaitingForPlayers && phase != BattleStartPhase.Intro) ||
            Time.unscaledTime < nextPreparationTime)
            return;

        // Baking runs after animation. Discovery and reliable readiness retries are at most once per second.
        nextPreparationTime = Time.unscaledTime + 1f;
        if (!TryResolvePlayers(battle, out Player local, out Player opponent))
            return;

        bool solo = battle.ExpectedPlayerCount <= 1;
        if (soloText != null)
            soloText.gameObject.SetActive(solo && phase == BattleStartPhase.Intro);

        if (!EnsureSnapshot(ref localSnapshot, local, 0) ||
            (!solo && !EnsureSnapshot(ref opponentSnapshot, opponent, 1)))
            return;

        if (localPortrait != null)
            localPortrait.texture = localSnapshot.Texture;
        if (opponentPortrait != null)
            opponentPortrait.texture = solo ? null : opponentSnapshot.Texture;
        UpdatePortraitVisibility(phase);

        if (phase == BattleStartPhase.WaitingForPlayers && localSnapshot.IsReady &&
            (solo || opponentSnapshot.IsReady) &&
            (introUserInfo == null || (introUserInfo.isActiveAndEnabled &&
             introUserInfo.HasUserInfoFor(local.Object.InputAuthority,
                 solo ? default : opponent.Object.InputAuthority, !solo))))
            local.ReportBattleSceneReady();
    }

    private static bool TryResolvePlayers(BattleManager battle, out Player local, out Player opponent)
    {
        local = null;
        opponent = null;
        NetworkGameManager network = NetworkGameManager.Instance;
        BattleFlag flag = BattleFlag.Instance;
        if (network == null || !network.IsBattleSceneLoaded || flag == null ||
            flag.Object == null || !flag.Object.IsValid || flag.Runner == null || !flag.Runner.IsRunning)
            return false;

        NetworkRunner runner = flag.Runner;
        int readyPlayers = 0;
        foreach (PlayerRef playerRef in runner.ActivePlayers)
        {
            if (!runner.TryGetPlayerObject(playerRef, out NetworkObject playerObject) ||
                playerObject == null || !playerObject.IsValid)
                return false;
            Player player = playerObject.GetComponent<Player>();
            if (player == null || player.Runner != runner || player.TeamIndex <= 0 || !player.IsPresentationReady)
                return false;
            readyPlayers++;
            if (playerObject.HasInputAuthority)
                local = player;
        }
        if (local == null || readyPlayers < Mathf.Max(1, battle.ExpectedPlayerCount))
            return false;
        if (battle.ExpectedPlayerCount <= 1)
            return true;

        foreach (PlayerRef playerRef in runner.ActivePlayers)
        {
            if (!runner.TryGetPlayerObject(playerRef, out NetworkObject playerObject))
                continue;
            Player candidate = playerObject.GetComponent<Player>();
            if (candidate != local && candidate.TeamIndex > 0 && candidate.TeamIndex != local.TeamIndex)
            {
                opponent = candidate;
                return true;
            }
        }
        return false;
    }

    private bool EnsureSnapshot(ref PortraitSnapshot snapshot, Player player, int slot)
    {
        if (snapshot != null && snapshot.Player == player && snapshot.Texture != null && snapshot.Texture.IsCreated())
            return true;
        ReleaseSnapshot(ref snapshot);
        try
        {
            snapshot = new PortraitSnapshot { Player = player };
            BuildSnapshot(snapshot, slot);
            reportedSnapshotFailure = false;
            return true;
        }
        catch (Exception exception)
        {
            ReleaseSnapshot(ref snapshot);
            if (!reportedSnapshotFailure)
            {
                Debug.LogWarning($"Battle portrait is not ready; waiting before retrying. {exception.Message}", this);
                reportedSnapshotFailure = true;
            }
            return false;
        }
    }

    private void BuildSnapshot(PortraitSnapshot snapshot, int slot)
    {
        Player player = snapshot.Player;
        Transform sourceRoot = player.transform;
        Vector3 stagePosition = new Vector3(10000f + slot * 50f, 10000f, 10000f);
        snapshot.Stage = new GameObject(slot == 0 ? "Local portrait stage" : "Opponent portrait stage");
        snapshot.Stage.layer = PortraitLayer;
        snapshot.Stage.transform.position = stagePosition;
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(snapshot.Stage, gameObject.scene);

        Transform visualRoot = new GameObject("Visual meshes only").transform;
        visualRoot.gameObject.layer = PortraitLayer;
        visualRoot.SetParent(snapshot.Stage.transform, false);
        visualRoot.localRotation = sourceRoot.rotation;
        visualRoot.localScale = sourceRoot.lossyScale;
        var transforms = new Dictionary<Transform, Transform> { { sourceRoot, visualRoot } };
        MaterialPropertyBlock properties = new MaterialPropertyBlock();
        Bounds visualBounds = new Bounds(sourceRoot.position, Vector3.zero);
        int meshCount = 0;

        foreach (Renderer source in player.GetComponentsInChildren<Renderer>())
        {
            if (!source.enabled || !source.gameObject.activeInHierarchy || source.GetComponentInParent<Canvas>() != null)
                continue;
            Mesh mesh;
            if (source is SkinnedMeshRenderer skin && skin.sharedMesh != null)
            {
                mesh = new Mesh { name = source.name + " intro snapshot" };
                snapshot.OwnedMeshes.Add(mesh);
                skin.BakeMesh(mesh, false);
            }
            else if (source is MeshRenderer && source.TryGetComponent(out MeshFilter sourceFilter))
                mesh = sourceFilter.sharedMesh;
            else
                continue;
            if (mesh == null || mesh.vertexCount == 0)
                continue;

            Transform copy = CopyVisualTransform(source.transform, transforms);
            // A separate leaf supports multiple renderers without adding gameplay components.
            GameObject meshObject = new GameObject(source.name + " portrait mesh", typeof(MeshFilter), typeof(MeshRenderer));
            meshObject.layer = PortraitLayer;
            meshObject.transform.SetParent(copy, false);
            meshObject.GetComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = meshObject.GetComponent<MeshRenderer>();
            Material[] materials = source.sharedMaterials;
            renderer.sharedMaterials = materials;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.allowOcclusionWhenDynamic = false;
            renderer.sortingLayerID = source.sortingLayerID;
            renderer.sortingOrder = source.sortingOrder;
            renderer.renderingLayerMask = source.renderingLayerMask;
            properties.Clear();
            source.GetPropertyBlock(properties);
            renderer.SetPropertyBlock(properties);
            for (int materialIndex = 0; materialIndex < materials.Length; materialIndex++)
            {
                properties.Clear();
                source.GetPropertyBlock(properties, materialIndex);
                if (!properties.isEmpty)
                    renderer.SetPropertyBlock(properties, materialIndex);
            }
            if (meshCount++ == 0)
                visualBounds = source.bounds;
            else
                visualBounds.Encapsulate(source.bounds);
        }
        if (meshCount == 0)
            throw new InvalidOperationException("The player has no active visual meshes yet.");

        Vector3 headPosition = FindHeadPosition(player, visualBounds);
        Vector3 target = stagePosition + headPosition - sourceRoot.position + sourceRoot.rotation * headOffset;
        Vector3 forward = Quaternion.AngleAxis(portraitYaw, sourceRoot.up) * sourceRoot.forward;

        snapshot.Texture = new RenderTexture(Mathf.Clamp(portraitResolution, 128, 2048),
            Mathf.Clamp(portraitResolution, 128, 2048), 24, RenderTextureFormat.ARGB32)
        {
            name = slot == 0 ? "Local face portrait" : "Opponent face portrait",
            antiAliasing = 1,
            useMipMap = false,
            autoGenerateMips = false,
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };
        if (!snapshot.Texture.Create())
            throw new InvalidOperationException("Could not allocate the face RenderTexture.");

        GameObject cameraObject = new GameObject("Portrait camera", typeof(Camera));
        cameraObject.layer = PortraitLayer;
        cameraObject.transform.SetParent(snapshot.Stage.transform, false);
        cameraObject.transform.position = target + forward * Mathf.Max(0.1f, headDistance);
        cameraObject.transform.rotation = Quaternion.LookRotation(-forward, sourceRoot.up);
        Camera camera = cameraObject.GetComponent<Camera>();
        snapshot.Camera = camera;
        camera.targetTexture = snapshot.Texture;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.clear;
        camera.cullingMask = 1 << PortraitLayer;
        camera.orthographic = true;
        camera.orthographicSize = Mathf.Max(0.05f, portraitHalfHeight);
        camera.nearClipPlane = 0.01f;
        camera.farClipPlane = Mathf.Max(5f, headDistance + 3f);
        camera.allowHDR = false;
        camera.allowMSAA = false;
        camera.useOcclusionCulling = false;
        camera.depth = -100f;
        UniversalAdditionalCameraData cameraData = cameraObject.AddComponent<UniversalAdditionalCameraData>();
        cameraData.renderPostProcessing = false;
        cameraData.renderShadows = false;
        cameraData.requiresColorOption = CameraOverrideOption.Off;
        cameraData.requiresDepthOption = CameraOverrideOption.Off;
        cameraData.antialiasing = AntialiasingMode.None;
        cameraData.allowXRRendering = false;
        cameraData.volumeLayerMask = 0;

        GameObject lightObject = new GameObject("Portrait fill light", typeof(Light));
        lightObject.layer = PortraitLayer;
        lightObject.transform.SetParent(snapshot.Stage.transform, false);
        lightObject.transform.position = cameraObject.transform.position + sourceRoot.up * 0.4f;
        Light light = lightObject.GetComponent<Light>();
        light.type = LightType.Point;
        light.range = 6f;
        light.intensity = 1.5f;
        light.shadows = LightShadows.None;
        light.cullingMask = 1 << PortraitLayer;
    }

    private static Transform CopyVisualTransform(Transform source, Dictionary<Transform, Transform> transforms)
    {
        if (transforms.TryGetValue(source, out Transform existing))
            return existing;
        Transform parent = CopyVisualTransform(source.parent, transforms);
        Transform copy = new GameObject(source.name).transform;
        copy.gameObject.layer = PortraitLayer;
        copy.SetParent(parent, false);
        copy.localPosition = source.localPosition;
        copy.localRotation = source.localRotation;
        copy.localScale = source.localScale;
        transforms.Add(source, copy);
        return copy;
    }

    private static Vector3 FindHeadPosition(Player player, Bounds bounds)
    {
        foreach (Animator animator in player.GetComponentsInChildren<Animator>())
        {
            if (!animator.isHuman || animator.avatar == null || !animator.avatar.isValid)
                continue;
            Transform head = animator.GetBoneTransform(HumanBodyBones.Head);
            if (head != null)
                return head.position;
        }
        foreach (Transform candidate in player.GetComponentsInChildren<Transform>())
            if (string.Equals(candidate.name, "Head", StringComparison.OrdinalIgnoreCase))
                return candidate.position;
        return bounds.center + player.transform.up * bounds.extents.y * 0.7f;
    }

    private void OnCameraRendered(ScriptableRenderContext context, Camera camera) => MarkRendered(camera);
    private void OnBuiltinCameraRendered(Camera camera) => MarkRendered(camera);

    private void MarkRendered(Camera camera)
    {
        if (localSnapshot != null && localSnapshot.Camera == camera)
            localSnapshot.RenderedFrame = Time.frameCount;
        if (opponentSnapshot != null && opponentSnapshot.Camera == camera)
            opponentSnapshot.RenderedFrame = Time.frameCount;
    }

    private static void FreezeCompletedSnapshot(PortraitSnapshot snapshot)
    {
        if (snapshot != null && snapshot.IsReady && snapshot.Camera != null)
            snapshot.Camera.enabled = false;
    }


    private void SetUIPhase(BattleStartPhase phase)
    {
        bool intro = phase == BattleStartPhase.Intro;
        bool countdown = phase == BattleStartPhase.Countdown;
        if (countdown) EnsureCountdownText();
        bool waiting = phase == BattleStartPhase.WaitingForPlayers;
        // Allow optional start buttons while waiting; lock flight input again for the countdown.
        if (phase != BattleStartPhase.Ended)
        {
            bool showCursor = waiting || (phase == BattleStartPhase.Playing && CombatPresentation.MenuOpen);
            Cursor.lockState = showCursor ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = showCursor;
        }
        if (introPanel != null)
        {
            bool containsActiveText = countdown && countdownText != null && countdownText.transform.IsChildOf(introPanel.transform);
            introPanel.SetActive(waiting || intro || containsActiveText);
        }
        UpdatePortraitVisibility(phase);
        if (soloText != null)
            soloText.gameObject.SetActive(intro && BattleManager.Instance != null && BattleManager.Instance.ExpectedPlayerCount <= 1);
        if (countdownText != null)
            countdownText.gameObject.SetActive(countdown);
        if (waitingText != null)
            waitingText.gameObject.SetActive(false);
        displayedCountdown = -1;
    }

    private void EnsureCountdownText()
    {
        if (countdownText == null)
        {
            var root = new GameObject("Battle countdown", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            root.transform.SetParent(transform, false);
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 90;
            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            var label = new GameObject("3 2 1", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            label.transform.SetParent(root.transform, false);
            countdownText = label.GetComponent<TextMeshProUGUI>();
            countdownText.alignment = TextAlignmentOptions.Center;
            countdownText.fontSize = 112f;
            countdownText.color = Color.white;
            countdownText.raycastTarget = false;
            countdownText.rectTransform.anchorMin = countdownText.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            countdownText.rectTransform.sizeDelta = new Vector2(240f, 180f);
            countdownText.rectTransform.anchoredPosition = Vector2.zero;
        }
        if (countdownFont != null) countdownText.font = countdownFont;
    }

    private void UpdatePortraitVisibility(BattleStartPhase phase)
    {
        bool visible = phase == BattleStartPhase.WaitingForPlayers || phase == BattleStartPhase.Intro;
        if (localPortrait != null)
            localPortrait.enabled = visible && localPortrait.texture != null;
        if (opponentPortrait != null)
            opponentPortrait.enabled = visible && opponentPortrait.texture != null &&
                (BattleManager.Instance == null || BattleManager.Instance.ExpectedPlayerCount > 1);
    }

    private void ReleasePortraits()
    {
        if (localPortrait != null)
            localPortrait.texture = null;
        if (opponentPortrait != null)
            opponentPortrait.texture = null;
        ReleaseSnapshot(ref localSnapshot);
        ReleaseSnapshot(ref opponentSnapshot);

        // Panel visibility is controlled by SetUIPhase, not texture cleanup.

    }

    private static void ReleaseSnapshot(ref PortraitSnapshot snapshot)
    {
        if (snapshot == null)
            return;
        if (snapshot.Camera != null)
        {
            snapshot.Camera.enabled = false;
            snapshot.Camera.targetTexture = null;
        }
        if (snapshot.Texture != null)
        {
            snapshot.Texture.Release();
            Destroy(snapshot.Texture);
        }
        foreach (Mesh mesh in snapshot.OwnedMeshes)
            if (mesh != null)
                Destroy(mesh);
        if (snapshot.Stage != null)
        {
            snapshot.Stage.SetActive(false);
            Destroy(snapshot.Stage);
        }
        snapshot = null;
    }

    private void OnDisable()
    {
        RenderPipelineManager.endCameraRendering -= OnCameraRendered;
        Camera.onPostRender -= OnBuiltinCameraRendered;
        ReleasePortraits();
        SetUIPhase(BattleStartPhase.Ended);
        hasDisplayedPhase = false;
    }

    private void OnDestroy()
    {
        ReleasePortraits();
    }
}
