using System.Collections.Generic;
using Fusion;
using UnityEngine;

public sealed class RaidPotionPickup : NetworkBehaviour
{
    [SerializeField] private BattleItemType itemType;
    [SerializeField] private GameObject visualRoot;
    [SerializeField] private float pickupRadius = 1.5f;
    [Networked] private NetworkId BossId { get; set; }
    [Networked] public bool Available { get; private set; }
    [Networked] public Vector3 PickupPosition { get; private set; }
    [Networked] private TickTimer NextLocation { get; set; }
    private GiantMeowBoss boss;
    private float nextResolve;
    private Renderer[] renderers;
    private struct Sample { public NetworkId id; public Vector3 position; }
    private readonly Dictionary<PlayerRef, Sample> previous = new(4);
    private readonly RaycastHit[] sightHits = new RaycastHit[32];

    private void Awake()
    {
        foreach (var collider in GetComponentsInChildren<Collider>(true)) collider.enabled = false;
        renderers = GetComponentsInChildren<Renderer>(true);
    }
    public void Configure(GiantMeowBoss owner) { boss = owner; BossId = owner.Object.Id; }
    public override void Spawned()
    {
        previous.Clear();
        if (boss == null && Runner.TryFindObject(BossId, out var owner)) boss = owner.GetComponent<GiantMeowBoss>();
    }
    public override void FixedUpdateNetwork()
    {
        ResolveBoss();
        if (!Object.HasStateAuthority) return;
        if (boss == null || boss.Object == null || !boss.Object.IsValid || !boss.IsTargetable)
        { previous.Clear(); return; }
        if (!Available)
        {
            if (!NextLocation.ExpiredOrNotRunning(Runner)) return;
            if (boss.TryPotionPosition(PickupPosition, out var point))
            { PickupPosition = point; transform.position = point; Available = true; previous.Clear(); }
            else NextLocation = TickTimer.CreateFromSeconds(Runner, .5f);
            return;
        }
        foreach (var player in Player.ActiveCombatants)
        {
            if (player == null || player.Object == null || !player.Object.IsValid || player.Runner != Runner) continue;
            if (!player.IsAlive || player.IsReturningToMap)
            { previous.Remove(player.ParticipantRef); continue; }
            BattleFlag.GetCapsule(player, out var center, out var halfAxis, out float radius);
            Vector3 before = previous.TryGetValue(player.ParticipantRef, out var sample) && sample.id == player.Object.Id ? sample.position : center;
            float sweep = Mathf.Max(5f, Mathf.Abs(player.CurrentSpeed) * Runner.DeltaTime * 2f + 2f);
            if ((center - before).sqrMagnitude > sweep * sweep) before = center;
            previous[player.ParticipantRef] = new Sample { id = player.Object.Id, position = center };
            // Keep the movement sample even at full HP/MP: a player can spend mana
            // or take damage during the same fast pass through the pickup.
            if (!player.CanReceiveBattleItem(itemType)) continue;
            float distance = BattleFlag.SegmentDistanceSquared(before, center, PickupPosition - halfAxis, PickupPosition + halfAxis, out var contact);
            float contactRadius = radius + Mathf.Max(.1f, pickupRadius);
            if (distance > contactRadius * contactRadius || !HasPickupSight(contact)) continue;
            if (!player.TryApplyBattleItem(itemType, boss.Settings.potionRestoreAmount, 1f, 0f, boss.Settings.potionRestoreAmount)) continue;
            PlayerData.FindPerkData(Runner, player.ParticipantRef)?.RecordMission(MissionObjective.ItemCollected);
            Available = false;
            NextLocation = TickTimer.CreateFromSeconds(Runner, boss.Settings.potionRespawnSeconds);
            previous.Clear();
            break;
        }
    }
    private bool HasPickupSight(Vector3 contact)
    {
        Vector3 delta = PickupPosition - contact;
        if (delta.sqrMagnitude < .0001f) return true;
        // Pickup and spawn must query the same Fusion physics scene. A combat
        // explosion's global overlap query can reject an otherwise valid pickup.
        int count = Runner.GetPhysicsScene().Raycast(contact, delta.normalized, sightHits,
            delta.magnitude, ~0, QueryTriggerInteraction.Ignore);
        if (count >= sightHits.Length) return false;
        for (int i = 0; i < count; i++)
        {
            var hit = sightHits[i].collider;
            if (hit == null || hit.transform.IsChildOf(transform) ||
                hit.GetComponentInParent<Player>() != null || hit.GetComponentInParent<MagicProjectile>() != null ||
                hit.GetComponentInParent<RaidPotionPickup>() != null) continue;
            return false;
        }
        return true;
    }
    public override void Despawned(NetworkRunner runner, bool hasState) => previous.Clear();
    public override void Render()
    {
        ResolveBoss();
        transform.position = PickupPosition;
        bool show = Available && boss != null && boss.Object != null && boss.Object.IsValid && boss.IsTargetable;
        if (visualRoot != null && visualRoot != gameObject) visualRoot.SetActive(show);
        else foreach (var renderer in renderers) if (renderer != null) renderer.forceRenderingOff = !show;
    }
    private void ResolveBoss()
    {
        if (boss != null || Time.unscaledTime < nextResolve) return;
        nextResolve = Time.unscaledTime + .5f;
        if (Runner.TryFindObject(BossId, out var obj) && obj != null) boss = obj.GetComponent<GiantMeowBoss>();
    }
}
