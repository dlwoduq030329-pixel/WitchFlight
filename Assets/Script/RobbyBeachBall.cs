using Fusion;
using UnityEngine;

// Main lobby only: the host simulates physics and NetworkTransform replicates it.
[DisallowMultipleComponent]
public sealed class RobbyBeachBall : NetworkBehaviour
{
    [SerializeField] private Rigidbody body;
    [SerializeField, Min(0f)] private float kickSpeed = 8f;
    [SerializeField, Min(0f)] private float upwardSpeed = 3f;
    [SerializeField, Min(0f)] private float contactCooldown = 0.2f;
    private float nextKickTime;

    private void Awake()
    {
        if (body == null) body = GetComponent<Rigidbody>();
        if (body != null) body.isKinematic = true;
    }

    public override void Spawned()
    {
        if (body == null) return;
        body.isKinematic = !Object.HasStateAuthority;
        body.interpolation = RigidbodyInterpolation.None;
        body.collisionDetectionMode = Object.HasStateAuthority
            ? CollisionDetectionMode.ContinuousDynamic : CollisionDetectionMode.Discrete;
        nextKickTime = 0f;
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (body == null || Object == null || !Object.IsValid || !Object.HasStateAuthority ||
            Time.time < nextKickTime) return;
        PlayerRobby player = collision.collider.GetComponentInParent<PlayerRobby>();
        if (player == null || player.Object == null || !player.Object.IsValid || player.Runner != Runner) return;
        Vector3 away = body.worldCenterOfMass - player.transform.position;
        away.y = 0f;
        if (away.sqrMagnitude < 0.0001f) away = player.transform.forward;
        // Set a consistent kick speed instead of stacking unlimited impulses.
        body.linearVelocity = away.normalized * kickSpeed + Vector3.up * upwardSpeed;
        body.WakeUp();
        player.PlayBallKick();
        nextKickTime = Time.time + contactCooldown;
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        if (body != null) body.isKinematic = true;
    }
}
