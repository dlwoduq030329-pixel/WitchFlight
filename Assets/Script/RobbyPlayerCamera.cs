using UnityEngine;

// Scene-owned camera. Never attaches to the battle Player or battle cameras.
[DisallowMultipleComponent]
public sealed class RobbyPlayerCamera : MonoBehaviour
{
    [SerializeField] private Camera followCamera;
    [SerializeField] private AudioListener followListener;
    [SerializeField] private Camera mainCamera;
    [SerializeField] private AudioListener mainListener;
    [SerializeField] private GameObject robbyUI;
    [SerializeField] private Vector3 followOffset = new Vector3(-2.965f, 3.26f, -8.94f);
    [SerializeField] private Vector3 lookOffset = new Vector3(0f, 1.5f, 0f);
    [SerializeField, Min(0f)] private float followSharpness = 10f;
    [SerializeField, Min(0f)] private float mouseSensitivity = 3f;
    [SerializeField] private Vector2 pitchLimits = new Vector2(-65f, 70f);
    [SerializeField] private float verticalSensitivity = 3f;
    [Header("Lobby interaction camera")]
    [SerializeField, Min(.1f)] private float interactionCameraSeconds = .65f;
    private bool interactionCameraReady;
    private float returnSettledSeconds;
    [Header("Lobby copy of battle lock camera presentation")]
    [SerializeField] private bool lockCameraPresentation = true;
    [SerializeField] private float lockSideOffset = .9f, lockForwardOffset = 1.2f, lockFovReduction = 7f;
    [SerializeField, Min(.01f)] private float lockBlendSeconds = .2f;
    private float baseFov, lockWeight;
    private Vector3 targetingPosition;
    [Header("Lobby camera obstruction")]
    [SerializeField] private LayerMask obstructionLayers = ~0;
    [SerializeField, Min(0.01f)] private float obstructionRadius = 0.25f;
    [SerializeField, Min(0f)] private float obstructionPadding = 0.08f;
    private readonly RaycastHit[] obstructionHits = new RaycastHit[64];

    private static RobbyPlayerCamera instance;
    public static bool CanUsePractice => instance != null && instance.ControlsActive;
    // Chat blocks controls, not the lobby HUD or the camera's existing view.
    public static bool ShowsLobbyAvatar => instance != null && instance.showing;
    public static Camera PracticeCamera => instance != null ? instance.followCamera : null;
    public static Vector3 TargetingPosition => instance != null ? instance.targetingPosition : Vector3.zero;
    private float viewYaw;
    private float viewPitch;
    private Vector3 smoothedTargetPosition;
    private float obstructionFraction = 1f;
    private CursorLockMode previousCursorLock;
    private bool previousCursorVisible;

    private bool showing, mainCameraWasEnabled, mainListenerWasEnabled;
    private Transform previousTarget;

    private void Awake()
    {
        instance = this;
        if (followCamera != null) baseFov = followCamera.fieldOfView;
        if (followCamera != null) followCamera.enabled = false;
        if (followListener != null) followListener.enabled = false;
    }

    private bool ControlsActive => showing && Application.isFocused &&
        !MainRoomChatUI.BlocksLobbyInput &&
        Cursor.lockState == CursorLockMode.Locked && robbyUI != null && !robbyUI.activeInHierarchy &&
        NetworkGameManager.Instance != null && NetworkGameManager.Instance.IsWaitingInCodeRoom &&
        PlayerRobby.LocalPlayer != null;

    private void Update()
    {
        if (ControlsActive)
        {
            var player = PlayerRobby.LocalPlayer;
            if (player != null && player.InteractionCameraReturning) return;
            viewYaw = Mathf.Repeat(viewYaw + Input.GetAxisRaw("Mouse X") * mouseSensitivity, 360f);
            viewPitch = Mathf.Clamp(viewPitch - Input.GetAxisRaw("Mouse Y") * verticalSensitivity, pitchLimits.x, pitchLimits.y);
        }
    }

