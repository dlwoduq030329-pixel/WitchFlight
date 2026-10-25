using UnityEngine;

// Display only. Radius is the actual spherical damage radius, including at flight altitude.
public sealed class MagicAreaVisual : MonoBehaviour
{
    private Material material;
    private LineRenderer countdown;
    private Vector3 authoredScale = Vector3.one;
    private bool burst;
    private float burstStarted, burstLifetime, burstExpandSeconds, burstStartScale, burstTargetScale;
    private Color burstColor;

    public static MagicAreaVisual Create(GameObject prefab, string label)
    {
        var root = new GameObject(label);
        var visual = root.AddComponent<MagicAreaVisual>();
        var custom = CombatPresentation.InstantiateVfx(prefab,Vector3.zero,Quaternion.identity,root.transform);
        if (custom != null) { visual.authoredScale = root.transform.localScale; return visual; }
        visual.material = CombatPresentation.CreateEffectMaterial(Color.cyan);
        Ring(root.transform,visual.material,Quaternion.identity);
        Ring(root.transform,visual.material,Quaternion.Euler(90,0,0));
        Ring(root.transform,visual.material,Quaternion.Euler(0,0,90));
        visual.countdown = Ring(root.transform,visual.material,Quaternion.identity);
        return visual;
    }

    public static LineRenderer Ring(Transform parent, Material material, Quaternion rotation)
    {
        var obj = new GameObject("Magic ring");
        obj.transform.SetParent(parent,false);
        obj.transform.localRotation = rotation;
        var ring = obj.AddComponent<LineRenderer>();
        ring.sharedMaterial = material; ring.useWorldSpace = false; ring.loop = true;
        ring.positionCount = 64; ring.widthMultiplier = .14f;
        ring.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        for (int i=0;i<64;i++)
        {
            float angle = i * Mathf.PI * 2f / 64;
            ring.SetPosition(i,new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle)));
        }
        return ring;
    }

    public void SetArea(Vector3 center, float radius, Color color, float progress)
    {
        transform.position = center;
        transform.localScale = authoredScale * Mathf.Max(.1f,radius);
        if (material != null) material.color = color;
        if (countdown != null)
        {
            countdown.gameObject.SetActive(progress > 0f);
            countdown.transform.localScale = Vector3.one * Mathf.Max(.02f,1f-progress);
        }
    }

    public static void ThunderFlash(Vector3 point, float radius, MagicStatEntry stats)
    {
        BattleAudio.PlayMagic(MagicType.Thunder, MagicSound.AreaWarning, point);
        CreateBurst(point, radius, stats.thunderFlashVfxPrefab, stats.thunderVfxReferenceRadius,
            1f, .01f, .14f, new Color(1f,.92f,.6f,.3f));
    }

    public static void ThunderBurst(Vector3 point, MagicStatEntry stats)
    {
        BattleAudio.PlayMagic(MagicType.Thunder, MagicSound.Impact, point);
        float expand = stats.thunderBurstExpandSeconds > 0f ? stats.thunderBurstExpandSeconds : .12f;
        float start = stats.thunderBurstStartScale > 0f ? stats.thunderBurstStartScale : .08f;
        CreateBurst(point, stats.radius, stats.thunderBurstVfxPrefab != null ? stats.thunderBurstVfxPrefab : stats.impactVfxPrefab,
            stats.thunderVfxReferenceRadius, start, expand, Mathf.Max(expand + .05f, stats.vfxLifetime), new Color(.7f,.85f,1f,.5f));
    }

    private static void CreateBurst(Vector3 point, float radius, GameObject prefab, float referenceRadius,
        float start, float expand, float lifetime, Color color)
    {
        var root = new GameObject("Thunder area flash / burst");
        // Start particle systems only after the initial small scale has been applied.
        root.SetActive(false);
        root.transform.position = point;
        var visual = root.AddComponent<MagicAreaVisual>();
        visual.burst = true;
        visual.burstStarted = Time.time;
        visual.burstLifetime = lifetime;
        visual.burstExpandSeconds = expand;
        visual.burstStartScale = Mathf.Clamp01(start);
        var custom = CombatPresentation.InstantiateVfx(prefab, point, Quaternion.identity, root.transform);
        visual.burstTargetScale = Mathf.Max(.1f, radius) / (custom != null && referenceRadius > 0f ? referenceRadius : 1f);
        if (custom == null)
        {
            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            var collider = sphere.GetComponent<Collider>(); collider.enabled = false; Destroy(collider);
            sphere.transform.SetParent(root.transform, false);
            sphere.transform.localScale = Vector3.one * 2f; // Unity sphere radius .5 -> authored radius 1.
            visual.material = CombatPresentation.CreateEffectMaterial(color);
            var renderer = sphere.GetComponent<Renderer>();
            renderer.sharedMaterial = visual.material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }
        visual.burstColor = color;
        root.transform.localScale = Vector3.one * visual.burstTargetScale * visual.burstStartScale;
        root.SetActive(true);
    }

    public static float BurstScale(float elapsed, float expandSeconds, float startScale)
    {
        float t = Mathf.Clamp01(elapsed / Mathf.Max(.01f, expandSeconds));
        return Mathf.Lerp(Mathf.Clamp01(startScale), 1f, 1f - (1f-t)*(1f-t)*(1f-t));
    }

    private void Update()
    {
        if (!burst) return;
        if (BattleManager.Instance == null || !BattleManager.Instance.IsGameplayActive)
        { Destroy(gameObject); return; }
        float age = Time.time - burstStarted;
        if (age >= burstLifetime) { Destroy(gameObject); return; }
        transform.localScale = Vector3.one * burstTargetScale * BurstScale(age, burstExpandSeconds, burstStartScale);
        if (material != null)
        {
            Color color = burstColor;
            color.a *= 1f - Mathf.Clamp01((age-burstExpandSeconds) / Mathf.Max(.01f,burstLifetime-burstExpandSeconds));
            material.color = color;
        }
    }

    private void OnDestroy() { if (material != null) Destroy(material); }
}
