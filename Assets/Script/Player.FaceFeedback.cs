using Fusion;

public partial class Player
{
    private TickTimer faceMissCooldown;
    public void NotifyFaceMiss()
    {
        if (Object == null || !Object.IsValid || !Object.HasStateAuthority || IsMatchBot || !IsAlive ||
            BattleManager.Instance == null || !BattleManager.Instance.IsGameplayActive ||
            !faceMissCooldown.ExpiredOrNotRunning(Runner)) return;
        faceMissCooldown = TickTimer.CreateFromSeconds(Runner, .6f);
        RPC_FaceMiss();
    }
    [Rpc(RpcSources.StateAuthority, RpcTargets.InputAuthority)]
    private void RPC_FaceMiss()
    {
        if (Object != null && Object.IsValid && Object.HasInputAuthority) BattleFacePortrait.Miss(this);
    }
}
