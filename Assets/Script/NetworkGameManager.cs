using UnityEngine;
using UnityEngine.SceneManagement;

using Fusion;
using Fusion.Sockets;

using System.Collections.Generic;
using System;
using static Unity.Collections.Unicode;
using System.Linq;


/*
 * 이 클래스는 게임 전체에서 네트워크와 관련된 일을 관리하는
 * "중앙 관리자" 역할을 한다.
 *
 * 주요 역할
 * 1. NetworkRunner 생성 및 실행
 * 2. Fusion Session(방) 생성 / 참가
 * 3. 플레이어 접속 / 퇴장 감지
 * 4. PlayerData 생성
 * 5. PlayerData를 PlayerRef와 연결하여 관리
 * 6. 네트워크 입력 전달
 * 7. 씬 전환 감지
 * 8. Fusion에서 제공하는 각종 네트워크 이벤트 수신
 * 이 객체는 DontDestroyOnLoad를 사용하기 때문에
 * 씬이 변경되어도 계속 존재한다.
 
 */
public class NetworkGameManager : MonoBehaviour, INetworkRunnerCallbacks
{
    private static NetworkGameManager instance;

    public static NetworkGameManager Instance => instance;



    private void Awake()
    {
        if (instance == null)
        {
            instance = this;
        }

        DontDestroyOnLoad(gameObject);
    }


    /*
     * ========================================================
     * NetworkRunner
     * ========================================================
     *
     * Fusion에서 실제 네트워크 시뮬레이션을 담당하는 핵심 객체.
     *
     * 쉽게 생각하면
     *
     * "이 게임의 Fusion 네트워크 엔진"
     *
     * 이라고 생각하면 된다.
     *
     * Session 연결
     * Player 관리
     * NetworkObject Spawn
     * 네트워크 입력
     * 씬 동기화
     *
     * 등의 중심이 된다.
     *
     * StartGame()을 호출할 때 생성된다.
     *
     * ========================================================
     */

    private NetworkRunner _runner;

    [SerializeField]
    private NetworkPrefabRef _playerPrefab;


    /*
     * PlayerData Prefab
     * --------------------------------------------------------
     *
     * 플레이어의 게임 설정 / 상태 정보를 저장하는
     * NetworkObject Prefab.
     *
     * 예:
     *
     * 모자
     * 빗자루
     * 마법 1
     * 마법 2
     * Ready 상태
     * Camp
     *
     * 등의 데이터를 저장하는 용도로 사용한다.
     *
     * 실제 전투 캐릭터와는 별개의 객체다.
     *
     * ========================================================
     */

    [SerializeField]
    private NetworkPrefabRef playerDataPrefab;


    /*
     * ========================================================
     * _spawnedCharacters
     * ========================================================
     *
     * 현재 네트워크에 참가한 플레이어와
     * 그 플레이어에게 생성된 NetworkObject를 연결하는 Dictionary.
     *
     *
     * Key
     * --------------------------------------------------------
     * PlayerRef
     *
     * Fusion이 각 플레이어에게 부여하는 네트워크상의 플레이어 ID.
     *
     *
     * Value
     * --------------------------------------------------------
     * NetworkObject
     *
     * 해당 플레이어에게 생성된 실제 네트워크 캐릭터.
     *
     *
     * 예:
     *
     * PlayerRef(1) → Player NetworkObject
     * PlayerRef(2) → Player NetworkObject
     *
     *
     * 현재 코드에서는 플레이어 캐릭터 Spawn 부분이 주석 처리되어
     * 있기 때문에 아직 실제로 추가되고 있지는 않다.
     *
     * ========================================================
     */

    private Dictionary<PlayerRef, NetworkObject> _spawnedCharacters
        = new Dictionary<PlayerRef, NetworkObject>();


