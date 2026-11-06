using UnityEngine;
using UnityEngine.Rendering;

// Lobby-only reusable hitscan feedback. The original spell material is never edited.
public sealed class RobbyInstantMagicVisual : MonoBehaviour
{
    private CombatMagicVisual authoredBeam;
    private LineRenderer core;
    private Material coreMaterial;
    private Color coreColor;
    private float started, duration;

    public void Initialize(MagicStatEntry stats)
    {
        authoredBeam = CombatMagicVisual.Beam(stats.magic, stats.beamVfxPrefab, stats.beamMaterial);
        authoredBeam.transform.SetParent(transform, false);
        core = gameObject.AddComponent<LineRenderer>();
        coreMaterial = CombatPresentation.CreateEffectMaterial(Color.white);
        core.sharedMaterial = coreMaterial;
        core.useWorldSpace = true;
        core.positionCount = 2;
        core.alignment = LineAlignment.View;
        core.numCapVertices = 4;
        core.shadowCastingMode = ShadowCastingMode.Off;
        core.receiveShadows = false;
        coreColor = Color.Lerp(CombatPresentation.MagicColor(stats.magic), Color.white, .7f);
        coreColor.a = 1;
        gameObject.SetActive(false);
    }

    public void Show(Vector3 origin, Vector3 end, float seconds, float width)
    {
        started = Time.unscaledTime; duration = Mathf.Max(.05f, seconds);
        authoredBeam.SetEndpoints(origin, end);
        core.SetPosition(0, origin); core.SetPosition(1, end);
        core.startWidth = Mathf.Max(.01f, width); core.endWidth = core.startWidth * .6f;
        core.startColor = core.endColor = coreColor;
        gameObject.SetActive(true);
    }

    private void Update()
    {
        float progress = (Time.unscaledTime - started) / duration;
        if (progress >= 1) { gameObject.SetActive(false); return; }
        Color color = coreColor;
        color.a *= 1 - Mathf.InverseLerp(.4f, 1f, progress);
        core.startColor = core.endColor = color;
    }

    private void OnDestroy()
    {
        // Return any nested authored VFX lease before destroying its wrapper.
        if (authoredBeam != null) MagicVfxPool.Release(authoredBeam.gameObject);
        if (coreMaterial != null) Destroy(coreMaterial);
    }
}
