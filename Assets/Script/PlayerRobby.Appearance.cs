using Fusion;
using UnityEngine;

public sealed partial class PlayerRobby
{
    [SerializeField] private PlayerAppearance lobbyAppearance;
    [SerializeField] private GameObject[] lobbyHats;
    [Networked, OnChangedRender(nameof(ApplyLobbyAppearance))] private PlayerConfig LobbyAppearanceConfig { get; set; }
    [Networked, OnChangedRender(nameof(ApplyLobbyAppearance))] private NetworkBool LobbyAppearanceReady { get; set; }
    private PlayerConfig lastSentAppearance;
    private bool sentAppearance;
    private bool appearanceDirty;
    private bool appearanceApplied;
    private PlayerConfig lastAppliedAppearance;
    public bool IsLobbyPresentationReady => Object != null && Object.IsValid &&
        LobbyAppearanceReady && appearanceApplied;

    private void StartLobbyAppearance()
    {
        appearanceApplied = false;
        if (lobbyAppearance == null) lobbyAppearance = GetComponent<PlayerAppearance>();
        if (lobbyAppearance != null) lobbyAppearance.UnbindFromDataConfig();
        if (Object.HasInputAuthority)
        {
            DataConfig.Changed += QueueLobbyAppearance;
            sentAppearance = false;
            SendLobbyAppearance();
        }
        ApplyLobbyAppearance();
    }

    private void QueueLobbyAppearance() => appearanceDirty = true;

    private void FlushLobbyAppearance()
    {
        if (!appearanceDirty) return;
        appearanceDirty = false;
        SendLobbyAppearance();
    }

    private void SendLobbyAppearance()
    {
        if (Object == null || !Object.IsValid || !Object.HasInputAuthority) return;
        var config = DataConfig.GetPlayerConfig().Sanitized();
        if (sentAppearance && lastSentAppearance.Equals(config)) return;
        sentAppearance = true; lastSentAppearance = config;
        RPC_SetLobbyAppearance(config);
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    private void RPC_SetLobbyAppearance(PlayerConfig config)
    {
        if (NetworkGameManager.Instance == null || !NetworkGameManager.Instance.CanReceiveLobbyAppearance(Runner)) return;
        LobbyAppearanceConfig = config.Sanitized();
        LobbyAppearanceReady = true;
        PlayerData.FindPerkData(Runner, Object.InputAuthority)?.ApplyLobbyAppearanceConfig(LobbyAppearanceConfig);
        ApplyLobbyAppearance();
    }

    private void ApplyLobbyAppearance()
    {
        if (Object == null || !Object.IsValid || !LobbyAppearanceReady) return;
        var config = LobbyAppearanceConfig;
        if (appearanceApplied && lastAppliedAppearance.Equals(config)) return;
        using var timer = new RespawnWorkTimer("LobbyAvatar.ApplyReplicatedAppearance", this);
        lobbyAppearance?.ApplyPlayerConfig(config);
        if (lobbyHats != null)
            for (int i = 0; i < lobbyHats.Length; i++)
                if (lobbyHats[i] != null) lobbyHats[i].SetActive(i == config.hatIndex);
        lastAppliedAppearance = config;
        appearanceApplied = true;
    }

    private void StopLobbyAppearance()
    {
        DataConfig.Changed -= QueueLobbyAppearance;
        FlushLobbyAppearance();
    }
}
