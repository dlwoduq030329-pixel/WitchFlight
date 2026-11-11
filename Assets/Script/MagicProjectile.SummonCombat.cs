using System.Collections.Generic;
using Fusion;
using UnityEngine;

public partial class MagicProjectile
{
    private float summonBurnRemaining, summonBurnDps, summonBurnTick;
    private MagicStatEntry summonBurnStats;
    private int summonBurnTeam;
    private NetworkId summonBurnAttacker;

    private void TickSummonBurn()
    {
        if (ConstructKind != MagicConstructKind.Summon || summonBurnRemaining <= 0f || Finished) return;
        float dt = Mathf.Min(Runner.DeltaTime, summonBurnRemaining);
        summonBurnRemaining -= dt; summonBurnTick += dt;
        if (summonBurnTick < .5f && summonBurnRemaining > 0f) return;
        var tick = summonBurnStats; tick.damage = summonBurnDps * summonBurnTick;
        summonBurnTick = 0f;
        ReceiveSummonHit(tick, summonBurnTeam, summonBurnAttacker);
    }

    [Networked] public float SummonDamageTaken { get; private set; }
    public virtual float SummonMaxHealth => PresentationStats.summonChannelHealth > 0 ? PresentationStats.summonChannelHealth : 50f;
    public virtual float SummonHealthFraction => IsLiveSummon ? Mathf.Clamp01(1f - SummonDamageTaken / SummonMaxHealth) : 0f;
    public static IEnumerable<MagicProjectile> ActiveProjectiles => active;
    public virtual bool IsLiveSummon => Object != null && Object.IsValid &&
        ConstructKind == MagicConstructKind.Summon && !Finished;
    public virtual float SummonHitRadius => Mathf.Max(.05f,
        PresentationStats.summonHitRadius > 0f ? PresentationStats.summonHitRadius : .65f);

    public bool IsSummonEnemyOf(Player observer) => observer != null && observer.Object != null &&
        observer.Object.IsValid && observer.IsAlive && Runner == observer.Runner && IsLiveSummon &&
        !ShooterId.Equals(observer.Object.Id) && ShooterTeam != observer.TeamIndex;

    private MagicProjectile FindSummonTargetById()
    {
        if (!Runner.TryFindObject(TargetId, out NetworkObject obj) || obj == null) return null;
        var target = obj.GetComponent<MagicProjectile>();
        return target != null && target.IsLiveSummon && target.Runner == Runner && target.ShooterTeam != ShooterTeam ? target : null;
    }

    // A virtual hit sphere needs no runtime collider or additional NetworkBehaviour on the prefab.
    // The caller bounds this sweep by its nearest physical wall/player hit.
    public static bool TryTraceSummon(NetworkRunner runner, int attackerTeam, Vector3 origin, Vector3 direction,
        float maxDistance, float attackRadius, out MagicProjectile target, out float distance)
    {
        target = null; distance = Mathf.Max(0f, maxDistance);
        if (direction.sqrMagnitude < .00001f) return false;
        direction.Normalize();
        foreach (var candidate in active)
        {
            if (candidate == null || !candidate.IsLiveSummon || candidate.Runner != runner || candidate.ShooterTeam == attackerTeam) continue;
            if (!candidate.TraceBody(origin, direction, Mathf.Max(0f, attackRadius), distance, out float contact)) continue;
            if (target != null && contact >= distance) continue;
            target = candidate; distance = contact;
        }
        return target != null;
    }

    public virtual bool TraceBody(Vector3 origin, Vector3 direction, float attackRadius, float limit, out float distance) =>
        TrySphereContact(origin, direction, transform.position, SummonHitRadius + attackRadius, limit, out distance);

    public virtual float DistanceToBody(Vector3 point) => Vector3.Distance(point, transform.position);
    public virtual float AreaContactRadius(float radius) => radius + SummonHitRadius;

    public static bool TrySphereContact(Vector3 origin, Vector3 direction, Vector3 center, float radius, float limit, out float distance)
    {
        Vector3 offset = origin - center;
        float c = offset.sqrMagnitude - radius * radius;
        distance = 0f;
        if (c <= 0f) return true;
        float b = Vector3.Dot(offset, direction);
        float discriminant = b * b - c;
        if (b >= 0f || discriminant < 0f) return false;
        distance = -b - Mathf.Sqrt(discriminant);
        return distance >= 0f && distance <= limit;
    }

