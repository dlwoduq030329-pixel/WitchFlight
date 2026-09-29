using UnityEngine;
using UnityEngine.UI;

// Lightweight UI geometry: no textures, particles, custom shaders or network objects.
[AddComponentMenu("UI/WitchFlight Speed Wind Lines")]
public sealed class SpeedWindLines : MaskableGraphic
{
    private float opacity;
    private float elapsed;
    private float travelSpeed;
    private int lineCount;
    private uint animationCycle;
    private float minWidth = 1f;
    private float maxWidth = 6f;

    protected override void Awake()
    {
        base.Awake();
        raycastTarget = false;
    }

    public void SetPresentation(float strength, Color tint, int count, float speed,
        float minimumWidth = 1f, float maximumWidth = 6f)
    {
        opacity = Mathf.Clamp01(strength);
        color = tint;
        lineCount = Mathf.Clamp(count, 8, 96);
        travelSpeed = Mathf.Max(0.01f, speed);
        minWidth = Mathf.Max(0.1f, Mathf.Min(minimumWidth, maximumWidth));
        maxWidth = Mathf.Max(minWidth, Mathf.Max(minimumWidth, maximumWidth));
        bool visible = opacity > 0.001f;
        if (enabled && !visible)
            animationCycle = unchecked(animationCycle + 1u);
        if (enabled != visible)
            enabled = visible;
        if (visible)
        {
            float advanced = elapsed + Time.unscaledDeltaTime * travelSpeed;
            animationCycle = unchecked(animationCycle + (uint)Mathf.FloorToInt(advanced));
            elapsed = Mathf.Repeat(advanced, 1f);
            SetVerticesDirty();
        }
        else
            elapsed = 0f;
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        if (opacity <= 0.001f)
            return;
        Rect rect = rectTransform.rect;
        Vector2 halfSize = rect.size * 0.5f;
        if (halfSize.x <= 0f || halfSize.y <= 0f)
            return;

        for (int i = 0; i < lineCount; i++)
        {
            // Deterministic spacing/phases without touching UnityEngine.Random.
            float angle = (i + 0.5f) * 2.39996323f;
            Vector2 direction = new(Mathf.Cos(angle), Mathf.Sin(angle));
            Vector2 radial = Vector2.Scale(direction, halfSize);
            float edge = 1f / Mathf.Max(Mathf.Abs(direction.x), Mathf.Abs(direction.y));
            float phaseTime = elapsed + Mathf.Repeat(i * 0.61803399f, 1f);
            float phase = Mathf.Repeat(phaseTime, 1f);
            uint streakCycle = unchecked(animationCycle + (uint)Mathf.FloorToInt(phaseTime));
            // A new random width per streak lifetime, never per mesh rebuild/frame.
            float width = Mathf.Lerp(minWidth, maxWidth, WidthSample(i, streakCycle));
            float headRadius = Mathf.Lerp(0.82f, edge + 0.35f, phase);
            float tailRadius = Mathf.Max(0.76f, headRadius - 0.22f);
            Vector2 tail = rect.center + radial * tailRadius;
            Vector2 head = rect.center + radial * headRadius;
            Vector2 side = new Vector2(-radial.y, radial.x).normalized *
                (rect.height / 1080f) * width * 0.5f;
            Color tipColor = color;
            tipColor.a *= opacity * Mathf.Sin(phase * Mathf.PI);
            Color tailColor = tipColor;
            tailColor.a = 0f;

            int start = vh.currentVertCount;
            vh.AddVert(tail - side, tailColor, Vector2.zero);
            vh.AddVert(tail + side, tailColor, Vector2.zero);
            vh.AddVert(head + side, tipColor, Vector2.zero);
            vh.AddVert(head - side, tipColor, Vector2.zero);
            vh.AddTriangle(start, start + 1, start + 2);
            vh.AddTriangle(start, start + 2, start + 3);
        }
    }

    private static float WidthSample(int index, uint cycle)
    {
        // Stateless cosmetic randomness; does not consume gameplay's Random state.
        unchecked
        {
            uint value = (uint)(index + 1) * 0x9e3779b9u ^ (cycle + 1u) * 0x85ebca6bu;
            value = (value ^ (value >> 16)) * 0x7feb352du;
            value = (value ^ (value >> 15)) * 0x846ca68bu;
            value ^= value >> 16;
            return (value & 0x00ffffffu) / 16777215f;
        }
    }
}
