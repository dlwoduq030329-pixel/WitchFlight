using UnityEngine;

// Lives under the VS presentation mesh root; releasing that stage releases the cat too.
public sealed class CatIntroFloat : MonoBehaviour
{
    private Vector3 rest;
    private float phase;
    private Camera framingCamera;
    private Renderer[] catRenderers;
    private Bounds catLocalBounds;
    private bool hasCatBounds;
    public void Initialize(Vector3 position, float startPhase) { rest = position; phase = startPhase; }
    public void PlaceBeside(Camera camera, Renderer[] characterRenderers, int side, float gap)
    {
        if (camera == null) return;
        Vector3 axis = camera.transform.right * (side < 0 ? -1f : 1f);
        float bodyEdge = float.NegativeInfinity, catEdge = float.PositiveInfinity;
        foreach (var renderer in characterRenderers)
        {
            if (renderer == null || !renderer.enabled || renderer.GetComponentInParent<CatAvatar>() != null) continue;
            var b = CatSeatGeometry.TryBounds(renderer.transform,Vector3.zero,Quaternion.identity,out Bounds tight) ? tight : renderer.bounds;
            bodyEdge = Mathf.Max(bodyEdge, Vector3.Dot(b.center, axis) + ProjectExtent(b.extents, axis));
        }
        catRenderers = GetComponentsInChildren<Renderer>();
        hasCatBounds = CatSeatGeometry.TryBounds(transform,out catLocalBounds);
        Bounds catBounds = CatBounds();
        catEdge = Vector3.Dot(catBounds.center,axis) - ProjectExtent(catBounds.extents,axis);
        if (float.IsInfinity(bodyEdge) || float.IsInfinity(catEdge)) return;
        // Reserve room for the bob and small roll as well as the resting bounds.
        float offset = bodyEdge + Mathf.Max(0f, gap) + .03f - catEdge;
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
        {
            Bounds b = CatBounds();
            Vector3 center = camera.transform.InverseTransformPoint(b.center);
            // Include the entire plane, hat and paws, plus room for bobbing and roll.
            float width = Mathf.Abs(center.x) + ProjectExtent(b.extents, camera.transform.right) + .05f;
            float height = Mathf.Abs(center.y) + ProjectExtent(b.extents, camera.transform.up) + .05f;
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

    private Bounds CatBounds()
    {
        if (!hasCatBounds)
        {
            Bounds fallback = new Bounds(transform.position,Vector3.zero);
            foreach (var r in catRenderers) if(r!=null && r.enabled) fallback.Encapsulate(r.bounds);
            return fallback;
        }
        Bounds world = new Bounds(transform.TransformPoint(catLocalBounds.center),Vector3.zero);
        for(int i=0;i<8;i++) world.Encapsulate(transform.TransformPoint(catLocalBounds.center + Vector3.Scale(catLocalBounds.extents,
            new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1))));
        return world;
    }

    public void FrameWithFocus(Camera camera,Vector3 focus,Vector2 halfSize)
    {
        Vector3 center = camera.transform.InverseTransformPoint(focus);
        Bounds pair = new Bounds(center,new Vector3(halfSize.x*2,halfSize.y*2,.1f));
        Bounds cat = CatBounds();
        Vector3 c = camera.transform.InverseTransformPoint(cat.center);
        Vector3 e = new Vector3(ProjectExtent(cat.extents,camera.transform.right),ProjectExtent(cat.extents,camera.transform.up),0);
        pair.Encapsulate(c-e);pair.Encapsulate(c+e);
        ApplyFrame(camera,pair);
    }
    public static void FrameWholeGroup(Transform root,Camera camera)
    {
        // Use the posed mesh, not the deliberately padded SkinnedMeshRenderer culling box.
        if(CatSeatGeometry.TryBounds(root,camera.transform.position,camera.transform.rotation,out Bounds bounds)) ApplyFrame(camera,bounds);
    }
    private static void ApplyFrame(Camera camera,Bounds bounds)
    {
        camera.transform.position += camera.transform.right*bounds.center.x + camera.transform.up*bounds.center.y;
        camera.orthographicSize = Mathf.Max(.1f,Mathf.Max(bounds.extents.y+.1f,(bounds.extents.x+.065f)/Mathf.Max(.1f,camera.aspect)));
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
