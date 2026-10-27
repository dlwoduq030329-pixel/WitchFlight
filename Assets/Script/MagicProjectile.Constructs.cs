using Fusion;
using UnityEngine;

// Reuses the registered MagicProjectile NetworkObject and NetworkTransform. No second runner,
// per-frame RPC, unregistered runtime NetworkBehaviour, or client-authoritative damage.
public enum MagicConstructKind { Projectile, ThunderArea, Summon, SummonBolt }

public sealed partial class MagicProjectile
{
    [Networked] public MagicConstructKind ConstructKind { get; private set; }
    [Networked] public float ConstructRadius { get; private set; }
    [Networked] public float ConstructDelay { get; private set; }
    [Networked] public float SummonLockProgress { get; private set; }
    [Networked] public bool SummonMoving { get; private set; }
    [Networked] public int SummonAttackSequence { get; private set; }
    [Networked] private TickTimer ConstructTimer { get; set; }
    private TickTimer summonShotTimer, summonScanTimer;
    private NetworkPrefabRef summonProjectilePrefab;
    private int constructSpawnTick;
    private MagicAreaVisual areaVisual;
    private MagicSummonVisual summonVisual;
    private readonly SummonFlightMotion summonFlight = new();
    private TickTimer summonPursuitRefresh;
    private NetworkId summonPursuitTarget;
    private Vector3 summonObservedPosition, summonObservedVelocity;
    private const float SummonReactionSeconds = .35f;
    public bool IsConstruct => ConstructKind == MagicConstructKind.ThunderArea || ConstructKind == MagicConstructKind.Summon;
    public MagicStatEntry PresentationStats => initialized ? stats : ConstructKind == MagicConstructKind.SummonBolt
        ? SummonBoltStats(CombatPresentation.Stats(Magic)) : CombatPresentation.Stats(Magic);

    public static MagicStatEntry SummonBoltStats(MagicStatEntry source)
    {
        source.effect = MagicEffectKind.DirectDamage;
        source.requiresTarget = source.requiresFullLock = true;
        source.castSeconds = 0f;
        source.radius = 0f;
        return source;
    }

    public void InitializeConstruct(Player owner, MagicStatEntry entry, NetworkPrefabRef projectilePrefab)
    {
        Initialize(owner, null, entry, owner.transform.forward);
        if (!Object.HasStateAuthority) return;
        summonProjectilePrefab = projectilePrefab;
        constructSpawnTick = Runner.Tick.Raw;
        ConstructKind = entry.effect == MagicEffectKind.DelayedArea ? MagicConstructKind.ThunderArea : MagicConstructKind.Summon;
        ConstructRadius = Mathf.Max(.1f, entry.radius);
        ConstructDelay = Mathf.Max(.1f, entry.strikeDelay);
        Settled = true; // Construct movement is not a predicted projectile flight path.
        ConstructTimer = TickTimer.CreateFromSeconds(Runner, ConstructKind == MagicConstructKind.ThunderArea
            ? ConstructDelay : Mathf.Max(.01f,entry.activationDelay));
        Lifetime = TickTimer.CreateFromSeconds(Runner, ConstructKind == MagicConstructKind.ThunderArea
            ? ConstructDelay + 1f : Mathf.Max(.1f,entry.effectDuration));
        summonShotTimer = summonScanTimer = TickTimer.None;
        SummonLockProgress = 0f;
        SummonDamageTaken = 0f;
        SummonMoving = false;
        SummonAttackSequence = 0;
        summonPursuitRefresh = TickTimer.None;
        summonPursuitTarget = default;
        ResetSummonNavigation();
        if (ConstructKind == MagicConstructKind.Summon)
        {
            summonFlight.Initialize(owner.SummonLaunchVelocity, owner.transform.forward, entry.summonMoveSpeed,
                owner.SteeringTurnRate, owner.SummonFlightTurnResponse, owner.SummonFlightAcceleration, owner.SummonFlightBraking);
            direction = summonFlight.Heading;
            transform.rotation = Quaternion.LookRotation(direction);
        }
    }

