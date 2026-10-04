using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Serialization;

[DefaultExecutionOrder(-100)]
public class CameraFollow : MonoBehaviour
{
    [Tooltip("기본 시점으로 전환하는 반응성입니다. 비행 중 위치는 표시된 캐릭터를 지연 없이 따라갑니다.")]
    [SerializeField, Min(0.01f)] private float followSpeed = 20f;
    [Tooltip("조준 시점으로 전환하는 반응성입니다.")]
    [SerializeField, Min(0.01f)] private float aimFollowSpeed = 40f;
    [SerializeField, Min(0.1f)] private float teleportSnapDistance = 12f;
    [FormerlySerializedAs("lockLookSpeed")]
    [SerializeField, Min(0.01f)] private float rotationFollowSpeed = 60f;
    [SerializeField] private Vector3 followOffset = new Vector3(0f, 2f, -5f);
    [SerializeField] private Vector3 aimOffset = new Vector3(0.65f, 1.8f, -4f);

    [Header("Local speed shake (forward stages 2 and 3)")]
    [SerializeField] private bool enableSpeedShake = true;
    [Tooltip("2단계에서 목표로 하는 흔들림 배율입니다. 0이면 해당 단계에서 끕니다.")]
    [SerializeField, Min(0f)] private float speedShakeStage2Intensity = 0.35f;
    [Tooltip("3단계에서 목표로 하는 흔들림 배율입니다.")]
    [SerializeField, Min(0f)] private float speedShakeStage3Intensity = 1f;
    [Tooltip("강도 1일 때 카메라 로컬 X/Y/Z 이동 폭(미터)입니다. 렌더링에만 적용됩니다.")]
    [SerializeField] private Vector3 speedShakePositionAmplitude = new Vector3(0.015f, 0.015f, 0f);
    [Tooltip("강도 1일 때 X/Y/Z 회전 폭(도)입니다. 에임 방향에는 적용되지 않습니다.")]
    [SerializeField] private Vector3 speedShakeRotationAmplitude = new Vector3(0.12f, 0.12f, 0.2f);
    [SerializeField, Min(0.01f)] private float speedShakeFrequency = 8f;
    [Tooltip("강도가 목표값으로 변하는 반응 속도입니다. 낮을수록 천천히 켜지고 꺼집니다.")]
    [SerializeField, Min(0.01f)] private float speedShakeTransitionSpeed = 3f;

    [Header("Mouse aim flight")]
    [Tooltip("큰 조준점의 방향으로 캐릭터가 선회합니다. 끄면 이전 자유 시점/직접 피치 조작을 사용합니다.")]
    [SerializeField] private bool enableMouseAimSteering = true;
    [SerializeField, Min(0f)] private float mousePitchAimSensitivity = 2.5f;
    [Tooltip("마우스 X 입력당 목표 시점의 좌우 회전 각도입니다. 이전 모드에서는 카메라 궤도에만 적용합니다.")]
    [SerializeField, Min(0f)] private float mouseYawSensitivity = 2.5f;

    [Header("Local mouse orbit (legacy mode)")]
    [SerializeField] private bool enableMouseYawOrbit = true;

    [Header("Map boundary camera return")]
    [Tooltip("복귀 비행을 관찰할 기본 거리입니다. 캐릭터 크기와 FOV에 따라 더 멀어질 수 있습니다.")]
    [SerializeField, Min(1f)] private float boundaryViewDistance = 12f;
    [Tooltip("연출용 관찰 위치가 이동하는 반응 속도입니다. 평소 비행 추적에는 적용하지 않습니다.")]
    [SerializeField, Min(0.1f)] private float boundaryViewFollowSpeed = 4f;
    [Tooltip("캐릭터가 화면 가장자리에 닿지 않도록 확보할 구도 여유 배율입니다.")]
    [SerializeField, Min(1f)] private float boundaryFramingPadding = 1.25f;
    [Tooltip("맵 복귀 연출 종료 후 연출 카메라에서 플레이어 뒤로 이동하는 시간(초)입니다.")]
    [SerializeField, Min(0.05f)] private float boundaryReturnBlendDuration = 1f;

