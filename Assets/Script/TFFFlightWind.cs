using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// Local visual response only: no RPCs, colliders, or changes to shared material assets.
public sealed class TFFFlightWind : MonoBehaviour
{
    [Min(1)] public float radius = 30f;
    [Min(1)] public float fullEffectSpeed = 35f;
    [Min(1)] public float frequencyMultiplier = 3f;
    [Min(1)] public float strengthMultiplier = 2.5f;
    [Min(1)] public float treeBoostMultiplier = 5f;
    [Min(.01f)] public float riseTime = .15f;
    [Min(.01f)] public float fallTime = .8f;
    private static readonly int Offset = Shader.PropertyToID("_FlightWindOffset");
    private static readonly int Strength = Shader.PropertyToID("_FlightWindStrength");
    private sealed class Target
    {
        public Renderer renderer;
        public Material[] original;
        public int[] slots;
        public float target, weight, phase;
        public TFFTreeBend tree;
        public Bounds originalBounds;
    }
    private readonly List<Target> targets = new();
    private readonly Dictionary<Material, Material> materials = new();
    // Native Unity objects must be created in a main-thread lifecycle callback,
    // not in a MonoBehaviour field initializer (including script reloads).
    [System.NonSerialized] private MaterialPropertyBlock block;
    private void Awake() => EnsurePropertyBlock();
    private void OnEnable() => EnsurePropertyBlock();
    private void EnsurePropertyBlock()
    {
        if (block == null) block = new MaterialPropertyBlock();
    }
    private float nextSample;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Install()
    {
        SceneManager.sceneLoaded -= Loaded;
        SceneManager.sceneLoaded += Loaded;
    }
    private static void Loaded(Scene scene, LoadSceneMode mode)
    {
        if (!Application.isPlaying) return;
        foreach (var root in scene.GetRootGameObjects())
            if (root.GetComponentInChildren<TFFFlightWind>(true) != null) return;
        var host = new GameObject("TFF Flight Wind");
        SceneManager.MoveGameObjectToScene(host, scene);
        host.AddComponent<TFFFlightWind>();
    }
    private void Start()
    {
        var preparedTrees = new HashSet<TFFTreeBend>();
        foreach (var root in gameObject.scene.GetRootGameObjects())
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            var tree = renderer.GetComponentInParent<TFFTreeBend>();
            // Also cover unpacked map trees and vendor prefabs excluded from source control.
            if (tree == null)
            {
                Transform treeRoot = null;
                for (var parent = renderer.transform; parent != null; parent = parent.parent)
                    if (TFFTreeBend.IsStandingTree(parent.name)) treeRoot = parent;
                if (treeRoot != null) tree = treeRoot.gameObject.AddComponent<TFFTreeBend>();
            }
            if (tree != null && preparedTrees.Add(tree)) tree.Prepare();
            var original = renderer.sharedMaterials;
            var replacement = (Material[])original.Clone();
            var slots = new List<int>();
            for (int i = 0; i < original.Length; i++)
            {
                var source = original[i];
                if (source == null || source.shader == null) continue;
                string shaderName = source.shader.name;
                if (shaderName != "Toon/TFF_CustomGrass" && shaderName != "Toon/TFF_CustomToonVegetation" && !(tree != null && shaderName == "Toon/Toon")) continue;
                if (!materials.TryGetValue(source, out var material))
                {
                    var shader = Resources.Load<Shader>("TFFReactiveWind/" + (shaderName == "Toon/Toon" ? "TFFTreeToon" : shaderName.Substring(5)));
                    if (shader == null || !shader.isSupported) continue;
                    material = new Material(source) { shader = shader, name = source.name + " (Flight Wind)" };
                    material.SetFloat(Offset, 0); material.SetFloat(Strength, 1);
                    materials.Add(source, material);
                }
                replacement[i] = material;
                slots.Add(i);
            }
            if (slots.Count == 0) continue;
            renderer.sharedMaterials = replacement;
            var target = new Target { renderer = renderer, original = original, slots = slots.ToArray(), tree = tree, originalBounds = renderer.localBounds };
            targets.Add(target);
            if (tree != null)
            {
                var bounds = renderer.localBounds;
                float scale = Mathf.Max(.001f, Mathf.Min(Mathf.Abs(renderer.transform.lossyScale.x), Mathf.Abs(renderer.transform.lossyScale.y), Mathf.Abs(renderer.transform.lossyScale.z)));
                bounds.Expand(tree.Height * tree.bendAmount * Mathf.Max(1,treeBoostMultiplier) * 2 / scale);
                renderer.localBounds = bounds;
                foreach (int slot in target.slots) ApplyTree(target, slot);
            }
        }
    }
    private void LateUpdate()
    {
        EnsurePropertyBlock();
        bool sample = Time.unscaledTime >= nextSample;
        if (sample) nextSample = Time.unscaledTime + .1f;
        foreach (var item in targets)
        {
            if (item.renderer == null) continue;
            if (sample)
            {
                item.target = 0;
                foreach (var player in Player.ActiveCombatants)
                {
                    if (player == null || !player.isActiveAndEnabled || !player.IsAlive || player.gameObject.scene != gameObject.scene) continue;
                    float distance = Vector3.Distance(player.transform.position, (item.tree != null ? item.tree.WindBounds : item.renderer.bounds).ClosestPoint(player.transform.position));
                    float near = Mathf.Clamp01(1 - distance / Mathf.Max(1, radius));
                    float speed = player.IsBoosting ? 1f : Mathf.Min(.65f, player.FlightVisualVelocity.magnitude / Mathf.Max(1, fullEffectSpeed));
                    item.target = Mathf.Max(item.target, near * speed);
                }
            }
            float old = item.weight;
            item.weight = Mathf.MoveTowards(item.weight, item.target, Time.deltaTime / Mathf.Max(.01f, item.target > item.weight ? riseTime : fallTime));
            if (item.weight == 0 && old == 0) continue;
            // Integrate extra phase rather than multiplying absolute time: no snapping on approach/exit.
            item.phase += Time.deltaTime * item.weight * (Mathf.Max(1, frequencyMultiplier) - 1);
            foreach (int slot in item.slots)
            {
                item.renderer.GetPropertyBlock(block, slot);
                block.SetFloat(Offset, item.phase);
                block.SetFloat(Strength, Mathf.Lerp(1, strengthMultiplier, item.weight));
                item.renderer.SetPropertyBlock(block, slot);
                block.Clear();
                if (item.tree != null) ApplyTree(item, slot);
            }
        }
    }
    private void ApplyTree(Target item, int slot)
    {
        var tree = item.tree;
        item.renderer.GetPropertyBlock(block, slot);
        block.SetVector("_TreeRoot", tree.transform.position);
        Vector3 up = tree.transform.up;
        block.SetVector("_TreeUpHeight", new Vector4(up.x,up.y,up.z,tree.Height));
        block.SetVector("_TreeBendSettings", new Vector4(tree.bendAmount,tree.swayFrequency,tree.Phase,tree.fixedRootFraction));
        block.SetVector("_TreeWind", new Vector4(Mathf.Lerp(1,treeBoostMultiplier,item.weight),item.phase,0,0));
        item.renderer.SetPropertyBlock(block,slot); block.Clear();
    }
    private void OnDestroy()
    {
        if (targets.Count > 0) EnsurePropertyBlock();
        foreach (var item in targets)
        {
            if (item.renderer == null) continue;
            item.renderer.sharedMaterials = item.original;
            if (item.tree != null) item.renderer.localBounds = item.originalBounds;
            foreach (int slot in item.slots)
            {
                item.renderer.GetPropertyBlock(block, slot);
                block.SetFloat(Offset, 0); block.SetFloat(Strength, 1);
                item.renderer.SetPropertyBlock(block, slot); block.Clear();
            }
        }
        foreach (var material in materials.Values) if (material != null) Destroy(material);
    }
}
