using System.Collections.Generic;
using Fusion;
using UnityEngine;

public enum BattleStartPhase { WaitingForPlayers, Intro, Countdown, Playing, Ended }

// One existing network flag owns the replicated match clock, carrier and result.
public class BattleFlag : NetworkBehaviour, IAfterRender
{
    [SerializeField, Min(0.1f)] private float pickupRadius = 0.85f;
    [SerializeField] private Vector3 carryOffset = new Vector3(0f, 1.3f, -0.5f);

    [Networked] public PlayerRef Carrier { get; private set; }
    [Networked] public PlayerRef LastCarrier { get; private set; }
    [Networked] public int LastCarrierTeam { get; private set; }
    [Networked] public int WinningTeam { get; private set; }
    [Networked] public bool HasEnded { get; private set; }
    [Networked] private bool HasStarted { get; set; }
    [Networked] private NetworkId CarrierObjectId { get; set; }
    [Networked] private Vector3 WorldPosition { get; set; }
    [Networked] public bool IsDescending { get; private set; }
    [Networked] private TickTimer MatchTimer { get; set; }
    [Networked] public BattleStartPhase Phase { get; private set; }
    [Networked] public int ExpectedPlayerCount { get; private set; }
    [Networked] private bool IsPrepared { get; set; }
    [Networked] private TickTimer PhaseTimer { get; set; }

    private readonly HashSet<PlayerRef> readyPlayers = new();
    private bool startRequested;
    private float introSeconds;
    private float matchSeconds;
    private float initialFlagHeight;
    private float fallStopHeight;
    private float fallSpeed;

    private struct PositionSample
    {
        public NetworkId ObjectId;
        public Vector3 Center;
    }

    private readonly Dictionary<PlayerRef, PositionSample> previousPositions = new();
    private readonly RaycastHit[] obstacleHits = new RaycastHit[32];
    private PropertyReader<Vector3> worldPositionReader;
    private PropertyReader<NetworkId> carrierIdReader;
    public static BattleFlag Instance { get; private set; }
    public float PhaseRemainingSeconds => Object != null && Object.IsValid
        ? Mathf.Max(0f, PhaseTimer.RemainingTime(Runner) ?? 0f) : 0f;
    public float RemainingSeconds => Object != null && Object.IsValid && HasStarted && !HasEnded
        ? Mathf.Max(0f, MatchTimer.RemainingTime(Runner) ?? 0f) : 0f;

    public override void Spawned()
    {
        Object.ForceRemoteRenderTimeframe = !Object.HasStateAuthority;
        worldPositionReader = GetPropertyReader<Vector3>(nameof(WorldPosition));
        carrierIdReader = GetPropertyReader<NetworkId>(nameof(CarrierObjectId));
        Instance = this;
        BattleManager.Instance?.RegisterFlag(this);
        foreach (Collider flagCollider in GetComponentsInChildren<Collider>())
            flagCollider.isTrigger = true;
    }

    public void PrepareMatch(Vector3 position, float durationSeconds, int requiredPlayers, float presentationSeconds)
    {
        if (!Object.HasStateAuthority || IsPrepared)
            return;
        WorldPosition = position;
        transform.position = position;
        initialFlagHeight = position.y;
        IsDescending = false;
        ExpectedPlayerCount = Mathf.Max(1, requiredPlayers);
        matchSeconds = Mathf.Max(1f, durationSeconds);
        introSeconds = Mathf.Max(0.1f, presentationSeconds);
        Phase = BattleStartPhase.WaitingForPlayers;
        PhaseTimer = TickTimer.None;
        MatchTimer = TickTimer.None;
        readyPlayers.Clear();
        HasStarted = false;
        HasEnded = false;
        IsPrepared = true;
    }

    // Called only by an InputAuthority RPC on the actual Player object, never a supplied player id.
    public void MarkPlayerReady(PlayerRef playerRef)
    {
        if (!Object.HasStateAuthority || !IsPrepared || Phase != BattleStartPhase.WaitingForPlayers)
            return;
        if (!Runner.TryGetPlayerObject(playerRef, out NetworkObject playerObject) ||
            playerObject == null || !playerObject.IsValid || playerObject.InputAuthority != playerRef)
            return;
        Player player = playerObject.GetComponent<Player>();
        if (player != null && player.IsAlive && player.TeamIndex > 0 && player.MaxHp > 0f)
            readyPlayers.Add(playerRef);
    }

