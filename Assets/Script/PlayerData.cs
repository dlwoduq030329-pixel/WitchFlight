using Fusion;
using UnityEngine;

public enum HatType { None, Classic, Twisted, Elemental, Serenity, Cosmic }
public enum BroomType { None, Slow, Standard, Speed }
public enum MagicType { None, Fire, Ice, Vision, Thunder, Flare, Smoke, Dark, Decoy, Mine, Scane }
public enum Camp { A, B }

public class PlayerData : NetworkBehaviour
{
    // Lobby presentation acknowledgements are host-local; PlayerData is still the RPC owner.
    private PlayerData previewedOpponent;
    private int previewedOpponentProfile, previewedOwnProfile;

    public bool HasPresentedLobbyOpponent(PlayerData opponent)
    {
        return opponent != null && previewedOpponent == opponent &&
            previewedOpponentProfile == opponent.playerprofile && previewedOwnProfile == playerprofile;
    }

    public void ConfirmLobbyProfilePreview(PlayerData opponent)
    {
        if (Object == null || !Object.IsValid || !Object.HasInputAuthority ||
            opponent == null || opponent.Object == null || !opponent.Object.IsValid) return;
        RPC_ConfirmLobbyProfilePreview(opponent.Object.InputAuthority, opponent.playerprofile, playerprofile);
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    private void RPC_ConfirmLobbyProfilePreview(PlayerRef opponentRef, int opponentProfile, int ownProfile)
    {
        NetworkGameManager manager = NetworkGameManager.Instance;
        if (manager == null || manager.Runner != Runner || !manager.IsRandomMatch || manager.IsBattleSceneLoaded) return;
        PlayerData opponent = manager.GetPlayerData(opponentRef);
        if (!IsLoadoutInitialized || opponent == null || opponent == this ||
            !opponent.IsLoadoutInitialized || teamIndex <= 0 || opponent.teamIndex <= 0 ||
            opponent.teamIndex == teamIndex || opponent.playerprofile != opponentProfile || playerprofile != ownProfile) return;
        previewedOpponent = opponent;
        previewedOpponentProfile = opponentProfile;
        previewedOwnProfile = ownProfile;
    }

    public void ClearLobbyProfilePreview()
    {
        if (Object != null && Object.IsValid && Object.HasInputAuthority)
            RPC_ClearLobbyProfilePreview();
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    private void RPC_ClearLobbyProfilePreview()
    {
        previewedOpponent = null;
    }

    // Profile image ID only. Image selection/rendering and backend storage are separate.
    [Networked] public int playerprofile { get; private set; } = 0;
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
        if (Object.HasStateAuthority)
            IsRoomOwner = Runner.IsServer && Object.InputAuthority == Runner.LocalPlayer;
        if (Object.HasInputAuthority)
        {
            // Only the local owner's loaded DataConfig supplies this player's name.
            RPC_SetLoadout(DataConfig.GetPlayerConfig(),
                (MagicType)DataConfig.magic1Index, (MagicType)DataConfig.magic2Index,
                DataConfig.playerprofile, NormalizePlayerName(DataConfig.playerName));
        }

        base.Spawned();

        // 호스트에서만 팀 배정과 PlayerData Dictionary 관리를 수행한다.
        if (Object.HasStateAuthority)
        {
            NetworkGameManager.Instance?.AssignTeamIndex(this);
            NetworkGameManager.Instance?.RegisterPlayerData(this);
        }
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    private void RPC_SetLoadout(PlayerConfig selectedConfig,
        MagicType selectedMagic1, MagicType selectedMagic2, int selectedProfile, string selectedName)
    {
        selectedConfig = selectedConfig.Sanitized();
        hat = NormalizeHat((HatType)selectedConfig.hatIndex);
        broom = NormalizeBroom((BroomType)selectedConfig.broomIndex);
        magic1 = NormalizeMagic(selectedMagic1, MagicType.Fire);
        magic2 = NormalizeMagic(selectedMagic2, MagicType.Ice);
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
        playerName = NormalizePlayerName(selectedName);
        IsLoadoutInitialized = true;
        NetworkGameManager.Instance?.NotifyPlayerDataInitialized(this);
    }

    private string NormalizePlayerName(string value)
    {
        string name = string.IsNullOrWhiteSpace(value) ? $"Player {Object.InputAuthority.PlayerId}" : value.Trim();
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
        return value >= (int)MagicType.None && value <= (int)MagicType.Scane
            ? selectedMagic
            : fallback;
    }

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
        NetworkGameManager.Instance?.UnregisterPlayerData(this);
    }
}
