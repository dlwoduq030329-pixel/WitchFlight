using Fusion;

public partial class Player
{
    // Ephemeral presentation, not simulated combat state. No Sprite/texture is sent.
    private TickTimer emoteRepeatTimer;

    public bool RequestEmote(int emoteId)
    {
        if (Object == null || !Object.IsValid || !Object.HasInputAuthority || !IsAlive ||
            Runner == null || !Runner.IsRunning || BattleManager.Instance == null ||
            !BattleManager.Instance.IsGameplayActive || EmoteTable.Default == null ||
            !EmoteTable.Default.TryGet(emoteId, out _)) return false;
        RPC_RequestEmote(emoteId);
        return true;
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority, Channel = RpcChannel.Reliable)]
    private void RPC_RequestEmote(int emoteId)
    {
        EmoteTable table = EmoteTable.Default;
        if (!IsAlive || IsReturningToMap || ParticipantRef == PlayerRef.None ||
            BattleManager.Instance == null || !BattleManager.Instance.IsGameplayActive ||
            table == null || !table.TryGet(emoteId, out _) || TimerIsActive(emoteRepeatTimer)) return;
        emoteRepeatTimer = TickTimer.CreateFromSeconds(Runner, table.MinimumInterval);
        RPC_PresentEmote(emoteId);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All, Channel = RpcChannel.Reliable)]
    private void RPC_PresentEmote(int emoteId)
    {
        BattleEmoteUI.Present(this, emoteId);
        BattleAudio.Play(BattleSound.Emote, LockAimPoint, transform, Object.HasInputAuthority);
    }
}
