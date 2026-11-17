using System.Collections.Generic;
using Fusion;
using UnityEngine;

// Session-owned pool. Fusion still performs every spawn/despawn and owns replication.
public sealed class MagicNetworkObjectPool : NetworkObjectProviderDefault
{
    private const int Capacity = 512;
    private readonly Dictionary<NetworkObject, Stack<NetworkObject>> idle = new();
    private readonly Dictionary<NetworkObject, NetworkObject> origins = new();
    private readonly Dictionary<NetworkObject, bool> eligible = new();
    private readonly Dictionary<NetworkObject, PlayerRef> avatarOwners = new();
    private readonly Dictionary<(NetworkObject, PlayerRef), Stack<NetworkObject>> avatars = new();
    private PlayerRef acquiringOwner;
    public override NetworkObjectAcquireResult AcquirePrefabInstance(NetworkRunner runner, in NetworkPrefabAcquireContext context, out NetworkObject instance)
    {
        var previous = acquiringOwner;
        // Locally initiated Spawn reaches the provider before Fusion creates its meta.
        // Remote replication supplies Meta; local acquisition uses the shared bucket.
        acquiringOwner = context.Meta != null ? context.Meta.InputAuthority : PlayerRef.None;
        try { return base.AcquirePrefabInstance(runner, context, out instance); }
        finally { acquiringOwner = previous; }
    }
    private Transform storage;
    private int retained;

    public override void ReleaseInstance(NetworkRunner runner, in NetworkObjectReleaseContext context)
    {
        if (context.IsBeingDestroyed) { origins.Remove(context.Object); avatarOwners.Remove(context.Object); }
        base.ReleaseInstance(runner, context);
    }

    protected override NetworkObject InstantiatePrefab(NetworkRunner runner, NetworkObject prefab)
    {
        if (prefab.GetComponent<Player>() != null)
        {
            var key = (prefab, acquiringOwner);
            if (!avatars.TryGetValue(key, out var available)) avatars[key] = available = new Stack<NetworkObject>();
            while (available.Count > 0)
            {
                var reused = available.Pop();
                if (reused == null) continue;
                reused.transform.SetParent(null, false);
                reused.gameObject.SetActive(true);
                RespawnWorkTimer.Mark("CharacterPool.REUSE", reused);
                return reused;
            }
            using var timer = new RespawnWorkTimer("CharacterPool.CREATE", prefab);
            var createdAvatar = base.InstantiatePrefab(runner, prefab);
            origins[createdAvatar] = prefab;
            avatarOwners[createdAvatar] = acquiringOwner;
            RespawnWorkTimer.Mark("CharacterPool.CREATE", createdAvatar);
            return createdAvatar;
        }
        if (!eligible.TryGetValue(prefab, out bool pooled))
        {
            pooled = prefab.GetComponent<MagicProjectile>() != null;
            eligible.Add(prefab, pooled);
        }
        if (!pooled)
        {
            // Spawned() starts after Instantiate/Awake/OnEnable, so time that missing stage here.
            bool avatar = prefab.GetComponent<Player>() != null || prefab.GetComponent<PlayerRobby>() != null;
            if (avatar)
            {
                RespawnWorkTimer.Mark("NetworkAvatar.Instantiate.begin", prefab);
                using var timer = new RespawnWorkTimer("NetworkAvatar.Instantiate", prefab);
                var avatarInstance = base.InstantiatePrefab(runner, prefab);
                RespawnWorkTimer.Mark("NetworkAvatar.Instantiate.end", avatarInstance);
                return avatarInstance;
            }
            return base.InstantiatePrefab(runner, prefab);
        }
        if (!idle.TryGetValue(prefab, out var stack))
            idle.Add(prefab, stack = new Stack<NetworkObject>());
        while (stack.Count > 0)
        {
            var instance = stack.Pop();
            retained--;
            if (instance == null) continue;
            instance.transform.SetParent(null, false);
            instance.gameObject.SetActive(true);
            return instance;
        }
        var created = base.InstantiatePrefab(runner, prefab);
        origins.Add(created, prefab);
        return created;
    }

    protected override void DestroyPrefabInstance(NetworkRunner runner, NetworkPrefabId prefabId, NetworkObject instance)
    {
        if (avatarOwners.TryGetValue(instance, out var owner) && origins.TryGetValue(instance, out var avatarPrefab))
        {
            var available = avatars[(avatarPrefab, owner)];
            // Keep one spare per human; bots share their prefab bucket (up to a full match).
            int limit = owner == PlayerRef.None ? BattleMatchRules.MaxTeamPlayers * 2 : 1;
            if (available.Count >= limit)
            {
                avatarOwners.Remove(instance); origins.Remove(instance);
                base.DestroyPrefabInstance(runner, prefabId, instance);
                return;
            }
            EnsureStorage();
            instance.gameObject.SetActive(false);
            instance.transform.SetParent(storage, false);
            avatars[(avatarPrefab, owner)].Push(instance);
            RespawnWorkTimer.Mark("CharacterPool.RETURN", instance);
            return;
        }
        if (!origins.TryGetValue(instance, out var prefab) || prefab == null || retained >= Capacity)
        {
            origins.Remove(instance);
            base.DestroyPrefabInstance(runner, prefabId, instance);
            return;
        }
        EnsureStorage();
        instance.gameObject.SetActive(false);
        instance.transform.SetParent(storage, false);
        idle[prefab].Push(instance);
        retained++;
    }
    private void EnsureStorage()
    {
        if (storage != null) return;
        var root = new GameObject("Inactive network pool");
        root.transform.SetParent(transform, false);
        root.SetActive(false);
        storage = root.transform;
    }
}
