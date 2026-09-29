using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Serialization;

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

    private void OnEnable()
    {
        viewCamera = GetComponent<Camera>();
        if (viewCamera == null)
            return;
        originalCullingMask = viewCamera.cullingMask;
        int portraitLayer = LayerMask.NameToLayer("BattlePortrait");
        if (portraitLayer >= 0)
            viewCamera.cullingMask &= ~(1 << portraitLayer);
    }

    private void OnDisable()
    {
        if (viewCamera != null)
            viewCamera.cullingMask = originalCullingMask;
    }

    public void SetTarget(GameObject targetObject)
    {
        target = targetObject;
        targetPlayer = targetObject != null ? targetObject.GetComponent<Player>() : null;
        hasFollowAnchor = false;
        head = FindHead(targetObject);
        if (target != null)
            UpdateCamera(true);
    }

    public void setAim(bool value)
    {
        isAim = value;
    }

    public void SetLockTarget(Player targetPlayer)
    {
        // Lock-on selects a spell target; the chase camera always follows its own pilot.
        // Keep this API for existing CameraManager and enemyLockOn callers.
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

    private void UpdateCamera(bool forceSnap)
    {
        Transform targetTransform = target.transform;
        if (IsFaceView())
        {
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
        // Pitch and yaw follow the aircraft; decorative banking never rolls the horizon.
        Quaternion rawRotation = Quaternion.Euler(
            targetTransform.eulerAngles.x,
            targetTransform.eulerAngles.y,
            0f
        );
        bool snap = forceSnap || !hasFollowAnchor ||
            Vector3.SqrMagnitude(rawPosition - followAnchorPosition) >
            teleportSnapDistance * teleportSnapDistance;

        Vector3 desiredOffset = isAim ? aimOffset : followOffset;
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
        transform.SetPositionAndRotation(rawPosition + rawRotation * currentLocalOffset, rawRotation);
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
