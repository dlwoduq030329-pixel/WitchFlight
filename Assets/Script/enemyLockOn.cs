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

    // Called from OnInput, including latched clicks that happened between network ticks.
    private bool wasHeld;
    private MagicType sampledMagic;
    public NetworkId GetInputTarget() => GetInputTarget(Input.GetMouseButton(0));

    public NetworkId GetInputTarget(bool held)
    {
        if (owner == null || owner.Object == null || !owner.Object.IsValid ||
            !owner.Object.HasInputAuthority || BattleManager.Instance == null ||
            !BattleManager.Instance.IsGameplayActive || !owner.IsAlive || CombatPresentation.MenuOpen ||
            !owner.CanAcquireMagicTarget() || !TryGetAlignedAim(out _))
        {
            currentTarget = null;
            wasHeld = false;
            return default;
        }

        if (sampledMagic != owner.GetSelectedMagic())
        {
            sampledMagic = owner.GetSelectedMagic();
            currentTarget = null;
            wasHeld = false;
        }
        if (!wasHeld && held) currentTarget = FindClosestVisibleEnemy();
        if (currentTarget != null && !IsVisible(currentTarget)) currentTarget = null;
        if (held && currentTarget == null && Time.unscaledTime >= nextRefreshTime)
        {
            nextRefreshTime = Time.unscaledTime + targetRefreshInterval;
            currentTarget = FindClosestVisibleEnemy();
        }
        NetworkId result = currentTarget != null ? currentTarget.Object.Id : default;
        wasHeld = held;
        if (!held) currentTarget = null; // Return the last valid selection on the release tick.
        return result;
    }

    private Player FindClosestVisibleEnemy()
    {
        Player best = null;
        float bestScore = float.PositiveInfinity;
        if (owner.Runner == null) return null;
        foreach (Player candidate in Player.ActiveCombatants)
        {
            if (candidate == null || candidate.Runner != owner.Runner || !IsVisible(candidate))
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

        Vector3 displayedDirection = (follow.GetDisplayedAimPoint() - owner.LockAimPoint).normalized;
        direction = follow.TryGetSteeringInput(out Vector3 desired, out _) ? desired : displayedDirection;
        // The new lock rule is the character's forward 180 degrees; circle alignment is cosmetic.
        return direction.sqrMagnitude > 0.0001f;
    }

    public bool CanLockTarget(Player candidate)
    {
        return BattleManager.Instance != null && BattleManager.Instance.IsGameplayActive &&
            !CombatPresentation.MenuOpen && TryGetAlignedAim(out _) &&
            owner.CanAcquireMagicTarget() && IsVisible(candidate);
    }

    private bool IsVisible(Player candidate)
    {
        if (targetCamera == null)
            targetCamera = Camera.main;
        if (targetCamera == null || candidate == null || candidate.Object == null || !candidate.Object.IsValid ||
            !candidate.IsTargetableBy(owner))
            return false;

        MagicStatEntry stats = owner.SelectedMagicStats;
        float range = stats.range > 0f ? Mathf.Min(maxTargetDistance, stats.range) : maxTargetDistance;
        if ((candidate.LockAimPoint - owner.LockAimPoint).sqrMagnitude > range * range)
            return false;

        Vector3 delta = candidate.LockAimPoint - owner.LockAimPoint;
        if (Vector3.Dot(owner.transform.forward, delta) < 0f) return false;
        if (stats.IsChanneled && (!TryGetAlignedAim(out Vector3 aim) ||
            Vector3.Angle(aim, delta) > stats.autoAimHalfAngle)) return false;
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
