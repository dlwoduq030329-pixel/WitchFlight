using System.Collections.Generic;
using Fusion;
using UnityEngine;

// Uses the existing runner. Only StateAuthority simulates flight and damage.
public sealed class MagicProjectile : NetworkBehaviour
{
    private static readonly HashSet<MagicProjectile> active = new();
    [Networked] public MagicType Magic { get; private set; }
    [Networked] public PlayerRef Shooter { get; private set; }
    [Networked] public int ShooterTeam { get; private set; }
    [Networked] public NetworkId TargetId { get; private set; }
    [Networked] public bool IsMine { get; private set; }
    [Networked] public bool Settled { get; private set; }
    [Networked] public bool Finished { get; private set; }
    [Networked] private TickTimer Lifetime { get; set; }
    [Networked] private TickTimer ArmTimer { get; set; }
    [Networked] private TickTimer RevealTimer { get; set; }

    private MagicStatEntry stats;
    private Vector3 direction;
    private float travelled;
    private bool initialized;
    private Renderer visual;
    private Material material;
    private TrailRenderer trail;
    private MagicType visualMagic = MagicType.None;
    public bool IsRevealed => RevealTimer.IsRunning && !RevealTimer.Expired(Runner);

    public override void Spawned()
    {
        active.Add(this);
    }

    public void Initialize(Player shooter, Player target, MagicStatEntry entry, Vector3 aim)
    {
        if (!Object.HasStateAuthority)
            return;
        stats = entry;
        Magic = entry.magic;
        Shooter = shooter.Object.InputAuthority;
        ShooterTeam = shooter.TeamIndex;
        TargetId = target != null ? target.Object.Id : default;
        IsMine = entry.effect == MagicEffectKind.Mine;
        direction = aim.normalized;
        if (direction.sqrMagnitude < 0.5f)
            direction = shooter.transform.forward;
        transform.rotation = Quaternion.LookRotation(direction);
        float duration = IsMine
            ? entry.placementDistance / Mathf.Max(1f, entry.projectileSpeed) +
                entry.activationDelay + entry.effectDuration
            : entry.projectileLifetime;
        Lifetime = TickTimer.CreateFromSeconds(Runner, Mathf.Max(0.1f, duration));
        initialized = true;
    }

    public override void FixedUpdateNetwork()
    {
        if (!Object.HasStateAuthority || !initialized)
            return;
        if (BattleManager.Instance == null || !BattleManager.Instance.IsGameplayActive)
        {
            Runner.Despawn(Object);
            return;
        }
        if (Lifetime.Expired(Runner))
        {
            if (!Finished && !IsMine && stats.effect == MagicEffectKind.AreaDamage)
                Impact(transform.position);
            else
                Runner.Despawn(Object);
            return;
        }
        if (Finished)
            return;
        if (IsMine && Settled)
        {
            CheckMine();
            return;
        }

        Player target = FindTarget();
        if (target != null && stats.projectileTurnSpeed > 0f)
        {
            Vector3 desired = target.LockAimPoint - transform.position;
            if (desired.sqrMagnitude > 0.001f)
                direction = Vector3.RotateTowards(direction, desired.normalized,
                    stats.projectileTurnSpeed * Mathf.Deg2Rad * Runner.DeltaTime, 0f).normalized;
        }

        float limit = IsMine ? stats.placementDistance : stats.range;
        float step = Mathf.Min(stats.projectileSpeed * Runner.DeltaTime, Mathf.Max(0f, limit - travelled));
        Vector3 next = transform.position + direction * step;
        if (TryGetInitialOverlap(out Collider overlap))
        {
            if (IsMine)
                SettleMine();
            else
                Impact(transform.position, overlap.GetComponentInParent<Player>());
            return;
        }
        if (TryGetCollision(step, out RaycastHit hit))
        {
            next = transform.position + direction * Mathf.Max(0f, hit.distance - 0.02f);
            transform.position = next;
            if (IsMine)
                SettleMine();
            else
                Impact(next, hit.collider.GetComponentInParent<Player>());
            return;
        }

        transform.SetPositionAndRotation(next, Quaternion.LookRotation(direction));
        travelled += step;
        if (travelled >= limit - 0.001f)
        {
            if (IsMine)
                SettleMine();
            else
                Impact(next);
        }
    }

