using UnityEngine;
using UnityEngine.SceneManagement;

using Fusion;
using Fusion.Sockets;

using System;
using System.Collections.Generic;
using TMPro;

public class NetworkGameManager : MonoBehaviour, INetworkRunnerCallbacks
{
    private static NetworkGameManager instance;
    public static NetworkGameManager Instance => instance;

    [Header("Scene")]
    [SerializeField] private int battleSceneIndex = 2;

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

    private bool isGameStarting = false;


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

        string roomId = roomIdInput.text;

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

        if (playerDatas.ContainsKey(player))
            return;

        runner.Spawn(
            playerDataPrefab,
            Vector3.zero,
            Quaternion.identity,
            player
        );
    }


    // PlayerData가 생성된 후 PlayerData.Spawned()에서 호출
    // 생성된 PlayerData를 Dictionary에 등록
    public void RegisterPlayerData(
        PlayerData data)
    {
        PlayerRef player =
            data.Object.InputAuthority;

        if (playerDatas.ContainsKey(player))
            return;

        playerDatas.Add(
            player,
            data
        );

        if (_runner != null &&
            _runner.IsServer)
        {
            CheckPlayerCount();
        }
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
            Debug.LogError(
                "BattleManager를 찾을 수 없습니다."
            );

            return;
        }

        battleManager.InitializeBattle(
            runner,
            playerDatas,
            playerPrefab,
            spawnedPlayers
        );
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