using System.Collections.Generic;
using Fusion;
using UnityEngine;
using Unity.Profiling;

// Uses the existing runner. Only StateAuthority simulates flight and damage.
public partial class MagicProjectile : NetworkBehaviour
{
    private static readonly ProfilerMarker simulationMarker = new("WitchFlight.Projectile.Simulate");
    private static readonly HashSet<MagicProjectile> active = new();
    [Networked] public MagicType Magic { get; protected set; }
    [Networked] public NetworkId ShooterId { get; protected set; }
    [Networked] public PlayerRef Shooter { get; protected set; }
    [Networked] public int ShooterTeam { get; protected set; }
    [Networked] public NetworkId TargetId { get; protected set; }
    [Networked] public bool IsMine { get; protected set; }
    [Networked] public bool Settled { get; protected set; }
    [Networked] public bool Finished { get; protected set; }
    [Networked] public int PredictionInputTick { get; protected set; }
    [Networked] public float TravelledDistance { get; protected set; }
    [Networked] public bool FocusedBlade { get; private set; }
    [Networked] public float FocusedBladeCharge { get; private set; }
    [Networked] public float FlightSpeedFraction { get; protected set; }
    [Networked] public bool FlightExhausted { get; protected set; }
    [Networked] private bool ExhaustionEnabled { get; set; }
    private Vector3 fallingVelocity;
    private Suimono.Core.SuimonoModule fallingWater;
    private MagicStatTable flightSettings;
    private Player sweptTarget;
    private Vector3 sweptTargetCenter, sweptProjectileStart;
    private int sweptPortalSequence;

    public static bool UsesFlightExhaustion(MagicType magic, MagicStatTable table) =>
        table != null && table.homingExhaustion &&
        (magic == MagicType.Fire || magic == MagicType.Ice || magic == MagicType.Binding);

    public static float DrainFlightSpeed(float fraction, Vector3 before, Vector3 after, float dt, MagicStatTable table)
    {
        dt = Mathf.Max(0f, dt);
        // atan2 remains precise for tiny corrections; acos(dot) can turn float rounding
        // into fictitious steering loss during an otherwise straight flight.
        float degrees = Mathf.Atan2(Vector3.Cross(before, after).magnitude,
            Vector3.Dot(before, after)) * Mathf.Rad2Deg;
        // Limit the RATE, not the loss per tick. A large correction cannot consume
        // most of the shot's energy in one update, even at a different tick rate.
        float turnLoss = Mathf.Min(degrees * Mathf.Max(0f, table.homingSpeedLossPerDegree),
            Mathf.Max(0f, table.homingMaxTurnLossPerSecond) * dt);
        return Mathf.Max(0f, fraction - Mathf.Max(0f, table.homingSpeedLossPerSecond) * dt - turnLoss);
    }

    public static float FlightTurnSpeed(float referenceTurnSpeed, float speed, MagicStatTable table)
    {
        // Sample speed BEFORE steering/draining. No recursive solve or distance bonus.
        // Power > 1 also widens the turning radius as the projectile loses speed.
        float ratio = Mathf.Max(0f, speed) / Mathf.Max(1f, table.homingTurnReferenceSpeed);
        return Mathf.Max(0f, referenceTurnSpeed) *
            Mathf.Pow(ratio, Mathf.Max(1f, table.homingTurnSpeedExponent));
    }

    public static float FlightDistance(MagicStatEntry entry, MagicStatTable table)
    {
        if (entry.effect == MagicEffectKind.Mine) return entry.placementDistance;
        return entry.range * (entry.requiresTarget && UsesFlightExhaustion(entry.magic, table)
            ? Mathf.Max(1f, table.homingTravelDistanceMultiplier) : 1f);
    }

    public static float FlightLifetime(MagicStatEntry entry, MagicStatTable table)
    {
        return entry.projectileLifetime * (entry.effect != MagicEffectKind.Mine && entry.requiresTarget &&
            UsesFlightExhaustion(entry.magic, table) ? Mathf.Max(1f, table.homingLifetimeMultiplier) : 1f);
    }

    // Constant-velocity intercept avoids wasting steering energy on a target that never turns.
    // This changes the aim point only: RotateTowards still bounds every in-flight correction.
    public static Vector3 HomingAimDelta(Vector3 delta, Vector3 targetVelocity, float speed, float maxLeadSeconds)
    {
        if (maxLeadSeconds <= 0f || speed <= .001f || delta.sqrMagnitude <= .001f) return delta;
        float a = targetVelocity.sqrMagnitude - speed * speed;
        float b = 2f * Vector3.Dot(delta, targetVelocity);
        float c = delta.sqrMagnitude;
        float time = 0f;
        if (Mathf.Abs(a) < .001f)
        {
            if (b < -.001f) time = -c / b;
        }
        else
        {
            float discriminant = b * b - 4f * a * c;
            if (discriminant >= 0f)
            {
                float root = Mathf.Sqrt(discriminant);
                float first = (-b - root) / (2f * a);
                float second = (-b + root) / (2f * a);
                if (first > 0f) time = first;
                if (second > 0f && (time <= 0f || second < time)) time = second;
            }
        }
        return delta + targetVelocity * Mathf.Clamp(time, 0f, maxLeadSeconds);
    }

    public bool HasFlightExhaustion => ConstructKind == MagicConstructKind.Projectile && ExhaustionEnabled;

    private void ExhaustFlight()
    {
        if (FlightExhausted) return;
        FlightExhausted = true;
        TargetId = default;
        fallingVelocity = direction * stats.projectileSpeed * FlightSpeedFraction;
        // One lookup when entering the fall, never a repeated scene search.
        fallingWater = FindFirstObjectByType<Suimono.Core.SuimonoModule>();
        Lifetime = TickTimer.CreateFromSeconds(Runner, Mathf.Max(.1f, flightSettings.homingFallLifetime));
        NotifyShooterFaceMiss();
    }