    /*
     * ========================================================
     * playerDatas
     * ========================================================
     *
     * 각 플레이어의 PlayerRef와 PlayerData를 연결한다.
     *
     *
     * Key
     * --------------------------------------------------------
     * PlayerRef
     *
     * Fusion에서 해당 플레이어를 식별하는 ID.
     *
     *
     * Value
     * --------------------------------------------------------
     * PlayerData
     *
     * 해당 플레이어의 네트워크 데이터.
     *
     *
     * 예:
     *
     * PlayerRef(1)
     *     ↓
     * PlayerData
     *     ├─ Hat
     *     ├─ Broom
     *     ├─ Magic1
     *     ├─ Magic2
     *     └─ Ready
     *
     *
     * 이렇게 플레이어와 데이터를 연결해서 관리한다.
     *
     * ========================================================
     */

    private Dictionary<PlayerRef, PlayerData> playerDatas
        = new Dictionary<PlayerRef, PlayerData>();


    /*
     * ========================================================
     * StartGame()
     * ========================================================
     *
     * Fusion Session에 참가하거나 생성하는 함수.
     *
     * mode
     * --------------------------------------------------------
     * 게임 참가 방식을 결정한다.
     *
     * 예:
     *
     * GameMode.Host
     * GameMode.Client
     * GameMode.AutoHostOrClient
     *
     *
     * mapName
     * --------------------------------------------------------
     * Session 이름이다.
     *
     * 같은 SessionName을 사용하는 플레이어끼리
     * 같은 Session을 찾을 수 있다.
     *
     *
     * 현재 코드에서는
     *
     * StartGame(GameMode.AutoHostOrClient, "이재엽");
     *
     * 형태로 호출하고 있다.
     *
     * ========================================================
     */

    async void StartGame(GameMode mode, string mapName) //유저가 접속했을 경우 모든 유저에게 호출된다.
    {
        _runner = gameObject.AddComponent<NetworkRunner>();


        _runner.ProvideInput = true;


        
        var scene = SceneRef.FromIndex(
            SceneManager.GetActiveScene().buildIndex
        );


        var sceneInfo = new NetworkSceneInfo();
        if (scene.IsValid)
        {
            sceneInfo.AddSceneRef(
                scene,
                LoadSceneMode.Single
            );
        }

        await _runner.StartGame(new StartGameArgs()
        {

            GameMode = mode,

            SessionName = mapName,

            PlayerCount = 2,

            Scene = scene,

            SceneManager =
                gameObject.AddComponent<NetworkSceneManagerDefault>()
        });
    }



    /*
     * ========================================================
     * OnGUI()
     * ========================================================
     *
     * Unity의 GUI를 이용해 임시 테스트 버튼을 만드는 함수.
     *
     * 실제 게임 UI를 만드는 용도가 아니라
     * 현재 Fusion 연결을 테스트하기 위한 코드다.
     *
     * ========================================================
     */

    private void OnGUI()
    {
        /*
         * 아직 NetworkRunner가 생성되지 않았다면
         * GameStart 버튼을 표시한다.
         */

        if (_runner == null)
        {
            /*
             * GameStart 버튼 클릭
             *
             * AutoHostOrClient 방식으로
             * "이재엽"이라는 Session에 참가한다.
             */

            if (GUI.Button(
                new Rect(0, 0, 200, 40),
                "GameStart"))
            {
                StartGame(
                    GameMode.AutoHostOrClient,
                    "이재엽"
                );
            }
        }
    }



    /*
     * ========================================================
     * OnPlayerJoined()
     * ========================================================
     *
     * Fusion Session에 새로운 Player가 참가했을 때
     * 호출되는 콜백.
     *
     *
     * 아주 중요한 함수다.
     *
     *
     * 예:
     *
     * Host가 방에 들어옴
     *     ↓
     * OnPlayerJoined()
     *
     * Client가 들어옴
     *     ↓
     * OnPlayerJoined()
     *
     *
     * 여기서 중요한 것은
     *
     * "누가 접속했는가?"
     *
     * 를 PlayerRef player로 알려준다는 것이다.
     *
     * ========================================================
     */

