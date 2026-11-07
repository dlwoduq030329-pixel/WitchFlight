using Fusion;
using UnityEngine;

public sealed partial class GiantMeowBoss
{
    public const int CoreCount = 4;
    public const int WallColumns = 9, WallRows = 5, WallGapColumns = 1;
    public float WallHitRadius => Mathf.Max(settings.barrageHitRadius, settings.wallHitRadius);
    [SerializeField] private NetworkPrefabRef corePrefab;
    [Networked, Capacity(CoreCount)] public NetworkArray<float> CoreHealth => default;
    [Networked, Capacity(CoreCount)] public NetworkArray<Vector3> CorePositions => default;
    [Networked] public TickTimer ExposureTimer { get; private set; }
    [Networked] public float DetonationRadius { get; private set; }
    [Networked] public Vector3 DetonationPoint { get; private set; }
    [Networked] public int WallState { get; private set; }
    [Networked] public Vector3 WallOrigin { get; private set; }
    [Networked] public Vector3 WallDirection { get; private set; }
    [Networked] public int WallGapColumn { get; private set; }
    [Networked] public TickTimer WallTimer { get; private set; }
    [Networked, Capacity(4)] public NetworkArray<Vector3> ThunderHazards => default;
    [Networked] public int HazardCount { get; private set; }
    [Networked] public TickTimer HazardTimer { get; private set; }
    private readonly NetworkObject[] coreObjects = new NetworkObject[CoreCount];
    private int wallWave, wallTarget;
    private float hazardPulse;
    public bool Exposed => IsTargetable && !ExposureTimer.ExpiredOrNotRunning(Runner);
    public bool HazardsActive => IsTargetable && !HazardTimer.ExpiredOrNotRunning(Runner);
    public int RemainingCores
    {
        get
        {
            int count = 0;
            for (int i = 0; i < CoreCount; i++) if (CoreHealth[i] > 0f) count++;
            return count;
        }
    }
    public float AbsorbBlastRadius => Mathf.Lerp(settings.minimumBlastRadius, settings.blastRadius, RemainingCores / (float)CoreCount);

    private void SpawnCores()
    {
        if (!Object.HasStateAuthority || !corePrefab.IsValid) return;
        for (int i = 0; i < CoreCount; i++)
        {
            int slot = i;
            coreObjects[i] = Runner.Spawn(corePrefab, transform.position, Quaternion.identity, PlayerRef.None,
                onBeforeSpawned: (r, obj) => obj.GetComponent<RaidCoreTarget>().Configure(this, slot));
        }
    }

    private void PrepareCores()
    {
        ExposureTimer = TickTimer.None;
        DetonationRadius = 0f;
        Vector3 forward = targetCount > 0 ? (targets[0].LockAimPoint - transform.position).normalized : transform.forward;
        Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
        if (right.sqrMagnitude < .01f) right = transform.right;
        Vector3 up = Vector3.Cross(forward, right).normalized;
        for (int i = 0; i < CoreCount; i++)
        {
            float angle = (45 + i * 90) * Mathf.Deg2Rad;
            CorePositions.Set(i, transform.position + forward * (settings.hitRadius + 12f) +
                (right * Mathf.Cos(angle) + up * Mathf.Sin(angle)) * settings.coreOrbitRadius);
            CoreHealth.Set(i, Mathf.Max(1f, settings.coreHealth));
        }
    }

    public bool DamageCore(int index, MagicStatEntry attack, NetworkId attacker)
    {
        if (!Object.HasStateAuthority || !Absorbing || index < 0 || index >= CoreCount || CoreHealth[index] <= 0f ||
            float.IsNaN(attack.damage) || float.IsInfinity(attack.damage) || attack.damage <= 0f) return false;
        float before = CoreHealth[index];
        CoreHealth.Set(index, Mathf.Max(0f, before - attack.damage));
        RecordDealt(attacker, before - CoreHealth[index], false, attack.magic);
        if (Runner.TryFindObject(attacker, out var source) && source != null) source.GetComponent<Player>()?.ConfirmSummonHit();
        if (RemainingCores == 0)
        {
            ExposureTimer = TickTimer.CreateFromSeconds(Runner, settings.exposureSeconds);
            Stage = RaidPatternStage.Recovery;
            StageTimer = ExposureTimer;
            WallState = 0;
        }
        return true;
    }

