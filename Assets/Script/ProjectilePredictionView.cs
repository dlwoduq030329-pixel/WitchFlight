using System.Collections.Generic;
using Fusion;
using UnityEngine;

// Cosmetic prediction only. This component never spawns a NetworkObject, applies damage,
// consumes mana or acknowledges a hit. Server snapshots/RPCs always end the real shot.
[DisallowMultipleComponent]
public sealed class ProjectilePredictionView : MonoBehaviour
{
    private static readonly HashSet<ProjectilePredictionView> pending = new();
    private Player owner, target;
    private MagicProjectile source;
    private MagicStatEntry stats;
    private MagicStatTable table;
    private NetworkRunner runner;
    private NetworkId shooterId;
    private int inputTick;
    private float createdAt, launchAt, travelled;
    private Vector3 direction, correction;
    private bool predictedStop, predictedImpact, confirmedImpact;
    private bool hasSource;
    private GameObject content;
    private Material material;
    private ProjectileFormationVisual formation;
    private readonly RaycastHit[] hits = new RaycastHit[32];
    private readonly Collider[] overlaps = new Collider[32];

    public static bool HasPending(Player player, MagicType magic)
    {
        foreach (var view in pending)
            if (view != null && view.owner == player && view.stats.magic == magic) return true;
        return false;
    }

    public static void Predict(Player player, Player target, MagicStatEntry entry, int tick)
    {
        if (player == null || player.Object == null || !player.Object.HasInputAuthority ||
            !player.Object.IsValid || player.Runner == null || player.Object.HasStateAuthority ||
            !player.Runner.IsForward || entry.projectileSpeed <= 0f ||
            pending.Count >= 32 || HasPending(player, entry.magic)) return;
        var view = Create(entry, player.MagicCastPosition, player.transform.forward);
        view.owner = player;
        view.target = target;
        view.runner = player.Runner;
        view.shooterId = player.Object.Id;
        view.table = player.MagicTable;
        view.inputTick = tick;
        view.createdAt = Time.unscaledTime;
        view.launchAt = view.createdAt + Mathf.Max(0f, entry.castSeconds);
        view.direction = MagicProjectile.ResolveLaunchDirection(entry.requiresTarget,
            player.transform.forward, player.transform.forward,
            target != null ? target.LockAimPoint - view.transform.position : Vector3.zero);
        view.content.SetActive(entry.castSeconds <= 0f);
        pending.Add(view);
    }

    public static ProjectilePredictionView Attach(MagicProjectile projectile, MagicStatEntry entry)
    {
        ProjectilePredictionView view = null;
        foreach (var candidate in pending)
        {
            if (candidate != null && candidate.runner == projectile.Runner &&
                candidate.shooterId.Equals(projectile.ShooterId) &&
                candidate.inputTick == projectile.PredictionInputTick && candidate.stats.magic == projectile.Magic)
            { view = candidate; break; }
        }
        if (view != null) pending.Remove(view);
        else view = Create(entry, projectile.transform.position, projectile.transform.forward);
        view.hasSource = true;
        view.source = projectile;
        view.runner = projectile.Runner;
        view.shooterId = projectile.ShooterId;
        if (projectile.Runner.TryFindObject(projectile.ShooterId, out NetworkObject shooter) && shooter != null)
            view.owner = shooter.GetComponent<Player>();
        view.table = view.owner != null ? view.owner.MagicTable : null;
        // Preserve the existing predicted visual instead of showing a second projectile.
        Vector3 predictedPosition = view.transform.position;
        view.transform.SetParent(projectile.transform, true);
        view.correction = predictedPosition - view.GetConfirmedPosition();
        view.predictedImpact = view.predictedStop = false;
        return view;
    }

