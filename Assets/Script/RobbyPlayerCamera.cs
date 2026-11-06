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

    private bool showing, mainCameraWasEnabled, mainListenerWasEnabled;
    private Transform previousTarget;

    private void Awake()
    {
        if (followCamera != null) followCamera.enabled = false;
        if (followListener != null) followListener.enabled = false;
    }

    private void LateUpdate()
    {
        var manager = NetworkGameManager.Instance;
        var player = PlayerRobby.LocalPlayer;
        bool visible = manager != null && manager.IsWaitingInCodeRoom &&
            robbyUI != null && !robbyUI.activeInHierarchy && player != null && player.isActiveAndEnabled;
        if (!visible) { SetShowing(false); previousTarget = null; return; }

        Transform target = player.transform;
        Vector3 position = target.position + followOffset;
        bool snap = !showing || previousTarget != target;
        float blend = followSharpness <= 0f ? 1f : 1f - Mathf.Exp(-followSharpness * Time.unscaledDeltaTime);
        transform.position = snap ? position : Vector3.Lerp(transform.position, position, blend);
        Vector3 direction = target.position + lookOffset - transform.position;
        if (direction.sqrMagnitude > 0.0001f)
            transform.rotation = Quaternion.LookRotation(direction, Vector3.up);
        previousTarget = target;
        SetShowing(true);
    }

    private void SetShowing(bool value)
    {
        if (showing == value) return;
        showing = value;
        if (value)
        {
            mainCameraWasEnabled = mainCamera != null && mainCamera.enabled;
            mainListenerWasEnabled = mainListener != null && mainListener.enabled;
            if (mainCamera != null) mainCamera.enabled = false;
            if (mainListener != null) mainListener.enabled = false;
        }
        else
        {
            if (mainCamera != null) mainCamera.enabled = mainCameraWasEnabled;
            if (mainListener != null) mainListener.enabled = mainListenerWasEnabled;
        }
        if (followCamera != null) followCamera.enabled = value;
        if (followListener != null) followListener.enabled = value;
    }

    private void OnDisable() => SetShowing(false);
}
