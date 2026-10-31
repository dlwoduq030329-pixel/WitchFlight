using Fusion;
using UnityEngine;
using System.Collections.Generic;

public enum HatType { None, Classic, Twisted, Elemental, Serenity, Cosmic }
public enum BroomType { None, Slow, Standard, Speed }
// Explicit IDs preserve existing saved loadouts. Removed spells are replaced in-place.
public enum MagicType { None = 0, Fire = 1, Ice = 2, Vision = 3, Thunder = 4, Healing = 5, Binding = 6, Dark = 7, Curse = 8, Mine = 9, Razier = 10, Flare = 11, Smoke = 12 }
public enum Camp { A, B }

public partial class PlayerData : NetworkBehaviour
{
    public static PlayerData Local { get; private set; }
    [Networked] public int MagicChangeRound { get; private set; }
    [Networked] public NetworkBool RoundSwapReady { get; private set; }
    [Networked] public MagicType MagicChangeBase1 { get; private set; }
    [Networked] public MagicType MagicChangeBase2 { get; private set; }
    [Networked] public MagicType InitialMagic1 { get; private set; }
    [Networked] public MagicType InitialMagic2 { get; private set; }
    public event System.Action<int, int, bool, string> RoundMagicChangeResult;
    // Lobby presentation acknowledgements are host-local; PlayerData is still the RPC owner.
    private readonly Dictionary<PlayerRef, (PlayerData data, int profile, int ownProfile, int team, int ownTeam)> lobbyPreviews = new();
    [Networked] public BattleMatchMode RoomMatchMode { get; private set; } = BattleMatchMode.TwoVsTwo;

    public void SetRoomMatchMode(BattleMatchMode mode)
    {
        if (Object == null || !Object.IsValid || !Object.HasStateAuthority || !IsRoomOwner) return;
        RoomMatchMode = System.Enum.IsDefined(typeof(BattleMatchMode), mode) ? mode : BattleMatchMode.TwoVsTwo;
    }

