using Fusion;
using UnityEngine;

public partial class Player
{
    public Vector3 SummonLaunchVelocity => (IsBound || IsHitStunned ? Vector3.zero : transform.forward * Speed) + KnockbackVelocity;
    public float SummonFlightAcceleration => stageTransitionSpeed;
    public float SummonFlightBraking => brakeSpeed;
    public float SummonFlightTurnResponse => turnAccel;

    public bool TryGetMagicTarget(NetworkId id, out Player player, out MagicProjectile summon, out Vector3 point, int inputSlot = 0)
    {
        player = null; summon = null; point = default;
        if (Object == null || !Object.IsValid || Runner == null || !Runner.TryFindObject(id, out NetworkObject obj) || obj == null || !obj.IsValid) return false;
        MagicStatEntry stats = inputSlot >= 1 && inputSlot <= 2 ? GetMagicStats(GetMagicInSlot(inputSlot)) : SelectedMagicStats;
        player = obj.GetComponent<Player>();
        if (player != null)
        {
            point = player.LockAimPoint;
            return IsValidLockTarget(player, stats);
        }
        if (stats.TargetsAllies) return false; // Healing never selects enemy summon bodies.
        summon = obj.GetComponent<MagicProjectile>();
        if (summon == null || !summon.IsSummonEnemyOf(this)) return false;
        point = summon.transform.position;
        float range = stats.range > 0f ? Mathf.Min(maxLockDistance, stats.range) : maxLockDistance;
        range *= LockRangeMultiplier;
        Vector3 delta = point - LockAimPoint;
        return delta.sqrMagnitude <= range * range && Vector3.Dot(transform.forward, delta) >= 0f &&
            MagicProjectile.HasSummonSight(MagicCastPosition, point);
    }

    public bool TryGetDisplayedLockPoint(out Vector3 point)
    {
        point = default;
        // Rendering reads the selected ID; physics validation stays in input/simulation.
        if (!CanAcquireMagicTarget() || !Runner.TryFindObject(LockTargetId, out NetworkObject obj) || obj == null || !obj.IsValid) return false;
        if (obj.TryGetComponent(out Player player) &&
            (SelectedMagicStats.TargetsAllies ? IsHealingAlly(player) : player.IsTargetableBy(this)))
        { point = player.LockAimPoint; return true; }
        if (SelectedMagicStats.TargetsAllies) return false;
        if (obj.TryGetComponent(out MagicProjectile summon) && summon.IsSummonEnemyOf(this))
        { point = summon.transform.position; return true; }
        return false;
    }

    // No extra RPC per tick: reuses the existing rate-limited confirmed-hit notification.
    public void ConfirmSummonHit()
    {
        if (Object == null || !Object.IsValid || !Object.HasStateAuthority || ParticipantRef == PlayerRef.None ||
            TimerIsActive(nextHitMarkerFeedback)) return;
        nextHitMarkerFeedback = TickTimer.CreateFromSeconds(Runner, .05f);
        RecordCatAttack(false);
        RPC_ConfirmedMagicHit(false);
    }

    private void TrackPerkSummonBeam(MagicProjectile target, float time, MagicStatEntry stats)
    {
        if (target == null || !target.IsSummonEnemyOf(this) || stats.perkSecond?.id != PerkId.BeamFocus) return;
        if (!beamVictim.Equals(target.Object.Id) || perkClock - beamLastContact > Mathf.Max(.05f, stats.channelTickSeconds + Runner.DeltaTime * 2)) beamContact = 0f;
        beamVictim = target.Object.Id; beamLastContact = perkClock; beamContact += time;
    }

    private float SummonChannelDamage(MagicStatEntry stats, NetworkId targetId, float time)
        => Mathf.Max(0f, stats.damagePerSecond) * time;

    public PerkDefinition TrackAcceptedSummonBeam(MagicProjectile target, MagicStatEntry stats)
    {
        if (stats.perkSecond?.id != PerkId.BeamFocus) return null;
        TrackPerkSummonBeam(target, stats.channelContactSeconds, stats);
        return beamVictim.Equals(target.Object.Id) && beamContact + .0001f >= stats.perkSecond.condition ? stats.perkSecond : null;
    }

    private Vector3 TraceMagicTargets(MagicStatEntry stats, Vector3 origin, Vector3 direction, out Player victim, out MagicProjectile summon)
    {
        if (stats.HasUnlimitedInstantRange) stats.range = Mathf.Infinity;
        environmentImpactHit = null;
        Vector3 end = TraceMagic(stats, origin, direction, out victim);
        float distance = tracedMagicDistance;
        if (MagicProjectile.TryTraceSummon(Runner, TeamIndex, origin, direction, distance, stats.hitscanRadius, out summon, out float hit))
        {
            victim = null;
            return origin + direction.normalized * hit;
        }
        return end;
    }
}