    public void RequestStart()
    {
        if (Object.HasStateAuthority && IsPrepared && Phase == BattleStartPhase.WaitingForPlayers)
            startRequested = true;
    }

    public void ForgetReadyPlayer(PlayerRef playerRef)
    {
        if (Object == null || !Object.IsValid || !Object.HasStateAuthority)
            return;
        readyPlayers.Remove(playerRef);
        if (Phase == BattleStartPhase.Intro || Phase == BattleStartPhase.Countdown)
            ResetPreparation();
    }

    private bool AreAllPlayersReady()
    {
        int count = 0;
        foreach (PlayerRef playerRef in Runner.ActivePlayers)
        {
            count++;
            if (!readyPlayers.Contains(playerRef) ||
                !Runner.TryGetPlayerObject(playerRef, out NetworkObject playerObject) ||
                playerObject == null || !playerObject.IsValid)
                return false;
        }
        return count >= ExpectedPlayerCount;
    }

    private void ResetPreparation()
    {
        Phase = BattleStartPhase.WaitingForPlayers;
        PhaseTimer = TickTimer.None;
        readyPlayers.Clear();
    }

    private void TickPreparation()
    {
        if (!AreAllPlayersReady())
        {
            // Never begin a two-player match if one leaves during the intro/countdown.
            if (Phase != BattleStartPhase.WaitingForPlayers)
                ResetPreparation();
            return;
        }
        if (Phase == BattleStartPhase.WaitingForPlayers)
        {
            BattleManager manager = BattleManager.Instance;
            if (!startRequested && manager != null && manager.AutoStartWhenReady)
                manager.startGame();
            if (!startRequested)
                return;
            Phase = BattleStartPhase.Intro;
            PhaseTimer = TickTimer.CreateFromSeconds(Runner, introSeconds);
        }
        else if (Phase == BattleStartPhase.Intro && PhaseTimer.Expired(Runner))
        {
            Phase = BattleStartPhase.Countdown;
            PhaseTimer = TickTimer.CreateFromSeconds(Runner, 3f);
        }
        else if (Phase == BattleStartPhase.Countdown && PhaseTimer.Expired(Runner))
        {
            BeginMatch(WorldPosition, matchSeconds);
        }
    }

    private void BeginMatch(Vector3 position, float durationSeconds)
    {
        if (!Object.HasStateAuthority || HasStarted)
            return;
        Carrier = PlayerRef.None;
        LastCarrier = PlayerRef.None;
        LastCarrierTeam = 0;
        WinningTeam = 0;
        CarrierObjectId = default;
        WorldPosition = position;
        transform.position = position;
        HasEnded = false;
        HasStarted = true;
        Phase = BattleStartPhase.Playing;
        PhaseTimer = TickTimer.None;
        MatchTimer = TickTimer.CreateFromSeconds(Runner, Mathf.Max(1f, durationSeconds));
        previousPositions.Clear();
    }

    public override void FixedUpdateNetwork()
    {
        if (!Object.HasStateAuthority || !IsPrepared || HasEnded)
            return;
        if (Phase != BattleStartPhase.Playing)
        {
            TickPreparation();
            return;
        }
        if (MatchTimer.Expired(Runner))
        {
            HasEnded = true;
            Phase = BattleStartPhase.Ended;
            WinningTeam = LastCarrierTeam;
            BattleManager.Instance?.NotifyBattleEnded(WinningTeam);
            return;
        }

        BattleManager.Instance?.TickRespawns();
        if (Carrier != PlayerRef.None)
        {
            Player carrier = GetCarrierPlayer();
            if (carrier == null || !carrier.IsAlive)
                DropCarrier(Carrier, carrier != null ? carrier.transform.position : WorldPosition,
                    carrier != null && carrier.DiedFromAltitude);
            else
                WorldPosition = carrier.transform.TransformPoint(carryOffset);
        }
        UpdateDescent();
        CheckPickupAndRecordPositions();
        transform.position = WorldPosition;
    }

    private Player GetCarrierPlayer()
    {
        return Runner.TryFindObject(CarrierObjectId, out NetworkObject carrierObject) && carrierObject != null
            ? carrierObject.GetComponent<Player>() : null;
    }