    [Header("Before Battle")]
    [SerializeField, Min(0.2f)] private float faceDistance = 1.5f;
    [SerializeField] private float faceHeightOffset = 0.08f;
    [SerializeField] private float fallbackHeadHeight = 1.2f;

    

    private bool isAim;
    private GameObject target;
    private Player targetPlayer;
    private Vector3 followAnchorPosition;
    private Vector3 currentLocalOffset;
    private bool hasFollowAnchor;
    private Transform head;
    private bool wasFaceView;
    private Camera viewCamera;
    private int originalCullingMask;
    private bool boundaryCameraFrozen;
    private Vector3 boundaryCameraPosition;
    private Quaternion boundaryCameraRotation;
    private bool boundaryReturnBlending;
    private float boundaryReturnBlendStarted;
    private Vector3 boundaryViewDirection;
    private Vector3 boundaryViewUp;
    private Renderer[] targetRenderers;
    private float mouseOrbitYaw;
    private Quaternion mouseAimRotation = Quaternion.identity;
    private float speedShakeIntensity;
    private int speedShakeUpdatedFrame = -1;
    private bool speedShakeViewApplied;

    public bool IsBoundaryPresentationActive => boundaryCameraFrozen || boundaryReturnBlending;
    public float AimProjectionDistance => targetPlayer != null && targetPlayer.Object != null && targetPlayer.Object.IsValid
        ? (targetPlayer.SelectedMagicStats.range > 1f ? targetPlayer.SelectedMagicStats.range : 250f) : 250f;

    public bool TryGetSteeringInput(out Vector3 direction, out Vector3 up)
    {
        direction = Vector3.forward;
        up = Vector3.up;
        if (!isActiveAndEnabled || !enableMouseAimSteering || targetPlayer == null ||
            targetPlayer.Object == null || !targetPlayer.Object.IsValid || !targetPlayer.Object.HasInputAuthority)
            return false;
        // Evaluate the desired camera pose at this player's current position rather than
        // steering toward last frame's camera (which would drift while flying forward).
        Vector3 offset = isAim ? aimOffset : followOffset;
        offset.z = Mathf.Min(-0.5f, offset.z);
        Vector3 point = target.transform.position + mouseAimRotation * (offset + Vector3.forward * AimProjectionDistance);
        direction = (point - targetPlayer.LockAimPoint).normalized;
        up = mouseAimRotation * Vector3.up;
        return true;
    }

    public Vector3 GetDisplayedAimPoint()
    {
        return transform.position + transform.forward * AimProjectionDistance;
    }

    private void OnEnable()
    {
        viewCamera = GetComponent<Camera>();
        if (viewCamera == null)
            return;
        originalCullingMask = viewCamera.cullingMask;
        int portraitLayer = LayerMask.NameToLayer("BattlePortrait");
        if (portraitLayer >= 0)
            viewCamera.cullingMask &= ~(1 << portraitLayer);
        ResetSpeedShake();
        RenderPipelineManager.beginCameraRendering += BeginSpeedShakeRendering;
        RenderPipelineManager.endCameraRendering += EndSpeedShakeRendering;
    }

