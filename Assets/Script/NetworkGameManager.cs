using UnityEngine;
using UnityEngine.SceneManagement;

using Fusion;
using Fusion.Sockets;

using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TMPro;

public class NetworkGameManager : MonoBehaviour, INetworkRunnerCallbacks
{
    private static NetworkGameManager instance;
    public static NetworkGameManager Instance => instance;
    public bool IsMatching => _runner != null || startInProgress || stopInProgress;
    public NetworkRunner Runner => _runner;
    public bool IsRandomMatch { get; private set; }
    public string MatchStatus { get; private set; } = "";
    public bool CanCancelMatch => IsMatching && !isGameStarting && !IsBattleSceneLoaded &&
        !cancelRequested && !stopInProgress &&
        (startInProgress || _runner == null || _runner.SceneManager == null || !_runner.SceneManager.IsBusy);
    public bool IsBattleSceneLoaded { get; private set; }
    public int RequiredPlayerCount => activePlayerCount > 0 ? activePlayerCount : Mathf.Max(1, maxPlayerCount);
    public bool IsRoomHost => _runner != null && _runner.IsRunning && _runner.IsServer;
    public bool CanStartRoomGame => !IsRandomMatch && CanBeginBattle();
    public bool IsWaitingInCodeRoom => !IsRandomMatch && _runner != null && _runner.IsRunning &&
        initialSceneLoaded && !startInProgress && !stopInProgress && !cancelRequested &&
        !isGameStarting && !IsBattleSceneLoaded;

    [Header("Scene")]
    [SerializeField] private int battleSceneIndex = 1;

    [Header("Room")]
    [SerializeField] private TMP_InputField roomIdInput;
    [SerializeField] private int maxPlayerCount = 2;
    [Header("Matchmaking UI (optional)")]
    [SerializeField] private TMP_Text matchmakingStatusText;
    [Header("Random match start")]
    [Tooltip("켜면 양쪽 linkuserinfo의 상대 프로필 1초 표시 확인을 기다립니다. 테스트 시 끄면 PlayerData 준비 후 1초 뒤 시작합니다.")]
    [SerializeField] private bool requireRandomMatchProfilePreview = true;

    private const int RandomPlayerCount = 2;
    private const string RandomLobbyName = "WitchFlight-Random-1v1-v1";
    private enum MatchRequest { CreateRoom, JoinRoom, Random, LegacyCodeMatch }

    [Header("Network Prefabs")]
    [SerializeField] private NetworkPrefabRef playerPrefab;
    [SerializeField] private NetworkPrefabRef playerDataPrefab;

    private NetworkRunner _runner;
    private GameObject runnerObject;
    private CancellationTokenSource matchCancellation;
    private bool startInProgress, stopInProgress, cancelRequested, initialSceneLoaded;
    private int activePlayerCount;
    private float nextWaitingStatusRefresh;
    private float randomPlayersReadyAt = -1f;

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
    private Vector2 accumulatedLook;
    private NetworkButtons latchedButtons;
    private Player inputPlayer;
    private enemyLockOn inputTargeting;
    private Camera inputCamera;
    private CameraFollow inputCameraFollow;

