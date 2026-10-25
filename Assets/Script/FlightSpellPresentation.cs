using UnityEngine;

[DisallowMultipleComponent]
public sealed class FlightSpellPresentation : MonoBehaviour
{
    private Player owner;
    private GameObject circle;
    private Material circleMaterial;
    private Vector3 circleBaseScale = Vector3.one;
    private MagicType shownMagic;
    private int portal;
    private bool initialized;

    private void Awake() => owner = GetComponent<Player>();
    private void LateUpdate()
    {
        if (owner == null || owner.Object == null || !owner.Object.IsValid) { ClearCircle(); return; }
        if (!initialized) { initialized = true; portal = owner.PortalExitSequence; }
        if (portal != owner.PortalExitSequence)
        {
            portal = owner.PortalExitSequence;
            var settings = FlightCombatTable.Default;
            if (settings != null && !owner.IsHiddenBySmokeFor(Player.LocalPlayer))
            {
                PortalPulse.Play(settings.portalEntryPrefab, owner.PortalEntryPosition, owner.PortalExitRotation, settings);
                PortalPulse.Play(settings.portalExitPrefab, owner.transform.position, owner.PortalExitRotation, settings);
            }
        }
        bool charging = owner.IsAlive && owner.LockProgress > 0f && owner.SelectedMagicStats.requiresTarget &&
            BattleManager.Instance != null && BattleManager.Instance.IsGameplayActive &&
            !owner.IsHiddenBySmokeFor(Player.LocalPlayer);
        if (!charging) { if (circle != null) circle.SetActive(false); return; }
        MagicStatEntry stats = owner.SelectedMagicStats;
        if (circle == null || shownMagic != stats.magic) BuildCircle(stats);
        circle.SetActive(true);
        Vector3 offset = stats.lockChargeCircleOffset == Vector3.zero ? new Vector3(0f, .8f, -.7f) : stats.lockChargeCircleOffset;
        circle.transform.SetPositionAndRotation(owner.transform.TransformPoint(offset), owner.transform.rotation *
            Quaternion.AngleAxis(Time.time * (12f + (int)stats.magic * 2f), Vector3.forward));
        float size = stats.lockChargeCircleSize > 0f ? stats.lockChargeCircleSize : 1.8f;
        circle.transform.localScale = circleBaseScale * size * Mathf.Lerp(.65f, 1f, owner.LockProgress);
    }

    private void BuildCircle(MagicStatEntry stats)
    {
        ClearCircle();
        shownMagic = stats.magic;
        circle = CombatPresentation.InstantiateVfx(stats.lockChargeCirclePrefab, transform.position, transform.rotation);
        if (circle != null) { circleBaseScale = circle.transform.localScale; RearThreatCamera.ExcludeLocalEffect(circle, owner); return; }
        circleBaseScale = Vector3.one;
        circle = new GameObject(stats.magic + " charging circle");
        circleMaterial = CombatPresentation.CreateEffectMaterial(CombatPresentation.MagicColor(stats.magic));
        MakeLoop(circle.transform, circleMaterial, 48, .5f, 1);
        MakeLoop(circle.transform, circleMaterial, 48, .38f, 1);
        int points = 5 + (int)stats.magic % 4;
        MakeLoop(circle.transform, circleMaterial, points, .45f, points % 2 == 1 ? 2 : 1);
        RearThreatCamera.ExcludeLocalEffect(circle, owner);
    }

    internal static void MakeLoop(Transform parent, Material material, int count, float radius, int stride)
    {
        var root = new GameObject("Sigil line", typeof(LineRenderer));
        root.transform.SetParent(parent, false);
        var line = root.GetComponent<LineRenderer>();
        line.sharedMaterial = material;
        line.useWorldSpace = false;
        line.loop = true;
        line.widthMultiplier = .018f;
        line.positionCount = count;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        for (int i = 0; i < count; i++)
        {
            float angle = 2f * Mathf.PI * i * stride / count;
            line.SetPosition(i, new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * radius);
        }
    }

    private void ClearCircle()
    {
        if (circle != null) Destroy(circle);
        if (circleMaterial != null) Destroy(circleMaterial);
        circle = null; circleMaterial = null;
    }
    private void OnDisable() { ClearCircle(); initialized = false; }
}

public sealed class PortalPulse : MonoBehaviour
{
    private Material ownedMaterial;
    private float age, seconds, size;
    private bool authored;
    public static void Play(GameObject prefab, Vector3 position, Quaternion rotation, FlightCombatTable settings)
    {
        var root = new GameObject("Portal passage VFX");
        root.transform.SetPositionAndRotation(position, rotation);
        var effect = root.AddComponent<PortalPulse>();
        effect.seconds = Mathf.Max(.1f, settings.portalVfxSeconds);
        effect.size = Mathf.Max(.1f, settings.portalVfxSize);
        effect.authored = CombatPresentation.InstantiateVfx(prefab, position, rotation, root.transform) != null;
        if (!effect.authored)
        {
            effect.ownedMaterial = CombatPresentation.CreateEffectMaterial(new Color(.4f, .85f, 1f, .8f));
            FlightSpellPresentation.MakeLoop(root.transform, effect.ownedMaterial, 48, .5f, 1);
            FlightSpellPresentation.MakeLoop(root.transform, effect.ownedMaterial, 48, .35f, 1);
        }
    }
    private void Update()
    {
        age += Time.deltaTime;
        float progress = Mathf.Clamp01(age / seconds);
        transform.localScale = Vector3.one * size * (authored ? 1f : Mathf.Lerp(.2f, 1.4f, progress));
        if (ownedMaterial != null) ownedMaterial.color = new Color(.4f, .85f, 1f, .8f * (1f - progress));
        if (age >= seconds) Destroy(gameObject);
    }
    private void OnDestroy() { if (ownedMaterial != null) Destroy(ownedMaterial); }
}