    private void TickExhaustedFlight(float dt)
    {
        float drag = Mathf.Exp(-Mathf.Max(0f, flightSettings.homingFallDrag) * dt);
        fallingVelocity.x *= drag;
        fallingVelocity.z *= drag;
        fallingVelocity += Vector3.down * Mathf.Max(.1f, flightSettings.homingFallGravity) * dt;
        Vector3 step = fallingVelocity * dt;
        Vector3 next = transform.position + step;
        if (InterceptedByFlare(transform.position, next)) return;
        bool waterHit = fallingWater != null && fallingWater.isActiveAndEnabled &&
            fallingWater.TryGetSurfaceHeight(next, out float seaY) && next.y <= seaY;
        if (waterHit || (step.sqrMagnitude > .000001f &&
            TryBladeWallHit(transform.position, step.normalized, step.magnitude, collisionHits, out _)))
        { Runner.Despawn(Object); return; }
        if (step.sqrMagnitude > .000001f) transform.SetPositionAndRotation(next, Quaternion.LookRotation(step));
    }
    [Networked] protected TickTimer Lifetime { get; set; }
    [Networked] protected TickTimer ArmTimer { get; set; }

    protected MagicStatEntry stats;
    protected Vector3 direction;
    protected bool initialized;
    private Vector3 stormAimDirection;
    private ProjectilePredictionView view;
    private bool impactPresented;
    private bool audioStarted;
    private BattleAudio.Loop flightAudio;
    private bool iceFollowUpPresented;
    private bool iceFollowUpStarted;
    private bool iceDamagePending;
    private Player iceDamageVictim;
    private TickTimer iceDamageTimer;
    private readonly RaycastHit[] collisionHits = new RaycastHit[32];
    private Collider[] overlapHits = new Collider[32];
    // Main-thread, non-reentrant visibility queries; no damage callbacks within them.
    private static readonly RaycastHit[] blastHits = new RaycastHit[32];
    private static readonly Collider[] blastOverlaps = new Collider[32];

    public override void Spawned()
    {
        flightAudio.Stop();
        audioStarted = false;
        impactPresented = false;
        iceFollowUpPresented = false;
        iceFollowUpStarted = false;
        iceDamagePending = false;
        iceDamageVictim = null;
        // Projectiles remain server-simulated: even their shooter renders remote snapshots.
        Object.ForceRemoteRenderTimeframe = !Object.HasStateAuthority;
        active.Add(this);
        if (ConstructKind == MagicConstructKind.TimeField) timeFields.Add(this);
        if (ConstructKind == MagicConstructKind.FlareField) flareFields.Add(this);
    }

    public void Initialize(Player shooter, Player target, MagicStatEntry entry, Vector3 aim, int predictionInputTick = 0, NetworkId summonTargetId = default)
    {
        if (!Object.HasStateAuthority)
            return;
        ConstructKind = MagicConstructKind.Projectile;
        stats = entry;
        FocusedBlade = entry.focusedBlade;
        FocusedBladeCharge = entry.focusedBladeCharge;
        flightSettings = shooter.MagicTable != null ? shooter.MagicTable : MagicStatTable.Default;
        ExhaustionEnabled = entry.requiresTarget && UsesFlightExhaustion(entry.magic, flightSettings);
        FlightSpeedFraction = 1f;
        FlightExhausted = false;
        sweptTarget = null;
        fallingVelocity = Vector3.zero;
        decoyField = null;
        fallingWater = null;
        timeLifetimeRemainder = 0f;
        stormAimDirection = Vector3.zero;
        if (entry.magic == MagicType.MagicStorm)
        {
            Vector2 spread = Random.insideUnitCircle * Mathf.Tan(entry.stormSpreadAngle * Mathf.Deg2Rad);
            // Snapshot each bullet's aim. Later cursor movement only affects new bullets.
            stormAimDirection = (Quaternion.LookRotation(shooter.MagicStormAimDirection) *
                new Vector3(spread.x, spread.y, 1f)).normalized;
        }
        Settled = Finished = false;
        ArmTimer = TickTimer.None;
        PredictionInputTick = predictionInputTick;
        TravelledDistance = 0f;
        Magic = entry.magic;
        Shooter = shooter.ParticipantRef;
        ShooterId = shooter.Object.Id;
        ShooterTeam = shooter.TeamIndex;
        TargetId = target != null ? target.Object.Id : summonTargetId;
        IsMine = entry.effect == MagicEffectKind.Mine;
        Vector3 targetDelta = target != null ? target.LockAimPoint - transform.position : Vector3.zero;
        if (HasFlightExhaustion && target != null)
            targetDelta = HomingAimDelta(targetDelta, target.ParryHintVelocity, entry.projectileSpeed,
                flightSettings.homingLeadSeconds);
        direction = ResolveLaunchDirection(entry.requiresTarget && !IsMine, aim,
            shooter.transform.forward, targetDelta);
        if (direction.sqrMagnitude < 0.5f)
            direction = shooter.transform.forward;
        transform.rotation = Quaternion.LookRotation(direction);
        if (HasFlightExhaustion && target != null) TryGetSweptTargetContact(target, out _);
        float duration = IsMine
            ? entry.placementDistance / Mathf.Max(1f, entry.projectileSpeed) +
                entry.activationDelay + entry.effectDuration
            : FlightLifetime(entry, flightSettings);
        Lifetime = TickTimer.CreateFromSeconds(Runner, Mathf.Max(0.1f, duration));
        initialized = true;
    }

