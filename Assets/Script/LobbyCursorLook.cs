using UnityEngine;

[DefaultExecutionOrder(150)]
[DisallowMultipleComponent]
public sealed class LobbyCursorLook : MonoBehaviour
{
    [SerializeField] private Camera previewCamera;
    [SerializeField, Range(0f, 45f)] private float headYaw = 24f;
    [SerializeField, Range(0f, 30f)] private float headPitch = 14f;
    [SerializeField, Range(0f, 15f)] private float chestYaw = 7f;
    [SerializeField, Range(0f, 10f)] private float chestPitch = 4f;
    [SerializeField, Min(.1f)] private float response = 6f;
    private Transform head, chest;
    private Quaternion headBase, chestBase;
    private bool applied;
    private Vector2 cursor;
    public bool FollowCursor { get; set; } = true;

    private void Start()
    {
        Animator animator = GetComponentInChildren<Animator>(true);
        if (animator != null && animator.isHuman && animator.avatar != null && animator.avatar.isValid)
        {
            head = animator.GetBoneTransform(HumanBodyBones.Head);
            chest = animator.GetBoneTransform(HumanBodyBones.UpperChest);
            if (chest == null) chest = animator.GetBoneTransform(HumanBodyBones.Chest);
        }
        if (head == null || chest == null)
            foreach (Transform bone in GetComponentsInChildren<Transform>(true))
            {
                if (head == null && bone.name == "Head") head = bone;
                if (chest == null && bone.name == "Chest") chest = bone;
            }
        if (previewCamera == null)
            foreach (Camera camera in FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (camera.name == "RobbyCamera01") { previewCamera = camera; break; }
    }

    private void Update() => Restore();

    private void LateUpdate()
    {
        Restore();
        if (head == null || previewCamera == null || !previewCamera.isActiveAndEnabled) return;
        Vector3 mouse = Input.mousePosition;
        bool onScreen = Application.isFocused && mouse.x >= 0 && mouse.y >= 0 && mouse.x <= Screen.width && mouse.y <= Screen.height;
        Vector2 desired = FollowCursor && onScreen ? new Vector2(
            Mathf.Clamp(mouse.x / Mathf.Max(1, Screen.width) * 2f - 1f, -1f, 1f),
            Mathf.Clamp(mouse.y / Mathf.Max(1, Screen.height) * 2f - 1f, -1f, 1f)) : Vector2.zero;
        cursor = Vector2.Lerp(cursor, desired, 1f - Mathf.Exp(-response * Time.unscaledDeltaTime));
        headBase = head.localRotation;
        if (chest != null)
        {
            chestBase = chest.localRotation;
            RotateTowardCursor(chest, chestYaw, chestPitch);
        }
        RotateTowardCursor(head, headYaw, headPitch);
        applied = true;
    }

    private void RotateTowardCursor(Transform bone, float yaw, float pitch)
    {
        bone.rotation = Quaternion.AngleAxis(-cursor.x * yaw, previewCamera.transform.up) *
            Quaternion.AngleAxis(cursor.y * pitch, previewCamera.transform.right) * bone.rotation;
    }

    private void Restore()
    {
        if (!applied) return;
        if (chest != null) chest.localRotation = chestBase;
        if (head != null) head.localRotation = headBase;
        applied = false;
    }
    private void OnDisable() { Restore(); cursor = Vector2.zero; }
}
