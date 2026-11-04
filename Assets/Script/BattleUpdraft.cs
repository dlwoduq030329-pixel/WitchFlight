using System.Collections.Generic;
using Fusion;
using UnityEngine;

// Reusable map pad. Contact is swept on the authoritative player tick, so fast flight
// doesn't depend on Unity trigger callbacks. Movement uses the player's replicated impulse.
[DisallowMultipleComponent]
public sealed class BattleUpdraft : MonoBehaviour
{
    [Header("Launch")]
    [Tooltip("발동 순간의 최소 상승 속도(월드 위쪽, m/s). 비행 속도 단계와 방향은 유지합니다.")]
    [SerializeField, Min(1f)] private float launchSpeed = 35f;
    [Tooltip("같은 플레이어가 이 상승기류를 다시 사용할 때의 최소 간격. 영역을 나갔다가 다시 들어와야 합니다.")]
    [SerializeField, Min(.1f)] private float cooldownSeconds = 1f;
    [Header("Contact area")]
    [Tooltip("발동 영역 반경(월드 미터). 프리팹의 모델 크기와 독립적입니다.")]
    [SerializeField, Min(.1f)] private float activationRadius = 2.5f;
    [SerializeField] private Vector3 activationOffset = new Vector3(0f, .5f, 0f);

    private struct Contact
    {
        public Vector3 center;
        public int tick;
        public bool inside;
        public TickTimer cooldown;
    }
    private static readonly HashSet<BattleUpdraft> active = new();
    private readonly Dictionary<Player, Contact> contacts = new();
    private readonly List<Player> stale = new();
    private readonly RaycastHit[] sightHits = new RaycastHit[32];
    private int round = -1;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRegistry() => active.Clear();
    private void OnEnable() { active.Add(this); contacts.Clear(); round = -1; }
    private void OnDisable() { active.Remove(this); contacts.Clear(); }
    private void Awake()
    {
        // A wind pad must not be a solid obstacle, damage players or intercept projectiles.
        foreach (Collider collider in GetComponentsInChildren<Collider>(true)) collider.enabled = false;
    }

    internal static void TickPlayer(Player player)
    {
        if (player == null || player.Object == null || !player.Object.IsValid || !player.Object.HasStateAuthority) return;
        foreach (BattleUpdraft pad in active)
            if (pad != null && pad.isActiveAndEnabled && pad.gameObject.scene == player.gameObject.scene) pad.Sample(player);
    }

    private void Sample(Player player)
    {
        if (BattleManager.Instance == null || !BattleManager.Instance.IsGameplayActive) return;
        if (round != BattleManager.Instance.RoundNumber) { round = BattleManager.Instance.RoundNumber; contacts.Clear(); }
        stale.Clear();
        foreach (Player known in contacts.Keys)
            if (known == null || known.Object == null || !known.Object.IsValid) stale.Add(known);
        foreach (Player known in stale) contacts.Remove(known);

        BattleFlag.GetCapsule(player, out Vector3 center, out Vector3 halfAxis, out float radius);
        Vector3 point = transform.TransformPoint(activationOffset);
        bool sampled = contacts.TryGetValue(player, out Contact state) && state.tick == player.Runner.Tick.Raw - 1;
        Vector3 previous = sampled ? state.center : center;
        // Teleports/respawns must not activate every pad between the old and new positions.
        float maxSweep = Mathf.Max(5f, player.ParryHintVelocity.magnitude * player.Runner.DeltaTime * 2f + 2f);
        if ((center - previous).sqrMagnitude > maxSweep * maxSweep) { previous = center; sampled = false; }
        float reach = radius + Mathf.Max(.1f, activationRadius);
        bool inside = BattleFlag.SegmentDistanceSquared(center, center, point - halfAxis, point + halfAxis, out _) <= reach * reach;
        bool crossed = BattleFlag.SegmentDistanceSquared(previous, center, point - halfAxis, point + halfAxis, out Vector3 contact) <= reach * reach;
        if (crossed && (!sampled || !state.inside) && state.cooldown.ExpiredOrNotRunning(player.Runner) &&
            HasSight(contact, point) && player.TryApplyUpdraft(launchSpeed))
            state.cooldown = TickTimer.CreateFromSeconds(player.Runner, Mathf.Max(.1f, cooldownSeconds));
        state.center = center;
        state.tick = player.Runner.Tick.Raw;
        state.inside = inside;
        contacts[player] = state;
    }

    private bool HasSight(Vector3 origin, Vector3 point)
    {
        Vector3 delta = point - origin;
        if (delta.sqrMagnitude < .0001f) return true;
        int count = Physics.RaycastNonAlloc(origin, delta.normalized, sightHits, delta.magnitude, ~0, QueryTriggerInteraction.Ignore);
        RaycastHit[] hits = sightHits;
        if (count == sightHits.Length) { hits = Physics.RaycastAll(origin, delta.normalized, delta.magnitude, ~0, QueryTriggerInteraction.Ignore); count = hits.Length; }
        for (int i = 0; i < count; i++)
            if (hits[i].collider != null && !hits[i].transform.IsChildOf(transform) && hits[i].collider.GetComponentInParent<Player>() == null)
                return false;
        return true;
    }

    private void OnDrawGizmosSelected()
    {
        Vector3 point = transform.TransformPoint(activationOffset);
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(point, Mathf.Max(.1f, activationRadius));
        Vector3 tip = point + Vector3.up * 3f;
        Gizmos.DrawLine(point, tip);
        Gizmos.DrawLine(tip, tip + new Vector3(-.4f, -.6f, 0f));
        Gizmos.DrawLine(tip, tip + new Vector3(.4f, -.6f, 0f));
    }
}
