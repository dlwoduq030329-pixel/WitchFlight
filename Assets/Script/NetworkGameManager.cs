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
using UnityEngine.Serialization;

[RequireComponent(typeof(RandomMatchUI))]
public partial class NetworkGameManager : MonoBehaviour, INetworkRunnerCallbacks
{
    private static NetworkGameManager instance;
    public static NetworkGameManager Instance => instance;
    public bool IsMatching => _runner != null || startInProgress || stopInProgress;
    public NetworkRunner Runner => _runner;
    public bool IsRandomMatch { get; private set; }
    public bool IsTraining { get; private set; }
    public bool IsCancellingMatch => cancelRequested || stopInProgress;
    public bool IsBattleTransitioning => isGameStarting;
    public string MatchStatus { get; private set; } = "";
    public bool CanCancelMatch => IsMatching && !isGameStarting && !IsBattleSceneLoaded &&
        !cancelRequested && !stopInProgress &&
        (startInProgress || _runner == null || _runner.SceneManager == null || !_runner.SceneManager.IsBusy);
    public bool IsBattleSceneLoaded { get; private set; }
    public BattleMatchMode CurrentMatchMode
    {
        get
        {
            if (IsRandomMatch) return BattleMatchMode.TwoVsTwo;
            if (_runner != null && _runner.IsRunning)
                foreach (PlayerData data in PlayerData.PerkPlayers)
                    if (data != null && data.Object != null && data.Object.IsValid && data.Runner == _runner && data.IsRoomOwner)
                        return data.RoomMatchMode;
            return roomMatchMode;
        }
    }
    public int RequiredPlayerCount => IsTraining ? 1 : BattleMatchRules.PlayerCount(CurrentMatchMode);
    public int ConnectedPlayerCount
    {
        get { int count = 0; if (_runner != null && _runner.IsRunning) foreach (PlayerRef p in _runner.ActivePlayers) count++; return count; }
    }
    public bool IsRoomHost => _runner != null && _runner.IsRunning && _runner.IsServer;
    public bool CanStartRoomGame => !IsTraining && !IsRandomMatch && CanBeginBattle();
    public bool IsWaitingInCodeRoom => !IsTraining && !IsRandomMatch && _runner != null && _runner.IsRunning &&
        initialSceneLoaded && !startInProgress && !stopInProgress && !cancelRequested &&
        !isGameStarting && !IsBattleSceneLoaded;

    [Header("Scene")]
    [SerializeField] private int mainSceneIndex = 0;
    [SerializeField] private int battleSceneIndex = 1;

    [Header("Room")]
    [SerializeField] private TMP_InputField roomIdInput;
    [FormerlySerializedAs("maxPlayerCount")]
    [SerializeField] private BattleMatchMode roomMatchMode = BattleMatchMode.TwoVsTwo;
    [Header("Matchmaking UI (optional)")]
    [SerializeField] private TMP_Text matchmakingStatusText;
    [Header("Random match start")]
    [Tooltip("모든 참가자의 RandomMatchUI에서 4명의 프로필을 1초 표시한 뒤 전투를 시작합니다. 끄면 데이터 준비 후 1초 뒤 시작하는 테스트 모드입니다.")]
    [SerializeField] private bool requireRandomMatchProfilePreview = true;

    private const int RandomPlayerCount = 4;
    private const string RandomLobbyName = "WitchFlight-Random-2v2-v1";
    private enum MatchRequest { CreateRoom, JoinRoom, Random, LegacyCodeMatch, Training }

    [Header("Network Prefabs")]
    [SerializeField] private NetworkPrefabRef playerPrefab;
    [SerializeField] private NetworkPrefabRef playerDataPrefab;

    private NetworkRunner _runner;
    private GameObject runnerObject;
    private CancellationTokenSource matchCancellation;
    private bool startInProgress, stopInProgress, cancelRequested, initialSceneLoaded;
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
    private bool pendingEmoteCombatSuppression;
    private Player inputPlayer;
    private enemyLockOn inputTargeting;
    private Camera inputCamera;
    private CameraFollow inputCameraFollow;

