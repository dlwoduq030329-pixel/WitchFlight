using UnityEngine;

// Session-owned local pool. Uses the production flight math, never a NetworkRunner.
public sealed class MagicBalanceLabShot : MonoBehaviour
{
    private MagicBalanceLabSession session;
    private MagicBalanceLabActor shooter, target;
    private MagicType magic;
    private Vector3 direction, fallVelocity;
    private float fraction, distance, age, lifetime;
    private bool falling;
    private Vector3 previousShot, previousTargetCenter;
    private TrailRenderer trail;
    private Renderer body;
    private readonly RaycastHit[] hits = new RaycastHit[16];
    public int LaunchSequence { get; private set; }

    public bool IsLiveHomingThreat(MagicBalanceLabActor observer)
        => gameObject.activeSelf && !falling && observer != null && target == observer && session != null &&
           session.Magic.GetStats(magic).requiresTarget && MagicProjectile.UsesFlightExhaustion(magic, session.Magic);

    public bool TryGetHomingThreat(MagicBalanceLabActor observer, out float arrival, out float speed,
        out float turnRate, out float initialTurnRate)
    {
        arrival = float.PositiveInfinity; speed = turnRate = initialTurnRate = 0f;
        if (!IsLiveHomingThreat(observer)) return false;
        var entry = session.Magic.GetStats(magic);
        if (!entry.requiresTarget || !MagicProjectile.UsesFlightExhaustion(magic, session.Magic)) return false;
        Vector3 delta = observer.LockAimPoint - transform.position;
        if (Vector3.Dot(direction, delta) <= 0f) return false;
        speed = entry.projectileSpeed * fraction;
        float closing = Vector3.Dot(direction * speed - observer.Velocity, delta.normalized);
        if (closing <= .01f) return false;
        arrival = delta.magnitude / closing;
        turnRate = MagicProjectile.FlightTurnSpeed(entry.projectileTurnSpeed, speed, session.Magic);
        initialTurnRate = MagicProjectile.FlightTurnSpeed(entry.projectileTurnSpeed, entry.projectileSpeed, session.Magic);
        return true;
    }

    public void Initialize(Material material)
    {
        var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        var collider = sphere.GetComponent<Collider>();
        collider.enabled = false;
        Destroy(collider);
        sphere.transform.SetParent(transform, false);
        body = sphere.GetComponent<Renderer>(); body.sharedMaterial = material;
        trail = gameObject.AddComponent<TrailRenderer>(); trail.sharedMaterial = material;
        trail.time = .22f; trail.startWidth = .24f; trail.endWidth = .01f; trail.minVertexDistance = .15f;
    }

    public void Launch(MagicBalanceLabSession lab, MagicBalanceLabActor owner, MagicBalanceLabActor victim, MagicStatEntry stats, Vector3 aim)
    {
        session = lab; shooter = owner; target = victim; magic = stats.magic;
        LaunchSequence++;
        fraction = 1; distance = age = 0; falling = false;
        lifetime = MagicProjectile.FlightLifetime(stats, lab.Magic);
        transform.position = owner.CastPoint;
        previousShot = transform.position;
        if (victim != null) victim.GetCollisionCapsule(out previousTargetCenter, out _, out _);
        direction = aim.normalized;
        if (victim != null && stats.requiresTarget)
            direction = MagicProjectile.HomingAimDelta(victim.LockAimPoint - transform.position, victim.Velocity,
                stats.projectileSpeed, lab.Magic.homingLeadSeconds).normalized;
        transform.rotation = Quaternion.LookRotation(direction);
        body.transform.localScale = Vector3.one * Mathf.Max(.2f, stats.projectileRadius * 2);
        gameObject.SetActive(true); trail.Clear();
    }

    public void Simulate(float dt)
    {
        if (!gameObject.activeSelf) return;
        if (session == null || shooter == null) { Retire(); return; }
        var stats = session.Magic.GetStats(magic);
        age += dt;
        if (age >= lifetime) { Retire(); return; }
        Vector3 before = transform.position, step;
        if (falling)
        {
            float drag = Mathf.Exp(-Mathf.Max(0, session.Magic.homingFallDrag) * dt);
            fallVelocity.x *= drag; fallVelocity.z *= drag;
            fallVelocity += Vector3.down * Mathf.Max(.1f, session.Magic.homingFallGravity) * dt;
            step = fallVelocity * dt;
        }
        else
        {
            Vector3 oldDirection = direction;
            bool exhaustion = stats.requiresTarget && MagicProjectile.UsesFlightExhaustion(magic, session.Magic);
            if (exhaustion && target != null)
            {
                target.GetCollisionCapsule(out Vector3 center, out Vector3 halfAxis, out float radius);
                bool contact = MagicProjectile.TrySweptTargetContact(previousShot, before,
                    previousTargetCenter, center, halfAxis, radius + Mathf.Max(.02f, stats.projectileRadius), out Vector3 point);
                previousTargetCenter = center;
                previousShot = before;
                if (contact && !MagicProjectile.TryBladeWallHit(point, (target.LockAimPoint - point).normalized,
                    Vector3.Distance(point, target.LockAimPoint), hits, out _))
                {
                    target.Hit(stats); session.RecordHit(stats.damage); Retire(); return;
                }
            }
            if (target != null && stats.requiresTarget)
            {
                Vector3 delta = target.LockAimPoint - before;
                if (exhaustion) delta = MagicProjectile.HomingAimDelta(delta, target.Velocity, stats.projectileSpeed * fraction, session.Magic.homingLeadSeconds);
                float turnSpeed = exhaustion ? MagicProjectile.FlightTurnSpeed(stats.projectileTurnSpeed,
                    stats.projectileSpeed * fraction, session.Magic) : stats.projectileTurnSpeed;
                direction = MagicProjectile.ResolveHomingDirection(true, direction, delta, turnSpeed, dt);
            }
            if (exhaustion)
            {
                fraction = MagicProjectile.DrainFlightSpeed(fraction, oldDirection, direction, dt, session.Magic);
                if (fraction <= session.Magic.homingFallThreshold)
                {
                    falling = true; fallVelocity = direction * stats.projectileSpeed * fraction;
                    age = 0; lifetime = Mathf.Max(.1f, session.Magic.homingFallLifetime); return;
                }
            }
            float remaining = Mathf.Max(0, MagicProjectile.FlightDistance(stats, session.Magic) - distance);
            step = direction * Mathf.Min(remaining, stats.projectileSpeed * (exhaustion ? fraction : 1) * dt);
            if (remaining <= .001f) { Retire(); return; }
            distance += step.magnitude;
        }
        int count = Physics.SphereCastNonAlloc(before, Mathf.Max(.01f, stats.projectileRadius),
            step.normalized, hits, step.magnitude, ~0, QueryTriggerInteraction.Ignore);
        Collider closest = null; float nearest = float.PositiveInfinity;
        for (int i = 0; i < count; i++)
        {
            if (hits[i].collider == null || hits[i].collider.transform.IsChildOf(shooter.transform)) continue;
            if (hits[i].distance < nearest) { closest = hits[i].collider; nearest = hits[i].distance; }
        }
        if (closest != null)
        {
            var actor = closest.GetComponentInParent<MagicBalanceLabActor>();
            if (actor != null && actor != shooter && !falling) { actor.Hit(stats); session.RecordHit(stats.damage); }
            Retire(); return;
        }
        transform.position += step;
        if (step.sqrMagnitude > .000001f) transform.rotation = Quaternion.LookRotation(step);
        if (transform.position.y < 0) Retire();
    }

    public void Retire() { trail.Clear(); gameObject.SetActive(false); }
}
