using UnityEngine;

// Used only by Vision/Dark cast flashes. Keep authored size/rotation and particle
// motion while translating with the rendered wand; shared prefab assets stay intact.
[DefaultExecutionOrder(15200)]
public sealed class InstantCastMuzzleFollow : MonoBehaviour
{
    private Player owner;
    private Transform anchor;
    private ParticleSystem[] particles;
    private ParticleSystemSimulationSpace[] spaces;
    private Transform[] customSpaces;
    private MagicVfxLease lease;
    private bool bound;

    public static void Bind(GameObject view, Player player)
    {
        if (view == null || player == null) return;
        if (!view.TryGetComponent(out InstantCastMuzzleFollow follower))
            follower = view.AddComponent<InstantCastMuzzleFollow>();
        follower.Initialize(player);
    }

    private void Initialize(Player player)
    {
        if (particles == null)
        {
            // Cache once per pooled instance, including inactive sub-emitters.
            particles = GetComponentsInChildren<ParticleSystem>(true);
            spaces = new ParticleSystemSimulationSpace[particles.Length];
            customSpaces = new Transform[particles.Length];
            lease = GetComponent<MagicVfxLease>();
        }
        owner = player;
        anchor = player.MagicCastAnchor;
        transform.position = player.MagicCastPosition;
        for (int i = 0; i < particles.Length; i++)
        {
            if (particles[i] == null) continue;
            var main = particles[i].main;
            spaces[i] = main.simulationSpace;
            customSpaces[i] = main.customSimulationSpace;
            if (spaces[i] != ParticleSystemSimulationSpace.World) continue;
            // Rent has already started play-on-awake emitters. Restart those in the
            // correct space before the first rendered frame, avoiding a world-space remnant.
            particles[i].Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            main.simulationSpace = ParticleSystemSimulationSpace.Custom;
            main.customSimulationSpace = transform;
        }
        bound = true;
        lease?.Restart();
    }

    private void LateUpdate()
    {
        if (!bound) return;
        if (anchor == null || owner == null || owner.Object == null || !owner.Object.IsValid || !owner.IsAlive)
        { MagicVfxPool.Release(gameObject); return; }
        transform.position = owner.MagicCastPosition;
    }

    private void OnDisable()
    {
        if (!bound) return;
        for (int i = 0; i < particles.Length; i++)
        {
            if (particles[i] == null) continue;
            var main = particles[i].main;
            main.simulationSpace = spaces[i];
            main.customSimulationSpace = customSpaces[i];
        }
        bound = false;
        owner = null;
        anchor = null;
    }
}
