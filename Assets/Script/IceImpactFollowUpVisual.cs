using UnityEngine;
using UnityEngine.Rendering;

// One LOCAL batch, started only by the core's confirmed-hit RPC on each observer.
// Eight cosmetic shards: no colliders, physics queries, damage, hit markers or extra network objects.
public sealed class IceImpactFollowUpVisual : MonoBehaviour
{
    public const int ShardCount = 8;
    private sealed class Shard
    {
        public Transform view;
        public Vector3 origin, control, scale;
        public float launchedAt, duration;
        public bool arrived;
    }
    private readonly Shard[] shards = new Shard[ShardCount];
    private Player caster, victim;
    private MagicStatTable table;
    private Vector3 bodyPoint, targetPoint;
    private Quaternion bodyRotation;
    private float startedAt, interval, expiresAt;
    private int launched, arrived;
    private Material ownedMaterial;
    private Mesh ownedMesh;

    public static void Play(Player caster, Player victim, Vector3 hitPoint, MagicStatTable table)
    {
        if (caster == null || table == null || !table.iceImpactFollowUpEnabled) return;
        var root = new GameObject("Ice confirmed-hit follow-up (8 cosmetic shards)");
        // Not parented to the core: its network object despawns before these shards finish.
        var effect = root.AddComponent<IceImpactFollowUpVisual>();
        effect.caster = caster;
        effect.victim = victim;
        effect.table = table;
        effect.bodyPoint = caster.LockAimPoint; // Explicitly the body, not MagicCastRoot.
        effect.bodyRotation = caster.transform.rotation;
        effect.targetPoint = victim != null ? victim.LockAimPoint : hitPoint;
        effect.startedAt = Time.time;
        effect.interval = Mathf.Clamp(table.iceImpactLaunchInterval, .01f, .5f);
        effect.expiresAt = Time.time + (ShardCount-1)*effect.interval + 4.5f;
    }

    private void Update()
    {
        if (table == null || Time.time >= expiresAt || BattleManager.Instance == null || !BattleManager.Instance.IsGameplayActive)
        { Destroy(gameObject); return; }
        if (caster != null && caster.Object != null && caster.Object.IsValid && caster.IsAlive)
        {
            bodyPoint = caster.LockAimPoint;
            bodyRotation = caster.transform.rotation;
        }
        if (victim != null && victim.Object != null && victim.Object.IsValid && victim.IsAlive)
            targetPoint = victim.LockAimPoint;
        // References stay bound to the original actors. Despawn/respawn cannot retarget a new life.
        while (launched < ShardCount && ShouldLaunch(launched, Time.time-startedAt, interval))
            Launch(launched++);
        for (int i = 0; i < launched; i++)
        {
            Shard shard = shards[i];
            if (shard.arrived) continue;
            if (shard.view == null) { shard.arrived = true; arrived++; continue; }
            float t = Mathf.Clamp01((Time.time-shard.launchedAt)/shard.duration);
            Vector3 next = FlightPosition(shard.origin, shard.control, targetPoint, t);
            Vector3 delta = next-shard.view.position;
            if (delta.sqrMagnitude > .000001f) shard.view.rotation = Quaternion.LookRotation(delta);
            shard.view.position = next;
            shard.view.localScale = shard.scale * Mathf.Lerp(1f,.4f,t*t);
            if (t < 1f) continue;
            shard.arrived = true; arrived++;
            shard.view.gameObject.SetActive(false);
            CombatTransientEffect.PlayPulse(targetPoint, new Color(.5f,.9f,1f,.55f), .4f, .12f);
        }
        if (arrived == ShardCount) Destroy(gameObject);
    }