    private static ProjectilePredictionView Create(MagicStatEntry entry, Vector3 position, Vector3 forward)
    {
        var root = new GameObject("Projectile visual (no gameplay authority)");
        root.transform.SetPositionAndRotation(position, Quaternion.LookRotation(forward));
        var view = root.AddComponent<ProjectilePredictionView>();
        view.stats = entry;
        view.content = CombatPresentation.InstantiateVfx(entry.projectileVfxPrefab, position, root.transform.rotation, root.transform);
        if (view.content == null)
        {
            bool mine = entry.effect == MagicEffectKind.Mine;
            view.content = GameObject.CreatePrimitive(mine ? PrimitiveType.Cube : PrimitiveType.Sphere);
            var collider = view.content.GetComponent<Collider>();
            collider.enabled = false;
            Destroy(collider);
            view.content.transform.SetParent(root.transform, false);
            view.content.transform.localScale = Vector3.one * (mine ? 0.7f : 0.3f);
            Color color = CombatPresentation.MagicColor(entry.magic);
            view.material = CombatPresentation.CreateEffectMaterial(color);
            view.content.GetComponent<Renderer>().sharedMaterial = view.material;
            if (!mine)
            {
                var trail = view.content.AddComponent<TrailRenderer>();
                trail.sharedMaterial = view.material;
                trail.time = 0.15f;
                trail.startWidth = 0.22f;
                trail.endWidth = 0f;
                trail.startColor = color;
                trail.endColor = new Color(color.r, color.g, color.b, 0f);
            }
        }
        return view;
    }

    public void ConfirmImpact()
    {
        confirmedImpact = true;
        if (content != null) content.SetActive(false);
        formation?.SetVisible(false);
    }

    private void LateUpdate()
    {
        if (hasSource)
        {
            if (source == null || source.Object == null || !source.Object.IsValid)
            { Destroy(gameObject); return; }
            bool show = !confirmedImpact && !source.Finished;
            content.SetActive(show);
            formation?.SetVisible(show);
            if (!show) return;
            Vector3 desired = GetConfirmedPosition();
            correction *= Mathf.Exp(-Mathf.Max(1f, table != null ? table.projectileCorrectionSpeed : 18f) * Time.deltaTime);
            // Reconciliation must not slide through a wall either.
            Vector3 position = correction.sqrMagnitude > 0.000001f
                ? Sweep(desired, desired + correction, out _) : desired;
            transform.SetPositionAndRotation(position, source.transform.rotation);
            UpdateFormation();
            return;
        }

        float timeout = table != null ? table.projectileConfirmationTimeout : 1.2f;
        if (owner == null || owner.Object == null || !owner.Object.IsValid || !owner.IsAlive ||
            owner.IsReturningToMap || BattleManager.Instance == null || !BattleManager.Instance.IsGameplayActive ||
            Time.unscaledTime - launchAt > Mathf.Max(0.2f, timeout))
        { Destroy(gameObject); return; }
        if (Time.unscaledTime < launchAt)
        {
            if (owner.IsHitStunned || CombatPresentation.MenuOpen || owner.GetSelectedMagic() != stats.magic)
            { Destroy(gameObject); return; }
            transform.position = owner.MagicCastPosition;
            if (!stats.requiresTarget) direction = owner.transform.forward;
            return;
        }
        content.SetActive(!predictedImpact);
        formation?.SetVisible(!predictedImpact);
        if (predictedStop || predictedImpact) return;
        // Bounded substeps give the cosmetic collision sweep a turn each frame even at low FPS.
        float remaining = Mathf.Min(Time.deltaTime, 0.2f);
        while (remaining > 0f && !predictedStop && !predictedImpact)
        {
            float dt = Mathf.Min(remaining, 1f / 60f);
            remaining -= dt;
            if (target != null && target.Object != null && target.Object.IsValid && target.IsAlive)
                direction = MagicProjectile.ResolveHomingDirection(stats.requiresTarget, direction,
                    target.LockAimPoint - transform.position, stats.projectileTurnSpeed, dt);
            float limit = stats.effect == MagicEffectKind.Mine ? stats.placementDistance : stats.range;
            float step = Mathf.Min(stats.projectileSpeed * dt, Mathf.Max(0f, limit - travelled));
            Vector3 next = Sweep(transform.position, transform.position + direction * step, out bool hit);
            travelled += step;
            transform.SetPositionAndRotation(next, Quaternion.LookRotation(direction));
            if (hit || travelled >= limit - 0.001f)
            {
                predictedStop = stats.effect == MagicEffectKind.Mine;
                predictedImpact = !predictedStop;
                content.SetActive(!predictedImpact); // No speculative hit VFX or damage.
                formation?.SetVisible(!predictedImpact);
            }
        }
        if (!predictedImpact) UpdateFormation();
    }

