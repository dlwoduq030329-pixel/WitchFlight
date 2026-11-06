using Fusion;
using UnityEngine;

public struct RobbyInputData : INetworkInput
{
    public Vector2 move;
    public float yaw;
}

// Lobby-only movement. Uses its own input type, never the battle Player/input data.
[DisallowMultipleComponent]
public sealed class PlayerRobby : NetworkBehaviour
{
    public static PlayerRobby LocalPlayer { get; private set; }
    [SerializeField, Min(0f)] private float moveSpeed = 5f;

    public override void FixedUpdateNetwork()
    {
        // Host owns movement; NetworkTransform interpolates it for other clients.
        if (!Object.HasStateAuthority || !GetInput(out RobbyInputData input)) return;
        var manager = NetworkGameManager.Instance;
        if (manager == null || !manager.IsWaitingInCodeRoom) return;
        transform.Rotate(0f, input.yaw, 0f, Space.World);
        Vector2 move = Vector2.ClampMagnitude(input.move, 1f);
        Vector3 direction = transform.right * move.x + transform.forward * move.y;
        transform.position += direction * (moveSpeed * Runner.DeltaTime);
    }

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
