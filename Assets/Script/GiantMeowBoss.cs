using Fusion;
using UnityEngine;
using Unity.Profiling;

public enum RaidPattern { FireVolley = 0, VisionCorridors = 1, AbsorbBurst = 2, FlameBreath = 4, Thunder = 5, Summon = 6 }
public enum RaidPatternStage { Warning, Active, Recovery }
public struct RaidVisionPortal : INetworkStruct
{
    public Vector3 Origin, Direction;
    public int State; // 0 pooled, 1 warning, 2 returning to pool
    public TickTimer Timer;
}

[RequireComponent(typeof(RaidBossTarget))]
public sealed partial class GiantMeowBoss : NetworkBehaviour
{
    private static readonly ProfilerMarker simulationMarker = new ProfilerMarker("WitchFlight.Raid.Simulate");
    public const int BoltCapacity = 192;
    public const int VisionPortalCount = 20;
    [SerializeField] private NetworkPrefabRef projectilePrefab;
    [SerializeField] private GiantMeowRaidSettings settings;
    [SerializeField] private GiantMeowBossView presentation;
    [Networked] public float MaxHealth { get; private set; }
    [Networked] public float Health { get; private set; }
    [Networked] public float AbsorbedDamage { get; private set; }
    [Networked] public BattleStartPhase Phase { get; private set; }
    [Networked] public RaidPattern Pattern { get; private set; }
    [Networked] public RaidPatternStage Stage { get; private set; }
    [Networked] public TickTimer StageTimer { get; private set; }
    [Networked] public Vector3 Aim { get; private set; }
    [Networked] public int WarningCount { get; private set; }
    [Networked, Capacity(4)] public NetworkArray<Vector3> Origins => default;
    [Networked, Capacity(4)] public NetworkArray<Vector3> Directions => default;
    [Networked] public bool Retreating { get; private set; }
    [Networked, Capacity(VisionPortalCount)] public NetworkArray<RaidVisionPortal> VisionPortals => default;
    private int portalsLaunched, visionTargetCursor;
    private float portalSpawnTimer;
    public float VisionRange => visionHit.range;
    public float VisionRadius => Mathf.Max(.05f, visionHit.hitscanRadius);
    private readonly System.Collections.Generic.HashSet<PlayerRef> readyPlayers = new();
    private readonly Player[] targets = new Player[4];
    private readonly RaidMagicProjectile[] shots = new RaidMagicProjectile[BoltCapacity];
    private int preparedShots;
    private readonly NetworkId[] shotIds = new NetworkId[BoltCapacity];
    private float barrageTimer, retreatTimer;
    private int barrageOrigin;
    private Vector3 retreatDestination;
    private int targetCount;
    private float refreshTargets, pulse;
    private RaidBossTarget targetProxy;
    private MagicStatEntry fireHit, visionHit, thunderHit;
    public GiantMeowRaidSettings Settings => settings;
    public bool IsTargetable => Phase == BattleStartPhase.Playing && Health > 0;
    public bool Absorbing => Pattern == RaidPattern.AbsorbBurst && Stage != RaidPatternStage.Recovery && IsTargetable;
    public float ThunderRadius => Mathf.Max(1f, thunderHit.radius) * 2f;
    public float StageDuration => Phase == BattleStartPhase.Intro ? settings.entranceSeconds :
        Phase == BattleStartPhase.Countdown ? 3f : Stage == RaidPatternStage.Warning ? WarningSeconds() :
        Stage == RaidPatternStage.Active ? ActiveSeconds() : settings.recoverySeconds;
    public float StageProgress => Mathf.Clamp01(1f - (StageTimer.RemainingTime(Runner) ?? 0f) / Mathf.Max(.01f, StageDuration));
    public Vector3 Mouth => transform.position + Vector3.up * settings.height * .25f + transform.forward * settings.hitRadius * .55f;
    public Vector3 BreathDirection => Quaternion.AngleAxis(Mathf.Lerp(-settings.breathSweepDegrees * .5f, settings.breathSweepDegrees * .5f, StageProgress), Vector3.up) * Aim;

