using UnityEngine;

// Local presentation of replicated book count; no extra network objects or RPCs.
public sealed class VisionBookOrbit : MonoBehaviour
{
    private Player player;
    private Transform[] books, hinges;
    private GameObject[] closedViews, openViews;
    private GameObject[] animatedViews;
    private float[] sampledOpenness;
    private Material cover, pages;
    private bool[] wasOpen;
    private float[] openingTime, startOpenness, openness;
    private Quaternion[] openingStart;
    private Light[] pageLights;
    private Material[][] glowingPages;
    private VisionPageEnergy[] energy;
    private readonly System.Collections.Generic.List<Material> glowMaterials = new();
    private void Awake() { player = GetComponent<Player>(); }
    private void LateUpdate()
    {
        bool show = player != null && player.Object != null && player.Object.IsValid && player.IsAlive && player.HasVisionEquipped;
        if (!show) { SetVisible(false); return; }
        var s = VisionBookSettings.Default;
        int count = Mathf.Clamp(s.bookCount, 1, 12);
        if (books == null || books.Length != count) Build(s, count);
        SetVisible(true);
        for (int i = 0; i < count; i++)
        {
            // Fixed left/right shoulder positions. Extra books form vertical pairs.
            float side = i % 2 == 0 ? -1f : 1f;
            float bob = Mathf.Sin(Time.time * s.bobSpeed + i * Mathf.PI) * s.bobHeight;
            books[i].localPosition = new Vector3(side * s.shoulderDistance,
                s.shoulderHeight + (i / 2) * s.bookScale * 1.5f + bob, s.shoulderForwardOffset);
            books[i].localScale = Vector3.one * s.bookScale;
            bool open = i < player.VisionOpenBooks;
            if (open != wasOpen[i])
            {
                openingTime[i] = 0f;
                openingStart[i] = books[i].rotation;
                startOpenness[i] = openness[i];
            }
            wasOpen[i] = open;
            openingTime[i] += Time.deltaTime;
            float progress = Mathf.Clamp01(openingTime[i] / Mathf.Max(.1f, s.openingSeconds));
            // Quintic easing gives a gentle start/stop and a faster middle.
            float eased = progress * progress * progress * (progress * (progress * 6f - 15f) + 10f);
            float unfolding = Mathf.Lerp(startOpenness[i], open ? 1f : 0f, eased);
            openness[i] = unfolding;
            pageLights[i].intensity = s.pageLightIntensity * unfolding;
            pageLights[i].range = s.pageLightRange;
            pageLights[i].color = s.pageGlowColor;
            pageLights[i].enabled = unfolding > .001f;
            foreach (var mat in glowingPages[i])
            {
                Color emission = s.pageGlowColor * (s.pageGlowIntensity * unfolding);
                if (mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", emission);
            }
            if (animatedViews[i] != null)
            {
                if (s.bookClosingClip != null && !Mathf.Approximately(sampledOpenness[i], unfolding))
                {
                    s.bookClosingClip.SampleAnimation(animatedViews[i], (1f - unfolding) * s.bookClosingClip.length);
                    sampledOpenness[i] = unfolding;
                }
            }
            else if (hinges[i] != null) hinges[i].localRotation = Quaternion.Slerp(hinges[i].localRotation,
                Quaternion.Euler(0, 0, 155f * unfolding), 1f);
            else
            {
                bool unfolded = unfolding > .5f;
                if (closedViews[i].activeSelf == unfolded) closedViews[i].SetActive(!unfolded);
                if (openViews[i].activeSelf != unfolded) openViews[i].SetActive(unfolded);
            }
            // Built-in spine runs along Z; the free edge is +X and the top is -Z.
            // Aim the opening (not the page normal) forwards. Counter half the
            // cover angle so both halves open symmetrically around that direction.
            Quaternion openingForward;
            if (hinges[i] != null)
            {
                float opening = Mathf.DeltaAngle(0f, hinges[i].localEulerAngles.z);
                openingForward = Quaternion.Euler(0, -90f, 0) * Quaternion.Euler(90f, 0, 0) *
                    Quaternion.AngleAxis(-opening * .5f, Vector3.forward);
            }
            else openingForward = Quaternion.Euler(s.prefabRotationCorrection);
            Quaternion closedRotation = transform.rotation * Quaternion.Euler(-s.backwardTiltDegrees, 0, 0) * openingForward;
            if (open)
            {
                Vector3 towardsReader = transform.TransformPoint(Vector3.up * s.shoulderHeight) - books[i].position;
                if (towardsReader.sqrMagnitude < .0001f) towardsReader = -transform.forward;
                // The exposed page normals, rather than the free edge, face the reader.
                Quaternion pagesForward = hinges[i] != null ? Quaternion.Euler(90f, 0, 0) *
                    Quaternion.AngleAxis((180f - 155f * unfolding) * .5f, Vector3.forward) :
                    Quaternion.Euler(s.openPrefabRotationCorrection);
                // Restore the book's reader-facing horizontal axis. A positive
                // backwardTiltDegrees setting applies a negative local X rotation.
                Quaternion reading = Quaternion.LookRotation(towardsReader, Vector3.up) *
                    Quaternion.Euler(-s.backwardTiltDegrees, 0, 0) * pagesForward;
                Quaternion pose = Quaternion.Slerp(openingStart[i], reading, eased);
                // Pre-multiply: spin around WORLD Y, independent of pilot banking.
                books[i].rotation = Quaternion.AngleAxis(360f * eased, Vector3.up) * pose;
            }
            else books[i].rotation = progress < 1f
                ? Quaternion.AngleAxis(-360f * eased, Vector3.up) * Quaternion.Slerp(openingStart[i], closedRotation, eased)
                : closedRotation;
            energy[i].Draw(s, unfolding);
            if (energy[i].HasSource) pageLights[i].transform.position = energy[i].SourcePosition + Vector3.up * .06f;
        }
    }
    private void SetVisible(bool value)
    {
        if (books == null) return;
        foreach (var b in books) if (b != null && b.gameObject.activeSelf != value) b.gameObject.SetActive(value);
    }
    private void Build(VisionBookSettings s, int count)
    {
        if (books != null) foreach (var b in books) if (b != null) Destroy(b.gameObject);
        foreach (var mat in glowMaterials) if (mat != null) Destroy(mat);
        glowMaterials.Clear();
        books = new Transform[count]; hinges = new Transform[count];
        wasOpen = new bool[count]; openingTime = new float[count]; openingStart = new Quaternion[count];
        startOpenness = new float[count]; openness = new float[count];
        for (int i = 0; i < count; i++) { openingTime[i] = float.MaxValue; openingStart[i] = Quaternion.identity; }
        closedViews = new GameObject[count]; openViews = new GameObject[count];
        animatedViews = new GameObject[count]; sampledOpenness = new float[count];
        pageLights = new Light[count]; glowingPages = new Material[count][];
        energy = new VisionPageEnergy[count];
        if (cover == null)
        {
            cover = new Material(Shader.Find("Universal Render Pipeline/Lit")); cover.color = new Color(.18f, .06f, .36f);
            pages = new Material(cover); pages.color = new Color(.95f, .86f, .63f);
        }
        for (int i = 0; i < count; i++)
        {
            var root = new GameObject("Vision Book " + (i + 1)).transform;
            root.SetParent(transform, false); books[i] = root;
            if (s.animatedBookPrefab != null)
            {
                var model = Instantiate(s.animatedBookPrefab, root);
                animatedViews[i] = model;
                sampledOpenness[i] = -1f;
                foreach (var animator in model.GetComponentsInChildren<Animator>(true)) animator.enabled = false;
                foreach (var collider in model.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
                AddPageGlow(i, root);
                continue;
            }
            if (s.closedBookPrefab != null && s.openBookPrefab != null)
            {
                closedViews[i] = Instantiate(s.closedBookPrefab, root);
                openViews[i] = Instantiate(s.openBookPrefab, root);
                foreach (var c in root.GetComponentsInChildren<Collider>(true)) c.enabled = false;
                AddPageGlow(i, root);
                continue;
            }
            Part(root, new Vector3(.45f, -.1f, 0), new Vector3(.95f, .09f, 1.25f), cover);
            Part(root, new Vector3(.45f, 0, 0), new Vector3(.85f, .16f, 1.15f), pages);
            Part(root, new Vector3(0, 0, 0), new Vector3(.1f, .3f, 1.25f), cover);
            var hinge = new GameObject("Cover hinge").transform; hinge.SetParent(root, false);
            hinge.localPosition = new Vector3(0, .12f, 0); hinges[i] = hinge;
            Part(hinge, new Vector3(.45f, 0, 0), new Vector3(.95f, .08f, 1.25f), cover);
            Part(hinge, new Vector3(.45f, -.05f, 0), new Vector3(.85f, .02f, 1.15f), pages);
            AddPageGlow(i, root);
        }
    }
    private void AddPageGlow(int index, Transform root)
    {
        var lightRoot = new GameObject("Open pages glow");
        lightRoot.transform.SetParent(root, false);
        var lamp = lightRoot.AddComponent<Light>();
        lamp.type = LightType.Point; lamp.shadows = LightShadows.None;
        lamp.range = 4f; lamp.intensity = 0f; lamp.enabled = false;
        lamp.renderMode = LightRenderMode.ForcePixel;
        pageLights[index] = lamp;
        var owned = new System.Collections.Generic.List<Material>();
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            var materials = renderer.sharedMaterials;
            bool changed = false;
            for (int slot = 0; slot < materials.Length; slot++)
            {
                var original = materials[slot];
                if (original == null || (original != pages &&
                    original.name.IndexOf("page", System.StringComparison.OrdinalIgnoreCase) < 0)) continue;
                var copy = new Material(original);
                copy.EnableKeyword("_EMISSION");
                if (copy.HasProperty("_UseEmission")) copy.SetFloat("_UseEmission", 1f);
                if (copy.HasProperty("_EmissionColor")) copy.SetColor("_EmissionColor", Color.black);
                materials[slot] = copy; owned.Add(copy); glowMaterials.Add(copy); changed = true;
            }
            if (changed) renderer.sharedMaterials = materials;
        }
        glowingPages[index] = owned.ToArray();
        energy[index] = root.gameObject.AddComponent<VisionPageEnergy>();
    }
    private static void Part(Transform parent, Vector3 position, Vector3 scale, Material material)
    {
        var part = GameObject.CreatePrimitive(PrimitiveType.Cube);
        part.GetComponent<Collider>().enabled = false;
        Destroy(part.GetComponent<Collider>());
        part.transform.SetParent(parent, false); part.transform.localPosition = position; part.transform.localScale = scale;
        part.GetComponent<Renderer>().sharedMaterial = material;
    }
    private void OnDisable() { SetVisible(false); }
    private void OnDestroy()
    {
        if (cover != null) Destroy(cover); if (pages != null) Destroy(pages);
        foreach (var mat in glowMaterials) if (mat != null) Destroy(mat);
    }
}

// Each sample is born in world space and never seeks the moving pilot again.
internal sealed class VisionPageEnergy : MonoBehaviour
{
    private const int Strands = 16, Capacity = 96;
    private readonly System.Collections.Generic.List<PageSurface> surfaces = new();
    public Vector3 SourcePosition { get; private set; }
    public bool HasSource { get; private set; }
    private float sampledOpen = -1f;
    private sealed class PageSurface
    {
        public Renderer renderer;
        public Mesh mesh;
        public bool owned;
        public readonly System.Collections.Generic.List<Vector3> vertices = new();
        public readonly System.Collections.Generic.List<int> triangles = new();
        public readonly System.Collections.Generic.List<int> scratch = new();
        public int[] slots;
        public void Refresh()
        {
            if (renderer is SkinnedMeshRenderer skin) skin.BakeMesh(mesh);
            mesh.GetVertices(vertices);
            triangles.Clear();
            foreach (int slot in slots)
            {
                if (slot >= mesh.subMeshCount) continue;
                scratch.Clear(); mesh.GetTriangles(scratch, slot);
                triangles.AddRange(scratch);
            }
        }
    }
    private LineRenderer[] lines;
    private Material material, runeMaterial;
    private ParticleSystem runes;
    private float runeBudget;
    private int runeSequence;
    private readonly ParticleSystem.Particle[] runeParticles = new ParticleSystem.Particle[64];
    private readonly Vector3[,] births = new Vector3[Strands, Capacity];
    private readonly float[] birthTimes = new float[Capacity];
    private readonly float[] birthStrength = new float[Capacity];
    private readonly Vector3[] vertices = new Vector3[Capacity];
    private int head = -1, count;
    private Vector3 lastOrigin;
    private bool PagePoint(int strand, out Vector3 point)
    {
        point = transform.position;
        if (surfaces.Count == 0) return false;
        for (int k = 0; k < surfaces.Count; k++)
        {
            var page = surfaces[(strand + k) % surfaces.Count];
            if (!page.renderer.gameObject.activeInHierarchy || !page.renderer.enabled || page.triangles.Count < 3) continue;
            // Pick an actual page triangle, favouring its exposed side facing the reader.
            Vector3 reader = transform.parent != null ? transform.parent.position + Vector3.up * VisionBookSettings.Default.shoulderHeight : transform.position;
            for (int attempt = 0; attempt < 24; attempt++)
            {
                int triangle = ((strand * 37 + attempt * 13) % (page.triangles.Count / 3)) * 3;
                Vector3 a = page.renderer.transform.TransformPoint(page.vertices[page.triangles[triangle]]);
                Vector3 b = page.renderer.transform.TransformPoint(page.vertices[page.triangles[triangle + 1]]);
                Vector3 c = page.renderer.transform.TransformPoint(page.vertices[page.triangles[triangle + 2]]);
                Vector3 normal = Vector3.Cross(b - a, c - a).normalized;
                float u = .2f + .35f * Mathf.Repeat(strand * .618034f, 1f);
                float v = .2f + .2f * Mathf.Repeat(strand * .381966f, 1f);
                point = a * (1 - u - v) + b * u + c * v;
                if (Vector3.Dot(normal, reader - point) > 0 || attempt == 23)
                {
                    point += normal * .008f;
                    return true;
                }
            }
        }
        return false;
    }

