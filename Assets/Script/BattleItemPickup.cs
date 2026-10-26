using System.Collections.Generic;
using Fusion;
using UnityEngine;

public enum BattleItemType { Booster = 0, HealthPotion = 1, ManaPotion = 2 }

// Place the network prefab in the battle scene. The host owns contact, consumption
// and respawn; the shared state also works for a client receiving a late snapshot.
[DisallowMultipleComponent, RequireComponent(typeof(NetworkObject))]
public sealed class BattleItemPickup : NetworkBehaviour
{
    [SerializeField] private BattleItemType itemType;
    [Header("Effect")]
    [Tooltip("HP 포션의 고정 회복량. 최대 체력을 넘지 않으며 체력이 가득 찼으면 획득하지 않습니다.")]
    [SerializeField, Min(0f)] private float healAmount = 40f;
    [Tooltip("MP 포션의 고정 회복량. 최대 마나를 넘지 않으며 마나가 가득 찼으면 획득하지 않습니다.")]
    [SerializeField, Min(0f)] private float manaRestoreAmount = 40f;
    [Tooltip("부스터 이동속도 배율. 1.3이면 30% 증가. 재획득 시 배율을 중첩하지 않습니다.")]
    [SerializeField, Min(1f)] private float speedMultiplier = 1.3f;
    [SerializeField, Min(0.1f)] private float boostDurationSeconds = 5f;
    [Header("Pickup / respawn")]
    [Tooltip("획득 판정 반경(월드 단위). 모델 배율과 독립적이며 선택 시 구로 표시됩니다.")]
    [SerializeField, Min(0.1f)] private float pickupRadius = 1.5f;
    [SerializeField] private Vector3 pickupOffset;
    [Tooltip("획득 후 재등장 시간. 0이면 해당 라운드에는 재등장하지 않습니다. 새 라운드에서는 초기화됩니다.")]
    [SerializeField, Min(0f)] private float respawnSeconds = 20f;
    [Header("Presentation")]
    [Tooltip("숨길 모델 자식 오브젝트. NetworkObject가 있는 루트는 지정하지 마세요.")]
    [SerializeField] private GameObject visualRoot;

    [Networked] public NetworkBool Consumed { get; private set; }
    [Networked] private TickTimer RespawnAt { get; set; }
    [Networked] private int ItemRound { get; set; }

    private struct PositionSample { public NetworkId objectId; public Vector3 center; }
    private readonly Dictionary<PlayerRef, PositionSample> previousPositions = new();
    private readonly RaycastHit[] sightHits = new RaycastHit[32];
    private Renderer[] renderers;
    private bool[] originalRenderingOff;
    private bool ready;
    private Vector3 PickupPoint => transform.TransformPoint(pickupOffset);

    private void Awake()
    {
        // A collectible must not behave as a wall or block magic. Pickup itself uses a
        // swept capsule test, not OnTriggerEnter (which can miss a fast flyer).
        foreach (Collider itemCollider in GetComponentsInChildren<Collider>(true))
            itemCollider.enabled = false;
        if (visualRoot != null && (visualRoot == gameObject || !visualRoot.transform.IsChildOf(transform)))
        {
            Debug.LogWarning("Battle item Visual Root must be a model child; using renderers instead.", this);
            visualRoot = null;
        }
        renderers = GetComponentsInChildren<Renderer>(true);
        originalRenderingOff = new bool[renderers.Length];
        for (int i = 0; i < renderers.Length; i++) originalRenderingOff[i] = renderers[i].forceRenderingOff;
    }

    public override void Spawned()
    {
        ready = true;
        previousPositions.Clear();
        RefreshVisibility();
    }

    public override void FixedUpdateNetwork()
    {
        if (!Object.HasStateAuthority) return;
        BattleManager battle = BattleManager.Instance;
        BattleFlag flag = BattleFlag.Instance;
        if (battle == null || flag == null || flag.Object == null || !flag.Object.IsValid || flag.Runner != Runner)
        {
            previousPositions.Clear();
            return;
        }
        if (ItemRound != battle.RoundNumber)
        {
            ItemRound = battle.RoundNumber;
            Consumed = false;
            RespawnAt = TickTimer.None;
            previousPositions.Clear();
        }
        if (!battle.IsGameplayActive)
        {
            previousPositions.Clear();
            return;
        }
        if (Consumed && RespawnAt.Expired(Runner))
        {
            Consumed = false;
            RespawnAt = TickTimer.None;
            previousPositions.Clear();
        }
        if (Consumed)
        {
            previousPositions.Clear();
            return;
        }
        CheckPickup();
    }