    private void Awake()
    {
        targetProxy = GetComponent<RaidBossTarget>();
        fireHit = MagicStatTable.Default.GetStats(MagicType.Fire);
        visionHit = MagicStatTable.Default.GetStats(MagicType.Vision);
        thunderHit = MagicStatTable.Default.GetStats(MagicType.Thunder);
    }
    public void Configure(int players)
    {
        MaxHealth = Mathf.Max(1, settings.health + settings.healthPerExtraPlayer * (players - 1));
        Health = MaxHealth;
        Phase = BattleStartPhase.WaitingForPlayers;
    }
    public override void Spawned()
    {
        GiantMeowRaidDirector.Instance?.Register(this);
        targetProxy.Configure(this);
        presentation.Initialize(this);
        SpawnCores();
        SpawnSupportPools();
    }
    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_Ready(RpcInfo info = default)
    {
        PlayerRef participant = info.Source == PlayerRef.None ? Runner.LocalPlayer : info.Source;
        var data = PlayerData.FindPerkData(Runner, participant);
        if (Phase == BattleStartPhase.WaitingForPlayers && data != null && data.IsLoadoutInitialized && !data.IsMatchBot)
            readyPlayers.Add(participant);
    }
    private void RefreshTargets()
    {
        targetCount = 0;
        foreach (var player in Player.ActiveCombatants)
        {
            if (player == null || player.Object == null || !player.Object.IsValid || player.Runner != Runner ||
                player.IsMatchBot || player.IsTestBot || !player.IsAlive || targetCount >= targets.Length) continue;
            int index = targetCount++;
            while (index > 0 && targets[index - 1].ParticipantRef.RawEncoded > player.ParticipantRef.RawEncoded)
            { targets[index] = targets[index - 1]; index--; }
            targets[index] = player;
        }
        for (int i = targetCount; i < targets.Length; i++) targets[i] = null;
    }
    public override void FixedUpdateNetwork()
    {
        if (!Object.HasStateAuthority || Phase == BattleStartPhase.Ended) return;
        using var sample = simulationMarker.Auto();
        refreshTargets -= Runner.DeltaTime;
        if (refreshTargets <= 0) { refreshTargets = .25f; RefreshTargets(); }
        if (Phase == BattleStartPhase.WaitingForPlayers)
        {
            for (int n = 0; n < 8 && preparedShots < shots.Length; n++)
            {
                var obj = Runner.Spawn(projectilePrefab, transform.position, transform.rotation, PlayerRef.None);
                if (obj == null) return;
                shots[preparedShots] = obj.GetComponent<RaidMagicProjectile>();
                shotIds[preparedShots++] = obj.Id;
            }
            if (preparedShots < shots.Length) return;
            int humans = 0;
            bool ready = true;
            foreach (var data in PlayerData.PerkPlayers)
            {
                if (data == null || data.Object == null || !data.Object.IsValid || data.Runner != Runner || data.IsMatchBot) continue;
                humans++; ready &= readyPlayers.Contains(data.ParticipantRef);
            }
            if (humans > 0 && humans == targetCount && ready)
            { InitializeResults(); Phase = BattleStartPhase.Intro; StageTimer = TickTimer.CreateFromSeconds(Runner, settings.entranceSeconds); }
            return;
        }
        if (Phase == BattleStartPhase.Intro || Phase == BattleStartPhase.Countdown)
        {
            if (!StageTimer.Expired(Runner)) return;
            if (Phase == BattleStartPhase.Intro)
            { Phase = BattleStartPhase.Countdown; StageTimer = TickTimer.CreateFromSeconds(Runner, 3); }
            else { Phase = BattleStartPhase.Playing; BeginPattern(RaidPattern.FireVolley); }
            return;
        }
        if (targetCount == 0)
        {
            RefreshTargets();
            if (targetCount == 0) { EndRaid(); return; }
        }
        if (Health <= MaxHealth * .5f) Retreating = true;
        if (Retreating && !Absorbing && !Exposed) MoveAwayFromPlayers();
        TickMinionSpawns();
        TickWall();
        TickThunderHazards();
        barrageTimer -= Runner.DeltaTime;
        if (barrageTimer <= 0f && !Exposed)
        {
            barrageTimer = Mathf.Max(.1f, Retreating ? settings.retreatBarrageInterval : settings.barrageInterval);
            FireBarrage();
        }
        if (Pattern == RaidPattern.VisionCorridors && Stage == RaidPatternStage.Active)
        {
            TickVisionPattern();
            return;
        }
        if (StageTimer.Expired(Runner))
        {
            if (Stage == RaidPatternStage.Warning) BeginAttack();
            else if (Stage == RaidPatternStage.Active)
            {
                if (Pattern == RaidPattern.AbsorbBurst) Burst();
                Stage = RaidPatternStage.Recovery;
                StageTimer = TickTimer.CreateFromSeconds(Runner, settings.recoverySeconds);
            }
            else BeginPattern(Pattern switch
            {
                RaidPattern.FireVolley => RaidPattern.VisionCorridors,
                RaidPattern.VisionCorridors => RaidPattern.AbsorbBurst,
                RaidPattern.AbsorbBurst => RaidPattern.FlameBreath,
                RaidPattern.FlameBreath => RaidPattern.Thunder,
                RaidPattern.Thunder => RaidPattern.Summon,
                _ => RaidPattern.FireVolley
            });
        }
        if (Stage != RaidPatternStage.Active) return;
        pulse -= Runner.DeltaTime;
        if (Pattern == RaidPattern.FlameBreath && pulse <= 0)
        { pulse = Mathf.Max(.05f, settings.damageTickSeconds); Breathe(pulse); }
    }