    public override void FixedUpdateNetwork()
    {
        if (!Object.HasStateAuthority || !initialized)
            return;
        using var simulationSample = simulationMarker.Auto();
        if (BattleManager.Instance == null || !BattleManager.Instance.IsGameplayActive)
        {
            Runner.Despawn(Object);
            return;
        }
        if (ConstructKind == MagicConstructKind.TimeField)
        {
            if (Lifetime.Expired(Runner)) Runner.Despawn(Object);
            return;
        }
        if (ConstructKind == MagicConstructKind.FlareField)
        { TickFlareField(); return; }
        if (IsConstruct) { TickConstruct(); return; }
        if (iceDamagePending) { TickIceFollowUpDamage(); return; }
        float flightDelta = TimeFlightStep();
        if (Lifetime.Expired(Runner))
        {
            if (!Finished && HasFlightExhaustion && !FlightExhausted)
            { ExhaustFlight(); return; }
            if (!Finished && !IsMine && stats.effect == MagicEffectKind.AreaDamage)
                Impact(transform.position);
            else
            {
                if (!Finished && !IsMine && !FlightExhausted) NotifyShooterFaceMiss();
                Runner.Despawn(Object);
            }
            return;
        }
        if (Finished)
            return;
        if (FlightExhausted) { TickExhaustedFlight(flightDelta); return; }
        if (IsMine && Settled)
        {
            CheckMine();
            return;
        }

        if (InterceptedByFlare(transform.position, transform.position)) return;
        ApplyFlareAttraction();
        bool followsFlare = TryGetFlareAim(out Vector3 flareAim);
        if (Magic == MagicType.MagicStorm)
            direction = Vector3.RotateTowards(direction, stormAimDirection,
                stats.projectileTurnSpeed * Mathf.Deg2Rad * flightDelta, 0f).normalized;
        Vector3 previousDirection = direction;
        Player target = FindTarget();
        if (TryGetSweptTargetContact(target, out Vector3 crossingPoint))
        {
            Impact(crossingPoint, target);
            return;
        }
        MagicProjectile summonTarget = FindSummonTargetById();
        if ((followsFlare || target != null || summonTarget != null) && (stats.requiresTarget || stats.projectileTurnSpeed > 0f))
        {
            Vector3 desired = (followsFlare ? flareAim : target != null ? target.LockAimPoint : summonTarget.transform.position) - transform.position;
            if (HasFlightExhaustion && target != null)
                desired = HomingAimDelta(desired, target.ParryHintVelocity,
                    stats.projectileSpeed * FlightSpeedFraction * (flightDelta / Mathf.Max(.000001f, Runner.DeltaTime)),
                    flightSettings.homingLeadSeconds);
            if (desired.sqrMagnitude > 0.001f)
                direction = ResolveHomingDirection(stats.requiresTarget, direction, desired,
                    HasFlightExhaustion ? FlightTurnSpeed(stats.projectileTurnSpeed,
                        stats.projectileSpeed * FlightSpeedFraction, flightSettings) : stats.projectileTurnSpeed, flightDelta);
        }

        if (HasFlightExhaustion)
        {
            FlightSpeedFraction = DrainFlightSpeed(FlightSpeedFraction, previousDirection, direction, flightDelta, flightSettings);
            if (FlightSpeedFraction <= flightSettings.homingFallThreshold)
            { ExhaustFlight(); return; }
        }
        float limit = FlightDistance(stats, flightSettings);
        float step = Mathf.Min(stats.projectileSpeed * (HasFlightExhaustion ? FlightSpeedFraction : 1f) * flightDelta, Mathf.Max(0f, limit - TravelledDistance));
        Vector3 next = transform.position + direction * step;
        if (TryGetInitialOverlap(out Collider overlap))
        {
            if (IsMine)
                SettleMine();
            else
                Impact(transform.position, overlap.GetComponentInParent<Player>(), headTraceOrigin: transform.position);
            return;
        }
        bool collided = TryGetCollision(step, out RaycastHit hit);
        if (InterceptedByFlare(transform.position, transform.position + direction * (collided ? hit.distance : step))) return;
        if (TryTraceSummon(Runner, ShooterTeam, transform.position, direction,
            collided ? hit.distance : step, stats.projectileRadius, out MagicProjectile body, out float bodyDistance))
        {
            Impact(transform.position + direction * bodyDistance, null, body);
            return;
        }
        if (collided)
        {
            Vector3 sweepOrigin = transform.position;
            next = transform.position + direction * Mathf.Max(0f, hit.distance - 0.02f);
            transform.position = next;
            if (IsMine)
                SettleMine();
            else
                Impact(next, hit.collider.GetComponentInParent<Player>(), headTraceOrigin: sweepOrigin);
            return;
        }

        transform.SetPositionAndRotation(next, Quaternion.LookRotation(direction));
        TravelledDistance += step;
        if (TravelledDistance >= limit - 0.001f)
        {
            if (HasFlightExhaustion) { ExhaustFlight(); return; }
            if (IsMine)
                SettleMine();
            else
                Impact(next);
        }
    }

    // Lock-on chooses the initial aim only. In flight, all guidance obeys the turn limit.
    // Non-lock projectiles (including mines) retain their authored firing direction at launch.
    public static Vector3 ResolveLaunchDirection(bool locked, Vector3 aim, Vector3 forward, Vector3 targetDelta)
    {
        Vector3 result = locked && targetDelta.sqrMagnitude > 0.001f ? targetDelta : aim;
        return result.sqrMagnitude > 0.001f ? result.normalized : forward.normalized;
    }

    public static Vector3 ResolveHomingDirection(bool locked, Vector3 current, Vector3 targetDelta,
        float turnSpeed, float deltaTime)
    {
        if (targetDelta.sqrMagnitude <= 0.001f) return current;
        return Vector3.RotateTowards(current, targetDelta.normalized,
            Mathf.Max(0f, turnSpeed) * Mathf.Deg2Rad * Mathf.Max(0f,deltaTime), 0f).normalized;
    }

