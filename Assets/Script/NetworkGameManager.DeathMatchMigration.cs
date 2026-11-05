using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Fusion;
using UnityEngine;
using UnityEngine.SceneManagement;

public partial class NetworkGameManager
{
    [SerializeField, Min(1f)] private float deathMatchSnapshotSeconds = 2f;
    public bool IsMigratingDeathMatch { get; private set; }
    private byte[] deathMatchConnectionToken;
    private bool migrationSnapshotBusy, migrationRestoring;
    private float nextMigrationSnapshot, migrationReservationDeadline;
    private readonly Dictionary<string, NetworkObject> migrationData = new();
    private readonly Dictionary<string, NetworkObject> migrationCharacters = new();
    private readonly HashSet<PlayerRef> migrationParticipants = new();

    private byte[] DeathMatchConnectionToken() => deathMatchConnectionToken ??= Guid.NewGuid().ToByteArray();
    private string DeathMatchIdentity(NetworkRunner runner, PlayerRef player)
    {
        byte[] token = player == runner.LocalPlayer ? DeathMatchConnectionToken() : runner.GetPlayerConnectionToken(player);
        return token != null && token.Length == 16 ? new Guid(token).ToString("N") : "";
    }

    public bool IsRestoredDeathMatchPlayer(PlayerRef player) => IsDeathMatch && migrationParticipants.Contains(player);

    private void TickDeathMatchMigration()
    {
        if (!IsDeathMatch || IsMigratingDeathMatch || !IsRoomHost || !IsBattleSceneLoaded || stopInProgress) return;
        if (Time.unscaledTime >= nextMigrationSnapshot && !migrationSnapshotBusy)
        {
            nextMigrationSnapshot = Time.unscaledTime + Mathf.Max(1f, deathMatchSnapshotSeconds);
            _ = PushDeathMatchSnapshot(_runner);
        }
        if (migrationData.Count > 0 && Time.unscaledTime >= migrationReservationDeadline)
        {
            foreach (var obj in migrationCharacters.Values) if (obj != null && obj.IsValid) _runner.Despawn(obj);
            foreach (var obj in migrationData.Values) if (obj != null && obj.IsValid) _runner.Despawn(obj);
            migrationCharacters.Clear(); migrationData.Clear();
        }
    }

    private async Task PushDeathMatchSnapshot(NetworkRunner runner)
    {
        if (migrationSnapshotBusy || runner == null || !runner.IsRunning || !runner.IsServer) return;
        migrationSnapshotBusy = true;
        try
        {
            bool saved = await runner.PushHostMigrationSnapshot();
            if (!saved && this != null && !IsMigratingDeathMatch)
                Debug.LogWarning("[DeathMatch/Migration] 호스트 복구 스냅샷 전송 실패. 다음 주기에 재시도합니다.", this);
        }
        catch (Exception error) { if (this != null) Debug.LogWarning($"[DeathMatch/Migration] {error.Message}", this); }
        finally { migrationSnapshotBusy = false; }
    }

    private async void MigrateDeathMatch(NetworkRunner previous, HostMigrationToken token)
    {
        if (!IsDeathMatch || IsMigratingDeathMatch || stopInProgress || cancelRequested || previous != _runner) return;
        IsMigratingDeathMatch = true;
        startInProgress = true;
        SetMatchStatus("호스트를 이전하는 중...");
        Debug.Log($"[DeathMatch/Migration] 시작: 새 역할 {token.GameMode}", this);
        Scene oldScene = SceneManager.GetSceneByBuildIndex(battleSceneIndex);
        Scene holdingScene = SceneManager.CreateScene("DeathMatch Host Transfer");
        SceneManager.SetActiveScene(holdingScene);
        try
        {
            if (battleInitializationRoutine != null) StopCoroutine(battleInitializationRoutine);
            battleInitializationRoutine = null;
            await previous.Shutdown(destroyGameObject: false, shutdownReason: ShutdownReason.HostMigration);
            if (oldScene.IsValid() && oldScene.isLoaded)
            {
                var unload = SceneManager.UnloadSceneAsync(oldScene);
                if (unload != null) await unload;
            }
            if (runnerObject != null) Destroy(runnerObject);
            spawnedPlayers.Clear(); playerDatas.Clear(); assignedTeamIndexes.Clear();
            migrationData.Clear(); migrationCharacters.Clear(); migrationParticipants.Clear();
            inputPlayer = null; inputTargeting = null; inputCamera = null; inputCameraFollow = null;
            accumulatedLook = Vector2.zero; latchedButtons = default;
            initialSceneLoaded = IsBattleSceneLoaded = false;
            migrationRestoring = true;
            runnerObject = new GameObject("WitchFlight Migrated Deathmatch");
            runnerObject.transform.SetParent(transform, false);
            var resumed = runnerObject.AddComponent<NetworkRunner>();
            _runner = resumed;
            resumed.AddCallbacks(this);
            resumed.ProvideInput = true;
            var scenes = runnerObject.AddComponent<BattleNetworkSceneManager>();
            var pool = runnerObject.AddComponent<MagicNetworkObjectPool>();
            var result = await resumed.StartGame(new StartGameArgs
            {
                GameMode = token.GameMode, HostMigrationToken = token, HostMigrationResume = ResumeDeathMatch,
                ConnectionToken = DeathMatchConnectionToken(), CustomPhotonAppSettings = DeathMatchPhotonSettings(),
                PlayerCount = DeathMatchCapacity, Scene = SceneRef.FromIndex(battleSceneIndex),
                SceneManager = scenes, ObjectProvider = pool, IsOpen = true, IsVisible = false
            });
            if (!result.Ok) throw new InvalidOperationException(result.ShutdownReason.ToString());
            migrationRestoring = false;
            IsMigratingDeathMatch = false;
            startInProgress = false;
            if (resumed.IsServer)
                foreach (var player in resumed.ActivePlayers) OnPlayerJoined(resumed, player);
            OnSceneLoadDone(resumed);
            migrationReservationDeadline = Time.unscaledTime + 30f;
            nextMigrationSnapshot = Time.unscaledTime + 1f;
            if (holdingScene.IsValid() && holdingScene.isLoaded) await SceneManager.UnloadSceneAsync(holdingScene);
            SetMatchStatus("데스매치 · 호스트 이전 완료");
            Debug.Log("[DeathMatch/Migration] 완료. 경기 기록과 참가자 권한을 복구했습니다.", this);
        }
        catch (Exception error)
        {
            Debug.LogError($"[DeathMatch/Migration] 복구 실패: {error}", this);
            IsMigratingDeathMatch = migrationRestoring = startInProgress = false;
            await ReturnToMainMenuAsync();
        }
    }

