using Fusion;
using UnityEngine;

// Snapshot data, not a network prefab. The match flag owns these across player respawns.
public struct SmokeCloudState : INetworkStruct
{
    public Vector3 Center;
    public float Radius;
    public TickTimer Lifetime;
    public int Sequence;

    public bool Contains(Vector3 point) => Radius > 0f && (point - Center).sqrMagnitude <= Radius * Radius;

    public static bool ValidSettings(float radius, float seconds) =>
        radius > 0f && !float.IsInfinity(radius) && !float.IsNaN(radius) &&
        seconds > 0f && !float.IsInfinity(seconds) && !float.IsNaN(seconds);
}
