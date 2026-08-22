using UnityEngine;

using Fusion;

using System.Collections.Generic;

public class BattleManager : MonoBehaviour
{
    [Header("Network Prefab")]
    [SerializeField] private NetworkPrefabRef flagPrefab;


    [Header("Battle Spawn Point")]

    [SerializeField] private Transform playerSpawnA;

    [SerializeField] private Transform playerSpawnB;

    [SerializeField] private Transform flagSpawnPoint;


    private NetworkRunner runner;

    private Dictionary<PlayerRef, PlayerData> playerDatas;

    private Dictionary<PlayerRef, NetworkObject> spawnedPlayers;


    private NetworkObject spawnedFlag;

    private static BattleManager instance = null;
    public static BattleManager Instance => instance;

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


    // =========================================================
    // 전투 초기화
    //
    // NetworkGameManager의
    // OnSceneLoadDone()에서 호출
    // =========================================================
    public void InitializeBattle(
        NetworkRunner runner,
        Dictionary<PlayerRef, PlayerData> playerDatas,
        NetworkPrefabRef playerPrefab,
        Dictionary<PlayerRef, NetworkObject> spawnedPlayers)
    {
        this.runner = runner;

        this.playerDatas = playerDatas;

        this.spawnedPlayers = spawnedPlayers;


        FindSpawnPoints();


        SpawnBattlePlayers(
            playerPrefab
        );


        SpawnFlag();


        StartBattle();
    }


    // =========================================================
    // 전투씬 내부 SpawnPoint 탐색
    //
    // NetworkGameManager는 DontDestroyOnLoad이기 때문에
    // 씬이 변경되면 기존 Transform 참조를 사용할 수 없다.
    //
    // 따라서 전투씬 로딩 이후
    // SpawnPoint를 다시 찾는다.
    // =========================================================
    private void FindSpawnPoints()
    {
        GameObject spawnA =
            GameObject.FindWithTag(
                "PlayerSpawnA"
            );

        GameObject spawnB =
            GameObject.FindWithTag(
                "PlayerSpawnB"
            );

        GameObject flagSpawn =
            GameObject.FindWithTag(
                "FlagSpawn"
            );


        if (spawnA != null)
        {
            playerSpawnA =
                spawnA.transform;
        }


        if (spawnB != null)
        {
            playerSpawnB =
                spawnB.transform;
        }


        if (flagSpawn != null)
        {
            flagSpawnPoint =
                flagSpawn.transform;
        }
    }


    // =========================================================
    // PlayerData를 기반으로
    // 실제 전투 Player 생성
    // =========================================================
    private void SpawnBattlePlayers(
        NetworkPrefabRef playerPrefab)
    {
        int index = 0;


        foreach (
            KeyValuePair<PlayerRef, PlayerData>
            pair
            in playerDatas)
        {
            PlayerRef playerRef =
                pair.Key;

            PlayerData playerData =
                pair.Value;


            Vector3 spawnPosition =
                GetSpawnPosition(
                    index
                );


            NetworkObject playerObject =
                runner.Spawn(
                    playerPrefab,
                    spawnPosition,
                    Quaternion.identity,
                    playerRef
                );


            spawnedPlayers.Add(
                playerRef,
                playerObject
            );


            Player player =
                playerObject
                    .GetComponent<Player>();


            if (player != null)
            {
                InitializePlayer(
                    player,
                    playerData
                );
            }


            index++;
        }
    }


    // =========================================================
    // 플레이어 생성 위치 반환
    // =========================================================
    private Vector3 GetSpawnPosition(
        int index)
    {
        if (index == 0 &&
            playerSpawnA != null)
        {
            return playerSpawnA.position;
        }


        if (index == 1 &&
            playerSpawnB != null)
        {
            return playerSpawnB.position;
        }


        return Vector3.zero;
    }


    // =========================================================
    // PlayerData 정보를
    // 실제 Player에 적용
    //
    // 이후 PlayerData에
    //
    // speed
    // hp
    //
    // 등이 추가되면 여기에서 전달
    // =========================================================
    private void InitializePlayer(
        Player player,
        PlayerData playerData)
    {
        /*
        player.InitPlayer(
            playerData.speed,
            playerData.hp
        );
        */


        PlayerEquipment equipment =
            player.GetComponent<
                PlayerEquipment>();


        if (equipment != null)
        {
            equipment.EquipHat(
                (int)playerData.hat
            );


            equipment.EquipBroom(
                (int)playerData.broom
            );


            equipment.EquipMagic(
                (int)playerData.magic1,
                (int)playerData.magic2
            );
        }
    }


    // =========================================================
    // 중앙 깃발 생성
    // =========================================================
    private void SpawnFlag()
    {
        if (spawnedFlag != null)
            return;


        Vector3 spawnPosition =
            flagSpawnPoint != null
            ? flagSpawnPoint.position
            : Vector3.zero;


        spawnedFlag =
            runner.Spawn(
                flagPrefab,
                spawnPosition,
                Quaternion.identity
            );
    }


    // =========================================================
    // 전투 시작
    //
    // 이후 여기에서
    //
    // TickTimer 생성
    // 전투 시간 시작
    //
    // 등의 기능 추가
    // =========================================================
    private void StartBattle()
    {
    }


    // =========================================================
    // 플레이어 사망 처리
    //
    // 이후 Player의 사망 시스템에서
    // 이 함수를 호출하도록 연결
    // =========================================================
    public void PlayerKilled(
        PlayerRef deadPlayer,
        PlayerRef killerPlayer)
    {
        /*
         *
         * 이후 Flag 시스템과 연결
         *
         * deadPlayer가 Flag를 가지고 있었다면
         *
         * killerPlayer에게 Flag 이전
         *
         */
    }


    // =========================================================
    // 게임 종료
    //
    // 이후 Timer 종료 시 호출
    // =========================================================
    private void EndBattle()
    {
    }
}