    public void OnPlayerJoined(
        NetworkRunner runner,
        PlayerRef player) //내가 접속했을때만 X 접속하면 모든 유저에게 호출된다.
    {
        if (!runner.IsServer)
            return;

        if (playerDatas.ContainsKey(player)) //PlayerData 중복 생성 방지.
            return;


        /*
         * ====================================================
         * PlayerData NetworkObject Spawn
         * ====================================================
         *
         * playerDataPrefab을 네트워크 상에 생성한다.
         *
         *
         * 마지막 인자인 player는
         * Spawn되는 NetworkObject의 Input Authority를
         * 해당 플레이어에게 부여하는 역할을 한다.
         *
         *
         * 즉,
         *
         * runner.Spawn(
         *     playerDataPrefab,
         *     위치,
         *     회전,
         *     player
         * );
         *
         *
         * 마지막 player가
         *
         * "이 NetworkObject는 이 Player가 소유한다"
         *
         * 라는 의미가 된다.
         *
         * ====================================================
         */

        NetworkObject obj = runner.Spawn(
            playerDataPrefab,
            Vector3.zero,
            Quaternion.identity,
            player
        );


        /*
         * 방금 생성한 NetworkObject에서
         * PlayerData 컴포넌트를 가져온다.
         *
         * 현재 코드에서는 가져오기만 하고
         * 실제 Dictionary 등록은 하지 않는다.
         *
         * PlayerData의 Spawned()에서
         * RegisterPlayerData()를 호출하는 구조라면
         * 이 객체는 Spawned 이후 등록될 수 있다.
         */

        PlayerData data =
            obj.GetComponent<PlayerData>();


        /*
         * 현재는 직접 Dictionary에 넣지 않는다.
         *
         * 아래 방식 대신
         *
         * playerDatas.Add(player, data);
         *
         * PlayerData 쪽에서
         *
         * NetworkGameManager.Instance.RegisterPlayerData(this);
         *
         * 를 호출하는 구조를 사용할 수 있다.
         */

        //playerDatas.Add(player, data);

        if (runner.ActivePlayers.Count() >= 2)
        {
            runner.LoadScene(SceneRef.FromIndex(2));
        }
    }



    /*
     * ========================================================
     * RegisterPlayerData()
     * ========================================================
     *
     * PlayerData가 자신의 존재를
     * NetworkGameManager에게 등록할 때 사용하는 함수.
     *
     *
     * PlayerData가 NetworkBehaviour이고
     * Spawned()에서 이 함수를 호출한다고 생각하면 된다.
     *
     * ========================================================
     */

    public void RegisterPlayerData(PlayerData data)
    {
        /*
         * PlayerData가 가지고 있는 NetworkObject의
         * InputAuthority를 PlayerRef로 사용한다.
         *
         *
         * 결과:
         *
         * PlayerRef
         *      ↓
         * PlayerData
         *
         * 형태로 Dictionary에 저장된다.
         */

        playerDatas.Add(
            data.Object.InputAuthority,
            data
        );
    }



    /*
     * ========================================================
     * UnregisterPlayerData()
     * ========================================================
     *
     * PlayerData가 네트워크에서 제거될 때
     * Dictionary에서도 해당 데이터를 제거하기 위한 함수.
     *
     * ========================================================
     */

    public void UnregisterPlayerData(PlayerData data)
    {
        /*
         * 해당 PlayerData의 InputAuthority를 이용해서
         * Dictionary에서 삭제한다.
         */

        playerDatas.Remove(
            data.Object.InputAuthority
        );
    }



    /*
     * ========================================================
     * OnPlayerLeft()
     * ========================================================
     *
     * 플레이어가 Session에서 나갔을 때 호출되는 콜백.
     *
     * 예:
     *
     * Client가 게임을 종료
     *     ↓
     * OnPlayerLeft()
     *
     * ========================================================
     */

