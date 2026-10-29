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
    private CameraFollow targetCameraFollow;
    private NetworkObject currentTarget;
    private float nextRefreshTime;
    private readonly RaycastHit[] obstructionHits = new RaycastHit[32];
    public Player CurrentTarget => currentTarget != null ? currentTarget.GetComponent<Player>() : null;
    // Read-only camera presentation state; includes summon targets and requires no new search.
    public bool HasSelectedTarget => wasHeld && currentTarget != null && currentTarget.IsValid &&
        owner != null && owner.GetSelectedMagic() == sampledMagic;

    private void Awake() => owner = GetComponent<Player>();

    // Called from OnInput, including latched clicks that happened between network ticks.
    private bool wasHeld;
    private MagicType sampledMagic;
    public NetworkId GetInputTarget() => GetInputTarget(ControlSettings.Held(ControlAction.LockOn));

    public NetworkId GetInputTarget(bool held)
    {
        if (owner == null || owner.Object == null || !owner.Object.IsValid ||
            !owner.Object.HasInputAuthority || BattleManager.Instance == null ||
            !BattleManager.Instance.IsGameplayActive || !owner.IsAlive || CombatPresentation.MenuOpen ||
            BattlePerkUI.BlocksCombatInput || BattleEmoteUI.BlocksCombatInput || !owner.CanAcquireMagicTarget() || !TryGetAlignedAim(out _))
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
            nextRefreshTime = 0f;
        }
        if (owner.SelectedMagicStats.TargetsAllies)
        {
            // Healing continuously previews an instant ally lock, even before clicking.
            // Refresh on press so a quick click cannot heal a stale selection.
            if (held || currentTarget == null || !IsVisible(currentTarget) || Time.unscaledTime >= nextRefreshTime)
            {
                nextRefreshTime = Time.unscaledTime + targetRefreshInterval;
                currentTarget = FindClosestVisibleEnemy();
            }
            return currentTarget != null ? currentTarget.Id : default;
        }
        if (!wasHeld && held)
        {
            currentTarget = FindClosestVisibleEnemy();
            if (currentTarget == null && !HasEnemyInRange()) BattlePerkUI.ShowNoEnemyInRange();
        }
        if (currentTarget != null && !IsVisible(currentTarget)) currentTarget = null;
        if (held && currentTarget == null && Time.unscaledTime >= nextRefreshTime)
        {
            nextRefreshTime = Time.unscaledTime + targetRefreshInterval;
            currentTarget = FindClosestVisibleEnemy();
        }
        NetworkId result = currentTarget != null ? currentTarget.Id : default;
        wasHeld = held;
        if (!held) currentTarget = null; // Return the last valid selection on the release tick.
        return result;
    }

    private NetworkObject FindClosestVisibleEnemy()
    {
        NetworkObject best = null;
        float bestScore = float.PositiveInfinity;
        if (owner.Runner == null) return null;
        foreach (Player candidate in Player.ActiveCombatants)
        {
            if (candidate == null || candidate.Runner != owner.Runner || !IsVisible(candidate.Object))
                continue;
            Vector3 point = TargetingViewport(candidate.LockAimPoint);
            float score = owner.SelectedMagicStats.TargetsAllies
                ? Vector3.Angle(targetCamera.transform.forward, candidate.LockAimPoint - targetCamera.transform.position)
                : new Vector2(point.x - 0.5f, point.y - 0.5f).sqrMagnitude;
            if (score < bestScore)
            {
                bestScore = score;
                best = candidate.Object;
            }
        }
        if (owner.SelectedMagicStats.TargetsAllies) return best;
        foreach (var candidate in MagicProjectile.ActiveProjectiles)
        {
            if (candidate == null || !candidate.IsSummonEnemyOf(owner) || !IsVisible(candidate.Object)) continue;
            Vector3 point = TargetingViewport(candidate.transform.position);
            float score = new Vector2(point.x - .5f, point.y - .5f).sqrMagnitude;
            if (score < bestScore) { bestScore = score; best = candidate.Object; }
        }
        return best;
    }

    // Do not call a wall/behind-camera rejection a range failure.
    private bool HasEnemyInRange()
    {
        float configured = owner.SelectedMagicStats.range;
        float range = configured > 0f ? Mathf.Min(maxTargetDistance, configured) : maxTargetDistance;
        foreach (Player enemy in Player.ActiveCombatants)
            if (enemy != null && enemy.Runner == owner.Runner && enemy.IsTargetableBy(owner) &&
                (enemy.LockAimPoint - owner.LockAimPoint).sqrMagnitude <= range * range) return true;
        foreach (var summon in MagicProjectile.ActiveProjectiles)
            if (summon != null && summon.IsSummonEnemyOf(owner) &&
                (summon.transform.position - owner.LockAimPoint).sqrMagnitude <= range * range) return true;
        return false;
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
        targetCameraFollow = follow;

        Vector3 displayedDirection = (follow.GetDisplayedAimPoint() - owner.LockAimPoint).normalized;
        direction = follow.TryGetSteeringInput(out Vector3 desired, out _) ? desired : displayedDirection;
        // The new lock rule is the character's forward 180 degrees; circle alignment is cosmetic.
        return direction.sqrMagnitude > 0.0001f;
    }

    public bool CanLockTarget(Player candidate)
    {
        return BattleManager.Instance != null && BattleManager.Instance.IsGameplayActive &&
            !CombatPresentation.MenuOpen && !BattlePerkUI.BlocksCombatInput && !BattleEmoteUI.BlocksCombatInput && TryGetAlignedAim(out _) &&
            owner.CanAcquireMagicTarget() && candidate != null && IsVisible(candidate.Object);
    }

    private bool IsVisible(NetworkObject candidate)
    {
        if (targetCamera == null)
            targetCamera = Camera.main;
        if (targetCamera != null && candidate != null && candidate.IsValid && owner.SelectedMagicStats.TargetsAllies)
        {
            Player ally = candidate.GetComponent<Player>();
            if (!owner.IsHealingAlly(ally)) return false;
            float allyRange = owner.SelectedMagicStats.range > 0f
                ? Mathf.Min(maxTargetDistance, owner.SelectedMagicStats.range) : maxTargetDistance;
            return (ally.LockAimPoint-owner.LockAimPoint).sqrMagnitude <= allyRange*allyRange &&
                Vector3.Dot(targetCamera.transform.forward, ally.LockAimPoint-targetCamera.transform.position) > 0f &&
                HasLineOfSight(targetCamera.transform.position, ally);
        }
        if (targetCamera == null || candidate == null || !candidate.IsValid ||
            !owner.TryGetMagicTarget(candidate.Id, out Player player, out _, out Vector3 targetPoint))
            return false;

        MagicStatEntry stats = owner.SelectedMagicStats;
        float range = stats.range > 0f ? Mathf.Min(maxTargetDistance, stats.range) : maxTargetDistance;
        if ((targetPoint - owner.LockAimPoint).sqrMagnitude > range * range)
            return false;

        Vector3 delta = targetPoint - owner.LockAimPoint;
        if (Vector3.Dot(owner.transform.forward, delta) < 0f) return false;
        if (stats.IsChanneled && (!TryGetAlignedAim(out Vector3 aim) ||
            Vector3.Angle(aim, delta) > stats.autoAimHalfAngle)) return false;
        Vector3 point = TargetingViewport(targetPoint);
        return point.z > 0f && point.x >= 0f && point.x <= 1f && point.y >= 0f && point.y <= 1f &&
            (player != null ? HasLineOfSight(TargetingOrigin, player)
                : MagicProjectile.HasSummonSight(TargetingOrigin, targetPoint));
    }

    private Vector3 TargetingOrigin => targetCameraFollow != null
        ? targetCameraFollow.TargetingCameraPosition : targetCamera.transform.position;

    private Vector3 TargetingViewport(Vector3 point) => targetCameraFollow != null
        ? targetCameraFollow.WorldToTargetingViewportPoint(point) : targetCamera.WorldToViewportPoint(point);

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
