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
    [Header("Lobby camera obstruction")]
    [SerializeField] private LayerMask obstructionLayers = ~0;
    [SerializeField, Min(0.01f)] private float obstructionRadius = 0.25f;
    [SerializeField, Min(0f)] private float obstructionPadding = 0.08f;
    private readonly RaycastHit[] obstructionHits = new RaycastHit[64];

    private static RobbyPlayerCamera instance;
    private float pendingYaw;
    private CursorLockMode previousCursorLock;
    private bool previousCursorVisible;

    private bool showing, mainCameraWasEnabled, mainListenerWasEnabled;
    private Transform previousTarget;

    private void Awake()
    {
        instance = this;
        if (followCamera != null) followCamera.enabled = false;
        if (followListener != null) followListener.enabled = false;
    }

    private bool ControlsActive => showing && Application.isFocused &&
        Cursor.lockState == CursorLockMode.Locked && robbyUI != null && !robbyUI.activeInHierarchy &&
        NetworkGameManager.Instance != null && NetworkGameManager.Instance.IsWaitingInCodeRoom &&
        PlayerRobby.LocalPlayer != null;

    private void Update()
    {
        if (ControlsActive) pendingYaw += Input.GetAxisRaw("Mouse X") * mouseSensitivity;
        else pendingYaw = 0f;
    }

    public static RobbyInputData ConsumeInput()
    {
        var camera = instance;
        if (camera == null) return default;
        if (!camera.ControlsActive) { camera.pendingYaw = 0f; return default; }
        var input = new RobbyInputData
        {
            move = new Vector2((Input.GetKey(KeyCode.D) ? 1f : 0f) - (Input.GetKey(KeyCode.A) ? 1f : 0f),
                (Input.GetKey(KeyCode.W) ? 1f : 0f) - (Input.GetKey(KeyCode.S) ? 1f : 0f)),
            yaw = camera.pendingYaw
        };
        camera.pendingYaw = 0f;
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
        Vector3 position = target.position + target.rotation * followOffset;
        bool snap = !showing || previousTarget != target;
        float blend = followSharpness <= 0f ? 1f : 1f - Mathf.Exp(-followSharpness * Time.unscaledDeltaTime);
        Vector3 focus = target.position + lookOffset;
        Vector3 candidate = snap ? position : Vector3.Lerp(transform.position, position, blend);
        // Check AFTER smoothing so sideways movement/rotation cannot leave the camera
        // behind a wall. Pull inward immediately, ease outward via the normal follow.
        transform.position = ResolveObstruction(focus, candidate, target);
        Vector3 direction = focus - transform.position;
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
            pendingYaw = 0f;
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