    private Player FindTarget()
    {
        if (!Runner.TryFindObject(TargetId, out NetworkObject obj) || obj == null)
            return null;
        Player target = obj.GetComponent<Player>();
        if (target == null && obj.TryGetComponent(out MagicProjectile decoy) && decoy.ConstructKind == MagicConstructKind.FlareField) return null;
        // Summon bodies share the same network target ID path as players.
        if (target == null && FindSummonTargetById() != null) return null;
        if (target == null || !target.IsAlive || target.IsStealthed || target.TeamIndex == ShooterTeam)
        {
            TargetId = default;
            return null;
        }
        return target;
    }

    // The ordinary SphereCast still handles walls and all other colliders. This extra
    // check uses actual consecutive poses of ONLY the locked target, not predicted hits.
    private bool TryGetSweptTargetContact(Player target, out Vector3 point)
    {
        point = default;
        if (!HasFlightExhaustion || IsMine || target == null)
        { sweptTarget = null; return false; }
        BattleFlag.GetCapsule(target, out Vector3 center, out Vector3 halfAxis, out float radius);
        float maxMovement = Mathf.Max(4f, target.ParryHintVelocity.magnitude * Runner.DeltaTime * 2f + radius);
        bool continuous = sweptTarget == target && sweptPortalSequence == target.PortalExitSequence &&
            (center - sweptTargetCenter).sqrMagnitude <= maxMovement * maxMovement;
        bool hit = continuous && TrySweptTargetContact(sweptProjectileStart, transform.position,
            sweptTargetCenter, center, halfAxis, radius + Mathf.Max(.02f, stats.projectileRadius), out point);
        // Never turn a crossing through scenery into a hit on the far side.
        if (hit && !HasBlastSight(point, target)) hit = false;
        sweptTarget = target;
        sweptPortalSequence = target.PortalExitSequence;
        sweptTargetCenter = center;
        sweptProjectileStart = transform.position;
        return hit;
    }

    public static bool TrySweptTargetContact(Vector3 shotStart, Vector3 shotEnd, Vector3 targetStart,
        Vector3 targetEnd, Vector3 halfAxis, float radius, out Vector3 point)
    {
        Vector3 relativeStart = shotStart - targetStart;
        Vector3 relativeEnd = shotEnd - targetEnd;
        float squared = BattleFlag.SegmentDistanceSquared(relativeStart, relativeEnd,
            -halfAxis, halfAxis, out Vector3 closest);
        Vector3 relativeStep = relativeEnd - relativeStart;
        float t = relativeStep.sqrMagnitude > .000001f
            ? Mathf.Clamp01(Vector3.Dot(closest - relativeStart, relativeStep) / relativeStep.sqrMagnitude) : 0f;
        point = Vector3.Lerp(shotStart, shotEnd, t);
        return squared <= Mathf.Max(0f, radius) * Mathf.Max(0f, radius);
    }

    // Only existing hostile homing shots lose their target. No teleport, invulnerability,
    // damage cancellation, or immunity to newly fired shots. Continue on the last heading.
    public static int BreakHomingFor(Player defender)
        => BreakHomingFor(defender, out _);

    public static int BreakHomingFor(Player defender, out bool rewardable)
    {
        rewardable = false;
        if (defender == null || defender.Object == null || !defender.Object.IsValid ||
            !defender.Object.HasStateAuthority) return 0;
        int count = 0;
        foreach (MagicProjectile shot in active)
        {
            if (shot == null || shot.Runner != defender.Runner || shot.Object == null ||
                !shot.Object.IsValid || !shot.Object.HasStateAuthority || !shot.initialized ||
                shot.Finished || shot.IsMine || shot.IsConstruct || !shot.stats.requiresTarget ||
                shot.ShooterTeam == defender.TeamIndex || !shot.TargetId.Equals(defender.Object.Id)) continue;
            shot.TargetId = default;
            // Bots can be countered, but cannot be farmed for growth.
            if (shot.Shooter != PlayerRef.None && PlayerData.FindPerkData(defender.Runner,shot.Shooter) != null) rewardable = true;
            count++;
        }
        return count;
    }

