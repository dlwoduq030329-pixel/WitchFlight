using Fusion;
using UnityEngine;

public sealed partial class GiantMeowBoss
{
    public const int MinionCount = 10;
    [SerializeField] private NetworkPrefabRef minionPrefab, healthPotionPrefab, manaPotionPrefab;
    private readonly RaidMinion[] minions = new RaidMinion[MinionCount];
    private readonly RaidPotionPickup[] potions = new RaidPotionPickup[8];
    private readonly Vector3[] minionPositions = new Vector3[MinionCount];
    private int pendingMinion, minionAppearanceOffset;
    private bool summoningMinions;
    private float minionSpawnRetry;
    private readonly Collider[] potionObstacles = new Collider[32];

    private void SpawnSupportPools()
    {
        if (!Object.HasStateAuthority) return;
        for (int i = 0; i < minions.Length; i++)
        {
            var obj = Runner.Spawn(minionPrefab, transform.position, transform.rotation, PlayerRef.None,
                onBeforeSpawned: (r, spawned) => spawned.GetComponent<RaidMinion>().Configure(this));
            if (obj != null) minions[i] = obj.GetComponent<RaidMinion>();
        }
        for (int i = 0; i < potions.Length; i++)
        {
            var obj = Runner.Spawn(i < 4 ? healthPotionPrefab : manaPotionPrefab, transform.position, Quaternion.identity, PlayerRef.None,
                onBeforeSpawned: (r, spawned) => spawned.GetComponent<RaidPotionPickup>().Configure(this));
            if (obj != null) potions[i] = obj.GetComponent<RaidPotionPickup>();
        }
    }

    private void SummonMinions()
    {
        minionAppearanceOffset = Random.Range(0, 7);
        pendingMinion = 0;
        summoningMinions = true;
        minionSpawnRetry = 0f;
        TickMinionSpawns();
    }

    private void TickMinionSpawns()
    {
        if (!summoningMinions) return;
        minionSpawnRetry -= Runner.DeltaTime;
        if (minionSpawnRetry > 0f) return;
        minionSpawnRetry = .5f;
        for (; pendingMinion < minions.Length; pendingMinion++)
        {
            if (minions[pendingMinion] == null) continue;
            if (!TryMinionPosition(pendingMinion, out var position)) return;
            minionPositions[pendingMinion] = position;
            minions[pendingMinion].Activate(position, (pendingMinion + minionAppearanceOffset) % 7 + 1, pendingMinion);
        }
        summoningMinions = false;
    }

    private bool TryMinionPosition(int index, out Vector3 position)
    {
        position = default;
        if (settings.boundary == null || !settings.boundary.TryGetBotFlightBounds(out var bounds)) return false;
        float low = Mathf.Clamp(settings.potionMinAltitude, bounds.min.y + 3f, bounds.max.y - 3f);
        float high = Mathf.Clamp(settings.potionMaxAltitude, low, bounds.max.y - 3f);
        float bestSeparation = 625f;
        bool found = false;
        // Choose the most separated valid sample, without allocating or spawning new objects.
        for (int attempt = 0; attempt < 48; attempt++)
        {
            Vector3 point = new Vector3(Random.Range(bounds.min.x + 3f, bounds.max.x - 3f),
                Random.Range(low, high), Random.Range(bounds.min.z + 3f, bounds.max.z - 3f));
            if ((point - transform.position).sqrMagnitude < Mathf.Pow(settings.hitRadius + 10f, 2f)) continue;
            if (Runner.GetPhysicsScene().OverlapSphere(point, 3f, potionObstacles, ~0, QueryTriggerInteraction.Ignore) != 0) continue;
            float separation = float.MaxValue;
            for (int previous = 0; previous < index; previous++)
                if (minions[previous] != null) separation = Mathf.Min(separation, (point - minionPositions[previous]).sqrMagnitude);
            if (separation < bestSeparation) continue;
            bestSeparation = separation;
            position = point;
            found = true;
        }
        return found;
    }

    public Player MinionTarget(int slot)
    {
        for (int i = 0; i < targetCount; i++)
        {
            var target = targets[(slot + i) % targetCount];
            if (CanHit(target)) return target;
        }
        return null;
    }

    public void FireMinion(Vector3 origin, Vector3 destination) =>
        LaunchFire(origin, destination, false, settings.minionDamage * settings.spellDamageMultiplier);

    public bool TryPotionPosition(Vector3 previous, out Vector3 position)
    {
        position = previous;
        if (settings.boundary == null || !settings.boundary.TryGetBotFlightBounds(out var bounds)) return false;
        float low = Mathf.Clamp(settings.potionMinAltitude, bounds.min.y, bounds.max.y);
        float high = Mathf.Clamp(settings.potionMaxAltitude, low, bounds.max.y);
        for (int attempt = 0; attempt < 32; attempt++)
        {
            Vector3 point = new Vector3(Random.Range(bounds.min.x, bounds.max.x), Random.Range(low, high), Random.Range(bounds.min.z, bounds.max.z));
            if ((point - previous).sqrMagnitude < 625f) continue;
            int count = Runner.GetPhysicsScene().OverlapSphere(point, 3f, potionObstacles, ~0, QueryTriggerInteraction.Ignore);
            if (count != 0) continue;
            bool nearby = false;
            foreach (var item in potions)
                if (item != null && item.Available && (item.PickupPosition - point).sqrMagnitude < 100f) { nearby = true; break; }
            if (nearby) continue;
            position = point;
            return true;
        }
        return false;
    }
}
