using UnityEngine;

[CreateAssetMenu(fileName = "MapBoundaryTable", menuName = "WitchFlight/Map/Boundary Table")]
public sealed class MapBoundaryTable : ScriptableObject
{
    [Header("Bot flight box (world space, baked from map geometry)")]
    public bool botFlightBoxEnabled;
    public Bounds botFlightBox;
    [Min(0.1f)] public float botBoundaryMargin = 5f;

    public bool TryGetBotFlightBounds(out Bounds bounds)
    {
        bool box = botFlightBoxEnabled && botFlightBox.size.x > .2f &&
            botFlightBox.size.y > .2f && botFlightBox.size.z > .2f;
        Vector3 min = box ? botFlightBox.min : new Vector3(xmin, ymin, zmin);
        Vector3 max = box ? botFlightBox.max : new Vector3(xmax, ymax, zmax);
        if (HasValidBounds)
        {
            min.x = Mathf.Max(min.x, xmin); max.x = Mathf.Min(max.x, xmax);
            min.z = Mathf.Max(min.z, zmin); max.z = Mathf.Min(max.z, zmax);
        }
        // Bots stay below blackout, not merely below the lethal ceiling.
        if (HasValidLowerLimit) min.y = Mathf.Max(min.y, ymin);
        if (HasValidAltitudeLimit) max.y = Mathf.Min(max.y, altitudeFadeStartY);
        bounds = default;
        if (!IsFinite(min.x) || !IsFinite(min.y) || !IsFinite(min.z) ||
            !IsFinite(max.x) || !IsFinite(max.y) || !IsFinite(max.z) ||
            max.x <= min.x || max.y <= min.y || max.z <= min.z) return false;
        Vector3 inset = Vector3.Min(Vector3.one * Mathf.Max(.1f, botBoundaryMargin), (max - min) * .2f);
        bounds.SetMinMax(min + inset, max - inset);
        return true;
    }

    public Vector3 ClampBotPosition(Vector3 position) =>
        TryGetBotFlightBounds(out var bounds) ? bounds.ClosestPoint(position) : ClampToLowerLimit(position);

    public Vector3 BotRetreatDestination(Vector3 position, Vector3 enemy, Vector3 forward, float distance)
    {
        Vector3 away = Vector3.ProjectOnPlane(position - enemy, Vector3.up).normalized;
        if (away.sqrMagnitude < .01f) away = Vector3.ProjectOnPlane(-forward, Vector3.up).normalized;
        if (away.sqrMagnitude < .01f) away = Vector3.forward;
        Vector3 best = ClampBotPosition(position);
        float bestScore = float.NegativeInfinity;
        for (int i = 0; i < 16; i++)
        {
            Vector3 target = ClampBotPosition(position + Quaternion.AngleAxis(i * 22.5f, Vector3.up) * away * distance);
            Vector3 delta = target - position;
            // Prefer usable lateral travel over remaining pinned against an edge.
            if (delta.sqrMagnitude < 4f) continue;
            float score = Vector3.Distance(target, enemy) + Mathf.Min(delta.magnitude, distance) * .35f +
                Vector3.Dot(delta.normalized, forward) * 3f;
            if (score > bestScore) { bestScore = score; best = target; }
        }
        return best;
    }

    [Header("World-space X/Z bounds")]
    public bool boundaryEnabled = true;
    public float xmin = -200f;
    public float xmax = 200f;
    public float zmin = -200f;
    public float zmax = 200f;

    [Header("Lower boundary / solid floor (world Y)")]
    public bool lowerLimitEnabled = false;
    [Tooltip("플레이어 위치가 이 월드 Y 좌표 아래로 내려가지 못하도록 막습니다. 자동 복귀는 실행하지 않습니다.")]
    public float ymin = -50f;
    public bool HasValidLowerLimit => IsFinite(ymin) &&
        (!altitudeLimitEnabled || !HasValidAltitudeLimit || ymin < altitudeFadeStartY);

