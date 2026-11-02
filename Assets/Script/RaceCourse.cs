using System;
using System.Collections.Generic;
using Fusion;
using UnityEngine;

// Per-player progress lives on PlayerData, so death/respawn cannot erase a checkpoint or vote.
public sealed class RaceCourse : MonoBehaviour
{
    public static RaceCourse Instance { get; private set; }
    [SerializeField] private RaceRing[] rings;
    [SerializeField, Min(1)] private float countdownSeconds = 3;
    [SerializeField, Range(0, .5f)] private float botMissChance = .12f;
    [SerializeField, Min(1)] private float botRecoverySeconds = 4;
    private readonly List<PlayerData> racers = new();
    private readonly List<PlayerData> standings = new();
    private readonly Dictionary<PlayerRef, Sample> samples = new();
    private int tick = -1, highlighted = -2;
    private float nextReady, nextRoster;
    private PlayerData clock;
    private sealed class Sample { public NetworkId body; public Vector3 position; public int portal; public bool alive; }
    public int RingCount => rings != null ? rings.Length : 0;
    public IReadOnlyList<RaceRing> OrderedRings => rings;
    public float BotRecoverySeconds => botRecoverySeconds;
    public bool Finished => clock != null && clock.Object != null && clock.Object.IsValid && clock.RaceClosed;
    public BattleStartPhase Phase => Finished ? BattleStartPhase.Ended :
        clock == null || !clock.RaceStarted ? BattleStartPhase.WaitingForPlayers :
        !clock.RaceCountdown.Expired(clock.Runner) ? BattleStartPhase.Countdown : BattleStartPhase.Playing;
    public float CountdownRemaining => clock != null ? clock.RaceCountdown.RemainingTime(clock.Runner) ?? 0 : 0;
    public float ElapsedSeconds => clock != null && clock.RaceStarted ? Mathf.Max(0, (float)(clock.Runner.SimulationTime - clock.RaceStartTime)) : 0;
    public float FinishSecondsRemaining => clock != null ? clock.RaceFinishWindow.RemainingTime(clock.Runner) ?? -1 : -1;

    private void Awake()
    {
        Instance = this;
        Reindex();
    }

    [ContextMenu("Reindex rings in Hierarchy order")]
    public void Reindex()
    {
        var discovered = new List<RaceRing>();
        foreach (var root in gameObject.scene.GetRootGameObjects())
            discovered.AddRange(root.GetComponentsInChildren<RaceRing>(true));
        rings = discovered.ToArray();
        Array.Sort(rings, CompareHierarchy);
        for (int i = 0; i < rings.Length; i++) if (rings[i] != null) rings[i].SetIndex(i + 1);
    }

    private static int CompareHierarchy(RaceRing a, RaceRing b)
    {
        if (a == b) return 0;
        if (a == null) return 1;
        if (b == null) return -1;
        var first = new List<int>();
        var second = new List<int>();
        for (Transform t = a.transform; t != null; t = t.parent) first.Add(t.GetSiblingIndex());
        for (Transform t = b.transform; t != null; t = t.parent) second.Add(t.GetSiblingIndex());
        first.Reverse(); second.Reverse();
        for (int i = 0; i < Mathf.Min(first.Count, second.Count); i++)
            if (first[i] != second[i]) return first[i].CompareTo(second[i]);
        return first.Count.CompareTo(second.Count);
    }

    private void Start()
    {
        foreach (var ring in rings) if (ring != null) ring.SetTarget(false);
        Highlight(0);
    }

    private void RefreshRoster()
    {
        racers.Clear();
        var runner = NetworkGameManager.Instance != null ? NetworkGameManager.Instance.Runner : null;
        foreach (var data in PlayerData.PerkPlayers)
            if (data != null && data.Object != null && data.Object.IsValid && data.Runner == runner && data.IsLoadoutInitialized)
                racers.Add(data);
        racers.Sort((a, b) => a.ParticipantRef.RawEncoded.CompareTo(b.ParticipantRef.RawEncoded));
        clock = racers.Count > 0 ? racers[0] : null;
    }

    private void Update()
    {
        if (Time.unscaledTime >= nextRoster) { nextRoster = Time.unscaledTime + .5f; RefreshRoster(); }
        PlayerData local = PlayerData.Local;
        Player player = Player.LocalPlayer;
        if (local != null && local.Object != null && local.Object.IsValid)
        {
            if (!local.RaceReady && player != null && player.IsPresentationReady && Time.unscaledTime >= nextReady)
            { nextReady = Time.unscaledTime + .5f; local.RPC_RaceReady(); }
            Highlight(local.RaceRank > 0 || local.RaceClosed ? -1 : local.RacePassed % Mathf.Max(1, RingCount));
        }
    }

    private void Highlight(int index)
    {
        if (highlighted == index) return;
        if (highlighted >= 0 && highlighted < RingCount && rings[highlighted] != null) rings[highlighted].SetTarget(false);
        highlighted = index;
        if (index >= 0 && index < RingCount && rings[index] != null) rings[index].SetTarget(true);
    }

    public RaceRing NextRing(PlayerData data) => RingCount > 0 && data != null && data.RaceRank == 0 ? rings[data.RacePassed % RingCount] : null;

    public float MissChanceFor(PlayerData data)
    {
        int leader = data.RacePassed;
        foreach (var racer in racers) leader = Mathf.Max(leader, racer.RacePassed);
        // Gentle recovery assistance: trailing bots make fewer mistakes, but still fly through every gate.
        return botMissChance * (leader - data.RacePassed >= 2 ? .5f : 1f);
    }

