using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// Scene-local visual leases. NetworkObjects must continue through Fusion's provider.
public sealed class MagicVfxPool : MonoBehaviour
{
    private const int RetainedPerPrefab = 32, RetainedTotal = 256;
    private static MagicVfxPool instance;
    private readonly Dictionary<GameObject, Stack<MagicVfxLease>> idle = new();
    private readonly Dictionary<GameObject, MagicVfxLease> leases = new();
    private readonly Dictionary<GameObject, bool> validPrefabs = new();
    private readonly List<Deadline> deadlines = new();
    private readonly List<MagicVfxLease> releaseBuffer = new();
    private int retained;
    private Transform storage;
    private struct Deadline { public GameObject root; public float at; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => instance = null;

    private static MagicVfxPool Ensure()
    {
        if (instance != null) return instance;
        instance = new GameObject("Magic VFX pool (local)").AddComponent<MagicVfxPool>();
        var storage = new GameObject("Inactive visuals");
        storage.transform.SetParent(instance.transform, false);
        storage.SetActive(false);
        instance.storage = storage.transform;
        return instance;
    }

    public static GameObject Rent(GameObject prefab, Vector3 position, Quaternion rotation, Transform parent = null)
    {
        if (prefab == null) return null;
        var pool = Ensure();
        if (!pool.validPrefabs.TryGetValue(prefab, out bool valid))
        {
            valid = prefab.GetComponentInChildren<Fusion.NetworkObject>(true) == null;
            pool.validPrefabs.Add(prefab, valid);
            if (!valid) Debug.LogWarning("Magic VFX must not contain a NetworkObject: " + prefab.name);
        }
        if (!valid) return null;
        if (!pool.idle.TryGetValue(prefab, out var stack))
            pool.idle.Add(prefab, stack = new Stack<MagicVfxLease>());
        MagicVfxLease lease = null;
        while (stack.Count > 0 && lease == null) { lease = stack.Pop(); pool.retained--; }
        if (lease == null)
        {
            // Inactive parent prevents play-on-awake before the emitter has its final pose.
            GameObject root = Instantiate(prefab, pool.storage, false);
            root.SetActive(false);
            lease = root.AddComponent<MagicVfxLease>();
            lease.Capture(prefab);
            pool.leases.Add(root, lease);
        }
        lease.Restore();
        lease.transform.SetParent(parent, false);
        if (parent == null) SceneManager.MoveGameObjectToScene(lease.gameObject, pool.gameObject.scene);
        lease.transform.SetPositionAndRotation(position, rotation);
        lease.InUse = true;
        lease.gameObject.SetActive(true);
        lease.Restart();
        return lease.gameObject;
    }

    // Replaces Destroy for an owned visual hierarchy. Return nested prefab leases
    // before destroying a lightweight controller, including inactive children.
    public static void Release(GameObject root, float delay = 0f)
    {
        if (root == null) return;
        if (delay > 0f)
        {
            var pool = Ensure();
            for (int i = pool.deadlines.Count - 1; i >= 0; i--)
                if (pool.deadlines[i].root == root) pool.deadlines.RemoveAt(i);
            pool.deadlines.Add(new Deadline { root = root, at = Time.time + delay });
            return;
        }
        if (instance == null) { Destroy(root); return; }
        var owner = instance;
        for (int i = owner.deadlines.Count - 1; i >= 0; i--)
            if (owner.deadlines[i].root == null || owner.deadlines[i].root == root ||
                owner.deadlines[i].root.transform.IsChildOf(root.transform)) owner.deadlines.RemoveAt(i);
        if (owner.leases.TryGetValue(root, out var direct) && direct != null)
        { owner.Return(direct); return; }
        owner.releaseBuffer.Clear();
        root.GetComponentsInChildren(true, owner.releaseBuffer);
        // Return does not recurse through Release; no per-return array allocation.
        for (int i = owner.releaseBuffer.Count - 1; i >= 0; i--) owner.Return(owner.releaseBuffer[i]);
        owner.releaseBuffer.Clear();
        Destroy(root);
    }

    private void Return(MagicVfxLease lease)
    {
        if (lease == null || !lease.InUse) return;
        lease.InUse = false;
        RearThreatCamera.ExcludeLocalEffect(lease.gameObject, null);
        lease.Stop();
        lease.gameObject.SetActive(false);
        lease.transform.SetParent(storage, false);
        var stack = idle[lease.Prefab];
        if (retained >= RetainedTotal || stack.Count >= RetainedPerPrefab)
        { leases.Remove(lease.gameObject); Destroy(lease.gameObject); return; }
        stack.Push(lease);
        retained++;
    }

    private void Update()
    {
        for (int i = deadlines.Count - 1; i >= 0; i--)
        {
            if (i >= deadlines.Count) continue;
            Deadline item = deadlines[i];
            if (item.root != null && Time.time < item.at) continue;
            deadlines.RemoveAt(i);
            if (item.root != null) Release(item.root);
        }
    }

    internal static void Forget(GameObject root) { if (instance != null) instance.leases.Remove(root); }
    private void OnDestroy() { if (instance == this) instance = null; }
}
