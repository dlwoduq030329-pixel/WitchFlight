using System;
using System.Collections.Generic;
using Fusion;
using UnityEngine;
using UnityEngine.Events;

// Local UI bridge only. Fusion's existing PlayerData remains the source of truth.
[DisallowMultipleComponent]
public sealed class linkuserinfo : MonoBehaviour
{
    [Serializable]
    public sealed class PlayerDataEvent : UnityEvent<PlayerData> { }

    [Header("Refresh while this panel is active")]
    [SerializeField, Min(0.05f)] private float refreshInterval = 0.2f;

    [Header("Runtime references (resolved automatically)")]
    [SerializeField] private PlayerData localPlayerData;
    [SerializeField] private PlayerData opponentPlayerData;

    [Header("Optional UI hooks (null means the user is unavailable)")]
    [SerializeField] private PlayerDataEvent onLocalUserInfoChanged = new PlayerDataEvent();
    [SerializeField] private PlayerDataEvent onOpponentUserInfoChanged = new PlayerDataEvent();
    [SerializeField] private UnityEvent onUsersChanged = new UnityEvent();

    public PlayerData LocalPlayerData => IsReady(localPlayerData) ? localPlayerData : null;
    public PlayerData OpponentPlayerData => IsReady(opponentPlayerData) ? opponentPlayerData : null;
    public IReadOnlyList<PlayerData> AllPlayerDatas => players.AsReadOnly();
    public event Action UsersChanged;

    private readonly List<PlayerData> players = new List<PlayerData>();
    private readonly List<UserState> states = new List<UserState>();
    private readonly List<UserState> previousStates = new List<UserState>();
    private UserState previousLocal;
    private UserState previousOpponent;
    private bool hasSnapshot;
    private float nextRefreshTime;

    // Compare values, not just references: networked fields can change on the same object.
    private struct UserState
    {
        public PlayerData Data;
        public HatType Hat;
        public BroomType Broom;
        public MagicType Magic1, Magic2;
        public Camp Camp;
        public bool Ready;
        public int TeamIndex, HairLength, HairColor, ClothColor;
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
                HairLength = data.hairLength, HairColor = data.hairColor, ClothColor = data.clothColor,
                MaxHp = data.BattleMaxHp, Hp = data.BattleCurrentHp,
                MaxAp = data.BattleMaxAp, Ap = data.BattleCurrentAp,
                ApRecovery = data.BattleApRecoveryPerSecond
            };
        }
    }

    private void OnEnable()
    {
        hasSnapshot = false;
        RefreshUserInfo();
    }

    private void Update()
    {
        if (Time.unscaledTime >= nextRefreshTime)
            RefreshSnapshot();
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
        localPlayerData = null;
        opponentPlayerData = null;

        NetworkGameManager network = NetworkGameManager.Instance;
        NetworkRunner runner = network != null ? network.GetComponent<NetworkRunner>() : null;
        if (runner != null && runner.IsRunning)
        {
            // The manager's dictionary is host-only. Search replicated objects on BOTH peers.
            PlayerData[] candidates = FindObjectsByType<PlayerData>(FindObjectsSortMode.None);
            foreach (PlayerRef playerRef in runner.ActivePlayers)
            {
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
        localPlayerData = null;
        opponentPlayerData = null;
        players.Clear();
        states.Clear();
        previousStates.Clear();
        previousLocal = default;
        previousOpponent = default;
        hasSnapshot = false;
    }
}
