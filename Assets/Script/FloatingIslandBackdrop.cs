using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Pure scenery: deterministic geometry, no colliders, gameplay scripts or network objects.
[ExecuteAlways, DisallowMultipleComponent]
public sealed class FloatingIslandBackdrop : MonoBehaviour
{
    [Serializable]
    public sealed class Belt
    {
        public string name;
        [Range(0, 24)] public int count;
        public Vector2 radius;
        public Vector2 topHeight;
        public Vector2 width;
        public Vector2 height;
        [Range(0f, 1f)] public float haze;
        public Belt(string name, int count, Vector2 radius, Vector2 topHeight, Vector2 width, Vector2 height, float haze)
        {
            this.name = name; this.count = count; this.radius = radius; this.topHeight = topHeight;
            this.width = width; this.height = height; this.haze = haze;
        }
    }

    [Header("Scenery outside the battle boundary")]
    [SerializeField] private int seed = 7319;
    [Tooltip("맵 중심 기준으로 배치합니다. 카메라를 따라가지 않으므로 거리별 시차가 유지됩니다.")]
    [SerializeField] private Vector3 center;
    [Tooltip("섬의 가장 안쪽 끝이 이 반경 안으로 들어오지 않습니다. 현재 ±250m 전투 구역의 모서리보다 여유 있게 설정합니다.")]
    [SerializeField, Min(380f)] private float clearRadius = 410f;
    [SerializeField] private Material islandMaterial;
    [SerializeField] private Material cloudMaterial;
    [SerializeField, Range(3, 8)] private int clusterCount = 5;
    [SerializeField] private Belt[] belts =
    {
        new("Near crags", 8, new(500, 590), new(100, 210), new(45, 85), new(95, 190), .06f),
        new("Middle mountains", 12, new(650, 740), new(130, 310), new(55, 110), new(130, 260), .22f),
        new("Distant silhouettes", 14, new(810, 890), new(170, 350), new(65, 125), new(160, 290), .45f)
    };
    [Header("Clouds between the mountain groups")]
    [SerializeField, Range(0f, 1f)] private float cloudOpacity = .48f;
    [SerializeField, Range(0f, 2f)] private float cloudSizeMultiplier = 1f;
    [SerializeField] private Color rockColor = new(.18f, .26f, .24f);
    [SerializeField] private Color summitColor = new(.1f, .28f, .12f);
    [SerializeField] private Color distantColor = new(.4f, .57f, .66f);

    [NonSerialized] private GameObject generated;
    private readonly List<Mesh> meshes = new();
    private bool rebuild;
    private static readonly float[] Heights = { -1f, -.86f, -.68f, -.46f, -.24f, -.09f, 0f };
    private static readonly float[][] Profiles =
    {
        new[] { .4f, .72f, .83f, .78f, .93f, 1f, .85f },
        new[] { .32f, .57f, .92f, 1f, .87f, .97f, .8f },
        new[] { .28f, .66f, .93f, 1f, .97f, .9f, .74f },
        new[] { .38f, .68f, .76f, .93f, .72f, 1f, .84f }
    };

    private void OnEnable() => rebuild = true;
    private void OnValidate() => rebuild = true;
    private void Update()
    {
        if (!rebuild) return;
        rebuild = false;
        Rebuild();
    }