    private Player FindTarget()
    {
        if (!Runner.TryFindObject(TargetId, out NetworkObject obj) || obj == null)
            return null;
        Player target = obj.GetComponent<Player>();
        if (target == null || !target.IsAlive || target.IsStealthed || target.TeamIndex == ShooterTeam)
        {
            TargetId = default;
            return null;
        }
        return target;
    }

    private bool TryGetCollision(float distance, out RaycastHit nearest)
    {
        nearest = default;
        float best = float.PositiveInfinity;
        foreach (RaycastHit hit in Physics.SphereCastAll(transform.position,
                     Mathf.Max(0.02f, stats.projectileRadius), direction, distance, ~0,
                     QueryTriggerInteraction.Ignore))
        {
            if (hit.collider == null || hit.transform.IsChildOf(transform))
                continue;
            Player player = hit.collider.GetComponentInParent<Player>();
            if (player != null && (!player.IsAlive || player.Object.InputAuthority == Shooter))
                continue;
            if (hit.distance >= best)
                continue;
            best = hit.distance;
            nearest = hit;
        }
        return best < float.PositiveInfinity;
    }

    private bool TryGetInitialOverlap(out Collider overlap)
    {
        foreach (Collider collider in Physics.OverlapSphere(transform.position,
                     Mathf.Max(0.02f, stats.projectileRadius), ~0, QueryTriggerInteraction.Ignore))
        {
            if (collider == null || collider.transform.IsChildOf(transform))
                continue;
            Player player = collider.GetComponentInParent<Player>();
            if (player != null && (!player.IsAlive || player.Object.InputAuthority == Shooter))
                continue;
            overlap = collider;
            return true;
        }
        overlap = null;
        return false;
    }

    private void SettleMine()
    {
        Settled = true;
        ArmTimer = TickTimer.CreateFromSeconds(Runner, Mathf.Max(0.01f, stats.activationDelay));
        Lifetime = TickTimer.CreateFromSeconds(Runner,
            Mathf.Max(0.01f, stats.activationDelay) + Mathf.Max(0.1f, stats.effectDuration));
    }

    private void CheckMine()
    {
        if (!ArmTimer.Expired(Runner))
            return;
        foreach (Player player in FindObjectsByType<Player>(FindObjectsSortMode.None))
        {
            if (player.IsAlive && (player.LockAimPoint - transform.position).sqrMagnitude <= stats.radius * stats.radius &&
                HasBlastSight(player))
            {
                Impact(transform.position);
                return;
            }
        }
    }

    private void Impact(Vector3 point, Player directHit = null)
    {
        if (Finished)
            return;
        Finished = true;
        transform.position = point;
        if (IsMine || stats.effect == MagicEffectKind.AreaDamage)
        {
            foreach (Player player in FindObjectsByType<Player>(FindObjectsSortMode.None))
            {
                if (!player.IsAlive || (!IsMine && player.TeamIndex == ShooterTeam) ||
                    (player.LockAimPoint - point).sqrMagnitude > stats.radius * stats.radius || !HasBlastSight(player))
                    continue;
                player.ReceiveMagicHit(stats, Shooter);
            }
        }
        else if (directHit != null && directHit.IsAlive && directHit.TeamIndex != ShooterTeam)
        {
            directHit.ReceiveMagicHit(stats, Shooter);
        }
        RPC_Impact(point, Magic);
        // Leave the object alive briefly so the impact RPC reaches all observers.
        Lifetime = TickTimer.CreateFromSeconds(Runner, 0.2f);
    }

