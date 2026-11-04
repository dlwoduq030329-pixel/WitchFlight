using UnityEngine;

// Cached once per instance, not searched every frame or every particle emission.
public sealed class MagicVfxLease : MonoBehaviour
{
    public GameObject Prefab { get; private set; }
    public bool InUse { get; set; }
    private Transform[] nodes;
    private Vector3[] positions, scales;
    private Quaternion[] rotations;
    private bool[] active;
    private int[] layers;
    private ParticleSystem[] particles;
    private bool[] loops, playOnAwake;
    private ParticleSystemSimulationSpace[] spaces;
    private Animator[] animators;
    private bool[] animatorEnabled;
    private float[] animatorSpeeds;
    private TrailRenderer[] trails;
    private bool[] trailEmitting;
    private Renderer[] renderers;
    private bool[] rendererEnabled;
    private Material[][] materials;
    private MaterialPropertyBlock[][] blocks;
    private MaterialPropertyBlock[] rendererBlocks;
    private AudioSource[] audioSources;

    public void Capture(GameObject prefab)
    {
        Prefab = prefab;
        nodes = GetComponentsInChildren<Transform>(true);
        positions = new Vector3[nodes.Length]; scales = new Vector3[nodes.Length];
        rotations = new Quaternion[nodes.Length]; active = new bool[nodes.Length]; layers = new int[nodes.Length];
        for (int i = 0; i < nodes.Length; i++)
        {
            positions[i] = nodes[i].localPosition; rotations[i] = nodes[i].localRotation;
            scales[i] = nodes[i].localScale; active[i] = nodes[i].gameObject.activeSelf; layers[i] = nodes[i].gameObject.layer;
        }
        foreach (Collider collider in GetComponentsInChildren<Collider>(true)) collider.enabled = false;
        foreach (Rigidbody body in GetComponentsInChildren<Rigidbody>(true))
        { body.detectCollisions = false; body.isKinematic = true; body.useGravity = false; }
        particles = GetComponentsInChildren<ParticleSystem>(true);
        loops = new bool[particles.Length]; playOnAwake = new bool[particles.Length];
        spaces = new ParticleSystemSimulationSpace[particles.Length];
        for (int i = 0; i < particles.Length; i++)
        {
            var main = particles[i].main;
            loops[i] = main.loop; playOnAwake[i] = main.playOnAwake; spaces[i] = main.simulationSpace;
            main.stopAction = ParticleSystemStopAction.None;
            var collision = particles[i].collision; collision.enabled = false;
            var trigger = particles[i].trigger; trigger.enabled = false;
        }
        animators = GetComponentsInChildren<Animator>(true);
        animatorEnabled = new bool[animators.Length]; animatorSpeeds = new float[animators.Length];
        for (int i = 0; i < animators.Length; i++) { animatorEnabled[i] = animators[i].enabled; animatorSpeeds[i] = animators[i].speed; }
        trails = GetComponentsInChildren<TrailRenderer>(true); trailEmitting = new bool[trails.Length];
        for (int i = 0; i < trails.Length; i++) { trailEmitting[i] = trails[i].emitting; trails[i].autodestruct = false; }
        audioSources = GetComponentsInChildren<AudioSource>(true);
        renderers = GetComponentsInChildren<Renderer>(true);
        rendererEnabled = new bool[renderers.Length]; materials = new Material[renderers.Length][];
        blocks = new MaterialPropertyBlock[renderers.Length][]; rendererBlocks = new MaterialPropertyBlock[renderers.Length];
        for (int i = 0; i < renderers.Length; i++)
        {
            var r = renderers[i]; rendererEnabled[i] = r.enabled; materials[i] = r.sharedMaterials;
            rendererBlocks[i] = new MaterialPropertyBlock(); r.GetPropertyBlock(rendererBlocks[i]);
            blocks[i] = new MaterialPropertyBlock[materials[i].Length];
            for (int j = 0; j < blocks[i].Length; j++) { blocks[i][j] = new MaterialPropertyBlock(); r.GetPropertyBlock(blocks[i][j], j); }
        }
    }

    public void Restore()
    {
        Stop();
        for (int i = 0; i < nodes.Length; i++) if (nodes[i] != null)
        {
            nodes[i].localPosition = positions[i]; nodes[i].localRotation = rotations[i]; nodes[i].localScale = scales[i];
            nodes[i].gameObject.layer = layers[i];
            if (nodes[i] != transform) nodes[i].gameObject.SetActive(active[i]);
        }
        for (int i = 0; i < renderers.Length; i++) if (renderers[i] != null)
        {
            var r = renderers[i]; r.sharedMaterials = materials[i]; r.enabled = rendererEnabled[i]; r.forceRenderingOff = false;
            r.SetPropertyBlock(rendererBlocks[i]);
            for (int j = 0; j < blocks[i].Length; j++) r.SetPropertyBlock(blocks[i][j], j);
        }
        for (int i = 0; i < particles.Length; i++) if (particles[i] != null)
        {
            var main = particles[i].main; main.loop = loops[i]; main.simulationSpace = spaces[i]; main.stopAction = ParticleSystemStopAction.None;
        }
        for (int i = 0; i < animators.Length; i++) if (animators[i] != null)
        { animators[i].enabled = animatorEnabled[i]; animators[i].speed = animatorSpeeds[i]; }
        for (int i = 0; i < trails.Length; i++) if (trails[i] != null) { trails[i].Clear(); trails[i].emitting = trailEmitting[i]; }
    }

    public void Restart()
    {
        foreach (var animator in animators) if (animator != null && animator.isActiveAndEnabled && animator.runtimeAnimatorController != null)
        { animator.Rebind(); animator.Update(0f); }
        for (int i = 0; i < particles.Length; i++)
            if (particles[i] != null && playOnAwake[i] && particles[i].gameObject.activeInHierarchy) particles[i].Play(false);
    }

    // Held visuals can stop their reveal animation after Restart has sampled frame zero.
    // Recover the prefab scale, not the animated (potentially invisible) current scale.
    public void RestoreAuthoredScale(Transform node)
    {
        if (node == null) return;
        for (int i = 0; i < nodes.Length; i++)
            if (nodes[i] == node) { node.localScale = scales[i]; return; }
    }

    public void Stop()
    {
        foreach (var particle in particles) if (particle != null) particle.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
        foreach (var trail in trails) if (trail != null) trail.Clear();
        foreach (var source in audioSources) if (source != null) source.Stop();
    }
    private void OnDestroy() => MagicVfxPool.Forget(gameObject);
}