    private void Launch(int index)
    {
        Vector3 radial = bodyRotation * ClockwiseOffset(index);
        Vector3 origin = bodyPoint + radial * Mathf.Max(0f,table.iceImpactRingRadius);
        Vector3 heading = targetPoint-origin;
        Quaternion rotation = heading.sqrMagnitude > .0001f ? Quaternion.LookRotation(heading) : bodyRotation;
        GameObject view = CombatPresentation.InstantiateVfx(table.iceImpactShardVfxPrefab,origin,rotation,transform);
        if (view == null)
        {
            view = new GameObject("Ice crystal",typeof(MeshFilter),typeof(MeshRenderer));
            view.transform.SetParent(transform,false);
            view.transform.SetPositionAndRotation(origin,rotation);
            EnsureResources();
            view.GetComponent<MeshFilter>().sharedMesh = ownedMesh;
            var renderer = view.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = ownedMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            var trail = view.AddComponent<TrailRenderer>();
            trail.sharedMaterial = ownedMaterial;
            trail.time = .2f; trail.minVertexDistance = .12f;
            trail.startWidth = .12f; trail.endWidth = 0f;
            trail.numCapVertices = 2;
            trail.shadowCastingMode = ShadowCastingMode.Off;
        }
        // Applies to supplied VFX too, including inactive children and particle triggers.
        foreach (Collider collider in view.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
        foreach (Rigidbody body in view.GetComponentsInChildren<Rigidbody>(true))
        { body.detectCollisions = false; body.isKinematic = true; body.useGravity = false; }
        foreach (ParticleSystem particles in view.GetComponentsInChildren<ParticleSystem>(true))
        { var collision = particles.collision; collision.enabled = false; var trigger = particles.trigger; trigger.enabled = false; }
        view.name = "Clockwise ice shard " + (index+1);
        Vector3 scale = view.transform.localScale * Mathf.Max(.01f,table.iceImpactShardScale);
        view.transform.localScale = scale;
        shards[index] = new Shard {
            view = view.transform, origin = origin, scale = scale,
            control = origin + heading*.3f + radial*Mathf.Max(0f,table.iceImpactCurve),
            launchedAt = startedAt + index*interval,
            duration = Mathf.Clamp(heading.magnitude/Mathf.Max(1f,table.iceImpactShardSpeed),.18f,4f)
        };
    }

    // Looking forward from behind the caster: 12 o'clock -> upper right -> right -> ...
    public static Vector3 ClockwiseOffset(int index)
    {
        float angle = index*Mathf.PI*2f/ShardCount;
        return new Vector3(Mathf.Sin(angle),Mathf.Cos(angle),0f);
    }
    public static bool ShouldLaunch(int index, float age, float launchInterval)
        => index >= 0 && index < ShardCount && age >= index*Mathf.Clamp(launchInterval,.01f,.5f);
    public static Vector3 FlightPosition(Vector3 start, Vector3 control, Vector3 target, float progress)
    {
        float t = Mathf.Clamp01(progress), s = 1f-t;
        return s*s*start + 2f*s*t*control + t*t*target;
    }
    private void EnsureResources()
    {
        if (ownedMaterial == null) ownedMaterial = CombatPresentation.CreateEffectMaterial(new Color(.42f,.88f,1f,.95f));
        if (ownedMesh != null) return;
        ownedMesh = new Mesh { name = "Cosmetic ice crystal" };
        ownedMesh.vertices = new[] { new Vector3(0,0,.85f),new Vector3(0,0,-.45f),
            new Vector3(.22f,0,0),new Vector3(0,.22f,0),new Vector3(-.22f,0,0),new Vector3(0,-.22f,0) };
        ownedMesh.triangles = new[] { 0,2,3,0,3,4,0,4,5,0,5,2,1,3,2,1,4,3,1,5,4,1,2,5 };
        ownedMesh.RecalculateNormals(); ownedMesh.RecalculateBounds();
    }
    private void OnDestroy()
    {
        if (ownedMaterial != null) Destroy(ownedMaterial);
        if (ownedMesh != null) Destroy(ownedMesh);
    }
}
