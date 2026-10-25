using Fusion;

public partial class Player
{
    // Only successful authoritative gameplay invokes this. Audio clips stay local to each client.
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_PlayBattleSound(BattleSound sound)
    {
        BattleAudio.Play(sound, LockAimPoint, transform, Object.HasInputAuthority);
    }
}
