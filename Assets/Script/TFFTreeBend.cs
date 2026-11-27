using UnityEngine;

// Marker/settings shared by the trunk, canopy and all LODs. No bones or collider changes.
public sealed class TFFTreeBend : MonoBehaviour
{
    [Range(0f,.08f), Tooltip("나무 높이에 대한 꼭대기 이동량 비율")] public float bendAmount = .055f;
    [Range(.1f,3f)] public float swayFrequency = 1.1f;
    [Range(0f,.3f)] public float fixedRootFraction = .08f;
    internal Bounds WindBounds { get; private set; }
    internal float Height { get; private set; }
    internal float Phase => transform.position.x * .17f + transform.position.z * .23f;
    internal static bool IsStandingTree(string name)
    {
        if (name.Contains("_LOD")) return false;
        return name.StartsWith("TFF_Oak_Tree_") || name.StartsWith("TFF_Pine_Tree_") ||
            name.StartsWith("TFF_Birch_Tree_") || name.StartsWith("TFF_Aspen_Tree_") || name.StartsWith("TFF_Dead_Tree_");
    }
    internal void Prepare()
    {
        var renderers = GetComponentsInChildren<MeshRenderer>(true);
        WindBounds = new Bounds(transform.position, Vector3.zero);
        float top = 0;
        foreach (var renderer in renderers)
        {
            var bounds = renderer.bounds;
            var combined = WindBounds; combined.Encapsulate(bounds); WindBounds = combined;
            for (int c=0;c<8;c++)
            {
                var corner = bounds.center + Vector3.Scale(bounds.extents,new Vector3((c&1)==0?-1:1,(c&2)==0?-1:1,(c&4)==0?-1:1));
                top = Mathf.Max(top,Vector3.Dot(corner-transform.position,transform.up));
            }
        }
        Height = Mathf.Max(.1f,top);
    }
}
