using System;
using System.Collections.Generic;
using Fusion;
using UnityEngine;

[RequireComponent(typeof(BattleIntroPresentation))]
public class BattleManager : MonoBehaviour
{
    [Header("Network Prefab")]
    [SerializeField] private NetworkPrefabRef flagPrefab;

    [Header("Battle Spawn Point")]
    [SerializeField] private Transform playerSpawnA;
    [SerializeField] private Transform playerSpawnB;
    [SerializeField] private Transform flagSpawnPoint;

    [Header("Map boundary")]
    [SerializeField] private MapBoundaryTable mapBoundaryTable;
    public MapBoundaryTable MapBoundary => mapBoundaryTable;

    [Header("Battle Start")]
    [Tooltip("All clients must prepare their scene and portraits before startGame is called automatically.")]
    [SerializeField] private bool autoStartWhenReady = true;
    [Tooltip("Face-versus-face presentation duration, before the shared three-second countdown.")]
    [SerializeField, Min(0.1f)] private float introDurationSeconds = 5f;

    [Header("Flag Match / Respawn")]
    [Tooltip("Match duration in seconds. The last flag holder wins when time expires.")]
    [SerializeField, Min(1f)] private float matchDurationSeconds = 180f;
    [SerializeField, Min(0f)] private float deathDespawnDelay = 2f;
    [Tooltip("Time measured from death, not from disappearance.")]
    [SerializeField, Min(0.1f)] private float respawnDelaySeconds = 7f;

    private sealed class PendingRespawn
    {
        public PlayerRef PlayerRef;
        public NetworkObject DeadObject;
        public TickTimer DespawnAt;
        public TickTimer RespawnAt;
    }

    private NetworkRunner runner;
    private Dictionary<PlayerRef, PlayerData> playerDatas;
    private Dictionary<PlayerRef, NetworkObject> spawnedPlayers;
    private NetworkPrefabRef playerPrefab;
    private readonly List<PendingRespawn> pendingRespawns = new();
    private BattleFlag battleFlag;
    private bool isInitialized;
    private bool resultAnnounced;
    private bool startRequestedLocally;
    private static BattleManager instance;

    public static BattleManager Instance => instance;
    private bool HasValidFlag => battleFlag != null && battleFlag.Object != null && battleFlag.Object.IsValid;
    public BattleStartPhase Phase => HasValidFlag ? battleFlag.Phase : BattleStartPhase.WaitingForPlayers;
    public bool IsGameplayActive => Phase == BattleStartPhase.Playing;
    public bool AutoStartWhenReady => autoStartWhenReady;
    public int ExpectedPlayerCount => HasValidFlag && battleFlag.ExpectedPlayerCount > 0
        ? battleFlag.ExpectedPlayerCount : NetworkGameManager.Instance != null
            ? NetworkGameManager.Instance.RequiredPlayerCount : 2;
    public float PhaseRemainingSeconds => HasValidFlag ? battleFlag.PhaseRemainingSeconds : 0f;
    public bool IsBattleEnded => battleFlag != null && battleFlag.Object != null &&
        battleFlag.Object.IsValid && battleFlag.HasEnded;
    public float RemainingSeconds => battleFlag != null ? battleFlag.RemainingSeconds : 0f;
    public int WinningTeam => IsBattleEnded ? battleFlag.WinningTeam : 0;
    public float RespawnDelaySeconds => respawnDelaySeconds;
    public event Action<int> BattleEnded;

    private void Awake()
    {
        if (mapBoundaryTable != null && !mapBoundaryTable.HasValidBounds)
            Debug.LogWarning("MapBoundaryTable needs finite xmin < xmax and zmin < zmax. Boundary return is disabled.", this);
        if (mapBoundaryTable != null && mapBoundaryTable.altitudeLimitEnabled && !mapBoundaryTable.HasValidAltitudeLimit)
            Debug.LogWarning("MapBoundaryTable needs a finite altitudeFadeStartY < ymax. Altitude death is disabled.", this);
        if (instance == null)
            instance = this;
        else
            Destroy(gameObject);
    }

