using Fusion;
using UnityEngine;

// Lobby network identity only; intentionally independent of the battle Player.
// No input, combat logic or per-frame update loop.
[DisallowMultipleComponent]
public sealed class PlayerRobby : NetworkBehaviour
{
    public static PlayerRobby LocalPlayer { get; private set; }

    public override void Spawned()
    {
        if (Object.HasInputAuthority) LocalPlayer = this;
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        if (LocalPlayer == this) LocalPlayer = null;
    }

    private void OnDestroy()
    {
        if (LocalPlayer == this) LocalPlayer = null;
    }
}
