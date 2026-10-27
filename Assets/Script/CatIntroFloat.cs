using UnityEngine;

// Lives under the VS presentation mesh root; releasing that stage releases the cat too.
public sealed class CatIntroFloat : MonoBehaviour
{
    private Vector3 rest;
    private float phase;
    private Camera framingCamera;
    private Renderer[] catRenderers;
    public void Initialize(Vector3 position, float startPhase) { rest = position; phase = startPhase; }
    public void PlaceBeside(Camera camera, Renderer[] characterRenderers, int side, float gap)
    {
        if (camera == null) return;
        Vector3 axis = camera.transform.right * (side < 0 ? -1f : 1f);
        float bodyEdge = float.NegativeInfinity, catEdge = float.PositiveInfinity;
        foreach (var renderer in characterRenderers)
        {
            if (renderer == null || !renderer.enabled || renderer.GetComponentInParent<CatAvatar>() != null) continue;
            var b = renderer.bounds;
            bodyEdge = Mathf.Max(bodyEdge, Vector3.Dot(b.center, axis) + ProjectExtent(b.extents, axis));
        }
        catRenderers = GetComponentsInChildren<Renderer>();
        foreach (var renderer in catRenderers)
        {
            if (!renderer.enabled) continue;
            var b = renderer.bounds;
            catEdge = Mathf.Min(catEdge, Vector3.Dot(b.center, axis) - ProjectExtent(b.extents, axis));
        }
        if (float.IsInfinity(bodyEdge) || float.IsInfinity(catEdge)) return;
        // Reserve room for the bob and small roll as well as the resting bounds.
        float offset = bodyEdge + Mathf.Max(0f, gap) + .12f - catEdge;
        transform.position += axis * offset;
        rest = transform.localPosition;
        framingCamera = camera;
        KeepInsideFrame();
    }

    private void KeepInsideFrame()
    {
        if (framingCamera == null || !framingCamera.orthographic) return;
        var camera = framingCamera;
        float halfHeight = camera.orthographicSize;
        float nearest = float.PositiveInfinity, farthest = 0f;
        foreach (var renderer in catRenderers)
        {
            if (renderer == null || !renderer.enabled) continue;
            Bounds b = renderer.bounds;
            Vector3 center = camera.transform.InverseTransformPoint(b.center);
            // Include the entire plane, hat and paws, plus room for bobbing and roll.
            float width = Mathf.Abs(center.x) + ProjectExtent(b.extents, camera.transform.right) + .12f;
            float height = Mathf.Abs(center.y) + ProjectExtent(b.extents, camera.transform.up) + .12f;
            halfHeight = Mathf.Max(halfHeight, Mathf.Max(height, width / Mathf.Max(.1f, camera.aspect)));
            float depth = ProjectExtent(b.extents, camera.transform.forward);
            nearest = Mathf.Min(nearest, center.z - depth);
            farthest = Mathf.Max(farthest, center.z + depth);
        }
        camera.orthographicSize = halfHeight;
        float retreat = Mathf.Max(0f, camera.nearClipPlane + .1f - nearest);
        camera.transform.position -= camera.transform.forward * retreat;
        camera.farClipPlane = Mathf.Max(camera.farClipPlane, farthest + retreat + .2f);
    }

    private static float ProjectExtent(Vector3 extent, Vector3 axis) =>
        Mathf.Abs(axis.x) * extent.x + Mathf.Abs(axis.y) * extent.y + Mathf.Abs(axis.z) * extent.z;
    private void Update()
    {
        var table = CatCompanionTable.Default;
        float t = Time.unscaledTime * .8f + phase;
        transform.localPosition = rest + new Vector3(Mathf.Sin(t) * .035f, Mathf.Sin(t * 1.4f) * table.bobHeight, 0);
        transform.localRotation = Quaternion.Euler(0, Mathf.Sin(t) * 8, Mathf.Cos(t) * 3);
    }
    private void LateUpdate() => KeepInsideFrame();
}