    private void UpdateFormation()
    {
        if (table == null || !table.projectileFormationEnabled || owner == null ||
            stats.projectileSpeed <= 0f || stats.effect == MagicEffectKind.Mine)
        {
            formation?.Dispose();
            formation = null;
            return;
        }
        if (formation == null)
            formation = new ProjectileFormationVisual(this, content.transform, owner, stats, table);

        Player destination = hasSource ? null : target;
        if (hasSource && runner.TryFindObject(source.TargetId, out NetworkObject obj) && obj != null)
            destination = obj.GetComponent<Player>();
        float remaining = destination != null && destination.Object != null && destination.Object.IsValid && destination.IsAlive
            ? Vector3.Distance(transform.position, destination.LockAimPoint)
            : Mathf.Max(0f, stats.range - (hasSource ? source.TravelledDistance : travelled));
        formation.Update(transform.position, transform.forward, remaining);
    }

    private Vector3 GetConfirmedPosition()
    {
        Vector3 position = source.transform.position;
        if (!source.Object.HasInputAuthority || source.Object.HasStateAuthority || source.Settled ||
            (table != null && !table.predictLocalProjectiles)) return position;
        float lead = BoundedLead((float)(runner.LocalRenderTime - runner.RemoteRenderTime),
            table != null ? table.projectilePredictionLead : 0.2f);
        float remainingRange = (source.IsMine ? stats.placementDistance : stats.range) - source.TravelledDistance;
        Vector3 forward = source.transform.forward;
        if (stats.requiresTarget && runner.TryFindObject(source.TargetId, out NetworkObject obj) && obj != null)
        {
            var player = obj.GetComponent<Player>();
            if (player != null && player.IsAlive)
                forward = MagicProjectile.ResolveHomingDirection(true, forward, player.LockAimPoint - position, 0, 0);
        }
        return Sweep(position, position + forward * Mathf.Min(stats.projectileSpeed * lead, Mathf.Max(0f, remainingRange)), out _);
    }

    public static float BoundedLead(float timeGap, float maximum)
    {
        if (float.IsNaN(timeGap) || float.IsInfinity(timeGap) || float.IsNaN(maximum) || float.IsInfinity(maximum)) return 0f;
        return Mathf.Clamp(timeGap, 0f, Mathf.Clamp(maximum, 0f, 0.3f));
    }

    private bool Ignore(Collider collider)
    {
        if (collider == null || collider.GetComponentInParent<MagicProjectile>() != null ||
            collider.GetComponentInParent<ProjectilePredictionView>() != null) return true;
        var player = collider.GetComponentInParent<Player>();
        return player != null && (!player.IsAlive || player.Object == null || player.Object.Id.Equals(shooterId));
    }

    private Vector3 Sweep(Vector3 from, Vector3 to, out bool blocked)
    {
        blocked = false;
        float radius = Mathf.Max(0.02f, stats.projectileRadius);
        int count = Physics.OverlapSphereNonAlloc(from, radius, overlaps, ~0, QueryTriggerInteraction.Ignore);
        Collider[] allOverlaps = overlaps;
        if (count == overlaps.Length)
        { allOverlaps = Physics.OverlapSphere(from, radius, ~0, QueryTriggerInteraction.Ignore); count = allOverlaps.Length; }
        for (int i = 0; i < count; i++)
            if (!Ignore(allOverlaps[i])) { blocked = true; return from; }
        Vector3 delta = to - from;
        float length = delta.magnitude;
        if (length < 0.0001f) return from;
        count = Physics.SphereCastNonAlloc(from, radius, delta / length, hits, length, ~0, QueryTriggerInteraction.Ignore);
        RaycastHit[] allHits = hits;
        if (count == hits.Length)
        { allHits = Physics.SphereCastAll(from, radius, delta / length, length, ~0, QueryTriggerInteraction.Ignore); count = allHits.Length; }
        float nearest = length;
        for (int i = 0; i < count; i++)
            if (!Ignore(allHits[i].collider) && allHits[i].distance <= nearest)
            { nearest = Mathf.Max(0f, allHits[i].distance - 0.02f); blocked = true; }
        return from + delta / length * nearest;
    }

    private void OnDestroy()
    {
        pending.Remove(this);
        formation?.Dispose();
        if (material != null) Destroy(material);
    }
}