    private void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering -= BeginSpeedShakeRendering;
        RenderPipelineManager.endCameraRendering -= EndSpeedShakeRendering;
        ResetSpeedShake();
        mouseOrbitYaw = 0f;
        boundaryCameraFrozen = false;
        boundaryReturnBlending = false;
        if (viewCamera != null)
            viewCamera.cullingMask = originalCullingMask;
    }

    public void SetTarget(GameObject targetObject)
    {
        ResetSpeedShake();
        mouseOrbitYaw = 0f;
        boundaryCameraFrozen = false;
        boundaryReturnBlending = false;
        target = targetObject;
        targetPlayer = targetObject != null ? targetObject.GetComponent<Player>() : null;
        mouseAimRotation = targetObject != null ? targetObject.transform.rotation : Quaternion.identity;
        hasFollowAnchor = false;
        head = FindHead(targetObject);
        targetRenderers = targetObject != null ? targetObject.GetComponentsInChildren<Renderer>(true) : null;
        if (target != null)
            UpdateCamera(true);
    }

    public void setAim(bool value)
    {
        isAim = value;
    }

    public void SetLockTarget(Player targetPlayer)
    {
        // Lock-on selects a spell target; it never overrides the local camera orbit.
        // Keep this API for existing CameraManager and enemyLockOn callers.
    }

    private void Update()
    {
        if (!enableMouseAimSteering && !enableMouseYawOrbit)
        {
            mouseOrbitYaw = 0f;
            return;
        }
        if (targetPlayer == null || targetPlayer.Object == null || !targetPlayer.Object.IsValid ||
            !targetPlayer.Object.HasInputAuthority || !targetPlayer.IsAlive ||
            BattleManager.Instance == null || !BattleManager.Instance.IsGameplayActive ||
            CombatPresentation.MenuOpen || Cursor.lockState != CursorLockMode.Locked ||
            targetPlayer.IsReturningToMap || boundaryCameraFrozen || boundaryReturnBlending)
            return;

        if (enableMouseAimSteering)
        {
            float keyboardYaw = (Input.GetKey(KeyCode.D) ? 1f : 0f) - (Input.GetKey(KeyCode.A) ? 1f : 0f);
            float yaw = Input.GetAxisRaw("Mouse X") * Mathf.Max(0f, mouseYawSensitivity) +
                keyboardYaw * targetPlayer.SteeringTurnRate * Time.deltaTime;
            float pitch = -Input.GetAxisRaw("Mouse Y") * Mathf.Max(0f, mousePitchAimSensitivity);
            // Keep a quaternion goal independent of the pilot. Reading Euler angles at
            // vertical flight would flip the desired heading and create an endless chase.
            mouseAimRotation = (Quaternion.AngleAxis(yaw, Vector3.up) * mouseAimRotation *
                Quaternion.AngleAxis(pitch, Vector3.right)).normalized;
            return;
        }

        // Mouse axes are frame deltas: do not multiply by deltaTime or consume per network tick.
        // This is camera-local state and must never rotate the Player transform.
        mouseOrbitYaw = Mathf.Repeat(mouseOrbitYaw + Input.GetAxisRaw("Mouse X") *
            Mathf.Max(0f, mouseYawSensitivity) + 180f, 360f) - 180f;
    }

    private void LateUpdate()
    {
        if (target == null)
            return;
        // Face framing needs animated bones. Networked flight is updated exclusively
        // from Player.IAfterRender, after every NetworkTransform has finished rendering.
        if (IsFaceView() || targetPlayer == null || targetPlayer.Object == null || !targetPlayer.Object.IsValid)
            UpdateCamera(false);
    }

    public void FollowRenderedPlayer(Player player)
    {
        if (!isActiveAndEnabled || target == null || player != targetPlayer || IsFaceView())
            return;
        UpdateCamera(false);
    }

    private static bool IsFaceView()
    {
        BattleManager battle = BattleManager.Instance;
        return battle != null && (battle.Phase == BattleStartPhase.WaitingForPlayers ||
            battle.Phase == BattleStartPhase.Intro);
    }

    private void BeginSpeedShakeRendering(ScriptableRenderContext context, Camera renderingCamera)
    {
        if (renderingCamera != viewCamera) return;
        // Remove a leftover override before computing the current frame's base pose.
        RestoreSpeedShakeView();
        BattleManager battle = BattleManager.Instance;
        bool canShake = isActiveAndEnabled && enableSpeedShake && battle != null && battle.IsGameplayActive &&
            !CombatPresentation.MenuOpen && !IsBoundaryPresentationActive && targetPlayer != null &&
            targetPlayer.Object != null && targetPlayer.Object.IsValid && targetPlayer.Object.HasInputAuthority &&
            targetPlayer.IsAlive && !targetPlayer.IsReturningToMap;
        if (!canShake)
        {
            ResetSpeedShake();
            return;
        }

        // Multiple renders in one frame must not speed up the fade.
        if (speedShakeUpdatedFrame != Time.frameCount)
        {
            speedShakeUpdatedFrame = Time.frameCount;
            int stage = targetPlayer.CurrentSpeedStage;
            float targetIntensity = stage >= 3 ? Mathf.Max(0f, speedShakeStage3Intensity)
                : stage == 2 ? Mathf.Max(0f, speedShakeStage2Intensity) : 0f;
            speedShakeIntensity = Mathf.Lerp(speedShakeIntensity, targetIntensity,
                GetExponentialBlend(speedShakeTransitionSpeed));
            if (Mathf.Abs(speedShakeIntensity - targetIntensity) < 0.0001f)
                speedShakeIntensity = targetIntensity;
        }
        if (speedShakeIntensity <= 0.0001f) return;

        float phase = Time.unscaledTime * Mathf.Max(0.01f, speedShakeFrequency);
        Vector3 positionNoise = new Vector3(ShakeNoise(phase, 11f), ShakeNoise(phase, 29f), ShakeNoise(phase, 47f));
        Vector3 rotationNoise = new Vector3(ShakeNoise(phase, 61f), ShakeNoise(phase, 83f), ShakeNoise(phase, 107f));
        Vector3 localOffset = Vector3.Scale(positionNoise, speedShakePositionAmplitude) * speedShakeIntensity;
        Quaternion localRotation = Quaternion.Euler(
            Vector3.Scale(rotationNoise, speedShakeRotationAmplitude) * speedShakeIntensity);

        // Only URP rendering sees the shaken view. Transform, mouseAimRotation,
        // steering, lock-on tests and firing input continue using the unshaken pose.
        Vector3 renderPosition = transform.position + transform.rotation * localOffset;
        Quaternion renderRotation = transform.rotation * localRotation;
        // Unity camera space looks along -Z, unlike Transform.forward (+Z).
        viewCamera.worldToCameraMatrix = Matrix4x4.Scale(new Vector3(1f, 1f, -1f)) *
            Matrix4x4.TRS(renderPosition, renderRotation, Vector3.one).inverse;
        speedShakeViewApplied = true;
    }

    private void EndSpeedShakeRendering(ScriptableRenderContext context, Camera renderingCamera)
    {
        if (renderingCamera == viewCamera) RestoreSpeedShakeView();
    }

    private static float ShakeNoise(float phase, float seed)
        => Mathf.PerlinNoise(phase, seed) * 2f - 1f;

    private void RestoreSpeedShakeView()
    {
        if (!speedShakeViewApplied) return;
        // Reset, rather than assigning last frame's matrix, so Transform tracking stays live.
        if (viewCamera != null) viewCamera.ResetWorldToCameraMatrix();
        speedShakeViewApplied = false;
    }

    private void ResetSpeedShake()
    {
        RestoreSpeedShakeView();
        speedShakeIntensity = 0f;
        speedShakeUpdatedFrame = -1;
    }

    private void UpdateCamera(bool forceSnap)
    {
        bool returning = targetPlayer != null && targetPlayer.Object != null && targetPlayer.Object.IsValid &&
            targetPlayer.IsAlive && targetPlayer.IsReturningToMap &&
            BattleManager.Instance != null && BattleManager.Instance.IsGameplayActive;
        if (returning)
        {
            if (!boundaryCameraFrozen)
            {
                boundaryViewDirection = transform.forward;
                boundaryViewUp = transform.up;
                mouseOrbitYaw = 0f;
                boundaryCameraFrozen = true;
                boundaryReturnBlending = false;
            }
            UpdateBoundaryObservation();
            return;
        }
        if (boundaryCameraFrozen)
        {
            // The observation camera now moves. Blend from its LAST pose, not the entry pose.
            boundaryCameraPosition = transform.position;
            boundaryCameraRotation = transform.rotation;
            boundaryCameraFrozen = false;
            hasFollowAnchor = false;
            isAim = false;
            boundaryReturnBlending = true;
            boundaryReturnBlendStarted = Time.unscaledTime;
            mouseAimRotation = target.transform.rotation;
        }
        // Respawn/target changes, death and phase changes must not retain an old blend.
        if (forceSnap || targetPlayer == null || targetPlayer.Object == null || !targetPlayer.Object.IsValid ||
            !targetPlayer.IsAlive || BattleManager.Instance == null || !BattleManager.Instance.IsGameplayActive)
            boundaryReturnBlending = false;
        Transform targetTransform = target.transform;
        if (IsFaceView())
        {
            mouseOrbitYaw = 0f;
            mouseAimRotation = targetTransform.rotation;
            Vector3 lookAt = head != null ? head.position : targetTransform.position + Vector3.up * fallbackHeadHeight;
            Vector3 forward = Quaternion.Euler(0f, targetTransform.eulerAngles.y, 0f) * Vector3.forward;
            Vector3 position = lookAt + forward * faceDistance + Vector3.up * faceHeightOffset;
            Quaternion rotation = Quaternion.LookRotation(lookAt - position, Vector3.up);
            if (forceSnap || !wasFaceView)
                transform.SetPositionAndRotation(position, rotation);
            else
            {
                transform.position = Vector3.Lerp(transform.position, position, GetExponentialBlend(followSpeed));
                transform.rotation = Quaternion.Slerp(transform.rotation, rotation, GetExponentialBlend(rotationFollowSpeed));
            }
            wasFaceView = true;
            hasFollowAnchor = false;
            return;
        }
        // Countdown begins behind the pilot, without sweeping through the model's face.
        forceSnap |= wasFaceView;
        wasFaceView = false;
        Vector3 rawPosition = targetTransform.position;
        // The visual bank is on child transforms. Preserve the actual root quaternion,
        // including inverted flight, instead of discarding Euler Z at vertical pitch.
        Quaternion rawRotation = enableMouseAimSteering && !boundaryReturnBlending
            ? mouseAimRotation : targetTransform.rotation;
        // Apply the same world-up yaw to both the camera offset and view rotation.
        // The pilot stays in the same framing while mouse X orbits around them.
        if (!enableMouseAimSteering)
            rawRotation = Quaternion.AngleAxis(enableMouseYawOrbit ? mouseOrbitYaw : 0f, Vector3.up) * rawRotation;
        bool teleported = hasFollowAnchor && Vector3.SqrMagnitude(rawPosition - followAnchorPosition) >
            teleportSnapDistance * teleportSnapDistance;
        bool snap = forceSnap || !hasFollowAnchor || teleported;
        if (teleported)
            boundaryReturnBlending = false;

        Vector3 desiredOffset = isAim && !boundaryReturnBlending ? aimOffset : followOffset;
        desiredOffset.z = Mathf.Min(-0.5f, desiredOffset.z);

        if (snap)
        {
            currentLocalOffset = desiredOffset;
            hasFollowAnchor = true;
        }
        else
        {
            float sharpness = isAim ? aimFollowSpeed : followSpeed;
            currentLocalOffset = Vector3.Lerp(currentLocalOffset, desiredOffset, GetExponentialBlend(sharpness));
        }

        // NetworkTransform already interpolated this pose. A second world-space lag
        // makes the pilot drift against the camera, especially with variable frame times.
        // Translate and rotate from exactly the same displayed pose instead.
        followAnchorPosition = rawPosition;
        Vector3 desiredPosition = rawPosition + rawRotation * currentLocalOffset;
        if (boundaryReturnBlending)
        {
            float progress = Mathf.Clamp01((Time.unscaledTime - boundaryReturnBlendStarted) /
                Mathf.Max(0.05f, boundaryReturnBlendDuration));
            float blend = Mathf.SmoothStep(0f, 1f, progress);
            // Blend toward the CURRENT rendered pose so a moving pilot never leaves
            // the camera chasing an obsolete endpoint. Normal flight gets no extra lag.
            transform.SetPositionAndRotation(
                Vector3.Lerp(boundaryCameraPosition, desiredPosition, blend),
                Quaternion.Slerp(boundaryCameraRotation, rawRotation, blend));
            if (progress >= 1f)
            {
                boundaryReturnBlending = false;
                mouseAimRotation = rawRotation;
            }
            return;
        }
        transform.SetPositionAndRotation(desiredPosition, rawRotation);
    }

    private void UpdateBoundaryObservation()
    {
        Bounds bounds = new Bounds(target.transform.position + Vector3.up * 0.8f, Vector3.one * 2f);
        bool hasBounds = false;
        if (targetRenderers != null)
        {
            foreach (Renderer renderer in targetRenderers)
            {
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy ||
                    (renderer is not SkinnedMeshRenderer && renderer is not MeshRenderer) ||
                    renderer.GetComponentInParent<Canvas>() != null)
                    continue;
                if (!hasBounds)
                {
                    bounds = renderer.bounds;
                    hasBounds = true;
                }
                else bounds.Encapsulate(renderer.bounds);
            }
        }

        Vector3 focus = bounds.center;
        float radius = Mathf.Max(1f, bounds.extents.magnitude) * Mathf.Max(1f, boundaryFramingPadding);
        float minimumDistance = radius + 0.5f;
        if (viewCamera != null && !viewCamera.orthographic)
        {
            // Fit a bounding sphere inside both the vertical and horizontal FOV.
            float verticalHalfAngle = Mathf.Clamp(viewCamera.fieldOfView, 1f, 179f) * Mathf.Deg2Rad * 0.5f;
            float horizontalHalfAngle = Mathf.Atan(Mathf.Tan(verticalHalfAngle) * Mathf.Max(0.01f, viewCamera.aspect));
            float halfAngle = Mathf.Min(verticalHalfAngle, horizontalHalfAngle);
            minimumDistance = Mathf.Max(radius / Mathf.Max(0.001f, Mathf.Sin(halfAngle)),
                radius + viewCamera.nearClipPlane + 0.5f);
        }

        float distance = Mathf.Max(boundaryViewDistance, minimumDistance);
        // Keep the observation direction independent of the pilot's rotating yaw.
        // This shows the U-turn rather than orbiting behind it as a chase camera would.
        Vector3 desiredPosition = focus - boundaryViewDirection * distance;
        Vector3 position = Vector3.Lerp(transform.position, desiredPosition,
            GetExponentialBlend(boundaryViewFollowSpeed));
        Vector3 offset = position - focus;
        if (offset.sqrMagnitude < minimumDistance * minimumDistance)
            position = focus + (offset.sqrMagnitude > 0.0001f ? offset.normalized : -boundaryViewDirection) * minimumDistance;

        Vector3 lookDirection = (focus - position).normalized;
        Vector3 up = Mathf.Abs(Vector3.Dot(lookDirection, boundaryViewUp)) < 0.99f
            ? boundaryViewUp : boundaryViewDirection;
        // Exact aiming is intentional: smoothing rotation can let a fast pilot leave the frame.
        transform.SetPositionAndRotation(position, Quaternion.LookRotation(lookDirection, up));
    }

    private static float GetExponentialBlend(float sharpness)
    {
        return 1f - Mathf.Exp(-Mathf.Max(0.01f, sharpness) * Time.unscaledDeltaTime);
    }

    private static Transform FindHead(GameObject targetObject)
    {
        if (targetObject == null)
            return null;
        Animator animator = targetObject.GetComponentInChildren<Animator>();
        if (animator != null && animator.isHuman)
        {
            Transform bone = animator.GetBoneTransform(HumanBodyBones.Head);
            if (bone != null)
                return bone;
        }
        foreach (Transform child in targetObject.GetComponentsInChildren<Transform>(true))
            if (child.name == "Head")
                return child;
        return null;
    }
}