    private void Update()
    {
        // Re-check after startup/scene loading completes as those callbacks can precede
        // loadout initialization. Only random matchmaking may start automatically.
        if (IsRandomMatch && IsRoomHost) CheckPlayerCount();
        if (Time.unscaledTime >= nextWaitingStatusRefresh)
        {
            nextWaitingStatusRefresh = Time.unscaledTime + 0.25f;
            UpdateWaitingStatus();
        }
        if (_runner == null || Player.LocalPlayer == null || BattleManager.Instance == null ||
            !BattleManager.Instance.IsGameplayActive || CombatPresentation.MenuOpen ||
            Cursor.lockState != CursorLockMode.Locked)
        {
            accumulatedLook = Vector2.zero;
            latchedButtons = default;
            return;
        }

        accumulatedLook += new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y"));
        LatchButton(PlayerInputButton.Accelerate, Input.GetKeyDown(KeyCode.W));
        LatchButton(PlayerInputButton.Decelerate, Input.GetKeyDown(KeyCode.S));
        LatchButton(PlayerInputButton.Parry, Input.GetMouseButtonDown(1));
        LatchButton(PlayerInputButton.Boost, Input.GetKeyDown(KeyCode.LeftShift) || Input.GetKeyDown(KeyCode.RightShift));
        LatchButton(PlayerInputButton.MagicSlot1, Input.GetKeyDown(KeyCode.Alpha1));
        LatchButton(PlayerInputButton.MagicSlot2, Input.GetKeyDown(KeyCode.Alpha2));
        LatchButton(PlayerInputButton.MagicSlot3, Input.GetKeyDown(KeyCode.Alpha3));
    }

