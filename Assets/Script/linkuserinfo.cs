using System;
using System.Collections.Generic;
using Fusion;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using TMPro;
using UnityEngine.Serialization;

// Local UI bridge only. Fusion's existing PlayerData remains the source of truth.
[DisallowMultipleComponent]
public sealed class linkuserinfo : MonoBehaviour
{
    [Serializable]
    public sealed class PlayerDataEvent : UnityEvent<PlayerData> { }

    [Serializable]
    public sealed class WaitingRoomSlot
    {
        public GameObject occupiedRoot;
        public GameObject emptyRoot;
        public Image profileImage;
        public TMP_Text playerLabel;
        public TMP_Text teamLabel;
        public GameObject localPlayerMarker;
    }

    [Header("Refresh while this panel is active")]
    [SerializeField, Min(0.05f)] private float refreshInterval = 0.2f;

    [Header("Runtime references (resolved automatically)")]
    [SerializeField] private PlayerData localPlayerData;
    [SerializeField] private PlayerData opponentPlayerData;

    [Header("Profile UI (sprite array index = playerprofile)")]
    [SerializeField] private Image localProfileImage;
    [SerializeField] private Image opponentProfileImage;
    [SerializeField] private Sprite[] profileSprites = Array.Empty<Sprite>();
    [SerializeField] private Sprite fallbackProfileSprite;
    [Tooltip("Enable on exactly one active MAIN lobby UI bridge, not the Battle intro panel. Random matchmaking waits for both peers to display the opponent for one second.")]
    [SerializeField] private bool confirmRandomMatchPreview;

    [Header("Code waiting room (optional, MAIN only)")]
    [Tooltip("Keep this component on an always-active object OUTSIDE this panel, so it can open the panel after connecting.")]
    [SerializeField] private GameObject waitingRoomPanel;
    [SerializeField] private WaitingRoomSlot[] waitingRoomSlots = Array.Empty<WaitingRoomSlot>();
    [SerializeField] private TMP_Text waitingRoomCodeText;
    [SerializeField] private TMP_Text waitingRoomCountText;

    [Header("Room owner / guest UI (A = host, B = guest on EVERY client)")]
    [FormerlySerializedAs("waitingRoomTitleText")]
    [SerializeField, InspectorName("roomkeepername")] private TMP_Text roomkeepername;
    [SerializeField, InspectorName("Aslotname")] private TMP_Text Aslotname;
    [SerializeField, InspectorName("Bslotname")] private TMP_Text Bslotname;
    [SerializeField, InspectorName("AslotProfile")] private Image AslotProfile;
    [SerializeField, InspectorName("BslotProfile")] private Image BslotProfile;
    [Tooltip("방장에게만 표시합니다. 정원과 PlayerData가 준비되면 클릭할 수 있습니다. OnClick에 NetworkGameManager.StartRoomGame()을 직접 연결하세요.")]
    [SerializeField, InspectorName("startButton")] private Button waitingRoomStartButton;

    [Header("Optional UI hooks (null means the user is unavailable)")]
    [SerializeField] private PlayerDataEvent onLocalUserInfoChanged = new PlayerDataEvent();
    [SerializeField] private PlayerDataEvent onOpponentUserInfoChanged = new PlayerDataEvent();
    [SerializeField] private UnityEvent onUsersChanged = new UnityEvent();

    public PlayerData LocalPlayerData => IsReady(localPlayerData) ? localPlayerData : null;
    public PlayerData OpponentPlayerData => IsReady(opponentPlayerData) ? opponentPlayerData : null;
    public IReadOnlyList<PlayerData> AllPlayerDatas => players.AsReadOnly();
    public event Action UsersChanged;

    private readonly List<PlayerData> players = new List<PlayerData>();
    private readonly List<PlayerRef> waitingParticipants = new List<PlayerRef>();
    private readonly List<UserState> states = new List<UserState>();
    private readonly List<UserState> previousStates = new List<UserState>();
    private UserState previousLocal;
    private UserState previousOpponent;
    private bool hasSnapshot;
    private float nextRefreshTime;
    private PlayerData previewLocal, previewOpponent;
    private int previewLocalProfile, previewOpponentProfile;
    private float previewStartedAt = -1f;
    private bool previewConfirmed;
    private bool waitingRoomInitialized;

