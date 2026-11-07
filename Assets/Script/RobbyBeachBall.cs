using Fusion;
using UnityEngine;

// Lobby-only swept-sphere simulation. All rollback state lives in Networked fields.
// Does not take over Unity Physics.Simulate or alter the battle Runner configuration.
[DisallowMultipleComponent]
public sealed class RobbyBeachBall : NetworkBehaviour
{
    private static readonly System.Collections.Generic.List<RobbyBeachBall> activeBalls = new();
    [SerializeField] private Rigidbody body;
    [SerializeField] private RobbyGoal goal;
    [SerializeField, Min(0.1f)] private float goalResetSeconds = 2f;
    [Networked] private Vector3 SpawnPosition { get; set; }
    [Networked] private Quaternion SpawnRotation { get; set; }
    [Networked] private TickTimer GoalReset { get; set; }
    private NetworkTransform networkTransform;
    private float nextNetEffect;
    [SerializeField, Min(0f)] private float kickSpeed = 8f;
    [SerializeField, Min(0f)] private float upwardSpeed = 3f;
    [SerializeField, Min(0f)] private float contactCooldown = 0.2f;
    [Header("Lobby magic impact")]
    [SerializeField, Min(0f)] private float magicHitSpeed = 8f;
    [SerializeField, Min(0f)] private float magicLiftSpeed = .5f;
    [SerializeField, Min(.1f)] private float magicMaxSpeed = 18f;
    [SerializeField, Range(0f, 1f)] private float bounce = 0.5f;
    [SerializeField, Min(0f)] private float airDrag = 0.25f;
    [SerializeField, Min(0f)] private float groundDrag = 1.5f;
    [SerializeField] private LayerMask collisionLayers = ~0;
    [Networked] private Vector3 Position { get; set; }
    [Networked] private Vector3 Velocity { get; set; }
    [Networked] private Quaternion Rotation { get; set; }
    [Networked] private TickTimer KickCooldown { get; set; }
    private readonly RaycastHit[] hits = new RaycastHit[64];
    private SphereCollider sphere;
    private float radius;
    private Vector3 centerOffset;

    private void Awake()
    {
        if (body == null) body = GetComponent<Rigidbody>();
        sphere = GetComponent<SphereCollider>();
        networkTransform = GetComponent<NetworkTransform>();
        if (body != null) { body.isKinematic = true; body.useGravity = false; }
    }