    public static RobbyInputData ConsumeInput()
    {
        var camera = instance;
        if (camera == null) return default;
        if (!camera.ControlsActive) return default;
        var input = new RobbyInputData
        {
            move = new Vector2((Input.GetKey(KeyCode.D) ? 1f : 0f) - (Input.GetKey(KeyCode.A) ? 1f : 0f),
                (Input.GetKey(KeyCode.W) ? 1f : 0f) - (Input.GetKey(KeyCode.S) ? 1f : 0f)),
            yaw = camera.viewYaw,
            pitch = camera.viewPitch,
            hasHeading = true,
            sprint = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift),
            standRequested = Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.D),
            interactionCameraReady = PlayerRobby.LocalPlayer.InteractionCameraReturning && camera.interactionCameraReady
        };
        return input;
    }

    private void LateUpdate()
    {
        var manager = NetworkGameManager.Instance;
        var player = PlayerRobby.LocalPlayer;
        bool visible = manager != null && manager.IsWaitingInCodeRoom &&
            robbyUI != null && !robbyUI.activeInHierarchy && player != null && player.isActiveAndEnabled;
        if (!visible) { SetShowing(false); previousTarget = null; return; }

        Transform target = player.transform;
        bool snap = !showing || previousTarget != target;
        if (snap) { viewYaw = player.SimulationYaw; viewPitch = 0f; }
        float cameraStep = 180f * Time.unscaledDeltaTime / Mathf.Max(.1f, interactionCameraSeconds);
        // Sitting never overrides the existing orbit. Mouse input rotates only the
        // camera while PlayerRobby keeps the seated character's heading fixed.
        if (player.InteractionCameraReturning)
        {
            float desiredYaw = player.SimulationYaw;
            viewYaw = Mathf.MoveTowardsAngle(viewYaw, desiredYaw, cameraStep);
            viewPitch = Mathf.MoveTowards(viewPitch, 0, cameraStep);
        }
        bool aligned = player.InteractionCameraReturning && Mathf.Abs(Mathf.DeltaAngle(viewYaw, player.SimulationYaw)) < .1f &&
            Mathf.Abs(viewPitch) < .1f;
        returnSettledSeconds = aligned ? returnSettledSeconds + Time.unscaledDeltaTime : 0;
        interactionCameraReady = aligned && returnSettledSeconds >= .15f;
        // Rotate the visible local avatar in the same frame as the camera, even if
        // no Fusion tick has run this frame. Never orbit around a stationary avatar.
        player.PresentLocalViewHeading(viewYaw);
        player.PresentLocalLookPitch(viewPitch);
        float blend = followSharpness <= 0f ? 1f : 1f - Mathf.Exp(-followSharpness * Time.unscaledDeltaTime);
        smoothedTargetPosition = snap ? target.position : Vector3.Lerp(smoothedTargetPosition, target.position, blend);
        Quaternion viewRotation = Quaternion.Euler(0f, viewYaw, 0f);
        Vector3 focusOffset = new Vector3(0f, lookOffset.y, lookOffset.z);
        Vector3 rearOffset = new Vector3(0f, followOffset.y, Mathf.Min(followOffset.z, lookOffset.z - 0.1f));
        Vector3 focus = smoothedTargetPosition + viewRotation * focusOffset;
        // Pitch orbits the camera around the head/torso pivot, never the body collider.
        Vector3 candidate = focus + Quaternion.Euler(viewPitch, viewYaw, 0f) * (rearOffset - focusOffset);
        targetingPosition = candidate;
        float desiredLock = lockCameraPresentation && !player.InteractionBusy ? Mathf.SmoothStep(0, 1, RobbyMagicPractice.DisplayedLockProgress) : 0;
        lockWeight = Mathf.Lerp(lockWeight, desiredLock, 1 - Mathf.Exp(-3 * Time.deltaTime / Mathf.Max(.01f, lockBlendSeconds)));
        Quaternion orbit = Quaternion.Euler(viewPitch, viewYaw, 0f);
        Vector3 shoulder = orbit * Vector3.right * (lockSideOffset * lockWeight);
        focus += shoulder;
        candidate += shoulder + orbit * Vector3.forward * Mathf.Min(lockForwardOffset * lockWeight, Vector3.Distance(candidate, focus) * .5f);
        if (followCamera != null) followCamera.fieldOfView = Mathf.Max(15, baseFov - lockFovReduction * lockWeight);
        Vector3 safe = ResolveObstruction(focus, candidate, target);
        float desiredDistance = Vector3.Distance(focus, candidate);
        float safeFraction = desiredDistance > 0.001f ? Vector3.Distance(focus, safe) / desiredDistance : 1f;
        obstructionFraction = snap || safeFraction < obstructionFraction
            ? safeFraction : Mathf.Lerp(obstructionFraction, safeFraction, blend);
        transform.position = Vector3.Lerp(focus, candidate, obstructionFraction);
        Vector3 direction = focus - transform.position;
        // Even when a wall pushes the camera onto its pivot, mouse yaw must not freeze.
        if (direction.sqrMagnitude <= 0.0001f) direction = viewRotation * (focusOffset - rearOffset);
        if (direction.sqrMagnitude > 0.0001f)
            transform.rotation = Quaternion.LookRotation(direction, Vector3.up);
        previousTarget = target;
        SetShowing(true);
    }

    private Vector3 ResolveObstruction(Vector3 focus, Vector3 candidate, Transform target)
    {
        Vector3 delta = candidate - focus;
        float distance = delta.magnitude;
        if (distance < 0.001f) return candidate;
        Vector3 direction = delta / distance;
        float radius = obstructionRadius;
        if (followCamera != null)
        {
            // Include the near-plane corners, not only the camera's center point.
            float near = followCamera.nearClipPlane;
            float halfHeight = near * Mathf.Tan(followCamera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            radius = Mathf.Max(radius, Mathf.Sqrt(near * near + halfHeight * halfHeight *
                (1f + followCamera.aspect * followCamera.aspect)));
        }
        int count = Physics.SphereCastNonAlloc(focus, radius, direction, obstructionHits,
            distance, obstructionLayers, QueryTriggerInteraction.Ignore);
        float allowed = distance;
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = obstructionHits[i];
            if (hit.collider == null || hit.collider.transform.IsChildOf(target)) continue;
            allowed = Mathf.Min(allowed, Mathf.Max(0f, hit.distance - obstructionPadding));
        }
        // A full buffer is not guaranteed to contain the closest hit. Fail inward.
        if (count == obstructionHits.Length) allowed = 0f;
        return focus + direction * allowed;
    }

    private void SetShowing(bool value)
    {
        if (showing == value) return;
        showing = value;
        if (value)
        {
            previousCursorLock = Cursor.lockState;
            previousCursorVisible = Cursor.visible;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            mainCameraWasEnabled = mainCamera != null && mainCamera.enabled;
            mainListenerWasEnabled = mainListener != null && mainListener.enabled;
            if (mainCamera != null) mainCamera.enabled = false;
            if (mainListener != null) mainListener.enabled = false;
        }
        else
        {
            lockWeight = 0;
            if (followCamera != null) followCamera.fieldOfView = baseFov;
            Cursor.lockState = previousCursorLock;
            Cursor.visible = previousCursorVisible;
            if (mainCamera != null) mainCamera.enabled = mainCameraWasEnabled;
            if (mainListener != null) mainListener.enabled = mainListenerWasEnabled;
        }
        if (followCamera != null) followCamera.enabled = value;
        if (followListener != null) followListener.enabled = value;
    }

    private void OnDisable() => SetShowing(false);
    private void OnDestroy() { if (instance == this) instance = null; }
}