    [ContextMenu("Rebuild Floating Islands")]
    public void Rebuild()
    {
        rebuild = false;
        Clear();
        if (islandMaterial == null || belts == null) return;
        generated = new GameObject("Floating mountains (generated)") { hideFlags = HideFlags.DontSave, layer = 2 };
        generated.transform.SetParent(transform, false);
        var random = new System.Random(seed);
        var clouds = new List<ParticleSystem.Particle>();
        int clusters = Mathf.Clamp(clusterCount, 3, 8);
        var bearings = new float[clusters];
        for (int i = 0; i < clusters; i++)
            bearings[i] = i * 360f / clusters + Range(random, -15f, 15f) + 11f;

        for (int tier = 0; tier < belts.Length; tier++)
        {
            Belt belt = belts[tier];
            if (belt == null) continue;
            var vertices = new List<Vector3>();
            var colors = new List<Color>();
            var triangles = new List<int>();
            for (int i = 0; i < Mathf.Clamp(belt.count, 0, 24); i++)
            {
                // Irregular groups and changing angular offsets avoid concentric fences.
                float angle = (bearings[i % clusters] + Range(random, -23f, 23f) + tier * 9f) * Mathf.Deg2Rad;
                float width = Mathf.Max(12f, Range(random, belt.width.x, belt.width.y));
                float height = Mathf.Max(20f, Range(random, belt.height.x, belt.height.y));
                float radius = Mathf.Max(Mathf.Max(380f, clearRadius) + width, Range(random, belt.radius.x, belt.radius.y));
                float top = Range(random, belt.topHeight.x, belt.topHeight.y);
                // Keep floating tips above the sea instead of creating submerged pillars.
                top = Mathf.Max(top, height - 8f);
                Vector3 position = center + new Vector3(Mathf.Cos(angle) * radius, top, Mathf.Sin(angle) * radius);
                Color cliff = Color.Lerp(rockColor * Range(random, .88f, 1.12f), distantColor, belt.haze);
                Color summit = Color.Lerp(summitColor * Range(random, .88f, 1.13f), distantColor, belt.haze);
                AddRock(vertices, colors, triangles, random, position, width, height, cliff, summit);
                // Small orbiting fragments break the repetitive single-rock silhouette.
                if ((i + tier) % 3 == 0)
                {
                    Vector3 offset = new Vector3(-Mathf.Sin(angle), 0, Mathf.Cos(angle)) * width * .85f;
                    offset.y = -height * .36f;
                    AddRock(vertices, colors, triangles, random, position + offset, width * .26f,
                        height * .29f, cliff, summit);
                }
                AddCloud(clouds, random, position + new Vector3(0, -height * .67f, 0), width * 2.1f,
                    cloudOpacity * (tier == 0 ? .65f : 1f));
                if (i % 2 == 0)
                {
                    // Offset mist sits in front of some spires and behind others.
                    Vector3 gap = position + new Vector3(Mathf.Cos(angle) * -65f, -height * .32f, Mathf.Sin(angle) * -65f);
                    AddCloud(clouds, random, gap, width * 2.5f, cloudOpacity * .65f);
                }
            }
            if (vertices.Count == 0) continue;
            var mesh = new Mesh { name = "Backdrop " + belt.name, hideFlags = HideFlags.DontSave };
            mesh.SetVertices(vertices); mesh.SetColors(colors); mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            meshes.Add(mesh);
            var group = new GameObject(belt.name) { hideFlags = HideFlags.DontSave, layer = 2 };
            group.transform.SetParent(generated.transform, false);
            group.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = group.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = islandMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }
        CreateClouds(clouds);
    }