    public void TickHost(Player player, PlayerData data)
    {
        if (data == null || !player.Object.HasStateAuthority || RingCount == 0) return;
        NetworkRunner activeRunner = player.Runner;
        if (tick != activeRunner.Tick.Raw)
        {
            tick = activeRunner.Tick.Raw;
            RefreshRoster();
            if (clock == null || Finished) return;
            if (!clock.RaceStarted)
            {
                int expected = NetworkGameManager.Instance.RequiredPlayerCount;
                bool ready = racers.Count == expected;
                foreach (var racer in racers) ready &= racer.RaceReady;
                if (ready) foreach (var racer in racers) racer.StartRace(countdownSeconds);
            }
            if (Phase == BattleStartPhase.Playing)
            {
                BattleManager.Instance?.TickRespawns();
                bool allFinished = racers.Count > 0;
                foreach (var racer in racers) allFinished &= racer.RaceRank > 0;
                if (allFinished || clock.RaceFinishWindow.Expired(activeRunner)) CloseRace();
            }
        }
        if (player == null || player.Object == null || !player.Object.IsValid) return;
        Vector3 now = player.transform.position;
        if (!samples.TryGetValue(data.ParticipantRef, out Sample previous))
        { previous = new Sample(); samples.Add(data.ParticipantRef, previous); }
        bool continuous = previous.body == player.Object.Id && previous.alive && player.IsAlive && previous.portal == player.PortalExitSequence;
        Vector3 from = previous.position;
        previous.position = now; previous.body = player.Object.Id; previous.alive = player.IsAlive; previous.portal = player.PortalExitSequence;
        if (Phase != BattleStartPhase.Playing || data.RaceRank > 0 || !continuous) return;
        // Check only ordered targets. Swept plane tests do not miss a ring at high speed.
        for (int i = 0; i < RingCount + 1; i++)
        {
            RaceRing ring = NextRing(data);
            if (ring == null || !ring.Crossed(from, now, out float t)) break;
            data.PassRaceRing();
            from = Vector3.Lerp(from, now, Mathf.Min(1f, t + .00001f));
            if (data.RacePassed < RingCount + 1) continue;
            int rank = 1;
            foreach (var racer in racers) if (racer.RaceRank > 0) rank++;
            data.SetRaceResult(rank, true);
            if (rank == 1) foreach (var racer in racers) racer.SetRaceFinishWindow();
            break;
        }
    }

    private void CloseRace()
    {
        standings.Clear(); standings.AddRange(racers);
        standings.Sort(CompareProgress);
        for (int i = 0; i < standings.Count; i++)
        {
            standings[i].SetRaceResult(i + 1, false);
            standings[i].CloseRace();
        }
    }

    private int CompareProgress(PlayerData a, PlayerData b)
    {
        if (a.RaceRank > 0 || b.RaceRank > 0)
            return (a.RaceRank > 0 ? a.RaceRank : int.MaxValue).CompareTo(b.RaceRank > 0 ? b.RaceRank : int.MaxValue);
        int passed = b.RacePassed.CompareTo(a.RacePassed);
        if (passed != 0) return passed;
        var pa = Player.FindInRunner(a.Runner, a.ParticipantRef);
        var pb = Player.FindInRunner(b.Runner, b.ParticipantRef);
        float da = pa != null ? Vector3.Distance(pa.transform.position, NextRing(a).Center) : float.PositiveInfinity;
        float db = pb != null ? Vector3.Distance(pb.transform.position, NextRing(b).Center) : float.PositiveInfinity;
        int distance = da.CompareTo(db);
        return distance != 0 ? distance : a.ParticipantRef.RawEncoded.CompareTo(b.ParticipantRef.RawEncoded);
    }

    public PlayerData AtRank(int rank)
    {
        foreach (var data in racers) if (data != null && data.RaceRank == rank) return data;
        return null;
    }

    public int CompetitorTeam(PlayerRef participant)
    {
        RefreshRoster();
        for (int i = 0; i < racers.Count; i++) if (racers[i].ParticipantRef == participant) return i + 1;
        return 1;
    }

    public void SpawnPose(PlayerRef participant, PlayerData data, out Vector3 position, out Quaternion rotation)
    {
        RaceRing ring = rings[0];
        Vector3 normal = ring.Normal;
        if (RingCount > 1 && Vector3.Dot(rings[1].Center - ring.Center, normal) < 0) normal = -normal;
        rotation = Quaternion.LookRotation(normal, Mathf.Abs(Vector3.Dot(normal, Vector3.up)) > .95f ? Vector3.forward : Vector3.up);
        int slot = 0;
        RefreshRoster();
        foreach (var racer in racers) { if (racer.ParticipantRef == participant) break; slot++; }
        if (data.RaceStarted && data.RacePassed > 0)
        {
            ring = rings[Mathf.Max(0, data.RacePassed - 1) % RingCount];
            RaceRing next = NextRing(data);
            if (next != null && (next.Center - ring.Center).sqrMagnitude > .01f) rotation = Quaternion.LookRotation(next.Center - ring.Center);
            position = ring.Center + rotation * Vector3.forward * 3f;
        }
        else position = ring.Center - normal * Mathf.Max(15, ring.Radius * 2) + rotation * Vector3.right * ((slot - 1.5f) * 2.5f);
    }

    private void OnDestroy() { if (Instance == this) Instance = null; }
}