    private void Initialize()
    {
        var shader = Resources.Load<Shader>("VisionPageEnergy");
        if (shader == null) return;
        foreach (var renderer in GetComponentsInChildren<Renderer>(true))
        {
            var slots = new System.Collections.Generic.List<int>();
            var materials = renderer.sharedMaterials;
            for (int slot = 0; slot < materials.Length; slot++)
                if (materials[slot] != null && materials[slot].name.IndexOf("page", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    slots.Add(slot);
            if (slots.Count == 0) continue;
            Mesh mesh = null; bool owned = renderer is SkinnedMeshRenderer;
            if (owned) mesh = new Mesh { name = "Vision page emission surface" };
            else if (renderer.TryGetComponent<MeshFilter>(out var filter)) mesh = filter.sharedMesh;
            if (mesh == null || (!owned && !mesh.isReadable)) continue;
            var surface = new PageSurface { renderer = renderer, mesh = mesh, owned = owned, slots = slots.ToArray() };
            surface.Refresh(); surfaces.Add(surface);
        }
        material = new Material(shader);
        CreateRunes();
        lines = new LineRenderer[Strands];
        for (int i = 0; i < Strands; i++)
        {
            var child = new GameObject("Mana filament " + (i + 1));
            child.transform.SetParent(transform, false);
            var line = child.AddComponent<LineRenderer>();
            line.sharedMaterial = material; line.useWorldSpace = true;
            line.textureMode = LineTextureMode.Stretch;
            line.widthCurve = new AnimationCurve(new Keyframe(0, .7f), new Keyframe(.2f, 1f), new Keyframe(1, 0));
            line.numCapVertices = 3;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false; line.positionCount = 0;
            lines[i] = line;
        }
    }

    private void CreateRunes()
    {
        var shader = Resources.Load<Shader>("VisionFloatingRunes");
        if (shader == null) return;
        runeMaterial = new Material(shader);
        var child = new GameObject("Floating page runes");
        child.transform.SetParent(transform, false);
        runes = child.AddComponent<ParticleSystem>();
        runes.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = runes.main;
        main.playOnAwake = false; main.loop = true; main.maxParticles = 64;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.scalingMode = ParticleSystemScalingMode.Shape;
        main.startSpeed = 0; main.startLifetime = 1.5f;
        var emission = runes.emission; emission.enabled = false;
        var shape = runes.shape; shape.enabled = false;
        var sheet = runes.textureSheetAnimation; sheet.enabled = true;
        sheet.numTilesX = 4; sheet.numTilesY = 2;
        sheet.frameOverTime = 0f;
        sheet.startFrame = new ParticleSystem.MinMaxCurve(0f, 7f);
        var fade = runes.colorOverLifetime; fade.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
            new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, .25f), new GradientAlphaKey(.8f, .65f), new GradientAlphaKey(0, 1) });
        fade.color = gradient;
        var size = runes.sizeOverLifetime; size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0, .5f, 1, 1f));
        var renderer = runes.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = runeMaterial;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        runes.Play();
    }

    private void UpdateRunes(VisionBookSettings s, float strength, Vector3 origin)
    {
        if (runes == null) return;
        if (!runes.isPlaying) runes.Play();
        runeBudget = Mathf.Min(runeBudget + Time.deltaTime * s.runeRate * strength, 4f);
        while (runeBudget >= 1f)
        {
            runeBudget -= 1f;
            int index = runeSequence++;
            if (!PagePoint(index, out var position)) continue;
            float phase = index * 2.39996f;
            var emit = new ParticleSystem.EmitParams {
                position = position,
                velocity = new Vector3(Mathf.Sin(phase) * .12f, s.runeRiseSpeed, Mathf.Cos(phase) * .12f),
                startLifetime = s.runeLifetime,
                startSize = s.runeSize * Mathf.Lerp(.75f, 1.25f, Mathf.Repeat(index * .618f, 1)),
                startColor = s.pageGlowColor,
                rotation = Mathf.Sin(phase) * 15f,
                angularVelocity = Mathf.Cos(phase) * 12f
            };
            runes.Emit(emit, 1);
        }
        int count = runes.GetParticles(runeParticles);
        for (int i = 0; i < count; i++)
        {
            float distance = Vector3.Distance(origin, runeParticles[i].position);
            float fadeStart = s.energyMaxTrail * .6f;
            float fadeProgress = Mathf.InverseLerp(fadeStart, s.energyMaxTrail, distance);
            float fade = 1f - Mathf.SmoothStep(0f, 1f, fadeProgress);
            Color color = s.pageGlowColor; color.a = fade;
            runeParticles[i].startColor = color;
            if (distance >= s.energyMaxTrail) runeParticles[i].remainingLifetime = 0;
        }
        runes.SetParticles(runeParticles, count);
    }

    public void Draw(VisionBookSettings s, float open)
    {
        if (lines == null) Initialize();
        if (lines == null) return;
        if (Mathf.Abs(open - sampledOpen) > .001f)
        {
            foreach (var surface in surfaces) if (surface.owned) surface.Refresh();
            sampledOpen = open;
        }
        HasSource = PagePoint(0, out Vector3 origin);
        SourcePosition = origin;
        if (!HasSource)
        {
            foreach (var line in lines) line.enabled = false;
            return;
        }
        if ((origin - lastOrigin).sqrMagnitude > 400f) { count = 0; head = -1; }
        lastOrigin = origin;
        float strength = Mathf.SmoothStep(0, 1, open);
        UpdateRunes(s, strength, origin);
        head = (head + 1) % Capacity; count = Mathf.Min(count + 1, Capacity);
        birthTimes[head] = Time.time; birthStrength[head] = strength;
        float lifetime = Mathf.Max(.025f, s.energyLifetime);
        float scale = Mathf.Abs(transform.lossyScale.x);
        int visibleStrands = Mathf.Clamp(Mathf.RoundToInt(8f * s.energyEmissionMultiplier), 0, Strands);
        for (int i = 0; i < Strands; i++)
        {
            float phase = i * 2.39996f;
            PagePoint(i, out Vector3 source);
            births[i, head] = source;
            int used = 0;
            for (int j = 0; j < count; j++)
            {
                int index = (head - j + Capacity) % Capacity;
                float age = Time.time - birthTimes[index];
                if (age > lifetime || birthStrength[index] < .005f) break;
                float t = age / lifetime;
                // Drift uses only birth time and age, never current movement or rotation.
                float wave = birthTimes[index] * .65f + age * 1.2f + phase;
                Vector3 drift = new Vector3(Mathf.Sin(wave) * .18f, 0, Mathf.Cos(wave * .8f) * .18f) * t * scale;
                Vector3 position = births[i, index] + Vector3.up * (t * s.energyHeight * scale * Mathf.Lerp(.55f, 1.25f, Mathf.Repeat(i * .618034f, 1f))) + drift;
                float distance = Vector3.Distance(origin, position);
                if (distance > s.energyMaxTrail) break;
                vertices[used++] = position;
            }
            var line = lines[i];
            line.enabled = used > 1 && i < visibleStrands;
            if (!line.enabled) continue;
            line.widthMultiplier = s.energyWidth * scale * (i < 12 ? .55f : .25f);
            Color color = s.pageGlowColor * 2f; color.a = strength * .3f;
            line.startColor = color; line.endColor = new Color(color.r, color.g, color.b, 0);
            line.positionCount = used;
            for (int j = 0; j < used; j++) line.SetPosition(j, vertices[j]);
        }
    }
    private void OnDisable()
    {
        count = 0; head = -1; runeBudget = 0;
        if (runes != null) runes.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        if (lines != null) foreach (var line in lines) if (line != null) line.enabled = false;
    }
    private void OnDestroy()
    {
        if (material != null) Destroy(material);
        if (runeMaterial != null) Destroy(runeMaterial);
        foreach (var surface in surfaces) if (surface.owned && surface.mesh != null) Destroy(surface.mesh);
    }
}
