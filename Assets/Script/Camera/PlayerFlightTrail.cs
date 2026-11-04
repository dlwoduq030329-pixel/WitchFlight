using System;
using UnityEngine;
using UnityEngine.Rendering;

// One reusable, viewer-local ribbon. Positions remain in world space when the camera turns.
[Serializable]
public sealed class PlayerFlightTrail : IDisposable
{
    public bool enabled = true;
    [Tooltip("선택 사항. 비워두면 로컬 플레이어 루트를 기준으로 Local Offset을 적용합니다.")]
    public Transform attachment;
    public Vector3 localOffset = new Vector3(0f, .65f, -.9f);
    public Color color = new Color(.55f, .9f, 1f, .5f);
    [Min(.01f)] public float width = .12f;
    [Min(.02f)] public float lifetime = .28f;
    [Min(.1f)] public float maximumLength = 6f;
    [Range(0f, 1f)] public float minimumSpeedRatio = .75f;
    [Tooltip("비워두면 부드러운 청백색 궤적용 기본 머티리얼을 사용합니다.")]
    public Material material;

    [NonSerialized] private TrailRenderer trail;
    [NonSerialized] private Material generatedMaterial;
    [NonSerialized] private Player owner;
    [NonSerialized] private int portal, frame = -1;
    [NonSerialized] private bool hasPosition, drawing;
    [NonSerialized] private Vector3 previousPosition, previousPoint;
    [NonSerialized] private Color appliedColor;

    internal static bool ShouldDraw(int stage, float ratio, float minimum) =>
        stage == 3 && ratio >= Mathf.Clamp01(minimum) && ratio > .01f;

    public void Update(Player player, bool allowed)
    {
        if (frame == Time.frameCount) return;
        bool gap = frame != Time.frameCount - 1;
        frame = Time.frameCount;
        if (!enabled || !allowed || player == null)
        {
            Clear(); owner = null; return;
        }
        Vector3 position = player.transform.position;
        bool reset = gap || !hasPosition || owner != player || portal != player.PortalExitSequence ||
            (position - previousPosition).sqrMagnitude >= 144f;
        if (reset) Clear();
        float speed = reset ? 0f : (position - previousPosition).magnitude / Mathf.Max(.0001f, Time.unscaledDeltaTime);
        previousPosition = position; hasPosition = true; owner = player; portal = player.PortalExitSequence;
        if (reset && trail != null) RearThreatCamera.ExcludeLocalEffect(trail.gameObject, player);
        bool show = ShouldDraw(player.CurrentSpeedStage, player.ForwardCruiseSpeedRatio, minimumSpeedRatio);
        if (!show) { drawing = false; return; } // existing points fade using their lifetime
        if (trail == null) Create(player);
        if (trail == null) return;
        trail.time = Mathf.Min(Mathf.Max(.02f, lifetime), Mathf.Max(.1f, maximumLength) / Mathf.Max(1f, speed));
        trail.widthMultiplier = Mathf.Max(.01f, width);
        trail.sharedMaterial = material != null ? material : generatedMaterial;
        if (appliedColor != color) SetColor();
        Transform source = attachment != null ? attachment : player.transform;
        Vector3 point = source.TransformPoint(localOffset);
        trail.transform.position = point;
        if (!drawing) { trail.Clear(); previousPoint = point; trail.AddPosition(point); }
        else if ((point - previousPoint).sqrMagnitude >= .0144f)
        {
            trail.AddPosition(point);
            previousPoint = point;
        }
        drawing = true;
    }

    private void Create(Player player)
    {
        Shader shader = Resources.Load<Shader>("PlayerFlightTrail");
        if (material == null && shader == null) return;
        var root = new GameObject("Player flight trail (local)") { hideFlags = HideFlags.DontSave, layer = player.gameObject.layer };
        // Deliberately outside the player hierarchy: never included in its blur mask or model bounds.
        trail = root.AddComponent<TrailRenderer>();
        trail.emitting = false; // explicit post-interpolation samples; no camera-orbit / automatic extra points
        trail.autodestruct = false;
        trail.minVertexDistance = .12f;
        trail.alignment = LineAlignment.View;
        trail.textureMode = LineTextureMode.Stretch;
        trail.numCornerVertices = 3; trail.numCapVertices = 2;
        trail.shadowCastingMode = ShadowCastingMode.Off;
        trail.receiveShadows = false;
        trail.lightProbeUsage = LightProbeUsage.Off;
        trail.reflectionProbeUsage = ReflectionProbeUsage.Off;
        trail.widthCurve = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);
        if (shader != null) generatedMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        SetColor();
        RearThreatCamera.ExcludeLocalEffect(root, player);
    }

    private void SetColor()
    {
        appliedColor = color;
        var gradient = new Gradient();
        gradient.SetKeys(new[] { new GradientColorKey(color, 0), new GradientColorKey(color, 1) },
            new[] { new GradientAlphaKey(color.a, 0), new GradientAlphaKey(color.a * .6f, .45f), new GradientAlphaKey(0, 1) });
        trail.colorGradient = gradient;
    }

    private void Clear()
    {
        if (trail != null) trail.Clear();
        drawing = false; hasPosition = false;
    }

    public void Dispose()
    {
        if (trail != null)
        {
            RearThreatCamera.ExcludeLocalEffect(trail.gameObject, null);
            Release(trail.gameObject);
        }
        if (generatedMaterial != null) Release(generatedMaterial);
        trail = null; generatedMaterial = null; owner = null; frame = -1; Clear();
    }

    private static void Release(UnityEngine.Object instance)
    {
        if (Application.isPlaying) UnityEngine.Object.Destroy(instance);
        else UnityEngine.Object.DestroyImmediate(instance);
    }
}
