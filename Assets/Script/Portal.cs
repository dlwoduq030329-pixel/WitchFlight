using System;
using Fusion;
using UnityEngine;
using UnityEngine.Events;

// Scene-local trigger. Player performs the authoritative, replicated teleport.
[DisallowMultipleComponent]
[RequireComponent(typeof(BoxCollider), typeof(Rigidbody))]
public sealed class Portal : MonoBehaviour
{
    [Serializable]
    public sealed class PortalUsedEvent : UnityEvent<Player> { }

    [Tooltip("Place an empty Transform at the desired arrival position, outside portal triggers and walls.")]
    [SerializeField] private Transform destination;
    [Header("Exit direction")]
    [Tooltip("도착 시 바라볼 방향입니다. 빈 오브젝트의 파란색 +Z 축을 벽 반대쪽으로 향하게 하세요. 비워두면 Destination의 회전을 사용합니다. 이 오브젝트의 위치는 사용하지 않습니다.")]
    [SerializeField] private Transform exitDirection;
    [Tooltip("Destination에서 출구 방향으로 떨어져 나오는 거리(미터)입니다. 포탈 중심/벽에 겹치지 않도록 조절하세요. 이미 안전한 도착 지점을 지정했다면 0으로 설정할 수 있습니다.")]
    [SerializeField, Min(0f)] private float exitForwardOffset = 1.5f;

    [Header("Cooldown")]
    [Tooltip("After one player uses this portal, ALL players must wait this many seconds to use this portal again.")]
    [SerializeField, Min(0f)] private float cooldownSeconds = 3f;
    [Tooltip("Per-player delay before any portal can teleport them again.")]
    [SerializeField, Min(0.1f)] private float reentryDelay = 0.5f;

    [Header("Presentation hook (host only)")]
    [SerializeField] private PortalUsedEvent onPortalUsed = new PortalUsedEvent();

    private NetworkRunner cooldownRunner;
    private TickTimer cooldownTimer;
    private static readonly System.Collections.Generic.HashSet<Portal> activePortals = new();
    public static System.Collections.Generic.IEnumerable<Portal> ActivePortals => activePortals;
    private void OnEnable() => activePortals.Add(this);
    private void OnDisable() => activePortals.Remove(this);

    public bool TryGetBotRoute(NetworkRunner runner, out Vector3 entry, out Vector3 exit, out Quaternion rotation)
    {
        entry = exit = default; rotation = Quaternion.identity;
        if (!isActiveAndEnabled || destination == null || runner == null) return false;
        var trigger = GetComponent<BoxCollider>();
        if (trigger == null || !trigger.enabled ||
            cooldownRunner == runner && !cooldownTimer.ExpiredOrNotRunning(runner)) return false;
        entry = transform.TransformPoint(trigger.center);
        GetExitPose(out exit, out rotation);
        return true;
    }

    private void Reset() => ConfigureTrigger();

    private void Awake()
    {
        ConfigureTrigger();
        if (destination == null)
            Debug.LogWarning("Portal: assign Destination in the Inspector. This portal is inactive until assigned.", this);
    }

    private void ConfigureTrigger()
    {
        GetComponent<BoxCollider>().isTrigger = true;
        Rigidbody body = GetComponent<Rigidbody>();
        body.isKinematic = true;
        body.useGravity = false;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!isActiveAndEnabled || destination == null)
            return;

        // Also accepts a collider on one of the Player's children.
        Player player = other.GetComponentInParent<Player>();
        if (player == null || player.Object == null || !player.Object.IsValid ||
            !player.Object.HasStateAuthority || player.Runner == null || !player.Runner.IsRunning)
            return;

        if (cooldownRunner != player.Runner)
        {
            cooldownRunner = player.Runner;
            cooldownTimer = TickTimer.None;
        }
        if (!cooldownTimer.ExpiredOrNotRunning(cooldownRunner))
            return;
        GetExitPose(out Vector3 exitPosition, out Quaternion exitRotation);
        if (!player.TryQueuePortalTeleport(exitPosition, exitRotation, reentryDelay))
            return;

        // One cooldown per portal, shared by all players, including simultaneous entrants.
        cooldownTimer = TickTimer.CreateFromSeconds(cooldownRunner, Mathf.Max(0f, cooldownSeconds));

        // [연출 추가 위치] 여기에 포탈 사용 이펙트/사운드를 연결하세요.
        // On Portal Used Inspector 이벤트로도 연결할 수 있습니다.
        // 호스트에서 사용 승인 직후 호출되며, 실제 이동은 다음 Fusion 틱에 적용됩니다.
        // 다른 클라이언트에도 연출을 보이려면 별도로 RPC/네트워크 연출을 전달해야 합니다.
        onPortalUsed.Invoke(player);
    }

    private void GetExitPose(out Vector3 position, out Quaternion rotation)
    {
        rotation = (exitDirection != null ? exitDirection : destination).rotation;
        position = destination.position + rotation * Vector3.forward * Mathf.Max(0f, exitForwardOffset);
    }

    private void OnDrawGizmosSelected()
    {
        if (destination == null) return;
        GetExitPose(out Vector3 position, out Quaternion rotation);
        Vector3 forward = rotation * Vector3.forward;
        Vector3 right = rotation * Vector3.right;
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(position, 0.25f);
        Gizmos.DrawLine(destination.position, position);
        Vector3 tip = position + forward * 2f;
        Gizmos.DrawLine(position, tip);
        Gizmos.DrawLine(tip, tip - forward * 0.4f + right * 0.25f);
        Gizmos.DrawLine(tip, tip - forward * 0.4f - right * 0.25f);
    }
}
