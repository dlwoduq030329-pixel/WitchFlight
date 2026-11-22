using Fusion;
using UnityEngine;

public partial class MagicProjectile
{
    private void PresentEnvironmentContact(Collider collider, Vector3 point, Vector3 normal)
    {
        if (EnvironmentImpactVfx.IsEnvironment(collider)) RPC_EnvironmentImpact(point, normal);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_EnvironmentImpact(Vector3 point, Vector3 normal)
    {
        Player caster = Runner.TryFindObject(ShooterId, out NetworkObject shooter) && shooter != null
            ? shooter.GetComponent<Player>() : null;
        EnvironmentImpactVfx.Play(point, normal, caster);
    }
}
