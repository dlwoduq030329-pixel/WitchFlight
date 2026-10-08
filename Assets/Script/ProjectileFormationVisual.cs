using UnityEngine;

// One local controller for all satellites. Pure VFX: no collision, damage or per-satellite Update.
internal sealed class ProjectileFormationVisual
{
    private readonly Transform core;
    private readonly MagicStatTable table;
    private readonly GameObject root;
    private readonly Transform[] satellites;
    private readonly Vector3[] scales, previousPositions;
    private readonly Vector3 birthPosition, birthForward;
    private readonly Quaternion birthRotation;
    private readonly Vector3 coreScale;
    private readonly float startedAt, catchupSeconds;
    private Material ownedMaterial;
    private float merge;

    public ProjectileFormationVisual(ProjectilePredictionView host, Transform core, Player owner,
        MagicStatEntry stats, MagicStatTable table)
    {
        this.core = core;
        this.table = table;
        coreScale = core.localScale;
        core.localScale = coreScale * Mathf.Max(0.01f, table.projectileCoreVisualScale);
        birthForward = owner.transform.forward;
        birthRotation = owner.transform.rotation;
        birthPosition = owner.LockAimPoint - birthForward * 0.15f;
        startedAt = Time.time;
        catchupSeconds = CatchupSeconds(stats.projectileSpeed, table.projectileSatelliteSpeedMultiplier,
            table.projectileSatelliteBackDistance, table.projectileSatelliteLeadDistance, table.projectileSatelliteBackSeconds);
        root = new GameObject("Projectile satellites (visual only)");
        root.transform.SetParent(host.transform, false);
        int count = Mathf.Clamp(table.projectileSatelliteCount, 1, 12);
        satellites = new Transform[count];
        scales = new Vector3[count];
        previousPositions = new Vector3[count];
        Color color = CombatPresentation.MagicColor(stats.magic);
        for (int i = 0; i < count; i++)
        {
            Vector3 initial = birthPosition + birthRotation * RingOffset(i, count) * 0.15f;
            GameObject view = CombatPresentation.InstantiateVfx(table.projectileSatelliteVfxPrefab,
                initial, birthRotation, root.transform);
            if (view == null)
            {
                view = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Collider collider = view.GetComponent<Collider>();
                collider.enabled = false;
                Object.Destroy(collider);
                view.transform.SetParent(root.transform, false);
                view.transform.localScale = Vector3.one * 0.3f;
                if (ownedMaterial == null) ownedMaterial = CombatPresentation.CreateEffectMaterial(color);
                view.GetComponent<Renderer>().sharedMaterial = ownedMaterial;
                var trail = view.AddComponent<TrailRenderer>();
                trail.sharedMaterial = ownedMaterial;
                trail.time = 0.12f;
                trail.startWidth = 0.07f;
                trail.endWidth = 0f;
                trail.startColor = color;
                trail.endColor = new Color(color.r, color.g, color.b, 0f);
            }
            view.name = "Satellite " + (i + 1);
            DisableSatelliteCollisions(view);
            satellites[i] = view.transform;
            scales[i] = view.transform.localScale * Mathf.Max(0.01f, table.projectileSatelliteVisualScale);
            view.transform.localScale = scales[i];
            previousPositions[i] = initial;
            view.transform.position = initial;
        }
    }

