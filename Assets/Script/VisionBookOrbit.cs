using UnityEngine;

// Local presentation of replicated book count; no extra network objects or RPCs.
// Read the final rendered bones after flight banking/bobbing and cosmetic pose solvers.
[DefaultExecutionOrder(16000)]
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
    private VisionEmpoweredElectricity electricity;
    private readonly System.Collections.Generic.List<Material> glowMaterials = new();
    private void Awake() { player = GetComponent<Player>(); }
    private void LateUpdate()
    {
        bool show = player != null && player.Object != null && player.Object.IsValid && player.IsAlive && player.HasVisionEquipped;
        if (!show) { SetVisible(false); electricity?.Hide(); return; }
        var s = VisionBookSettings.Default;
        int count = Mathf.Clamp(s.bookCount, 1, 12);
        bool charged = player.VisionOpenBooks >= count;
        if (charged && electricity == null) electricity = new VisionEmpoweredElectricity(transform);
        if (charged) electricity.Draw(s);
        else electricity?.Hide();
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
                if (original == null || (original != pages && !VisionPageEnergy.IsPageSurface(renderer, original))) continue;
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
    private void OnDisable() { SetVisible(false); electricity?.Hide(); }
    private void OnDestroy()
    {
        if (cover != null) Destroy(cover); if (pages != null) Destroy(pages);
        electricity?.Dispose();
        foreach (var mat in glowMaterials) if (mat != null) Destroy(mat);
    }
}

