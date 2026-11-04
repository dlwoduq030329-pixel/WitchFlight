using Fusion;

public partial class Player
{
    private TickTimer nextAllyHitVoice;
    [Networked] public int LockFailureSequence { get; private set; }
    private int lockAudioCastSequence, lockAudioSlot;

    // Send from the persistent shooter, not the short-lived/distant projectile.
    public void NotifySummonKillAudio(UnityEngine.Vector3 position)
    {
        if (Object != null && Object.IsValid && Object.HasStateAuthority) RPC_SummonKillAudio(position);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_SummonKillAudio(UnityEngine.Vector3 position)
    {
        BattleAudio.Play(BattleSound.CatKillVoice, position, local: Object.HasInputAuthority);
    }

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
        BattleAudio.Play(BattleSound.AllyHitVoice, LockAimPoint, transform, local: true);
    }
}
