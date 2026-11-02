using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class RaceRing : MonoBehaviour
{
    [SerializeField, Min(1)] private int index = 1;
    [SerializeField] private Outline outline;
    [SerializeField] private Transform halo;
    [SerializeField] private Material fadedMaterial;
    [SerializeField, Range(.1f, .95f)] private float openingRatio = .72f;
    [SerializeField] private Color targetColor = new Color(.05f, .5f, 1f, 1f);
    private Renderer[] surfaces;
    private Material[][] originalMaterials, fadedMaterials;
    private Vector3 center;
    private Quaternion planeRotation;
    private Vector2 radii;
    private bool prepared, visualPrepared;
    public int Index => index;
    public Vector3 Center { get { Prepare(); return transform.TransformPoint(center); } }
    public Vector3 Normal { get { Prepare(); return transform.TransformDirection(planeRotation * Vector3.forward).normalized; } }
    public Vector3 Right { get { Prepare(); return transform.TransformDirection(planeRotation * Vector3.right).normalized; } }
    public float Radius { get { Prepare(); return Mathf.Min(transform.TransformVector(planeRotation * Vector3.right * radii.x).magnitude, transform.TransformVector(planeRotation * Vector3.up * radii.y).magnitude); } }
    public void SetIndex(int value) => index = value;

    private void Awake() => Prepare();
    private void Prepare()
    {
        if (prepared && (!Application.isPlaying || visualPrepared)) return;
        prepared = true;
        var list = new List<Renderer>();
        Bounds bounds = default;
        bool found = false;
        foreach (MeshFilter mesh in GetComponentsInChildren<MeshFilter>(true))
        {
            if (mesh.sharedMesh == null || mesh.GetComponentInParent<RaceRing>() != this ||
                (halo != null && mesh.transform.IsChildOf(halo))) continue;
            if (!mesh.TryGetComponent(out Renderer surface)) continue;
            list.Add(surface);
            Bounds local = mesh.sharedMesh.bounds;
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 sign = new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1);
                Vector3 point = transform.InverseTransformPoint(mesh.transform.TransformPoint(local.center + Vector3.Scale(local.extents, sign)));
                if (!found) { bounds = new Bounds(point, Vector3.zero); found = true; }
                else bounds.Encapsulate(point);
            }
        }
        center = bounds.center;
        Vector3 size = bounds.extents;
        Vector3 normal = size.x <= size.y && size.x <= size.z ? Vector3.right : size.y <= size.z ? Vector3.up : Vector3.forward;
        planeRotation = Quaternion.FromToRotation(Vector3.forward, normal);
        Vector3 right = planeRotation * Vector3.right, up = planeRotation * Vector3.up;
        Vector2 outer = new Vector2(Vector3.Dot(Abs(right), size), Vector3.Dot(Abs(up), size));
        radii = outer * openingRatio;
        if (!Application.isPlaying) return;
        if (halo != null)
        {
            halo.localPosition = center;
            halo.localRotation = planeRotation;
            halo.localScale = new Vector3(outer.x * 2.6f, outer.y * 2.6f, 1);
            halo.gameObject.SetActive(false);
        }
        surfaces = list.ToArray();
        originalMaterials = new Material[surfaces.Length][];
        fadedMaterials = new Material[surfaces.Length][];
        for (int i = 0; i < surfaces.Length; i++)
        {
            originalMaterials[i] = surfaces[i].sharedMaterials;
            fadedMaterials[i] = new Material[originalMaterials[i].Length];
            for (int j = 0; j < fadedMaterials[i].Length; j++) fadedMaterials[i][j] = fadedMaterial;
        }
        visualPrepared = true;
        if (outline != null) outline.enabled = false;
    }

    private static Vector3 Abs(Vector3 value) => new Vector3(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));

    public void SetTarget(bool active)
    {
        Prepare();
        // Disable before replacing materials, so Outline never caches its own appended passes.
        if (outline != null) outline.enabled = false;
        for (int i = 0; i < surfaces.Length; i++)
            if (surfaces[i] != null) surfaces[i].sharedMaterials = active || fadedMaterial == null ? originalMaterials[i] : fadedMaterials[i];
        if (outline != null)
        {
            outline.OutlineColor = targetColor;
            outline.OutlineMode = Outline.Mode.OutlineVisible;
            outline.OutlineWidth = 5f;
            outline.enabled = active;
        }
        if (halo != null) halo.gameObject.SetActive(active);
    }

    public bool Crossed(Vector3 from, Vector3 to, out float fraction)
    {
        Prepare();
        Quaternion inverse = Quaternion.Inverse(planeRotation);
        Vector3 a = inverse * (transform.InverseTransformPoint(from) - center);
        Vector3 b = inverse * (transform.InverseTransformPoint(to) - center);
        fraction = 0f;
        if (radii.x <= .001f || radii.y <= .001f || Mathf.Abs(b.z - a.z) < .00001f || a.z * b.z > 0f) return false;
        fraction = -a.z / (b.z - a.z);
        if (fraction <= .00001f || fraction > 1f) return false;
        Vector3 point = Vector3.Lerp(a, b, fraction);
        return point.x * point.x / (radii.x * radii.x) + point.y * point.y / (radii.y * radii.y) <= 1f;
    }

    private void OnDrawGizmosSelected()
    {
        if (!Application.isPlaying) prepared = false;
        // Geometry-only preview must not replace the renderer's authored materials.
        Prepare();
        Gizmos.color = Color.cyan;
        Matrix4x4 previous = Gizmos.matrix;
        Gizmos.matrix = transform.localToWorldMatrix * Matrix4x4.TRS(center, planeRotation, Vector3.one);
        for (int i = 0; i < 48; i++)
        {
            float a = i * Mathf.PI * 2f / 48, b = (i + 1) * Mathf.PI * 2f / 48;
            Gizmos.DrawLine(new Vector3(Mathf.Cos(a) * radii.x, Mathf.Sin(a) * radii.y, 0), new Vector3(Mathf.Cos(b) * radii.x, Mathf.Sin(b) * radii.y, 0));
        }
        Gizmos.matrix = previous;
    }
}