    private float WarningSeconds() => Pattern switch
    {
        RaidPattern.FireVolley => settings.volleyWarning,
        RaidPattern.VisionCorridors => settings.visionWarning,
        RaidPattern.FlameBreath => settings.breathWarning,
        RaidPattern.Thunder => settings.thunderWarning,
        RaidPattern.Summon => settings.minionSummonWarning,
        _ => 1.5f
    };
    private float ActiveSeconds() => Pattern switch
    {
        RaidPattern.FireVolley => settings.wallPhaseSeconds,
        RaidPattern.VisionCorridors => VisionPortalCount * settings.visionSpawnInterval + settings.visionWarning,
        RaidPattern.AbsorbBurst => settings.absorbSeconds,
        RaidPattern.FlameBreath => settings.breathSeconds,
        _ => 1f
    };
    private void BeginPattern(RaidPattern pattern)
    {
        RefreshTargets();
        Pattern = pattern;
        Stage = RaidPatternStage.Warning;
        StageTimer = TickTimer.CreateFromSeconds(Runner, Mathf.Max(.1f, WarningSeconds()));
        AbsorbedDamage = 0;
        WarningCount = targetCount;
        if (pattern == RaidPattern.AbsorbBurst) PrepareCores();
        if (pattern == RaidPattern.VisionCorridors)
        {
            portalsLaunched = visionTargetCursor = 0;
            portalSpawnTimer = 0f;
            for (int i = 0; i < VisionPortalCount; i++) VisionPortals.Set(i, default);
            Stage = RaidPatternStage.Active;
            StageTimer = TickTimer.None;
            return;
        }
        Vector3 destination = targetCount > 0 ? targets[0].LockAimPoint : transform.position + transform.forward * 50;
        Aim = (destination - Mouth).normalized;
        if (Aim.sqrMagnitude < .01f) Aim = transform.forward;
        for (int i = 0; i < 4; i++)
        {
            Vector3 goal = targetCount > 0 ? targets[i % targetCount].LockAimPoint : destination;
            float angle = (45f + 90f * i) * Mathf.Deg2Rad;
            Vector3 circle = transform.position + transform.right * (Mathf.Cos(angle) * settings.hitRadius * 1.6f) +
                Vector3.up * (Mathf.Sin(angle) * settings.hitRadius * 1.6f);
            Origins.Set(i, pattern == RaidPattern.Thunder || pattern == RaidPattern.FireVolley ? goal : circle);
            Vector3 direction = goal - circle;
            Directions.Set(i, direction.sqrMagnitude > .001f ? direction.normalized : transform.forward);
        }
    }
    private void BeginAttack()
    {
        Stage = RaidPatternStage.Active;
        StageTimer = TickTimer.CreateFromSeconds(Runner, Mathf.Max(.1f, ActiveSeconds()));
        pulse = 0;
        if (Pattern == RaidPattern.Summon) SummonMinions();
        if (Pattern == RaidPattern.Thunder)
        {
            for (int i = 0; i < WarningCount; i++) DamageSphere(Origins[i], ThunderRadius, settings.thunderDamage, MagicType.Thunder);
            StartThunderHazards();
        }
    }