    private void CheckPickup()
    {
        Player candidate = null;
        float nearest = float.PositiveInfinity;
        Vector3 point = PickupPoint;
        foreach (PlayerRef playerRef in BattleParticipants.Refs(Runner))
        {
            if (!BattleParticipants.TryGetPlayerObject(Runner, playerRef, out NetworkObject playerObject) ||
                playerObject == null || !playerObject.IsValid)
            {
                previousPositions.Remove(playerRef);
                continue;
            }
            Player player = playerObject.GetComponent<Player>();
            if (player == null || player.Runner != Runner || !player.CanReceiveBattleItem(itemType))
            {
                previousPositions.Remove(playerRef);
                continue;
            }

            BattleFlag.GetCapsule(player, out Vector3 center, out Vector3 halfAxis, out float radius);
            Vector3 previous = previousPositions.TryGetValue(playerRef, out PositionSample sample) &&
                sample.objectId == playerObject.Id ? sample.center : center;
            // Do not collect items along a teleport/respawn path.
            float maxSweep = Mathf.Max(5f, Mathf.Abs(player.CurrentSpeed) * Runner.DeltaTime * 2f + 2f);
            if ((center - previous).sqrMagnitude > maxSweep * maxSweep) previous = center;
            previousPositions[playerRef] = new PositionSample { objectId = playerObject.Id, center = center };
            float distance = BattleFlag.SegmentDistanceSquared(previous, center,
                point - halfAxis, point + halfAxis, out Vector3 contact);
            float contactRadius = radius + Mathf.Max(0.1f, pickupRadius);
            if (distance <= contactRadius * contactRadius && distance < nearest && HasPickupSight(contact, point))
            {
                candidate = player;
                nearest = distance;
            }
        }
        if (candidate != null) TryCollect(candidate);
    }

    private bool TryCollect(Player player)
    {
        if (!Object.HasStateAuthority || Consumed || player == null || player.Runner != Runner ||
            !player.TryApplyBattleItem(itemType, healAmount, speedMultiplier, boostDurationSeconds, manaRestoreAmount)) return false;
        // Consumed is written in the same authority tick as the effect: only one player wins.
        Consumed = true;
        RespawnAt = respawnSeconds > 0f && !float.IsInfinity(respawnSeconds)
            ? TickTimer.CreateFromSeconds(Runner, respawnSeconds) : TickTimer.None;
        previousPositions.Clear();
        return true;
    }

    private bool HasPickupSight(Vector3 contact, Vector3 point)
    {
        Vector3 delta = point - contact;
        if (delta.sqrMagnitude < 0.0001f) return true;
        int count = Physics.RaycastNonAlloc(contact, delta.normalized, sightHits, delta.magnitude,
            ~0, QueryTriggerInteraction.Ignore);
        // A saturated buffer must not allow collection through an unreported wall.
        if (count >= sightHits.Length) return false;
        for (int i = 0; i < count; i++)
        {
            Collider hit = sightHits[i].collider;
            if (hit == null || hit.transform.IsChildOf(transform) ||
                hit.GetComponentInParent<Player>() != null || hit.GetComponentInParent<MagicProjectile>() != null)
                continue;
            return false;
        }
        return true;
    }

    public override void Render() => RefreshVisibility();

    private void RefreshVisibility()
    {
        bool visible = ready && Object != null && Object.IsValid && !Consumed &&
            BattleManager.Instance != null && BattleManager.Instance.IsGameplayActive;
        if (visualRoot != null)
        {
            if (visualRoot.activeSelf != visible) visualRoot.SetActive(visible);
        }
        else if (renderers != null)
        {
            for (int i = 0; i < renderers.Length; i++)
                if (renderers[i] != null) renderers[i].forceRenderingOff = !visible || originalRenderingOff[i];
        }
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        ready = false;
        previousPositions.Clear();
        RefreshVisibility();
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = itemType == BattleItemType.HealthPotion ? Color.green :
            itemType == BattleItemType.ManaPotion ? new Color(0.3f, 0.5f, 1f) : Color.cyan;
        Gizmos.DrawWireSphere(PickupPoint, Mathf.Max(0.1f, pickupRadius));
    }
}
