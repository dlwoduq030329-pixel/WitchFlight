using System.Collections.Generic;
using Fusion;
using UnityEngine;

// One pooled field follows the emitter. A bounded authority-only history retains
// the emitted world-space trails; no per-particle physics or per-frame RPCs.
public partial class MagicProjectile
{
    private static readonly HashSet<MagicProjectile> flareFields = new();
    [Networked] private float FlareSpeed { get; set; }
    [Networked] private float FlareParticleLifetime { get; set; }
    [Networked] private bool FlareEmissionStopped { get; set; }
    private Player flareCaster;
    private const int FlareHistoryCapacity = 128;
    private struct FlareSample { public Vector3 position; public Quaternion rotation; public float at; public int portal; }
    private struct FlareSegment { public Vector3 start, end, origin; public float radius; }
    private FlareSample[] flareHistory;
    private FlareSegment[] flareSegments;
    private int flareHistoryHead, flareHistoryCount, flareSegmentCount;
    private float nextFlareSample;
    private Bounds flareBounds;
    [Networked] private float FlareSpread { get; set; }
    [Networked] private float FlareAttractionRadius { get; set; }
    [Networked] private float FlareDuration { get; set; }
    private FlareFieldVisual flareVisual;

    public void InitializeFlareField(Player owner, MagicStatEntry entry)
    {
        Initialize(owner, null, entry, -owner.transform.forward);
        ConstructKind = MagicConstructKind.FlareField;
        Settled = true;
        ConstructRadius = Mathf.Max(.1f, entry.flareInterceptRadius);
        FlareSpeed = Mathf.Max(0f, entry.flareVfxSpeed);
        FlareParticleLifetime = 1.1f + Mathf.Clamp(entry.flareParticleExtraLifetime, 0f, 5f);
        FlareEmissionStopped = false;
        flareCaster = owner;
        flareHistory ??= new FlareSample[FlareHistoryCapacity];
        flareSegments ??= new FlareSegment[FlareHistoryCapacity * 3];
        flareHistoryHead = flareHistoryCount = flareSegmentCount = 0;
        nextFlareSample = 0f;
        FlareSpread = Mathf.Clamp(entry.flareSpreadAngle, 0f, 80f);
        FlareAttractionRadius = entry.perkFirst?.id == PerkId.FlarePulse ? Mathf.Max(0f, entry.perkFirst.amount) : 0f;
        FlareDuration = Mathf.Max(.1f, entry.effectDuration);
        Lifetime = TickTimer.CreateFromSeconds(Runner, FlareDuration + FlareParticleLifetime);
        flareFields.Add(this);
    }

    private bool IsLiveFlare(NetworkRunner runner) => ConstructKind == MagicConstructKind.FlareField &&
        Runner == runner && Object != null && Object.IsValid && !Finished && !Lifetime.ExpiredOrNotRunning(Runner);

    private float FlareElapsed => FlareDuration + FlareParticleLifetime - (Lifetime.RemainingTime(Runner) ?? 0f);
    private bool FlareEmitting => !FlareEmissionStopped && FlareElapsed < FlareDuration;

    private void FollowFlareCaster()
    {
        if (flareCaster == null && Runner.TryFindObject(ShooterId, out var obj) && obj != null)
            flareCaster = obj.GetComponent<Player>();
        if (flareCaster == null || flareCaster.Object == null || !flareCaster.Object.IsValid || !flareCaster.IsAlive) return;
        Vector3 rear = -flareCaster.transform.forward;
        transform.SetPositionAndRotation(flareCaster.LockAimPoint + rear * Mathf.Max(0f, PresentationStats.flareVfxBackOffset),
            Quaternion.LookRotation(rear));
    }
    private void TickFlareField()
    {
        if (Lifetime.Expired(Runner)) { Runner.Despawn(Object); return; }
        float now = FlareElapsed;
        if (FlareEmitting)
        {
            FollowFlareCaster();
            if (flareCaster == null || flareCaster.Object == null || !flareCaster.Object.IsValid || !flareCaster.IsAlive)
                FlareEmissionStopped = true;
            else if (now >= nextFlareSample)
            {
                flareHistory[flareHistoryHead] = new FlareSample {
                    position = transform.position, rotation = transform.rotation, at = now, portal = flareCaster.PortalExitSequence };
                flareHistoryHead = (flareHistoryHead + 1) % FlareHistoryCapacity;
                flareHistoryCount = Mathf.Min(flareHistoryCount + 1, FlareHistoryCapacity);
                nextFlareSample = now + .05f;
            }
        }
        // Cache the swept ribbons once per field tick, not once per incoming projectile.
        flareSegmentCount = 0;
        for (int branch = 0; branch < 3; branch++)
        {
            bool havePrevious = false;
            Vector3 previous = default;
            int previousPortal = 0;
            for (int i = 0; i < flareHistoryCount; i++)
            {
                var sample = flareHistory[(flareHistoryHead - 1 - i + FlareHistoryCapacity) % FlareHistoryCapacity];
                float age = now - sample.at;
                if (age > FlareParticleLifetime) break;
                Vector3 heading = sample.rotation * Quaternion.Euler(0f, (branch - 1) * FlareSpread, 0f) * Vector3.forward;
                Vector3 point = FlareTrailPoint(sample.position, heading, FlareSpeed, age);
                float radius = ConstructRadius + FlareSpeed * age * .12f; // seven-degree core cone
                Vector3 start = havePrevious && previousPortal == sample.portal ? previous : point;
                flareSegments[flareSegmentCount++] = new FlareSegment { start=start, end=point, origin=sample.position, radius=radius };
                if (flareSegmentCount == 1) flareBounds = new Bounds(point, Vector3.zero);
                flareBounds.Encapsulate(start + Vector3.one * radius); flareBounds.Encapsulate(start - Vector3.one * radius);
                flareBounds.Encapsulate(point + Vector3.one * radius); flareBounds.Encapsulate(point - Vector3.one * radius);
                previous=point; previousPortal=sample.portal; havePrevious=true;
            }
        }
    }
    internal static Vector3 FlareTrailPoint(Vector3 origin, Vector3 heading, float speed, float age)
        => origin + heading * (Mathf.Max(0f, speed) * .825f * Mathf.Max(0f, age));

