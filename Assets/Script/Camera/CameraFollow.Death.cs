using UnityEngine;

public partial class CameraFollow
{
    [Header("Falling death camera")]
    [Tooltip("사망 시 떨어지는 내 캐릭터를 바라보는 거리. 캐릭터가 화면에 들어오도록 필요한 경우 자동으로 늘어납니다.")]
    [SerializeField, Min(2f)] private float deathViewDistance = 8f;
    [Tooltip("기존 시점에서 낙하 관찰 시점으로 전환하는 시간입니다.")]
    [SerializeField, Min(0.05f)] private float deathViewBlendSeconds = .45f;
    private FallingDefeatVisual deathBody;
    private bool deathObserving;
    private Vector3 deathViewDirection, deathViewUp, deathViewStart;
    private Quaternion deathViewStartRotation;
    private float deathViewStarted;

    public void ObserveFallingDeath(Player player, FallingDefeatVisual body)
    {
        if (!isActiveAndEnabled || body == null || player == null || player != targetPlayer ||
            player.Object == null || !player.Object.IsValid || !player.Object.HasInputAuthority || player.IsAlive ||
            BattleManager.Instance == null || !BattleManager.Instance.IsGameplayActive) return;
        ResetSpeedShake();
        ResetTopSpeedKick();
        boundaryCameraFrozen = boundaryReturnBlending = false;
        deathBody = body;
        deathObserving = true;
        deathViewStart = transform.position;
        deathViewStartRotation = transform.rotation;
        deathViewDirection = transform.forward;
        deathViewUp = transform.up;
        deathViewStarted = Time.time;
    }

    private bool UpdateDeathObservation()
    {
        if (!deathObserving) return false;
        BattleManager battle = BattleManager.Instance;
        if (battle == null || !battle.IsGameplayActive ||
            (targetPlayer != null && targetPlayer.Object != null && targetPlayer.Object.IsValid && targetPlayer.IsAlive))
        {
            ClearDeathObservation();
            hasFollowAnchor = false;
            return false;
        }
        // The network Player despawns before the baked body. Keep following independently.
        // When the body expires, hold the final view until respawn calls SetTarget.
        if (deathBody == null) return true;
        Bounds bounds = deathBody.CameraBounds;
        Vector3 focus = bounds.center;
        float distance = DeathFramingDistance(bounds.extents.magnitude,
            viewCamera != null ? viewCamera.fieldOfView : 75f,
            viewCamera != null ? viewCamera.aspect : 16f / 9f,
            deathViewDistance);
        float progress = Mathf.SmoothStep(0f, 1f,
            (Time.time - deathViewStarted) / Mathf.Max(.05f, deathViewBlendSeconds));
        Vector3 desired = focus - deathViewDirection * distance;
        Vector3 position = Vector3.Lerp(deathViewStart, desired, progress);
        Vector3 look = focus - position;
        if (look.sqrMagnitude < .0001f) look = deathViewDirection;
        Vector3 up = Mathf.Abs(Vector3.Dot(look.normalized, deathViewUp)) < .99f
            ? deathViewUp : Vector3.Cross(deathViewDirection, deathViewUp);
        Quaternion rotation = Quaternion.Slerp(deathViewStartRotation, Quaternion.LookRotation(look, up), progress);
        transform.SetPositionAndRotation(position, rotation);
        return true;
    }

    internal static float DeathFramingDistance(float radius, float verticalFov, float aspect, float requested)
    {
        radius = Mathf.Max(.5f, radius) * 1.2f;
        float half = Mathf.Clamp(verticalFov, 1f, 179f) * Mathf.Deg2Rad * .5f;
        half = Mathf.Min(half, Mathf.Atan(Mathf.Tan(half) * Mathf.Max(.01f, aspect)));
        return Mathf.Max(Mathf.Max(2f, requested), radius / Mathf.Max(.001f, Mathf.Sin(half)));
    }

    private void ClearDeathObservation()
    {
        deathObserving = false;
        deathBody = null;
    }
}