    private void Update()
    {
        // Re-check after startup/scene loading completes as those callbacks can precede
        // loadout initialization. Random matchmaking and solo training auto-start.
        if ((IsRandomMatch || IsTraining) && IsRoomHost) CheckPlayerCount();
        RefreshRoomAdmission();
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
            pendingEmoteCombatSuppression = false;
            return;
        }

        // Retain a brief wheel opening even when it occurs entirely between Fusion input ticks.
        pendingEmoteCombatSuppression |= BattleEmoteUI.BlocksCombatInput;
        if (BattleEmoteUI.CapturesMouse) accumulatedLook = Vector2.zero;
        else accumulatedLook += new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y")) * ControlSettings.SensitivityMultiplier;
        LatchButton(PlayerInputButton.Accelerate, ControlSettings.Down(ControlAction.Accelerate));
        LatchButton(PlayerInputButton.Decelerate, Input.GetKeyDown(KeyCode.S));
        if (BattlePerkUI.BlocksCombatInput || BattleEmoteUI.BlocksCombatInput) latchedButtons.Set(PlayerInputButton.Parry, false);
        else LatchButton(PlayerInputButton.Parry, ControlSettings.Down(ControlAction.Parry));
        LatchButton(PlayerInputButton.Boost, ControlSettings.Down(ControlAction.Boost));
        LatchButton(PlayerInputButton.MagicSlot1, ControlSettings.Down(ControlAction.MagicSlot1));
        LatchButton(PlayerInputButton.MagicSlot2, ControlSettings.Down(ControlAction.MagicSlot2));
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
            if (!Enum.IsDefined(typeof(BattleMatchMode), roomMatchMode)) roomMatchMode = BattleMatchMode.TwoVsTwo;
            if (GetComponent<RoomMatchSettingsUI>() == null) gameObject.AddComponent<RoomMatchSettingsUI>();
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
        if (TryReadRoomId(out string roomId))
            StartGame(MatchRequest.LegacyCodeMatch, roomId);
    }

    // Three separate button entry points; retain StartMatch only for existing UI.
    public void CreateRoom()
    {
        if (IsMatching) return;
        if (TryReadRoomId(out string roomId)) StartGame(MatchRequest.CreateRoom, roomId);
    }

    public void CreateRoom(string roomId, Action onSuccess, Action<string> onFailed)
    {
        StartGame(MatchRequest.CreateRoom, roomId, onSuccess, onFailed);
    }

    public void JoinRoom()
    {
        if (IsMatching) return;
        if (TryReadRoomId(out string roomId)) JoinRoom(roomId);
    }

    public void JoinRoom(string roomId)
    {
        JoinRoom(roomId, null, null);
    }

    /// <summary>
    /// 기존 방에 접속하고 요청한 UI에 결과를 전달합니다. 없는 방은 생성하지 않습니다.
    /// onSuccess: 세션 접속 완료 (PlayerData/프로필 동기화 완료는 별도).
    /// onFailed: 코드 오류, 빈 코드, 접속 중복, 로그인/설정 오류, 취소, 연결 실패.
    /// UI는 콜백에서 입력창/오류 문구를 처리하고 대기실 정보는 linkuserinfo로 갱신합니다.
    /// </summary>
    public void JoinRoom(string roomId, Action onSuccess, Action<string> onFailed)
    {
        if (IsMatching)
        {
            // Reject only this request; never replace an in-flight request's callback/status.
            NotifyRoomJoinResult(onSuccess, onFailed, "이미 접속 중이거나 방에 입장한 상태입니다.");
            return;
        }
        if (!RoomCodeRules.TryValidate(roomId, out string code, out string error))
        {
            SetMatchStatus(error);
            NotifyRoomJoinResult(onSuccess, onFailed, MatchStatus);
            return;
        }

        StartGame(MatchRequest.JoinRoom, code, onSuccess, onFailed);
    }

    private void NotifyRoomJoinResult(Action onSuccess, Action<string> onFailed, string error)
    {
        // The delegates belong to ONE join attempt, not a global UI reference.
        Delegate callback = error == null ? (Delegate)onSuccess : onFailed;
        if (callback == null) return;
        foreach (Delegate handler in callback.GetInvocationList())
        {
            // Scene changes may destroy the UI before the connection finishes.
            if (handler.Target is UnityEngine.Object target && target == null) continue;
            try
            {
                if (error == null) ((Action)handler)();
                else ((Action<string>)handler)(error);
            }
            catch (Exception exception)
            {
                // A UI callback error must not shut down a successfully joined session
                // or deliver a second (failure) result for the same successful attempt.
                Debug.LogException(exception, this);
            }
        }
    }

    private bool TryReadRoomId(out string roomId)
    {
        if (!RoomCodeRules.TryValidate(roomIdInput != null ? roomIdInput.text : null,
            out roomId, out string error))
        {
            SetMatchStatus(roomIdInput == null ? "Room Id Input을 연결해주세요." : error);
            return false;
        }
        return true;
    }

    // Connect this to a separate random-match button. No room code is required.
    public void StartRandomMatch()
    {
        if (!IsMatching) StartGame(MatchRequest.Random, null);
    }

    // Main scene training button: a local Fusion simulation, never a public room.
    public void StartTraining()
    {
        if (!IsMatching) StartGame(MatchRequest.Training, null);
    }

    // Unity Button.OnClick entry point. Keeps the login/profile when returning to Main.
    public async void ExitTraining()
    {
        if (IsTraining && IsBattleSceneLoaded) await ReturnToMainMenuAsync();
    }

    internal bool IsTrainingScene(Scene scene) => IsTraining && scene.buildIndex == battleSceneIndex;

    // Dropdown: 0 = 1v1, 1 = 2v2, 2 = 1v1 BOT, 3 = 2v2 BOT. Host only in a connected room.
    public bool TrySetRoomMatchMode(int dropdownValue)
    {
        if (dropdownValue < 0 || dropdownValue > 3) return false;
        BattleMatchMode selected = BattleMatchRules.FromDropdown(dropdownValue);
        if (IsMatching && (!IsWaitingInCodeRoom || !IsRoomHost)) return false;
        if (ConnectedPlayerCount > BattleMatchRules.PlayerCount(selected))
        {
            SetMatchStatus("참가자가 3명 이상이면 1대1로 변경할 수 없습니다.");
            return false;
        }
        if (selected == CurrentMatchMode) return true; // Keep manually selected teams on a no-op UI event.
        if (IsRoomHost)
        {
            PlayerData owner = GetPlayerData(_runner.LocalPlayer);
            if (owner == null || !owner.IsRoomOwner) return false;
            owner.SetRoomMatchMode(selected);
            // Two remaining players may be on the same side after others leave.
            var participants = new List<PlayerRef>(_runner.ActivePlayers);
            participants.Sort((a, b) => a.RawEncoded.CompareTo(b.RawEncoded));
            assignedTeamIndexes.Clear();
            foreach (PlayerRef player in participants)
            {
                int team = NextTeamIndex();
                assignedTeamIndexes[player] = team;
                if (playerDatas.TryGetValue(player, out PlayerData data) && data != null)
                { data.teamIndex = team; data.camp = team == 1 ? Camp.A : Camp.B; }
            }
        }
        roomMatchMode = selected;
        RefreshRoomAdmission();
        UpdateWaitingStatus();
        return true;
    }

    public void SetRoomMatchMode(int dropdownValue) => TrySetRoomMatchMode(dropdownValue);

    // Host-authoritative team changes. PlayerData's owner-only RPC supplies the requester.
    public bool TrySetWaitingTeam(PlayerData data, int team)
    {
        if (!IsRoomHost || !IsWaitingInCodeRoom || !BattleMatchRules.IsTeamMode(CurrentMatchMode) ||
            (team != 1 && team != 2) || data == null || data.Object == null || !data.Object.IsValid ||
            !data.Object.HasStateAuthority || data.Runner != _runner || !data.IsLoadoutInitialized)
            return false;
        PlayerRef player = data.ParticipantRef;
        if (GetPlayerData(player) != data || !assignedTeamIndexes.ContainsKey(player)) return false;
        bool connected = false;
        foreach (PlayerRef participant in _runner.ActivePlayers)
            if (participant == player) { connected = true; break; }
        if (!connected) return false;
        // Count reservations too: another join can be awaiting its PlayerData.Spawned callback.
        int others = 0;
        foreach (PlayerRef participant in _runner.ActivePlayers)
            if (participant != player && assignedTeamIndexes.TryGetValue(participant, out int assigned) && assigned == team)
                others++;
        if (others >= 2) return false;
        assignedTeamIndexes[player] = team;
        data.teamIndex = team;
        data.camp = team == 1 ? Camp.A : Camp.B;
        UpdateWaitingStatus();
        return true;
    }

    private void RefreshRoomAdmission()
    {
        if (IsTraining || !IsRoomHost || isGameStarting || IsBattleSceneLoaded || cancelRequested || stopInProgress) return;
        bool open = ConnectedPlayerCount < RequiredPlayerCount;
        if (_runner.SessionInfo.IsOpen != open) _runner.SessionInfo.IsOpen = open;
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
        if (!IsBotMatch && connectedPlayers < RequiredPlayerCount)
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

    // Despite its name, this starts/joins a NETWORK SESSION, not the battle.
    private async void StartGame(MatchRequest request, string roomId,
        Action onSuccess = null, Action<string> onFailed = null)
    {
        if (IsMatching)
        {
            NotifyRoomJoinResult(onSuccess, onFailed, "이미 접속 중이거나 방에 입장한 상태입니다.");
            return;
        }
        // Guard every code-room entry point, including the legacy create-or-join button.
        // Never truncate an overlong code into another room's code.
        if (request != MatchRequest.Random && request != MatchRequest.Training)
        {
            if (!RoomCodeRules.TryValidate(roomId, out string code, out string error))
            {
                SetMatchStatus(error);
                NotifyRoomJoinResult(onSuccess, onFailed, error);
                return;
            }
            roomId = code;
        }
        if (!ValidateMatchSetup())
        {
            NotifyRoomJoinResult(onSuccess, onFailed, MatchStatus);
            return;
        }
        // Keep standalone battle tests usable, but do not match with an uninitialized login profile.
        if (LoginManager.Instance != null && (!LoginManager.Instance.IsLoggedIn ||
            DatabaseManager.Instance == null || !DatabaseManager.Instance.IsDataConfigReady))
        {
            SetMatchStatus("로그인과 로비 데이터 초기화를 먼저 완료해주세요.");
            Debug.LogWarning(MatchStatus);
            NotifyRoomJoinResult(onSuccess, onFailed, MatchStatus);
            return;
        }
        bool randomMatch = request == MatchRequest.Random;
        startInProgress = true;
        cancelRequested = false;
        IsRandomMatch = randomMatch;
        IsTraining = request == MatchRequest.Training;
        randomPlayersReadyAt = -1f;
        var cancellation = new CancellationTokenSource();
        matchCancellation = cancellation;
        NetworkRunner sessionRunner = null;
        string failureMessage = null;
        bool connected = false;
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
            var sceneManager = runnerObject.AddComponent<BattleNetworkSceneManager>();
            SetMatchStatus(request switch
            {
                MatchRequest.CreateRoom => "코드 방을 만드는 중...",
                MatchRequest.JoinRoom => "코드 방에 접속 중...",
                MatchRequest.Random => "랜덤 상대를 찾는 중...",
                MatchRequest.Training => "훈련장을 준비하는 중...",
                _ => "코드 방에 연결 중..."
            });

            var result = await sessionRunner.StartGame(new StartGameArgs
            {
                GameMode = request switch
                {
                    MatchRequest.CreateRoom => GameMode.Host,
                    MatchRequest.JoinRoom => GameMode.Client,
                    MatchRequest.Training => GameMode.Single,
                    _ => GameMode.AutoHostOrClient
                },
                // Explicit join must never silently create a room when the code is absent.
                EnableClientSessionCreation = request != MatchRequest.JoinRoom,
                // Null + AutoHostOrClient = join random or create, handled by Photon.
                SessionName = roomId,
                // Keep transport capacity at four so a code-room host can switch mode.
                // Admission and launch enforce the selected two/four-player game size.
                PlayerCount = IsTraining ? 1 : RandomPlayerCount,
                CustomLobbyName = randomMatch ? RandomLobbyName : null,
                SessionProperties = randomMatch ? new Dictionary<string, SessionProperty>
                {
                    { "queue", "random-2v2" },
                    { "players", RandomPlayerCount },
                    { "battle", battleSceneIndex }
                } : null,
                IsVisible = randomMatch,
                IsOpen = !IsTraining,
                MatchmakingMode = Fusion.Photon.Realtime.MatchmakingMode.FillRoom,
                Scene = SceneRef.FromIndex(SceneManager.GetActiveScene().buildIndex),
                SceneManager = sceneManager,
                StartGameCancellationToken = cancellation.Token
            });
            if (this == null) return;
            if (cancelRequested || !result.Ok)
            {
                bool cancelled = cancelRequested || result.ShutdownReason == ShutdownReason.OperationCanceled;
                failureMessage = cancelled ? "매칭을 취소했습니다." : GetMatchFailureMessage(result.ShutdownReason);
                await ShutdownSession(sessionRunner);
                if (!cancelled) Debug.LogWarning($"Matchmaking failed: {result.ShutdownReason} {result.ErrorMessage}", this);
            }
            else if (sessionRunner != null && sessionRunner.IsRunning && ReferenceEquals(_runner, sessionRunner))
            {
                connected = true;
            }
            else
            {
                failureMessage = "방에 접속하는 중 연결이 종료되었습니다. 다시 시도해주세요.";
                await ShutdownSession(sessionRunner);
            }
        }
        catch (OperationCanceledException)
        {
            failureMessage = "매칭을 취소했습니다.";
            await ShutdownSession(sessionRunner);
        }
        catch (Exception exception)
        {
            failureMessage = "매칭 연결에 실패했습니다. 다시 시도해주세요.";
            await ShutdownSession(sessionRunner);
            if (this != null) Debug.LogException(exception, this);
        }
        finally
        {
            if (ReferenceEquals(matchCancellation, cancellation)) matchCancellation = null;
            cancellation.Dispose();
            startInProgress = false;
        }

        if (this == null) return;
        // Notify AFTER cleanup/finally so UI can safely retry from a failure callback
        // and IsWaitingInCodeRoom does not still report "startup in progress" on success.
        if (connected)
        {
            UpdateWaitingStatus();
            NotifyRoomJoinResult(onSuccess, onFailed, null);
        }
        else
        {
            SetMatchStatus(failureMessage ?? "방에 접속하지 못했습니다. 다시 시도해주세요.");
            NotifyRoomJoinResult(onSuccess, onFailed, MatchStatus);
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

    // A final-result UI calls this. Shutdown the session before reloading Main;
    // let Main create a fresh manager with its own live scene UI references.
    public async Task<bool> ReturnToMainMenuAsync()
    {
        if (stopInProgress || startInProgress) return false;
        if (!Application.CanStreamedLevelBeLoaded(mainSceneIndex))
        {
            Debug.LogError("Main Scene Index가 Build Settings의 활성 씬을 가리키는지 확인해주세요.", this);
            return false;
        }
        stopInProgress = true;
        try
        {
            await ShutdownSession(_runner);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            int index = mainSceneIndex;
            if (instance == this) instance = null;
            AsyncOperation loading = SceneManager.LoadSceneAsync(index, LoadSceneMode.Single);
            if (loading == null) throw new InvalidOperationException("메인 씬 로드를 시작하지 못했습니다.");
            Destroy(gameObject);
            await loading;
            return true;
        }
        catch (Exception exception)
        {
            if (this != null && instance == null) instance = this;
            Debug.LogException(exception);
            return false;
        }
        finally { if (this != null) stopInProgress = false; }
    }

    private async Task ShutdownSession(NetworkRunner sessionRunner)
    {
        // Capture before Shutdown invalidates NetworkBehaviour.Runner. Fusion normally
        // destroys these objects; the fallback also handles an interrupted startup.
        var sessionDataObjects = new List<GameObject>();
        if (sessionRunner != null)
        {
            foreach (PlayerData data in FindObjectsByType<PlayerData>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (data != null && data.Runner == sessionRunner) sessionDataObjects.Add(data.gameObject);
        }
        try
        {
            if (sessionRunner != null) await sessionRunner.Shutdown(destroyGameObject: false);
        }
        catch (Exception exception) { Debug.LogWarning($"Session shutdown: {exception.Message}"); }
        finally
        {
            foreach (GameObject dataObject in sessionDataObjects)
                if (dataObject != null) Destroy(dataObject);
            if (this != null) CleanupSession(sessionRunner);
        }
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
        IsRandomMatch = false;
        IsTraining = false;
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
        if (IsTraining)
            SetMatchStatus("훈련장을 준비하는 중...");
        else if (IsRandomMatch)
            SetMatchStatus(count >= RequiredPlayerCount
                ? (requireRandomMatchProfilePreview
                    ? "매칭 완료! 팀 프로필 표시 및 1초 대기 중..."
                    : "매칭 완료! 참가자 데이터 준비 및 1초 대기 중...")
                : $"랜덤 상대 대기 중 ({count}/{RequiredPlayerCount})");
        else if (IsBotMatch)
            SetMatchStatus(CanStartRoomGame ? $"봇전 준비 완료 · 시작 시 빈 자리 {RequiredPlayerCount - count}개를 봇이 채웁니다." : $"봇전 참가자 준비 중 ({count}/{RequiredPlayerCount})");
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

        // Two clients may pass the connection check in the same frame. Enforce the
        // selected capacity again before reserving a team or spawning PlayerData.
        if (assignedTeamIndexes.Count >= RequiredPlayerCount && !assignedTeamIndexes.ContainsKey(player))
        { runner.Disconnect(player); return; }
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

        PlayerRef player = data.ParticipantRef;
        if (!assignedTeamIndexes.TryGetValue(player, out int teamIndex))
        {
            // 복구 경로: 기존 PlayerData가 이미 존재하는 경우에도 일관된 값을 만든다.
            teamIndex = NextTeamIndex();
            assignedTeamIndexes[player] = teamIndex;
        }

        data.teamIndex = teamIndex;
        data.camp = teamIndex == 1 ? Camp.A : Camp.B;
        if (data.IsRoomOwner) data.SetRoomMatchMode(IsRandomMatch ? BattleMatchMode.TwoVsTwo : roomMatchMode);
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

        PlayerRef player = data.ParticipantRef;
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
            data.ParticipantRef;

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
        if ((!IsRandomMatch && !IsTraining) || !CanBeginBattle())
        {
            randomPlayersReadyAt = -1f;
            return;
        }
        if (IsTraining || AreRandomStartRequirementsMet()) BeginBattleTransition();
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
        // Every peer must display EVERY other participant, including its teammate.
        foreach (PlayerRef playerRef in _runner.ActivePlayers)
        {
            if (!playerDatas.TryGetValue(playerRef, out PlayerData data) || data == null) return false;
            foreach (PlayerRef opponentRef in _runner.ActivePlayers)
            {
                if (opponentRef == playerRef) continue;
                if (!playerDatas.TryGetValue(opponentRef, out PlayerData opponent) || opponent == null ||
                    !data.HasPresentedLobbyOpponent(opponent)) return false;
            }
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
        if (IsBotMatch && !FillBotSeats()) return;
        isGameStarting = true;
        if (!IsTraining)
        {
            _runner.SessionInfo.IsOpen = false;
            _runner.SessionInfo.IsVisible = false;
        }
        SetMatchStatus(IsTraining ? "훈련장으로 이동 중..." : "매칭 완료! Battle 씬으로 이동 중...");

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
        SetMatchStatus(IsTraining ? "훈련장" : "매칭 완료");

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
        int count = 0, teamA = 0, teamB = 0;
        // Count connected participants, not stale dictionary entries after disconnects.
        foreach (PlayerRef player in _runner.ActivePlayers)
        {
            count++;
            if (!playerDatas.TryGetValue(player, out PlayerData data) ||
                data == null || data.Object == null || !data.Object.IsValid || data.Runner != _runner || !data.IsLoadoutInitialized ||
                (data.teamIndex != 1 && data.teamIndex != 2))
                return false;
            if (data.teamIndex == 1) teamA++; else teamB++;
        }
        if (IsTraining) return count == 1 && teamA == 1 && playerDatas.ContainsKey(_runner.LocalPlayer);
        return IsBotMatch ? count > 0 && count <= RequiredPlayerCount && teamA <= RequiredPlayerCount / 2 && teamB <= RequiredPlayerCount / 2 : count == RequiredPlayerCount && teamA == RequiredPlayerCount / 2 && teamB == RequiredPlayerCount / 2;
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
            pendingEmoteCombatSuppression = false;
            input.Set(data);
            return;
        }
        data.buttons.Set(PlayerInputButton.Accelerate, ControlSettings.Held(ControlAction.Accelerate));
        data.buttons.Set(PlayerInputButton.Decelerate, Input.GetKey(KeyCode.S));
        data.buttons.Set(PlayerInputButton.TurnLeft, ControlSettings.Held(ControlAction.TurnLeft));
        data.buttons.Set(PlayerInputButton.TurnRight, ControlSettings.Held(ControlAction.TurnRight));
        data.buttons.Set(PlayerInputButton.Lock, ControlSettings.Held(ControlAction.LockOn));
        data.buttons.Set(PlayerInputButton.Parry, ControlSettings.Held(ControlAction.Parry));
        data.buttons.Set(PlayerInputButton.Boost, ControlSettings.Held(ControlAction.Boost));
        data.buttons.Set(PlayerInputButton.MagicSlot1, ControlSettings.Held(ControlAction.MagicSlot1));
        data.buttons.Set(PlayerInputButton.MagicSlot2, ControlSettings.Held(ControlAction.MagicSlot2));
        data.buttons.Set(PlayerInputButton.MagicSlot3, Input.GetKey(KeyCode.Alpha3));
        data.buttons.Set(PlayerInputButton.Menu, Input.GetKey(KeyCode.Escape));

        for (int i = 0; i <= (int)PlayerInputButton.Menu; i++)
        {
            if (latchedButtons.IsSet(i))
                data.buttons.Set(i, true);
        }
        latchedButtons = default;
        data.look = BattleEmoteUI.CapturesMouse ? Vector2.zero : accumulatedLook;
        data.suppressCombat = BattlePerkUI.BlocksCombatInput || BattleEmoteUI.BlocksCombatInput || pendingEmoteCombatSuppression;
        pendingEmoteCombatSuppression = false;
        if (data.suppressCombat)
        {
            data.buttons.Set(PlayerInputButton.Lock,false);
            data.buttons.Set(PlayerInputButton.Parry,false);
        }
        accumulatedLook = Vector2.zero;
        Player local = Player.LocalPlayer;
        if (local != null)
        {
            data.portalExitSequence = local.PortalExitSequence;
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
            data.lockTarget = inputTargeting.GetInputTarget(data.buttons.IsSet(PlayerInputButton.Lock));
            // Missing camera means no validated aim, not automatic nose alignment.
            data.aimDirection = Vector3.zero;
            data.aimUp = local.transform.up;
            if (inputCameraFollow != null)
            {
                CameraFollow follow = inputCameraFollow;
                follow.SynchronizePortalExit();
                data.aimDirection = (follow.GetDisplayedAimPoint() - local.LockAimPoint).normalized;
                if (follow.TryGetSteeringInput(out Vector3 direction, out Vector3 up))
                {
                    // Desired steering direction only; Dot hitscan aim is sent separately below.
                    data.aimDirection = direction;
                    data.aimUp = up;
                    data.steerToAim = true;
                }
            }
            if (inputCamera != null && data.buttons.IsSet(PlayerInputButton.Lock))
                data.magicAimDirection = local.GetDotAimDirection(BattleHud.GetMagicAimRay(inputCamera));
            // Include the release tick: that is when the area position becomes fixed.
            if (inputCamera != null)
                data.hasAreaAimPoint = local.TryGetAreaAimPoint(BattleHud.GetMagicAimRay(inputCamera), out data.areaAimPoint);
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
        if (IsTraining || runner != _runner || isGameStarting || cancelRequested || ConnectedPlayerCount >= RequiredPlayerCount)
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
