using Fusion;
using UnityEngine;
using UnityEngine.UI;

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
    private Player designatedTarget;
    private static enemyLockOn localDesignator;
    [SerializeField, Min(20f)] private float pingRadius = 120f;
    private TeamTargetPingHUD pingHud;
    public void SetPingRadius(float value) => pingRadius = Mathf.Max(20f, value);
    public Player DesignatedTarget => designatedTarget;

    // Viewer-local presentation only. No replicated target/appearance state.
    public static bool IsLocallyDesignated(Player candidate)
    {
        enemyLockOn selector = localDesignator;
        if (selector == null || !selector.isActiveAndEnabled || selector.owner == null ||
            selector.owner != Player.LocalPlayer || selector.owner.Object == null ||
            !selector.owner.Object.IsValid || !selector.owner.Object.HasInputAuthority || !selector.owner.IsAlive)
            return false;
        return candidate != null && selector.designatedTarget == candidate && candidate.Object != null &&
            candidate.Object.IsValid && candidate.Runner == selector.owner.Runner && candidate.IsTargetableBy(selector.owner);
    }

    private void SetDesignatedTarget(Player target)
    {
        if (designatedTarget == target) return;
        designatedTarget = target;
        if (target != null && owner == Player.LocalPlayer) localDesignator = this;
        else if (localDesignator == this) localDesignator = null;
        // A wheel-click during charging must also re-evaluate the lock selection.
        // Friendly healing/support keeps its own target selection.
        if (owner != null && owner.Object != null && owner.Object.IsValid && !owner.InputMagicStats.TargetsAllies)
        {
            currentTarget = null;
            nextRefreshTime = 0f;
        }
        if (owner != null && owner == Player.LocalPlayer && owner.Object != null && owner.Object.IsValid && owner.Object.HasInputAuthority)
        {
            var data = PlayerData.Local;
            if (data != null && data.Object != null && data.Object.IsValid && data.Runner == owner.Runner)
                data.RPC_SetTeamPing(target != null && target.Object != null && target.Object.IsValid ? target.Object.Id : default);
            if (pingHud != null) pingHud.LocalPing(target);
        }
    }

    private Player FindNextDesignatedTarget()
    {
        Ray aimRay = BattleHud.GetMagicAimRay(targetCamera);
        Vector2 aim = targetCamera.WorldToScreenPoint(aimRay.GetPoint(1f));
        Player best = null;
        float bestScore = float.PositiveInfinity;
        foreach (Player candidate in Player.ActiveCombatants)
        {
            if (candidate == null || candidate == designatedTarget || candidate.Object == null || !candidate.Object.IsValid ||
                candidate.Runner != owner.Runner || !candidate.IsTargetableBy(owner)) continue;
            Vector3 viewport = targetCamera.WorldToViewportPoint(candidate.LockAimPoint);
            if (viewport.z <= 0f || viewport.x < 0f || viewport.x > 1f || viewport.y < 0f || viewport.y > 1f) continue;
            Vector2 screen = targetCamera.WorldToScreenPoint(candidate.LockAimPoint);
            float score = (screen - aim).sqrMagnitude;
            if (score >= bestScore || !HasLineOfSight(aimRay.origin, candidate)) continue;
            bestScore = score;
            best = candidate;
        }
        return best;
    }

    private void Update()
    {
        bool active = owner != null && owner.Object != null && owner.Object.IsValid &&
            owner.Object.HasInputAuthority && owner == Player.LocalPlayer && owner.IsAlive && BattleManager.Instance != null &&
            BattleManager.Instance.IsGameplayActive;
        if (!active)
        {
            SetDesignatedTarget(null);
            SetArrowVisible(false);
            return;
        }
        if (designatedTarget != null && (designatedTarget.Object == null || !designatedTarget.Object.IsValid ||
            designatedTarget.Runner != owner.Runner || !designatedTarget.IsTargetableBy(owner))) SetDesignatedTarget(null);
        if (Input.GetMouseButtonDown(2) && Cursor.lockState == CursorLockMode.Locked && !CombatPresentation.MenuOpen &&
            !BattlePerkUI.BlocksCombatInput && !BattleEmoteUI.BlocksCombatInput && TryGetAlignedAim(out _))
        {
            SetDesignatedTarget(FindNextDesignatedTarget());
        }
    }

    private void LateUpdate()
    {
        if (owner == Player.LocalPlayer && targetCamera == null) targetCamera = Camera.main;
        if (pingHud != null) pingHud.Render(owner, targetCamera, designatedTarget, pingRadius);
    }
    private void SetArrowVisible(bool visible) { if (!visible && pingHud != null) pingHud.Hide(); }
    private void OnDisable() { SetDesignatedTarget(null); if (localDesignator == this) localDesignator = null; SetArrowVisible(false); }
    private void OnDestroy() { if (localDesignator == this) localDesignator = null; }
    public Player CurrentTarget => currentTarget != null ? currentTarget.GetComponent<Player>() : null;
    // Read-only camera presentation state; includes summon targets and requires no new search.
    public bool HasSelectedTarget => wasHeld && currentTarget != null && currentTarget.IsValid &&
        owner != null && owner.InputMagicStats.magic == sampledMagic;
    // Use the same predicted progress as the lock ring, and reject progress belonging
    // to the previous target while a newly selected target reaches the simulation.
    public float SelectedTargetProgress => HasSelectedTarget && owner.HasLockTarget(currentTarget)
        ? Mathf.Clamp01(owner.LockProgress) : 0f;

    private void Awake()
    {
        owner = GetComponent<Player>();
        pingHud = gameObject.AddComponent<TeamTargetPingHUD>();
    }

    // Called from OnInput, including latched clicks that happened between network ticks.
    private bool wasHeld;
    private MagicType sampledMagic;
    public NetworkId GetInputTarget() => GetInputTarget(owner != null && owner.MagicInputHeld);

    public NetworkId GetInputTarget(bool held)
    {
        if (owner == null || owner.Object == null || !owner.Object.IsValid ||
            !owner.Object.HasInputAuthority || BattleManager.Instance == null ||
            !BattleManager.Instance.IsGameplayActive || !owner.IsAlive || CombatPresentation.MenuOpen ||
            BattlePerkUI.BlocksCombatInput || BattleEmoteUI.BlocksCombatInput || !owner.CanAcquireMagicTarget(owner.LocalMagicInputSlot) || !TryGetAlignedAim(out _))
        {
            currentTarget = null;
            wasHeld = false;
            return default;
        }

        if (sampledMagic != owner.InputMagicStats.magic)
        {
            sampledMagic = owner.InputMagicStats.magic;
            currentTarget = null;
            wasHeld = false;
            nextRefreshTime = 0f;
        }
        if (owner.InputMagicStats.TargetsAllies)
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

    private NetworkObject FindClosestVisibleEnemy(bool excludeRainTargets = false)
    {
        if (!owner.InputMagicStats.TargetsAllies && designatedTarget != null &&
            designatedTarget.Object != null && designatedTarget.Object.IsValid && designatedTarget.Runner == owner.Runner &&
            designatedTarget.IsTargetableBy(owner) && IsVisible(designatedTarget.Object) &&
            (!excludeRainTargets || !owner.HasRainTarget(designatedTarget.Object.Id)))
            return designatedTarget.Object;
        NetworkObject best = null;
        float bestScore = float.PositiveInfinity;
        if (owner.Runner == null) return null;
        foreach (Player candidate in Player.ActiveCombatants)
        {
            if (candidate == null || candidate.Runner != owner.Runner || !IsVisible(candidate.Object))
                continue;
            if (excludeRainTargets && owner.HasRainTarget(candidate.Object.Id)) continue;
            Vector3 point = TargetingViewport(candidate.LockAimPoint);
            float score = owner.InputMagicStats.TargetsAllies
                ? Vector3.Angle(targetCamera.transform.forward, candidate.LockAimPoint - targetCamera.transform.position)
                : new Vector2(point.x - 0.5f, point.y - 0.5f).sqrMagnitude;
            if (score < bestScore)
            {
                bestScore = score;
                best = candidate.Object;
            }
        }
        if (owner.InputMagicStats.TargetsAllies) return best;
        foreach (var candidate in MagicProjectile.ActiveProjectiles)
        {
            if (candidate == null || !candidate.IsSummonEnemyOf(owner) || !IsVisible(candidate.Object)) continue;
            if (excludeRainTargets && owner.HasRainTarget(candidate.Object.Id)) continue;
            Vector3 point = TargetingViewport(candidate.transform.position);
            float score = new Vector2(point.x - .5f, point.y - .5f).sqrMagnitude;
            if (score < bestScore) { bestScore = score; best = candidate.Object; }
        }
        return best;
    }

    public void SampleBladeRain(ref NetworkInputData input)
    {
        if (owner == null || owner.InputMagicStats.magic != MagicType.BladeRain || !owner.IsRainCharging) return;
        input.rainTargetRevision = owner.RainTargetRevision;
        input.rainVisible0 = VisibleRainTarget(0);
        input.rainVisible1 = VisibleRainTarget(1);
        input.rainVisible2 = VisibleRainTarget(2);
        input.rainVisible3 = VisibleRainTarget(3);
        var candidate = FindClosestVisibleEnemy(true);
        input.rainCandidate = candidate != null ? candidate.Id : default;
    }
    private NetworkId VisibleRainTarget(int index)
    {
        var id = owner.GetRainTarget(index);
        return owner.Runner.TryFindObject(id, out var target) && IsVisible(target) ? id : default;
    }

    // Do not call a wall/behind-camera rejection a range failure.
    private bool HasEnemyInRange()
    {
        float configured = owner.InputMagicStats.range;
        float range = (configured > 0f ? Mathf.Min(maxTargetDistance, configured) : maxTargetDistance) * owner.LockRangeMultiplier;
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
            owner.CanAcquireMagicTarget(owner.LocalMagicInputSlot) && candidate != null && IsVisible(candidate.Object);
    }

    private bool IsVisible(NetworkObject candidate)
    {
        if (targetCamera == null)
            targetCamera = Camera.main;
        if (targetCamera != null && candidate != null && candidate.IsValid && owner.InputMagicStats.TargetsAllies)
        {
            Player ally = candidate.GetComponent<Player>();
            if (!owner.IsHealingAlly(ally)) return false;
            float allyRange = owner.InputMagicStats.range > 0f
                ? Mathf.Min(maxTargetDistance, owner.InputMagicStats.range) : maxTargetDistance;
            allyRange *= owner.LockRangeMultiplier;
            return (ally.LockAimPoint-owner.LockAimPoint).sqrMagnitude <= allyRange*allyRange &&
                Vector3.Dot(targetCamera.transform.forward, ally.LockAimPoint-targetCamera.transform.position) > 0f &&
                HasLineOfSight(targetCamera.transform.position, ally);
        }
        if (targetCamera == null || candidate == null || !candidate.IsValid ||
            !owner.TryGetMagicTarget(candidate.Id, out Player player, out _, out Vector3 targetPoint, owner.LocalMagicInputSlot))
            return false;

        MagicStatEntry stats = owner.InputMagicStats;
        float range = (stats.range > 0f ? Mathf.Min(maxTargetDistance, stats.range) : maxTargetDistance) * owner.LockRangeMultiplier;
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

// Two nested triangles keep the outline crisp without a texture or an Outline mesh effect.
public sealed class DesignatedTargetTriangle : MaskableGraphic
{
    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        Rect rect = rectTransform.rect;
        AddTriangle(mesh, rect.width, rect.height, Color.black);
        AddTriangle(mesh, rect.width * .68f, rect.height * .68f, color);
    }

    private static void AddTriangle(VertexHelper mesh, float width, float height, Color tint)
    {
        int start = mesh.currentVertCount;
        mesh.AddVert(new Vector3(0f, height * .5f), tint, Vector2.zero);
        mesh.AddVert(new Vector3(width * .5f, -height * .5f), tint, Vector2.zero);
        mesh.AddVert(new Vector3(-width * .5f, -height * .5f), tint, Vector2.zero);
        mesh.AddTriangle(start, start + 1, start + 2);
    }
}
