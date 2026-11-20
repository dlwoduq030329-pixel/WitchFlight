using Fusion;

public partial class PlayerData
{
    [Networked] public NetworkId TeamPingTarget { get; private set; }
    [Networked] public TickTimer TeamPingHeadTimer { get; private set; }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    public void RPC_SetTeamPing(NetworkId targetId)
    {
        if (!targetId.IsValid)
        {
            TeamPingTarget = default;
            TeamPingHeadTimer = TickTimer.None;
            return;
        }
        if (IsMatchBot || !Runner.TryFindObject(targetId, out var targetObject)) return;
        var target = targetObject.GetComponent<Player>();
        if (target == null || !target.IsAlive || target.TeamIndex <= 0 || target.TeamIndex == teamIndex) return;
        Player sender = null;
        foreach (var player in Player.ActiveCombatants)
            if (player != null && player.Object != null && player.Object.IsValid && player.Runner == Runner &&
                player.ParticipantRef == Object.InputAuthority && player.IsAlive) { sender = player; break; }
        if (sender == null || !target.IsTargetableBy(sender)) return;
        TeamPingTarget = targetId;
        TeamPingHeadTimer = TickTimer.CreateFromSeconds(Runner, 3f);
    }
}