    private void ApplyFlareAttraction()
    {
        if (IsConstruct || IsMine || Finished || FlightExhausted) return;
        MagicProjectile nearest = null;
        Player recipient = null;
        float closest = float.PositiveInfinity;
        foreach (var field in flareFields)
        {
            if (field == null || !field.IsLiveFlare(Runner) || field.ShooterTeam == ShooterTeam || field.FlareAttractionRadius <= 0f) continue;
            if (!Runner.TryFindObject(field.ShooterId, out var obj) || obj == null || !obj.TryGetComponent(out Player owner) || !owner.IsAlive) continue;
            float sqr = (owner.LockAimPoint - transform.position).sqrMagnitude;
            if (sqr > field.FlareAttractionRadius * field.FlareAttractionRadius || sqr >= closest) continue;
            nearest = field; recipient = owner; closest = sqr;
        }
        if (nearest == null || recipient == null || TargetId == recipient.Object.Id) return;
        if (!HasSummonSight(transform.position, recipient.LockAimPoint)) return;
        // Ownership/team/damage remain the enemy's. This also redirects shots aimed at allies.
        TargetId = recipient.Object.Id;
        stats.requiresTarget = true;
        stats.projectileTurnSpeed = Mathf.Max(stats.projectileTurnSpeed, 360f);
        sweptTarget = null;
    }

    private bool InterceptedByFlare(Vector3 start, Vector3 end)
    {
        if (IsConstruct || IsMine || Finished) return false;
        MagicProjectile nearest = null;
        Vector3 contact = end;
        float closest = float.PositiveInfinity;
        foreach (var field in flareFields)
        {
            if (field == null || !field.IsLiveFlare(Runner) || field.ShooterTeam == ShooterTeam) continue;
            if (field.flareSegmentCount == 0) continue;
            var sweepBounds = new Bounds(start, Vector3.zero); sweepBounds.Encapsulate(end);
            sweepBounds.Expand(Mathf.Max(0f, stats.projectileRadius) * 2f);
            if (!field.flareBounds.Intersects(sweepBounds)) continue;
            for (int i = 0; i < field.flareSegmentCount; i++)
            {
                var segment = field.flareSegments[i];
                float radius = segment.radius + Mathf.Max(0f, stats.projectileRadius);
                float sqr = BattleFlag.SegmentDistanceSquared(start, end, segment.start, segment.end, out Vector3 point);
                if (sqr > radius * radius || !HasSummonSight(segment.origin, point)) continue;
                float along = (point - start).sqrMagnitude;
                if (along >= closest) continue;
                nearest = field; contact = point; closest = along;
            }
        }
        if (nearest == null) return false;
        Finished = true;
        TargetId = default;
        transform.position = contact;
        RPC_Impact(contact, Magic); // Explosion only: never call Impact's area-damage path.
        Lifetime = TickTimer.CreateFromSeconds(Runner, .1f);
        if (PlayerData.FindPerkData(Runner, Shooter) != null)
            PlayerData.FindPerkData(Runner, nearest.Shooter)?.AwardPerkFlare();
        return true;
    }

    private void RenderFlareField()
    {
        if (Finished || Lifetime.ExpiredOrNotRunning(Runner)) { if (flareVisual != null) flareVisual.gameObject.SetActive(false); return; }
        if (FlareEmitting) FollowFlareCaster();
        if (flareVisual == null) flareVisual = FlareFieldVisual.Create(transform, PresentationStats);
        if (!flareVisual.gameObject.activeSelf) flareVisual.gameObject.SetActive(true);
        flareVisual.SetEmitting(FlareEmitting);
    }

    private void ClearFlareField()
    {
        flareFields.Remove(this);
        flareCaster = null;
        flareHistoryHead = flareHistoryCount = flareSegmentCount = 0;
        if (flareVisual != null) flareVisual.gameObject.SetActive(false);
    }
}