    public void OnPlayerLeft(
        NetworkRunner runner,
        PlayerRef player)
    {
        /*
         * 해당 PlayerRef로 생성된
         * NetworkObject를 Dictionary에서 찾는다.
         */

        if (_spawnedCharacters.TryGetValue(
            player,
            out NetworkObject networkObject))
        {
            /*
             * 해당 플레이어의 NetworkObject를
             * 네트워크에서 제거한다.
             */

            runner.Despawn(networkObject);


            /*
             * Dictionary에서도 제거한다.
             */

            _spawnedCharacters.Remove(player);
        }
    }



    /*
     * ========================================================
     * OnInput()
     * ========================================================
     *
     * Fusion이 로컬 플레이어의 입력을 요청할 때 호출된다.
     *
     * ProvideInput = true로 설정되어 있어야 의미가 있다.
     *
     *
     * 중요한 점:
     *
     * 여기서 입력을 "네트워크로 직접 보내는 것"이라기보다
     *
     * "현재 Tick에서 사용할 입력 데이터를 Fusion에게 제공"
     *
     * 한다고 이해하는 것이 좋다.
     *
     * ========================================================
     */

    public void OnInput(
        NetworkRunner runner,
        NetworkInput input)
    {
        /*
         * 네트워크로 전달할 입력 데이터를 생성한다.
         */

        NetworkInputData data =
            new NetworkInputData();


        /*
         * W
         * 가속 입력
         */

        data.accelerate =
            Input.GetKey(KeyCode.W);


        /*
         * S
         * 감속 입력
         */

        data.decelerate =
            Input.GetKey(KeyCode.S);


        /*
         * A
         * 좌회전 입력
         */

        data.turnLeft =
            Input.GetKey(KeyCode.A);


        /*
         * D
         * 우회전 입력
         */

        data.turnRight =
            Input.GetKey(KeyCode.D);


        /*
         * 마우스 Y축 움직임.
         *
         * GetAxisRaw를 사용하여
         * 마우스 움직임 값을 가져온다.
         */

        data.mouseY =
            Input.GetAxisRaw("Mouse Y");


        /*
         * 완성된 입력 데이터를 Fusion에 전달한다.
         *
         * 이후 NetworkBehaviour의
         * FixedUpdateNetwork()에서
         *
         * GetInput(out NetworkInputData data)
         *
         * 를 통해 이 데이터를 가져올 수 있다.
         */

        input.Set(data);
    }



    /*
     * ========================================================
     * OnInputMissing()
     * ========================================================
     *
     * Fusion이 특정 Player의 입력을 받지 못했을 때 호출될 수 있다.
     *
     * 현재는 아무런 처리를 하지 않는다.
     *
     * ========================================================
     */

    public void OnInputMissing(
        NetworkRunner runner,
        PlayerRef player,
        NetworkInput input)
    {
    }



    /*
     * ========================================================
     * OnShutdown()
     * ========================================================
     *
     * Fusion Runner가 종료되었을 때 호출된다.
     *
     * 예:
     *
     * Session 종료
     * 네트워크 연결 종료
     * 오류로 인한 Shutdown
     *
     * 등의 상황에서 호출된다.
     *
     * shutdownReason을 통해 종료 이유를 알 수 있다.
     *
     * ========================================================
     */

    public void OnShutdown(
        NetworkRunner runner,
        ShutdownReason shutdownReason)
    {
    }



    /*
     * ========================================================
     * OnConnectedToServer()
     * ========================================================
     *
     * Client가 Server/Host에 정상적으로 연결되었을 때
     * 호출된다.
     *
     * "네트워크 연결 성공"을 확인하는 용도로 사용할 수 있다.
     *
     * ========================================================
     */

    public void OnConnectedToServer(
        NetworkRunner runner)
    {
    }



    /*
     * ========================================================
     * OnDisconnectedFromServer()
     * ========================================================
     *
     * Server와의 연결이 끊겼을 때 호출된다.
     *
     * reason을 통해 연결이 끊긴 이유를 확인할 수 있다.
     *
     * ========================================================
     */

