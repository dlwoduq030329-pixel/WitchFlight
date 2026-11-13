using Fusion;
using UnityEngine;

public sealed class RaidMagicProjectile : MagicProjectile
{
    [Networked] public float RaidShotSpeed { get; private set; }
    [Networked] public float RaidShotRange { get; private set; }
    [Networked] public float RaidShotRadius { get; private set; }
    [Networked] public bool InFlight { get; private set; }
    [Networked] public int FlightSequence { get; private set; }
    private int shownSequence = -1;
    private GameObject flightVisual;
    private MagicVfxLease visualLease;
    private NetworkTransform networkTransform;
    private BattleAudio.Loop loopAudio;
    private GiantMeowBoss caster;
    private readonly Collider[] overlaps = new Collider[16];
    private Vector3 flightBaseScale;
    private float flightBaseRadius = 1.2f;
    public bool Available => Object != null && Object.IsValid && !InFlight;

    public override void Spawned()
    {
        base.Spawned();
        networkTransform = GetComponent<NetworkTransform>();
        var fire = MagicStatTable.Default.GetStats(MagicType.Fire);
        var director = GiantMeowRaidDirector.Instance;
        if (director != null && director.Boss != null) flightBaseRadius = Mathf.Max(.02f, director.Boss.Settings.barrageHitRadius);
        flightVisual = MagicVfxPool.Rent(fire.projectileVfxPrefab, transform.position, transform.rotation, transform);
        if (flightVisual != null)
        {
            visualLease = flightVisual.GetComponent<MagicVfxLease>();
            TimeVfxSpeed.Bind(flightVisual, Runner, 99, true);
            flightBaseScale = flightVisual.transform.localScale;
            flightVisual.SetActive(false);
        }
        if (Object.HasStateAuthority) { Finished = true; initialized = false; }
    }

    public override void FixedUpdateNetwork()
    {
        if (!Object.HasStateAuthority || !InFlight) return;
        float flightDelta = TimeFlightStep();
        if (Lifetime.Expired(Runner) || BattleManager.Instance == null || !BattleManager.Instance.IsGameplayActive)
        {
            InFlight = false;
            Finished = true;
            initialized = false;
            return;
        }
        if (Finished) return;
        int count = Runner.GetPhysicsScene().OverlapSphere(transform.position, stats.projectileRadius, overlaps, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            var collider = overlaps[i];
            if (collider == null || collider.transform.IsChildOf(transform)) continue;
            var player = collider.GetComponentInParent<Player>();
            if (player != null && !player.IsAlive) continue;
            FinishAt(transform.position, player);
            return;
        }
        float step = Mathf.Min(stats.projectileSpeed * flightDelta, Mathf.Max(0f, stats.range - TravelledDistance));
        bool collided = Runner.GetPhysicsScene().SphereCast(transform.position, stats.projectileRadius, direction,
            out var hit, step, ~0, QueryTriggerInteraction.Ignore);
        if (TryTraceSummon(Runner, ShooterTeam, transform.position, direction, collided ? hit.distance : step,
            stats.projectileRadius, out var summon, out float distance))
        {
            summon.ReceiveSummonHit(stats, ShooterTeam, ShooterId);
            FinishAt(transform.position + direction * distance, null);
            return;
        }
        if (collided) { FinishAt(hit.point, hit.collider.GetComponentInParent<Player>()); return; }
        transform.position += direction * step;
        TravelledDistance += step;
        if (TravelledDistance >= stats.range) FinishAt(transform.position, null);
    }

    private void FinishAt(Vector3 point, Player player)
    {
        if (Finished) return;
        Finished = true;
        transform.position = point;
        if (player != null && player.IsAlive && player.TeamIndex != ShooterTeam)
            player.ReceiveMagicHit(stats, Shooter, ShooterId, direction, point);
        Lifetime = TickTimer.CreateFromSeconds(Runner, .2f);
        RPC_RaidImpact(point);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_RaidImpact(Vector3 point)
    {
        if (caster == null && Runner.TryFindObject(ShooterId, out var owner) && owner != null) caster = owner.GetComponent<GiantMeowBoss>();
        if (caster != null) caster.PresentFireImpact(point);
    }

    public override void Render()
    {
        bool show = InFlight && !Finished;
        if (flightVisual == null) return;
        if (show && shownSequence != FlightSequence)
        {
            loopAudio.Stop();
            shownSequence = FlightSequence;
            visualLease.Restore();
            flightVisual.transform.localPosition = Vector3.zero;
            flightVisual.transform.localRotation = Quaternion.identity;
            flightVisual.transform.localScale = flightBaseScale * Mathf.Max(1f, RaidShotRadius / flightBaseRadius);
            flightVisual.SetActive(true);
            visualLease.Restart();
            loopAudio = BattleAudio.StartMagicLoop(Magic, MagicSound.ProjectileLoop, transform);
        }
        flightVisual.SetActive(show);
        if (!show) loopAudio.Stop();
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        loopAudio.Stop();
        if (flightVisual != null) MagicVfxPool.Release(flightVisual);
        flightVisual = null;
        base.Despawned(runner, hasState);
    }
    public override MagicStatEntry PresentationStats
    {
        get
        {
            var entry = base.PresentationStats;
            entry.projectileSpeed = RaidShotSpeed;
            entry.range = RaidShotRange;
            entry.projectileRadius = RaidShotRadius;
            entry.requiresTarget = entry.requiresFullLock = false;
            entry.projectileTurnSpeed = 0f;
            return entry;
        }
    }
    public void InitializeRaidShot(GiantMeowBoss caster, MagicStatEntry entry, Vector3 origin, Vector3 aim)
    {
        if (!Object.HasStateAuthority) return;
        this.caster = caster;
        ResetTimeFlight();
        stats = entry;
        stats.requiresTarget = stats.requiresFullLock = false;
        stats.projectileTurnSpeed = 0f;
        RaidShotSpeed = stats.projectileSpeed;
        RaidShotRange = stats.range;
        RaidShotRadius = stats.projectileRadius;
        ConstructKind = MagicConstructKind.Projectile;
        Magic = stats.magic;
        Shooter = PlayerRef.None;
        ShooterId = caster.Object.Id;
        ShooterTeam = 99;
        TargetId = default;
        Settled = Finished = IsMine = false;
        TravelledDistance = 0f;
        PredictionInputTick = 0;
        ArmTimer = TickTimer.None;
        direction = aim.normalized;
        transform.SetPositionAndRotation(origin, Quaternion.LookRotation(direction));
        networkTransform.Teleport(origin, Quaternion.LookRotation(direction));
        Lifetime = TickTimer.CreateFromSeconds(Runner, Mathf.Max(.1f, stats.projectileLifetime));
        initialized = true;
        FlightSequence++;
        InFlight = true;
    }
}