    private void ResumeDeathMatch(NetworkRunner runner)
    {
        var identities = new HashSet<string>();
        foreach (var snapshot in runner.GetResumeSnapshotNetworkObjects())
            if (snapshot.TryGetComponent<PlayerData>(out var data) && !data.IsRoomOwner)
                identities.Add(data.DeathMatchIdentity.ToString());
        foreach (var snapshot in runner.GetResumeSnapshotNetworkObjects())
        {
            if (snapshot.TryGetComponent<PlayerData>(out var data))
            {
                if (data.IsRoomOwner) continue; // The departed host must not remain as a ghost participant.
                string id = data.DeathMatchIdentity.ToString();
                if (string.IsNullOrEmpty(id)) continue;
                var restored = runner.Spawn(snapshot, inputAuthority: PlayerRef.None,
                    onBeforeSpawned: (r, obj) => { obj.CopyStateFrom(snapshot); obj.AssignInputAuthority(PlayerRef.None); });
                migrationData[id] = restored;
                runner.MakeDontDestroyOnLoad(restored.gameObject);
                deathMatchJoinSequence = Mathf.Max(deathMatchJoinSequence, data.DeathMatchJoinOrder);
            }
            else if (snapshot.TryGetComponent<Player>(out var pilot) && identities.Contains(pilot.DeathMatchMigrationIdentity.ToString()))
            {
                string id = pilot.DeathMatchMigrationIdentity.ToString();
                bool pose = snapshot.TryGetBehaviour<NetworkTRSP>(out var trsp);
                var restored = runner.Spawn(snapshot, pose ? trsp.Data.Position : snapshot.transform.position,
                    pose ? trsp.Data.Rotation : snapshot.transform.rotation, PlayerRef.None,
                    onBeforeSpawned: (r, obj) => { obj.CopyStateFrom(snapshot); obj.AssignInputAuthority(PlayerRef.None); });
                restored.GetComponent<Player>().ResetDeathMatchMigrationTargets();
                migrationCharacters[id] = restored;
            }
            else if (snapshot.TryGetComponent<DeathMatchState>(out _))
                runner.Spawn(snapshot, onBeforeSpawned: (r, obj) => obj.CopyStateFrom(snapshot));
            // In-flight projectiles are not restored: their old NetworkIds/targets are no longer valid.
        }
        foreach (var sceneObject in runner.GetResumeSnapshotNetworkSceneObjects())
            sceneObject.Item1.CopyStateFrom(sceneObject.Item2);
        migrationRestoring = false;
        foreach (var player in runner.ActivePlayers) OnPlayerJoined(runner, player);
    }

    private bool TryRestoreDeathMatchParticipant(NetworkRunner runner, PlayerRef participant)
    {
        if (!IsDeathMatch) return false;
        string id = DeathMatchIdentity(runner, participant);
        if (string.IsNullOrEmpty(id) || !migrationData.TryGetValue(id, out var obj) || obj == null || !obj.IsValid) return false;
        migrationData.Remove(id);
        var data = obj.GetComponent<PlayerData>();
        obj.AssignInputAuthority(participant);
        data.RestoreDeathMatchOwner(participant == runner.LocalPlayer);
        assignedTeamIndexes[participant] = data.teamIndex;
        playerDatas[participant] = data;
        migrationParticipants.Add(participant);
        if (migrationCharacters.TryGetValue(id, out var character))
        {
            migrationCharacters.Remove(id);
            if (character != null && character.IsValid)
            {
                character.AssignInputAuthority(participant);
                spawnedPlayers[participant] = character;
                runner.SetPlayerObject(participant, character);
            }
        }
        BattleManager.Instance?.RestoreDeathMatchRespawns();
        return true;
    }
}