    private void TickWall()
    {
        bool enabled = (Pattern == RaidPattern.FireVolley || Pattern == RaidPattern.VisionCorridors) &&
            Stage == RaidPatternStage.Active;
        if (!enabled || Exposed) { WallState = 0; return; }
        if (WallState == 0)
        {
            if (targetCount == 0) return;
            var target = targets[wallTarget++ % targetCount];
            if (!CanHit(target)) return;
            WallOrigin = transform.position;
            Vector3 aim = target.LockAimPoint + target.ParryHintVelocity * settings.wallWarningSeconds - WallOrigin;
            WallDirection = aim.sqrMagnitude > .001f ? aim.normalized : transform.forward;
            WallGapColumn = 2 + (wallWave++ % 3) * 2;
            WallTimer = TickTimer.CreateFromSeconds(Runner, settings.wallWarningSeconds);
            WallState = 1;
        }
        else if (WallTimer.Expired(Runner))
        {
            if (WallState == 1)
            {
                int available = 0;
                foreach (var shot in shots) if (shot != null && shot.Available) available++;
                if (available < (WallColumns - WallGapColumns) * WallRows)
                {
                    WallTimer = TickTimer.CreateFromSeconds(Runner, .1f);
                    return;
                }
                Quaternion rotation = Quaternion.LookRotation(WallDirection);
                for (int column = 0; column < WallColumns; column++)
                {
                    if (column >= WallGapColumn && column < WallGapColumn + WallGapColumns) continue;
                    for (int row = 0; row < WallRows; row++)
                    {
                        Vector3 origin = WallOrigin + rotation * new Vector3((column - 4) * settings.wallSpacing,
                            (row - 2) * settings.wallSpacing, 0);
                        LaunchFire(origin, origin + WallDirection * settings.wallRange, true);
                    }
                }
                WallState = 2;
                WallTimer = TickTimer.CreateFromSeconds(Runner, settings.wallInterval);
            }
            else WallState = 0;
        }
    }

    private void StartThunderHazards()
    {
        HazardCount = WarningCount;
        for (int i = 0; i < HazardCount; i++) ThunderHazards.Set(i, Origins[i]);
        HazardTimer = TickTimer.CreateFromSeconds(Runner, settings.thunderHazardSeconds);
        hazardPulse = settings.thunderHazardTick;
    }

    private void TickThunderHazards()
    {
        if (!HazardsActive) return;
        hazardPulse -= Runner.DeltaTime;
        if (hazardPulse > 0f) return;
        hazardPulse = Mathf.Max(.1f, settings.thunderHazardTick);
        // Overlapping residue areas count as one tick per player.
        for (int i = 0; i < targetCount; i++)
        {
            var target = targets[i];
            if (!CanHit(target)) continue;
            for (int hazard = 0; hazard < HazardCount; hazard++)
            {
                Vector3 center = ThunderHazards[hazard];
                if ((target.LockAimPoint - center).sqrMagnitude > settings.thunderHazardRadius * settings.thunderHazardRadius ||
                    !MagicProjectile.HasSummonSight(center, target.LockAimPoint)) continue;
                target.ReceiveMagicHit(Hit(settings.thunderHazardDamage, MagicType.Thunder), PlayerRef.None, Object.Id, target.LockAimPoint - center);
                break;
            }
        }
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        if (!hasState || !runner.IsServer) return;
        foreach (var core in coreObjects) if (core != null && core.IsValid) runner.Despawn(core);
        foreach (var minion in minions) if (minion != null && minion.Object != null && minion.Object.IsValid) runner.Despawn(minion.Object);
        foreach (var potion in potions) if (potion != null && potion.Object != null && potion.Object.IsValid) runner.Despawn(potion.Object);
        foreach (var shot in shots) if (shot != null && shot.Object != null && shot.Object.IsValid) runner.Despawn(shot.Object);
    }
}