    public static void TrimSummons(Player owner, int limit, NetworkObject newest)
    {
        // Snapshot candidates before Despawn mutates the active set. Bounded by the per-owner limit.
        var owned = new System.Collections.Generic.List<MagicProjectile>();
        foreach (var shot in active)
            if (shot != null && shot.Runner == owner.Runner && shot.Object != null && shot.Object.IsValid &&
                shot.Object.HasStateAuthority && shot.ConstructKind == MagicConstructKind.Summon &&
                shot.Shooter == owner.ParticipantRef && shot.Object != newest) owned.Add(shot);
        owned.Sort((a,b) => a.constructSpawnTick != b.constructSpawnTick
            ? a.constructSpawnTick.CompareTo(b.constructSpawnTick) : a.Object.Id.Raw.CompareTo(b.Object.Id.Raw));
        for (int i=0; i < owned.Count + 1 - Mathf.Clamp(limit,1,16); i++) owner.Runner.Despawn(owned[i].Object);
    }

    private void TickConstruct()
    {
        if (Object == null || !Object.IsValid || !Object.HasStateAuthority) return;
        SummonMoving = false;
        if (Finished || Lifetime.Expired(Runner))
        {
            if (Lifetime.Expired(Runner)) Runner.Despawn(Object);
            return;
        }
        if (ConstructKind == MagicConstructKind.ThunderArea)
        {
            if (!ConstructTimer.Expired(Runner)) return;
            Finished = true; // Guard before damage callbacks; each victim is hit at most once.
            Vector3 center = transform.position;
            foreach (Player enemy in Player.ActiveCombatants)
            {
                if (enemy == null || enemy.Object == null || !enemy.Object.IsValid || enemy.Runner != Runner ||
                    !enemy.IsAlive || enemy.TeamIndex == ShooterTeam) continue;
                float distance = Vector3.Distance(center, enemy.LockAimPoint);
                if (distance <= ConstructRadius && HasBlastSight(center,enemy))
                    enemy.ReceiveMagicHit(PerkEffects.AreaHit(stats,distance), Shooter, ShooterId, enemy.LockAimPoint - center);
            }
            DamageSummonsInArea(Runner, ShooterTeam, center, ConstructRadius, stats, ShooterId);
            RPC_Impact(center, Magic);
            Lifetime = TickTimer.CreateFromSeconds(Runner,.25f);
            return;
        }
        if (!Runner.TryFindObject(ShooterId,out NetworkObject obj) || obj == null ||
            !obj.TryGetComponent(out Player owner) || !owner.IsAlive)
        { Runner.Despawn(Object); return; }
        Player target = FindTarget();
        if (!summonScanTimer.IsRunning || summonScanTimer.Expired(Runner))
        {
            summonScanTimer = TickTimer.CreateFromSeconds(Runner,.1f);
            if (!IsSummonTarget(target))
            {
                target = FindSummonTarget();
                TargetId = target != null ? target.Object.Id : default;
                SummonLockProgress = 0f;
            }
        }
        Vector3 delta = target != null ? target.LockAimPoint - transform.position : Vector3.zero;
        bool launching = summonFlight.Launching;
        Vector3 pursuitDelta = delta;
        if (target != null)
        {
            if (summonPursuitTarget != target.Object.Id || summonPursuitRefresh.ExpiredOrNotRunning(Runner))
            {
                summonPursuitTarget = target.Object.Id;
                summonObservedPosition = target.LockAimPoint;
                summonObservedVelocity = target.ParryHintVelocity;
                summonPursuitRefresh = TickTimer.CreateFromSeconds(Runner,SummonReactionSeconds);
            }
            float sinceObservation = SummonReactionSeconds - (summonPursuitRefresh.RemainingTime(Runner) ?? 0f);
            // Chase the last observed flight path. Abrupt braking creates a real overshoot window.
            pursuitDelta = summonObservedPosition + summonObservedVelocity*(.45f+sinceObservation) - transform.position;
        }
        else { summonPursuitTarget = default; summonPursuitRefresh = TickTimer.None; }
        MoveSummonWithNavigation(pursuitDelta, target != null);
        direction = summonFlight.Heading;
        transform.rotation = Quaternion.LookRotation(summonFlight.Heading);
        if (target == null || launching || !ConstructTimer.Expired(Runner)) { SummonLockProgress = 0f; return; }
        delta = target.LockAimPoint - transform.position;
        float distanceToTarget = delta.magnitude;
        if (distanceToTarget > Mathf.Max(1f,stats.summonAttackRange) ||
            Vector3.Dot(summonFlight.Heading,delta.normalized) < .5f || !HasBlastSight(target))
        { SummonLockProgress = 0f; return; }
        SummonLockProgress = Mathf.Clamp01(SummonLockProgress + Runner.DeltaTime / Mathf.Max(.05f,stats.lockChargeSeconds));
        if (SummonLockProgress < 1f || (summonShotTimer.IsRunning && !summonShotTimer.Expired(Runner))) return;
        NetworkObject bolt = Runner.Spawn(summonProjectilePrefab,transform.position,transform.rotation,Shooter);
        if (bolt != null)
        {
            var projectile = bolt.GetComponent<MagicProjectile>();
            projectile.Initialize(owner,target,SummonBoltStats(stats),delta.normalized);
            projectile.ConstructKind = MagicConstructKind.SummonBolt;
            SummonAttackSequence++;
        }
        SummonLockProgress = 0f;
        summonShotTimer = TickTimer.CreateFromSeconds(Runner,Mathf.Max(.1f,stats.summonAttackInterval));
    }

