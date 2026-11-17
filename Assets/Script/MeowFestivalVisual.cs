using UnityEngine;

public sealed class MeowFestivalVisual : MonoBehaviour
{
    [SerializeField] private Color zoneColor = new Color(.18f, 1f, .48f, .6f);
    [SerializeField, Min(.1f)] private float zoneHeight = 1.2f;
    [SerializeField, Min(0f)] private float riseSpeed = .45f;
    private AudioSource song;
    private MagicStatEntry songSettings;
    private GameObject zone;
    private Mesh zoneMesh;
    private Material zoneMaterial;
    private MaterialPropertyBlock zoneProperties;
    private MeshRenderer zoneRenderer;
    private static readonly int ColorId = Shader.PropertyToID("_ZoneColor");
    private static readonly int PhaseId = Shader.PropertyToID("_Phase");
    private static GameObject prefab;
    private readonly GameObject[] cats = new GameObject[3];
    private readonly Vector3[] starts = new Vector3[3];
    private Player owner;
    private int sequence = -1;
    private float elapsed;
    public void Initialize(Player player, MagicStatEntry settings)
    {
        owner = player;
        songSettings = settings;
        if (prefab == null) prefab = Resources.Load<GameObject>("MeowFestivalVisual");
    }
    public void Show(bool active)
    {
        if (!active) { Hide(); return; }
        EnsureZone();
        if (sequence != owner.FestivalSequence)
        {
            sequence = owner.FestivalSequence; elapsed = 0;
            PlaySong();
            for (int i=0; i<cats.Length; i++)
            {
                starts[i] = owner.FestivalOrigin + Vector3.up * (i*.12f);
                if (cats[i] == null && prefab != null) cats[i] = MagicVfxPool.Rent(prefab, starts[i], Quaternion.identity, transform);
                if (cats[i] != null) TimeVfxSpeed.Bind(cats[i], owner.Runner, owner.TeamIndex, false);
            }
        }
    }
    private void LateUpdate()
    {
        if (owner == null || !owner.HasMeowFestival) { Hide(); return; }
        elapsed += Time.deltaTime * owner.TimeFieldSpeed;
        float arrival = Mathf.SmoothStep(0,1,Mathf.Clamp01(elapsed/.8f));
        bool visible = !owner.IsHiddenBySmokeFor(Player.LocalPlayer);
        if (song != null) song.mute = !visible;
        if (zone != null)
        {
            zone.SetActive(visible);
            zone.transform.SetPositionAndRotation(owner.LockAimPoint, Quaternion.identity);
            zone.transform.localScale = new Vector3(owner.FestivalRadius, zoneHeight, owner.FestivalRadius);
            Color color = zoneColor;
            color.a *= Mathf.Clamp01(elapsed / .25f);
            zoneProperties.SetColor(ColorId, color);
            zoneProperties.SetFloat(PhaseId, elapsed * riseSpeed);
            zoneRenderer.SetPropertyBlock(zoneProperties);
        }
        for (int i=0; i<cats.Length; i++)
        {
            if (cats[i] == null) continue;
            cats[i].SetActive(visible);
            float angle = elapsed*2f+i*Mathf.PI*2f/3f;
            Vector3 destination = owner.LockAimPoint + new Vector3(Mathf.Cos(angle)*owner.FestivalRadius, .2f+Mathf.Sin(angle*2)*.12f, Mathf.Sin(angle)*owner.FestivalRadius);
            Vector3 previous = cats[i].transform.position;
            Vector3 position = Vector3.Lerp(starts[i],destination,arrival) + Vector3.up*(Mathf.Sin(arrival*Mathf.PI)*.5f);
            Vector3 forward = position-previous;
            Quaternion rotation = forward.sqrMagnitude > .0001f ? Quaternion.LookRotation(forward) : cats[i].transform.rotation;
            cats[i].transform.SetPositionAndRotation(position,rotation);
        }
    }
    public void Hide()
    {
        if (song != null && song.isPlaying) song.Stop();
        if (zone != null) zone.SetActive(false);
        for (int i=0; i<cats.Length; i++)
        {
            if (cats[i] != null) MagicVfxPool.Release(cats[i]);
            cats[i] = null;
        }
        sequence = -1;
    }
    private void OnDisable() => Hide();
    private void PlaySong()
    {
        MagicStatEntry stats = songSettings;
        if (stats.festivalSong == null) return;
        if (song == null)
        {
            song = gameObject.AddComponent<AudioSource>();
            song.playOnAwake = false;
            song.loop = true;
            song.spatialBlend = 1f;
            song.rolloffMode = AudioRolloffMode.Linear;
        }
        song.clip = stats.festivalSong;
        song.volume = stats.festivalSongVolume;
        song.minDistance = Mathf.Max(1f, owner.FestivalRadius * .25f);
        song.maxDistance = Mathf.Max(2f, owner.FestivalRadius * 2f);
        song.Play();
    }
    private void OnDestroy()
    {
        Hide();
        if (zone != null) Destroy(zone);
        if (zoneMesh != null) Destroy(zoneMesh);
        if (zoneMaterial != null) Destroy(zoneMaterial);
    }

    private void EnsureZone()
    {
        if (zone != null) return;
        Shader shader = Resources.Load<Shader>("MeowFestivalZone");
        if (shader == null) return;
        // World-space root avoids inheriting the rider's roll and character scale.
        zone = new GameObject("MeowFestivalRecoveryZone");
        zone.layer = gameObject.layer;
        zoneMaterial = new Material(shader);
        zoneProperties = new MaterialPropertyBlock();
        zoneRenderer = zone.AddComponent<MeshRenderer>();
        zoneRenderer.sharedMaterial = zoneMaterial;
        zoneRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        zoneRenderer.receiveShadows = false;
        const int sides = 96;
        var vertices = new Vector3[1 + (sides + 1) * 3];
        var uv = new Vector2[vertices.Length];
        var colors = new Color[vertices.Length];
        var triangles = new int[sides * 9];
        for (int i = 0; i <= sides; i++)
        {
            float t = (float)i / sides;
            float a = t * Mathf.PI * 2;
            Vector3 edge = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
            int disk = 1 + i, bottom = sides + 2 + i * 2;
            vertices[disk] = edge;
            vertices[bottom] = edge;
            vertices[bottom + 1] = edge + Vector3.up;
            uv[bottom] = new Vector2(t, 0);
            uv[bottom + 1] = new Vector2(t, 1);
            colors[bottom] = colors[bottom + 1] = Color.white;
            if (i == sides) continue;
            int k = i * 9;
            triangles[k] = 0; triangles[k + 1] = disk + 1; triangles[k + 2] = disk;
            triangles[k + 3] = bottom; triangles[k + 4] = bottom + 1; triangles[k + 5] = bottom + 2;
            triangles[k + 6] = bottom + 2; triangles[k + 7] = bottom + 1; triangles[k + 8] = bottom + 3;
        }
        zoneMesh = new Mesh { name = "MeowFestivalZone" };
        zoneMesh.vertices = vertices; zoneMesh.uv = uv; zoneMesh.colors = colors;
        zoneMesh.triangles = triangles; zoneMesh.RecalculateBounds();
        zone.AddComponent<MeshFilter>().sharedMesh = zoneMesh;
        zone.SetActive(false);
    }
}