// Each sample is born in world space and never seeks the moving pilot again.
internal sealed class VisionPageEnergy : MonoBehaviour
{
    // The authored Book_Page mesh may use the Book material override. Page1..6
    // are animation helper meshes and can all be inactive in the open pose.
    internal static bool IsPageSurface(Renderer renderer, Material material)
    {
        return renderer.name.Equals("Book_Page", System.StringComparison.OrdinalIgnoreCase) ||
            (material != null && material.name.IndexOf("page", System.StringComparison.OrdinalIgnoreCase) >= 0);
    }
    private bool warnedMissingSource;
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
    private Vector3 previousRuneOrigin;
    private bool hasRuneOrigin;
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
                if (materials[slot] != null && IsPageSurface(renderer, materials[slot]))
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
            new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, .02f), new GradientAlphaKey(.8f, .65f), new GradientAlphaKey(0, 1) });
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
        float speed = hasRuneOrigin ? Vector3.Distance(origin, previousRuneOrigin) / Mathf.Max(.001f, Time.deltaTime) : 0f;
        previousRuneOrigin = origin; hasRuneOrigin = true;
        // A birth-time lifetime cap keeps old world-space glyphs short without chasing the book.
        float lifetime = Mathf.Min(s.runeLifetime, Mathf.Max(.04f, s.runeMaxTrail / Mathf.Max(1f, speed)));
        float rate = Mathf.Min(24f, s.runeRate * (1f + Mathf.Clamp01(speed / 30f)));
        runeBudget = Mathf.Min(runeBudget + Time.deltaTime * rate * strength, 8f);
        while (runeBudget >= 1f)
        {
            runeBudget -= 1f;
            int index = runeSequence++;
            if (!PagePoint(index, out var position)) continue;
            float phase = index * 2.39996f;
            var emit = new ParticleSystem.EmitParams {
                position = position,
                velocity = new Vector3(Mathf.Sin(phase) * .12f, s.runeRiseSpeed, Mathf.Cos(phase) * .12f),
                startLifetime = lifetime,
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
            float fadeStart = s.runeMaxTrail * .35f;
            float fadeProgress = Mathf.InverseLerp(fadeStart, s.runeMaxTrail, distance);
            float fade = 1f - Mathf.SmoothStep(0f, 1f, fadeProgress);
            Color color = Color.Lerp(s.pageGlowColor,s.arcaneBlueColor,(i % 3)*.35f); color.a = fade;
            runeParticles[i].startColor = color;
            if (distance >= s.runeMaxTrail) runeParticles[i].remainingLifetime = 0;
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
            if (open > .95f && !warnedMissingSource)
            {
                warnedMissingSource = true;
                Debug.LogWarning("[VisionBooks] Open book has no active readable page mesh. Check Book_Page renderer and mesh Read/Write setting.", this);
            }
            return;
        }
        warnedMissingSource = false;
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
            line.startColor = color;
            Color tip = s.arcaneBlueColor * 2f; tip.a = 0f; line.endColor = tip;
            line.positionCount = used;
            for (int j = 0; j < used; j++) line.SetPosition(j, vertices[j]);
        }
    }
    private void OnDisable()
    {
        count = 0; head = -1; runeBudget = 0; hasRuneOrigin = false;
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

// Reused local renderers follow animated bones; readiness comes from replicated book state.
internal sealed class VisionEmpoweredElectricity
{
    private const int Arcs = 10, Slots = 16, Points = 9;
    private readonly Transform owner, root;
    private readonly Transform[] anchors;
    private readonly LineRenderer[] cores = new LineRenderer[Slots], halos = new LineRenderer[Slots];
    private readonly Vector3[] points = new Vector3[Points];
    private readonly Vector3[,] offsets = new Vector3[Slots, Points];
    private readonly float[] startedAt = new float[Slots], duration = new float[Slots];
    private readonly float[] pathStart = new float[Slots], pathEnd = new float[Slots], reach = new float[Slots];
    private readonly Material material;
    private Material auraMaterial;
    private Mesh auraMesh;
    private Vector3[] auraRestVertices, auraDeformedVertices;
    private readonly Player auraPlayer;
    private Transform auraVeil;
    private VisionWandFocus wandFocus;
    private float auraElapsed, moteBudget;
    private Vector3 previousAuraPosition, auraBend, auraBendVelocity;
    private bool hasAuraPosition;
    private ParticleSystem auraMotes;
    private Material moteMaterial;
    private int moteSequence;
    private readonly System.Random random = new System.Random();
    private readonly float[] widths = new float[Slots];
    private float nextPulse;
    private int pulsesRemaining, nextArc;
    private readonly bool[] active = new bool[Slots];
    private readonly Vector3[,] localNormals = new Vector3[Slots, 3];
    private readonly Vector3[] pathPoints = new Vector3[3], pathNormals = new Vector3[3];
    private readonly int[] routes = new int[Slots];
    private static readonly int[,] Paths = { {1,2,3}, {4,5,6}, {8,9,10}, {11,12,13},
        {0,1,2}, {0,4,5}, {0,7,8}, {0,7,11} };
    private static readonly float[] SurfaceRadii = { .18f,.09f,.065f,.045f,.09f,.065f,.045f,.16f,
        .11f,.07f,.055f,.11f,.07f,.055f };
    private static readonly Vector3[] Fallback = {
        new Vector3(0,1.3f,0), new Vector3(-.3f,1.3f,0), new Vector3(-.45f,1.1f,0), new Vector3(-.6f,.9f,.1f),
        new Vector3(.3f,1.3f,0), new Vector3(.45f,1.1f,0), new Vector3(.6f,.9f,.1f), new Vector3(0,.8f,0),
        new Vector3(-.18f,.7f,0), new Vector3(-.18f,.4f,0), new Vector3(-.18f,.1f,.1f),
        new Vector3(.18f,.7f,0), new Vector3(.18f,.4f,0), new Vector3(.18f,.1f,.1f) };

    public VisionEmpoweredElectricity(Transform owner)
    {
        this.owner = owner;
        auraPlayer = owner.GetComponent<Player>();
        root = new GameObject("Vision empowered electricity").transform;
        root.SetParent(owner, false);
        anchors = new Transform[Fallback.Length];
        var animator = owner.GetComponent<Animator>();
        if (animator != null && animator.isHuman && animator.avatar != null && animator.avatar.isValid)
        {
            var bones = new[] { HumanBodyBones.Chest, HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand,
                HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand, HumanBodyBones.Hips,
                HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot,
                HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot };
            for (int i = 0; i < anchors.Length; i++) anchors[i] = animator.GetBoneTransform(bones[i]);
        }
        var shader = Resources.Load<Shader>("VisionPageEnergy");
        if (shader != null) { material = new Material(shader); material.SetFloat("_Steady", 1f); }
        for (int i = 0; i < Slots; i++)
        {
            cores[i] = MakeLine("Electric core " + i);
            halos[i] = MakeLine("Electric glow " + i);
            if (i >= Arcs)
            {
                int n = i - Arcs;
                routes[i] = n < 4 ? n : n + 2;
                pathStart[i] = 0f; pathEnd[i] = 2f; reach[i] = .08f;
                BindSurface(i);
                for (int j = 0; j < Points; j++)
                    offsets[i,j] = new Vector3(.01f,0,Mathf.Sin(i*3.7f+j*2.4f)*.035f);
            }
        }
    }
    private void CreateRisingAura()
    {
        var flowShader = Resources.Load<Shader>("VisionPageEnergy");
        if (flowShader != null)
        {
            auraMaterial = new Material(flowShader);
            auraMaterial.SetFloat("_Aura", 1f);
            // One continuous open veil, no disconnected billboards or moving tufts.
            const int columns = 64, rows = 16;
            var vertices = new Vector3[(columns+1)*(rows+1)];
            var uv = new Vector2[vertices.Length];
            var colors = new Color[vertices.Length];
            var triangles = new int[columns*rows*6];
            int ti = 0;
            for (int y = 0; y <= rows; y++)
            for (int x = 0; x <= columns; x++)
            {
                float h = y/(float)rows, angle = x/(float)columns*Mathf.PI*2;
                float radius = .3f + Mathf.Sin(h*Mathf.PI*.7f)*.23f;
                int v = y*(columns+1)+x;
                vertices[v] = new Vector3(Mathf.Cos(angle)*radius,h,Mathf.Sin(angle)*radius);
                uv[v] = new Vector2(h,x/(float)columns); colors[v] = Color.white;
                if (y == rows || x == columns) continue;
                triangles[ti++] = v; triangles[ti++] = v+columns+1; triangles[ti++] = v+1;
                triangles[ti++] = v+1; triangles[ti++] = v+columns+1; triangles[ti++] = v+columns+2;
            }
            auraRestVertices = vertices; auraDeformedVertices = new Vector3[vertices.Length];
            auraMesh = new Mesh { name = "Continuous rising energy veil" };
            auraMesh.MarkDynamic();
            auraMesh.vertices = vertices; auraMesh.uv = uv; auraMesh.colors = colors; auraMesh.triangles = triangles;
            auraMesh.bounds = new Bounds(new Vector3(0,.5f,0),new Vector3(6,6,6));
            var go = new GameObject("Continuous rising body aura");
            auraVeil = go.transform; auraVeil.SetParent(root,false);
            go.AddComponent<MeshFilter>().sharedMesh = auraMesh;
            var renderer = go.AddComponent<MeshRenderer>(); renderer.sharedMaterial = auraMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            moteMaterial = new Material(flowShader); moteMaterial.SetFloat("_Mote",1f);
            var motes = new GameObject("Floating aura light specks"); motes.transform.SetParent(root,false);
            auraMotes = motes.AddComponent<ParticleSystem>();
            auraMotes.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = auraMotes.main;
            main.playOnAwake = false; main.loop = true; main.maxParticles = 64;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startSpeed = 0;
            var emission = auraMotes.emission; emission.enabled = false;
            var shape = auraMotes.shape; shape.enabled = false;
            var fade = auraMotes.colorOverLifetime; fade.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] {new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1)},
                new[] {new GradientAlphaKey(0,0),new GradientAlphaKey(.8f,.12f),new GradientAlphaKey(.6f,.6f),new GradientAlphaKey(0,1)});
            fade.color = gradient;
            var moteRenderer = auraMotes.GetComponent<ParticleSystemRenderer>();
            moteRenderer.sharedMaterial = moteMaterial;
            moteRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            moteRenderer.receiveShadows = false;
            auraMotes.Play();
        }
    }
    private LineRenderer MakeLine(string name)
    {
        var child = new GameObject(name); child.transform.SetParent(root, false);
        var line = child.AddComponent<LineRenderer>();
        line.sharedMaterial = material; line.useWorldSpace = true; line.positionCount = Points;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false; line.numCapVertices = 2;
        line.widthCurve = new AnimationCurve(new Keyframe(0, .15f), new Keyframe(.2f, 1), new Keyframe(.8f, 1), new Keyframe(1, .15f));
        return line;
    }
    private Vector3 Anchor(int index) => anchors[index] != null ? anchors[index].position : owner.TransformPoint(Fallback[index]);
    private Transform Bone(int index) => anchors[index] != null ? anchors[index] : owner;
    private void BindSurface(int arc)
    {
        int route = routes[arc];
        float angle = Mathf.Lerp(-35f, 35f, (float)random.NextDouble());
        for (int node = 0; node < 3; node++)
        {
            int index = Paths[route,node];
            Vector3 tangent = Anchor(Paths[route,Mathf.Min(2,node+1)]) - Anchor(Paths[route,Mathf.Max(0,node-1)]);
            if (tangent.sqrMagnitude < .00001f) tangent = Bone(index).up;
            tangent.Normalize();
            Vector3 radial = Vector3.ProjectOnPlane(Anchor(index) - (Anchor(0) + Anchor(7)) * .5f, tangent);
            if (radial.sqrMagnitude < .001f) radial = Vector3.ProjectOnPlane(owner.forward, tangent);
            if (radial.sqrMagnitude < .001f) radial = Vector3.ProjectOnPlane(owner.right, tangent);
            radial = Quaternion.AngleAxis(angle, tangent) * radial.normalized;
            // Store the outward direction relative to this particular animated bone.
            localNormals[arc,node] = Bone(index).InverseTransformDirection(radial);
        }
    }
    private void BuildSurfacePath(int arc, VisionBookSettings s)
    {
        int route = routes[arc];
        for (int node = 0; node < 3; node++)
        {
            int index = Paths[route,node];
            pathNormals[node] = Bone(index).TransformDirection(localNormals[arc,node]).normalized;
            pathPoints[node] = Anchor(index) + pathNormals[node] * SurfaceRadii[index];
        }
        // Split exactly at the elbow/knee so no point bridges straight across the joint.
        for (int j = 0; j < Points; j++)
        {
            float progress = j / (float)(Points-1);
            // Keep an exact joint sample even when the visible section changes length.
            bool crossesJoint = pathStart[arc] < 1f && pathEnd[arc] > 1f;
            float pathT = crossesJoint
                ? (progress <= .5f ? Mathf.Lerp(pathStart[arc], 1f, progress * 2f)
                    : Mathf.Lerp(1f, pathEnd[arc], (progress - .5f) * 2f))
                : Mathf.Lerp(pathStart[arc], pathEnd[arc], progress);
            int segment = Mathf.Min(1, Mathf.FloorToInt(pathT));
            float t = pathT-segment;
            Vector3 normal = Vector3.Slerp(pathNormals[segment], pathNormals[segment+1], t).normalized;
            Vector3 tangent = (pathPoints[segment+1]-pathPoints[segment]).normalized;
            Vector3 side = Vector3.Cross(tangent,normal).normalized;
            // Each segment has its own surface frame; the joint itself remains attached.
            float envelope = Mathf.Sin(t*Mathf.PI) * Mathf.Sin(progress*Mathf.PI);
            Vector3 jitter = normal * Mathf.Abs(offsets[arc,j].x) + side * offsets[arc,j].z;
            points[j] = Vector3.Lerp(pathPoints[segment],pathPoints[segment+1],t) +
                (normal * s.chargedElectricReach * reach[arc] + jitter) * envelope;
        }
    }
    public void Draw(VisionBookSettings s)
    {
        root.gameObject.SetActive(true);
        DrawChargedAura(s);
        if (wandFocus == null && auraPlayer != null) wandFocus = new VisionWandFocus(root);
        if (wandFocus != null) wandFocus.Draw(auraPlayer.MagicCastPosition,s);
        int alive = 0;
        for (int i = 0; i < Arcs; i++)
        {
            if (active[i] && Time.time >= startedAt[i] + duration[i])
            {
                active[i] = false;
            }
            if (active[i]) alive++;
        }
        int spawnCount = 0;
        bool pulse = alive == 0 && Time.time >= nextPulse;
        float longestPulse = 0f;
        if (pulse)
        {
            // One phrase is "jik", "ji-jik", or "ji-ji-jik", then an audible-feeling rest.
            if (pulsesRemaining == 0) pulsesRemaining = random.Next(1, 4);
            pulsesRemaining--;
            spawnCount = random.NextDouble() < .7 ? 1 : 2;
        }
        int firstArc = nextArc;
        for (int order = 0; order < Arcs; order++)
        {
            int i = (firstArc + order) % Arcs;
            if (!active[i] && spawnCount > 0)
            {
                spawnCount--; nextArc = (i + 1) % Arcs;
                active[i] = true; alive++;
                startedAt[i] = Time.time;
                duration[i] = Mathf.Lerp(.07f, .16f, (float)random.NextDouble());
                longestPulse = Mathf.Max(longestPulse, duration[i]);
                int previousRoute = routes[i];
                for (int attempt = 0; attempt < 12; attempt++)
                {
                    routes[i] = random.Next(Paths.GetLength(0));
                    bool occupied = routes[i] == previousRoute;
                    for (int other = 0; other < Arcs; other++)
                        if (other != i && active[other] && routes[other] == routes[i]) occupied = true;
                    if (!occupied) break;
                }
                pathStart[i] = Mathf.Lerp(0f, 1.15f, (float)random.NextDouble());
                pathEnd[i] = Mathf.Min(2f, pathStart[i] + Mathf.Lerp(.55f, 1.6f, (float)random.NextDouble()));
                reach[i] = Mathf.Lerp(.3f, 1f, (float)random.NextDouble());
                BindSurface(i);
                widths[i] = Mathf.Lerp(.85f, 1.25f, (float)random.NextDouble());
                for (int j = 0; j < Points; j++)
                    offsets[i,j] = new Vector3((float)random.NextDouble()-.5f, (float)random.NextDouble()-.5f,
                        (float)random.NextDouble()-.5f) * .12f;
            }
            cores[i].enabled = halos[i].enabled = active[i] && material != null;
            if (!active[i] || material == null) continue;
            // Hold one jagged shape for 3–5 frames at 30 FPS; only follow the animated body.
            // Never morph one bolt across the body into another bolt.
            BuildSurfacePath(i, s);
            float age = Time.time - startedAt[i];
            float opacity = Mathf.SmoothStep(0,1,Mathf.Clamp01(age / .015f)) *
                Mathf.SmoothStep(0,1,Mathf.Clamp01((duration[i]-age) / .035f));
            Color core = Color.Lerp(s.chargedElectricColor,new Color(.9f,.8f,1f),.75f) * s.chargedElectricIntensity;
            core.a = opacity;
            Color halo = s.chargedElectricColor * s.chargedElectricIntensity; halo.a = .45f * opacity;
            cores[i].widthMultiplier = s.chargedElectricWidth * widths[i];
            halos[i].widthMultiplier = s.chargedElectricWidth * widths[i] * 3f;
            cores[i].startColor = cores[i].endColor = core;
            halos[i].startColor = halos[i].endColor = halo;
            cores[i].SetPositions(points); halos[i].SetPositions(points);
        }
        if (pulse)
        {
            float pause = pulsesRemaining > 0
                ? Mathf.Lerp(.045f, .12f, (float)random.NextDouble())
                : Mathf.Lerp(.22f, .6f, (float)random.NextDouble());
            nextPulse = Time.time + longestPulse + pause;
        }
    }
    private void DrawChargedAura(VisionBookSettings s)
    {
        for (int i = Arcs; i < Slots; i++) cores[i].enabled = halos[i].enabled = false;
        if (auraMaterial == null) CreateRisingAura();
        auraElapsed += Time.deltaTime;
        if (auraVeil == null) return;
        auraVeil.SetPositionAndRotation(Anchor(7)-Vector3.up*.55f,Quaternion.identity);
        auraVeil.localScale = new Vector3(s.chargedEnergyWidth/.7f,s.chargedEnergyHeight,s.chargedEnergyWidth/.7f);
        auraMaterial.SetColor("_AuraTint",s.chargedAuraColor*s.chargedAuraIntensity);
        auraMaterial.SetColor("_AuraBlue",s.arcaneBlueColor*s.chargedAuraIntensity);
        auraMaterial.SetFloat("_AuraClock",auraElapsed*s.chargedEnergyRiseSpeed/Mathf.Max(.1f,s.chargedEnergyHeight));
        Vector3 delta = hasAuraPosition ? owner.position-previousAuraPosition : Vector3.zero;
        bool teleported = delta.sqrMagnitude > 100f;
        Vector3 velocity = teleported ? Vector3.zero : delta/Mathf.Max(.001f,Time.deltaTime);
        if (!teleported && auraPlayer != null && auraPlayer.Object != null && auraPlayer.Object.IsValid)
            velocity = auraPlayer.FlightVisualVelocity;
        previousAuraPosition = owner.position; hasAuraPosition = true;
        if (teleported) { auraBend = auraBendVelocity = Vector3.zero; auraMotes.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear); }
        Vector3 targetBend = Vector3.ClampMagnitude(-velocity*s.chargedAuraDrag,s.chargedAuraMaxBend);
        auraBend = Vector3.SmoothDamp(auraBend,targetBend,ref auraBendVelocity,.2f,Mathf.Infinity,Time.deltaTime);
        // Deform the actual mesh, including its visible lower half. No per-frame arrays.
        for (int i = 0; i < auraRestVertices.Length; i++)
        {
            Vector3 rest = auraRestVertices[i];
            float h = rest.y;
            float weight = Mathf.SmoothStep(0f,1f,Mathf.Clamp01(h/.65f));
            Vector3 sway = new Vector3(Mathf.Sin(auraElapsed*2.3f-h*4f),0,Mathf.Cos(auraElapsed*1.7f-h*3f))*.045f;
            auraDeformedVertices[i] = rest + auraVeil.InverseTransformVector((auraBend+sway)*weight);
        }
        auraMesh.vertices = auraDeformedVertices;
        auraMaterial.SetFloat("_AuraOpacity",s.chargedAuraOpacity);
        if (auraMotes == null) return;
        if (!auraMotes.isPlaying) auraMotes.Play();
        moteBudget = Mathf.Min(4f,moteBudget+Time.deltaTime*s.chargedAuraMoteRate);
        while (moteBudget >= 1f)
        {
            moteBudget -= 1f;
            int index = moteSequence++;
            float angle = index*2.39996f, h = Mathf.Repeat(index*.4142f,1f);
            Vector3 radial = new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle));
            auraMotes.Emit(new ParticleSystem.EmitParams {
                position = auraVeil.position + radial*(.35f+h*.22f) + Vector3.up*(h*s.chargedEnergyHeight*.75f) + auraBend*h*h,
                velocity = Vector3.up*.55f + radial*.08f,
                startLifetime = Mathf.Min(1.1f,1.5f/Mathf.Max(1f,velocity.magnitude)),
                startSize = Mathf.Lerp(.018f,.045f,Mathf.Repeat(index*.618f,1f)),
                startColor = Color.Lerp(s.chargedAuraColor,new Color(.85f,.8f,1f),.7f)*1.3f
            },1);
        }
    }

    public void Hide()
    {
        if (root == null || !root.gameObject.activeSelf) return;
        System.Array.Clear(active,0,active.Length);
        pulsesRemaining = 0; nextPulse = Time.time + .1f;
        wandFocus?.Hide();
        auraElapsed = 0f; hasAuraPosition = false;
        auraBend = auraBendVelocity = Vector3.zero; moteBudget = 0;
        if (auraMotes != null) auraMotes.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
        root.gameObject.SetActive(false);
    }
    public void Dispose()
    {
        if (root != null) Object.Destroy(root.gameObject);
        if (material != null) Object.Destroy(material);
        if (auraMaterial != null) Object.Destroy(auraMaterial);
        if (auraMesh != null) Object.Destroy(auraMesh);
        if (moteMaterial != null) Object.Destroy(moteMaterial);
        wandFocus?.Dispose();
    }
}