    private void LatchButton(PlayerInputButton button, bool pressed)
    {
        if (pressed)
            latchedButtons.Set(button, true);
    }


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
        if (IsMatching) return;
        if (TryReadRoomId(out string roomId, allowLegacyFallback: true))
            StartGame(MatchRequest.LegacyCodeMatch, roomId);
    }

    // Three separate button entry points; retain StartMatch only for existing UI.
    public void CreateRoom()
    {
        if (IsMatching) return;
        if (TryReadRoomId(out string roomId)) StartGame(MatchRequest.CreateRoom, roomId);
    }

    public void JoinRoom()
    {
        if (IsMatching)
        {
            return;
        }


        if (TryReadRoomId(out string roomId)) StartGame(MatchRequest.JoinRoom, roomId);
    }

    private bool TryReadRoomId(out string roomId, bool allowLegacyFallback = false)
    {
        roomId = roomIdInput != null ? roomIdInput.text.Trim() : allowLegacyFallback ? "textBuild" : "";
        if (string.IsNullOrWhiteSpace(roomId))
        {
            SetMatchStatus(roomIdInput == null ? "Room Id Input을 연결해주세요." : "방 코드를 입력해주세요.");
            return false;
        }
        return true;
    }

    // Connect this to a separate random-match button. No room code is required.
    public void StartRandomMatch()
    {
        if (!IsMatching) StartGame(MatchRequest.Random, null);
    }

    // Main/code-room start button. This is separate from BattleManager.startGame()
    // which begins the intro/countdown AFTER Battle has loaded.
    public void StartRoomGame()
    {
        if (isGameStarting || IsBattleSceneLoaded) return;
        if (IsRandomMatch)
        {
            SetMatchStatus("랜덤 매칭은 참가자가 준비되면 자동으로 시작합니다.");
            return;
        }
        if (!IsRoomHost)
        {
            SetMatchStatus("방장만 게임을 시작할 수 있습니다.");
            return;
        }
        // Check actual connected users even if invoked directly, not through the UI.
        int connectedPlayers = 0;
        foreach (PlayerRef player in _runner.ActivePlayers) connectedPlayers++;
        if (connectedPlayers < RequiredPlayerCount)
        {
            SetMatchStatus($"게임 시작 실패: 인원이 부족합니다. ({connectedPlayers}/{RequiredPlayerCount})");
            return;
        }
        if (!CanStartRoomGame)
        {
            SetMatchStatus($"참가자 {RequiredPlayerCount}명과 PlayerData 준비가 완료되어야 시작할 수 있습니다.");
            return;
        }
        BeginBattleTransition();
    }

    private bool CanBeginBattle()
    {
        return IsRoomHost && !startInProgress && !stopInProgress && !cancelRequested &&
            !isGameStarting && !IsBattleSceneLoaded && initialSceneLoaded &&
            (_runner.SceneManager == null || !_runner.SceneManager.IsBusy) && AreBattlePlayerDatasReady();
    }

    private bool ValidateMatchSetup()
    {
        if (!HasValidNetworkPrefab(playerDataPrefab, "PlayerData") ||
            !HasValidNetworkPrefab(playerPrefab, "Player"))
        {
            SetMatchStatus("네트워크 프리팹 설정을 확인해주세요.");
            return false;
        }
        int currentScene = SceneManager.GetActiveScene().buildIndex;
        if (battleSceneIndex < 0 || battleSceneIndex >= SceneManager.sceneCountInBuildSettings ||
            currentScene < 0 || currentScene == battleSceneIndex)
        {
            Debug.LogError("Start matchmaking from the lobby with a valid Battle build index.", this);
            SetMatchStatus("로비 씬 및 Battle 빌드 설정을 확인해주세요.");
            return false;
        }
        return true;
    }

    private async void StartGame(MatchRequest request, string roomId)
    {
        if (IsMatching || !ValidateMatchSetup()) return;
        // Keep standalone battle tests usable, but do not match with an uninitialized login profile.
        if (LoginManager.Instance != null && (!LoginManager.Instance.IsLoggedIn ||
            DatabaseManager.Instance == null || !DatabaseManager.Instance.IsDataConfigReady))
        {
            Debug.LogWarning("로그인과 로비 데이터 초기화를 먼저 완료해주세요.");
            return;
        }
        bool randomMatch = request == MatchRequest.Random;
        startInProgress = true;
        cancelRequested = false;
        IsRandomMatch = randomMatch;
        randomPlayersReadyAt = -1f;
        activePlayerCount = randomMatch ? RandomPlayerCount : Mathf.Max(1, maxPlayerCount);
        var cancellation = new CancellationTokenSource();
        matchCancellation = cancellation;
        NetworkRunner sessionRunner = null;
        try
        {
            // A failed/shut-down Runner cannot be reused. Its disposable child keeps
            // shutdown from destroying this persistent manager and the user's UI.
            runnerObject = new GameObject("WitchFlight Network Session");
            runnerObject.transform.SetParent(transform, false);
            sessionRunner = runnerObject.AddComponent<NetworkRunner>();
            _runner = sessionRunner;
            sessionRunner.AddCallbacks(this);
            sessionRunner.ProvideInput = true;
            var sceneManager = runnerObject.AddComponent<NetworkSceneManagerDefault>();
            SetMatchStatus(request switch
            {
                MatchRequest.CreateRoom => "코드 방을 만드는 중...",
                MatchRequest.JoinRoom => "코드 방에 접속 중...",
                MatchRequest.Random => "랜덤 상대를 찾는 중...",
                _ => "코드 방에 연결 중..."
            });

            var result = await sessionRunner.StartGame(new StartGameArgs
            {
                GameMode = request switch
                {
                    MatchRequest.CreateRoom => GameMode.Host,
                    MatchRequest.JoinRoom => GameMode.Client,
                    _ => GameMode.AutoHostOrClient
                },
                // Explicit join must never silently create a room when the code is absent.
                EnableClientSessionCreation = request != MatchRequest.JoinRoom,
                // Null + AutoHostOrClient = join random or create, handled by Photon.
                SessionName = roomId,
                PlayerCount = activePlayerCount,
                CustomLobbyName = randomMatch ? RandomLobbyName : null,
                SessionProperties = randomMatch ? new Dictionary<string, SessionProperty>
                {
                    { "queue", "random-1v1" },
                    { "players", RandomPlayerCount },
                    { "battle", battleSceneIndex }
                } : null,
                IsVisible = randomMatch,
                IsOpen = true,
                MatchmakingMode = Fusion.Photon.Realtime.MatchmakingMode.FillRoom,
                Scene = SceneRef.FromIndex(SceneManager.GetActiveScene().buildIndex),
                SceneManager = sceneManager,
                StartGameCancellationToken = cancellation.Token
            });
            if (this == null) return;
            if (cancelRequested || !result.Ok)
            {
                bool cancelled = cancelRequested || result.ShutdownReason == ShutdownReason.OperationCanceled;
                await ShutdownSession(sessionRunner);
                SetMatchStatus(cancelled ? "매칭을 취소했습니다." : GetMatchFailureMessage(result.ShutdownReason));
                if (!cancelled) Debug.LogWarning($"Matchmaking failed: {result.ShutdownReason} {result.ErrorMessage}", this);
                return;
            }
            UpdateWaitingStatus();
        }
        catch (OperationCanceledException)
        {
            await ShutdownSession(sessionRunner);
            if (this != null) SetMatchStatus("매칭을 취소했습니다.");
        }
        catch (Exception exception)
        {
            await ShutdownSession(sessionRunner);
            if (this != null)
            {
                SetMatchStatus("매칭 연결에 실패했습니다. 다시 시도해주세요.");
                Debug.LogException(exception, this);
            }
        }
        finally
        {
            if (ReferenceEquals(matchCancellation, cancellation)) matchCancellation = null;
            cancellation.Dispose();
            startInProgress = false;
        }
    }

    // Lobby-only cancellation. Battle/scene transition is not a matchmaking cancel.
    public async void CancelMatch()
    {
        if (!CanCancelMatch) return;
        cancelRequested = true;
        SetMatchStatus("매칭 취소 중...");
        if (startInProgress)
        {
            matchCancellation?.Cancel();
            return; // StartGame owns shutdown until its awaited operation finishes.
        }
        stopInProgress = true;
        try
        {
            await ShutdownSession(_runner);
            if (this != null) SetMatchStatus("매칭을 취소했습니다.");
        }
        finally { stopInProgress = false; }
    }

    private async Task ShutdownSession(NetworkRunner sessionRunner)
    {
        try
        {
            if (sessionRunner != null) await sessionRunner.Shutdown(destroyGameObject: false);
        }
        catch (Exception exception) { Debug.LogWarning($"Session shutdown: {exception.Message}"); }
        finally { if (this != null) CleanupSession(sessionRunner); }
    }

    private void CleanupSession(NetworkRunner sessionRunner)
    {
        if (!ReferenceEquals(_runner, sessionRunner)) return;
        if (battleInitializationRoutine != null) StopCoroutine(battleInitializationRoutine);
        battleInitializationRoutine = null;
        IsBattleSceneLoaded = false;
        initialSceneLoaded = false;
        randomPlayersReadyAt = -1f;
        accumulatedLook = Vector2.zero;
        latchedButtons = default;
        _runner = null;
        isGameStarting = false;
        activePlayerCount = 0;
        IsRandomMatch = false;
        spawnedPlayers.Clear();
        playerDatas.Clear();
        assignedTeamIndexes.Clear();
        if (runnerObject != null) Destroy(runnerObject);
        runnerObject = null;
    }

    private void SetMatchStatus(string status)
    {
        MatchStatus = status;
        if (matchmakingStatusText != null) matchmakingStatusText.text = status;
    }

    private static string GetMatchFailureMessage(ShutdownReason reason)
    {
        return reason switch
        {
            ShutdownReason.GameNotFound => "해당 코드의 방이 없습니다. 코드를 확인해주세요.",
            ShutdownReason.GameIdAlreadyExists => "이미 사용 중인 방 코드입니다. 다른 코드로 만들거나 방 접속을 선택해주세요.",
            ShutdownReason.GameIsFull => "방의 인원이 가득 찼습니다.",
            ShutdownReason.GameClosed => "이미 시작했거나 입장이 닫힌 방입니다.",
            _ => $"매칭 실패: {reason}. 다시 시도해주세요."
        };
    }

    private void UpdateWaitingStatus()
    {
        if (_runner == null || !_runner.IsRunning || isGameStarting || IsBattleSceneLoaded || cancelRequested) return;
        int count = 0;
        foreach (PlayerRef player in _runner.ActivePlayers) count++;
        if (IsRandomMatch)
            SetMatchStatus(count >= RequiredPlayerCount
                ? (requireRandomMatchProfilePreview
                    ? "매칭 완료! 상대 프로필 표시 및 1초 대기 중..."
                    : "매칭 완료! 참가자 데이터 준비 및 1초 대기 중...")
                : $"랜덤 상대 대기 중 ({count}/{RequiredPlayerCount})");
        else if (count < RequiredPlayerCount)
            SetMatchStatus($"참가자 대기 중 ({count}/{RequiredPlayerCount})");
        else if (!_runner.IsServer)
            SetMatchStatus("방장의 게임 시작을 기다리는 중...");
        else
            SetMatchStatus(CanStartRoomGame ? "참가자가 준비되었습니다. 게임 시작 버튼을 눌러주세요." : "참가자 정보를 준비하는 중...");
    }

    private void OnDestroy()
    {
        if (instance != this) return;
        instance = null;
        matchCancellation?.Cancel();
    }


    // 방에 플레이어가 접속했을 때 Fusion이 자동 호출
    // Host가 해당 플레이어의 PlayerData 생성
    public void OnPlayerJoined(
        NetworkRunner runner,
        PlayerRef player)
    {
        if (runner != _runner || cancelRequested) return;
        randomPlayersReadyAt = -1f;
        UpdateWaitingStatus();
        if (!runner.IsServer)
            return;

        if (playerDatas.ContainsKey(player) || assignedTeamIndexes.ContainsKey(player))
            return;

        // Spawned()가 비동기로 실행되므로, PlayerData를 등록하기 전에 진영을 예약한다.
        assignedTeamIndexes[player] = NextTeamIndex();
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
        if (_runner == null || !_runner.IsServer || data == null || data.Runner != _runner || data.teamIndex != 0)
            return;

        PlayerRef player = data.Object.InputAuthority;
        if (!assignedTeamIndexes.TryGetValue(player, out int teamIndex))
        {
            // 복구 경로: 기존 PlayerData가 이미 존재하는 경우에도 일관된 값을 만든다.
            teamIndex = NextTeamIndex();
            assignedTeamIndexes[player] = teamIndex;
        }

        data.teamIndex = teamIndex;
        data.camp = teamIndex == 1 ? Camp.A : Camp.B;
    }

    private int NextTeamIndex()
    {
        // Still 1,2,1,2 in join order; fill the vacant side after a waiting player leaves.
        int team1 = 0, team2 = 0;
        foreach (int team in assignedTeamIndexes.Values)
            if (team == 1) team1++; else if (team == 2) team2++;
        return team1 <= team2 ? 1 : 2;
    }
    // PlayerData가 생성된 후 PlayerData.Spawned()에서 호출
    // 생성된 PlayerData를 Dictionary에 등록
    public void RegisterPlayerData(
        PlayerData data)
    {
        // PlayerData Dictionary는 전투 Player를 생성하는 호스트만 관리한다.
        if (_runner == null || !_runner.IsServer || cancelRequested || data == null ||
            data.Object == null || data.Runner != _runner)
            return;

        PlayerRef player = data.Object.InputAuthority;
        playerDatas[player] = data;
    }

    public void NotifyPlayerDataInitialized(PlayerData data)
    {
        if (_runner == null || !_runner.IsServer || cancelRequested || data == null || data.Runner != _runner)
            return;

        RegisterPlayerData(data);
        CheckPlayerCount();
    }


    // PlayerData가 제거될 때 PlayerData.Despawned()에서 호출
    public void UnregisterPlayerData(
        PlayerData data)
    {
        if (data == null || data.Object == null || data.Runner != _runner)
            return;

        PlayerRef player =
            data.Object.InputAuthority;

        if (playerDatas.TryGetValue(player, out PlayerData registered) && registered == data)
        {
            randomPlayersReadyAt = -1f;
            playerDatas.Remove(player);
            assignedTeamIndexes.Remove(player);
        }
    }


    // PlayerData 등록 후 호출
    // 현재 플레이어가 최대 인원인지 확인
    private void CheckPlayerCount()
    {
        if (!IsRandomMatch || !CanBeginBattle())
        {
            randomPlayersReadyAt = -1f;
            return;
        }
        if (AreRandomStartRequirementsMet()) BeginBattleTransition();
    }

    private bool AreRandomStartRequirementsMet()
    {
        if (requireRandomMatchProfilePreview)
        {
            randomPlayersReadyAt = -1f;
            return AreRandomProfilesPresented();
        }
        // UI-independent test path. CanBeginBattle must still validate the host,
        // connected participants, initialized PlayerData and scene-loading state.
        if (randomPlayersReadyAt < 0f) randomPlayersReadyAt = Time.unscaledTime;
        return Time.unscaledTime - randomPlayersReadyAt >= 1f;
    }

    private bool AreRandomProfilesPresented()
    {
        // Each peer acknowledges only after its replicated opponent profile was displayed
        // for one second. A host-only delay would skip the remote client's loading time.
        foreach (PlayerRef playerRef in _runner.ActivePlayers)
        {
            if (!playerDatas.TryGetValue(playerRef, out PlayerData data) || data == null) return false;
            bool presented = false;
            foreach (PlayerRef opponentRef in _runner.ActivePlayers)
            {
                if (opponentRef == playerRef) continue;
                if (playerDatas.TryGetValue(opponentRef, out PlayerData opponent) && opponent != null &&
                    opponent.teamIndex != data.teamIndex && data.HasPresentedLobbyOpponent(opponent))
                    presented = true;
            }
            if (!presented) return false;
        }
        return true;
    }

    private void BeginBattleTransition()
    {
        // Revalidate at click time; do not trust an earlier UI enabled state.
        if (!CanBeginBattle())
        {
            randomPlayersReadyAt = -1f;
            return;
        }
        if (IsRandomMatch && !AreRandomStartRequirementsMet()) return;
        isGameStarting = true;
        _runner.SessionInfo.IsOpen = false;
        _runner.SessionInfo.IsVisible = false;
        SetMatchStatus("매칭 완료! Battle 씬으로 이동 중...");

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
        if (runner != _runner || cancelRequested) return;
        initialSceneLoaded = true;
        int sceneIndex =
            SceneManager.GetActiveScene().buildIndex;

        IsBattleSceneLoaded = sceneIndex == battleSceneIndex;
        if (sceneIndex != battleSceneIndex)
            return;
        isGameStarting = true;
        SetMatchStatus("매칭 완료");

        if (!runner.IsServer)
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
        if (_runner == null || !_runner.IsRunning || !_runner.IsServer) return false;
        int count = 0;
        // Count connected participants, not stale dictionary entries after disconnects.
        foreach (PlayerRef player in _runner.ActivePlayers)
        {
            count++;
            if (!playerDatas.TryGetValue(player, out PlayerData data) ||
                data == null || data.Object == null || !data.Object.IsValid || data.Runner != _runner || !data.IsLoadoutInitialized ||
                (data.teamIndex != 1 && data.teamIndex != 2))
                return false;
        }
        return count >= RequiredPlayerCount;
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
        if (runner != _runner || cancelRequested) return;
        randomPlayersReadyAt = -1f;
        UpdateWaitingStatus();
        if (!runner.IsServer)
            return;

        BattleManager.Instance?.PlayerLeft(player);

        if (spawnedPlayers.TryGetValue(
            player,
            out NetworkObject playerObject))
        {
            runner.Despawn(playerObject);

            spawnedPlayers.Remove(player);
        }

        if (playerDatas.TryGetValue(player, out PlayerData departedData))
        {
            playerDatas.Remove(player);
            if (departedData != null && departedData.Object != null && departedData.Object.IsValid)
                runner.Despawn(departedData.Object);
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
        if (BattleManager.Instance == null || !BattleManager.Instance.IsGameplayActive ||
            CombatPresentation.MenuOpen || Cursor.lockState != CursorLockMode.Locked)
        {
            data.suppressActions = true;
            accumulatedLook = Vector2.zero;
            latchedButtons = default;
            input.Set(data);
            return;
        }
        data.buttons.Set(PlayerInputButton.Accelerate, Input.GetKey(KeyCode.W));
        data.buttons.Set(PlayerInputButton.Decelerate, Input.GetKey(KeyCode.S));
        data.buttons.Set(PlayerInputButton.TurnLeft, Input.GetKey(KeyCode.A));
        data.buttons.Set(PlayerInputButton.TurnRight, Input.GetKey(KeyCode.D));
        data.buttons.Set(PlayerInputButton.Lock, Input.GetMouseButton(0));
        data.buttons.Set(PlayerInputButton.Parry, Input.GetMouseButton(1));
        data.buttons.Set(PlayerInputButton.Boost,
            Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift));
        data.buttons.Set(PlayerInputButton.MagicSlot1, Input.GetKey(KeyCode.Alpha1));
        data.buttons.Set(PlayerInputButton.MagicSlot2, Input.GetKey(KeyCode.Alpha2));
        data.buttons.Set(PlayerInputButton.MagicSlot3, Input.GetKey(KeyCode.Alpha3));
        data.buttons.Set(PlayerInputButton.Menu, Input.GetKey(KeyCode.Escape));

        for (int i = 0; i <= (int)PlayerInputButton.Menu; i++)
        {
            if (latchedButtons.IsSet(i))
                data.buttons.Set(i, true);
        }
        latchedButtons = default;
        data.look = accumulatedLook;
        accumulatedLook = Vector2.zero;
        Player local = Player.LocalPlayer;
        if (local != null)
        {
            if (inputPlayer != local)
            {
                inputPlayer = local;
                inputTargeting = local.GetComponent<enemyLockOn>();
                inputCamera = null; // Respawn/scene changes must not retain the old camera.
            }
            if (inputCamera == null || !inputCamera.isActiveAndEnabled || !inputCamera.CompareTag("MainCamera"))
            {
                inputCamera = Camera.main;
                inputCameraFollow = inputCamera != null ? inputCamera.GetComponent<CameraFollow>() : null;
            }
            if (inputTargeting != null)
                data.lockTarget = inputTargeting.GetInputTarget();
            // Missing camera means no validated aim, not automatic nose alignment.
            data.aimDirection = Vector3.zero;
            data.aimUp = local.transform.up;
            if (inputCameraFollow != null)
            {
                CameraFollow follow = inputCameraFollow;
                data.aimDirection = (follow.GetDisplayedAimPoint() - local.LockAimPoint).normalized;
                if (follow.TryGetSteeringInput(out Vector3 direction, out Vector3 up))
                {
                    // Desired steering direction only. The host uses the actual nose for firing.
                    data.aimDirection = direction;
                    data.aimUp = up;
                    data.steerToAim = true;
                }
            }
        }

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
        if (!ReferenceEquals(runner, _runner)) return;
        // Async startup/cancellation owns cleanup until its await completes.
        if (startInProgress || stopInProgress) return;
        CleanupSession(runner);
        SetMatchStatus($"연결이 종료되었습니다: {shutdownReason}. 로비에서 다시 매칭해주세요.");
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
        if (runner != _runner || isGameStarting || cancelRequested)
            request.Refuse();
        else
            request.Accept();
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
        if (runner != _runner || cancelRequested) return;
        // The first load is the initial lobby; later loads belong to the battle transition.
        if (initialSceneLoaded)
        {
            isGameStarting = true;
            SetMatchStatus("매칭 완료! Battle 씬으로 이동 중...");
        }
        IsBattleSceneLoaded = false;
        accumulatedLook = Vector2.zero;
        latchedButtons = default;
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