    private bool HasBlastSight(Player target)
    {
        return HasBlastSight(transform.position, target);
    }

    public static bool HasBlastSight(Vector3 origin, Player target)
    {
        // Rays alone can miss a wall enclosing their origin.
        foreach (Collider collider in Physics.OverlapSphere(origin, 0.01f, ~0, QueryTriggerInteraction.Ignore))
            if (collider != null && collider.GetComponentInParent<Player>() == null &&
                collider.GetComponentInParent<MagicProjectile>() == null)
                return false;
        Vector3 delta = target.LockAimPoint - origin;
        foreach (RaycastHit hit in Physics.RaycastAll(origin, delta.normalized, delta.magnitude,
                     ~0, QueryTriggerInteraction.Ignore))
        {
            if (hit.collider == null || hit.collider.GetComponentInParent<MagicProjectile>() != null ||
                hit.collider.GetComponentInParent<Player>() != null)
                continue;
            return false;
        }
        return true;
    }

    public static void BreakTracking(Player target)
    {
        foreach (MagicProjectile shot in active)
        {
            if (shot != null && shot.Object != null && shot.Object.HasStateAuthority &&
                shot.TargetId.Equals(target.Object.Id))
                shot.TargetId = default;
        }
    }

    public static void RevealMines(Player scanner, float radius, float duration)
    {
        foreach (MagicProjectile shot in active)
        {
            if (shot != null && shot.Object != null && shot.Object.HasStateAuthority && shot.IsMine &&
                (shot.transform.position - scanner.transform.position).sqrMagnitude <= radius * radius)
                shot.RevealTimer = TickTimer.CreateFromSeconds(shot.Runner, Mathf.Max(0.1f, duration));
        }
    }

    public static bool HasMineOwnedBy(PlayerRef player)
    {
        foreach (MagicProjectile shot in active)
            if (shot != null && shot.Object != null && shot.Object.IsValid &&
                shot.IsMine && !shot.Finished && shot.Shooter == player)
                return true;
        return false;
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_Impact(Vector3 position, MagicType magic)
    {
        CombatPresentation.ShowImpact(position, magic);
    }

    public override void Render()
    {
        if (Magic == MagicType.None)
            return;
        if (visual == null)
            CreateVisual();
        if (visualMagic != Magic)
        {
            visualMagic = Magic;
            Color color = Magic == MagicType.Ice ? Color.cyan :
                Magic == MagicType.Thunder ? new Color(0.7f, 0.35f, 1f) :
                IsMine ? Color.yellow : new Color(1f, 0.25f, 0.05f);
            material.color = color;
            trail.startColor = color;
            trail.endColor = new Color(color.r, color.g, color.b, 0f);
        }
        Player local = Player.LocalPlayer;
        bool visible = !Finished && (!IsMine || local == null || local.TeamIndex == ShooterTeam || IsRevealed);
        visual.enabled = visible;
        trail.enabled = visible && !IsMine;
    }

    private void CreateVisual()
    {
        GameObject ball = GameObject.CreatePrimitive(IsMine ? PrimitiveType.Cube : PrimitiveType.Sphere);
        ball.name = IsMine ? "Mine visual" : "Magic visual";
        Collider collider = ball.GetComponent<Collider>();
        collider.enabled = false;
        Destroy(collider);
        ball.transform.SetParent(transform, false);
        ball.transform.localScale = Vector3.one * (IsMine ? 0.7f : 0.3f);
        visual = ball.GetComponent<Renderer>();
        material = CombatPresentation.CreateEffectMaterial(Color.white);
        visual.sharedMaterial = material;
        trail = ball.AddComponent<TrailRenderer>();
        trail.sharedMaterial = material;
        trail.time = 0.15f;
        trail.startWidth = 0.22f;
        trail.endWidth = 0f;
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        active.Remove(this);
    }

    private void OnDestroy()
    {
        active.Remove(this);
        if (material != null)
            Destroy(material);
    }
}
