using System;
using System.Collections.Generic;
using Fusion;
using UnityEngine;

[RequireComponent(typeof(BattleIntroPresentation))]
[RequireComponent(typeof(BattleRoundUI))]
[RequireComponent(typeof(BattleKillFeed))]
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

    [Header("Best of three / between-round magic changes")]
    [Tooltip("라운드 사이 마법 교체 시간(초). 호스트 설정을 모든 클라이언트에 적용합니다.")]
    [SerializeField, Min(1f)] private float magicChangeDurationSeconds = 40f;
    [Tooltip("직전 라운드 장비 대비 변경 가능한 마법 슬롯 수입니다. 같은 슬롯은 제한 시간 내 다시 선택할 수 있습니다.")]
    [SerializeField, Range(1, 2)] private int maxMagicChangesPerRound = 2;

    [Header("Flag Match / Respawn")]
    [Tooltip("한 라운드 시간(초). 종료 시 마지막 깃발 소유자가 승리합니다. 2승 시 경기 종료.")]
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
    public int RoundNumber => HasValidFlag ? battleFlag.RoundNumber : 1;
    public int Team1Wins => HasValidFlag ? battleFlag.Team1Wins : 0;
    public int Team2Wins => HasValidFlag ? battleFlag.Team2Wins : 0;
    public int LastRoundWinner => HasValidFlag ? battleFlag.LastRoundWinner : 0;
    public int AllowedMagicChanges => HasValidFlag ? battleFlag.AllowedMagicChanges : maxMagicChangesPerRound;
    public bool IsOvertime => HasValidFlag && battleFlag.IsOvertime;
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
        flag.PrepareMatch(position, matchDurationSeconds, ExpectedPlayerCount, introDurationSeconds,
            magicChangeDurationSeconds, maxMagicChangesPerRound);
    }

    public void BeginMagicChange()
    {
        if (runner == null || !runner.IsServer || Phase != BattleStartPhase.Intermission) return;
        pendingRespawns.Clear();
        foreach (PlayerData data in playerDatas.Values)
            if (data != null && data.Object != null && data.Object.IsValid)
                data.BeginRoundMagicChange(RoundNumber);
    }

    public void ResetPlayersForNextRound()
    {
        if (runner == null || !runner.IsServer || Phase != BattleStartPhase.WaitingForPlayers) return;
        pendingRespawns.Clear();
        // Respawn, rather than reusing dead/faded instances: reset HP/MP, cooldowns,
        // physics, camera binding, cached portraits and all status effects together.
        var oldPlayers = new List<NetworkObject>(spawnedPlayers.Values);
        spawnedPlayers.Clear();
        foreach (NetworkObject old in oldPlayers)
            if (old != null && old.IsValid) runner.Despawn(old);
        SpawnBattlePlayers(playerPrefab);
    }

    public bool TryChangeRoundMagic(PlayerData data, int round, MagicType first, MagicType second, out string error)
    {
        error = "마법 교체 시간이 아닙니다.";
        if (runner == null || !runner.IsServer || !HasValidFlag || !battleFlag.CanChangeMagic(round) ||
            data == null || data.Object == null || !data.Object.IsValid || data.Runner != runner ||
            !data.Object.HasStateAuthority || !data.IsLoadoutInitialized || !IsConnected(data.Object.InputAuthority) ||
            !playerDatas.TryGetValue(data.Object.InputAuthority, out PlayerData registered) || registered != data ||
            data.MagicChangeRound != round) return false;
        if (!BattleRoundRules.IsAllowedLoadout(data.MagicChangeBase1, data.MagicChangeBase2,
            first, second, battleFlag.AllowedMagicChanges))
        {
            error = $"유효한 마법을 선택해주세요. 변경 가능한 슬롯은 최대 {battleFlag.AllowedMagicChanges}개입니다.";
            return false;
        }
        data.magic1 = first;
        data.magic2 = second;
        error = string.Empty;
        return true;
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
        playerDatas.TryGetValue(deadPlayer, out PlayerData victim);
        playerDatas.TryGetValue(killerPlayer, out PlayerData killer);
        battleFlag?.AnnounceKill(killer != null ? killer.playerName.ToString() : "환경 / 더미",
            killer != null ? killer.playerprofile : -1,
            victim != null ? victim.playerName.ToString() : $"Player {deadPlayer.PlayerId}",
            victim != null ? victim.playerprofile : -1);
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
        if (ExpectedPlayerCount > 1 && !IsBattleEnded)
            foreach (var pair in playerDatas)
                if (pair.Key != playerRef && pair.Value != null && IsConnected(pair.Key))
                {
                    battleFlag?.EndSeriesByForfeit(pair.Value.teamIndex);
                    break;
                }
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
