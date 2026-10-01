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
    [Tooltip("After one player uses this portal, ALL players must wait this many seconds to use this portal again.")]
    [SerializeField, Min(0f)] private float cooldownSeconds = 3f;
    [Tooltip("Per-player delay before any portal can teleport them again.")]
    [SerializeField, Min(0.1f)] private float reentryDelay = 0.5f;

    [Header("Presentation hook (host only)")]
    [SerializeField] private PortalUsedEvent onPortalUsed = new PortalUsedEvent();

    private NetworkRunner cooldownRunner;
    private TickTimer cooldownTimer;

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
        if (!player.TryQueuePortalTeleport(destination.position, reentryDelay))
            return;

        // One cooldown per portal, shared by all players, including simultaneous entrants.
        cooldownTimer = TickTimer.CreateFromSeconds(cooldownRunner, Mathf.Max(0f, cooldownSeconds));

        // [연출 추가 위치] 여기에 포탈 사용 이펙트/사운드를 연결하세요.
        // On Portal Used Inspector 이벤트로도 연결할 수 있습니다.
        // 호스트에서 사용 승인 직후 호출되며, 실제 이동은 다음 Fusion 틱에 적용됩니다.
        // 다른 클라이언트에도 연출을 보이려면 별도로 RPC/네트워크 연출을 전달해야 합니다.
        onPortalUsed.Invoke(player);
    }
}