    public void RequestWaitingTeam(int team)
    {
        if (Object == null || !Object.IsValid || !Object.HasInputAuthority || (team != 1 && team != 2)) return;
        RPC_RequestWaitingTeam(team);
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    private void RPC_RequestWaitingTeam(int team)
    {
        NetworkGameManager.Instance?.TrySetWaitingTeam(this, team);
    }

    public bool HasPresentedLobbyOpponent(PlayerData opponent)
    {
        return opponent != null && opponent.Object != null && opponent.Object.IsValid &&
            lobbyPreviews.TryGetValue(opponent.ParticipantRef, out var preview) && preview.data == opponent &&
            preview.profile == opponent.playerprofile && preview.ownProfile == playerprofile &&
            preview.team == opponent.teamIndex && preview.ownTeam == teamIndex;
    }

    public void ConfirmLobbyProfilePreview(PlayerData opponent)
    {
        if (Object == null || !Object.IsValid || !Object.HasInputAuthority ||
            opponent == null || opponent.Object == null || !opponent.Object.IsValid) return;
        RPC_ConfirmLobbyProfilePreview(opponent.ParticipantRef, opponent.playerprofile, playerprofile);
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    private void RPC_ConfirmLobbyProfilePreview(PlayerRef opponentRef, int opponentProfile, int ownProfile)
    {
        NetworkGameManager manager = NetworkGameManager.Instance;
        if (manager == null || manager.Runner != Runner || !manager.IsRandomMatch || manager.IsBattleSceneLoaded) return;
        PlayerData opponent = manager.GetPlayerData(opponentRef);
        if (!IsLoadoutInitialized || opponent == null || opponent == this ||
            !opponent.IsLoadoutInitialized || teamIndex <= 0 || opponent.teamIndex <= 0 ||
            opponent.playerprofile != opponentProfile || playerprofile != ownProfile) return;
        lobbyPreviews[opponentRef] = (opponent, opponentProfile, ownProfile, opponent.teamIndex, teamIndex);
    }

    public void ClearLobbyProfilePreview()
    {
        if (Object != null && Object.IsValid && Object.HasInputAuthority)
            RPC_ClearLobbyProfilePreview();
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    private void RPC_ClearLobbyProfilePreview()
    {
        lobbyPreviews.Clear();
    }

    // Profile image ID only. Image selection/rendering and backend storage are separate.
    [Networked] public int playerprofile { get; private set; } = 0;
    [Networked] public int CatBodyIndex { get; private set; } = 1;
    [Networked] public int CatEyeIndex { get; private set; } = 1;
    // Compatibility: both names read the SAME replicated profile index (default 0).
    // Use SetPlayerProfile to change it; do not introduce a second networked copy.
    public int profileimage => playerprofile;
    // LoginPlayerData.nickname -> DataConfig.playerName -> owner's RPC -> all clients.
    [Networked] public NetworkString<_32> playerName { get; private set; }
    [Networked] public bool IsRoomOwner { get; private set; }
    [Networked] public HatType hat { get; set; }
    [Networked] public BroomType broom { get; set; }
    [Networked] public MagicType magic1 { get; set; }
    [Networked] public MagicType magic2 { get; set; }
    [Networked] public bool ready { get; set; }
    [Networked] public Camp camp { get; set; }
    [Networked] public int teamIndex { get; set; }
    [Networked] public int hairStylePreset { get; private set; }
    [Networked] public float bangsLength { get; private set; }
    [Networked] public float bangsDirection { get; private set; }
    [Networked] public float sideHairLength { get; private set; }
    [Networked] public float ahogeLength { get; private set; }
    [Networked] public Color hairColor { get; private set; } = Color.white;
    [Networked] public Color clothColor { get; private set; } = Color.white;
    [Networked] public Color eyeColor { get; private set; } = Color.white;
    [Networked] public int wandIndex { get; private set; }
    public int hairLength => Mathf.RoundToInt(bangsLength);

    public PlayerConfig GetPlayerConfig() => new PlayerConfig
    {
        hairStylePreset = hairStylePreset, bangsLength = bangsLength, bangsDirection = bangsDirection,
        sideHairLength = sideHairLength, ahogeLength = ahogeLength,
        hairColor = hairColor, clothColor = clothColor, eyeColor = eyeColor,
        hatIndex = (int)hat, broomIndex = (int)broom, wandIndex = wandIndex
    };



    [Networked] public bool IsLoadoutInitialized { get; private set; }

    // 전투 중인 Player의 체력/AP 상태를 보존하는 PlayerData 값입니다.
    [Networked] public float BattleMaxHp { get; private set; }
    [Networked] public float BattleCurrentHp { get; private set; }
    [Networked] public float BattleMaxAp { get; private set; }
    [Networked] public float BattleCurrentAp { get; private set; }
    [Networked] public float BattleApRecoveryPerSecond { get; private set; }

    public override void Spawned()
    {
        perkPlayers.Add(this);
        if (Object.HasStateAuthority)
            IsRoomOwner = Runner.IsServer && ParticipantRef == Runner.LocalPlayer;
        if (Object.HasInputAuthority)
        {
            Local = this;
            SetEquippedTitle(DataConfig.equippedTitleId);
            SyncEquippedVoice();
            int catBody = 1, catEyes = 1;
            if (DatabaseManager.Instance != null && DatabaseManager.Instance.TryGetCatAppearance(out int body, out int eyes))
            { catBody = body; catEyes = eyes; }
            // Only the local owner's loaded DataConfig supplies this player's name.
            RPC_SetLoadout(DataConfig.GetPlayerConfig(),
                (MagicType)DataConfig.magic1Index, (MagicType)DataConfig.magic2Index,
                DataConfig.playerprofile, NormalizePlayerName(DataConfig.playerName), catBody, catEyes);
        }

        base.Spawned();

        // 호스트에서만 팀 배정과 PlayerData Dictionary 관리를 수행한다.
        if (Object.HasStateAuthority && !IsMatchBot)
        {
            NetworkGameManager.Instance?.AssignTeamIndex(this);
            NetworkGameManager.Instance?.RegisterPlayerData(this);
        }
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    private void RPC_SetLoadout(PlayerConfig selectedConfig,
        MagicType selectedMagic1, MagicType selectedMagic2, int selectedProfile, string selectedName, int catBody, int catEyes)
    {
        // Initial account snapshot only. Mid-match changes use the validated round RPC below.
        if (IsLoadoutInitialized) return;
        selectedConfig = selectedConfig.Sanitized();
        hat = NormalizeHat((HatType)selectedConfig.hatIndex);
        broom = NormalizeBroom((BroomType)selectedConfig.broomIndex);
        magic1 = NormalizeMagic(selectedMagic1, MagicType.Fire);
        magic2 = NormalizeMagic(selectedMagic2, MagicType.Ice);
        InitialMagic1 = magic1;
        InitialMagic2 = magic2;
        hairStylePreset = selectedConfig.hairStylePreset;
        bangsLength = selectedConfig.bangsLength;
        bangsDirection = selectedConfig.bangsDirection;
        sideHairLength = selectedConfig.sideHairLength;
        ahogeLength = selectedConfig.ahogeLength;
        hairColor = selectedConfig.hairColor;
        clothColor = selectedConfig.clothColor;
        eyeColor = selectedConfig.eyeColor;
        wandIndex = selectedConfig.wandIndex;
        playerprofile = Mathf.Max(0, selectedProfile);
        CatBodyIndex = Mathf.Clamp(catBody, 1, 7);
        CatEyeIndex = Mathf.Clamp(catEyes, 1, 3);
        playerName = NormalizePlayerName(selectedName);
        IsLoadoutInitialized = true;
        NetworkGameManager.Instance?.NotifyPlayerDataInitialized(this);
    }

    private string NormalizePlayerName(string value)
    {
        string name = string.IsNullOrWhiteSpace(value) ? $"Player {ParticipantRef.PlayerId}" : value.Trim();
        // Keep the network string bounded and avoid cutting a UTF-16 surrogate pair.
        if (name.Length > 32)
            name = name.Substring(0, char.IsHighSurrogate(name[31]) ? 31 : 32);
        return name;
    }

    private static HatType NormalizeHat(HatType selectedHat)
    {
        return selectedHat == HatType.Classic ||
               selectedHat == HatType.Twisted ||
               selectedHat == HatType.Elemental ||
               selectedHat == HatType.Serenity ||
               selectedHat == HatType.Cosmic
            ? selectedHat
            : HatType.Classic;
    }

    // Call on YOUR PlayerData. Other players cannot update this owner's profile via RPC.
    public void SetPlayerProfile(int profileId)
    {
        if (Object == null || !Object.IsValid || !Object.HasInputAuthority) return;
        DataConfig.playerprofile = Mathf.Max(0, profileId);
        RPC_SetPlayerProfile(DataConfig.playerprofile);
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    private void RPC_SetPlayerProfile(int profileId)
    {
        playerprofile = Mathf.Max(0, profileId);
    }

    private static BroomType NormalizeBroom(BroomType selectedBroom)
    {
        return selectedBroom == BroomType.Slow ||
               selectedBroom == BroomType.Standard ||
               selectedBroom == BroomType.Speed
            ? selectedBroom
            : BroomType.Standard;
    }

    private static MagicType NormalizeMagic(MagicType selectedMagic, MagicType fallback)
    {
        // None (0) is an intentional empty slot and must survive backend/network loading.
        int value = (int)selectedMagic;
        return value >= (int)MagicType.None && value <= (int)MagicType.Smoke
            ? selectedMagic
            : fallback;
    }

    public void BeginRoundMagicChange(int round)
    {
        if (Object == null || !Object.IsValid || !Object.HasStateAuthority) return;
        MagicChangeBase1 = magic1;
        MagicChangeBase2 = magic2;
        MagicChangeRound = round;
        RoundSwapReady = false;
    }

    public bool RequestRoundReady(int round, bool readyForRound)
    {
        if (Object == null || !Object.IsValid || !Object.HasInputAuthority || !IsLoadoutInitialized) return false;
        RPC_SetRoundReady(round, readyForRound);
        return true;
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    private void RPC_SetRoundReady(int round, bool readyForRound)
        => BattleManager.Instance?.TrySetRoundReady(this, round, readyForRound);

    public void SetRoundReadyFromHost(bool value)
    {
        if (Object != null && Object.IsValid && Object.HasStateAuthority) RoundSwapReady = value;
    }

    public bool RequestRoundMagicChange(int round, MagicType first, MagicType second, int requestId, bool resetInitial = false)
    {
        if (Object == null || !Object.IsValid || !Object.HasInputAuthority || !IsLoadoutInitialized) return false;
        RPC_ChangeRoundMagic(round, first, second, requestId, resetInitial);
        return true;
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    private void RPC_ChangeRoundMagic(int round, MagicType first, MagicType second, int requestId, bool resetInitial)
    {
        string error = "전투 정보를 불러오지 못했습니다.";
        bool accepted = BattleManager.Instance != null &&
            BattleManager.Instance.TryChangeRoundMagic(this, round, first, second, out error, resetInitial);
        RPC_RoundMagicChangeResult(requestId, round, accepted, error);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.InputAuthority)]
    private void RPC_RoundMagicChangeResult(int requestId, int round, bool accepted, string error)
        => RoundMagicChangeResult?.Invoke(requestId, round, accepted, error);

    public void SetBattleHealth(float maxHp, float currentHp)
    {
        if (!Object.HasStateAuthority)
            return;

        BattleMaxHp = Mathf.Max(0f, maxHp);
        BattleCurrentHp = Mathf.Clamp(currentHp, 0f, BattleMaxHp);
    }

    public void SetBattleAp(float maxAp, float currentAp, float recoveryPerSecond)
    {
        if (!Object.HasStateAuthority)
            return;

        BattleMaxAp = Mathf.Max(0f, maxAp);
        BattleCurrentAp = Mathf.Clamp(currentAp, 0f, BattleMaxAp);
        BattleApRecoveryPerSecond = Mathf.Max(0f, recoveryPerSecond);
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        if (hasState && Object != null && Object.HasInputAuthority) TitleProgression.Observe(this, true);
        perkPlayers.Remove(this);
        if (Local == this) Local = null;
        NetworkGameManager.Instance?.UnregisterPlayerData(this);
    }

    private void OnDestroy() { perkPlayers.Remove(this); if (Local == this) Local = null; }
}
