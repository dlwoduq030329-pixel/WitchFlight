using System.Collections.Generic;
using Fusion;
using UnityEngine;
using Unity.Profiling;

// Uses the existing runner. Only StateAuthority simulates flight and damage.
public sealed partial class MagicProjectile : NetworkBehaviour
{
    private static readonly ProfilerMarker simulationMarker = new("WitchFlight.Projectile.Simulate");
    private static readonly HashSet<MagicProjectile> active = new();
    [Networked] public MagicType Magic { get; private set; }
    [Networked] public NetworkId ShooterId { get; private set; }
    [Networked] public PlayerRef Shooter { get; private set; }
    [Networked] public int ShooterTeam { get; private set; }
    [Networked] public NetworkId TargetId { get; private set; }
    [Networked] public bool IsMine { get; private set; }
    [Networked] public bool Settled { get; private set; }
    [Networked] public bool Finished { get; private set; }
    [Networked] public int PredictionInputTick { get; private set; }
    [Networked] public float TravelledDistance { get; private set; }
    [Networked] private TickTimer Lifetime { get; set; }
    [Networked] private TickTimer ArmTimer { get; set; }

    private MagicStatEntry stats;
    private Vector3 direction;
    private bool initialized;
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
    private readonly Collider[] overlapHits = new Collider[32];
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
    }

    public void Initialize(Player shooter, Player target, MagicStatEntry entry, Vector3 aim, int predictionInputTick = 0, NetworkId summonTargetId = default)
    {
        if (!Object.HasStateAuthority)
            return;
        ConstructKind = MagicConstructKind.Projectile;
        stats = entry;
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
        direction = ResolveLaunchDirection(entry.requiresTarget && !IsMine, aim,
            shooter.transform.forward, target != null ? target.LockAimPoint - transform.position : Vector3.zero);
        if (direction.sqrMagnitude < 0.5f)
            direction = shooter.transform.forward;
        transform.rotation = Quaternion.LookRotation(direction);
        float duration = IsMine
            ? entry.placementDistance / Mathf.Max(1f, entry.projectileSpeed) +
                entry.activationDelay + entry.effectDuration
            : entry.projectileLifetime;
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
        if (IsConstruct) { TickConstruct(); return; }
        if (iceDamagePending) { TickIceFollowUpDamage(); return; }
        if (Lifetime.Expired(Runner))
        {
            if (!Finished && !IsMine && stats.effect == MagicEffectKind.AreaDamage)
                Impact(transform.position);
            else
                Runner.Despawn(Object);
            return;
        }
        if (Finished)
            return;
        if (IsMine && Settled)
        {
            CheckMine();
            return;
        }

        Player target = FindTarget();
        MagicProjectile summonTarget = FindSummonTargetById();
        if ((target != null || summonTarget != null) && (stats.requiresTarget || stats.projectileTurnSpeed > 0f))
        {
            Vector3 desired = (target != null ? target.LockAimPoint : summonTarget.transform.position) - transform.position;
            if (desired.sqrMagnitude > 0.001f)
                direction = ResolveHomingDirection(stats.requiresTarget, direction, desired,
                    stats.projectileTurnSpeed, Runner.DeltaTime);
        }

        float limit = IsMine ? stats.placementDistance : stats.range;
        float step = Mathf.Min(stats.projectileSpeed * Runner.DeltaTime, Mathf.Max(0f, limit - TravelledDistance));
        Vector3 next = transform.position + direction * step;
        if (TryGetInitialOverlap(out Collider overlap))
        {
            if (IsMine)
                SettleMine();
            else
                Impact(transform.position, overlap.GetComponentInParent<Player>());
            return;
        }
        bool collided = TryGetCollision(step, out RaycastHit hit);
        if (TryTraceSummon(Runner, ShooterTeam, transform.position, direction,
            collided ? hit.distance : step, stats.projectileRadius, out MagicProjectile body, out float bodyDistance))
        {
            Impact(transform.position + direction * bodyDistance, null, body);
            return;
        }
        if (collided)
        {
            next = transform.position + direction * Mathf.Max(0f, hit.distance - 0.02f);
            transform.position = next;
            if (IsMine)
                SettleMine();
            else
                Impact(next, hit.collider.GetComponentInParent<Player>());
            return;
        }

        transform.SetPositionAndRotation(next, Quaternion.LookRotation(direction));
        TravelledDistance += step;
        if (TravelledDistance >= limit - 0.001f)
        {
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
        // Summon bodies share the same network target ID path as players.
        if (target == null && FindSummonTargetById() != null) return null;
        if (target == null || !target.IsAlive || target.IsStealthed || target.TeamIndex == ShooterTeam)
        {
            TargetId = default;
            return null;
        }
        return target;
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
            if (hit.distance >= best)
                continue;
            best = hit.distance;
            nearest = hit;
        }
        return best < float.PositiveInfinity;
    }

    private bool TryGetInitialOverlap(out Collider overlap)
    {
        float radius = Mathf.Max(0.02f, stats.projectileRadius);
        int count = Physics.OverlapSphereNonAlloc(transform.position, radius, overlapHits,
            ~0, QueryTriggerInteraction.Ignore);
        Collider[] hits = overlapHits;
        if (count == hits.Length)
        {
            hits = Physics.OverlapSphere(transform.position, radius, ~0, QueryTriggerInteraction.Ignore);
            count = hits.Length;
        }
        for (int i = 0; i < count; i++)
        {
            Collider collider = hits[i];
            if (collider == null || collider.transform.IsChildOf(transform))
                continue;
            Player player = collider.GetComponentInParent<Player>();
            if (player != null && (!player.IsAlive || player.Object.Id.Equals(ShooterId)))
                continue;
            overlap = collider;
            return true;
        }
        overlap = null;
        return false;
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

    private void Impact(Vector3 point, Player directHit = null, MagicProjectile summonHit = null)
    {
        if (Finished)
            return;
        Finished = true;
        transform.position = point;
        if (IsMine || stats.effect == MagicEffectKind.AreaDamage)
        {
            DamageSummonsInArea(Runner, ShooterTeam, point, stats.radius, stats, ShooterId);
            foreach (Player player in Player.ActiveCombatants)
            {
                if (player == null || player.Runner != Runner) continue;
                if (player == null || !player.IsAlive || (!IsMine && player.TeamIndex == ShooterTeam) ||
                    (player.LockAimPoint - point).sqrMagnitude > stats.radius * stats.radius || !HasBlastSight(player))
                    continue;
                player.ReceiveMagicHit(PerkEffects.AreaHit(stats, Vector3.Distance(player.LockAimPoint,point)), Shooter, ShooterId, player.LockAimPoint - point);
            }
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
            directHit.ReceiveMagicHit(stats, Shooter, ShooterId, direction);
            if (ConstructKind == MagicConstructKind.SummonBolt && !directHit.IsAlive &&
                Runner.TryFindObject(ShooterId, out NetworkObject catOwner) && catOwner != null)
                catOwner.GetComponent<Player>()?.NotifySummonKillAudio(point);
        }
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
            bool timed = shot.TryGetImpactTime(observer, out float impactSeconds);
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

    // Warning/bot reaction estimate only: never delays a shot or grants a block. Hitscan spells
    // have no MagicProjectile instance and therefore never enter this warning path.
    public bool TryGetImpactTime(Player observer, out float seconds)
    {
        seconds = float.PositiveInfinity;
        if (observer == null || observer.Object == null || !observer.Object.IsValid ||
            Object == null || !Object.IsValid || Runner != observer.Runner || Finished || IsMine || IsConstruct || Settled ||
            ShooterTeam == observer.TeamIndex) return false;
        MagicStatEntry entry = PresentationStats;
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
            float horizon = Mathf.Min(4f,Mathf.Max(0f,entry.range-TravelledDistance)/entry.projectileSpeed);
            for(float elapsed=0f;elapsed<horizon;)
            {
                float dt = Mathf.Min(1f/30f,horizon-elapsed);
                forward = ResolveHomingDirection(true,forward,-offset,entry.projectileTurnSpeed,dt);
                Vector3 relative = forward*entry.projectileSpeed-observerVelocity;
                if(TryEstimateImpactTime(offset,relative,radius,false,out float contact) && contact<=dt)
                { seconds=elapsed+contact; return true; }
                offset += relative*dt;
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
        UpdateFlightAudio();
        if (IsConstruct) { RenderConstruct(); return; }
        if (view == null) view = ProjectilePredictionView.Attach(this, PresentationStats);
        if (Finished || impactPresented) view.ConfirmImpact();
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        flightAudio.Stop();
        active.Remove(this);
        ClearConstructVisual(true);
        if (view != null) Destroy(view.gameObject);
        view = null;
        initialized = false;
    }

    private void OnDestroy()
    {
        flightAudio.Stop();
        ClearConstructVisual();
        active.Remove(this);
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