    private static void AddRock(List<Vector3> vertices, List<Color> colors, List<int> triangles,
        System.Random random, Vector3 position, float width, float height, Color cliff, Color summit)
    {
        const int sides = 18;
        int first = vertices.Count;
        float rotation = Range(random, 0, Mathf.PI * 2);
        float phase = Range(random, 0, Mathf.PI * 2);
        float aspect = Range(random, .6f, .95f);
        float lean = Range(random, -.16f, .16f) * width;
        float[] profile = Profiles[random.Next(Profiles.Length)];
        var angular = new float[sides];
        for (int side = 0; side < sides; side++) angular[side] = Range(random, .77f, 1.15f);
        for (int ring = 0; ring < Heights.Length; ring++)
        {
            float h = Heights[ring];
            float ringWidth = profile[ring] * Range(random, .85f, 1.14f);
            for (int side = 0; side < sides; side++)
            {
                float a = side * Mathf.PI * 2 / sides;
                float r = width * .5f * ringWidth * angular[side] * Range(random, .88f, 1.12f);
                float x = Mathf.Cos(a) * r, z = Mathf.Sin(a) * r * aspect;
                float y = h * height + Mathf.Sin(a * 3 + phase) * height * (ring == 0 ? .07f : .028f);
                vertices.Add(position + new Vector3(Mathf.Cos(rotation) * x - Mathf.Sin(rotation) * z + lean * Mathf.Sin(h * 2),
                    y, Mathf.Sin(rotation) * x + Mathf.Cos(rotation) * z + lean * .5f * h));
                Color color = ring == Heights.Length - 1 ? summit : cliff * Range(random, .88f, 1.08f);
                color.a = 1; colors.Add(color);
                if (ring == 0) continue;
                int current = first + ring * sides + side;
                int next = first + ring * sides + (side + 1) % sides;
                triangles.Add(current - sides); triangles.Add(current); triangles.Add(next);
                triangles.Add(current - sides); triangles.Add(next); triangles.Add(next - sides);
            }
        }
        int cap = vertices.Count;
        vertices.Add(position + Vector3.up * height * .07f); colors.Add(summit);
        for (int side = 0; side < sides; side++)
        {
            int top = first + (Heights.Length - 1) * sides;
            triangles.Add(cap); triangles.Add(top + (side + 1) % sides); triangles.Add(top + side);
            triangles.Add(first); triangles.Add(first + side); triangles.Add(first + (side + 1) % sides);
        }
    }

    private void AddCloud(List<ParticleSystem.Particle> clouds, System.Random random, Vector3 position, float width, float alpha)
    {
        width *= Mathf.Max(0f, cloudSizeMultiplier);
        if (width <= 0 || alpha <= 0) return;
        clouds.Add(new ParticleSystem.Particle
        {
            position = position + new Vector3(Range(random, -25, 25), Range(random, -12, 12), Range(random, -25, 25)),
            startSize3D = new Vector3(width, width * Range(random, .26f, .42f), width),
            startColor = new Color(.9f, .95f, 1f, Mathf.Clamp01(alpha)),
            rotation = Range(random, -7, 7), startLifetime = 10000, remainingLifetime = 10000,
            randomSeed = (uint)(clouds.Count + 1)
        });
    }

    private void CreateClouds(List<ParticleSystem.Particle> clouds)
    {
        if (cloudMaterial == null || clouds.Count == 0) return;
        var obj = new GameObject("Depth clouds") { hideFlags = HideFlags.DontSave, layer = 2 };
        obj.transform.SetParent(generated.transform, false);
        var particles = obj.AddComponent<ParticleSystem>();
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = particles.main;
        main.playOnAwake = false; main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.startSize3D = true; main.maxParticles = clouds.Count; main.startSpeed = 0;
        main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
        var emission = particles.emission; emission.enabled = false;
        var shape = particles.shape; shape.enabled = false;
        var renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = cloudMaterial; renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.sortMode = ParticleSystemSortMode.Distance; renderer.maxParticleSize = 1;
        renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
        renderer.lightProbeUsage = LightProbeUsage.Off; renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        particles.Play(); particles.Pause(); particles.SetParticles(clouds.ToArray(), clouds.Count);
        renderer.localBounds = new Bounds(center + Vector3.up * 150, new Vector3(2600, 1100, 2600));
    }

    private static float Range(System.Random random, float min, float max) => Mathf.Lerp(min, max, (float)random.NextDouble());
    private void OnDisable() => Clear();
    private void Clear()
    {
        if (generated != null) { generated.SetActive(false); Release(generated); generated = null; }
        foreach (Mesh mesh in meshes) if (mesh != null) Release(mesh);
        meshes.Clear();
    }
    private static void Release(UnityEngine.Object value)
    {
        if (Application.isPlaying) Destroy(value); else DestroyImmediate(value);
    }
}
