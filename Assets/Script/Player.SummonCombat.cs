using Fusion;
using UnityEngine;

public partial class Player
{
    public bool TryGetMagicTarget(NetworkId id, out Player player, out MagicProjectile summon, out Vector3 point)
    {
        player = null; summon = null; point = default;
        if (Runner == null || !Runner.TryFindObject(id, out NetworkObject obj) || obj == null || !obj.IsValid) return false;
        player = obj.GetComponent<Player>();
        if (player != null)
        {
            point = player.LockAimPoint;
            return IsValidLockTarget(player);
        }
        if (SelectedMagicStats.TargetsAllies) return false; // Healing never selects enemy summon bodies.
        summon = obj.GetComponent<MagicProjectile>();
        if (summon == null || !summon.IsSummonEnemyOf(this)) return false;
        point = summon.transform.position;
        float range = SelectedMagicStats.range > 0f ? Mathf.Min(maxLockDistance, SelectedMagicStats.range) : maxLockDistance;
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
        RPC_ConfirmedMagicHit(false);
    }

    private void TrackPerkSummonBeam(MagicProjectile target, float time, MagicStatEntry stats)
    {
        if (target == null || !target.IsSummonEnemyOf(this) || stats.perkSecond?.id != PerkId.BeamFocus) return;
        if (!beamVictim.Equals(target.Object.Id) || perkClock - beamLastContact > Mathf.Max(.001f, stats.perkSecond.condition)) beamContact = 0f;
        beamVictim = target.Object.Id; beamLastContact = perkClock; beamContact += time;
    }

    private float SummonChannelDamage(MagicStatEntry stats, NetworkId targetId, float time)
    {
        float damage = Mathf.Max(0f, stats.damagePerSecond) * time;
        if (stats.perkSecond?.id == PerkId.BeamFocus && beamVictim.Equals(targetId) && beamContact >= stats.perkSecond.seconds)
            damage *= 1f + Mathf.Max(0f, stats.perkSecond.amount);
        return damage;
    }

    private Vector3 TraceMagicTargets(MagicStatEntry stats, Vector3 origin, Vector3 direction, out Player victim, out MagicProjectile summon)
    {
        Vector3 end = TraceMagic(stats, origin, direction, out victim);
        float distance = Vector3.Distance(origin, end);
        if (MagicProjectile.TryTraceSummon(Runner, TeamIndex, origin, direction, distance, stats.hitscanRadius, out summon, out float hit))
        {
            victim = null;
            return origin + direction.normalized * hit;
        }
        return end;
    }
}