    private void OnDrawGizmosSelected()
    {
        if (mapBoundaryTable == null || !mapBoundaryTable.HasValidBounds)
            return;
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireCube(new Vector3((mapBoundaryTable.xmin + mapBoundaryTable.xmax) * 0.5f,
            transform.position.y, (mapBoundaryTable.zmin + mapBoundaryTable.zmax) * 0.5f),
            new Vector3(mapBoundaryTable.xmax - mapBoundaryTable.xmin, 0f,
                mapBoundaryTable.zmax - mapBoundaryTable.zmin));
    }

    // UnityEvent/Button API. It requests the sequence, never bypasses the readiness barrier.
    public void startGame()
    {
        if (Phase != BattleStartPhase.WaitingForPlayers)
            return;
        startRequestedLocally = true;
        TrySendStartRequest();
    }

    public void StartGame() => startGame();

    private void Update()
    {
        if (startRequestedLocally)
            TrySendStartRequest();
    }

    private void TrySendStartRequest()
    {
        if (!HasValidFlag)
            return;
        if (battleFlag.Object.HasStateAuthority)
        {
            battleFlag.RequestStart();
            startRequestedLocally = false;
        }
        else if (Player.LocalPlayer != null && Player.LocalPlayer.IsPresentationReady)
        {
            Player.LocalPlayer.RequestBattleStart();
            startRequestedLocally = false;
        }
    }

    // NetworkGameManager calls this only on the host after PlayerData is ready.
    public void InitializeBattle(NetworkRunner runner,
        Dictionary<PlayerRef, PlayerData> playerDatas, NetworkPrefabRef playerPrefab,
        Dictionary<PlayerRef, NetworkObject> spawnedPlayers)
    {
        if (isInitialized || runner == null || !runner.IsServer)
            return;

        isInitialized = true;
        this.runner = runner;
        this.playerDatas = playerDatas;
        this.playerPrefab = playerPrefab;
        this.spawnedPlayers = spawnedPlayers;
        pendingRespawns.Clear();
        resultAnnounced = false;
        FindSpawnPoints();
        SpawnBattlePlayers(playerPrefab);
        SpawnFlag();
    }

    private void FindSpawnPoints()
    {
        if (playerSpawnA == null)
            playerSpawnA = FindSpawnPoint("PlayerSpawnA", "spawnA");
        if (playerSpawnB == null)
            playerSpawnB = FindSpawnPoint("PlayerSpawnB", "spawnB");
        if (flagSpawnPoint == null)
            flagSpawnPoint = FindSpawnPoint("FlagSpawn", "Flag");
    }

    private static Transform FindSpawnPoint(string preferredName, string sceneName)
    {
        GameObject found = GameObject.Find(preferredName) ?? GameObject.Find(sceneName);
        return found != null ? found.transform : null;
    }

    private void SpawnBattlePlayers(NetworkPrefabRef prefab)
    {
        foreach (KeyValuePair<PlayerRef, PlayerData> pair in playerDatas)
            SpawnBattlePlayer(pair.Key, pair.Value, prefab);
    }

    private void SpawnBattlePlayer(PlayerRef playerRef, PlayerData playerData, NetworkPrefabRef prefab)
    {
        if (playerData == null || !playerData.IsLoadoutInitialized || !IsConnected(playerRef))
            return;
        if (spawnedPlayers.TryGetValue(playerRef, out NetworkObject existing) &&
            existing != null && existing.IsValid)
            return;

        Transform spawn = playerData.teamIndex == 1 ? playerSpawnA : playerSpawnB;
        if (spawn == null || (playerData.teamIndex != 1 && playerData.teamIndex != 2))
        {
            Debug.LogError($"Missing spawn point for team {playerData.teamIndex}.", this);
            return;
        }

        NetworkObject playerObject = runner.Spawn(prefab, spawn.position, spawn.rotation, playerRef);
        if (playerObject == null)
            return;

        spawnedPlayers[playerRef] = playerObject;
        runner.SetPlayerObject(playerRef, playerObject);
        Player player = playerObject.GetComponent<Player>();
        if (player != null)
            player.InitPlayer(playerData);
    }

    private bool IsConnected(PlayerRef playerRef)
    {
        if (runner == null || !runner.IsRunning)
            return false;
        foreach (PlayerRef active in runner.ActivePlayers)
            if (active == playerRef)
                return true;
        return false;
    }

