using UnityEngine;
using UnityEngine.UI;

// Lightweight UI geometry: no textures, particles, custom shaders or network objects.
[AddComponentMenu("UI/WitchFlight Speed Wind Lines")]
[RequireComponent(typeof(CanvasRenderer))]
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
            float headRadius = Mathf.Lerp(0.78f, edge + 0.45f, phase);
            float length = Mathf.Lerp(.38f, .72f, WidthSample(i + 137, streakCycle));
            float tailRadius = Mathf.Max(.54f, headRadius - length);
            Vector2 tail = rect.center + radial * tailRadius;
            Vector2 head = rect.center + radial * headRadius;
            Vector2 side = new Vector2(-radial.y, radial.x).normalized *
                (rect.height / 1080f) * width * 1.8f;
            Color streak = Color.white;
            streak.a = color.a * opacity * Mathf.Sin(phase * Mathf.PI);
            // Feather both sides and taper towards the clear centre. No texture,
            // extra material, or per-frame managed allocations are required.
            int start = vh.currentVertCount;
            for (int row = 0; row < 4; row++)
            {
                float t = row / 3f;
                Vector2 point = Vector2.Lerp(tail, head, t);
                float spread = Mathf.Lerp(.02f, 1f, t * t);
                for (int column = 0; column < 5; column++)
                {
                    float across = (column - 2) * .5f;
                    Color c = streak;
                    c.a *= (column == 0 || column == 4 ? 0f : column == 2 ? 1f : .28f) * t;
                    vh.AddVert(point + side * (across * spread), c, Vector2.zero);
                }
            }
            for (int row = 0; row < 3; row++)
                for (int column = 0; column < 4; column++)
                {
                    int a = start + row * 5 + column;
                    vh.AddTriangle(a, a + 1, a + 6);
                    vh.AddTriangle(a, a + 6, a + 5);
                }
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
