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
    private Transform storage;
    private int retained;

    public override void ReleaseInstance(NetworkRunner runner, in NetworkObjectReleaseContext context)
    {
        if (context.IsBeingDestroyed) origins.Remove(context.Object);
        base.ReleaseInstance(runner, context);
    }

    protected override NetworkObject InstantiatePrefab(NetworkRunner runner, NetworkObject prefab)
    {
        if (!eligible.TryGetValue(prefab, out bool pooled))
        {
            pooled = prefab.GetComponent<MagicProjectile>() != null;
            eligible.Add(prefab, pooled);
        }
        if (!pooled) return base.InstantiatePrefab(runner, prefab);
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
        if (!origins.TryGetValue(instance, out var prefab) || prefab == null || retained >= Capacity)
        {
            origins.Remove(instance);
            base.DestroyPrefabInstance(runner, prefabId, instance);
            return;
        }
        if (storage == null)
        {
            var root = new GameObject("Inactive network magic");
            root.transform.SetParent(transform, false);
            root.SetActive(false);
            storage = root.transform;
        }
        instance.gameObject.SetActive(false);
        instance.transform.SetParent(storage, false);
        idle[prefab].Push(instance);
        retained++;
    }
}