    // Retain the enemy while chasing: range/occlusion limit acquisition or firing, not pursuit.
    private bool IsSummonTarget(Player target) => target != null && target.Object != null && target.Object.IsValid &&
        target.Runner == Runner && target.IsAlive && !target.IsStealthed && target.TeamIndex != ShooterTeam;

    private Player FindSummonTarget()
    {
        Player best = null;
        float distance = stats.range * stats.range;
        foreach (Player candidate in Player.ActiveCombatants)
        {
            if (!IsSummonTarget(candidate)) continue;
            float sqr = (candidate.LockAimPoint-transform.position).sqrMagnitude;
            if (sqr < distance) { best = candidate; distance = sqr; }
        }
        return best;
    }

    private void RenderConstruct()
    {
        if (Finished || impactPresented)
        { ClearConstructVisual(true); return; }
        if (ConstructKind == MagicConstructKind.ThunderArea)
        {
            if (areaVisual == null)
            {
                areaVisual = MagicAreaVisual.Create(PresentationStats.areaWarningVfxPrefab,"Thunder confirmed area");
                MagicAreaVisual.ThunderFlash(transform.position, ConstructRadius, PresentationStats);
            }
            bool friendly = Player.LocalPlayer != null && Player.LocalPlayer.Object != null && Player.LocalPlayer.Object.IsValid &&
                Player.LocalPlayer.TeamIndex == ShooterTeam;
            float remaining = ConstructTimer.RemainingTime(Runner) ?? 0f;
            areaVisual.SetArea(transform.position,ConstructRadius, friendly ? new Color(.2f,1f,.9f,.85f) : new Color(1f,.2f,.08f,.9f),
                1f-Mathf.Clamp01(remaining / Mathf.Max(.1f,ConstructDelay)));
        }
        else
        {
            if (summonVisual == null) summonVisual = MagicSummonVisual.Create(PresentationStats, transform, SummonAttackSequence, this);
            // Render is read-only. FindTarget clears invalid IDs and is simulation-only.
            Player target = Runner.TryFindObject(TargetId,out NetworkObject targetObject) && targetObject != null
                ? targetObject.GetComponent<Player>() : null;
            summonVisual.SetLock(target, SummonLockProgress);
            summonVisual.SetMotion(SummonMoving, SummonAttackSequence);
        }
    }

    private void ClearConstructVisual(bool playSummonDeath = false)
    {
        if (areaVisual != null) Destroy(areaVisual.gameObject);
        if (summonVisual != null)
        {
            if (playSummonDeath) summonVisual.Retire();
            else Destroy(summonVisual.gameObject);
        }
        areaVisual = null; summonVisual = null;
    }
}