    private void SpawnFlag()
    {
        if (battleFlag != null || !flagPrefab.IsValid)
            return;
        Vector3 position = flagSpawnPoint != null ? flagSpawnPoint.position : Vector3.zero;
        Quaternion rotation = flagSpawnPoint != null ? flagSpawnPoint.rotation : Quaternion.identity;
        NetworkObject flagObject = runner.Spawn(flagPrefab, position, rotation);
        BattleFlag flag = flagObject != null ? flagObject.GetComponent<BattleFlag>() : null;
        if (flag == null)
        {
            Debug.LogError("FlagOBJ must have BattleFlag and be registered as a network prefab.", this);
            return;
        }
        RegisterFlag(flag);
        flag.PrepareMatch(position, matchDurationSeconds, ExpectedPlayerCount, introDurationSeconds);
    }

    // BattleFlag.Spawned also calls this on remote clients.
    public void RegisterFlag(BattleFlag flag)
    {
        battleFlag = flag;
    }

    public void PlayerKilled(PlayerRef deadPlayer, PlayerRef killerPlayer)
    {
        if (runner == null || !runner.IsServer || !IsGameplayActive || deadPlayer == PlayerRef.None)
            return;
        foreach (PendingRespawn pending in pendingRespawns)
            if (pending.PlayerRef == deadPlayer)
                return;
        if (!spawnedPlayers.TryGetValue(deadPlayer, out NetworkObject deadObject) || deadObject == null)
            return;

        Player deadPilot = deadObject.GetComponent<Player>();
        battleFlag?.DropCarrier(deadPlayer, deadObject.transform.position,
            deadPilot != null && deadPilot.DiedFromAltitude);
        pendingRespawns.Add(new PendingRespawn
        {
            PlayerRef = deadPlayer,
            DeadObject = deadObject,
            DespawnAt = TickTimer.CreateFromSeconds(runner, Mathf.Min(deathDespawnDelay, respawnDelaySeconds)),
            RespawnAt = TickTimer.CreateFromSeconds(runner, respawnDelaySeconds)
        });
    }

    public void PlayerLeft(PlayerRef playerRef)
    {
        if (runner == null || !runner.IsServer)
            return;
        battleFlag?.ForgetReadyPlayer(playerRef);
        if (spawnedPlayers.TryGetValue(playerRef, out NetworkObject playerObject) && playerObject != null)
            battleFlag?.DropCarrier(playerRef, playerObject.transform.position);
        pendingRespawns.RemoveAll(pending => pending.PlayerRef == playerRef);
    }

    // Called from the authoritative flag's network tick: no scene-lifetime coroutines.
    public void TickRespawns()
    {
        if (runner == null || !runner.IsRunning || !runner.IsServer || !IsGameplayActive)
            return;
        for (int index = pendingRespawns.Count - 1; index >= 0; index--)
        {
            PendingRespawn pending = pendingRespawns[index];
            if (!IsConnected(pending.PlayerRef))
            {
                pendingRespawns.RemoveAt(index);
                continue;
            }
            if (pending.DespawnAt.Expired(runner) && pending.DeadObject != null)
            {
                if (pending.DeadObject.IsValid)
                    runner.Despawn(pending.DeadObject);
                if (spawnedPlayers.TryGetValue(pending.PlayerRef, out NetworkObject registered) &&
                    registered == pending.DeadObject)
                    spawnedPlayers.Remove(pending.PlayerRef);
                pending.DeadObject = null;
            }
            if (!pending.RespawnAt.Expired(runner))
                continue;
            if (playerDatas.TryGetValue(pending.PlayerRef, out PlayerData playerData))
                SpawnBattlePlayer(pending.PlayerRef, playerData, playerPrefab);
            pendingRespawns.RemoveAt(index);
        }
    }

    public void NotifyBattleEnded(int winningTeam)
    {
        if (resultAnnounced)
            return;
        resultAnnounced = true;
        pendingRespawns.Clear();
        Debug.Log(winningTeam > 0 ? $"Flag match ended. Team {winningTeam} wins." : "Flag match ended in a draw.");
        BattleEnded?.Invoke(winningTeam);
    }

    private void OnDestroy()
    {
        pendingRespawns.Clear();
        if (instance == this)
            instance = null;
    }
}