    public override void Spawned()
    {
        if (sphere == null) { Debug.LogError("RobbyBeachBall requires a SphereCollider.", this); return; }
        Vector3 scale = transform.lossyScale;
        radius = Mathf.Max(0.01f, sphere.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
        centerOffset = Vector3.Scale(sphere.center, scale);
        if (body != null) { body.isKinematic = true; body.interpolation = RigidbodyInterpolation.None; }
        if (Object.HasStateAuthority)
        {
            Position = transform.position;
            Rotation = transform.rotation;
            SpawnPosition = Position;
            SpawnRotation = Rotation;
            Velocity = Vector3.zero;
        }
        // A ball has no input owner, so explicitly opt this proxy into resimulation.
        Runner.SetIsSimulated(Object, true);
        if (!activeBalls.Contains(this)) activeBalls.Add(this);
    }

    public override void Despawned(NetworkRunner runner, bool hasState) => activeBalls.Remove(this);
    private void OnDestroy() => activeBalls.Remove(this);

    private Vector3 WorldCenter => transform.TransformPoint(sphere.center);

    // Sweep the whole travelled segment so fast bullets and the blade's sides hit too.
    internal static bool TrySphereContact(Vector3 start, Vector3 end, Vector3 center, float hitRadius, out float fraction)
    {
        fraction = 0f;
        Vector3 delta = end - start, offset = start - center;
        float c = offset.sqrMagnitude - hitRadius * hitRadius;
        if (c <= 0f) return true;
        float a = delta.sqrMagnitude;
        if (a < .0000001f) return false;
        float b = Vector3.Dot(offset, delta);
        float discriminant = b * b - a * c;
        if (b >= 0f || discriminant < 0f) return false;
        fraction = (-b - Mathf.Sqrt(discriminant)) / a;
        return fraction >= 0f && fraction <= 1f;
    }

    internal static bool TryMagicContact(NetworkRunner runner, Vector3 start, Vector3 end, float shotRadius,
        out RobbyBeachBall ball, out float fraction)
    {
        ball = null; fraction = float.PositiveInfinity;
        foreach (var candidate in activeBalls)
        {
            if (candidate == null || !candidate.isActiveAndEnabled || candidate.Object == null ||
                !candidate.Object.IsValid || candidate.Runner != runner || candidate.sphere == null || !candidate.sphere.enabled) continue;
            if (!TrySphereContact(start, end, candidate.WorldCenter, candidate.radius + Mathf.Max(0, shotRadius), out float t) ||
                t >= fraction) continue;
            fraction = t; ball = candidate;
        }
        return ball != null;
    }

    internal static void ApplyAreaMagicHit(NetworkRunner runner, Vector3 center, float areaRadius, MagicType magic)
    {
        foreach (var ball in activeBalls)
        {
            if (ball == null || !ball.isActiveAndEnabled || ball.Object == null || !ball.Object.IsValid ||
                !ball.Object.HasStateAuthority || ball.Runner != runner || ball.sphere == null || !ball.sphere.enabled) continue;
            Vector3 away = ball.WorldCenter - center;
            float reach = Mathf.Max(0, areaRadius) + ball.radius;
            if (away.sqrMagnitude > reach * reach) continue;
            Vector3 outward = Vector3.ProjectOnPlane(away, Vector3.up);
            if (outward.sqrMagnitude < .0001f) outward = Vector3.forward;
            ball.ApplyMagicHit(outward, ball.WorldCenter - outward.normalized * ball.radius, magic, 1f);
        }
    }

    public override void FixedUpdateNetwork()
    {
        if (sphere == null) return;
        var manager = NetworkGameManager.Instance;
        if (manager == null || !manager.IsWaitingInCodeRoom) return;
        if (GoalReset.IsRunning && GoalReset.Expired(Runner))
        {
            Position = SpawnPosition; Rotation = SpawnRotation; Velocity = Vector3.zero;
            GoalReset = default; KickCooldown = TickTimer.CreateFromSeconds(Runner, 0.3f);
            transform.SetPositionAndRotation(Position, Rotation);
            if (Object.HasStateAuthority && networkTransform != null) networkTransform.Teleport(Position, Rotation);
            return;
        }
        float dt = Runner.DeltaTime;
        Vector3 center = Position + Rotation * centerOffset;
        TryKick(center);
        Vector3 velocity = Velocity + Physics.gravity * dt;
        velocity *= Mathf.Exp(-airDrag * dt);
        Vector3 start = center;
        float remaining = dt;
        for (int step = 0; step < 3 && remaining > 0.00001f; step++)
        {
            Vector3 displacement = velocity * remaining;
            float distance = displacement.magnitude;
            if (distance < 0.00001f) break;
            Vector3 direction = displacement / distance;
            if (!CastWorld(center, direction, distance, out RaycastHit hit))
            {
                center += displacement;
                break;
            }
            center += direction * Mathf.Max(0f, hit.distance - 0.005f);
            if (Object.HasStateAuthority && Runner.IsForward && goal != null && goal.IsNet(hit.collider) && Time.unscaledTime >= nextNetEffect)
            {
                nextNetEffect = Time.unscaledTime + 0.15f;
                RPC_NetImpact(hit.point, velocity);
            }
            center += hit.normal * 0.005f;
            remaining *= 1f - Mathf.Clamp01(hit.distance / distance);
            float intoSurface = Vector3.Dot(velocity, hit.normal);
            if (intoSurface < 0f)
            {
                // Small floor impacts settle instead of bouncing forever.
                float restitution = -intoSurface < 1f ? 0f : bounce;
                velocity -= (1f + restitution) * intoSurface * hit.normal;
            }
            if (hit.normal.y > 0.5f) velocity *= Mathf.Exp(-groundDrag * dt);
        }
        Vector3 travel = center - start;
        Vector3 roll = Vector3.Cross(Vector3.up, travel);
        if (roll.sqrMagnitude > 0.000001f)
            Rotation = Quaternion.AngleAxis(roll.magnitude / radius * Mathf.Rad2Deg, roll.normalized) * Rotation;
        Position = center - Rotation * centerOffset;
        Velocity = velocity;
        transform.SetPositionAndRotation(Position, Rotation);
        if (Object.HasStateAuthority && !GoalReset.IsRunning && goal != null && goal.ContainsBall(start, center, radius))
        {
            GoalReset = TickTimer.CreateFromSeconds(Runner, goalResetSeconds);
            RPC_Goal(goalResetSeconds);
        }
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_Goal(float seconds) { if (goal != null) goal.Celebrate(seconds); }
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_NetImpact(Vector3 point, Vector3 velocity) { if (goal != null) goal.HitNet(point, velocity); }

    internal void ApplyMagicHit(Vector3 direction, Vector3 point, MagicType magic, float strength)
    {
        if (Object == null || !Object.IsValid || !Object.HasStateAuthority || GoalReset.IsRunning ||
            direction.sqrMagnitude < .0001f) return;
        // The Rigidbody is kinematic: use the same networked velocity as walking kicks.
        // Rolling, gravity, wall bounces and goal reset continue in FixedUpdateNetwork.
        Velocity = Vector3.ClampMagnitude(Velocity +
            (direction.normalized * magicHitSpeed + Vector3.up * magicLiftSpeed) * Mathf.Clamp01(strength), magicMaxSpeed);
        KickCooldown = TickTimer.CreateFromSeconds(Runner, contactCooldown);
        RPC_MagicImpact(point, (int)magic);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_MagicImpact(Vector3 point, int magic) => CombatPresentation.ShowImpact(point, (MagicType)magic);

    private void TryKick(Vector3 center)
    {
        if (GoalReset.IsRunning || !KickCooldown.ExpiredOrNotRunning(Runner)) return;
        // Registry is maintained on spawn/despawn. No scene search or allocation.
        foreach (PlayerRobby player in PlayerRobby.ActivePlayers)
        {
            if (player == null || player.Object == null || !player.Object.IsValid || player.Runner != Runner ||
                (!Object.HasStateAuthority && !player.Object.HasInputAuthority) || !player.TouchesBall(center, radius)) continue;
            Vector3 away = center - player.SimulationPosition;
            away.y = 0f;
            if (away.sqrMagnitude < 0.0001f) away = Quaternion.Euler(0f, player.SimulationYaw, 0f) * Vector3.forward;
            Velocity = away.normalized * kickSpeed + Vector3.up * upwardSpeed;
            KickCooldown = TickTimer.CreateFromSeconds(Runner, contactCooldown);
            // Presentation is not replayed during rollback.
            if (Runner.IsForward)
            {
                if (Object.HasStateAuthority) player.PlayBallKick();
                else player.PredictBallKick();
            }
            break;
        }
    }

    private bool CastWorld(Vector3 origin, Vector3 direction, float distance, out RaycastHit nearest)
    {
        nearest = default;
        int count = Physics.SphereCastNonAlloc(origin, radius, direction, hits, distance,
            collisionLayers, QueryTriggerInteraction.Ignore);
        float best = float.PositiveInfinity;
        for (int i = 0; i < count; i++)
        {
            var hit = hits[i];
            if (hit.collider == null || hit.collider.transform.IsChildOf(transform)) continue;
            bool avatar = false;
            foreach (PlayerRobby player in PlayerRobby.ActivePlayers)
                if (player != null && hit.collider.transform.IsChildOf(player.transform)) { avatar = true; break; }
            if (avatar || hit.distance >= best) continue;
            best = hit.distance;
            nearest = hit;
        }
        return !float.IsPositiveInfinity(best);
    }
}
