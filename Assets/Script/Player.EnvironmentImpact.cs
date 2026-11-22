using Fusion;
using UnityEngine;

public partial class Player
{
    private RaycastHit? environmentImpactHit;
    private TickTimer environmentChannelImpactCooldown;

    private void PresentEnvironmentContact(Player victim, MagicProjectile summon, bool channel = false)
    {
        if (victim != null || summon != null || !environmentImpactHit.HasValue) return;
        if (channel && !environmentChannelImpactCooldown.ExpiredOrNotRunning(Runner)) return;
        if (channel) environmentChannelImpactCooldown = TickTimer.CreateFromSeconds(Runner, .2f);
        RaycastHit hit = environmentImpactHit.Value;
        RPC_EnvironmentImpact(hit.point, hit.normal);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_EnvironmentImpact(Vector3 point, Vector3 normal)
    {
        EnvironmentImpactVfx.Play(point, normal, this);
    }
}