    // Compare values, not just references: networked fields can change on the same object.
    private struct UserState
    {
        public PlayerData Data;
        public HatType Hat;
        public BroomType Broom;
        public MagicType Magic1, Magic2;
        public Camp Camp;
        public bool Ready;
        public string PlayerName;
        public bool IsRoomOwner;
        public int TeamIndex, PlayerProfile;
        public PlayerConfig Customization;
        public float MaxHp, Hp, MaxAp, Ap, ApRecovery;

        public static UserState Read(PlayerData data)
        {
            if (!IsReady(data))
                return default;
            return new UserState
            {
                Data = data, Hat = data.hat, Broom = data.broom,
                Magic1 = data.magic1, Magic2 = data.magic2, Camp = data.camp,
                Ready = data.ready, TeamIndex = data.teamIndex,
                PlayerProfile = data.playerprofile,
                PlayerName = data.playerName.ToString(), IsRoomOwner = data.IsRoomOwner,
                Customization = data.GetPlayerConfig(),
                MaxHp = data.BattleMaxHp, Hp = data.BattleCurrentHp,
                MaxAp = data.BattleMaxAp, Ap = data.BattleCurrentAp,
                ApRecovery = data.BattleApRecoveryPerSecond
            };
        }
    }

    private void OnEnable()
    {
        if (waitingRoomPanel != null && transform.IsChildOf(waitingRoomPanel.transform))
            Debug.LogWarning("Waiting Room Panel must not contain its linkuserinfo controller. Place the controller on an always-active sibling; automatic panel visibility is skipped for this hierarchy.", this);
        hasSnapshot = false;
        RefreshUserInfo();
    }

    private void Update()
    {
        if (Time.unscaledTime >= nextRefreshTime)
            RefreshSnapshot();
        UpdateWaitingRoomVisibility();
        UpdateLobbyPreview();
    }

    private void UpdateWaitingRoomVisibility()
    {
        NetworkGameManager manager = NetworkGameManager.Instance;
        bool waiting = manager != null && manager.IsWaitingInCodeRoom && waitingRoomInitialized;
        // Do not let hiding a panel disable its own controller (or the network manager).
        if (waitingRoomPanel != null && !transform.IsChildOf(waitingRoomPanel.transform) &&
            (manager == null || !manager.transform.IsChildOf(waitingRoomPanel.transform)) &&
            waitingRoomPanel.activeSelf != waiting)
            waitingRoomPanel.SetActive(waiting);
        if (waitingRoomStartButton != null)
        {
            waitingRoomStartButton.gameObject.SetActive(waiting && manager.IsRoomHost);
            waitingRoomStartButton.interactable = waiting && manager.CanStartRoomGame;
        }
    }

