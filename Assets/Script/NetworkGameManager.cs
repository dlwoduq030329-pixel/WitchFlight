using UnityEngine;
using UnityEngine.SceneManagement;

using Fusion;
using Fusion.Sockets;

using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;

public class NetworkGameManager : MonoBehaviour, INetworkRunnerCallbacks
{
    private static NetworkGameManager instance;
    public static NetworkGameManager Instance => instance;

    [Header("Scene")]
    [SerializeField] private int battleSceneIndex = 1;

    [Header("Room")]
    [SerializeField] private TMP_InputField roomIdInput;
    [SerializeField] private int maxPlayerCount = 2;

    [Header("Network Prefabs")]
    [SerializeField] private NetworkPrefabRef playerPrefab;
    [SerializeField] private NetworkPrefabRef playerDataPrefab;

    private NetworkRunner _runner;

    // PlayerRef → 실제 전투 Player
    private Dictionary<PlayerRef, NetworkObject> spawnedPlayers
        = new Dictionary<PlayerRef, NetworkObject>();

    // PlayerRef → PlayerData
    private Dictionary<PlayerRef, PlayerData> playerDatas
        = new Dictionary<PlayerRef, PlayerData>();

    // PlayerData.Spawned()의 호출 순서와 무관하게 접속 순서 기준의 진영을 보관한다.
    // 첫 접속자는 A(1), 다음 접속자는 B(2)로 배정한다.
    private Dictionary<PlayerRef, int> assignedTeamIndexes
        = new Dictionary<PlayerRef, int>();

    private bool isGameStarting = false;
    private Coroutine battleInitializationRoutine;