    private void FireBarrage()
    {
        for (int i = 0; i < targetCount; i++)
        {
            if (!CanHit(targets[i])) continue;
            for (int shot = 0; shot < Mathf.Clamp(settings.barrageShotsPerTarget, 1, 16); shot++)
            {
                float angle = barrageOrigin++ * 137.5f * Mathf.Deg2Rad;
                Vector3 origin = transform.position + (transform.right * Mathf.Cos(angle) + Vector3.up * Mathf.Sin(angle)) * settings.hitRadius * 1.3f;
                LaunchFire(origin, targets[i].LockAimPoint);
            }
        }
    }

    private void LaunchFire(Vector3 origin, Vector3 destination, bool wallShot = false, float damage = -1f)
    {
        Vector3 direction = destination - origin;
        if (direction.sqrMagnitude < .001f || !projectilePrefab.IsValid) return;
        int limit = wallShot ? shots.Length : shots.Length - 64;
        for (int i = 0; i < limit; i++)
        {
            var projectile = shots[i];
            if (projectile == null || !projectile.Available) continue;
            var stats = fireHit;
            stats.damage = damage >= 0f ? damage : settings.volleyDamage;
            stats.projectileSpeed *= Mathf.Max(.1f, settings.barrageSpeedMultiplier);
            stats.projectileRadius = Mathf.Max(stats.projectileRadius, wallShot ? WallHitRadius : settings.barrageHitRadius);
            stats.range = Mathf.Max(stats.range, direction.magnitude + 30f);
            stats.projectileLifetime = Mathf.Max(settings.volleyLifetime, stats.range / Mathf.Max(1f, stats.projectileSpeed) + .2f);
            projectile.InitializeRaidShot(this, stats, origin, direction);
            return;
        }
    }

    private void TickVisionPattern()
    {
        portalSpawnTimer -= Runner.DeltaTime;
        if (portalsLaunched < VisionPortalCount && portalSpawnTimer <= 0f)
        {
            RefreshTargets();
            if (targetCount > 0)
            {
                Player target = targets[visionTargetCursor++ % targetCount];
                float angle = portalsLaunched * 137.5f * Mathf.Deg2Rad;
                Vector3 origin = transform.position +
                    (transform.right * Mathf.Cos(angle) + Vector3.up * Mathf.Sin(angle)) * settings.hitRadius * 1.6f;
                Vector3 aimPoint = target.LockAimPoint;
                if (Random.value < .5f)
                    aimPoint += target.ParryHintVelocity * Mathf.Max(.1f, settings.visionWarning);
                Vector3 aim = aimPoint - origin;
                VisionPortals.Set(portalsLaunched++, new RaidVisionPortal
                {
                    Origin = origin,
                    Direction = aim.sqrMagnitude > .001f ? aim.normalized : transform.forward,
                    State = 1,
                    Timer = TickTimer.CreateFromSeconds(Runner, Mathf.Max(.1f, settings.visionWarning))
                });
                portalSpawnTimer = Mathf.Max(.05f, settings.visionSpawnInterval);
            }
        }
        bool pending = portalsLaunched < VisionPortalCount;
        for (int i = 0; i < portalsLaunched; i++)
        {
            var portal = VisionPortals[i];
            if (portal.State == 0) continue;
            if (portal.Timer.Expired(Runner))
            {
                if (portal.State == 1)
                {
                    FireVision(portal.Origin, portal.Direction);
                    portal.State = 2;
                    portal.Timer = TickTimer.CreateFromSeconds(Runner, .15f);
                }
                else portal.State = 0;
                VisionPortals.Set(i, portal);
            }
            pending |= portal.State != 0;
        }
        if (!pending)
        {
            Stage = RaidPatternStage.Recovery;
            StageTimer = TickTimer.CreateFromSeconds(Runner, settings.recoverySeconds);
        }
    }