    private void RefreshWaitingRoom(NetworkGameManager manager, NetworkRunner runner)
    {
        PlayerData owner = players.Find(data => IsReady(data) && data.IsRoomOwner);
        bool waiting = manager != null && manager.IsWaitingInCodeRoom && IsReady(localPlayerData) && owner != null;
        // Populate before opening the panel: never flash the previous room's title/profile.
        waitingRoomInitialized = false;
        if (roomkeepername != null)
        {
            roomkeepername.richText = false;
            roomkeepername.text = waiting ? owner.playerName.ToString() : "";
        }
        if (waitingRoomCodeText != null)
            waitingRoomCodeText.text = waiting ? runner.SessionInfo.Name : "";
        if (waitingRoomCountText != null)
            waitingRoomCountText.text = waiting ? $"{waitingParticipants.Count} / {runner.SessionInfo.MaxPlayers}" : "";
        // Preserve legacy slots, but also make their first slot explicitly the host's.
        if (owner != null)
        {
            PlayerRef ownerRef = owner.Object.InputAuthority;
            if (waitingParticipants.Remove(ownerRef)) waitingParticipants.Insert(0, ownerRef);
        }
        for (int i = 0; waitingRoomSlots != null && i < waitingRoomSlots.Length; i++)
        {
            WaitingRoomSlot slot = waitingRoomSlots[i];
            if (slot == null) continue;
            bool occupied = waiting && i < waitingParticipants.Count;
            PlayerRef playerRef = occupied ? waitingParticipants[i] : PlayerRef.None;
            PlayerData data = occupied ? GetPlayerData(playerRef) : null;
            // Reserve the same participant's slot even while its replicated data is arriving.
            if (slot.occupiedRoot != null) slot.occupiedRoot.SetActive(occupied);
            if (slot.emptyRoot != null) slot.emptyRoot.SetActive(!occupied);
            ApplyProfile(slot.profileImage, data);
            if (slot.playerLabel != null)
            {
                slot.playerLabel.richText = false;
                slot.playerLabel.text = !occupied ? "" : data == null ? "정보 받는 중..." : data.playerName.ToString();
            }
            if (slot.teamLabel != null)
                slot.teamLabel.text = data == null ? "" : $"팀 {data.teamIndex}";
            if (slot.localPlayerMarker != null)
                slot.localPlayerMarker.SetActive(occupied && playerRef == runner.LocalPlayer);
        }
        // The five explicit references take priority over legacy slot/profile bindings.
        // Never use localPlayerData for A: on a joining client, localPlayerData is B.
        bool guestConnected = waiting && waitingParticipants.Count > 1;
        PlayerData guest = guestConnected ? GetPlayerData(waitingParticipants[1]) : null;
        SetSlotName(Aslotname, waiting ? owner.playerName.ToString() : "");
        SetSlotName(Bslotname, !guestConnected ? "" : guest == null ? "정보 받는 중..." : guest.playerName.ToString());
        ApplyProfile(AslotProfile, waiting ? owner : null);
        ApplyProfile(BslotProfile, waiting ? guest : null);
        waitingRoomInitialized = waiting;
    }

    private static void SetSlotName(TMP_Text target, string value)
    {
        if (target == null) return;
        target.richText = false;
        target.text = value;
    }

    private Sprite GetProfileSprite(PlayerData data)
    {
        if (!IsReady(data)) return null;
        int id = data.profileimage;
        return profileSprites != null && id >= 0 && id < profileSprites.Length && profileSprites[id] != null
            ? profileSprites[id] : fallbackProfileSprite;
    }

    private void ApplyProfile(Image target, PlayerData data)
    {
        if (target == null) return;
        target.sprite = GetProfileSprite(data);
        target.enabled = target.sprite != null;
    }

    private bool IsOpponentProfileVisible()
    {
        Sprite expected = GetProfileSprite(opponentPlayerData);
        if (expected == null || opponentProfileImage == null || !opponentProfileImage.isActiveAndEnabled ||
            opponentProfileImage.sprite != expected || opponentProfileImage.color.a <= 0f ||
            opponentProfileImage.canvas == null || !opponentProfileImage.canvas.isActiveAndEnabled) return false;
        foreach (CanvasGroup group in opponentProfileImage.GetComponentsInParent<CanvasGroup>())
            if (group.isActiveAndEnabled && group.alpha <= 0f) return false;
        return true;
    }

    private void UpdateLobbyPreview()
    {
        NetworkGameManager manager = NetworkGameManager.Instance;
        if (!confirmRandomMatchPreview || manager == null || !manager.IsRandomMatch ||
            manager.IsBattleSceneLoaded || !manager.CanCancelMatch || !IsReady(localPlayerData) ||
            !IsReady(opponentPlayerData) || !IsOpponentProfileVisible())
        {
            ResetLobbyPreview();
            return;
        }
        if (previewLocal != localPlayerData || previewOpponent != opponentPlayerData ||
            previewLocalProfile != localPlayerData.playerprofile || previewOpponentProfile != opponentPlayerData.playerprofile)
        {
            ResetLobbyPreview();
            previewLocal = localPlayerData;
            previewOpponent = opponentPlayerData;
            previewLocalProfile = localPlayerData.playerprofile;
            previewOpponentProfile = opponentPlayerData.playerprofile;
            previewStartedAt = Time.unscaledTime;
        }
        if (!previewConfirmed && previewStartedAt >= 0f && Time.unscaledTime - previewStartedAt >= 1f)
        {
            previewLocal.ConfirmLobbyProfilePreview(previewOpponent);
            previewConfirmed = true;
        }
    }