    public void Update(Vector3 corePosition, Vector3 forward, float remainingDistance)
    {
        // Never split back apart if the target turns away after merging has begun.
        merge = Mathf.Max(merge, MergeProgress(remainingDistance,
            table.projectileMergeStartDistance, table.projectileMergeEndDistance));
        core.localScale = coreScale * Mathf.Max(0.01f, table.projectileCoreVisualScale) * Mathf.Lerp(1f, 1.2f, merge);
        SetVisible(merge < 1f);
        if (merge >= 1f) return;
        float age = Mathf.Max(0f, Time.time - startedAt);
        Quaternion flightRotation = Quaternion.LookRotation(forward);
        for (int i = 0; i < satellites.Length; i++)
        {
            Vector3 radial = RingOffset(i, satellites.Length);
            Vector3 initial = birthPosition + birthRotation * radial * 0.15f;
            Vector3 retreat = birthPosition - birthForward * Mathf.Max(0f, table.projectileSatelliteBackDistance)
                + birthRotation * radial * Mathf.Max(0f, table.projectileSatelliteSpread);
            Vector3 leading = corePosition + forward * Mathf.Max(0f, table.projectileSatelliteLeadDistance)
                + flightRotation * radial * Mathf.Max(0f, table.projectileSatelliteSpread);
            Vector3 desired = FlightPosition(age, table.projectileSatelliteBackSeconds, catchupSeconds,
                initial, retreat, birthForward, leading, forward, table.projectileSatelliteBackDistance);
            desired = Vector3.Lerp(desired, corePosition, merge);
            // Small satellites pass through walls/players; only the core performs hit tests.
            Vector3 delta = desired - previousPositions[i];
            satellites[i].SetPositionAndRotation(desired,
                delta.sqrMagnitude > 0.000001f ? Quaternion.LookRotation(delta) : birthRotation);
            satellites[i].localScale = scales[i] * (1f - merge);
            previousPositions[i] = desired;
        }
    }

    private static void DisableSatelliteCollisions(GameObject view)
    {
        // Apply to custom VFX as well as fallback spheres, including inactive children.
        foreach (Collider collider in view.GetComponentsInChildren<Collider>(true))
            collider.enabled = false;
        foreach (Rigidbody body in view.GetComponentsInChildren<Rigidbody>(true))
        {
            body.detectCollisions = false;
            body.isKinematic = true;
            body.useGravity = false;
        }
        foreach (ParticleSystem particles in view.GetComponentsInChildren<ParticleSystem>(true))
        {
            var collision = particles.collision;
            collision.enabled = false;
            var trigger = particles.trigger;
            trigger.enabled = false;
        }
    }

    public void SetVisible(bool visible)
    {
        if (root != null) root.SetActive(visible && merge < 1f);
    }

    public void Dispose()
    {
        SetVisible(false);
        if (core != null) core.localScale = coreScale;
        if (root != null) Object.Destroy(root);
        if (ownedMaterial != null) Object.Destroy(ownedMaterial);
    }

    internal static Vector3 RingOffset(int index, int count)
    {
        float angle = index * Mathf.PI * 2f / Mathf.Max(1, count);
        return new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f);
    }

    internal static float CatchupSeconds(float speed, float multiplier, float backDistance, float leadDistance, float backSeconds)
    {
        float gap = Mathf.Max(0f, backDistance) + Mathf.Max(0f, leadDistance) + Mathf.Max(0f, speed) * Mathf.Max(0f, backSeconds);
        return Mathf.Clamp(gap / Mathf.Max(0.1f, speed * (Mathf.Max(1.1f, multiplier) - 1f)), 0.05f, 2f);
    }

    internal static float MergeProgress(float remaining, float start, float end)
    {
        end = Mathf.Max(0f, end);
        start = Mathf.Max(end + 0.01f, start);
        return Mathf.SmoothStep(0f, 1f, 1f - Mathf.Clamp01((remaining - end) / (start - end)));
    }

    internal static Vector3 FlightPosition(float age, float backSeconds, float catchupSeconds,
        Vector3 initial, Vector3 retreat, Vector3 birthForward, Vector3 leading, Vector3 forward, float backDistance)
    {
        backSeconds = Mathf.Max(0.01f, backSeconds);
        if (age <= backSeconds)
            return Vector3.Lerp(initial, retreat, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(age / backSeconds)));
        float t = Mathf.Clamp01((age - backSeconds) / Mathf.Max(0.01f, catchupSeconds));
        // Bezier tangent initially continues backwards, then bends into the forward flight.
        float bend = Mathf.Max(0.1f, backDistance * 0.5f);
        Vector3 c1 = retreat - birthForward * bend;
        Vector3 c2 = leading - forward * bend;
        float s = 1f - t;
        return s * s * s * retreat + 3f * s * s * t * c1 + 3f * s * t * t * c2 + t * t * t * leading;
    }
}