    // 게임 시작 전, NetworkGameManager가 생성될 때 자동 호출
    private void Awake()
    {
        if (instance == null)
        {
            instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }


    // 게임 시작 / 매칭 버튼을 눌렀을 때 호출
    public void StartMatch()
    {
        if (_runner != null)
            return;

        string roomId = "";

        if (roomIdInput == null)
        {
            roomId = "textBuild";
        }
        else
        {
            roomId = roomIdInput.text;
        }

        if (!HasValidNetworkPrefab(playerDataPrefab, "PlayerData") ||
            !HasValidNetworkPrefab(playerPrefab, "Player"))
            return;

        if (battleSceneIndex < 0 || battleSceneIndex >= SceneManager.sceneCountInBuildSettings)
        {
            Debug.LogError($"Battle Scene Index({battleSceneIndex}) is not in Build Settings.");
            return;
        }


        if (string.IsNullOrWhiteSpace(roomId))
            return;

        StartGame(
            GameMode.AutoHostOrClient,
            roomId
        );
    }


    // StartMatch()에서 호출
    // Fusion 서버에 연결하고 RoomID에 해당하는 방 생성 또는 참가
    private async void StartGame(
        GameMode mode,
        string roomId)
    {
        _runner = gameObject.AddComponent<NetworkRunner>();

        _runner.ProvideInput = true;

        SceneRef scene =
            SceneRef.FromIndex(
                SceneManager.GetActiveScene().buildIndex
            );

        StartGameResult result =
            await _runner.StartGame(
                new StartGameArgs()
                {
                    GameMode = mode,

                    SessionName = roomId,

                    PlayerCount = maxPlayerCount,

                    Scene = scene,

                    SceneManager =
                        gameObject.AddComponent<
                            NetworkSceneManagerDefault>()
                }
            );

        if (!result.Ok)
        {
            Destroy(_runner);

            _runner = null;
        }
    }


    // 방에 플레이어가 접속했을 때 Fusion이 자동 호출
    // Host가 해당 플레이어의 PlayerData 생성
    public void OnPlayerJoined(
        NetworkRunner runner,
        PlayerRef player)
    {
        if (!runner.IsServer)
            return;

        if (playerDatas.ContainsKey(player) || assignedTeamIndexes.ContainsKey(player))
            return;

        // Spawned()가 비동기로 실행되므로, PlayerData를 등록하기 전에 진영을 예약한다.
        assignedTeamIndexes[player] = (assignedTeamIndexes.Count % 2) + 1;
NetworkObject playerDataObject = runner.Spawn(
            playerDataPrefab,
            Vector3.zero,
            Quaternion.identity,
            player
        );

        PlayerData playerData = playerDataObject != null
            ? playerDataObject.GetComponent<PlayerData>()
            : null;

        if (playerData == null)
        {
            assignedTeamIndexes.Remove(player);
            Debug.LogError($"Failed to create PlayerData for {player}.");
            return;
        }
// Spawn 직후 호스트가 먼저 등록한다. PlayerData.Spawned/RPC 순서에 의존하지 않는다.
        RegisterPlayerData(playerData);

        // PlayerData is match-lifetime data, not a lobby-scene object.
        // Keep it out of the scene that Fusion unloads during LoadScene.
        if (playerDataObject != null)
            runner.MakeDontDestroyOnLoad(playerDataObject.gameObject);
    }


    // PlayerData.Spawned()의 State Authority 구간에서 호출한다.
    // 이 시점은 Fusion 네트워크 상태값을 안전하게 기록할 수 있다.
    public void AssignTeamIndex(PlayerData data)
    {
        if (_runner == null || !_runner.IsServer || data == null || data.teamIndex != 0)
            return;

        PlayerRef player = data.Object.InputAuthority;
        if (!assignedTeamIndexes.TryGetValue(player, out int teamIndex))
        {
            // 복구 경로: 기존 PlayerData가 이미 존재하는 경우에도 일관된 값을 만든다.
            teamIndex = (assignedTeamIndexes.Count % 2) + 1;
            assignedTeamIndexes[player] = teamIndex;
        }

        data.teamIndex = teamIndex;
    }
    // PlayerData가 생성된 후 PlayerData.Spawned()에서 호출
    // 생성된 PlayerData를 Dictionary에 등록
    public void RegisterPlayerData(
        PlayerData data)
    {
        // PlayerData Dictionary는 전투 Player를 생성하는 호스트만 관리한다.
        if (_runner == null || !_runner.IsServer || data == null || data.Object == null)
            return;

        PlayerRef player = data.Object.InputAuthority;
        playerDatas[player] = data;
    }

    public void NotifyPlayerDataInitialized(PlayerData data)
    {
        if (_runner == null || !_runner.IsServer || data == null)
            return;

        RegisterPlayerData(data);
        CheckPlayerCount();
    }


    // PlayerData가 제거될 때 PlayerData.Despawned()에서 호출
    public void UnregisterPlayerData(
        PlayerData data)
    {
        if (data == null)
            return;

        PlayerRef player =
            data.Object.InputAuthority;

        if (playerDatas.ContainsKey(player))
        {
            playerDatas.Remove(player);
        }

        assignedTeamIndexes.Remove(player);
    }


    // PlayerData 등록 후 호출
    // 현재 플레이어가 최대 인원인지 확인
    private void CheckPlayerCount()
    {
        if (!_runner.IsServer)
            return;

        if (isGameStarting)
            return;

        if (playerDatas.Count < maxPlayerCount)
            return;

        foreach (PlayerData data in playerDatas.Values)
        {
            if (data == null || !data.IsLoadoutInitialized)
                return;
        }

        isGameStarting = true;

        LoadBattleScene();
    }


    // 플레이어 인원이 최대 인원에 도달했을 때 호출
    // 모든 플레이어를 전투 씬으로 이동
    private void LoadBattleScene()
    {
        if (!_runner.IsServer)
            return;

        _runner.LoadScene(
            SceneRef.FromIndex(
                battleSceneIndex
            )
        );
    }


    // 네트워크를 통한 씬 로딩이 완료되면 Fusion이 자동 호출
    // Host가 BattleManager를 찾아 전투 초기화 시작
    public void OnSceneLoadDone(
        NetworkRunner runner)
    {
        if (!runner.IsServer)
            return;

        int sceneIndex =
            SceneManager.GetActiveScene().buildIndex;

        if (sceneIndex != battleSceneIndex)
            return;

        BattleManager battleManager =
            FindFirstObjectByType<BattleManager>();

        if (battleManager == null)
        {
            GameObject battleManagerObject = new GameObject("BattleManager");
            battleManager = battleManagerObject.AddComponent<BattleManager>();
            Debug.LogWarning("Battle scene had no BattleManager. A runtime BattleManager was created.");
        }

        if (battleInitializationRoutine == null)
        {
            battleInitializationRoutine = StartCoroutine(
                InitializeBattleWhenPlayerDataIsReady(
                    battleManager,
                    runner
                )
            );
        }
    }


    private IEnumerator InitializeBattleWhenPlayerDataIsReady(
        BattleManager battleManager,
        NetworkRunner runner)
    {
        // PlayerData의 Spawned 및 Loadout RPC가 반영될 때까지 BattleManager 초기화를 미룬다.
        while (!AreBattlePlayerDatasReady())
            yield return null;

        battleManager.InitializeBattle(
            runner,
            playerDatas,
            playerPrefab,
            spawnedPlayers
        );

        battleInitializationRoutine = null;
    }

    private bool AreBattlePlayerDatasReady()
    {
        if (playerDatas.Count < maxPlayerCount)
            return false;

        foreach (PlayerData data in playerDatas.Values)
        {
            if (data == null || data.Object == null || !data.IsLoadoutInitialized ||
                (data.teamIndex != 1 && data.teamIndex != 2))
                return false;
        }

        return true;
    }
    // 다른 스크립트에서 특정 플레이어의 PlayerData가 필요할 때 호출
    public PlayerData GetPlayerData(
        PlayerRef player)
    {
        if (playerDatas.TryGetValue(
            player,
            out PlayerData data))
        {
            return data;
        }

        return null;
    }


    // 다른 스크립트에서 특정 플레이어의 실제 Player 오브젝트가 필요할 때 호출
    public NetworkObject GetPlayerObject(
        PlayerRef player)
    {
        if (spawnedPlayers.TryGetValue(
            player,
            out NetworkObject playerObject))
        {
            return playerObject;
        }

        return null;
    }


    // 게임 중 플레이어가 방을 나가거나 연결이 끊겼을 때 Fusion이 자동 호출
    public void OnPlayerLeft(
        NetworkRunner runner,
        PlayerRef player)
    {
        if (!runner.IsServer)
            return;

        if (spawnedPlayers.TryGetValue(
            player,
            out NetworkObject playerObject))
        {
            runner.Despawn(playerObject);

            spawnedPlayers.Remove(player);
        }

        if (playerDatas.ContainsKey(player))
        {
            playerDatas.Remove(player);
        }

        assignedTeamIndexes.Remove(player);
    }


    // 게임 실행 중 매 네트워크 Tick마다 Fusion이 자동 호출
    // 현재 키보드와 마우스 입력을 NetworkInputData에 저장
    public void OnInput(
        NetworkRunner runner,
        NetworkInput input)
    {
        NetworkInputData data =
            new NetworkInputData();

        data.accelerate =
            Input.GetKey(KeyCode.W);

        data.decelerate =
            Input.GetKey(KeyCode.S);

        data.turnLeft =
            Input.GetKey(KeyCode.A);

        data.turnRight =
            Input.GetKey(KeyCode.D);

        data.mouseY =
            Input.GetAxisRaw("Mouse Y");

        input.Set(data);
    }


    // 해당 Tick에 플레이어 입력을 받지 못했을 때 Fusion이 자동 호출
    public void OnInputMissing(
        NetworkRunner runner,
        PlayerRef player,
        NetworkInput input)
    {
    }


    // NetworkRunner가 종료될 때 Fusion이 자동 호출
    public void OnShutdown(
        NetworkRunner runner,
        ShutdownReason shutdownReason)
    {
        _runner = null;
        isGameStarting = false;
        spawnedPlayers.Clear();
        playerDatas.Clear();
        assignedTeamIndexes.Clear();
    }

    private bool HasValidNetworkPrefab(NetworkPrefabRef prefab, string name)
    {
        if (prefab.IsValid)
            return true;

        Debug.LogError($"NetworkGameManager {name} Network Prefab is not assigned.");
        return false;
    }


    // Fusion 서버 연결이 완료되었을 때 자동 호출
    public void OnConnectedToServer(
        NetworkRunner runner)
    {
    }


    // Fusion 서버와 연결이 끊겼을 때 자동 호출
    public void OnDisconnectedFromServer(
        NetworkRunner runner,
        NetDisconnectReason reason)
    {
    }


    // 다른 플레이어가 서버에 연결을 요청했을 때 자동 호출
    public void OnConnectRequest(
        NetworkRunner runner,
        NetworkRunnerCallbackArgs.ConnectRequest request,
        byte[] token)
    {
    }


    // 서버 연결에 실패했을 때 자동 호출
    public void OnConnectFailed(
        NetworkRunner runner,
        NetAddress remoteAddress,
        NetConnectFailedReason reason)
    {
    }


    // UserSimulationMessage를 받았을 때 자동 호출
    public void OnUserSimulationMessage(
        NetworkRunner runner,
        SimulationMessagePtr message)
    {
    }


    // 세션 목록이 변경되었을 때 자동 호출
    public void OnSessionListUpdated(
        NetworkRunner runner,
        List<SessionInfo> sessionList)
    {
    }


    // Custom Authentication 응답을 받았을 때 자동 호출
    public void OnCustomAuthenticationResponse(
        NetworkRunner runner,
        Dictionary<string, object> data)
    {
    }


    // 현재 Host가 나가서 Host Migration이 발생했을 때 자동 호출
    public void OnHostMigration(
        NetworkRunner runner,
        HostMigrationToken hostMigrationToken)
    {
    }


    // 네트워크 씬 로딩이 시작될 때 자동 호출
    public void OnSceneLoadStart(
        NetworkRunner runner)
    {
    }


    // 특정 NetworkObject가 플레이어의 AOI 밖으로 나갔을 때 자동 호출
    public void OnObjectExitAOI(
        NetworkRunner runner,
        NetworkObject obj,
        PlayerRef player)
    {
    }


    // 특정 NetworkObject가 플레이어의 AOI 안으로 들어왔을 때 자동 호출
    public void OnObjectEnterAOI(
        NetworkRunner runner,
        NetworkObject obj,
        PlayerRef player)
    {
    }


    // Reliable Data를 정상적으로 받았을 때 자동 호출
    public void OnReliableDataReceived(
        NetworkRunner runner,
        PlayerRef player,
        ReliableKey key,
        ArraySegment<byte> data)
    {
    }


    // Reliable Data를 받는 진행률이 변경될 때 자동 호출
    public void OnReliableDataProgress(
        NetworkRunner runner,
        PlayerRef player,
        ReliableKey key,
        float progress)
    {
    }
}
