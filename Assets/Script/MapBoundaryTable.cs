using UnityEngine;

[CreateAssetMenu(fileName = "MapBoundaryTable", menuName = "WitchFlight/Map/Boundary Table")]
public sealed class MapBoundaryTable : ScriptableObject
{
    [Header("World-space X/Z bounds")]
    public bool boundaryEnabled = true;
    public float xmin = -200f;
    public float xmax = 200f;
    public float zmin = -200f;
    public float zmax = 200f;

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
        return position.x >= xmin && position.x <= xmax && position.z >= zmin && position.z <= zmax;
    }

    public Vector3 ClampInside(Vector3 position)
    {
        float xInset = Mathf.Clamp(returnInset, 0.1f, (xmax - xmin) * 0.45f);
        float zInset = Mathf.Clamp(returnInset, 0.1f, (zmax - zmin) * 0.45f);
        return new Vector3(Mathf.Clamp(position.x, xmin + xInset, xmax - xInset), position.y,
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