    public void OnDisconnectedFromServer(
        NetworkRunner runner,
        NetDisconnectReason reason)
    {
    }



    /*
     * ========================================================
     * OnConnectRequest()
     * ========================================================
     *
     * 다른 Client가 이 Runner에 연결을 요청했을 때 호출된다.
     *
     *
     * 여기서 연결 요청을 검사하거나
     * Token을 확인하는 등의 처리를 할 수 있다.
     *
     * ========================================================
     */

    public void OnConnectRequest(
        NetworkRunner runner,
        NetworkRunnerCallbackArgs.ConnectRequest request,
        byte[] token)
    {
    }



    /*
     * ========================================================
     * OnConnectFailed()
     * ========================================================
     *
     * Server/Session에 연결하려 했지만 실패했을 때 호출된다.
     *
     * reason을 통해 실패 원인을 확인할 수 있다.
     *
     * ========================================================
     */

    public void OnConnectFailed(
        NetworkRunner runner,
        NetAddress remoteAddress,
        NetConnectFailedReason reason)
    {
    }



    /*
     * ========================================================
     * OnUserSimulationMessage()
     * ========================================================
     *
     * Fusion의 SimulationMessage를 처리하는 콜백.
     *
     * 현재 프로젝트에서는 직접 사용할 필요가 없기 때문에
     * 비워둔다.
     *
     * ========================================================
     */

    public void OnUserSimulationMessage(
        NetworkRunner runner,
        SimulationMessagePtr message)
    {
    }



    /*
     * ========================================================
     * OnSessionListUpdated()
     * ========================================================
     *
     * Session 목록이 업데이트되었을 때 호출된다.
     *
     *
     * 나중에
     *
     * "현재 존재하는 방 목록"
     *
     * 을 UI에 보여주고 싶다면 사용할 수 있다.
     *
     * ========================================================
     */

    public void OnSessionListUpdated(
        NetworkRunner runner,
        List<SessionInfo> sessionList)
    {
    }



    /*
     * ========================================================
     * OnCustomAuthenticationResponse()
     * ========================================================
     *
     * Fusion의 Custom Authentication을 사용했을 때
     * 인증 서버에서 받은 응답을 처리하는 곳.
     *
     * 현재는 사용하지 않는다.
     *
     * ========================================================
     */

    public void OnCustomAuthenticationResponse(
        NetworkRunner runner,
        Dictionary<string, object> data)
    {
    }



    /*
     * ========================================================
     * OnHostMigration()
     * ========================================================
     *
     * Host가 게임에서 나가 Host 권한을 다른 Player가
     * 이어받는 Host Migration 상황에서 호출된다.
     *
     * 현재는 아무런 처리를 하지 않는다.
     *
     * ========================================================
     */

    public void OnHostMigration(
        NetworkRunner runner,
        HostMigrationToken hostMigrationToken)
    {
    }



    /*
     * ========================================================
     * OnSceneLoadDone()
     * ========================================================
     *
     * Fusion이 네트워크 Scene을 모두 로드한 후 호출된다.
     *
     *
     * 예:
     *
     * Host가 씬2로 이동
     *        ↓
     * Client도 씬2 로드
     *        ↓
     * Scene Load 완료
     *        ↓
     * OnSceneLoadDone()
     *
     *
     * 여기에서 현재 Scene에 필요한
     * NetworkObject를 Spawn하는 등의 작업을 할 수 있다.
     *
     * ========================================================
     */