// Local-only charged wand focus. Geometry/materials are allocated once and reused.
internal sealed class VisionWandFocus
{
    private readonly Transform root;
    private readonly LineRenderer[] rings = new LineRenderer[3];
    private readonly Vector3[] points = new Vector3[65];
    private readonly Material ribbonMaterial, glowMaterial;
    private readonly ParticleSystem glow;
    private readonly ParticleSystem.Particle[] particles = new ParticleSystem.Particle[2];
    private readonly Light light;
    private float clock;
    public VisionWandFocus(Transform parent)
    {
        root = new GameObject("Charged wand arcane focus").transform;
        root.SetParent(parent,false);
        var shader = Resources.Load<Shader>("VisionPageEnergy");
        if (shader != null)
        {
            ribbonMaterial = new Material(shader); ribbonMaterial.SetFloat("_Steady",1);
            glowMaterial = new Material(shader); glowMaterial.SetFloat("_Mote",1);
        }
        for (int i=0;i<rings.Length;i++)
        {
            var go = new GameObject("Orbiting energy band " + i); go.transform.SetParent(root,false);
            var line = go.AddComponent<LineRenderer>(); rings[i]=line;
            line.sharedMaterial=ribbonMaterial; line.useWorldSpace=false; line.positionCount=65;
            line.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows=false; line.numCapVertices=3;
            line.widthCurve=new AnimationCurve(new Keyframe(0,.15f),new Keyframe(.15f,1),new Keyframe(.65f,.65f),new Keyframe(1,0));
        }
        var orb = new GameObject("White violet luminous core"); orb.transform.SetParent(root,false);
        glow=orb.AddComponent<ParticleSystem>(); glow.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
        var main=glow.main; main.playOnAwake=false; main.maxParticles=2; main.simulationSpace=ParticleSystemSimulationSpace.Local;
        var emission=glow.emission; emission.enabled=false;
        var shape=glow.shape; shape.enabled=false;
        var renderer=glow.GetComponent<ParticleSystemRenderer>(); renderer.sharedMaterial=glowMaterial;
        renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off; renderer.receiveShadows=false;
        light=orb.AddComponent<Light>(); light.type=LightType.Point; light.shadows=LightShadows.None;
    }
    public void Draw(Vector3 position, VisionBookSettings s)
    {
        root.gameObject.SetActive(true); root.SetPositionAndRotation(position,Quaternion.identity);
        clock+=Time.deltaTime;
        for(int i=0;i<rings.Length;i++)
        {
            float spin=clock*s.wandFocusOrbitSpeed*(i==1 ? -1.17f : 1f+i*.21f);
            Quaternion tilt=Quaternion.Euler(35+i*53,spin*.27f+i*61,20+i*37);
            for(int j=0;j<points.Length;j++)
            {
                float angle=(spin+j/(float)(points.Length-1)*310)*Mathf.Deg2Rad;
                float radius=s.wandFocusRadius*(1+i*.17f);
                points[j]=tilt*new Vector3(Mathf.Cos(angle)*radius,Mathf.Sin(angle)*radius*.6f,0);
            }
            rings[i].SetPositions(points); rings[i].widthMultiplier=s.wandFocusBandWidth;
            Color color=Color.Lerp(s.pageGlowColor,s.arcaneBlueColor,i*.4f)*2;
            color.a=.85f; rings[i].startColor=color; color.a=0; rings[i].endColor=color;
        }
        if(!glow.isPlaying) glow.Play();
        for(int i=0;i<particles.Length;i++)
        {
            particles[i].position=Vector3.zero;
            particles[i].startLifetime=particles[i].remainingLifetime=1;
            particles[i].startSize=s.wandFocusRadius*(i==0 ? 1.1f : 3.5f);
            particles[i].startColor=i==0 ? new Color(3,2.5f,3.2f,1) : new Color(.65f,.22f,1f,.42f);
        }
        glow.SetParticles(particles,particles.Length);
        light.color=new Color(.7f,.4f,1f); light.intensity=s.wandFocusLightIntensity; light.range=2;
    }
    public void Hide() { root.gameObject.SetActive(false); clock=0; }
    public void Dispose()
    {
        if(root!=null) Object.Destroy(root.gameObject);
        if(ribbonMaterial!=null) Object.Destroy(ribbonMaterial);
        if(glowMaterial!=null) Object.Destroy(glowMaterial);
    }
}
