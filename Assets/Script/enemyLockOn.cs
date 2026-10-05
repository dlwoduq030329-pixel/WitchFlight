using Fusion;
using UnityEngine;

// Local screen/visibility selection. The same target travels with the release input.
public class enemyLockOn : MonoBehaviour
{
    [SerializeField, Min(0.01f)] private float targetRefreshInterval = 0.05f;
    [SerializeField, Min(1f)] private float maxTargetDistance = 250f;
    [SerializeField] private LayerMask obstructionMask = ~0;

    private Player owner;
    private Camera targetCamera;
    private Player currentTarget;
    private float nextRefreshTime;
    private readonly RaycastHit[] obstructionHits = new RaycastHit[32];
    public Player CurrentTarget => currentTarget;

    private void Awake() => owner = GetComponent<Player>();

    private void Update()
    {
        if (owner == null || owner.Object == null || !owner.Object.HasInputAuthority)
            return;

        if (BattleManager.Instance == null || !BattleManager.Instance.IsGameplayActive ||
            !owner.IsAlive || CombatPresentation.MenuOpen || !owner.SelectedMagicStats.requiresTarget ||
            !TryGetAlignedAim(out _))
        {
            currentTarget = null;
            return;
        }

        // Keep the last selection for the release tick, but validate it again in GetInputTarget.
        if (!Input.GetMouseButton(0))
            return;

        if (currentTarget != null && !IsVisible(currentTarget))
            currentTarget = null;

        if (currentTarget == null && Time.unscaledTime >= nextRefreshTime)
        {
            nextRefreshTime = Time.unscaledTime + targetRefreshInterval;
            currentTarget = FindClosestVisibleEnemy();
        }
    }

    public NetworkId GetInputTarget()
    {
        if (BattleManager.Instance == null || !BattleManager.Instance.IsGameplayActive ||
            owner == null || !owner.IsAlive || CombatPresentation.MenuOpen ||
            !owner.SelectedMagicStats.requiresTarget || !TryGetAlignedAim(out _))
        {
            currentTarget = null;
            return default;
        }

        // A wall or screen exit invalidates even a fully charged target on the release frame.
        if (currentTarget != null && !IsVisible(currentTarget))
            currentTarget = null;
        if (currentTarget == null && Input.GetMouseButton(0))
            currentTarget = FindClosestVisibleEnemy();

        NetworkId result = currentTarget != null ? currentTarget.Object.Id : default;
        if (!Input.GetMouseButton(0))
            currentTarget = null;
        return result;
    }

    private Player FindClosestVisibleEnemy()
    {
        Player best = null;
        float bestScore = float.PositiveInfinity;
        if (owner.Runner == null) return null;
        foreach (PlayerRef playerRef in owner.Runner.ActivePlayers)
        {
            if (!owner.Runner.TryGetPlayerObject(playerRef, out NetworkObject networkPlayer) ||
                networkPlayer == null || !networkPlayer.IsValid) continue;
            Player candidate = networkPlayer.GetComponent<Player>();
            if (!IsVisible(candidate))
                continue;
            Vector3 point = targetCamera.WorldToViewportPoint(candidate.LockAimPoint);
            float score = new Vector2(point.x - 0.5f, point.y - 0.5f).sqrMagnitude;
            if (score < bestScore)
            {
                bestScore = score;
                best = candidate;
            }
        }
        return best;
    }

    public bool TryGetAlignedAim(out Vector3 direction)
    {
        direction = Vector3.zero;
        if (owner == null || owner.Object == null || !owner.Object.IsValid || !owner.IsAlive ||
            owner.IsReturningToMap)
            return false;
        if (targetCamera == null) targetCamera = Camera.main;
        if (targetCamera == null || !targetCamera.TryGetComponent(out CameraFollow follow) ||
            follow.IsBoundaryPresentationActive)
            return false;

        // Check the circles actually displayed, and also the latest mouse goal so a
        // rapid turn cannot retain last frame's lock before the camera renders again.
        Vector3 displayedDirection = (follow.GetDisplayedAimPoint() - owner.LockAimPoint).normalized;
        direction = follow.TryGetSteeringInput(out Vector3 desired, out _) ? desired : displayedDirection;
        return owner.IsWithinLockAim(displayedDirection) && owner.IsWithinLockAim(direction);
    }

    public bool CanLockTarget(Player candidate)
    {
        return BattleManager.Instance != null && BattleManager.Instance.IsGameplayActive &&
            !CombatPresentation.MenuOpen && TryGetAlignedAim(out _) &&
            owner.SelectedMagicStats.requiresTarget && IsVisible(candidate);
    }

    private bool IsVisible(Player candidate)
    {
        if (targetCamera == null)
            targetCamera = Camera.main;
        if (targetCamera == null || candidate == null || candidate.Object == null ||
            !candidate.IsTargetableBy(owner))
            return false;

        MagicStatEntry stats = owner.SelectedMagicStats;
        float range = stats.range > 0f ? Mathf.Min(maxTargetDistance, stats.range) : maxTargetDistance;
        if ((candidate.LockAimPoint - owner.LockAimPoint).sqrMagnitude > range * range)
            return false;

        Vector3 point = targetCamera.WorldToViewportPoint(candidate.LockAimPoint);
        return point.z > 0f && point.x >= 0f && point.x <= 1f && point.y >= 0f && point.y <= 1f &&
            HasLineOfSight(targetCamera.transform.position, candidate) &&
            HasLineOfSight(owner.LockAimPoint, candidate);
    }

    private bool HasLineOfSight(Vector3 origin, Player candidate)
    {
        Vector3 delta = candidate.LockAimPoint - origin;
        float distance = delta.magnitude;
        if (distance < 0.01f)
            return true;

        int count = Physics.RaycastNonAlloc(origin, delta / distance, obstructionHits,
            distance, obstructionMask, QueryTriggerInteraction.Ignore);
        RaycastHit[] hits = obstructionHits;
        if (count == obstructionHits.Length)
        {
            hits = Physics.RaycastAll(origin, delta / distance, distance,
                obstructionMask, QueryTriggerInteraction.Ignore);
            count = hits.Length;
        }
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = hits[i];
            if (hit.collider == null || hit.transform.IsChildOf(owner.transform) ||
                hit.transform.IsChildOf(candidate.transform))
                continue;
            return false;
        }
        return true;
    }
}