    [Header("Altitude blackout / death (world Y)")]
    public bool altitudeLimitEnabled = true;
    public float ymax = 100f;
    [Tooltip("이 높이부터 화면 주변이 점차 어두워집니다. ymax보다 낮아야 합니다.")]
    public float altitudeFadeStartY = 80f;
    [Tooltip("고도 초과 사망으로 떨어진 깃발의 하강 속도(m/s)입니다.")]
    [Min(0.1f)] public float flagFallSpeed = 5f;

    public bool HasValidAltitudeLimit => IsFinite(ymax) && IsFinite(altitudeFadeStartY) && altitudeFadeStartY < ymax;

    public float GetAltitudeDarkness(float y)
    {
        return altitudeLimitEnabled && HasValidAltitudeLimit
            ? Mathf.InverseLerp(altitudeFadeStartY, ymax, y) : 0f;
    }

    public bool ExceedsAltitudeLimit(float y) => altitudeLimitEnabled && HasValidAltitudeLimit && y > ymax;

    [Header("Automatic U-turn")]
    [Min(0.25f)] public float returnDuration = 3f;
    [Min(0f)] public float climbHeight = 12f;
    [Min(1f)] public float turnRadius = 12f;
    [Min(0.1f)] public float returnInset = 5f;

    public bool HasValidBounds => IsFinite(xmin) && IsFinite(xmax) &&
        IsFinite(zmin) && IsFinite(zmax) && xmax - xmin > 0.2f && zmax - zmin > 0.2f;

    public bool Contains(Vector3 position)
    {
        // Only horizontal edges trigger the automatic U-turn.
        return position.x >= xmin && position.x <= xmax && position.z >= zmin && position.z <= zmax;
    }

    public Vector3 ClampToLowerLimit(Vector3 position)
    {
        if (boundaryEnabled && lowerLimitEnabled && HasValidLowerLimit)
            position.y = Mathf.Max(position.y, ymin);
        return position;
    }

    public Vector3 ConstrainMovement(Vector3 position, Vector3 movement)
    {
        return ClampToLowerLimit(position + movement) - position;
    }

    public Vector3 ClampInside(Vector3 position)
    {
        float xInset = Mathf.Clamp(returnInset, 0.1f, (xmax - xmin) * 0.45f);
        float zInset = Mathf.Clamp(returnInset, 0.1f, (zmax - zmin) * 0.45f);
        float y = ClampToLowerLimit(position).y;
        return new Vector3(Mathf.Clamp(position.x, xmin + xInset, xmax - xInset), y,
            Mathf.Clamp(position.z, zmin + zInset, zmax - zInset));
    }

    // A horizontal Bezier U-turn, with an independent smooth rise/fall.
    // Endpoint tangents are entryDirection and -entryDirection, respectively.
    public void CreateReturnPath(Vector3 start, Vector3 entryDirection,
        out Vector3 control1, out Vector3 control2, out Vector3 end)
    {
        Vector3 side = Vector3.Cross(Vector3.up, entryDirection);
        Vector3 center = new Vector3((xmin + xmax) * 0.5f, start.y, (zmin + zmax) * 0.5f);
        if (Vector3.Dot(side, center - start) < 0f)
            side = -side;
        float radius = Mathf.Max(1f, turnRadius);
        end = ClampInside(start - entryDirection * (radius * 2f) + side * (radius * 2f));
        control1 = start + entryDirection * radius;
        control2 = end + entryDirection * radius;
    }

    public static void EvaluateReturnPath(Vector3 start, Vector3 control1, Vector3 control2,
        Vector3 end, float height, float progress, out Vector3 position, out Vector3 tangent)
    {
        float t = Mathf.Clamp01(progress);
        float u = 1f - t;
        position = u * u * u * start + 3f * u * u * t * control1 +
                   3f * u * t * t * control2 + t * t * t * end;
        tangent = 3f * u * u * (control1 - start) + 6f * u * t * (control2 - control1) +
                  3f * t * t * (end - control2);
        float rise = Mathf.Sin(Mathf.PI * t);
        position.y += Mathf.Max(0f, height) * rise * rise;
        tangent.y += Mathf.Max(0f, height) * Mathf.PI * Mathf.Sin(2f * Mathf.PI * t);
    }

    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