    private void ResetLobbyPreview()
    {
        if (previewConfirmed && IsReady(previewLocal)) previewLocal.ClearLobbyProfilePreview();
        previewConfirmed = false;
        previewStartedAt = -1f;
        previewLocal = previewOpponent = null;
    }

    // Can also be called by a UnityEvent after wiring your UI.
    public void RefreshUserInfo()
    {
        hasSnapshot = false;
        RefreshSnapshot();
    }

    private void RefreshSnapshot()
    {
        nextRefreshTime = Time.unscaledTime + Mathf.Max(0.05f, refreshInterval);
        players.Clear();
        states.Clear();
        waitingParticipants.Clear();
        localPlayerData = null;
        opponentPlayerData = null;

        NetworkGameManager network = NetworkGameManager.Instance;
        NetworkRunner runner = network != null ? network.Runner : null;
        if (runner != null && runner.IsRunning)
        {
            // The manager's dictionary is host-only. Search replicated objects on BOTH peers.
            PlayerData[] candidates = FindObjectsByType<PlayerData>(FindObjectsSortMode.None);
            foreach (PlayerRef playerRef in runner.ActivePlayers)
            {
                waitingParticipants.Add(playerRef);
                foreach (PlayerData data in candidates)
                {
                    if (!IsReady(data) || data.Runner != runner || data.Object.InputAuthority != playerRef)
                        continue;
                    players.Add(data);
                    if (playerRef == runner.LocalPlayer)
                        localPlayerData = data;
                    break;
                }
            }

            // Host/client enumeration order is not a UI ordering contract.
            waitingParticipants.Sort((a, b) => a.RawEncoded.CompareTo(b.RawEncoded));
            players.Sort((a, b) => a.Object.InputAuthority.RawEncoded.CompareTo(b.Object.InputAuthority.RawEncoded));
            if (localPlayerData != null)
            {
                foreach (PlayerData data in players)
                {
                    if (data != localPlayerData && data.teamIndex != localPlayerData.teamIndex)
                    {
                        opponentPlayerData = data;
                        break;
                    }
                }
            }
        }

        foreach (PlayerData data in players)
            states.Add(UserState.Read(data));
        ApplyProfile(localProfileImage, localPlayerData);
        ApplyProfile(opponentProfileImage, opponentPlayerData);
        RefreshWaitingRoom(network, runner);
        UserState local = UserState.Read(localPlayerData);
        UserState opponent = UserState.Read(opponentPlayerData);
        bool localChanged = !hasSnapshot || !local.Equals(previousLocal);
        bool opponentChanged = !hasSnapshot || !opponent.Equals(previousOpponent);
        bool allChanged = !hasSnapshot || states.Count != previousStates.Count;
        for (int i = 0; !allChanged && i < states.Count; i++)
            allChanged = !states[i].Equals(previousStates[i]);

        previousLocal = local;
        previousOpponent = opponent;
        previousStates.Clear();
        previousStates.AddRange(states);
        hasSnapshot = true;

        if (localChanged)
            onLocalUserInfoChanged.Invoke(localPlayerData);
        if (opponentChanged)
            onOpponentUserInfoChanged.Invoke(opponentPlayerData);
        if (allChanged || localChanged || opponentChanged)
        {
            onUsersChanged.Invoke();
            UsersChanged?.Invoke();
        }
    }

    public PlayerData GetPlayerData(PlayerRef playerRef)
    {
        foreach (PlayerData data in players)
            if (IsReady(data) && data.Object.InputAuthority == playerRef)
                return data;
        return null;
    }

    private static bool IsReady(PlayerData data)
    {
        return data != null && data.Object != null && data.Object.IsValid &&
               data.Runner != null && data.Runner.IsRunning &&
               data.IsLoadoutInitialized && data.teamIndex > 0;
    }

    private void OnDisable()
    {
        ResetLobbyPreview();
        ApplyProfile(localProfileImage, null);
        ApplyProfile(opponentProfileImage, null);
        localPlayerData = null;
        opponentPlayerData = null;
        players.Clear();
        waitingParticipants.Clear();
        RefreshWaitingRoom(null, null);
        if (waitingRoomStartButton != null) waitingRoomStartButton.interactable = false;
        states.Clear();
        previousStates.Clear();
        previousLocal = default;
        previousOpponent = default;
        hasSnapshot = false;
    }
}
