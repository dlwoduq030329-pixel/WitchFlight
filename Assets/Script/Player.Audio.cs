using Fusion;

public partial class Player
{
    private TickTimer nextAllyHitVoice;

    // Only successful authoritative gameplay invokes this. Audio clips stay local to each client.
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_PlayBattleSound(BattleSound sound)
    {
        if (sound == BattleSound.LockFailed && !Object.HasInputAuthority) return;
        BattleAudio.Play(sound, LockAimPoint, transform, Object.HasInputAuthority);
    }
    private void NotifyAllyHitVoice()
    {
        if (TimerIsActive(nextAllyHitVoice)) return;
        nextAllyHitVoice = TickTimer.CreateFromSeconds(Runner, 6f);
        RPC_AllyHitVoice();
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_AllyHitVoice()
    {
        Player listener = LocalPlayer;
        if (listener == null || listener == this || listener.Object == null || !listener.Object.IsValid ||
            listener.Runner != Runner || !listener.IsAlive || listener.TeamIndex <= 0 || listener.TeamIndex != TeamIndex) return;
        BattleAudio.Play(BattleSound.AllyHitVoice, local: true);
    }
}