    private void FireVision(Vector3 origin, Vector3 direction)
    {
        Vector3 end = origin + direction * visionHit.range;
        if (Runner.GetPhysicsScene().SphereCast(origin, VisionRadius, direction, out RaycastHit hit,
            visionHit.range, ~0, QueryTriggerInteraction.Ignore))
        {
            end = hit.point;
            var victim = hit.collider.GetComponentInParent<Player>();
            if (CanHit(victim))
            {
                var attack = visionHit;
                attack.damage *= settings.spellDamageMultiplier;
                victim.ReceiveMagicHit(attack, PlayerRef.None, Object.Id, direction);
            }
        }
        RPC_PresentVision(origin, end, direction);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_PresentVision(Vector3 origin, Vector3 end, Vector3 direction)
    {
        presentation.PresentVision(visionHit, origin, end, Quaternion.LookRotation(direction));
    }

    private Vector3 ClampFlightPosition(Vector3 point)
    {
        var boundary = settings.boundary;
        float inset = Mathf.Max(settings.flightBoundaryInset, settings.hitRadius);
        float xInset = Mathf.Min(inset, (boundary.xmax - boundary.xmin) * .45f);
        float zInset = Mathf.Min(inset, (boundary.zmax - boundary.zmin) * .45f);
        point.x = Mathf.Clamp(point.x, boundary.xmin + xInset, boundary.xmax - xInset);
        point.z = Mathf.Clamp(point.z, boundary.zmin + zInset, boundary.zmax - zInset);
        if (boundary.lowerLimitEnabled && boundary.HasValidLowerLimit) point.y = Mathf.Max(point.y, boundary.ymin + settings.height * .5f + 5f);
        if (boundary.altitudeLimitEnabled && boundary.HasValidAltitudeLimit) point.y = Mathf.Min(point.y, boundary.altitudeFadeStartY - settings.height * .5f);
        return point;
    }

    private void MoveAwayFromPlayers()
    {
        if (settings.boundary == null || !settings.boundary.HasValidBounds || targetCount == 0) return;
        retreatTimer -= Runner.DeltaTime;
        if (retreatTimer <= 0f || (retreatDestination - transform.position).sqrMagnitude < 4f)
        {
            retreatTimer = .4f;
            Vector3 center = Vector3.zero;
            int livePlayers = 0;
            for (int i = 0; i < targetCount; i++)
            {
                if (!CanHit(targets[i])) continue;
                center += targets[i].transform.position;
                livePlayers++;
            }
            if (livePlayers == 0) return;
            center /= livePlayers;
            Vector3 away = Vector3.ProjectOnPlane(transform.position - center, Vector3.up).normalized;
            if (away.sqrMagnitude < .01f) away = transform.forward;
            float best = float.NegativeInfinity;
            for (int i = 0; i < 16; i++)
            {
                Vector3 candidate = ClampFlightPosition(transform.position + Quaternion.AngleAxis(i * 22.5f, Vector3.up) * away * 45f);
                Vector3 delta = candidate - transform.position;
                if (delta.sqrMagnitude < 4f) continue;
                float nearest = float.PositiveInfinity;
                for (int player = 0; player < targetCount; player++)
                    if (CanHit(targets[player])) nearest = Mathf.Min(nearest, Vector3.Distance(candidate, targets[player].transform.position));
                float score = nearest + delta.magnitude * .4f + Vector3.Dot(delta.normalized, transform.forward) * 8f;
                if (score <= best) continue;
                best = score;
                retreatDestination = candidate;
            }
        }
        Vector3 movement = retreatDestination - transform.position;
        if (movement.sqrMagnitude < .01f) return;
        transform.position = ClampFlightPosition(Vector3.MoveTowards(transform.position, retreatDestination, settings.retreatSpeed * Runner.DeltaTime));
        transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(movement), settings.retreatTurnSpeed * Runner.DeltaTime);
    }

