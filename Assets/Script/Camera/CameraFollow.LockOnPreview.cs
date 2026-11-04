using UnityEngine;

public partial class CameraFollow
{
    [Header("Lock-on camera experiment (off = previous camera)")]
    [Tooltip("테스트용 연출입니다. 끄면 기존 카메라 구도와 FOV를 그대로 사용합니다.")]
    [SerializeField] private bool enableLockOnCameraPreview = true;
    [Tooltip("카메라를 오른쪽으로 이동해 캐릭터를 화면 왼쪽에 배치합니다. 실제 캐릭터/조준 방향은 변경하지 않습니다.")]
    [SerializeField, Min(0f)] private float lockOnPreviewSideOffset = 0.9f;
    [Tooltip("화면 중앙에서 왼쪽으로 이동할 화면 너비 비율입니다. 0.2 = 캐릭터를 약 30% 위치에 배치합니다.")]
    [SerializeField, Range(0f, 0.35f)] private float lockOnPreviewScreenOffset = 0.2f;
    [Tooltip("타겟팅 중 어깨 너머 구도를 위해 카메라를 앞으로 당기는 거리입니다.")]
    [SerializeField, Min(0f)] private float lockOnPreviewForwardOffset = 1.2f;
    [SerializeField, Range(0f, 15f)] private float lockOnPreviewFovReduction = 7f;
    [SerializeField, Range(0f, 10f)] private float lockOnPreviewFastFovReduction = 3f;
    [SerializeField, Min(0.01f)] private float lockOnPreviewBlendSeconds = 0.2f;
    private float lockOnPreviewWeight;
    private float lockOnPreviewZoom;
    private enemyLockOn lockOnPreviewSelector;
    private Vector3 lockOnPreviewBaselinePosition;
    private int lockOnPreviewUpdatedFrame = -1;

    public float LockOnPreviewFovReduction => enableLockOnCameraPreview && isActiveAndEnabled &&
        targetPlayer != null && targetPlayer.Object != null && targetPlayer.Object.IsValid
        ? lockOnPreviewZoom : 0f;

    private void ResetLockOnCameraPreview()
    {
        lockOnPreviewWeight = 0f;
        lockOnPreviewZoom = 0f;
        lockOnPreviewUpdatedFrame = -1;
    }

    private float UpdateLockOnCameraPreview(bool allowed)
    {
        if (!enableLockOnCameraPreview || !allowed || targetPlayer == null ||
            targetPlayer.Object == null || !targetPlayer.Object.IsValid ||
            !targetPlayer.Object.HasInputAuthority || !targetPlayer.IsAlive ||
            targetPlayer.IsReturningToMap || CombatPresentation.MenuOpen)
        {
            ResetLockOnCameraPreview();
            return 0f;
        }
        // Fusion render and LateUpdate can both update the follow pose in one frame.
        if (lockOnPreviewUpdatedFrame != Time.frameCount)
        {
            lockOnPreviewUpdatedFrame = Time.frameCount;
            // Selection gates presentation; the predicted charge shown by the lock
            // ring drives its amount. No extra target search/raycast from rendering.
            bool charging = ControlSettings.Held(ControlAction.LockOn) &&
                !BattlePerkUI.BlocksCombatInput && !BattleEmoteUI.BlocksCombatInput &&
                lockOnPreviewSelector != null && lockOnPreviewSelector.HasSelectedTarget &&
                targetPlayer.SelectedMagicStats.requiresTarget && targetPlayer.CanAcquireMagicTarget();
            float zoom = targetPlayer.CurrentSpeedStage == 3 || targetPlayer.IsBoosting
                ? lockOnPreviewFastFovReduction : lockOnPreviewFovReduction;
            float progress = charging ? LockOnPreviewProgress(lockOnPreviewSelector.SelectedTargetProgress) : 0f;
            lockOnPreviewWeight = BlendLockOnPreview(lockOnPreviewWeight, progress,
                Time.unscaledDeltaTime, lockOnPreviewBlendSeconds);
            lockOnPreviewZoom = BlendLockOnPreview(lockOnPreviewZoom, progress * Mathf.Max(0f, zoom),
                Time.unscaledDeltaTime, lockOnPreviewBlendSeconds);
        }
        return lockOnPreviewWeight * Mathf.Max(0f, lockOnPreviewSideOffset);
    }

    private static float LockOnPreviewProgress(float progress)
    {
        return Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(progress));
    }

    private static float BlendLockOnPreview(float current, float desired, float deltaTime, float seconds)
    {
        float result = Mathf.Lerp(current, desired,
            1f - Mathf.Exp(-3f * Mathf.Max(0f, deltaTime) / Mathf.Max(0.01f, seconds)));
        return Mathf.Abs(result - desired) < 0.0001f ? desired : result;
    }

    private static float LockOnScreenOffset(float depth, float verticalFov, float aspect, float screenOffset)
    {
        return 2f * Mathf.Max(0.5f, depth) * Mathf.Tan(Mathf.Clamp(verticalFov, 1f, 179f) * Mathf.Deg2Rad * 0.5f) *
            Mathf.Max(0.1f, aspect) * Mathf.Clamp(screenOffset, 0f, 0.35f);
    }

    private Vector3 ApplyLockOnPreviewOffset(Vector3 offset, bool allowed)
    {
        float minimumSide = UpdateLockOnCameraPreview(allowed);
        if (lockOnPreviewWeight <= 0f) return offset;
        offset.z = Mathf.Min(-0.5f, offset.z + lockOnPreviewWeight * Mathf.Max(0f, lockOnPreviewForwardOffset));
        // A fixed 0.9m shift disappears visually with a wide FOV/boost dolly.
        // Scale to the visible width so the pilot stays clearly left at every speed.
        float side = viewCamera != null ? LockOnScreenOffset(-offset.z, viewCamera.fieldOfView,
            viewCamera.aspect, lockOnPreviewScreenOffset) * lockOnPreviewWeight : minimumSide;
        offset.x += Mathf.Max(minimumSide, side);
        return offset;
    }

    // Presentation zoom/shoulder displacement must not repeatedly push a selected
    // target out of the selection frustum, release it, then reacquire it on zoom-out.
    public Vector3 TargetingCameraPosition => enableLockOnCameraPreview && lockOnPreviewWeight > 0f
        ? lockOnPreviewBaselinePosition : transform.position;

    public Vector3 WorldToTargetingViewportPoint(Vector3 point)
    {
        if (!enableLockOnCameraPreview || lockOnPreviewWeight <= 0f)
            return viewCamera.WorldToViewportPoint(point);
        Vector3 local = Quaternion.Inverse(transform.rotation) * (point - TargetingCameraPosition);
        float halfHeight = Mathf.Tan(Mathf.Clamp(viewCamera.fieldOfView + lockOnPreviewZoom, 1f, 179f) *
            Mathf.Deg2Rad * 0.5f) * Mathf.Max(0.0001f, local.z);
        return new Vector3(0.5f + local.x / (2f * halfHeight * viewCamera.aspect),
            0.5f + local.y / (2f * halfHeight), local.z);
    }
}