    public void OnSceneLoadDone(
        NetworkRunner runner)
    {
        /*
         * 현재 활성화된 Unity Scene의 Build Index를 가져온다.
         */

        // int sceneIndex =
        // SceneManager.GetActiveScene().buildIndex;


        /*
         * 현재 Scene에 따라 다른 처리를 하기 위한 분기.
         */

        /* switch (sceneIndex)
         {
             *//*
              * =================================================
              * Scene 1
              * =================================================
              *
              * 현재 기획에서 메인 / 커스터마이징 Scene.
              *
              * PlayerData를 사용하거나
              * Ready 시스템 등을 구현할 수 있는 위치.
              *
              * =================================================
              *//*

             case 1:

                 //SpawnLobbyPlayerData();

                 // 메인씬

                 break;



             *//*
              * =================================================
              * Scene 2
              * =================================================
              *
              * 현재 기획에서 실제 전투가 진행되는 Scene.
              *
              * PlayerData에 저장된 정보를 읽고
              * BattlePlayer를 Spawn하는 등의 작업을
              * 이곳에서 시작할 수 있다.
              *
              * =================================================
              *//*

             case 2:

                 // 전투씬

                 //SpawnBattlePlayer();

                 break;
         }*/

        if (!runner.IsServer)
            return;
        int sceneIndex = SceneManager.GetActiveScene().buildIndex;

        switch (sceneIndex)
        {
            case 1:
                // 메인/로비 씬
                break;

            case 2:
                // 전투 씬
                {
                    foreach (var data in playerDatas)
                    {
                         
                    }
                    break;

                }

        }
    }



    /*
     * ========================================================
     * OnSceneLoadStart()
     * ========================================================
     *
     * Fusion이 네트워크 Scene을 로드하기 시작할 때 호출된다.
     *
     *
     * OnSceneLoadDone()
     *      ↓
     * Scene 로드 완료
     *
     * 라면,
     *
     * OnSceneLoadStart()
     *      ↓
     * Scene 로드 시작
     *
     * 이라고 생각하면 된다.
     *
     * ========================================================
     */

    public void OnSceneLoadStart(
        NetworkRunner runner)
    {
    }



    /*
     * ========================================================
     * OnObjectExitAOI()
     * ========================================================
     *
     * AOI(Area Of Interest)에서 NetworkObject가
     * 벗어났을 때 호출된다.
     *
     *
     * 대규모 맵에서
     *
     * "내 주변에 있는 NetworkObject만 동기화"
     *
     * 하는 시스템을 사용할 때 활용할 수 있다.
     *
     * 현재 게임에서는 당장 사용하지 않아도 된다.
     *
     * ========================================================
     */

    public void OnObjectExitAOI(
        NetworkRunner runner,
        NetworkObject obj,
        PlayerRef player)
    {
    }



    /*
     * ========================================================
     * OnObjectEnterAOI()
     * ========================================================
     *
     * NetworkObject가 특정 Player의
     * AOI(Area Of Interest)에 들어왔을 때 호출된다.
     *
     * ========================================================
     */

    public void OnObjectEnterAOI(
        NetworkRunner runner,
        NetworkObject obj,
        PlayerRef player)
    {
    }



    /*
     * ========================================================
     * OnReliableDataReceived()
     * ========================================================
     *
     * Fusion의 Reliable Data 전송 기능을 사용할 때
     * 상대방에게서 신뢰성 있는 데이터를 받았을 경우 호출된다.
     *
     * 일반적인 Networked Property나 Network RPC와는
     * 별도의 데이터 전달 시스템이다.
     *
     * 현재 프로젝트에서는 사용하지 않는다.
     *
     * ========================================================
     */

    public void OnReliableDataReceived(
        NetworkRunner runner,
        PlayerRef player,
        ReliableKey key,
        ArraySegment<byte> data)
    {
    }



    /*
     * ========================================================
     * OnReliableDataProgress()
     * ========================================================
     *
     * Reliable Data 전송 진행 상황을 확인할 때 사용한다.
     *
     * progress는 일반적으로 0 ~ 1 범위의 진행률을 의미한다.
     *
     * 현재 프로젝트에서는 사용하지 않는다.
     *
     * ========================================================
     */

    public void OnReliableDataProgress(
        NetworkRunner runner,
        PlayerRef player,
        ReliableKey key,
        float progress)
    {
    }
}