    private MagicStatEntry Hit(float damage, MagicType magic, float stun = 0)
    {
        var hit = magic == MagicType.Fire ? fireHit : magic == MagicType.Thunder ? thunderHit : visionHit;
        hit.damage = damage * settings.spellDamageMultiplier; hit.hitStunSeconds = stun; hit.knockbackForce = 0;
        hit.effect = MagicEffectKind.DirectDamage;
        return hit;
    }
    private void DamageSphere(Vector3 center, float radius, float damage, MagicType magic)
    {
        for (int i = 0; i < targetCount; i++)
        {
            var target = targets[i];
            if (!CanHit(target) || (target.LockAimPoint - center).sqrMagnitude > radius * radius ||
                !MagicProjectile.HasSummonSight(center, target.LockAimPoint)) continue;
            target.ReceiveMagicHit(Hit(damage, magic, .2f), PlayerRef.None, Object.Id, target.LockAimPoint - center);
        }
    }
    private bool CanHit(Player target) => target != null && target.Object != null && target.Object.IsValid && target.IsAlive && target.Runner == Runner;
    private void Burst()
    {
        DetonationRadius = AbsorbBlastRadius;
        DetonationPoint = transform.position;
        DamageSphere(DetonationPoint, DetonationRadius,
            Mathf.Min(settings.blastDamageLimit, settings.blastBaseDamage + AbsorbedDamage * settings.absorbedDamageMultiplier), MagicType.Vision);
    }
    private void Breathe(float dt)
    {
        Vector3 start = Mouth, end = start + BreathDirection * settings.breathLength;
        var hit = Hit(settings.breathDamagePerSecond * dt, MagicType.Fire);
        for (int i = 0; i < targetCount; i++)
        {
            var target = targets[i];
            if (!CanHit(target) || DistanceToSegment(target.LockAimPoint, start, end) > settings.breathRadius ||
                !MagicProjectile.HasSummonSight(start, target.LockAimPoint)) continue;
            if (target is RaidPlayer raidPlayer) raidPlayer.ReceiveRaidBreath(hit, Object.Id, BreathDirection, settings.burnSeconds,
                settings.burnDamagePerSecond * settings.spellDamageMultiplier);
        }
    }
    private static float DistanceToSegment(Vector3 point, Vector3 start, Vector3 end)
    {
        Vector3 segment = end - start;
        float t = Mathf.Clamp01(Vector3.Dot(point - start, segment) / Mathf.Max(.001f, segment.sqrMagnitude));
        return Vector3.Distance(point, start + segment * t);
    }
    public bool ReceiveHit(MagicStatEntry hit, NetworkId attacker)
    {
        if (!Object.HasStateAuthority || !IsTargetable || float.IsNaN(hit.damage) || float.IsInfinity(hit.damage) || hit.damage <= 0) return false;
        float before = Health;
        if (Absorbing) AbsorbedDamage += hit.damage;
        else Health = Mathf.Max(0, Health - hit.damage * (Exposed ? settings.exposureDamageMultiplier : 1f));
        RecordDealt(attacker, before - Health, Health <= 0f, hit.magic);
        if (Runner.TryFindObject(attacker, out var source) && source != null) source.GetComponent<Player>()?.ConfirmSummonHit();
        if (Health <= 0)
            EndRaid();
        return true;
    }
    private void EndRaid()
    {
        CaptureRaidAwards();
        foreach(var data in PlayerData.PerkPlayers)
            if(data!=null && data.Runner==Runner) data.CompleteMissions(Health<=0);
        Phase = BattleStartPhase.Ended;
        WallState = 0;
        for (int i = 0; i < shots.Length; i++)
            if (shots[i] != null && shots[i].Object != null && shots[i].Object.IsValid && shots[i].Object.Id == shotIds[i])
                Runner.Despawn(shots[i].Object);
    }
    public bool TraceBody(Vector3 origin, Vector3 direction, float attackRadius, float limit, out float distance)
    {
        distance = limit;
        bool hit = false;
        for (int i = -1; i <= 1; i++)
        {
            Vector3 center = transform.position + Vector3.up * (i * settings.height * .25f);
            if (!MagicProjectile.TrySphereContact(origin, direction, center, settings.hitRadius + attackRadius, distance, out float at)) continue;
            hit = true; distance = at;
        }
        return hit;
    }
    public float DistanceToBody(Vector3 point)
    {
        float distance = float.PositiveInfinity;
        for (int i = -1; i <= 1; i++)
            distance = Mathf.Min(distance, Vector3.Distance(point, transform.position + Vector3.up * (i * settings.height * .25f)));
        return Mathf.Max(0, distance - settings.hitRadius);
    }
    public void PresentFireImpact(Vector3 point) => presentation.PresentFireImpact(point);
    public override void Render() => presentation.Present(this);
}