    private bool TryGetCollision(float distance, out RaycastHit nearest)
    {
        nearest = default;
        float best = float.PositiveInfinity;
        float radius = Mathf.Max(0.02f, stats.projectileRadius);
        int count = Physics.SphereCastNonAlloc(transform.position, radius, direction,
            collisionHits, distance, ~0, QueryTriggerInteraction.Ignore);
        RaycastHit[] hits = collisionHits;
        if (count == hits.Length)
        {
            hits = Physics.SphereCastAll(transform.position, radius, direction,
                distance, ~0, QueryTriggerInteraction.Ignore);
            count = hits.Length;
        }
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = hits[i];
            if (hit.collider == null || hit.transform.IsChildOf(transform))
                continue;
            Player player = hit.collider.GetComponentInParent<Player>();
            if (player != null && (!player.IsAlive || player.Object.Id.Equals(ShooterId)))
                continue;
            if (Magic == MagicType.Blade && player == null) continue;
            if (hit.distance >= best)
                continue;
            best = hit.distance;
            nearest = hit;
        }
        if (Magic == MagicType.Blade && TryBladeWallHit(transform.position, direction, distance, collisionHits, out var wall) && wall.distance < best)
        {
            nearest = wall;
            best = wall.distance;
        }
        return best < float.PositiveInfinity;
    }

    private bool TryGetInitialOverlap(out Collider overlap)
    {
        float radius = Mathf.Max(0.02f, stats.projectileRadius);
        int count = Physics.OverlapSphereNonAlloc(transform.position, radius, overlapHits,
            ~0, QueryTriggerInteraction.Ignore);
        Collider[] hits = overlapHits;
        while (count == hits.Length)
        {
            overlapHits = hits = new Collider[hits.Length * 2];
            count = Physics.OverlapSphereNonAlloc(transform.position, radius, hits,
                ~0, QueryTriggerInteraction.Ignore);
        }
        for (int i = 0; i < count; i++)
        {
            Collider collider = hits[i];
            if (collider == null || collider.transform.IsChildOf(transform))
                continue;
            Player player = collider.GetComponentInParent<Player>();
            if (player != null && (!player.IsAlive || player.Object.Id.Equals(ShooterId)))
                continue;
            if (Magic == MagicType.Blade && !BladeOverlapBlocks(collider, transform.position)) continue;
            overlap = collider;
            return true;
        }
        overlap = null;
        return false;
    }

    // Blade wings can clip scenery. Damage still uses the full projectile sphere.
    // Shared with cosmetic prediction so a side-wall touch cannot hide a live shot.
    public static bool BladeOverlapBlocks(Collider collider, Vector3 center)
    {
        if (collider.GetComponentInParent<MagicProjectile>() != null) return false;
        if (collider.GetComponentInParent<Player>() != null) return true;
        return (collider.ClosestPoint(center) - center).sqrMagnitude <= .000001f;
    }

    public static bool TryBladeWallHit(Vector3 origin, Vector3 direction, float distance, RaycastHit[] buffer, out RaycastHit nearest)
    {
        nearest = default;
        int count = Physics.RaycastNonAlloc(origin, direction, buffer, distance, ~0, QueryTriggerInteraction.Ignore);
        RaycastHit[] hits = buffer;
        if (count == buffer.Length)
        {
            hits = Physics.RaycastAll(origin, direction, distance, ~0, QueryTriggerInteraction.Ignore);
            count = hits.Length;
        }
        float closest = float.PositiveInfinity;
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = hits[i];
            if (hit.collider == null || hit.distance >= closest ||
                hit.collider.GetComponentInParent<Player>() != null ||
                hit.collider.GetComponentInParent<MagicProjectile>() != null) continue;
            nearest = hit;
            closest = hit.distance;
        }
        return closest < float.PositiveInfinity;
    }

    private void NotifyShooterFaceMiss()
    {
        if (ConstructKind != MagicConstructKind.Projectile && ConstructKind != MagicConstructKind.ThunderArea) return;
        if (Runner.TryFindObject(ShooterId, out NetworkObject shooter) && shooter != null)
            shooter.GetComponent<Player>()?.NotifyFaceMiss();
    }

    private void SettleMine()
    {
        Settled = true;
        ArmTimer = TickTimer.CreateFromSeconds(Runner, Mathf.Max(0.01f, stats.activationDelay));
        Lifetime = TickTimer.CreateFromSeconds(Runner,
            Mathf.Max(0.01f, stats.activationDelay) + Mathf.Max(0.1f, stats.effectDuration));
    }

    private void CheckMine()
    {
        if (!ArmTimer.Expired(Runner))
            return;
        foreach (Player player in Player.ActiveCombatants)
        {
            if (player == null || player.Runner != Runner) continue;
            if (player != null && player.IsAlive && (player.LockAimPoint - transform.position).sqrMagnitude <= stats.radius * stats.radius &&
                HasBlastSight(player))
            {
                Impact(transform.position);
                return;
            }
        }
    }

    private void Impact(Vector3 point, Player directHit = null, MagicProjectile summonHit = null, Vector3? headTraceOrigin = null)
    {
        if (Finished)
            return;
        Finished = true;
        transform.position = point;
        if (IsMine || stats.effect == MagicEffectKind.AreaDamage)
        {
            bool contacted = DamageSummonsInArea(Runner, ShooterTeam, point, stats.radius, stats, ShooterId);
            foreach (Player player in Player.ActiveCombatants)
            {
                if (player == null || player.Runner != Runner) continue;
                if (player == null || !player.IsAlive || (!IsMine && player.TeamIndex == ShooterTeam) ||
                    (player.LockAimPoint - point).sqrMagnitude > stats.radius * stats.radius || !HasBlastSight(player))
                    continue;
                contacted = true;
                player.ReceiveMagicHit(PerkEffects.AreaHit(stats, Vector3.Distance(player.LockAimPoint,point)), Shooter, ShooterId, player.LockAimPoint - point);
            }
            if (!contacted) NotifyShooterFaceMiss();
        }
        else if (summonHit != null)
        {
            summonHit.ReceiveSummonHit(stats, ShooterTeam, ShooterId);
        }
        else if (directHit != null && directHit.IsAlive && directHit.TeamIndex != ShooterTeam)
        {
            if (TryBeginIceFollowUp(directHit, point))
            {
                RPC_Impact(point, Magic);
                return;
            }
            MagicStatEntry hitStats = headTraceOrigin.HasValue
                ? directHit.ResolveHeadHit(stats, headTraceOrigin.Value, direction,
                    Vector3.Distance(headTraceOrigin.Value, point), stats.projectileRadius)
                : stats;
            directHit.ReceiveMagicHit(hitStats, Shooter, ShooterId, direction, point);
            if (ConstructKind == MagicConstructKind.SummonBolt && !directHit.IsAlive &&
                Runner.TryFindObject(ShooterId, out NetworkObject catOwner) && catOwner != null)
                catOwner.GetComponent<Player>()?.NotifySummonKillAudio(point);
        }
        if (directHit == null && summonHit == null && !IsMine && stats.effect != MagicEffectKind.AreaDamage)
            NotifyShooterFaceMiss();
        RPC_Impact(point, Magic);
        // Leave the object alive briefly so the impact RPC reaches all observers.
        Lifetime = TickTimer.CreateFromSeconds(Runner, 0.2f);
    }

    private bool TryBeginIceFollowUp(Player victim, Vector3 point)
    {
        if (Magic != MagicType.Ice || ConstructKind != MagicConstructKind.Projectile || victim.IsParrying ||
            !Runner.TryFindObject(ShooterId, out NetworkObject source) || source == null) return false;
        Player caster = source.GetComponent<Player>();
        MagicStatTable table = caster != null ? caster.MagicTable : null;
        if (table == null || !table.iceImpactFollowUpEnabled) return false;
        float interval = Mathf.Clamp(table.iceImpactLaunchInterval, .01f, .5f);
        float flight = Mathf.Clamp(Vector3.Distance(caster.LockAimPoint, victim.LockAimPoint) /
            Mathf.Max(1f, table.iceImpactShardSpeed), .18f, 4f);
        float total = (IceImpactFollowUpVisual.ShardCount - 1) * interval + flight;
        iceDamageVictim = victim;
        iceDamagePending = true;
        iceDamageTimer = TickTimer.CreateFromSeconds(Runner, total);
        Lifetime = TickTimer.CreateFromSeconds(Runner, total + .3f);
        RPC_IceFollowUp(victim.Object.Id, point, interval, flight);
        return true;
    }

    private void TickIceFollowUpDamage()
    {
        if (!Object.HasStateAuthority || !iceDamagePending || !iceDamageTimer.Expired(Runner)) return;
        iceDamagePending = false; // Clear before damage callbacks; the whole batch deals one hit.
        Player victim = iceDamageVictim;
        iceDamageVictim = null;
        if (victim != null && victim.Object != null && victim.Object.IsValid && victim.Runner == Runner &&
            victim.IsAlive && victim.TeamIndex != ShooterTeam)
            victim.ReceiveMagicHit(stats, Shooter, ShooterId);
        Lifetime = TickTimer.CreateFromSeconds(Runner, .2f);
    }

    private bool HasBlastSight(Player target)
    {
        return HasBlastSight(transform.position, target);
    }

    public static bool HasBlastSight(Vector3 origin, Player target)
    {
        // Rays alone can miss a wall enclosing their origin.
        int overlapCount = Physics.OverlapSphereNonAlloc(origin, 0.01f, blastOverlaps,
            ~0, QueryTriggerInteraction.Ignore);
        Collider[] overlaps = blastOverlaps;
        if (overlapCount == overlaps.Length)
        {
            overlaps = Physics.OverlapSphere(origin, 0.01f, ~0, QueryTriggerInteraction.Ignore);
            overlapCount = overlaps.Length;
        }
        for (int i = 0; i < overlapCount; i++)
        {
            Collider collider = overlaps[i];
            if (collider != null && collider.GetComponentInParent<Player>() == null &&
                collider.GetComponentInParent<MagicProjectile>() == null)
                return false;
        }
        Vector3 delta = target.LockAimPoint - origin;
        int hitCount = Physics.RaycastNonAlloc(origin, delta.normalized, blastHits,
            delta.magnitude, ~0, QueryTriggerInteraction.Ignore);
        RaycastHit[] hits = blastHits;
        if (hitCount == hits.Length)
        {
            hits = Physics.RaycastAll(origin, delta.normalized, delta.magnitude,
                ~0, QueryTriggerInteraction.Ignore);
            hitCount = hits.Length;
        }
        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = hits[i];
            if (hit.collider == null || hit.collider.GetComponentInParent<MagicProjectile>() != null ||
                hit.collider.GetComponentInParent<Player>() != null)
                continue;
            return false;
        }
        return true;
    }

    // Local read-only threat query; no RPCs, allocations, or extra network traffic.
    public static bool TryGetIncomingThreat(Player observer, float range, out float distance)
        => TryGetIncomingThreat(observer, range, out distance, out _);

    public static bool TryGetIncomingThreat(Player observer, float range, out float distance, out MagicProjectile nearest,
        float warningLeadSeconds = 0f, System.Predicate<MagicProjectile> candidateFilter = null,
        List<MagicProjectile> threats = null)
    {
        threats?.Clear();
        distance = range;
        nearest = null;
        if (observer == null || observer.Object == null || !observer.Object.IsValid) return false;
        bool found = false;
        float bestScore = float.PositiveInfinity;
        foreach (MagicProjectile shot in active)
        {
            if (shot == null || shot.Runner != observer.Runner || shot.Object == null ||
                !shot.Object.IsValid || shot.Finished || shot.IsConstruct || (!shot.IsMine && shot.ShooterTeam == observer.TeamIndex))
                continue;
            Vector3 delta = observer.LockAimPoint - shot.transform.position;
            float sqr = delta.sqrMagnitude;
            if (!shot.TargetId.Equals(default(NetworkId)) && !shot.TargetId.Equals(observer.Object.Id)) continue;
            bool timed = shot.TryGetImpactTime(observer, out float impactSeconds);
            if (!timed && !shot.TargetId.Equals(observer.Object.Id)) continue;
            if (sqr > range * range && (!timed || impactSeconds > warningLeadSeconds)) continue;
            if (!shot.Settled && Vector3.Dot(shot.transform.forward, delta) <= 0f) continue;
            // A visible foreground shot must not mask a different off-screen threat.
            if (candidateFilter != null && !candidateFilter(shot)) continue;
            if (!HasBlastSight(shot.transform.position, observer)) continue;
            threats?.Add(shot);
            float candidateDistance = Mathf.Sqrt(sqr);
            // Prefer the first predicted collision, not a nearby but slow/passing projectile.
            float score = timed ? impactSeconds : 10000f + candidateDistance;
            if (score >= bestScore) continue;
            bestScore = score;
            distance = candidateDistance;
            nearest = shot;
            found = true;
        }
        return found;
    }

    public static bool HasMineOwnedBy(PlayerRef player, NetworkRunner runner = null)
    {
        foreach (MagicProjectile shot in active)
            if (shot != null && (runner == null || shot.Runner == runner) && shot.Object != null && shot.Object.IsValid &&
                (shot.IsMine || shot.ConstructKind == MagicConstructKind.Summon) && !shot.Finished && shot.Shooter == player)
                return true;
        return false;
    }

    // Bot decision tick only. Cheap closing-time estimate; unlike the HUD/parry query
    // this does not integrate every shot's future path. Read the actual host flight state.
    public static bool TryGetBotHomingThreat(Player observer, float range, out MagicProjectile threat,
        out float arrival, out float currentTurnRate, out float initialTurnRate)
    {
        threat = null; arrival = float.PositiveInfinity; currentTurnRate = initialTurnRate = 0f;
        if (observer == null || observer.Object == null || !observer.Object.IsValid) return false;
        Vector3 targetVelocity = observer.ParryHintVelocity;
        foreach (var shot in active)
        {
            if (shot == null || shot.Object == null || !shot.Object.IsValid || !shot.initialized ||
                !shot.TargetId.Equals(observer.Object.Id) || !shot.HasFlightExhaustion || !shot.IsThreatTo(observer)) continue;
            Vector3 delta = observer.LockAimPoint - shot.transform.position;
            float squared = delta.sqrMagnitude;
            if (squared > range * range || Vector3.Dot(shot.transform.forward, delta) <= 0f) continue;
            float timeScale = TimeProjectileMultiplier(shot.Runner, shot.transform.position);
            float speed = shot.stats.projectileSpeed * shot.FlightSpeedFraction;
            float closing = Vector3.Dot(shot.transform.forward * speed * timeScale - targetVelocity, delta.normalized);
            if (closing <= .01f) continue;
            float eta = Mathf.Sqrt(squared) / closing;
            if (eta >= arrival || !HasBlastSight(shot.transform.position, observer)) continue;
            var table = shot.flightSettings != null ? shot.flightSettings : MagicStatTable.Default;
            currentTurnRate = FlightTurnSpeed(shot.stats.projectileTurnSpeed, speed, table) * timeScale;
            initialTurnRate = FlightTurnSpeed(shot.stats.projectileTurnSpeed, shot.stats.projectileSpeed, table) * timeScale;
            threat = shot; arrival = eta;
        }
        return threat != null;
    }

    public bool IsThreatTo(Player observer)
    {
        if (observer == null || observer.Object == null || !observer.Object.IsValid ||
            Object == null || !Object.IsValid || Runner != observer.Runner || Finished ||
            IsMine || IsConstruct || Settled || FlightExhausted || ShooterTeam == observer.TeamIndex) return false;
        if (!TargetId.Equals(default(NetworkId))) return TargetId.Equals(observer.Object.Id);
        return TryGetImpactTime(observer, out _);
    }

    // Warning/bot reaction estimate only: never delays a shot or grants a block. Hitscan spells
    // have no MagicProjectile instance and therefore never enter this warning path.
    public bool TryGetImpactTime(Player observer, out float seconds)
    {
        seconds = float.PositiveInfinity;
        if (observer == null || observer.Object == null || !observer.Object.IsValid ||
            Object == null || !Object.IsValid || Runner != observer.Runner || Finished || IsMine || IsConstruct || Settled ||
            ShooterTeam == observer.TeamIndex || FlightExhausted) return false;
        MagicStatEntry entry = PresentationStats;
        MagicStatTable guidance = flightSettings != null ? flightSettings : MagicStatTable.Default;
        float baseSpeed = entry.projectileSpeed;
        float timeScale = TimeProjectileMultiplier(Runner, transform.position);
        if (HasFlightExhaustion) entry.projectileSpeed *= FlightSpeedFraction;
        entry.projectileSpeed *= timeScale;
        if (entry.projectileSpeed <= 0f) return false;
        Vector3 offset = transform.position - observer.LockAimPoint;
        bool guidedToObserver = entry.requiresTarget && TargetId.Equals(observer.Object.Id);
        Vector3 forward = transform.forward;
        float radius = Mathf.Max(0.02f,entry.projectileRadius) + observer.ParryHintRadius;
        Vector3 observerVelocity = observer.ParryHintVelocity;
        if (guidedToObserver && entry.projectileTurnSpeed > 0f)
        {
            // Follow the limited arc against constant target velocity. A shot flying past
            // the defender is no longer treated as pointing directly at them this tick.
            float distanceLeft = Mathf.Max(0f, FlightDistance(entry, guidance) - TravelledDistance);
            float horizon = Mathf.Min(4f, Mathf.Max(0f, Lifetime.RemainingTime(Runner) ?? 0f));
            float fraction = HasFlightExhaustion ? FlightSpeedFraction : 1f;
            for(float elapsed=0f;elapsed<horizon;)
            {
                float dt = Mathf.Min(1f/30f,horizon-elapsed);
                Vector3 before = forward;
                Vector3 desired = HasFlightExhaustion
                    ? HomingAimDelta(-offset, observerVelocity, baseSpeed * fraction * timeScale, guidance.homingLeadSeconds)
                    : -offset;
                float turnSpeed = HasFlightExhaustion
                    ? FlightTurnSpeed(entry.projectileTurnSpeed, baseSpeed * fraction, guidance)
                    : entry.projectileTurnSpeed;
                forward = ResolveHomingDirection(true, forward, desired, turnSpeed, dt * timeScale);
                if (HasFlightExhaustion)
                {
                    fraction = DrainFlightSpeed(fraction, before, forward, dt * timeScale, guidance);
                    if (fraction <= guidance.homingFallThreshold) return false;
                }
                float speed = baseSpeed * fraction * timeScale;
                if (speed <= .001f || distanceLeft <= 0f) return false;
                dt = Mathf.Min(dt, distanceLeft / speed);
                Vector3 relative = forward * speed - observerVelocity;
                if(TryEstimateImpactTime(offset,relative,radius,false,out float contact) && contact<=dt)
                { seconds=elapsed+contact; return true; }
                offset += relative*dt;
                distanceLeft -= speed * dt;
                elapsed += dt;
            }
            return false;
        }
        Vector3 relativeVelocity = forward * entry.projectileSpeed - observerVelocity;
        return TryEstimateImpactTime(offset, relativeVelocity,
            radius, false, out seconds);
    }

    public static bool TryEstimateImpactTime(Vector3 offset, Vector3 relativeVelocity,
        float hitRadius, bool guided, out float seconds)
    {
        seconds = float.PositiveInfinity;
        if (!IsFiniteEstimateVector(offset) || !IsFiniteEstimateVector(relativeVelocity) ||
            float.IsNaN(hitRadius) || float.IsInfinity(hitRadius)) return false;
        float radius = Mathf.Max(0f, hitRadius);
        float c = offset.sqrMagnitude - radius * radius;
        if (c <= 0f) { seconds = 0f; return true; }
        float toward = Vector3.Dot(offset, relativeVelocity);
        if (toward >= -0.0001f) return false;
        if (guided)
        {
            // Homing re-aims each tick; current radial closing speed is the useful estimate.
            float distance = offset.magnitude;
            seconds = (distance - radius) / (-toward / distance);
            return true;
        }
        float a = relativeVelocity.sqrMagnitude;
        float discriminant = toward * toward - a * c;
        if (a < 0.0001f || discriminant < 0f) return false; // A passing shot is not a cue.
        // Stable form of the first ray/sphere contact root (avoids subtracting similar numbers).
        seconds = c / (-toward + Mathf.Sqrt(discriminant));
        return seconds >= 0f;
    }

    private static bool IsFiniteEstimateVector(Vector3 value)
    {
        return !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
            !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
            !float.IsNaN(value.z) && !float.IsInfinity(value.z) && !float.IsInfinity(value.sqrMagnitude);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_IceFollowUp(NetworkId victimId, Vector3 impactPoint, float launchInterval = -1f, float flightSeconds = -1f)
    {
        if (iceFollowUpPresented || Magic != MagicType.Ice) return;
        iceFollowUpPresented = true;
        flightAudio.Stop();
        if (!Runner.TryFindObject(ShooterId, out NetworkObject shooter) || shooter == null) return;
        Player caster = shooter.GetComponent<Player>();
        if (caster == null) return;
        BattleAudio.PlayMagic(MagicType.Ice, MagicSound.FollowUp, caster.MagicCastPosition, caster.transform);
        Player victim = Runner.TryFindObject(victimId, out NetworkObject hit) && hit != null
            ? hit.GetComponent<Player>() : null;
        iceFollowUpStarted = IceImpactFollowUpVisual.Play(caster, victim, impactPoint, caster.MagicTable, launchInterval, flightSeconds);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_Impact(Vector3 position, MagicType magic)
    {
        flightAudio.Stop();
        impactPresented = true;
        if (IsConstruct) ClearConstructVisual(true);
        if (view != null) view.ConfirmImpact();
        if (ConstructKind == MagicConstructKind.ThunderArea)
        {
            MagicStatEntry visualStats = PresentationStats;
            visualStats.radius = ConstructRadius;
            BattleAudio.Play(BattleSound.ThunderExplosion, position);
            MagicAreaVisual.ThunderBurst(position, visualStats);
            return; // The expanding burst owns Impact VFX too; do not play it twice.
        }
        // Reliable RPCs on this object arrive in order: a confirmed Ice follow-up owns its final impact.
        // Walls/parries/disabled follow-ups/missing casters retain the immediate impact fallback.
        if (magic != MagicType.Ice || !iceFollowUpStarted)
        {
            Player caster = Runner.TryFindObject(ShooterId, out NetworkObject shooter) && shooter != null
                ? shooter.GetComponent<Player>() : null;
            CombatPresentation.ShowImpact(position, magic, caster);
        }
    }

    public override void Render()
    {
        if (Magic == MagicType.None) return;
        if (ConstructKind == MagicConstructKind.TimeField) { RenderTimeField(); return; }
        if (ConstructKind == MagicConstructKind.FlareField) { RenderFlareField(); return; }
        UpdateFlightAudio();
        if (IsConstruct) { RenderConstruct(); return; }
        if (view == null || !view.gameObject.activeSelf) view = ProjectilePredictionView.Attach(this, PresentationStats, view);
        if (Finished || impactPresented) view.ConfirmImpact();
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        flightAudio.Stop();
        active.Remove(this);
        ClearFlareField();
        ClearTimeField();
        ClearConstructVisual(true);
        if (view != null) view.ReleaseForReuse();
        initialized = false;
    }

    private void OnDestroy()
    {
        flightAudio.Stop();
        ClearConstructVisual();
        active.Remove(this);
        ClearFlareField();
        ClearTimeField();
    }

    private void UpdateFlightAudio()
    {
        if (Finished || impactPresented) { flightAudio.Stop(); return; }
        if (!audioStarted)
        {
            audioStarted = true;
            if (ConstructKind != MagicConstructKind.ThunderArea)
                flightAudio = BattleAudio.StartMagicLoop(Magic, MagicSound.ProjectileLoop, transform);
            if (ConstructKind == MagicConstructKind.SummonBolt)
                BattleAudio.PlayMagic(Magic, MagicSound.SummonAttack, transform.position);
        }
    }
}