    // Visibility ignores character/projectile bodies but never ignores map colliders.
    public static bool HasSummonSight(Vector3 origin, Vector3 destination)
    {
        int count = Physics.OverlapSphereNonAlloc(origin, .01f, blastOverlaps, ~0, QueryTriggerInteraction.Ignore);
        Collider[] overlaps = count == blastOverlaps.Length
            ? Physics.OverlapSphere(origin, .01f, ~0, QueryTriggerInteraction.Ignore) : blastOverlaps;
        if (overlaps != blastOverlaps) count = overlaps.Length;
        for (int i = 0; i < count; i++)
            if (IsSummonSightObstacle(overlaps[i])) return false;
        Vector3 delta = destination - origin;
        count = Physics.RaycastNonAlloc(origin, delta.normalized, blastHits, delta.magnitude, ~0, QueryTriggerInteraction.Ignore);
        RaycastHit[] hits = count == blastHits.Length
            ? Physics.RaycastAll(origin, delta.normalized, delta.magnitude, ~0, QueryTriggerInteraction.Ignore) : blastHits;
        if (hits != blastHits) count = hits.Length;
        for (int i = 0; i < count; i++)
            if (IsSummonSightObstacle(hits[i].collider)) return false;
        return true;
    }

    private static bool IsSummonSightObstacle(Collider collider) => collider != null &&
        collider.GetComponentInParent<Player>() == null && collider.GetComponentInParent<MagicProjectile>() == null;

    public virtual bool ReceiveSummonHit(MagicStatEntry attack, int attackerTeam, NetworkId attackerId)
    {
        if (!IsLiveSummon || !Object.HasStateAuthority || ShooterTeam == attackerTeam || ShooterId.Equals(attackerId) ||
            BattleManager.Instance == null || !BattleManager.Instance.IsGameplayActive) return false;
        bool sustained = attack.magic == MagicType.Curse || attack.magic == MagicType.Razier;
        if (sustained)
        {
            if (attack.damage <= 0f || float.IsNaN(attack.damage) || float.IsInfinity(attack.damage)) return false;
            SummonDamageTaken += attack.damage;
        }
        else if (attack.damage <= 0f && attack.effect != MagicEffectKind.Binding) return false;

        // Do not grant player kills, combat XP, lifesteal or on-player-hit perks for a familiar.
        if (Runner.TryFindObject(attackerId, out NetworkObject attacker) && attacker != null)
        {
            var source = attacker.GetComponent<Player>();
            source?.ConfirmSummonHit();
            var burn = source != null ? source.TrackAcceptedSummonBeam(this, attack) : null;
            if (burn != null && burn.seconds > 0f)
            {
                if (summonBurnRemaining <= 0f) summonBurnTick = 0f;
                summonBurnRemaining = burn.seconds; summonBurnDps = burn.amount / burn.seconds;
                summonBurnTeam = attackerTeam; summonBurnAttacker = attackerId;
                summonBurnStats = attack; summonBurnStats.perkFirst = summonBurnStats.perkSecond = null;
            }
        }
        float threshold = stats.summonChannelHealth > 0f ? stats.summonChannelHealth : 50f;
        bool killed=!sustained || SummonDamageTaken + .0001f >= threshold;
        PlayerData.FindPerkData(Runner,attack.perkAttacker)?.RecordTitleSummonHit(attack.titleCastId,killed);
        if (killed)
        {
            Finished = true;
            SummonLockProgress = 0f; TargetId = default;
            RPC_Impact(transform.position, Magic);
            Lifetime = TickTimer.CreateFromSeconds(Runner, .2f);
        }
        return true;
    }

    public static bool DamageSummonsInArea(NetworkRunner runner, int team, Vector3 center, float radius, MagicStatEntry attack, NetworkId attacker)
    {
        bool contacted = false;
        // ReceiveSummonHit marks Finished; it does not remove from active during this enumeration.
        foreach (var candidate in active)
        {
            if (candidate == null || !candidate.IsLiveSummon || candidate.Runner != runner || candidate.ShooterTeam == team) continue;
            float distance = candidate.DistanceToBody(center);
            float hitRadius = candidate.AreaContactRadius(radius);
            if (distance <= hitRadius && HasSummonSight(center, candidate.transform.position))
            {
                contacted = true;
                candidate.ReceiveSummonHit(PerkEffects.AreaHit(attack, distance), team, attacker);
            }
        }
        return contacted;
    }
}