    public void DropCarrier(PlayerRef playerRef, Vector3 position, bool descend = false)
    {
        if (!Object.HasStateAuthority || HasEnded || Carrier != playerRef || playerRef == PlayerRef.None)
            return;
        WorldPosition = position;
        Carrier = PlayerRef.None;
        CarrierObjectId = default;
        IsDescending = descend;
        if (descend)
        {
            fallStopHeight = Mathf.Min(initialFlagHeight, position.y);
            MapBoundaryTable table = BattleManager.Instance != null ? BattleManager.Instance.MapBoundary : null;
            fallSpeed = Mathf.Max(0.1f, table != null ? table.flagFallSpeed : 5f);
        }
        // Keep LastCarrier/LastCarrierTeam: the rule is the last holder, even if the flag is dropped.
        previousPositions.Clear();
    }

    private void UpdateDescent()
    {
        if (!IsDescending || Carrier != PlayerRef.None)
            return;
        float step = fallSpeed * Runner.DeltaTime;
        float stopHeight = fallStopHeight;
        // Stop above solid scenery, ignoring flag triggers and player colliders.
        RaycastHit[] hits = GetObstacleHits(WorldPosition, Vector3.down, step + pickupRadius, out int count);
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = hits[i];
            if (hit.collider == null || hit.transform.IsChildOf(transform) ||
                hit.collider.GetComponentInParent<Player>() != null)
                continue;
            stopHeight = Mathf.Max(stopHeight, Mathf.Min(WorldPosition.y, hit.point.y + pickupRadius));
        }
        Vector3 position = WorldPosition;
        position.y = Mathf.Max(stopHeight, position.y - step);
        WorldPosition = position;
        if (position.y <= stopHeight)
            IsDescending = false;
    }

    private void CheckPickupAndRecordPositions()
    {
        Player candidate = null;
        float nearestDistance = float.PositiveInfinity;
        foreach (PlayerRef playerRef in Runner.ActivePlayers)
        {
            if (!Runner.TryGetPlayerObject(playerRef, out NetworkObject playerObject) || playerObject == null)
                continue;
            Player player = playerObject.GetComponent<Player>();
            if (player == null || !player.IsAlive)
            {
                previousPositions.Remove(playerRef);
                continue;
            }

            GetCapsule(player, out Vector3 center, out Vector3 halfAxis, out float radius);
            Vector3 previous = previousPositions.TryGetValue(playerRef, out PositionSample sample) &&
                sample.ObjectId == playerObject.Id ? sample.Center : center;
            previousPositions[playerRef] = new PositionSample { ObjectId = playerObject.Id, Center = center };
            if (Carrier != PlayerRef.None)
                continue;

            // Sweep the capsule between ticks so a fast player cannot skip through the flag.
            // Expanding the flag into a segment gives the same translational capsule distance.
            float distance = SegmentDistanceSquared(previous, center,
                WorldPosition - halfAxis, WorldPosition + halfAxis, out Vector3 contactCenter);
            float contactRadius = radius + pickupRadius;
            if (distance <= contactRadius * contactRadius && distance < nearestDistance &&
                HasPickupSight(contactCenter))
            {
                candidate = player;
                nearestDistance = distance;
            }
        }

        if (candidate == null)
            return;
        Carrier = candidate.Object.InputAuthority;
        IsDescending = false;
        LastCarrier = Carrier;
        LastCarrierTeam = candidate.TeamIndex;
        CarrierObjectId = candidate.Object.Id;
        WorldPosition = candidate.transform.TransformPoint(carryOffset);
    }

    private bool HasPickupSight(Vector3 contactCenter)
    {
        Vector3 delta = WorldPosition - contactCenter;
        if (delta.sqrMagnitude < 0.0001f)
            return true;
        RaycastHit[] hits = GetObstacleHits(contactCenter, delta.normalized, delta.magnitude, out int count);
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = hits[i];
            if (hit.collider == null || hit.transform.IsChildOf(transform) ||
                hit.collider.GetComponentInParent<Player>() != null)
                continue;
            return false;
        }
        return true;
    }

    private RaycastHit[] GetObstacleHits(Vector3 origin, Vector3 direction, float distance, out int count)
    {
        count = Physics.RaycastNonAlloc(origin, direction, obstacleHits, distance,
            ~0, QueryTriggerInteraction.Ignore);
        if (count < obstacleHits.Length) return obstacleHits;
        RaycastHit[] all = Physics.RaycastAll(origin, direction, distance, ~0, QueryTriggerInteraction.Ignore);
        count = all.Length;
        return all;
    }

    private static void GetCapsule(Player player, out Vector3 center, out Vector3 halfAxis, out float radius)
    {
        Transform playerTransform = player.transform;
        CharacterController controller = player.GetComponent<CharacterController>();
        if (controller != null)
        {
            Vector3 scale = playerTransform.lossyScale;
            radius = controller.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
            float halfHeight = Mathf.Max(0f, controller.height * Mathf.Abs(scale.y) * 0.5f - radius);
            center = playerTransform.TransformPoint(controller.center);
            halfAxis = playerTransform.up * halfHeight;
            return;
        }
        CapsuleCollider capsule = player.GetComponent<CapsuleCollider>();
        if (capsule != null)
        {
            Vector3 scale = playerTransform.lossyScale;
            int axis = capsule.direction;
            radius = capsule.radius * Mathf.Max(Mathf.Abs(scale[(axis + 1) % 3]), Mathf.Abs(scale[(axis + 2) % 3]));
            Vector3 direction = axis == 0 ? Vector3.right : axis == 1 ? Vector3.up : Vector3.forward;
            halfAxis = playerTransform.TransformDirection(direction) *
                Mathf.Max(0f, capsule.height * Mathf.Abs(scale[axis]) * 0.5f - radius);
            center = playerTransform.TransformPoint(capsule.center);
            return;
        }
        center = playerTransform.position;
        halfAxis = Vector3.zero;
        radius = 0.2f;
    }

    private static float SegmentDistanceSquared(Vector3 a0, Vector3 a1, Vector3 b0, Vector3 b1,
        out Vector3 closestPlayerCenter)
    {
        closestPlayerCenter = a0;
        Vector3 a = a1 - a0;
        Vector3 b = b1 - b0;
        Vector3 offset = a0 - b0;
        float aa = Vector3.Dot(a, a);
        float bb = Vector3.Dot(b, b);
        float bf = Vector3.Dot(b, offset);
        float s;
        float t;
        if (aa <= 0.000001f && bb <= 0.000001f)
            return offset.sqrMagnitude;
        if (aa <= 0.000001f)
        {
            s = 0f;
            t = Mathf.Clamp01(bf / bb);
        }
        else
        {
            float ac = Vector3.Dot(a, offset);
            if (bb <= 0.000001f)
            {
                t = 0f;
                s = Mathf.Clamp01(-ac / aa);
            }
            else
            {
                float ab = Vector3.Dot(a, b);
                float denominator = aa * bb - ab * ab;
                s = denominator > 0.000001f ? Mathf.Clamp01((ab * bf - ac * bb) / denominator) : 0f;
                t = (ab * s + bf) / bb;
                if (t < 0f)
                {
                    t = 0f;
                    s = Mathf.Clamp01(-ac / aa);
                }
                else if (t > 1f)
                {
                    t = 1f;
                    s = Mathf.Clamp01((ab - ac) / aa);
                }
            }
        }
        closestPlayerCenter = a0 + a * s;
        return (closestPlayerCenter - (b0 + b * t)).sqrMagnitude;
    }

    public override void Render()
    {
        if (!IsPrepared)
            return;
        BattleManager.Instance?.RegisterFlag(this);
        if (HasEnded)
            BattleManager.Instance?.NotifyBattleEnded(WinningTeam);
    }

    void IAfterRender.AfterRender()
    {
        if (!IsPrepared) return;
        // Carrier roots have finished NetworkTransform.Render before following them.
        // The flag has no NetworkTransform: interpolate its free-flight snapshots here,
        // instead of snapping directly to the latest received WorldPosition each frame.
        Player carrier = Carrier != PlayerRef.None ? GetCarrierPlayer() : null;
        Vector3 position = WorldPosition;
        if (carrier == null && Carrier == PlayerRef.None &&
            TryGetSnapshotsBuffers(out var from, out var to, out float alpha))
        {
            NetworkId fromCarrier = carrierIdReader.Read(from);
            NetworkId toCarrier = carrierIdReader.Read(to);
            // Never lerp across a pickup/drop or a different respawned carrier.
            if (fromCarrier.Equals(default(NetworkId)) && toCarrier.Equals(default(NetworkId)))
                position = Vector3.Lerp(worldPositionReader.Read(from), worldPositionReader.Read(to), alpha);
        }
        transform.position = carrier != null ? carrier.transform.TransformPoint(carryOffset) : position;
        if (carrier != null)
            transform.rotation = Quaternion.Euler(0f, carrier.transform.eulerAngles.y, 0f);
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        readyPlayers.Clear();
        previousPositions.Clear();
        if (Instance == this)
            Instance = null;
    }
}
